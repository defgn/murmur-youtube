using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MeetingTranscriber.Core;

namespace MeetingTranscriber;

/// <summary>
/// Variant C briefing layout: navy ground, device bar, full width in-flow alert
/// strip, timeline transcript with timestamp gutter plus YOU/THEM tags, bottom
/// summary card row. Keyboard: R record/pause, / search, L resume live, C copy
/// reply, Esc dismiss alert.
/// </summary>
internal sealed class MainWindow : Window
{
    private static readonly Color Bg = Color.FromRgb(0x0a, 0x12, 0x30);
    private static readonly Color HeaderBg = Color.FromRgb(0x10, 0x1c, 0x46);
    private static readonly Color Line = Color.FromRgb(0x2a, 0x3a, 0x6e);
    private static readonly Color Ink = Color.FromRgb(0xe8, 0xed, 0xf3);
    private static readonly Color Secondary = Color.FromRgb(0x93, 0xa1, 0xc7);
    private static readonly Color Accent = Color.FromRgb(0x7a, 0xa2, 0xff);
    private static readonly Color Green = Color.FromRgb(0x7b, 0xf0, 0xc1);
    private static readonly Color GreenBg = Color.FromRgb(0x0c, 0x2f, 0x26);
    private static readonly Color GreenLine = Color.FromRgb(0x1f, 0x6f, 0x57);
    private static readonly Color Orange = Color.FromRgb(0xf0, 0x88, 0x3e);
    private static readonly Color AlertBg = Color.FromRgb(0x3a, 0x24, 0x04);
    private static readonly Color QuoteInk = Color.FromRgb(0xf7, 0xea, 0xd2);
    private static readonly Color AskInk = Color.FromRgb(0xe3, 0xc8, 0x93);
    private static readonly Color StackInk = Color.FromRgb(0xc9, 0xa8, 0x7c);
    private static readonly Color RowInk = Color.FromRgb(0xcf, 0xd8, 0xef);
    private static readonly Color YouBg = Color.FromRgb(0x1c, 0x2a, 0x5e);
    private static readonly Color ThemBg = Color.FromRgb(0x3a, 0x2a, 0x5e);
    private static readonly Color ThemInk = Color.FromRgb(0xc9, 0xa8, 0xff);
    private static readonly Color HlBg = Color.FromRgb(0x2a, 0x1f, 0x14);
    private static readonly Color HlLine = Color.FromRgb(0xa0, 0x5c, 0x22);
    private static readonly Color RecRed = Color.FromRgb(0xf8, 0x51, 0x49);
    private static readonly Color DarkInk = Color.FromRgb(0x1a, 0x12, 0x06);
    private static readonly Color MatchBg = Color.FromRgb(0x1a, 0x24, 0x40);

    private readonly MeetingSettingsStore _settings;
    private readonly MtSession _session;
    private readonly MeetingOrchestrator _orchestrator;

    private readonly ComboBox _micPicker = NewPicker();
    private readonly ComboBox _outputPicker = NewPicker();
    private readonly Button _pauseButton;
    private readonly TextBlock _recLabel;
    private readonly Border _recDot;

    private readonly Border _alertStrip;
    private readonly TextBlock _alertBadge;
    private readonly TextBlock _alertTime;
    private readonly SelectableTextBlock _alertQuote;
    private readonly TextBlock _alertAsk;
    private readonly TextBlock _alertReply;
    private readonly TextBlock _alertMeta;

    private readonly ScrollViewer _transcriptScroll;
    private readonly StackPanel _transcriptList;
    private readonly TextBox _searchBox;
    private readonly Button _resumeButton;

    private readonly TextBlock _summaryTail;
    private readonly Grid _cardsGrid;
    private readonly TextBlock _statusLine;

    private readonly Stopwatch _sessionClock = Stopwatch.StartNew();
    private readonly DispatcherTimer _clockTick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _blinkTick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _summaryPoll = new() { Interval = TimeSpan.FromSeconds(5) };

    private readonly List<string?> _micIds = [];
    private readonly List<string?> _outputIds = [];
    private readonly List<RowEntry> _rows = [];
    private readonly HashSet<Guid> _triggerIds = [];
    private readonly List<SummaryCard> _cards = [];
    private readonly CancellationTokenSource _shutdown = new();

