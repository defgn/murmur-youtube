using System.Runtime.InteropServices;
using Murmur.Abstractions;

namespace Murmur.Platform.Windows;

/// <summary>
/// Reads the selected text in the focused application for Command Mode.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the clipboard rather than UI Automation:</b> UIA's <c>TextPattern.GetSelection</c>
/// is read-only supported on only some controls, and pulling in the WindowsDesktop
/// framework just for it would bloat a self-contained publish by a hundred megabytes. Every
/// application the user actually selects text in supports <c>Ctrl+C</c> — browsers,
/// editors, terminals, chat apps.
/// </para>
/// <para>
/// The clipboard is saved first and restored afterwards, exactly like the paste-primary
/// injector — a brief, invisible swap. If the clipboard held non-text, it is replaced
/// (same accepted limitation as the injector).
/// </para>
/// </remarks>
public sealed class ClipboardSelectionReader : ISelectionReader
{
    /// <summary>Time for the focused app to finish a copy before the clipboard is read.</summary>
    private static readonly TimeSpan CopySettle = TimeSpan.FromMilliseconds(300);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const int VK_CONTROL = 0x11;
    private const int VK_C = 0x43;

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;

    /// <inheritdoc />
    public async Task<string?> ReadSelectedTextAsync(CancellationToken cancellationToken)
    {
        var previous = ReadClipboardText();

        if (!PressCtrlC()) return null;

        await Task.Delay(CopySettle, cancellationToken).ConfigureAwait(false);

        var selected = ReadClipboardText();

        // Restore whatever the user had copied. If nothing was selected, the clipboard
        // still holds the previous text, so this is a harmless no-op.
        if (previous is not null) SetClipboardText(previous);

        return string.IsNullOrWhiteSpace(selected) ? null : selected;
    }

    private static bool PressCtrlC()
    {
        var inputs = new[]
        {
            Key(VK_CONTROL, up: false),
            Key(VK_C, up: false, extended: true),
            Key(VK_C, up: true, extended: true),
            Key(VK_CONTROL, up: true),
        };

        return SendInput((uint)inputs.Length, inputs, InputSize) == inputs.Length;
    }

    private static INPUT Key(int virtualKey, bool up, bool extended = false) => new()
    {
        Type = INPUT_KEYBOARD,
        Data = new INPUTUNION
        {
            Keyboard = new KEYBDINPUT
            {
                VirtualKey = (ushort)virtualKey,
                ScanCode = 0,
                Flags = (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0),
                Time = 0,
                ExtraInfo = IntPtr.Zero,
            },
        },
    };

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

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
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

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputs, INPUT[] buffer, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);
}
