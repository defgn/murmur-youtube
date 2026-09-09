using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// The Woffle+ panel: live two-sided transcript on the left, the detected question and its
/// AI answer on the right.
/// </summary>
/// <remarks>
/// <para>
/// The layout from the approved mockup: transcript column, answer column with a
/// Full/Short tab pair and a re-draft button, question card flagged in the accent colour.
/// Built in code from <see cref="Tokens"/> like every other view.
/// </para>
/// <para>
/// Answer text is a <see cref="SelectableTextBlock"/> so "copy answer" also works with the
/// mouse, not only the hotkey.
/// </para>
/// </remarks>
public sealed class InterviewView : UserControl
{
    private readonly Composition _composition;

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
    private readonly TextBlock _fullTabLabel;
    private readonly TextBlock _shortTabLabel;
    private readonly TextBlock _statusLine;
    private bool _shortShown;

    /// <summary>Builds the panel over the composition.</summary>
    public InterviewView(Composition composition)
    {
        _composition = composition;

        // ---- left: live transcript ----
        _transcriptList = new StackPanel();
        _transcriptScroll = new ScrollViewer
        {
            Content = _transcriptList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var transcriptPane = new DockPanel();
        transcriptPane.Children.Add(Panels.Docked(
            PaneHeader("LIVE TRANSCRIPT"), Dock.Top));
        transcriptPane.Children.Add(_transcriptScroll);

        // ---- right: question + answer ----
        _questionText = new TextBlock
        {
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.BodyLarge,
            FontWeight = FontWeight.SemiBold,
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.Wrap,
        };

        var questionCard = new Border
        {
            Background = Tokens.Brushes.Panel,
            CornerRadius = new CornerRadius(Tokens.Radius.Control),
            BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            Padding = new Thickness(Tokens.Space.Roomy),
            Child = new StackPanel
            {
                Spacing = Tokens.Space.Snug,
                Children =
                {
                    SmallCaps("DETECTED QUESTION", Tokens.Brushes.Accent),
                    _questionText,
                },
            },
        };

        _answerMeta = new TextBlock
        {
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary, 0.7),
        };

        _fullAnswer = AnswerBlock();
        _shortAnswer = AnswerBlock();

        _fullCard = AnswerCard(SmallCaps("SUGGESTED ANSWER", Tokens.Brushes.Success), _answerMeta, _fullAnswer);
        _shortCard = AnswerCard(SmallCaps("TALKING POINTS", Tokens.Brushes.Success), new TextBlock(), _shortAnswer);

        (_fullTab, _fullTabLabel) = AnswerTab("Full answer");
        (_shortTab, _shortTabLabel) = AnswerTab("Short version");
        _fullTab.PointerPressed += (_, _) => ShowShort(false);
        _shortTab.PointerPressed += (_, _) => ShowShort(true);

        var regen = Panels.Button("Try another angle");
        regen.HorizontalAlignment = HorizontalAlignment.Right;
        regen.Click += (_, _) => _composition.Assistant?.Regenerate();

        var tabBar = new DockPanel();
        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children = { _fullTab, _shortTab },
        };
        DockPanel.SetDock(regen, Dock.Right);
        tabBar.Children.Add(regen);
        tabBar.Children.Add(tabs);

        var answerPane = new DockPanel();
        answerPane.Children.Add(Panels.Docked(PaneHeader("AI ANSWER"), Dock.Top));
        answerPane.Children.Add(Panels.Docked(tabBar, Dock.Top));

        var answerScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var answerStack = new StackPanel
        {
            Spacing = Tokens.Space.Base,
            Margin = new Thickness(Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Base),
            Children = { questionCard, _fullCard, _shortCard },
        };
        answerScroll.Content = answerStack;
        answerPane.Children.Add(answerScroll);

