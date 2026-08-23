using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;
using Murmur.Speech;

namespace Murmur.App.Views;

/// <summary>Settings: the hotkey, the microphone, and the model.</summary>
public sealed class SettingsWindow : Window
{
    /// <summary>
    /// The keys offered, in recommendation order.
    /// </summary>
    /// <remarks>
    /// Right Alt is included but listed last and carries a warning: on German, Polish, UK,
    /// Nordic and most Latin-American layouts it is AltGr, and binding push-to-talk there
    /// breaks typing <c>@</c>, <c>€</c>, <c>\</c> and <c>|</c>.
    /// </remarks>
    private static readonly (int Key, string Label, string? Warning)[] Keys =
    [
        (0xA3, "RIGHT CTRL", null),
        (0xA1, "RIGHT SHIFT", null),
        (0x14, "CAPS LOCK", null),
        (0x7C, "F13", null),
        (0xA5, "RIGHT ALT", "Right Alt is AltGr on many European layouts — binding it here "
                          + "will interfere with typing @, €, \\ and |."),
    ];

    /// <summary>Gain slider bounds, in linear amplitude.</summary>
    private const float MinGain = 0.5f;
    private const float MaxGain = 4.0f;

    private readonly Composition _composition;
    private readonly AppSettings _settings;
    private readonly StackPanel _keyRow;
    private readonly TextBlock _keyWarning;
    private readonly StackPanel _micRow;
    private readonly TextBlock _micStatus;

    /// <summary>Builds the settings window.</summary>
    public SettingsWindow(Composition composition)
    {
        _composition = composition;
        _settings = composition.Settings;

        Title = "Murmur Settings";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = Tokens.Brushes.Chassis;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _keyRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
        };

        _keyWarning = new TextBlock
        {
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            Foreground = new SolidColorBrush(Tokens.Colors.MeterAmber),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        foreach (var (key, label, warning) in Keys)
        {
            var button = new TransportKey { Content = label, EngagedColor = Tokens.Colors.Ink };
            button.Click += (_, _) => SelectKey(key, warning);
            _keyRow.Children.Add(button);
        }

        _micRow = new StackPanel { Spacing = Tokens.Space.Tight };
        _micStatus = new TextBlock
        {
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
            TextWrapping = TextWrapping.Wrap,
        };

        Content = BuildContent();
        SelectKey(_settings.Data.PushToTalkKey, WarningFor(_settings.Data.PushToTalkKey));
        RefreshMicrophones();
    }

    private static string? WarningFor(int key) =>
        Keys.FirstOrDefault(k => k.Key == key).Warning;

    private StackPanel BuildContent() => new StackPanel
    {
        Margin = new Thickness(Tokens.Space.Panel),
        Spacing = Tokens.Space.Wide,
        Children =
        {
            Section("PUSH TO TALK", new StackPanel
            {
                Spacing = Tokens.Space.Snug,
                Children =
                {
                    _keyRow,
                    _keyWarning,
                    Note("Hold this key anywhere to dictate. The key is passed through to the "
                       + "focused app rather than swallowed, so it never gets stuck down."),
                },
            }),

            Section("MICROPHONE", BuildMicrophoneSection()),

            Section("MODEL", BuildModelSection()),

            Section("BEHAVIOUR", new StackPanel
            {
                Spacing = Tokens.Space.Snug,
                Children =
                {
                    Toggle("Type transcripts into the focused app", _settings.Data.InjectText,
                        v => Save(_settings.Data with { InjectText = v })),
                    Toggle("Remove filler words (um, uh, er, actually…)", _settings.Data.RemoveFillers,
                        v => Save(_settings.Data with { RemoveFillers = v })),
                    Toggle("Keep a transcript history", _settings.Data.KeepHistory,
                        v => Save(_settings.Data with { KeepHistory = v })),
                },
            }),
        },
    };

