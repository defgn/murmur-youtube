using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Murmur.App.Views;

namespace Murmur.App;

/// <summary>The application.</summary>
public partial class App : Application, IDisposable
{
    private Composition? _composition;
    private MainWindow? _main;

    /// <summary>No-op — the single-instance handle lives in Program.Main's using scope.</summary>
    public void Dispose() => GC.SuppressFinalize(this);

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        // A desktop app that dies at startup with no trace reads as "it won't launch".
        // Everything noteworthy goes to %LOCALAPPDATA%\Woffle\crash.log, so a launch
        // failure is one file instead of a guessing game.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteLog("unhandled", e.ExceptionObject as Exception);
        WriteLog("startup", null);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                // Single instance, deliberately. Two dictation processes would fight over
                // the push-to-talk hook and inject the same text twice. The gate itself ran
                // in Program.Main before any UI existed; this merely starts the listener
                // that wakes the window on a second launch.
                SingleInstance.StartShowSignalListener(() => Dispatcher.UIThread.Post(ShowMain));

                _composition = Composition.Create();
                _main = new MainWindow(_composition);
                desktop.MainWindow = _main;

                // Closing the window exits the app, tray icon included. OnLastWindowClose is
                // the mode; the explicit shutdown on window close is the belt and braces —
                // whatever keeps a window counted (a stray dialog, a tray quirk), the close
                // button always leaves. A process that survives its window in Task Manager
                // reads as broken.
                desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
                _main.Closed += (_, _) =>
                {
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    {
                        lifetime.Shutdown();
                        GuaranteeExit();
                    }
                };

                // Disposing tears down the keyboard hook and releases the audio device.
                // Leaving a low-level hook installed after exit is the kind of thing that
                // makes a machine feel broken until it is rebooted. The teardown runs on a
                // background task and never blocks the exit: the OS reclaims hooks and
                // hotkeys the moment the process dies, so a stalled teardown must not be
                // able to keep Woffle alive in Task Manager.
                desktop.ShutdownRequested += (_, _) =>
                {
                    var composition = _composition;
                    _composition = null;

                    if (composition is not null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await composition.DisposeAsync().ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                // The process is leaving regardless; a failed disposal must
                                // not turn a clean shutdown into a crash dialog.
                                WriteLog("shutdown", e);
                            }
                        });
                    }

                    Dispose();
                };
            }
            catch (Exception e)
            {
                WriteLog("startup", e);
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Appends a line to the crash log, creating it if needed.</summary>
    internal static void WriteLog(string phase, Exception? error)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Woffle");
            Directory.CreateDirectory(directory);

            var line = error is null
                ? $"[{DateTimeOffset.Now:O}] {phase}"
                : $"[{DateTimeOffset.Now:O}] {phase}: {error}";

            var path = Path.Combine(directory, "crash.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
            {
                File.WriteAllText(path, string.Empty);
            }

            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    // Tray callbacks arrive on a worker thread, not the UI thread. Touching windows from
    // there crashes Avalonia with a cross-thread exception — which is what "click anything
    // in the tray and the app dies" was. Every handler marshals to the UI dispatcher.

    private void OnTrayShow(object? sender, EventArgs e) => Dispatcher.UIThread.Post(ShowMain);

    private void OnTraySettings(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        ShowMain();
        if (_main is not null && _composition is not null)
        {
            _ = new SettingsWindow(_composition).ShowDialog(_main);
        }
    });

    private void OnTrayQuit(object? sender, EventArgs e)
    {
        // The failsafe must not depend on the UI thread: if it is wedged, a posted
        // shutdown never runs and the process survives in Task Manager. The tray callback
        // arrives on a worker thread, so arm the kill from here.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
            Environment.Exit(0);
        });

        Dispatcher.UIThread.Post(() =>
        {
            // Real quit: bypass the close-to-tray interception, then shut down.
            _main?.ApproveClose();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        });
    }

    /// <summary>
    /// If anything in the shutdown path stalls (a native audio teardown, a stray dialog),
    /// the process must still die: Task Manager should never show Woffle after Quit or a
    /// window close.
    /// </summary>
    private static void GuaranteeExit()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Environment.Exit(0);
        });
    }

    private void ShowMain()
    {
        if (_main is null) return;

        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }
}
