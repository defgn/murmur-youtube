using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Murmur.App.Design;
using Murmur.Core;
using Murmur.Dictionary;

namespace Murmur.App.Views;

/// <summary>
/// The dictionary: add, edit, delete, search.
/// </summary>
/// <remarks>
/// Both entry kinds live in one list rather than separate tabs — they are two shapes of the
/// same idea, and you want to see everything you have taught it at once. The kind is carried
/// by a small tag on each row.
/// </remarks>
public sealed class DictionaryView : UserControl
{
    private readonly DictionaryFile _file;
    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly TextBlock _count;

    /// <summary>Builds the view over <paramref name="file"/>.</summary>
    public DictionaryView(DictionaryFile file)
    {
        _file = file;

        _search = Panels.SearchBox("Search dictionary");
        _search.TextChanged += (_, _) => Refresh();

        var add = Panels.Button("Add");
        add.Click += (_, _) => ShowEditor(null);

        _list = new StackPanel { Spacing = Tokens.Space.Snug, Margin = new Thickness(Tokens.Space.Base) };
        _count = Panels.Caption("0 entries");

        var reveal = Panels.Button("Open dictionary.txt");
        reveal.Click += (_, _) => OpenInEditor(_file.FilePath);

        Content = new DockPanel
        {
            Children =
            {
                Panels.Docked(Panels.SearchRow(_search, add), Dock.Top),
                Panels.Docked(Panels.Footer(_count, reveal), Dock.Bottom),
                new ScrollViewer { Content = _list },
            },
        };

        _file.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        var entries = _file.Search(_search.Text ?? string.Empty);

        _list.Children.Clear();
        _count.Text = $"{_file.Entries.Count} entr{(_file.Entries.Count == 1 ? "y" : "ies")}";

        if (entries.Count == 0)
        {
            _list.Children.Add(Panels.EmptyState(
                _file.Entries.Count == 0 ? "Dictionary empty" : "No matches",
                _file.Entries.Count == 0
                    ? "Add words it keeps getting wrong."
                    : "Try a different search."));
            return;
        }

        foreach (var entry in entries) _list.Children.Add(BuildRow(entry));
    }

    private Border BuildRow(DictionaryEntry entry)
    {
        var edit = Panels.Button("Edit");
        edit.Click += (_, _) => ShowEditor(entry);

        var delete = Panels.Button("Delete");
        delete.Click += (_, _) => _file.Remove(entry.Id);

        var toggle = new ToggleSwitch
        {
            IsChecked = entry.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.IsCheckedChanged += (_, _) => _file.Update(entry with { IsEnabled = toggle.IsChecked ?? false });

        var kind = Panels.Caption(entry.Kind == EntryKind.Correction ? "FIX" : "TERM");

        var word = new TextBlock
        {
            Text = entry.Kind == EntryKind.Correction
                ? $"{entry.Hear}  →  {entry.Write}"
                : entry.Write,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            FontWeight = FontWeight.Medium,
            Foreground = Tokens.Brushes.Ink,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { kind, word },
        };

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { toggle, edit, delete },
        };

        return new Border
        {
            Background = entry.IsEnabled
                ? Tokens.Brushes.Glass
                : new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(Tokens.Radius.Panel),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Base),
            Opacity = entry.IsEnabled ? 1 : 0.55,
            Child = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                Children = { left, right },
            },
        };
    }

    private void ShowEditor(DictionaryEntry? entry)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var editor = new DictionaryEditorWindow(entry);
        editor.Saved += (_, saved) =>
        {
            if (entry is null) _file.Add(saved); else _file.Update(saved);
        };

        _ = editor.ShowDialog(owner);
    }

    /// <summary>Opens the dictionary in the user's default text editor.</summary>
    private static void OpenInEditor(string path)
    {
        try
        {
            // The file must exist before the shell will open it — a brand-new install has
            // never saved one.
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Empty);
            }

            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            // Opening an editor is a convenience; failing to must never take the app down.
        }
    }
}

/// <summary>
/// Add or edit one dictionary entry, with the false-positive warning shown live.
/// </summary>
public sealed class DictionaryEditorWindow : Window
{
    private readonly Button _termKey;
    private readonly Button _correctionKey;
    private readonly TextBox _hear;
    private readonly TextBox _write;
    private readonly StackPanel _warnings;
    private readonly Button _save;
    private readonly Guid _id;
    private readonly bool _wasEnabled;

    private EntryKind _kind;

    /// <summary>Raised when the user saves.</summary>
    public event EventHandler<DictionaryEntry>? Saved;

