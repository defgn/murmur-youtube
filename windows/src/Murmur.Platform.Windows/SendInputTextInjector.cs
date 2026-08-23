using System.Runtime.InteropServices;
using System.Threading;
using Murmur.Abstractions;

namespace Murmur.Platform.Windows;

/// <summary>
/// Inserts text into whatever application has focus.
/// </summary>
/// <remarks>
/// <para>
/// <b>UI Automation is not an option here.</b> On macOS the Accessibility API can write text
/// directly; Windows has no counterpart. <c>TextPattern</c> is documented as providing no
/// means to insert or modify text, and <c>ValuePattern</c> replaces an entire field rather
/// than inserting at the caret — and multi-line controls do not support it at all. Microsoft's
/// own sample code says plainly: "text input must be simulated."
/// </para>
/// <para>
/// <b>Paste is the primary path, not typing.</b> Typing leaves the clipboard alone, but it is
/// per-character: <c>SendInput</c> drops or duplicates synthetic keystrokes in Electron-family
/// apps (Discord, Slack, VS Code) and can fail partway through a string. A paste is a single
/// <c>Ctrl+V</c> — the target receives the whole text or none of it, so interleaved or doubled
/// characters are structurally impossible. The clipboard is restored afterwards, so the only
/// cost is a brief, invisible swap. Typing remains as the fallback for when the clipboard is
/// locked by another application.
/// </para>
/// </remarks>
public sealed class SendInputTextInjector : ITextInjector, IDisposable
{
    /// <summary>Characters per <c>SendInput</c> call when typing.</summary>
    private const int ChunkSize = 40;

    /// <summary>Pause between chunks, so slower targets keep up.</summary>
    private static readonly TimeSpan ChunkGap = TimeSpan.FromMilliseconds(4);

    /// <summary>How many times to retry opening the clipboard before giving up.</summary>
    /// <remarks>
    /// <c>OpenClipboard</c> fails while another application holds the clipboard open. That
    /// window is almost always momentary (a copy is a few milliseconds), so a short retry
    /// turns a flaky miss into a reliable paste.
    /// </remarks>
    private const int ClipboardRetries = 3;

    /// <summary>Pause between clipboard retries.</summary>
    private static readonly TimeSpan ClipboardRetryGap = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// Time for the target to observe the new clipboard before Ctrl+V arrives. Without it, a
    /// fast paste can pick up the <i>previous</i> contents.
    /// </summary>
    private static readonly TimeSpan ClipboardSettle = TimeSpan.FromMilliseconds(60);

    /// <summary>Time for the target to finish reading before the clipboard is restored.</summary>
    private static readonly TimeSpan PasteSettle = TimeSpan.FromMilliseconds(500);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_V = 0x56;
    private const int VK_RETURN = 0x0D;

    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParamL, ParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    /// <summary>
    /// Serializes injections so two calls can never interleave.
    /// </summary>
    /// <remarks>
    /// The engine's state machine already refuses a second dictation while one is being
    /// processed, but injection is a shared resource and the cost of defending it twice is a
    /// few lines. If two calls ever did race — a future caller, a button path, a hotkey edge —
    /// the second waits for the first to finish rather than typing into the middle of it.
    /// </remarks>
    private readonly SemaphoreSlim _injectionGate = new(1, 1);

    /// <summary>Called to place text on the clipboard and paste it.</summary>
    /// <remarks>
    /// Injected rather than called directly because clipboard access is UI-framework specific
    /// and must happen on an STA thread — the app supplies an implementation that marshals to
    /// its own dispatcher.
    /// </remarks>
    public Func<string, CancellationToken, Task<bool>>? ClipboardPaste { get; init; }

