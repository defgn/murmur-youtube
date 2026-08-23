using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// The main window — one hero pill, the transcriptions and dictionary behind it.
/// </summary>
/// <remarks>
/// <para>
/// The whole app is one gesture: the dark pill in the middle of the window. Click it or
/// hold the push-to-talk key; release and the cleaned text lands in the focused app.
/// </para>
/// <para>
/// Built in code rather than XAML, deliberately. Every value comes from <see cref="Tokens"/>,
/// and XAML makes it far too easy to type a literal <c>Margin="12,8"</c> that silently escapes
/// the design system. In C# a stray number is visible in review.
/// </para>
/// </remarks>
public sealed class MainWindow : Window
{
    /// <summary>The default hero caption, shown when the engine has nothing to say.</summary>
    private const string DefaultHint = "Hold Right Ctrl — or click the pill. Fillers like um, actually are removed automatically.";

    private readonly Composition? _composition;
    private readonly Border _heroPill;
    private TextBlock _heroLabel = null!;   // assigned by BuildHeroPill, called from the ctor
    private readonly Rectangle[] _waveBars = new Rectangle[Tokens.Material.WaveformBars];
    private readonly Ellipse _statusDot;
    private readonly TextBlock _statusText;
    private readonly TextBlock _subLine;
    private readonly Button _transcriptionsTab;
    private readonly Button _dictionaryTab;
    private readonly ContentControl _sectionHost;
    private readonly DispatcherTimer _waveTimer;
    private readonly DispatcherTimer _noticeTimer;

    private Control? _transcriptionsView;
    private Control? _dictionaryView;
    private bool _heroPressed;
    private bool _recordingVisual;
    private bool _wasRecordingVisual;

    /// <summary>Builds a window with no engine behind it. Used by headless tests.</summary>
    public MainWindow() : this(null) { }

    /// <summary>Builds the window over <paramref name="composition"/>.</summary>
    public MainWindow(Composition? composition)
    {
        _composition = composition;

        Title = "Woffle";
        MinWidth = 760;
        MinHeight = 560;
        Width = 980;
        Height = 720;
        Background = Tokens.Brushes.ChassisGradient;

        // The window icon is the same mark as the tray — resolved from the assembly name
        // so it keeps working however the executable is named.
        Icon = new WindowIcon(AssetLoader.Open(new Uri(
            "avares://" + typeof(MainWindow).Assembly.GetName().Name + "/Assets/tray.ico")));

        _heroPill = BuildHeroPill();

        _statusDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Tokens.Brushes.Success,
        };
        _statusText = new TextBlock
        {
            Text = "Ready",
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _subLine = new TextBlock
        {
            Text = DefaultHint,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        _transcriptionsTab = BuildTab("Transcriptions", engaged: true);
        _dictionaryTab = BuildTab("Dictionary", engaged: false);
        _transcriptionsTab.Click += (_, _) => ShowSection(transcriptions: true);
        _dictionaryTab.Click += (_, _) => ShowSection(transcriptions: false);

        _sectionHost = new ContentControl();

        // The waveform and pill state are polled rather than pushed. The engine raises
        // Changed on a background thread at buffer rate, and marshalling every one of
        // those to the UI thread would be far more traffic than a display refresh needs.
        _waveTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40),
        };
        _waveTimer.Tick += (_, _) => SyncFromEngine();
        _waveTimer.Start();