    private bool _pinnedToLive = true;
    private bool _summaryInFlight;
    private bool _blinkOn;
    private Border? _blinkCard;
    private AlertContent? _activeAlert;
    private MeetingAlert? _activeDetection;
    private DateTimeOffset _summaryUpdatedAt;

    private sealed class RowEntry
    {
        public RowEntry(TranscriptUtterance utterance, Border container, TextBlock text)
        {
            Utterance = utterance;
            Container = container;
            Text = text;
        }

        public TranscriptUtterance Utterance { get; }

        public Border Container { get; }

        public TextBlock Text { get; }
    }

    /// <summary>Builds the main window over the shared settings store.</summary>
    /// <param name="settings">Shared settings store.</param>
    public MainWindow(MeetingSettingsStore settings)
    {
        _settings = settings;
        _session = new MtSession(settings);
        _orchestrator = new MeetingOrchestrator(_session.Completer, () => DateTimeOffset.Now);

        Title = "Meeting Transcriber";
        Width = 1480;
        Height = 960;
        MinWidth = 1100;
        MinHeight = 750;
        Background = new SolidColorBrush(Bg);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _session.UtteranceReady += (_, u) => Dispatcher.UIThread.Post(() => OnUtterance(u));
        _session.Notice += (_, m) => Dispatcher.UIThread.Post(() => SetStatus(m));
        _session.ListeningChanged += (_, on) => Dispatcher.UIThread.Post(() => UpdatePauseUi(on));
        _settings.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _orchestrator.ConfigureCompleter(_session.Completer);
            RefreshPickersSelection();
        });

        _recLabel = NewText(13, Secondary, FontWeight.Bold);
        _recDot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(RecRed) };

        _alertStrip = BuildAlertStrip(
            out _alertBadge, out _alertTime, out _alertQuote, out _alertAsk, out _alertReply, out _alertMeta);

        _transcriptList = new StackPanel { Spacing = 12 };
        _transcriptScroll = new ScrollViewer
        {
            Content = _transcriptList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _transcriptScroll.ScrollChanged += (_, _) => UpdatePinned();

        _searchBox = new TextBox
        {
            Watermark = "Search transcript  ( / )",
            FontSize = 14,
            Foreground = new SolidColorBrush(Ink),
            Background = new SolidColorBrush(HeaderBg),
            BorderBrush = new SolidColorBrush(Line),
            Padding = new Thickness(8, 8, 8, 8),
            MinWidth = 200,
        };
        _searchBox.TextChanged += (_, _) => ApplySearch();

        _resumeButton = new Button
        {
            Content = "Resume live (L)",
            FontSize = 13.5,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Green),
            Background = new SolidColorBrush(GreenBg),
            BorderBrush = new SolidColorBrush(GreenLine),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 7, 16, 7),
            IsVisible = false,
        };
        _resumeButton.Click += (_, _) => ResumeLive();

        _pauseButton = new Button
        {
            Content = "Pause",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(RecRed),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(20, 8, 20, 8),
        };
        _pauseButton.Click += (_, _) => ToggleListening();

        _summaryTail = NewText(12.5, Secondary, FontWeight.SemiBold);
        _cardsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        _statusLine = NewText(14, Secondary, FontWeight.Normal);

        Content = BuildLayout();

        KeyDown += OnKeyDown;
        Closed += (_, _) =>
        {
            _shutdown.Cancel();
            _orchestrator.Dispose();
            _session.Dispose();
            _shutdown.Dispose();
        };

        _clockTick.Tick += (_, _) => TickClock();
        _clockTick.Start();
        _blinkTick.Tick += (_, _) => TickBlink();
        _summaryPoll.Tick += (_, _) => PollSummary();
        _summaryPoll.Start();

        TickClock();
        UpdatePauseUi(true);
        LoadDevices();
        RenderCards();
        SetStatus(DescribeStartup());

        _session.Start();

        if (Program.DemoMode)
        {
            _ = RunDemoAsync();
        }
    }

    private Control BuildLayout()
    {
        var root = new DockPanel { LastChildFill = true };

        var title = new Border
        {
            Background = new SolidColorBrush(HeaderBg),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 0, 18, 0),
            Height = 52,
        };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var appName = new TextBlock { FontSize = 16, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Ink), VerticalAlignment = VerticalAlignment.Center };
        var appInlines = appName.Inlines;
        if (appInlines is not null)
        {
            appInlines.Add(new Run("Meeting ") { Foreground = new SolidColorBrush(Ink) });
            appInlines.Add(new Run("Transcriber") { Foreground = new SolidColorBrush(Accent) });
        }
        else
        {
            appName.Text = "Meeting Transcriber";
        }
        var recPill = new Border
        {
            Background = new SolidColorBrush(GreenBg),
            BorderBrush = new SolidColorBrush(GreenLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(13, 4, 13, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var recRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        recRow.Children.Add(_recDot);
        recRow.Children.Add(_recLabel);
        recPill.Child = recRow;
        var meetingName = NewText(13, Secondary, FontWeight.Normal);
        meetingName.Text = "Fabric migration standup";
        meetingName.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(appName);
        titleRow.Children.Add(recPill);
        titleRow.Children.Add(meetingName);
        title.Child = titleRow;
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        var devbar = new Border
        {
            Background = new SolidColorBrush(Bg),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 10, 18, 10),
        };
        var devRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        devRow.Children.Add(DeviceLabel("MIC"));
        devRow.Children.Add(_micPicker);
        devRow.Children.Add(DeviceLabel("SYSTEM"));
        devRow.Children.Add(_outputPicker);
        devRow.Children.Add(_pauseButton);
        var settingsButton = new Button
        {
            Content = "Settings",
            FontSize = 14,
            Foreground = new SolidColorBrush(Ink),
            Background = new SolidColorBrush(HeaderBg),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 8, 16, 8),
        };
        settingsButton.Click += (_, _) => new SettingsWindow(_settings, _session).Show(this);
        devRow.Children.Add(settingsButton);
        var hint = NewText(13, Secondary, FontWeight.Normal);
        hint.Text = "R pause   / search   L live   C copy   Esc dismiss";
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        devRow.Children.Add(new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Stretch });
        devRow.Children.Add(hint);
        devbar.Child = devRow;
        DockPanel.SetDock(devbar, Dock.Top);
        root.Children.Add(devbar);

        DockPanel.SetDock(_alertStrip, Dock.Top);
        root.Children.Add(_alertStrip);

        var summary = new Border
        {
            Background = new SolidColorBrush(HeaderBg),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 12, 18, 14),
        };
        var summaryStack = new StackPanel { Spacing = 10 };
        var summaryHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var summaryTitle = NewText(12.5, Secondary, FontWeight.Bold);
        summaryTitle.Text = "DISCUSSION SO FAR";
        summaryHead.Children.Add(summaryTitle);
        var tailWrap = new Border { HorizontalAlignment = HorizontalAlignment.Right };
        tailWrap.Child = _summaryTail;
        summaryHead.Children.Add(new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Stretch });
        summaryHead.Children.Add(tailWrap);
        summaryStack.Children.Add(summaryHead);
        summaryStack.Children.Add(_cardsGrid);
        summary.Child = summaryStack;
        DockPanel.SetDock(summary, Dock.Bottom);
        root.Children.Add(summary);

        var statusWrap = new Border
        {
            Background = new SolidColorBrush(HeaderBg),
            Padding = new Thickness(18, 6, 18, 8),
        };
        statusWrap.Child = _statusLine;
        DockPanel.SetDock(statusWrap, Dock.Bottom);
        root.Children.Add(statusWrap);

        var foot = new Border { Padding = new Thickness(18, 10, 18, 12) };
        var footRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _searchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        footRow.Children.Add(_searchBox);
        footRow.Children.Add(_resumeButton);
        foot.Child = footRow;
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        var transWrap = new Border { Padding = new Thickness(18, 16, 18, 6) };
        transWrap.Child = _transcriptScroll;
        root.Children.Add(transWrap);

        return root;
    }

    private Border BuildAlertStrip(
        out TextBlock badge,
        out TextBlock badgeTime,
        out SelectableTextBlock quote,
        out TextBlock ask,
        out TextBlock reply,
        out TextBlock meta)
    {
        var strip = new Border
        {
            Background = new SolidColorBrush(AlertBg),
            BorderBrush = new SolidColorBrush(Orange),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(18, 12, 18, 12),
            IsVisible = false,
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 16,
        };

        var badgeBox = new Border
        {
            Background = new SolidColorBrush(Orange),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var badgeStack = new StackPanel { Spacing = 2 };
        badge = new TextBlock
        {
            Text = "GIANG, YOUR NAME",
            FontSize = 14,
            FontWeight = FontWeight.ExtraBold,
            Foreground = new SolidColorBrush(DarkInk),
            TextWrapping = TextWrapping.Wrap,
        };
        badgeTime = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(DarkInk),
            FontFamily = new FontFamily("ui-monospace,Cascadia Mono,Consolas"),
        };
        badgeStack.Children.Add(badge);
        badgeStack.Children.Add(badgeTime);
        badgeBox.Child = badgeStack;
        Grid.SetColumn(badgeBox, 0);
        grid.Children.Add(badgeBox);

        var middle = new StackPanel { Spacing = 5 };
        quote = new SelectableTextBlock
        {
            FontSize = 15,
            LineHeight = 23,
            Foreground = new SolidColorBrush(QuoteInk),
            TextWrapping = TextWrapping.Wrap,
        };
        ask = NewText(14.5, AskInk, FontWeight.Normal);
        ask.LineHeight = 22;
        ask.TextWrapping = TextWrapping.Wrap;
        reply = NewText(14.5, QuoteInk, FontWeight.Normal);
        reply.LineHeight = 22;
        reply.TextWrapping = TextWrapping.Wrap;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var copyButton = new Button
        {
            Content = "Copy reply (C)",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(DarkInk),
            Background = new SolidColorBrush(Orange),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(18, 8, 18, 8),
        };
        copyButton.Click += (_, _) => _ = CopyReplyAsync();
        var dismissButton = new Button
        {
            Content = "Dismiss (Esc)",
            FontSize = 14,
            Foreground = new SolidColorBrush(AskInk),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(HlLine),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 8, 18, 8),
        };
        dismissButton.Click += (_, _) => DismissAlert();
        actions.Children.Add(copyButton);
        actions.Children.Add(dismissButton);
        middle.Children.Add(quote);
        middle.Children.Add(ask);
        middle.Children.Add(reply);
        middle.Children.Add(actions);
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        meta = NewText(13, StackInk, FontWeight.Normal);
        meta.TextWrapping = TextWrapping.Wrap;
        var aside = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        aside.Children.Add(meta);
        Grid.SetColumn(aside, 2);
        grid.Children.Add(aside);

        strip.Child = grid;
        return strip;
    }

    private void OnUtterance(TranscriptUtterance utterance)
    {
        AddRow(utterance);
        var alert = _orchestrator.AddUtterance(utterance.Feed, utterance.Text, utterance.At);
        if (alert is not null)
        {
            _activeDetection = alert;
            _triggerIds.Add(alert.Trigger.Id);
            RestyleRow(alert.Trigger.Id);
            ShowAlert(alert, canned: null);
        }

        if (_orchestrator.IsSummaryDue())
        {
            _ = RegenSummaryAsync();
        }
    }

    private void ShowAlert(MeetingAlert alert, AlertContent? canned)
    {
        _alertBadge.Text = "GIANG, YOUR NAME";
        _alertTime.Text = alert.Trigger.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var quote = AlertWindow.FormatQuote(alert.Quote);
        _alertQuote.Text = "\"" + quote + "\"";
        if (canned is not null)
        {
            ApplyAlertContent(canned with { Quote = quote });
        }
        else
        {
            _alertAsk.Text = "Working out what they are asking…";
            _alertReply.Text = "Drafting a suggested reply…";
            _ = ResolveAlertAsync(alert);
        }

        _alertMeta.Text = alert.EarlierCount > 0
            ? "+" + alert.EarlierCount.ToString(CultureInfo.InvariantCulture) + " earlier : " +
              alert.Trigger.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : "First mention : " + alert.Trigger.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _alertStrip.IsVisible = true;
    }

    private async Task ResolveAlertAsync(MeetingAlert alert)
    {
        try
        {
            var content = await _orchestrator
                .ResolveAlertAsync(alert.Quote, _shutdown.Token)
                .ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_activeDetection?.Trigger.Id == alert.Trigger.Id)
                {
                    ApplyAlertContent(content);
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyAlertContent(AlertContent content)
    {
        _activeAlert = content;
        var askPrefix = new Run("What they are asking: ") { FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Ink) };
        var askInlines = _alertAsk.Inlines;
        if (askInlines is not null)
        {
            askInlines.Clear();
            askInlines.Add(askPrefix);
            askInlines.Add(new Run(content.AskLine));
        }
        else
        {
            _alertAsk.Text = "What they are asking: " + content.AskLine;
        }

        var replyPrefix = new Run("Suggested reply: ") { FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Green) };
        var replyInlines = _alertReply.Inlines;
        if (replyInlines is not null)
        {
            replyInlines.Clear();
            replyInlines.Add(replyPrefix);
            replyInlines.Add(new Run(content.ReplyLine + "   C copy   Esc dismiss"));
        }
        else
        {
            _alertReply.Text = "Suggested reply: " + content.ReplyLine;
        }

        if (content.Notice is not null)
        {
            SetStatus("Name alert: " + content.Notice);
        }
    }

    private void DismissAlert()
    {
        _alertStrip.IsVisible = false;
        _activeAlert = null;
        _activeDetection = null;
        _orchestrator.DismissAlert();
        _searchBox.Focus();
    }

    private async Task CopyReplyAsync()
    {
        if (_activeAlert is null)
        {
            SetStatus("Nothing to copy: no name alert on screen.");
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            SetStatus("Copy failed: no clipboard on this host.");
            return;
        }

        await clipboard.SetTextAsync(_activeAlert.ReplyLine);
        SetStatus("Suggested reply copied.");
    }

    private void AddRow(TranscriptUtterance utterance)
    {
        var container = new Border
        {
            Padding = new Thickness(8, 8, 8, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("76,Auto,*"),
            ColumnSpacing = 14,
        };
        var time = new TextBlock
        {
            Text = utterance.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Secondary),
            FontFamily = new FontFamily("ui-monospace,Cascadia Mono,Consolas"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var isYou = utterance.Feed == FeedKind.You;
        var tag = new Border
        {
            Background = new SolidColorBrush(isYou ? YouBg : ThemBg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Top,
        };
        tag.Child = new TextBlock
        {
            Text = isYou ? "YOU" : "THEM",
            FontSize = 12,
            FontWeight = FontWeight.ExtraBold,
            Foreground = new SolidColorBrush(isYou ? Accent : ThemInk),
        };
        var text = new TextBlock
        {
            Text = utterance.Text,
            FontSize = 15,
            LineHeight = 24,
            Foreground = new SolidColorBrush(RowInk),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(time, 0);
        Grid.SetColumn(tag, 1);
        Grid.SetColumn(text, 2);
        grid.Children.Add(time);
        grid.Children.Add(tag);
        grid.Children.Add(text);
        container.Child = grid;

        _transcriptList.Children.Add(container);
        _rows.Add(new RowEntry(utterance, container, text));
        while (_rows.Count > 500)
        {
            _transcriptList.Children.RemoveAt(0);
            _rows.RemoveAt(0);
        }

        RestyleRow(utterance.Id);
        ApplySearchToRow(_rows[^1]);
        if (_pinnedToLive)
        {
            Dispatcher.UIThread.Post(() => _transcriptScroll.ScrollToEnd(), DispatcherPriority.Background);
        }
        else
        {
            UpdatePinned();
        }
    }

    private void RestyleRow(Guid id)
    {
        foreach (var row in _rows)
        {
            if (row.Utterance.Id != id)
            {
                continue;
            }

            if (_triggerIds.Contains(id))
            {
                row.Container.Background = new SolidColorBrush(HlBg);
                row.Container.BorderBrush = new SolidColorBrush(HlLine);
            }
        }
    }

    private void ApplySearch()
    {
        foreach (var row in _rows)
        {
            ApplySearchToRow(row);
        }
    }

    private void ApplySearchToRow(RowEntry row)
    {
        var query = _searchBox.Text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            row.Container.IsVisible = true;
            if (!_triggerIds.Contains(row.Utterance.Id))
            {
                row.Container.Background = Brushes.Transparent;
                row.Container.BorderBrush = Brushes.Transparent;
            }

            return;
        }

        var hit = row.Utterance.Text.Contains(query, StringComparison.OrdinalIgnoreCase);
        row.Container.IsVisible = hit;
        if (hit && !_triggerIds.Contains(row.Utterance.Id))
        {
            row.Container.Background = new SolidColorBrush(MatchBg);
            row.Container.BorderBrush = Brushes.Transparent;
        }
    }

    private void UpdatePinned()
    {
        var max = _transcriptScroll.Extent.Height - _transcriptScroll.Viewport.Height;
        _pinnedToLive = _transcriptScroll.Offset.Y >= max - 40;
        _resumeButton.IsVisible = !_pinnedToLive;
    }

    private void ResumeLive()
    {
        _pinnedToLive = true;
        _transcriptScroll.ScrollToEnd();
        _resumeButton.IsVisible = false;
    }

    private async Task RegenSummaryAsync()
    {
        if (_summaryInFlight)
        {
            return;
        }

        _summaryInFlight = true;
        UpdateSummaryTail();
        try
        {
            var update = await _orchestrator
                .RegenerateSummaryAsync(_cards, _shutdown.Token)
                .ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _cards.Clear();
                _cards.AddRange(update.Cards);
                _summaryUpdatedAt = update.UpdatedAt;
                _summaryInFlight = false;
                if (update.Notice is not null)
                {
                    SetStatus(update.Notice);
                }

                RenderCards();
            });
        }
        catch (OperationCanceledException)
        {
            _summaryInFlight = false;
        }
    }

    private void PollSummary()
    {
        if (_orchestrator.IsSummaryDue())
        {
            _ = RegenSummaryAsync();
        }
    }

    private void RenderCards()
    {
        _cardsGrid.Children.Clear();
        if (_cards.Count == 0 && !_summaryInFlight)
        {
            var empty = SummaryCard("STANDBY", "Summary appears here once the transcript has a few lines.");
            Grid.SetColumn(empty, 0);
            _cardsGrid.Children.Add(empty);
            UpdateSummaryTail();
            return;
        }

        var shown = Math.Min(_cards.Count, 4);
        for (var i = 0; i < shown; i++)
        {
            var border = SummaryCard(_cards[i].Label, _cards[i].Line);
            if (_cards[i].Label.Contains("GIANG", StringComparison.OrdinalIgnoreCase))
            {
                border.BorderBrush = new SolidColorBrush(Orange);
            }

            Grid.SetColumn(border, i);
            _cardsGrid.Children.Add(border);
        }

        if (_summaryInFlight)
        {
            if (_cardsGrid.Children.Count < 4)
            {
                _blinkCard = SummaryCard("WORKING", "Updating summary from new lines…");
                Grid.SetColumn(_blinkCard, _cardsGrid.Children.Count);
                _cardsGrid.Children.Add(_blinkCard);
                _blinkTick.Start();
            }
            else if (_cardsGrid.Children.Count > 0)
            {
                _blinkCard = _cardsGrid.Children[^1] as Border;
                _blinkTick.Start();
            }
        }
        else
        {
            _blinkTick.Stop();
            _blinkCard = null;
        }

        UpdateSummaryTail();
    }

    private Border SummaryCard(string label, string line)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Bg),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 12, 0),
        };
        var stack = new StackPanel { Spacing = 3 };
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Ink),
        };
        var lineBlock = new TextBlock
        {
            Text = line,
            FontSize = 14.5,
            LineHeight = 22,
            Foreground = new SolidColorBrush(RowInk),
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(labelBlock);
        stack.Children.Add(lineBlock);
        border.Child = stack;
        return border;
    }

    private void UpdateSummaryTail()
    {
        if (_summaryInFlight)
        {
            var stamp = _summaryUpdatedAt == default
                ? "starting"
                : "updated " + _summaryUpdatedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _summaryTail.Text = stamp + " : updating…";
        }
        else if (_cards.Count > 0)
        {
            _summaryTail.Text = "updated " +
                _summaryUpdatedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }
        else
        {
            _summaryTail.Text = "waiting for transcript";
        }
    }

    private void TickBlink()
    {
        _blinkOn = !_blinkOn;
        if (_blinkCard is not null)
        {
            _blinkCard.Opacity = _blinkOn ? 1.0 : 0.45;
        }
    }

    private void TickClock()
    {
        var elapsed = _sessionClock.Elapsed;
        var stamp = elapsed.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
            elapsed.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
            elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture);
        _recLabel.Text = (_session.Listening ? "REC " : "HELD ") + stamp;
        _recDot.Background = new SolidColorBrush(_session.Listening ? RecRed : Secondary);
    }

    private void ToggleListening()
    {
        var on = _session.ToggleListening();
        UpdatePauseUi(on);
        SetStatus(on ? "Listening on both feeds." : "Paused. Press R to resume.");
    }

    private void UpdatePauseUi(bool on)
    {
        _pauseButton.Content = on ? "Pause" : "Resume";
        TickClock();
    }

    private void SetStatus(string message) => _statusLine.Text = message;

    private string DescribeStartup()
    {
        if (Program.DemoMode)
        {
            return "Demo session: Fabric standup sample, no devices needed.";
        }

        if (!Platform.Devices.IsAvailable)
        {
            return "Audio devices unavailable: Meeting Transcriber needs the Windows platform layer.";
        }

        if (_settings.Data.Backend == "Codex" && !Cloud.CodexLogin.IsSignedIn)
        {
            return "Not signed in to ChatGPT: Settings then Sign in, or pick a key backend.";
        }

        return "Listening on both feeds: new lines arrive below.";
    }

    private void LoadDevices()
    {
        _micIds.Clear();
        _outputIds.Clear();
        var mics = Platform.Devices.ListCaptureDevices();
        var renders = Platform.Devices.ListRenderDevices();

        var micNames = new List<string>(mics.Count);
        foreach (var device in mics)
        {
            _micIds.Add(device.Id);
            micNames.Add(device.Name + (device.IsDefault ? " (default)" : string.Empty));
        }

        if (micNames.Count == 0)
        {
            micNames.Add("No mic found: Windows build needed");
            _micIds.Add(null);
        }

        var renderNames = new List<string>(renders.Count);
        foreach (var device in renders)
        {
            _outputIds.Add(device.Id);
            renderNames.Add(device.Name + (device.IsDefault ? " (default)" : string.Empty));
        }

        if (renderNames.Count == 0)
        {
            renderNames.Add("No output found: Windows build needed");
            _outputIds.Add(null);
        }

        _micPicker.ItemsSource = micNames;
        _outputPicker.ItemsSource = renderNames;
        RefreshPickersSelection();
        RefreshPickerTooltips();

        _micPicker.SelectionChanged += MicChanged;
        _outputPicker.SelectionChanged += OutputChanged;
    }

    /// <summary>Shows the full device name on hover when the picker truncates it.</summary>
    private void RefreshPickerTooltips()
    {
        ToolTip.SetTip(_micPicker, _micPicker.SelectedItem?.ToString());
        ToolTip.SetTip(_outputPicker, _outputPicker.SelectedItem?.ToString());
    }

    private void RefreshPickersSelection()
    {
        _micPicker.SelectionChanged -= MicChanged;
        _outputPicker.SelectionChanged -= OutputChanged;
        _micPicker.SelectedIndex = PickIndex(_micIds, _settings.Data.MicDeviceId);
        _outputPicker.SelectedIndex = PickIndex(_outputIds, _settings.Data.OutputDeviceId);
        _micPicker.SelectionChanged += MicChanged;
        _outputPicker.SelectionChanged += OutputChanged;
    }

    private void MicChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshPickerTooltips();
        ApplyDevice(mic: true);
    }

    private void OutputChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshPickerTooltips();
        ApplyDevice(mic: false);
    }

    private static int PickIndex(List<string?> ids, string? current)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], current, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private void ApplyDevice(bool mic)
    {
        var picker = mic ? _micPicker : _outputPicker;
        var ids = mic ? _micIds : _outputIds;
        if (picker.SelectedIndex < 0 || picker.SelectedIndex >= ids.Count)
        {
            return;
        }

        var id = ids[picker.SelectedIndex];
        var data = mic
            ? _settings.Data with { MicDeviceId = id }
            : _settings.Data with { OutputDeviceId = id };
        _settings.Update(data);
        _session.ConfigureDevices(_settings.Data.MicDeviceId, _settings.Data.OutputDeviceId);
        SetStatus((mic ? "MIC" : "SYSTEM") + " feed switching to the selected device.");
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (_searchBox.IsFocused)
        {
            if (e.Key == Key.Escape)
            {
                _searchBox.Text = string.Empty;
                _searchBox.Focusable = false;
                Focus();
                _searchBox.Focusable = true;
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.R:
                ToggleListening();
                e.Handled = true;
                break;
            case Key.L:
                ResumeLive();
                e.Handled = true;
                break;
            case Key.C:
                _ = CopyReplyAsync();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_alertStrip.IsVisible)
                {
                    DismissAlert();
                    e.Handled = true;
                }

                break;
            default:
                if (string.Equals(e.KeySymbol, "/", StringComparison.Ordinal))
                {
                    _searchBox.Focus();
                    _searchBox.SelectAll();
                    e.Handled = true;
                }

                break;
        }
    }

    private static ComboBox NewPicker() => new()
    {
        Background = new SolidColorBrush(HeaderBg),
        Foreground = new SolidColorBrush(Ink),
        BorderBrush = new SolidColorBrush(Line),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        FontSize = 14,
        Padding = new Thickness(10, 7, 10, 7),
        MaxWidth = 340,
        MinWidth = 180,
    };

    private static TextBlock DeviceLabel(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        FontWeight = FontWeight.Bold,
        Foreground = new SolidColorBrush(Secondary),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock NewText(double size, Color color, FontWeight weight) => new()
    {
        FontSize = size,
        Foreground = new SolidColorBrush(color),
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
    };

    private async Task RunDemoAsync()
    {
        var now = DateTimeOffset.Now;
        var seed = new List<TranscriptUtterance>
        {
            new(Guid.NewGuid(), now.AddMinutes(-4).AddSeconds(-28), FeedKind.Them, "Morning all. Bridge tables for orders to customers are mapped. One open item left on duplicate keys in the legacy export."),
            new(Guid.NewGuid(), now.AddMinutes(-4).AddSeconds(-10), FeedKind.You, "SCD2 on customer address is in. Effective from and to dates plus the current flag, all tested."),
            new(Guid.NewGuid(), now.AddMinutes(-3).AddSeconds(-25), FeedKind.Them, "MERGE strategy is agreed. Match on order id plus line number. Update on price change only, inserts for the rest."),
            new(Guid.NewGuid(), now.AddMinutes(-2).AddSeconds(-50), FeedKind.Them, "Backfill runs tonight. About 40 minutes for 18 months of history, then we validate row counts in the morning."),
        };
        foreach (var utterance in seed)
        {
            OnUtterance(utterance);
            await Task.Delay(350, _shutdown.Token).ConfigureAwait(true);
        }

        _orchestrator.Mentions.Record(now.AddMinutes(-4).AddSeconds(-46));

        _cards.Clear();
        _cards.Add(new SummaryCard("BRIDGE", "Mapped, one duplicate key item open."));
        _cards.Add(new SummaryCard("SCD2", "Customer address live and tested."));
        _cards.Add(new SummaryCard("MERGE", "Match id plus line no, price only updates."));
        _cards.Add(new SummaryCard("BACKFILL", "Tonight, 40 min, counts in the morning."));
        _summaryUpdatedAt = now;
        RenderCards();

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), _shutdown.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var trigger = new TranscriptUtterance(
            Guid.NewGuid(), DateTimeOffset.Now, FeedKind.Them,
            "What do you think Giang? Should we keep the late arriving fact window at 3 days or cut it to 1?");
        AddRow(trigger);
        var alert = _orchestrator.AddUtterance(trigger.Feed, trigger.Text, trigger.At);
        if (alert is null)
        {
            return;
        }

        _activeDetection = alert;
        _triggerIds.Add(alert.Trigger.Id);
        RestyleRow(alert.Trigger.Id);
        var quote = AlertWindow.FormatQuote(alert.Quote);
        ShowAlert(alert, new AlertContent(
            quote,
            "Marco wants your call on the late fact window before tonight's backfill.",
            "Keep 3 days for this run, review after counts are clean.",
            null));
        _cards.Clear();
        _cards.Add(new SummaryCard("BRIDGE", "Mapped, one duplicate key item open."));
        _cards.Add(new SummaryCard("SCD2", "Customer address live and tested."));
        _cards.Add(new SummaryCard("MERGE", "Match id plus line no, price only updates."));
        _cards.Add(new SummaryCard("OPEN FOR GIANG", "Late fact window: 3 days or 1?"));
        _summaryUpdatedAt = DateTimeOffset.Now;
        RenderCards();
    }
}
