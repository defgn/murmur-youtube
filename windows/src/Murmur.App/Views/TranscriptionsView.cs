using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// Past transcriptions: searchable, each copyable and deletable.
/// </summary>
/// <remarks>
/// Rows show which dictionary corrections fired. Without that the dictionary is invisible and
/// there is no way to tell a rule that works from one that never matches.
/// </remarks>
public sealed class TranscriptionsView : UserControl
{
    private readonly TranscriptStore _store;
    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly TextBlock _count;
    private Border? _liveCard;
    private TextBlock _liveText = null!;

    /// <summary>Builds the view over <paramref name="store"/>.</summary>
    public TranscriptionsView(TranscriptStore store)
    {
        _store = store;

        _search = Panels.SearchBox("Search transcriptions");
        _search.TextChanged += (_, _) => Refresh();

        _list = new StackPanel { Spacing = Tokens.Space.Snug, Margin = new Thickness(Tokens.Space.Base) };
        _count = Panels.Caption("0 recordings");

        var clear = Panels.Button("Delete all");
        clear.Click += (_, _) => { _store.Clear(); Refresh(); };

        Content = new DockPanel
        {
            Children =
            {
                Panels.Docked(Panels.SearchRow(_search), Dock.Top),
                Panels.Docked(Panels.Footer(_count, clear), Dock.Bottom),
                new ScrollViewer { Content = _list },
            },
        };

        _store.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        var records = _store.Search(_search.Text ?? string.Empty);

        _list.Children.Clear();
        _count.Text = $"{_store.Records.Count} recording{(_store.Records.Count == 1 ? "" : "s")}";

        if (records.Count == 0)
        {
            _list.Children.Add(Panels.EmptyState(
                _store.Records.Count == 0 ? "No recordings" : "No matches",
                _store.Records.Count == 0
                    ? "Click the pill and speak."
                    : "Try a different search."));
            return;
        }

        foreach (var record in records) _list.Children.Add(BuildRow(record));
    }

    /// <summary>
    /// Shows the live preview card with the latest partial transcript. While the card is up
    /// the list shows nothing else — the recording has not produced a finished transcript yet.
    /// </summary>
    public void ShowLive(string text)
    {
        _liveCard ??= BuildLiveCard();
        _liveText.Text = text;

        _list.Children.Clear();
        _list.Children.Add(_liveCard);
    }

    /// <summary>Removes the live preview card; the store now holds the finished transcript.</summary>
    public void ClearLive()
    {
        if (_liveCard is null || !_list.Children.Contains(_liveCard)) return;
        Refresh();
    }

    /// <summary>The card shown while the key is held: a red lamp and what the engine hears.</summary>
    private Border BuildLiveCard()
    {
        var lamp = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Tokens.Brushes.Record,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children =
            {
                lamp,
                Panels.Caption("LISTENING"),
            },
        };

        _liveText = new TextBlock
        {
            Text = string.Empty,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.BodyLarge,
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 1.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var body = new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children = { header, _liveText },
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0x7A, 0x1F, 0x2E)),
            CornerRadius = new CornerRadius(Tokens.Radius.Panel),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xE1, 0x1D, 0x48)),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            Padding = new Thickness(Tokens.Space.Roomy),
            Child = body,
        };
    }

    private Border BuildRow(TranscriptRecord record)
    {
        var copy = Panels.Button("Copy");
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(record.Text).ConfigureAwait(true);

            copy.Content = "Copied";
            await Task.Delay(TimeSpan.FromSeconds(1.4)).ConfigureAwait(true);
            copy.Content = "Copy";
        };

        var delete = Panels.Button("Delete");
        delete.Click += (_, _) => _store.Remove(record.Id);

        var header = new DockPanel();
        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Panels.Caption(record.At.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)),
                new TextBlock
                {
                    Text = record.ProcessingSeconds.ToString("0.0s", CultureInfo.CurrentCulture),
                    FontFamily = Tokens.Fonts.Mono,
                    FontSize = Tokens.Fonts.Caption,
                    Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary, 0.8),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };
        header.Children.Add(meta);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Tight,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { copy, delete },
        };

        // Docked first; the LAST child (meta) fills the remaining space.
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(meta);

        var body = new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                header,
                new TextBlock
                {
                    Text = record.Text,
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.BodyLarge,
                    Foreground = Tokens.Brushes.Ink,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 1.5,
                },
            },
        };

        if (record.Corrections is { Count: > 0 } corrections)
        {
            body.Children.Add(BuildCorrectionBadges(corrections));
        }

        return Panels.Card(body);
    }

    /// <summary>Shows that the dictionary fired, and on what.</summary>
    private static WrapPanel BuildCorrectionBadges(IReadOnlyList<Dictionary.AppliedCorrection> corrections)
    {
        var row = new WrapPanel { ItemSpacing = Tokens.Space.Snug, LineSpacing = Tokens.Space.Tight };

        foreach (var correction in corrections)
        {
            var label = correction.Count > 1
                ? $"{correction.From} → {correction.To} ×{correction.Count}"
                : $"{correction.From} → {correction.To}";

            row.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xC0, 0x5B, 0x34)),
                CornerRadius = new CornerRadius(Tokens.Radius.Pill),
                Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Hair),
                Child = new TextBlock
                {
                    Text = label,
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Caption,
                    Foreground = new SolidColorBrush(Tokens.Colors.Accent),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }

        return row;
    }
}