        // Transient engine notices (typed, failed, loading) revert to the standing hint.
        _noticeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(4),
        };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _subLine.Text = DefaultHint;
        };

        Content = BuildLayout();
        ShowSection(transcriptions: true);

        if (_composition?.Engine is not null)
        {
            // Engine notices arrive on a background thread; the panel is UI-thread-only.
            _composition.Engine.Notice += (_, message) =>
                Dispatcher.UIThread.Post(() =>
                {
                    _subLine.Text = message;
                    _noticeTimer.Stop();
                    _noticeTimer.Start();
                });

            // Live preview text while the key is held.
            _composition.Engine.PartialTranscript += (_, text) =>
                Dispatcher.UIThread.Post(() =>
                    (_transcriptionsView as TranscriptionsView)?.ShowLive(text));

            _composition.Engine.Start();
        }
    }

    private DockPanel BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(Tokens.Space.Panel) };

        root.Children.Add(Panels.Docked(BuildHeader(), Dock.Top));
        root.Children.Add(Panels.Docked(BuildHero(), Dock.Top));
        root.Children.Add(Panels.Docked(BuildTabs(), Dock.Top));

        if (_composition is not null && !Composition.IsModelInstalled)
        {
            root.Children.Add(Panels.Docked(BuildModelBanner(), Dock.Top));
        }

        root.Children.Add(_sectionHost);
        return root;
    }

    /// <summary>Brand on the left, status chip and settings on the right.</summary>
    private DockPanel BuildHeader()
    {
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(Tokens.Radius.Control),
                    Background = Tokens.Brushes.Brand,
                    Child = new TextBlock
                    {
                        Text = "W",
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Title,
                        FontWeight = FontWeight.Bold,
                        Foreground = Tokens.Brushes.PillInk,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
                new StackPanel
                {
                    Spacing = 0,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Woffle",
                            FontFamily = Tokens.Fonts.Grotesque,
                            FontSize = Tokens.Fonts.Title,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Tokens.Brushes.Ink,
                        },
                        new TextBlock
                        {
                            Text = "DICTATION",
                            FontFamily = Tokens.Fonts.Grotesque,
                            FontSize = 10.5,
                            FontWeight = FontWeight.SemiBold,
                            LetterSpacing = Tokens.Fonts.SilkscreenTracking * 3,
                            Foreground = Tokens.Brushes.Silkscreen,
                        },
                    },
                },
            },
        };

        var statusChip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(Tokens.Radius.Pill),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug - Tokens.Space.Hair),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Tokens.Space.Snug,
                Children = { _statusDot, _statusText },
            },
        };

        var settings = new Button
        {
            // Material "tune" glyph as a vector so it renders on any platform, not just
            // Windows icon fonts.
            Content = VectorIcon(
                "M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zM15 9h2V7h4V5h-4V3h-2v6z",
                Tokens.Brushes.Ink, 18),
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(Tokens.Radius.Pill),
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        settings.Click += (_, _) => ShowSettings();

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { statusChip, settings },
        };

        var header = new DockPanel();

        // Docked children come first; the LAST child fills the remaining space. Adding the
        // right stack after the brand would make IT fill — the chip and gear would sit next
        // to the logo instead of in the top-right corner.
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(brand);

        return header;
    }

    /// <summary>The one gesture: the hero pill.</summary>
    private StackPanel BuildHero()
    {
        var hero = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = Tokens.Space.Snug,
            Margin = new Thickness(0, Tokens.Space.Roomy, 0, Tokens.Space.Base),
        };
        hero.Children.Add(_heroPill);
        hero.Children.Add(_subLine);
        return hero;
    }

    /// <summary>The hero pill: a plain Border, deliberately — the Fluent Button theme paints
    /// its own hover/pressed background over any custom colour, and the hero must hold its
    /// own. Pointer handlers carry the press/release gesture instead.</summary>
    private Border BuildHeroPill()
    {
        var mic = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(Tokens.Radius.Pill),
            Background = new SolidColorBrush(Tokens.Colors.PillInk, 0.10),
            Child = VectorIcon(
                "M12 14a3 3 0 0 0 3-3V6a3 3 0 1 0-6 0v5a3 3 0 0 0 3 3zm5-3a5 5 0 0 1-10 0H5a7 7 0 0 0 6 6.92V21h2v-3.08A7 7 0 0 0 19 11h-2z",
                Tokens.Brushes.PillInk, 17),
        };

        var label = new TextBlock
        {
            Text = "Hold to dictate",
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Pill,
            FontWeight = FontWeight.SemiBold,
            Foreground = Tokens.Brushes.PillInk,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _heroLabel = label;

        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
        };

        for (var i = 0; i < _waveBars.Length; i++)
        {
            _waveBars[i] = new Rectangle
            {
                Width = Tokens.Material.WaveformBarWidth,
                RadiusX = Tokens.Material.WaveformBarRadius,
                RadiusY = Tokens.Material.WaveformBarRadius,
                Fill = new SolidColorBrush(Tokens.Colors.PillInk, 0.55),
                VerticalAlignment = VerticalAlignment.Center,
            };
            bars.Children.Add(_waveBars[i]);
        }

        var pill = new Border
        {
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Tokens.Space.Roomy,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { mic, label, bars },
            },
            MinHeight = Tokens.Material.HeroPillHeight,
            MinWidth = Tokens.Material.HeroPillMinWidth,
            Padding = new Thickness(Tokens.Space.Wide + Tokens.Space.Snug, 0),
            CornerRadius = new CornerRadius(Tokens.Radius.Pill),
            Background = Tokens.Brushes.DarkPill,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
            // A soft drop shadow is what makes the pill float above the paper.
            Effect = new DropShadowEffect
            {
                BlurRadius = 40,
                OffsetY = 16,
                Color = Color.FromArgb(0x73, 0x24, 0x21, 0x1C),
            },
        };

        // Press and release, like a real push-to-talk key. The pointer is captured so a
        // release anywhere still counts, and Enter/Space work once focused.
        pill.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(pill).Properties.IsLeftButtonPressed) return;
            _heroPressed = true;
            e.Pointer.Capture(pill);
            UpdatePillBrush();
            e.Handled = true;
        };
        pill.PointerReleased += (_, e) =>
        {
            if (!_heroPressed) return;
            _heroPressed = false;
            e.Pointer.Capture(null);
            UpdatePillBrush();
            ToggleRecording();
            e.Handled = true;
        };
        pill.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                ToggleRecording();
                e.Handled = true;
            }
        };

        return pill;
    }

    /// <summary>A vector icon from SVG path data, at a fixed pixel size.</summary>
    /// <remarks>
    /// Drawn, not font glyphs: icon fonts (Segoe MDL2, Segoe Fluent) exist only on Windows,
    /// and a missing font renders as a box of tofu. Path data renders identically anywhere.
    /// </remarks>
    private static Avalonia.Controls.Shapes.Path VectorIcon(string pathData, IBrush fill, double size) => new()
    {
        Data = StreamGeometry.Parse(pathData),
        Fill = fill,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button BuildTab(string label, bool engaged)
    {
        var tab = new Button
        {
            Content = label,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            FontWeight = engaged ? FontWeight.Medium : FontWeight.Normal,
            Foreground = engaged ? Tokens.Brushes.Ink : new SolidColorBrush(Tokens.Colors.InkSecondary, 0.8),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = engaged ? Tokens.Brushes.Accent : Brushes.Transparent,
            Padding = new Thickness(2, 0, 2, Tokens.Space.Snug),
            CornerRadius = new CornerRadius(0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return tab;
    }

    private StackPanel BuildTabs() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = Tokens.Space.Wide + Tokens.Space.Snug,
        Margin = new Thickness(Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Base),
        Children = { _transcriptionsTab, _dictionaryTab },
    };

    /// <summary>
    /// A standing notice that the app cannot transcribe yet.
    /// </summary>
    /// <remarks>
    /// Windows has no built-in engine to fall back on, so a missing model means the app does
    /// nothing at all. That has to be visible on the front panel rather than buried in
    /// Settings.
    /// </remarks>
    private static Border BuildModelBanner() => Panels.Card(new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = Tokens.Space.Base,
        Margin = new Thickness(Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Base),
        Children =
        {
            new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Tokens.Colors.MeterAmber),
                VerticalAlignment = VerticalAlignment.Center,
            },
            new TextBlock
            {
                Text = "Speech model not installed — Woffle cannot transcribe yet. See Settings.",
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Label,
                Foreground = Tokens.Brushes.Ink,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
    });

    private void ShowSection(bool transcriptions)
    {
        SetTabEngaged(_transcriptionsTab, transcriptions);
        SetTabEngaged(_dictionaryTab, !transcriptions);

        if (_composition is null)
        {
            _sectionHost.Content = Panels.EmptyState(
                transcriptions ? "No recordings" : "Dictionary empty",
                transcriptions ? "Click the pill and speak." : "Add words it keeps getting wrong.");
            return;
        }

        // Built once and reused: rebuilding would drop the user's search text every time
        // they switched tabs.
        if (transcriptions)
        {
            _transcriptionsView ??= new TranscriptionsView(_composition.Transcripts);
            _sectionHost.Content = _transcriptionsView;
        }
        else
        {
            _dictionaryView ??= new DictionaryView(_composition.Dictionary);
            _sectionHost.Content = _dictionaryView;
        }
    }

    private static void SetTabEngaged(Button tab, bool engaged)
    {
        tab.FontWeight = engaged ? FontWeight.Medium : FontWeight.Normal;
        tab.Foreground = engaged ? Tokens.Brushes.Ink : new SolidColorBrush(Tokens.Colors.InkSecondary, 0.8);
        tab.BorderBrush = engaged ? Tokens.Brushes.Accent : Brushes.Transparent;
    }

    private void ShowSettings()
    {
        if (_composition is null) return;
        _ = new SettingsWindow(_composition).ShowDialog(this);
    }

    /// <summary>Pulls state from the engine onto the panel.</summary>
    private void SyncFromEngine()
    {
        var engine = _composition?.Engine;

        var recording = engine is null
            ? IsRecording
            : engine.State != DictationState.Idle;

        // Live preview card: show it the moment recording starts (the first partial lands
        // ~2s later), drop it when the finished transcript is on its way.
        if (recording && !_wasRecordingVisual)
        {
            (_transcriptionsView as TranscriptionsView)?.ShowLive(string.Empty);
        }
        else if (!recording && _wasRecordingVisual)
        {
            (_transcriptionsView as TranscriptionsView)?.ClearLive();
        }

        _wasRecordingVisual = recording;
        _heroLabel.Text = recording ? "Listening…" : "Hold to dictate";
        _recordingVisual = recording;
        UpdatePillBrush();

        _statusDot.Fill = recording ? Tokens.Brushes.Record : Tokens.Brushes.Success;
        _statusText.Text = recording ? "Recording" : "Ready";

        var level = engine?.Level ?? (recording ? 0.6 : 0);
        UpdateWaveform(recording, level);
    }

    /// <summary>Drives the hero waveform: still when idle, alive while recording.</summary>
    private void UpdateWaveform(bool recording, double level)
    {
        if (recording)
        {
            for (var i = 0; i < _waveBars.Length; i++)
            {
                var wiggle = (Random.Shared.NextDouble() * 0.5) + 0.25;
                var height = 4 + (level * 20 * wiggle);
                _waveBars[i].Height = Math.Clamp(height, 4, 22);
                _waveBars[i].Fill = Tokens.Brushes.RecordBar;
            }
        }
        else
        {
            var idle = new[] { 5.0, 8, 5, 11, 5, 8, 5 };
            for (var i = 0; i < _waveBars.Length; i++)
            {
                _waveBars[i].Height = idle[i % idle.Length];
                _waveBars[i].Fill = new SolidColorBrush(Tokens.Colors.PillInk, 0.55);
            }
        }
    }

    /// <summary>Toggles the transport. Exposed for headless tests.</summary>
    public void ToggleRecording()
    {
        // With no engine — a headless test, or a machine with no platform layer — the panel
        // still toggles so the visual state can be exercised.
        if (_composition?.Engine is null)
        {
            IsRecording = !IsRecording;
            SyncFromEngine();
            return;
        }

        // The button is a convenience; the hotkey is the real trigger. Both funnel through
        // the same engine so there is only ever one state machine.
        _composition.Engine.TogglePushToTalk();
        SyncFromEngine();
    }

    /// <summary>Applies the pill surface: recording colour, dimmed while pressed.</summary>
    private void UpdatePillBrush()
    {
        _heroPill.Background = _heroPressed
            ? _recordingVisual ? Tokens.Brushes.RecordPillPressed : Tokens.Brushes.DarkPillPressed
            : _recordingVisual ? Tokens.Brushes.RecordPill : Tokens.Brushes.DarkPill;
    }

    /// <summary>Whether the transport is engaged. Exposed for headless tests.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>The status chip text. Exposed for headless tests.</summary>
    public TextBlock StatusText => _statusText;

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _waveTimer.Stop();
        _noticeTimer.Stop();
        base.OnClosed(e);
    }
}
