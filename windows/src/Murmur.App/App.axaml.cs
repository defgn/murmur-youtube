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
    private Mutex? _singleInstance;

    /// <summary>Releases the single-instance mutex.</summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _singleInstance?.Dispose();
    }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Single instance, deliberately. Two dictation processes would fight over the
            // push-to-talk hook and inject the same text twice. A second launch exits
            // immediately — the running instance is the one the user should be using.
            _singleInstance = new Mutex(initiallyOwned: true, "Local\\Woffle.SingleInstance", out var createdNew);
            if (!createdNew)
            {
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
                base.OnFrameworkInitializationCompleted();
                return;
            }

            _composition = Composition.Create();
            _main = new MainWindow(_composition);
            desktop.MainWindow = _main;

            // Closing the window exits the app, tray icon included. OnLastWindowClose is the
            // mode; the explicit shutdown on window close is the belt and braces — whatever
            // keeps a window counted (a stray dialog, a tray quirk), the close button always
            // leaves. A process that survives its window in Task Manager reads as broken.
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            _main.Closed += (_, _) =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
            };

            // Disposing tears down the keyboard hook and releases the audio device. Leaving
            // a low-level hook installed after exit is the kind of thing that makes a
            // machine feel broken until it is rebooted.
            desktop.ShutdownRequested += (_, _) =>
            {
                try
                {
                    _composition?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch
                {
                    // The process is leaving regardless; a failed disposal must not turn a
                    // clean shutdown into a crash dialog.
                }
                _composition = null;
                _singleInstance?.Dispose();
                _singleInstance = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
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

    private void OnTrayQuit(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    });

    private void ShowMain()
    {
        if (_main is null) return;

        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }
}
