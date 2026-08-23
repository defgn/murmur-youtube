using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.App.Design;

namespace Murmur.App.Views;

/// <summary>
/// The one-time question: what should the close button do? Asked on the first close, and
/// answered permanently in Settings afterwards.
/// </summary>
public sealed class CloseToTrayDialog : Window
{
    /// <summary>Builds the dialog over <paramref name="owner"/>.</summary>
    public CloseToTrayDialog(Window owner)
    {
        Title = "Woffle";
        Width = 460;
        Height = 264;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.Full;
        Background = Tokens.Brushes.ChassisGradient;

        var message = new TextBlock
        {
            Text = "Keep Woffle running in the background when you close the window?\n\n" +
                   "It will stay in the notification area so you can keep dictating into any app. " +
                   "You can change this any time in Settings.",
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
        };

        var keep = new Button
        {
            Content = "Keep running in the background",
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            FontWeight = FontWeight.SemiBold,
            Background = Tokens.Brushes.DarkPill,
            Foreground = Tokens.Brushes.PillInk,
            CornerRadius = new CornerRadius(Tokens.Radius.Pill),
            Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Base),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        keep.Click += (_, _) => Close(true);

        var quit = new Button
        {
            Content = "Quit",
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            Background = Tokens.Brushes.Glass,
            Foreground = Tokens.Brushes.Ink,
            CornerRadius = new CornerRadius(Tokens.Radius.Pill),
            Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Base),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        quit.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            Children = { keep, quit },
        };

        var content = new StackPanel
        {
            Spacing = Tokens.Space.Roomy,
            Margin = new Thickness(Tokens.Space.Wide),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { message, buttons },
        };

        Content = new Border
        {
            Background = Tokens.Brushes.ChassisGradient,
            Child = content,
        };
    }
}