        _statusLine = new TextBlock
        {
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary, 0.8),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Snug),
        };

        // ---- the two columns ----
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(11, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(10, GridUnitType.Star)));
        Grid.SetColumn(transcriptPane, 0);
        grid.Children.Add(transcriptPane);
        Grid.SetColumn(Seam(), 1);
        grid.Children.Add(Seam());
        Grid.SetColumn(answerPane, 2);
        grid.Children.Add(answerPane);

        Content = grid;
        ShowShort(false);

        if (_composition.Assistant is { } coordinator)
        {
            coordinator.QuestionSeen += (_, turn) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AddTurn(turn);
                _questionText.Text = turn.Text;
                _answerMeta.Text = "drafting…";
                _fullAnswer.Text = string.Empty;
                _shortAnswer.Text = string.Empty;
                _statusLine.IsVisible = false;
            });

            coordinator.AnswerArrived += (_, answer) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _answerMeta.Text = answer.Attempt > 1
                    ? $"attempt {answer.Attempt} · drafted in {answer.Drafted.TotalSeconds:0.0}s"
                    : $"drafted in {answer.Drafted.TotalSeconds:0.0}s";
                _fullAnswer.Text = answer.FullAnswer;
                _shortAnswer.Text = answer.ShortAnswer;
            });

            coordinator.AssistantNotice += (_, message) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _statusLine.Text = message;
                _statusLine.IsVisible = true;
            });
        }
    }

    /// <summary>A finished turn in the transcript column; the interviewer's questions are flagged.</summary>
    public void AddTurn(InterviewTurn turn)
    {
        var who = turn.Speaker == Speaker.Interviewer ? "INTERVIEWER" : "YOU";
        var colour = turn.Speaker == Speaker.Interviewer ? Tokens.Brushes.Brand : Tokens.Brushes.Success;

        var row = new StackPanel
        {
            Spacing = Tokens.Space.Tight,
            Margin = new Thickness(Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Base),
            Children =
            {
                SmallCaps(who, colour),
                new TextBlock
                {
                    Text = turn.Text,
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Body,
                    Foreground = Tokens.Brushes.Ink,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        _transcriptList.Children.Add(row);
        _transcriptScroll.ScrollToEnd();
    }

    /// <summary>A transient assistant status under the answer column.</summary>
    public void ShowStatus(string message)
    {
        _statusLine.Text = message;
        _statusLine.IsVisible = true;
    }

    /// <summary>Ctrl+Shift+C: the answer text on the clipboard.</summary>
    public async Task CopyAnswerAsync(TopLevel owner)
    {
        var text = _shortShown ? _shortAnswer.Text : _fullAnswer.Text;
        if (!string.IsNullOrWhiteSpace(text) && owner.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void ShowShort(bool shortShown)
    {
        _shortShown = shortShown;
        _fullCard.IsVisible = !shortShown;
        _shortCard.IsVisible = shortShown;
        StyleTab(_fullTab, _fullTabLabel, engaged: !shortShown);
        StyleTab(_shortTab, _shortTabLabel, engaged: shortShown);
    }

    // ---- helpers ----

    private static Border PaneHeader(string label) => new Border
    {
        Background = Tokens.Brushes.Panel,
        BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
        BorderThickness = new Thickness(0, 0, 0, Tokens.Border.Seam),
        Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Base),
        Child = SmallCaps(label, Tokens.Brushes.Silkscreen),
    };

    private static Border Seam() => new Border
    {
        Width = Tokens.Border.Seam,
        Background = new SolidColorBrush(Tokens.Colors.Seam),
    };

    private static TextBlock SmallCaps(string label, IBrush colour) => new()
    {
        Text = label,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Silkscreen,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = Tokens.Fonts.SilkscreenTracking * 2,
        Foreground = colour,
    };

    private static SelectableTextBlock AnswerBlock() => new()
    {
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Body,
        Foreground = Tokens.Brushes.Ink,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.None,
    };

    private static Border AnswerCard(Control header, Control meta, Control body) => new()
    {
        Background = Tokens.Brushes.Panel,
        CornerRadius = new CornerRadius(Tokens.Radius.Control),
        BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        Padding = new Thickness(Tokens.Space.Roomy),
        Child = new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children = { header, meta, body },
        },
    };

    private static (Border Tab, TextBlock Label) AnswerTab(string label)
    {
        var text = new TextBlock
        {
            Text = label,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tab = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        return (tab, text);
    }

    private static void StyleTab(Border tab, TextBlock label, bool engaged)
    {
        label.FontWeight = engaged ? FontWeight.SemiBold : FontWeight.Normal;
        label.Foreground = engaged ? Tokens.Brushes.Ink : new SolidColorBrush(Tokens.Colors.InkSecondary, 0.8);
        tab.Background = engaged ? Tokens.Brushes.GlassStrong : Brushes.Transparent;
    }
}