    /// <inheritdoc />
    public async ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text)) return true;

        await _injectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await InjectCoreAsync(text, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _injectionGate.Release();
        }
    }

    /// <summary>Runs one injection, under the gate.</summary>
    /// <remarks>
    /// Order matters and is load-bearing: paste first, type only when the clipboard is
    /// unusable. Pasting over a partially typed string is exactly how a dictation app produces
    /// the doubled, interleaved garbage that makes it look broken — the type path must never
    /// run after a paste, and a paste must never run after a partial type.
    /// </remarks>
    private async ValueTask<bool> InjectCoreAsync(string text, CancellationToken cancellationToken)
    {
        // Electron-family apps drop synthetic Unicode keystrokes and some controls duplicate
        // them; a paste is atomic and works in every window the user can actually see.
        if (ClipboardPaste is not null)
        {
            if (await ClipboardPaste(text, cancellationToken).ConfigureAwait(false)) return true;

            // The app-supplied paste failed; fall through to the self-contained path.
        }

        if (PasteViaClipboard(text)) return true;

        // Last resort: the clipboard is locked by another application. Type instead. If a
        // chunk only partially sends, stop — the partial text is already in the target and
        // retrying over it would recreate the interleaving this ordering exists to prevent.
        return text.Contains('\n', StringComparison.Ordinal)
            ? TypeWithNewlines(text)
            : TypeUnicode(text);
    }

    /// <summary>Types arbitrary text as Unicode packets.</summary>
    /// <remarks>
    /// <para>
    /// Iterating by UTF-16 code unit is correct and intentional. A non-BMP character — an
    /// emoji, some CJK extensions — is two surrogate halves, and each is sent as its own
    /// event; Unicode-aware controls recombine them. The 16-bit <c>ScanCode</c> field cannot
    /// carry a full 21-bit code point.
    /// </para>
    /// <para>
    /// <b>Failure here is silent.</b> <c>SendInput</c> is subject to UIPI, and Microsoft
    /// documents that when it is blocked "neither GetLastError nor the return value will
    /// indicate the failure was caused by UIPI blocking." Injecting into an elevated window
    /// from a normal process can return success and do nothing. When a chunk only partially
    /// sends, the caller must not paste or retype over it — the partial text is already in
    /// the target, and layering more text on top produces interleaved garbage.
    /// </para>
    /// </remarks>
    private static bool TypeUnicode(string text)
    {
        for (var offset = 0; offset < text.Length; offset += ChunkSize)
        {
            var length = Math.Min(ChunkSize, text.Length - offset);

            // Never split a surrogate pair across two SendInput calls.
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length++;

            var inputs = new INPUT[length * 2];
            for (var i = 0; i < length; i++)
            {
                var unit = text[offset + i];
                inputs[i * 2] = UnicodeInput(unit, up: false);
                inputs[(i * 2) + 1] = UnicodeInput(unit, up: true);
            }

            if (SendInput((uint)inputs.Length, inputs, InputSize) != inputs.Length) return false;
            if (offset + length < text.Length) Thread.Sleep(ChunkGap);
        }

        return true;
    }

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard, presses Ctrl+V, and restores whatever
    /// was there before. The primary injection path, because it is atomic.
    /// </summary>
    /// <remarks>
    /// The clipboard is opened, written, closed, and the paste issued from the calling
    /// thread; Windows clipboard access is per-thread and requires no apartment. The user's
    /// previous clipboard content is saved first and restored after <see cref="PasteSettle"/>
    /// so the app does not destroy whatever they had copied.
    /// </remarks>
    private static bool PasteViaClipboard(string text)
    {
        string? previous = ReadClipboardText();
        if (!SetClipboardTextWithRetry(text)) return false;

        if (!PressCtrlV())
        {
            // Best effort to put back what the user had; failing that, at least the
            // transcript is on the clipboard where it can be pasted by hand.
            if (previous is not null) SetClipboardText(previous);
            return false;
        }

        Thread.Sleep(PasteSettle);
        if (previous is not null) SetClipboardText(previous);
        return true;
    }

    /// <summary>Sets the clipboard, retrying briefly while another app holds it open.</summary>
    private static bool SetClipboardTextWithRetry(string text)
    {
        for (var attempt = 0; attempt < ClipboardRetries; attempt++)
        {
            if (SetClipboardText(text)) return true;
            Thread.Sleep(ClipboardRetryGap * (attempt + 1));
        }

        return false;
    }

    /// <summary>Reads the clipboard as UTF-16 text, or null if none.</summary>
    private static string? ReadClipboardText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return null;

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;

            try
            {
                // NUL-terminated, not sized: GlobalSize returns the allocation size, which
                // the heap rounds up — reading size / 2 chars would swallow garbage after
                // the terminator.
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Replaces the clipboard contents with UTF-16 text.</summary>
    private static bool SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            if (!EmptyClipboard()) return false;

            var bytes = (text.Length + 1) * 2;   // +1 for the NUL terminator
            var memory = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)bytes);
            if (memory == IntPtr.Zero) return false;

            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) return false;

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            return SetClipboardData(CF_UNICODETEXT, memory) != IntPtr.Zero;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Types text, sending a real Return for each newline.</summary>
    private static bool TypeWithNewlines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 0 && !TypeUnicode(lines[i])) return false;

            if (i < lines.Length - 1)
            {
                INPUT[] enter = [KeyInput(VK_RETURN, up: false), KeyInput(VK_RETURN, up: true)];
                if (SendInput(2, enter, InputSize) != 2) return false;
            }
        }

        return true;
    }

    /// <summary>Presses Ctrl+V, temporarily lifting any modifier the user is resting on.</summary>
    /// <remarks>
    /// Microsoft: "This function does not reset the keyboard's current state. Any keys that
    /// are already pressed when the function is called might interfere." A user resting on
    /// Shift would otherwise get Ctrl+Shift+V — "paste as plain text", or something else
    /// entirely depending on the app.
    /// </remarks>
    public static bool PressCtrlV()
    {
        var held = new List<int>();
        foreach (var key in new[] { VK_SHIFT, VK_MENU, VK_LWIN, VK_RWIN })
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0) held.Add(key);
        }

        var sequence = new List<INPUT>();
        foreach (var key in held) sequence.Add(KeyInput(key, up: true));

        sequence.Add(KeyInput(VK_CONTROL, up: false));
        sequence.Add(KeyInput(VK_V, up: false));
        sequence.Add(KeyInput(VK_V, up: true));
        sequence.Add(KeyInput(VK_CONTROL, up: true));

        foreach (var key in held) sequence.Add(KeyInput(key, up: false));

        var inputs = sequence.ToArray();
        return SendInput((uint)inputs.Length, inputs, InputSize) == inputs.Length;
    }

    /// <summary>How long to wait after setting the clipboard before pasting.</summary>
    public static TimeSpan ClipboardSettleDelay => ClipboardSettle;

    /// <summary>How long to wait after pasting before restoring the clipboard.</summary>
    public static TimeSpan PasteSettleDelay => PasteSettle;

    private static INPUT UnicodeInput(ushort codeUnit, bool up) => new()
    {
        Type = INPUT_KEYBOARD,
        Union = new InputUnion
        {
            Keyboard = new KEYBDINPUT
            {
                VirtualKey = 0,   // must be 0 for KEYEVENTF_UNICODE
                ScanCode = codeUnit,
                Flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                Time = 0,
                ExtraInfo = PushToTalkHook.InjectedTag,
            },
        },
    };

    private static INPUT KeyInput(int virtualKey, bool up)
    {
        var flags = up ? KEYEVENTF_KEYUP : 0;
        if (IsExtendedKey(virtualKey)) flags |= KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            Type = INPUT_KEYBOARD,
            Union = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = (ushort)virtualKey,
                    ScanCode = (ushort)MapVirtualKey((uint)virtualKey, MAPVK_VK_TO_VSC),
                    Flags = flags,
                    Time = 0,
                    // Tagged so our own hook ignores it and doesn't re-trigger dictation.
                    ExtraInfo = PushToTalkHook.InjectedTag,
                },
            },
        };
    }

    private static bool IsExtendedKey(int virtualKey) => virtualKey is
        0xA3 or 0xA5 or 0x5B or 0x5C or   // right ctrl/alt, both Windows keys
        0x2D or 0x2E or 0x24 or 0x23 or   // insert, delete, home, end
        0x21 or 0x22 or                   // page up/down
        0x25 or 0x26 or 0x27 or 0x28;     // arrows

    /// <summary>Releases the injection gate.</summary>
    public void Dispose() => _injectionGate.Dispose();
}
