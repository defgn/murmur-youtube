using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WofflePlus.Design;

/// <summary>
/// The Woffle+ dark design system — the 001-dark-copilot mockup, tokenised.
/// </summary>
/// <remarks>
/// Deliberately separate from dictation-Woffle's paper-light Tokens: this app is the
/// dark copilot (sits beside a video call, draws less attention). Every colour the UI
/// uses comes from here.
/// </remarks>
internal static class Plus
{
    /// <summary>The mockup's palette.</summary>
    public static class Swatch
    {
        public static Avalonia.Media.Color Bg => From(0x0D1117);
        public static Avalonia.Media.Color Header => From(0x161B22);
        public static Avalonia.Media.Color Card => From(0x161B22);
        public static Avalonia.Media.Color Border => From(0x30363D);
        public static Avalonia.Media.Color Ink => From(0xE6EDF3);
        public static Avalonia.Media.Color InkSecondary => From(0x8B949E);
        public static Avalonia.Media.Color Green => From(0x3FB950);
        public static Avalonia.Media.Color GreenDim => From(0x12261E);
        public static Avalonia.Media.Color Blue => From(0x58A6FF);
        public static Avalonia.Media.Color BlueDim => From(0x1C2A3A);
        public static Avalonia.Media.Color Purple => From(0xBC8CFF);
        public static Avalonia.Media.Color PurpleDim => From(0x26202E);
        public static Avalonia.Media.Color Orange => From(0xF0883E);
        public static Avalonia.Media.Color OrangeDim => From(0x2A1F14);
        public static Avalonia.Media.Color Hover => From(0x1C2129);
        public static Avalonia.Media.Color Keycap => From(0x21262D);
        public static Avalonia.Media.Color Muted => From(0x6E7681);

        private static Avalonia.Media.Color From(uint rgb) => Avalonia.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    /// <summary>Frozen brushes — created once, never mutated.</summary>
    public static class Brush
    {
        public static readonly IBrush Bg = Frozen(Swatch.Bg);
        public static readonly IBrush Header = Frozen(Swatch.Header);
        public static readonly IBrush Card = Frozen(Swatch.Card);
        public static readonly IBrush Border = Frozen(Swatch.Border);
        public static readonly IBrush Ink = Frozen(Swatch.Ink);
        public static readonly IBrush InkSecondary = Frozen(Swatch.InkSecondary);
        public static readonly IBrush Green = Frozen(Swatch.Green);
        public static readonly IBrush GreenDim = Frozen(Swatch.GreenDim);
        public static readonly IBrush Blue = Frozen(Swatch.Blue);
        public static readonly IBrush BlueDim = Frozen(Swatch.BlueDim);
        public static readonly IBrush Purple = Frozen(Swatch.Purple);
        public static readonly IBrush PurpleDim = Frozen(Swatch.PurpleDim);
        public static readonly IBrush Orange = Frozen(Swatch.Orange);
        public static readonly IBrush OrangeDim = Frozen(Swatch.OrangeDim);
        public static readonly IBrush Hover = Frozen(Swatch.Hover);
        public static readonly IBrush Keycap = Frozen(Swatch.Keycap);
        public static readonly IBrush Muted = Frozen(Swatch.Muted);

        public static IBrush WithAlpha(Avalonia.Media.Color c, byte a) => new SolidColorBrush(c, a);
        public static IBrush Transparent => Brushes.Transparent;

        private static IBrush Frozen(Avalonia.Media.Color c) => new SolidColorBrush(c);
    }

    /// <summary>Spacing scale (px).</summary>
    public static class Space
    {
        public const double Hair = 2;
        public const double Tight = 4;
        public const double Snug = 8;
        public const double Base = 12;
        public const double Roomy = 16;
        public const double Wide = 24;
        /// <summary>Gap between the question label and the prompt bar.</summary>
        public const double BarGap = 6;
        /// <summary>Horizontal padding of the tab strip.</summary>
        public const double StripPad = 14;
    }

    /// <summary>Type scale.</summary>
    public static class Font
    {
        public const double Label = 12.5;
        public const double Small = 12;
        /// <summary>Pane-header tail text and other micro copy.</summary>
        public const double Micro = 11;
        /// <summary>Small-caps labels above controls.</summary>
        public const double Caption = 11.5;
        public const double Body = 15;
        public const double BodyLarge = 16;
        public const double Title = 19;

        public static FontFamily Family { get; } = FontFamily.Parse("Segoe UI, Inter, Helvetica Neue, Arial");
        public static FontFamily Mono { get; } = FontFamily.Parse("Consolas, Cascadia Mono, Menlo, monospace");
        /// <summary>Letter spacing for small-caps labels.</summary>
        public const double Tracking = 1.1;
    }

    /// <summary>Corner radii.</summary>
    public static class Radius
    {
        public const double Control = 6;
        public const double Card = 10;
        public const double Pill = 20;
        /// <summary>Fully round pill (prompt bar, Ask button).</summary>
        public const double Full = 999;
    }

    /// <summary>Pane header bar geometry (px).</summary>
    public static class Pane
    {
        /// <summary>Header bar height.</summary>
        public const double Height = 38;
        /// <summary>Header bar horizontal padding.</summary>
        public const double HPadding = 14;
        /// <summary>Header bar vertical padding.</summary>
        public const double VPadding = 9;
        /// <summary>Status dot diameter.</summary>
        public const double Dot = 7;
    }

    /// <summary>Line weights.</summary>
    public static class Line
    {
        public const double Hairline = 1;
        public const double Accent = 3;
    }
}