    private StackPanel BuildMicrophoneSection()
    {
        var gain = new Slider
        {
            Minimum = MinGain,
            Maximum = MaxGain,
            Value = _settings.Data.InputGain,
            IsSnapToTickEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var gainLabel = new TextBlock
        {
            FontFamily = Tokens.Fonts.Mono,
            FontSize = Tokens.Fonts.Label,
            Foreground = Tokens.Brushes.InkOnDeck,
            Text = $"Input boost: {_settings.Data.InputGain:0.0}x",
            VerticalAlignment = VerticalAlignment.Center,
        };

        gain.ValueChanged += (_, e) =>
        {
            var value = Math.Clamp((float)e.NewValue, MinGain, MaxGain);
            gainLabel.Text = $"Input boost: {value:0.0}x";
            _composition.ConfigureInput(_settings.Data.InputDeviceId, value);
        };

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                _micRow,
                _micStatus,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = Tokens.Space.Base,
                    Children = { gain, gainLabel },
                },
                Note("Choose the microphone you actually speak into. The boost amplifies "
                   + "quiet microphones so you don't have to shout — applied live, no restart."),
            },
        };
    }

    /// <summary>
    /// Rebuilds the device list. Called on open; devices can be unplugged, so it also
    /// refreshes whenever the window is shown.
    /// </summary>
    private void RefreshMicrophones()
    {
        _micRow.Children.Clear();

        var devices = PlatformFactory.ListInputDevices();
        if (devices.Count == 0)
        {
            _micStatus.Text = "No capture devices found — plug in a microphone.";
            return;
        }

        var selected = _settings.Data.InputDeviceId;
        _micStatus.Text = selected is null
            ? "Using the Windows default microphone. Pick one below to choose explicitly."
            : "Using your selected microphone.";

        foreach (var device in devices)
        {
            var isChosen = string.Equals(device.Id, selected, StringComparison.OrdinalIgnoreCase);

            var label = device.IsDefault && selected is null
                ? $"{device.Name}  (default)"
                : device.Name;

            var button = new TransportKey
            {
                Content = label,
                EngagedColor = Tokens.Colors.Ink,
                IsEngaged = isChosen,
            };

            button.Click += (_, _) => SelectMicrophone(device.Id, device.Name);
            _micRow.Children.Add(button);
        }
    }

    private void SelectMicrophone(string deviceId, string deviceName)
    {
        foreach (var child in _micRow.Children)
        {
            if (child is TransportKey key) key.IsEngaged = false;
        }

        foreach (var child in _micRow.Children)
        {
            if (child is TransportKey key && key.Content is string content && content.StartsWith(deviceName, StringComparison.Ordinal))
            {
                key.IsEngaged = true;
                break;
            }
        }

        _micStatus.Text = $"Using {deviceName}.";
        _composition.ConfigureInput(deviceId, _settings.Data.InputGain);
    }

    private static StackPanel BuildModelSection()
    {
        var located = ParakeetTranscriber.Locate();
        var found = located is not null;

        var status = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children =
            {
                new Lamp
                {
                    IsLit = found,
                    LampColor = found ? Tokens.Colors.MeterGreen : Tokens.Colors.MeterAmber,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = found ? "Parakeet ready" : "Model not installed",
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Body,
                    Foreground = Tokens.Brushes.Ink,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

        var detail = found
            // Showing the resolved path matters: "model not found" is unactionable without
            // knowing which directory was actually checked.
            ? Note($"Loaded from {located}")
            : Note("Windows has no built-in speech engine equivalent to Apple's, so Murmur "
                 + "cannot transcribe until the Parakeet model is downloaded (~661 MB). "
                 + "See docs/PARAKEET-WINDOWS.md. Expected in:\n"
                 + string.Join("\n", ParakeetTranscriber.DefaultSearchPaths()));

        return new StackPanel { Spacing = Tokens.Space.Snug, Children = { status, detail } };
    }

    private void SelectKey(int key, string? warning)
    {
        for (var i = 0; i < Keys.Length; i++)
        {
            ((TransportKey)_keyRow.Children[i]).IsEngaged = Keys[i].Key == key;
        }

        _keyWarning.Text = warning ?? string.Empty;
        _keyWarning.IsVisible = warning is not null;

        if (_settings.Data.PushToTalkKey != key) Save(_settings.Data with { PushToTalkKey = key });
    }

    private void Save(SettingsData data) => _settings.Update(data);

    private static BrushedPanel Section(string label, Control content) => new BrushedPanel
    {
        Child = new StackPanel
        {
            Margin = new Thickness(Tokens.Space.Roomy),
            Spacing = Tokens.Space.Base,
            Children = { new Silkscreen { Text = label, IsLarge = true }, content },
        },
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Label,
        Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
        TextWrapping = TextWrapping.Wrap,
    };

    private static CheckBox Toggle(string label, bool value, Action<bool> onChange)
    {
        var box = new CheckBox
        {
            IsChecked = value,
            Content = new TextBlock
            {
                Text = label,
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Body,
                Foreground = Tokens.Brushes.Ink,
            },
        };

        box.IsCheckedChanged += (_, _) => onChange(box.IsChecked ?? false);
        return box;
    }
}
