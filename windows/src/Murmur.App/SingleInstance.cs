using System.Runtime.InteropServices;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Murmur.App.Tests")]

namespace Murmur.App;

/// <summary>
/// The single-instance gate. Runs before any Avalonia initialization, deliberately: a
/// second launch that got as far as creating the app would register its own tray icon and
/// then exit — leaving a ghost icon in the notification area whose Quit does nothing.
/// That reads as "Woffle won't close", and it is exactly the bug that kept coming back.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Woffle.SingleInstance";
    private const string ShowSignalName = @"Local\Woffle.ShowWindow";

    private readonly Mutex _mutex;
    private bool _owned;

    /// <summary>Test hook: a unique mutex name isolates a test from kernel state left by
    /// earlier processes (Linux leaves named-mutex backing files behind after exit).</summary>
    internal static string? MutexNameOverride { get; set; }

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _owned = true;
    }

    /// <summary>
    /// Takes the instance mutex, or null when a live instance owns it — the caller should
    /// wake it and exit without touching UI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ownership is checked, not assumed: <c>createdNew == false</c> can mean a live
    /// instance, an abandoned mutex (the previous owner crashed), or — on Linux — a stale
    /// backing file left behind after a normal exit. An abandoned or stale mutex is taken
    /// over, which is what makes Woffle launchable again after a hard kill.
    /// </para>
    /// </remarks>
    public static SingleInstance? Acquire()
    {
        var name = MutexNameOverride ?? MutexName;
        var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        if (createdNew)
        {
            _ = mutex.WaitOne(0);
            return new SingleInstance(mutex);
        }

        try
        {
            if (mutex.WaitOne(0))
            {
                return new SingleInstance(mutex);
            }
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstance(mutex);
        }

        // Genuinely held by a live instance. A failed acquisition must not leave a
        // dangling handle: it would keep the object alive past the owner's release.
        mutex.Dispose();
        return null;
    }

    /// <summary>Starts the listener that brings the running window forward on a second launch.</summary>
    public static void StartShowSignalListener(Action showMain)
    {
        EventWaitHandle? signal = null;
        try
        {
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        }
        catch (Exception e)
        {
            // Named kernel objects are Windows-only; on other platforms the show-signal is
            // a nicety, not a requirement. Log and skip rather than fail to launch.
            App.WriteLog("show signal unavailable", e);
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                while (signal.WaitOne())
                {
                    showMain();
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal: shutdown disposed the handle.
            }
            catch (Exception e)
            {
                App.WriteLog("show signal", e);
            }
        })
        {
            IsBackground = true,
            Name = "Woffle show signal",
        };
        thread.Start();
    }

    /// <summary>Signals the running instance to come to the front, then reports the second launch.</summary>
    public static void WakeRunningInstance()
    {
        try
        {
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            signal.Set();
        }
        catch (Exception e)
        {
            App.WriteLog("single-instance signal", e);
        }

        // Tell the user why nothing new appeared — Win32 MessageBox because it must work
        // before any Avalonia window exists.
        try
        {
            _ = User32.MessageBox(IntPtr.Zero,
                "Woffle is already running — its window has been brought to the front.",
                "Woffle", 0x00000040 /* MB_ICONINFORMATION */);
        }
        catch (Exception e)
        {
            App.WriteLog("single-instance message", e);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Ownership was lost in a takeover race; disposing the handle still closes
                // it cleanly.
            }
            _owned = false;
        }

        _mutex.Dispose();
    }

    private static class User32
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    }
}