    /// <summary>Creates the editor for a new or existing entry.</summary>
    public DictionaryEditorWindow(DictionaryEntry? entry)
    {
        _id = entry?.Id ?? Guid.NewGuid();
        _wasEnabled = entry?.IsEnabled ?? true;
        _kind = entry?.Kind ?? EntryKind.Term;

        Title = entry is null ? "New entry" : "Edit entry";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = Tokens.Brushes.ChassisGradient;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _termKey = Segment("Term");
        _correctionKey = Segment("Correction");
        _termKey.Click += (_, _) => SetKind(EntryKind.Term);
        _correctionKey.Click += (_, _) => SetKind(EntryKind.Correction);

        _hear = Field("cloud code", entry?.Hear ?? string.Empty);
        _write = Field("Claude Code", entry?.Write ?? string.Empty);
        _hear.TextChanged += (_, _) => Revalidate();
        _write.TextChanged += (_, _) => Revalidate();

        _warnings = new StackPanel { Spacing = Tokens.Space.Snug };

        var cancel = Panels.Button("Cancel");
        cancel.Click += (_, _) => Close();

        _save = Panels.PillButton("Save");
        _save.Click += (_, _) =>
        {
            if (!IsValid) return;
            Saved?.Invoke(this, Draft);
            Close();
        };

        Content = BuildContent(cancel);
        SetKind(_kind);
    }

    private DictionaryEntry Draft => new()
    {
        Id = _id,
        Kind = _kind,
        Write = (_write.Text ?? string.Empty).Trim(),
        Hear = _kind == EntryKind.Correction ? (_hear.Text ?? string.Empty).Trim() : string.Empty,
        IsEnabled = _wasEnabled,
    };

    private bool IsValid =>
        Draft.Write.Length > 0 && (_kind == EntryKind.Term || Draft.Hear.Length > 0);

    private static Button Segment(string label) => new()
    {
        Content = label,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Body,
        FontWeight = FontWeight.Medium,
        Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
        CornerRadius = new CornerRadius(Tokens.Radius.Control),
        Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Snug - Tokens.Space.Hair),
    };

    private StackPanel BuildContent(Control cancel)
    {
        var hearField = new StackPanel { Spacing = Tokens.Space.Tight, Children = { Panels.Caption("WHEN YOU HEAR"), _hear } };
        hearField.IsVisible = _kind == EntryKind.Correction;
        _hearField = hearField;

        _writeLabel = Panels.Caption("WORD OR PHRASE");

        return new StackPanel
        {
            Margin = new Thickness(Tokens.Space.Panel),
            Spacing = Tokens.Space.Roomy,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = Tokens.Space.Snug,
                    Children = { _termKey, _correctionKey },
                },
                hearField,
                new StackPanel { Spacing = Tokens.Space.Tight, Children = { _writeLabel, _write } },
                _warnings,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = Tokens.Space.Snug,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, _save },
                },
            },
        };
    }

    private StackPanel? _hearField;
    private TextBlock? _writeLabel;

    private void SetKind(EntryKind kind)
    {
        _kind = kind;
        SetSegment(_termKey, kind == EntryKind.Term);
        SetSegment(_correctionKey, kind == EntryKind.Correction);

        if (_hearField is not null) _hearField.IsVisible = kind == EntryKind.Correction;
        if (_writeLabel is not null) _writeLabel.Text = kind == EntryKind.Correction ? "WRITE" : "WORD OR PHRASE";

        Revalidate();
    }

    private static void SetSegment(Button button, bool engaged)
    {
        button.Foreground = engaged ? Tokens.Brushes.Ink : new SolidColorBrush(Tokens.Colors.InkSecondary);
        button.Background = engaged ? Tokens.Brushes.GlassStrong : Brushes.Transparent;
        button.BorderBrush = engaged ? new SolidColorBrush(Tokens.Colors.Accent) : new SolidColorBrush(Tokens.Colors.Seam);
    }

    private void Revalidate()
    {
        _warnings.Children.Clear();

        foreach (var warning in DictionaryWarning.Check(Draft))
        {
            _warnings.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x26, 0xC9, 0x92, 0x2E)),
                BorderBrush = new SolidColorBrush(Tokens.Colors.MeterAmber, 0.45),
                BorderThickness = new Thickness(Tokens.Border.Hairline),
                CornerRadius = new CornerRadius(Tokens.Radius.Control),
                Padding = new Thickness(Tokens.Space.Base),
                Child = new TextBlock
                {
                    Text = warning.Message,
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Label,
                    Foreground = Tokens.Brushes.Ink,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 340,
                },
            });
        }

        _save.IsEnabled = IsValid;
    }

    private static TextBox Field(string placeholder, string text) => new()
    {
        Text = text,
        Watermark = placeholder,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Body,
        Foreground = Tokens.Brushes.Ink,
        Background = Tokens.Brushes.GlassStrong,
        BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        CornerRadius = new CornerRadius(Tokens.Radius.Control),
        Padding = new Thickness(Tokens.Space.Base),
    };
}
