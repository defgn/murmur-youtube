using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MeetingTranscriber.Core;

namespace MeetingTranscriber;

/// <summary>Application composition root.</summary>
public sealed class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(new MeetingSettingsStore(MeetingSettingsStore.DefaultPath));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
