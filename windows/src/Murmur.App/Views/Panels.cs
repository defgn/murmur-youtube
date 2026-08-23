using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.App.Design;

namespace Murmur.App.Views;

/// <summary>
/// Shared furniture — search rows, footers, cards, buttons, empty states.
/// </summary>
/// <remarks>
/// Every value comes from <see cref="Tokens"/>. Factoring these out is what stops the same
/// padding being typed slightly differently in three views, which is how a design system
/// erodes.
/// </remarks>
internal static class Panels
{
    /// <summary>A search field styled for a light card.</summary>
    public static TextBox SearchBox(string placeholder) => new()
    {
        Watermark = placeholder,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Body,
        Foreground = Tokens.Brushes.Ink,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The row a search field sits in, with a seam beneath it.</summary>
    public static Border SearchRow(Control search, Control? trailing = null)
    {
        var row = new DockPanel();

        if (trailing is not null)
        {
            DockPanel.SetDock(trailing, Dock.Right);
            row.Children.Add(trailing);
        }

        row.Children.Add(search);

        return new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
            BorderThickness = new Thickness(0, 0, 0, Tokens.Border.Seam),
            Child = row,
        };
    }

    /// <summary>A footer strip with a count on the left and an action on the right.</summary>
    public static Border Footer(Control leading, Control trailing)
    {
        var row = new DockPanel();
        DockPanel.SetDock(trailing, Dock.Right);
        row.Children.Add(trailing);
        row.Children.Add(leading);

        return new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
            BorderThickness = new Thickness(0, Tokens.Border.Seam, 0, 0),
            Child = row,
        };
    }

    /// <summary>A soft, modern action button.</summary>
    public static Button Button(string label) => new()
    {
        Content = label,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Label,
        FontWeight = FontWeight.Medium,
        Foreground = Tokens.Brushes.Ink,
        Background = Tokens.Brushes.Panel,
        BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        CornerRadius = new CornerRadius(Tokens.Radius.Control),
        Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug - Tokens.Space.Hair),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    /// <summary>A rounded pill button for emphasis — the hero's family.</summary>
    public static Button PillButton(string label) => new()
    {
        Content = label,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Body,
        FontWeight = FontWeight.SemiBold,
        Foreground = Tokens.Brushes.PillInk,
        Background = Tokens.Brushes.DarkPill,
        CornerRadius = new CornerRadius(Tokens.Radius.Pill),
        Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Snug),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>One row of content on a glass card.</summary>
    public static Border Card(Control content, bool strong = false) => new()
    {
        Background = strong ? Tokens.Brushes.GlassStrong : Tokens.Brushes.Glass,
        CornerRadius = new CornerRadius(Tokens.Radius.Panel),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        Padding = new Thickness(Tokens.Space.Roomy),
        Child = content,
    };

    /// <summary>Centred "nothing here yet" copy.</summary>
    public static Control EmptyState(string label, string detail) => new StackPanel
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = Tokens.Space.Snug,
        Margin = new Thickness(0, Tokens.Space.Panel),
        Children =
        {
            new TextBlock
            {
                Text = label,
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Title,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary, 0.75),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
            new TextBlock
            {
                Text = detail,
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Label,
                Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary, 0.6),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        },
    };

    /// <summary>
    /// Docks a control and returns it, so it reads inline in a Children list.
    /// </summary>
    /// <remarks>Not named <c>Dock</c>: that shadows the <see cref="Avalonia.Controls.Dock"/>
    /// enum at every call site.</remarks>
    public static Control Docked(Control control, Dock side)
    {
        DockPanel.SetDock(control, side);
        return control;
    }

    /// <summary>A small-caps label, the modern caption voice.</summary>
    public static TextBlock Caption(string label) => new()
    {
        Text = label,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Silkscreen,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = Tokens.Fonts.SilkscreenTracking,
        Foreground = Tokens.Brushes.Silkscreen,
    };
}
