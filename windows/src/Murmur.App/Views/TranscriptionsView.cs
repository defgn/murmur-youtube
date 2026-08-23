using System.Globalization;
using Avalonia;
using Avalonia.Controls;
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
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);

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
