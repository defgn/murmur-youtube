using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WofflePlus.Cloud;
using WofflePlus.Design;

namespace WofflePlus;

/// <summary>
/// The whole app: the 001-dark-copilot mockup, live — header with device pickers,
/// transcript on the left, question + answer (Full/Short) on the right, hotkey bar.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly PlusSettingsStore _settings;
    private readonly InterviewSession _session;

    private readonly ComboBox _micPicker;
    private readonly ComboBox _outputPicker;
    private readonly TextBlock _backendChip;
    private readonly StackPanel _transcriptList;
    private readonly ScrollViewer _transcriptScroll;
    private readonly TextBlock _questionText;
    private readonly TextBlock _answerMeta;
    private readonly SelectableTextBlock _fullAnswer;
    private readonly SelectableTextBlock _shortAnswer;
    private readonly Border _fullCard;
    private readonly Border _shortCard;
    private readonly Border _fullTab;
    private readonly Border _shortTab;
    private readonly TextBlock _statusLine;
    private bool _shortShown;

    // Listening toggle (built in Header; updated through UpdateListeningUi).
    private Border? _listenButton;
    private Border? _listenDot;
    private TextBlock? _listenLabel;
    private bool _listening = true;

    public MainWindow(PlusSettingsStore settings)
    {
        _settings = settings;
        _session = new InterviewSession(settings);

        Title = "Woffle+";
        Width = 1180;
        Height = 760;
        MinWidth = 980;
        MinHeight = 620;
        Background = Plus.Brush.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _micPicker = DevicePicker();
        _outputPicker = DevicePicker();
        _backendChip = new TextBlock
        {
            FontSize = Plus.Font.Small,
            FontWeight = FontWeight.SemiBold,
            Foreground = Plus.Brush.Green,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RefreshBackendChip();

        _transcriptList = new StackPanel();
        _transcriptScroll = new ScrollViewer
        {
            Content = _transcriptList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        _questionText = Text(Plus.Font.BodyLarge, Plus.Brush.Ink, FontWeight.SemiBold);
        _answerMeta = Text(Plus.Font.Small, Plus.Brush.InkSecondary, FontWeight.Normal);
        _fullAnswer = AnswerBlock();
        _shortAnswer = AnswerBlock();

        _fullCard = Card(new StackPanel
        {
            Spacing = Plus.Space.Snug,
            Children = { SmallCaps("SUGGESTED ANSWER", Plus.Brush.Green), _answerMeta, _fullAnswer },
        });
        _shortCard = Card(new StackPanel
        {
            Spacing = Plus.Space.Snug,
            Children = { SmallCaps("TALKING POINTS · ~30 SEC SPOKEN", Plus.Brush.Green), _shortAnswer },
        });

        (_fullTab, _) = Tab("Full answer");
        (_shortTab, _) = Tab("Short version");
        _fullTab.PointerPressed += (_, _) => ShowShort(false);
        _shortTab.PointerPressed += (_, _) => ShowShort(true);

        var regen = OutlineButton("↻ Try another angle");
        regen.Click += (_, _) => _session.Regenerate();
        var copy = OutlineButton("Copy");
        copy.Click += (_, _) => CopyAnswer();

        var tabBar = new DockPanel();
        DockPanel.SetDock(regen, Dock.Right);
        DockPanel.SetDock(copy, Dock.Right);
        tabBar.Children.Add(regen);
        tabBar.Children.Add(copy);
        tabBar.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Plus.Space.Snug,
            Children = { _fullTab, _shortTab },
        });

        var questionCard = Card(new StackPanel
        {
            Spacing = Plus.Space.Snug,
            Children = { SmallCaps("DETECTED QUESTION", Plus.Brush.Orange), _questionText },
        });
        questionCard.BorderThickness = new Thickness(0, 0, 0, Plus.Line.Accent);
        questionCard.BorderBrush = Plus.Brush.Orange;

        _statusLine = Text(Plus.Font.Label, Plus.Brush.InkSecondary, FontWeight.Normal);
        _statusLine.IsVisible = false;

        var answerColumn = new DockPanel();
        answerColumn.Children.Add(PaneHeader("AI ANSWER", Dock.Top));
        answerColumn.Children.Add(PaneHeaderStrip(tabBar));
        var answerScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = Plus.Space.Base,
                Margin = new Thickness(Plus.Space.Roomy, 0, Plus.Space.Roomy, Plus.Space.Base),
                Children = { questionCard, _fullCard, _shortCard, _statusLine },
            },
        };
        answerColumn.Children.Add(answerScroll);

        var transcriptColumn = new DockPanel();
        transcriptColumn.Children.Add(PaneHeader("LIVE TRANSCRIPT", Dock.Top));
        transcriptColumn.Children.Add(_transcriptScroll);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(11, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(10, GridUnitType.Star)));
        transcriptColumn.SetValue(Grid.ColumnProperty, 0);
        grid.Children.Add(transcriptColumn);
        var seam = new Border { Width = 1, Background = Plus.Brush.Border };
        seam.SetValue(Grid.ColumnProperty, 1);
        grid.Children.Add(seam);
        answerColumn.SetValue(Grid.ColumnProperty, 2);
        grid.Children.Add(answerColumn);

        var root = new DockPanel { Background = Plus.Brush.Bg };
        var header = Header();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var hotkeyBar = HotkeyBar();
        DockPanel.SetDock(hotkeyBar, Dock.Bottom);
        root.Children.Add(hotkeyBar);
        root.Children.Add(grid);
        Content = root;

        ShowShort(false);
        WireSession();
        RefreshDevices();

        KeyDown += (_, e) =>
        {
            if (e.KeyModifiers is not (KeyModifiers.Control | KeyModifiers.Shift)) return;
            if (e.Key == Key.Space)
            {
                _listening = _session.ToggleListening();
                UpdateListeningUi();
                e.Handled = true;
            }
            else if (e.Key == Key.A) { _session.Regenerate(); e.Handled = true; }
            else if (e.Key == Key.C) { CopyAnswer(); e.Handled = true; }
        };

        _session.ListeningChanged += (_, on) => Dispatcher.UIThread.Post(() =>
        {
            _listening = on;
            UpdateListeningUi();
        });

        // Sign-in/out and backend changes in Settings repaint the header chip.
        _settings.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshBackendChip);
        _session.CodexAuthChanged += (_, _) => Dispatcher.UIThread.Post(RefreshBackendChip);

        Closed += (_, _) => _session.Dispose();
    }

    // ---- header: brand, listening chip, device pickers, backend ----

    private Control Header()
    {
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Plus.Space.Base,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "Woffle+",
                    FontFamily = Plus.Font.Family,
                    FontSize = Plus.Font.Title,
                    FontWeight = FontWeight.Bold,
                    Foreground = Plus.Brush.Ink,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                BuildListeningButton(),
            },
        };

        var pickers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Plus.Space.Base,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                PickerLabel("MIC", _micPicker),
                PickerLabel("SPEAKER", _outputPicker),
            },
        };

        var settings = OutlineButton("⚙ Settings");
        settings.Click += (_, _) => new SettingsWindow(_settings, _session).Show(this);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Plus.Space.Base,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { pickers, _backendChip, settings },
        };

        var header = new DockPanel
        {
            Background = Plus.Brush.Header,
        };
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(brand);
        return new Border
        {
            Background = Plus.Brush.Header,
            Margin = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(Plus.Space.Wide, Plus.Space.Base),
            Child = header,
        };
    }

    private Control BuildListeningButton()
    {
        _listenDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _listenLabel = new TextBlock
        {
            FontSize = Plus.Font.Small,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _listenButton = new Border
        {
            Background = Plus.Brush.Green,
            BorderBrush = Plus.Brush.Green,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Plus.Radius.Pill),
            Padding = new Thickness(Plus.Space.Base, Plus.Space.Tight),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Plus.Space.Snug,
                Children = { _listenDot, _listenLabel },
            },
        };
        _listenButton.PointerPressed += (_, _) =>
        {
            _listening = _session.ToggleListening();
            UpdateListeningUi();
        };
        UpdateListeningUi();
        return _listenButton;
    }

    private void UpdateListeningUi()
    {
        if (_listenButton is null || _listenDot is null || _listenLabel is null) return;

        var on = _listening;
        _listenDot.Background = on ? Plus.Brush.Ink : Plus.Brush.InkSecondary;
        _listenLabel.Text = on ? "Listening" : "Paused";
        _listenLabel.Foreground = on ? Plus.Brush.Ink : Plus.Brush.InkSecondary;
        _listenButton.Background = on ? Plus.Brush.Green : Plus.Brush.Keycap;
        _listenButton.BorderBrush = on ? Plus.Brush.Green : Plus.Brush.Border;
    }

    private static Control PickerLabel(string label, Control picker) => new StackPanel
    {
        Spacing = Plus.Space.Hair,
        Children =
        {
            SmallCaps(label, Plus.Brush.InkSecondary),
            picker,
        },
    };

    private ComboBox DevicePicker() => new()
    {
        MinWidth = 210,
        MaxWidth = 250,
        FontSize = Plus.Font.Small,
        Background = Plus.Brush.Bg,
        BorderBrush = Plus.Brush.Border,
        CornerRadius = new CornerRadius(Plus.Radius.Control),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>Repaints the backend chip from the current settings + sign-in state.</summary>
    private void RefreshBackendChip()
    {
        _backendChip.Text = _settings.Data.Backend switch
        {
            "Codex" => CodexLogin.IsSignedIn ? "ChatGPT ✓" : "ChatGPT — sign in",
            "Zai" => "z.ai",
            "OpenAi" => "OpenAI",
            "Anthropic" => "Claude",
            _ => _settings.Data.Backend,
        };
    }

    // ---- transcript / answer panes ----

    private static Control PaneHeader(string label, Dock dock)
    {
        var border = new Border
        {
            Background = Plus.Brush.Header,
            BorderBrush = Plus.Brush.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = SmallCaps(label, Plus.Brush.InkSecondary),
        };
        DockPanel.SetDock(border, dock);
        return border;
    }

    private static Control PaneHeaderStrip(Control strip) => new Border
    {
        Background = Plus.Brush.Bg,
        Padding = new Thickness(Plus.Space.Roomy, Plus.Space.Snug, Plus.Space.Roomy, 0),
        Child = strip,
    };

    private static TextBlock SmallCaps(string label, IBrush colour) => new()
    {
        Text = label,
        FontSize = Plus.Font.Small,
        FontWeight = FontWeight.SemiBold,
        Foreground = colour,
    };

    private static TextBlock Text(double size, IBrush colour, FontWeight weight) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Foreground = colour,
        TextWrapping = TextWrapping.Wrap,
    };

    private static SelectableTextBlock AnswerBlock() => new()
    {
        FontSize = Plus.Font.BodyLarge,
        Foreground = Plus.Brush.Ink,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Border Card(Control content) => new()
    {
        Background = Plus.Brush.Card,
        BorderBrush = Plus.Brush.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(Plus.Radius.Card),
        Padding = new Thickness(Plus.Space.Roomy),
        Child = content,
    };

    private (Border Tab, TextBlock Label) Tab(string label)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = Plus.Font.Small,
            FontWeight = FontWeight.SemiBold,
            Foreground = Plus.Brush.InkSecondary,
        };
        var border = new Border
        {
            Child = text,
            Background = Plus.Brush.Card,
            BorderBrush = Plus.Brush.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Plus.Radius.Control),
            Padding = new Thickness(Plus.Space.Base, Plus.Space.Tight),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        return (border, text);
    }

    private static Button OutlineButton(string label) => new()
    {
        Content = label,
        FontSize = Plus.Font.Small,
        FontWeight = FontWeight.SemiBold,
        Foreground = Plus.Brush.Blue,
        Background = Plus.Brush.Keycap,
        BorderBrush = Plus.Brush.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(Plus.Radius.Control),
        Padding = new Thickness(Plus.Space.Base, Plus.Space.Tight),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void ShowShort(bool shortShown)
    {
        _shortShown = shortShown;
        _fullCard.IsVisible = !shortShown;
        _shortCard.IsVisible = shortShown;
        Style(_fullTab, !shortShown);
        Style(_shortTab, shortShown);

        void Style(Border tab, bool engaged)
        {
            tab.Background = engaged ? Plus.Brush.Header : Plus.Brush.Card;
            tab.BorderBrush = engaged ? Plus.Brush.Green : Plus.Brush.Border;
            if (tab.Child is TextBlock label)
            {
                label.Foreground = engaged ? Plus.Brush.Green : Plus.Brush.InkSecondary;
            }
        }
    }

    private static Control HotkeyBar() => new Border
    {
        Background = Plus.Brush.Header,
        BorderBrush = Plus.Brush.Border,
        BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(Plus.Space.Wide, Plus.Space.Snug),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Plus.Space.Wide,
            Children =
            {
                Hotkey("Ctrl+Shift+Space", "listening on/off"),
                Hotkey("Ctrl+Shift+A", "regenerate answer"),
                Hotkey("Ctrl+Shift+C", "copy answer"),
            },
        },
    };

    private static Control Hotkey(string keys, string label)
    {
        var kbd = new Border
        {
            Background = Plus.Brush.Keycap,
            BorderBrush = Plus.Brush.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1),
            Child = new TextBlock
            {
                Text = keys,
                FontSize = 11,
                FontFamily = Plus.Font.Mono,
                Foreground = Plus.Brush.Ink,
            },
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = Plus.Font.Small,
            Foreground = Plus.Brush.InkSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Plus.Space.Tight,
            Children = { kbd, text },
        };
    }

    // ---- live behaviour ----

    private void WireSession()
    {
        _session.TranscriptTurn += (_, turn) => Dispatcher.UIThread.Post(() =>
        {
            AddTurn(turn.Speaker, turn.Text);
        });

        _session.QuestionDetected += (_, question) => Dispatcher.UIThread.Post(() =>
        {
            _questionText.Text = question;
            _answerMeta.Text = "drafting…";
            _fullAnswer.Text = string.Empty;
            _shortAnswer.Text = string.Empty;
            _statusLine.IsVisible = false;
        });

        _session.AnswerReady += (_, answer) => Dispatcher.UIThread.Post(() =>
        {
            _answerMeta.Text = answer.Attempt > 1
                ? $"attempt {answer.Attempt} · drafted in {answer.Seconds:0.0}s"
                : $"drafted in {answer.Seconds:0.0}s";
            _fullAnswer.Text = answer.Full;
            _shortAnswer.Text = answer.Short;
        });

        _session.Notice += (_, message) => Dispatcher.UIThread.Post(() =>
        {
            _statusLine.Text = message;
            _statusLine.IsVisible = true;
        });

        _session.Start();
    }

    private void AddTurn(string speaker, string text)
    {
        var isInterviewer = speaker == "interviewer";
        var row = new StackPanel
        {
            Spacing = Plus.Space.Tight,
            Margin = new Thickness(Plus.Space.Roomy, 0, Plus.Space.Roomy, Plus.Space.Base),
            Children =
            {
                new TextBlock
                {
                    Text = isInterviewer ? "INTERVIEWER" : "YOU",
                    FontSize = Plus.Font.Small,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = isInterviewer ? Plus.Brush.Blue : Plus.Brush.Purple,
                },
                new TextBlock
                {
                    Text = text,
                    FontSize = Plus.Font.Body,
                    Foreground = Plus.Brush.Ink,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        if (isInterviewer && _settings.Data.Backend is "Codex" or "Zai" or "OpenAi" or "Anthropic")
        {
            // flagged questions already handled by the session; visual flag here.
        }

        _transcriptList.Children.Add(row);
        _transcriptScroll.ScrollToEnd();
    }

    private void RefreshDevices()
    {
        var mics = Platform.Devices.ListCaptureDevices();
        var outputs = Platform.Devices.ListRenderDevices();

        _micPicker.ItemsSource = mics.Select(d => d.Name).ToList();
        _outputPicker.ItemsSource = outputs.Select(d => d.Name).ToList();

        var micIndex = mics.FindIndex(d => d.Id == _settings.Data.MicDeviceId);
        var outIndex = outputs.FindIndex(d => d.Id == _settings.Data.OutputDeviceId);
        _micPicker.SelectedIndex = micIndex >= 0 ? micIndex : 0;
        _outputPicker.SelectedIndex = outIndex >= 0 ? outIndex : 0;

        _micPicker.SelectionChanged += (_, _) => ApplyDevice(mic: true);
        _outputPicker.SelectionChanged += (_, _) => ApplyDevice(mic: false);
    }

    private void ApplyDevice(bool mic)
    {
        var id = mic ? SelectedId(_micPicker) : SelectedId(_outputPicker);
        _settings.Update(mic
            ? _settings.Data with { MicDeviceId = id }
            : _settings.Data with { OutputDeviceId = id });
        _session.ConfigureDevices(_settings.Data.MicDeviceId, _settings.Data.OutputDeviceId);
    }

    private string? SelectedId(ComboBox picker)
    {
        var devices = picker == _micPicker
            ? Platform.Devices.ListCaptureDevices()
            : Platform.Devices.ListRenderDevices();
        var index = picker.SelectedIndex;
        return index >= 0 && index < devices.Count ? devices[index].Id : null;
    }

    private async void CopyAnswer()
    {
        var text = _shortShown ? _shortAnswer.Text : _fullAnswer.Text;
        if (!string.IsNullOrWhiteSpace(text) && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
