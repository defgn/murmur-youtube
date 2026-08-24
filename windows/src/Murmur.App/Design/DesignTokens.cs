using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Murmur.App.Design;

/// <summary>
/// The design system, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Direction: warm, calm, modern — cream surfaces, glass cards, a single dark hero pill,
/// and an indigo-violet brand mark. The brief is a modern dictation tool (Wispr Flow's
/// direction), not retro equipment.
/// </para>
/// <para>
/// <b>Views must not contain literal values.</b> If a control needs a number that isn't here,
/// add the token rather than inlining it.
/// </para>
/// <para>Rules that are not negotiable:</para>
/// <list type="bullet">
/// <item><b>Red means recording.</b> Nothing else in the app is red.</item>
/// <item><b>The hero pill is the app.</b> One dark pill, the whole gesture.</item>
/// <item><b>Radii are soft.</b> 8–28 px; nothing machined.</item>
/// </list>
/// </remarks>
public static class Tokens
{
    /// <summary>
    /// Whether the black-face palette is in use.
    /// </summary>
    /// <remarks>
    /// The modern face is light cream in both themes — the warm paper look is the product,
    /// not a theme preference. Keeping the hook lets a future dark face slot in without
    /// touching the views.
    /// </remarks>
    public static bool IsBlackFace => false;

    // ---- Colour ----

    /// <summary>Surfaces and ink, from the warm paper outward.</summary>
    public static class Colors
    {
        /// <summary>The window background. Warm paper.</summary>
        public static Color Chassis => Face(0xF7F4EE, 0xF7F4EE);

        /// <summary>The deeper end of the background gradient.</summary>
        public static Color ChassisDeep => Face(0xF1EDE4, 0xF1EDE4);

        /// <summary>Cards and raised surfaces.</summary>
        public static Color Panel => Face(0xFFFFFF, 0xFFFFFF);

        /// <summary>Top bevel highlight on a raised element.</summary>
        public static Color PanelHighlight => Face(0xFFFFFF, 0xFFFFFF);

        /// <summary>Bottom bevel shade on a raised element.</summary>
        public static Color PanelShade => Face(0xEFE9DE, 0xEFE9DE);

        /// <summary>Recessed wells, set into the paper.</summary>
        public static Color Well => Face(0xF3EFE6, 0xF3EFE6);

        /// <summary>Search rows and input beds.</summary>
        public static Color Deck => Face(0xF6F2E9, 0xF6F2E9);

        /// <summary>Button caps.</summary>
        public static Color Cap => Face(0xFFFFFF, 0xFFFFFF);

        /// <summary>The hairline between surfaces.</summary>
        public static Color Seam => Face(0xE5DCCB, 0xE5DCCB);

        /// <summary>Primary readable text.</summary>
        public static Color Ink => Face(0x23201C, 0x23201C);

        /// <summary>Supporting text.</summary>
        public static Color InkSecondary => Face(0x8D8271, 0x8D8271);

        /// <summary>Small-caps labels.</summary>
        public static Color Silkscreen => Face(0xB0A594, 0xB0A594);

        /// <summary>Text on a light surface, read as primary.</summary>
        public static Color InkOnDeck => Face(0x23201C, 0x23201C);

        /// <summary>The record state. The only red in the app.</summary>
        public static Color Record => Rgb(0xE11D48);

        /// <summary>The record state idle — a soft rose, not a dark lens.</summary>
        public static Color RecordIdle => Face(0xF4C6CF, 0xF4C6CF);

        /// <summary>A selected row. The card lifts rather than tints.</summary>
        public static Color Selection => Face(0xF1EBDF, 0xF1EBDF);

        /// <summary>Edge on a selected or focused element.</summary>
        public static Color SelectionEdge => Face(0xC9BCA6, 0xC9BCA6);

        /// <summary>Keyboard focus ring. Reads without relying on colour.</summary>
        public static Color FocusRing => Rgb(0x6D5DF6);

        /// <summary>Row under the pointer, before selection.</summary>
        public static Color Hover => Face(0xF5F0E6, 0xF5F0E6);

        // The Woffle brand. The speech-bubble mark is this colour everywhere.
        /// <summary>The brand indigo-violet of the Woffle mark.</summary>
        public static Color Brand => Rgb(0x6D5DF6);

        /// <summary>Terracotta accent — the active tab underline.</summary>
        public static Color Accent => Rgb(0xC05B34);

        /// <summary>Sage — the "ready" status dot.</summary>
        public static Color Success => Rgb(0x4D7C5F);

        /// <summary>The dark hero pill.</summary>
        public static Color DarkPill => Rgb(0x24211C);

        /// <summary>The hero pill while recording — dark rose.</summary>
        public static Color RecordPill => Rgb(0x7A1F2E);

        /// <summary>Waveform bars inside the pill while recording.</summary>
        public static Color RecordBar => Rgb(0xFDA4AF);

        /// <summary>The hero pill while pressed — slightly deeper.</summary>
        public static Color DarkPillPressed => Rgb(0x1A1815);

        /// <summary>The hero pill while pressed during recording.</summary>
        public static Color RecordPillPressed => Rgb(0x671A26);

        /// <summary>Text on the dark hero pill.</summary>
        public static Color PillInk => Rgb(0xFAF7F1);

        /// <summary>Glass card fill — white at 65%.</summary>
        public static Color Glass => Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF);

        // Instrumentation. Kept for the VU meter control; not used in the modern UI.

        /// <summary>Classic cream VU face.</summary>
        public static Color MeterFace => Rgb(0xF3EFE6);

        /// <summary>The amber lamp behind a VU face.</summary>
        public static Color MeterLamp => Rgb(0xE8B860);

        /// <summary>Needle and scale printing.</summary>
        public static Color MeterNeedle => Rgb(0x23201C);

        /// <summary>Nominal level.</summary>
        public static Color MeterGreen => Rgb(0x4D7C5F);

        /// <summary>Approaching peak.</summary>
        public static Color MeterAmber => Rgb(0xC9922E);

        /// <summary>Over.</summary>
        public static Color MeterRed => Rgb(0xE11D48);

        private static Color Rgb(uint hex) => Color.FromRgb(
            (byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));

        private static Color Face(uint light, uint dark) => Rgb(IsBlackFace ? dark : light);
    }

    /// <summary>Brushes for the colours above, allocated per call.</summary>
    public static class Brushes
    {
        /// <inheritdoc cref="Colors.Chassis"/>
        public static IBrush Chassis => new SolidColorBrush(Colors.Chassis);

        /// <summary>The warm paper gradient behind the whole window.</summary>
        public static IBrush ChassisGradient => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Chassis, 0),
                new GradientStop(Colors.ChassisDeep, 1),
            },
        };

        /// <inheritdoc cref="Colors.Panel"/>
        public static IBrush Panel => new SolidColorBrush(Colors.Panel);

        /// <summary>Glass card surface.</summary>
        public static IBrush Glass => new SolidColorBrush(Colors.Glass);

        /// <summary>Glass card surface, more opaque — settings cards.</summary>
        public static IBrush GlassStrong => new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

        /// <summary>Soft glass wash — hover fills, subtler than a card.</summary>
        public static IBrush GlassSoft => new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF));

        /// <inheritdoc cref="Colors.Well"/>
        public static IBrush Well => new SolidColorBrush(Colors.Well);

        /// <inheritdoc cref="Colors.Deck"/>
        public static IBrush Deck => new SolidColorBrush(Colors.Deck);

        /// <inheritdoc cref="Colors.Cap"/>
        public static IBrush Cap => new SolidColorBrush(Colors.Cap);

        /// <inheritdoc cref="Colors.Ink"/>
        public static IBrush Ink => new SolidColorBrush(Colors.Ink);

        /// <inheritdoc cref="Colors.Silkscreen"/>
        public static IBrush Silkscreen => new SolidColorBrush(Colors.Silkscreen);

        /// <inheritdoc cref="Colors.InkOnDeck"/>
        public static IBrush InkOnDeck => new SolidColorBrush(Colors.InkOnDeck);

        /// <inheritdoc cref="Colors.Record"/>
        public static IBrush Record => new SolidColorBrush(Colors.Record);

        /// <inheritdoc cref="Colors.Brand"/>
        public static IBrush Brand => new SolidColorBrush(Colors.Brand);

        /// <inheritdoc cref="Colors.Accent"/>
        public static IBrush Accent => new SolidColorBrush(Colors.Accent);

        /// <inheritdoc cref="Colors.Success"/>
        public static IBrush Success => new SolidColorBrush(Colors.Success);

        /// <inheritdoc cref="Colors.DarkPill"/>
        public static IBrush DarkPill => new SolidColorBrush(Colors.DarkPill);

        /// <inheritdoc cref="Colors.RecordPill"/>
        public static IBrush RecordPill => new SolidColorBrush(Colors.RecordPill);

        /// <inheritdoc cref="Colors.RecordBar"/>
        public static IBrush RecordBar => new SolidColorBrush(Colors.RecordBar);

        /// <inheritdoc cref="Colors.DarkPillPressed"/>
        public static IBrush DarkPillPressed => new SolidColorBrush(Colors.DarkPillPressed);

        /// <inheritdoc cref="Colors.RecordPillPressed"/>
        public static IBrush RecordPillPressed => new SolidColorBrush(Colors.RecordPillPressed);

        /// <inheritdoc cref="Colors.PillInk"/>
        public static IBrush PillInk => new SolidColorBrush(Colors.PillInk);

        /// <inheritdoc cref="Colors.MeterFace"/>
        public static IBrush MeterFace => new SolidColorBrush(Colors.MeterFace);
    }

    // ---- Type ----

    /// <summary>
    /// A neutral, modern sans — the Windows UI voice.
    /// </summary>
    public static class Fonts
    {
        /// <summary>The UI typeface.</summary>
        public static FontFamily Grotesque { get; } =
            new("Segoe UI Variable Text, Segoe UI, Inter, sans-serif");

        /// <summary>Readouts and timings. Monospaced so digits don't shift as they tick.</summary>
        public static FontFamily Mono { get; } =
            new("Cascadia Mono, Consolas, monospace");

        /// <summary>Small-caps labels.</summary>
        public const double Silkscreen = 11;

        /// <summary>A larger small-caps label, for section headers.</summary>
        public const double SilkscreenLarge = 13;

        /// <summary>Caption text.</summary>
        public const double Caption = 11.5;

        /// <summary>Secondary label text.</summary>
        public const double Label = 12.5;

        /// <summary>Body text.</summary>
        public const double Body = 15;

        /// <summary>Slightly larger body text, for the transcript cards.</summary>
        public const double BodyLarge = 16;

        /// <summary>Section titles.</summary>
        public const double Title = 19;

        /// <summary>The hero pill label.</summary>
        public const double Pill = 17;

        /// <summary>The big transport counter.</summary>
        public const double CounterLarge = 26;

        /// <summary>Letter spacing for small-caps labels, in device-independent pixels.</summary>
        public const double SilkscreenTracking = 0.4;
    }

    // ---- Geometry ----

    /// <summary>A 4pt grid. Panels are laid out on it; nothing sits between steps.</summary>
    public static class Space
    {
        /// <summary>2</summary>
        public const double Hair = 2;

        /// <summary>4</summary>
        public const double Tight = 4;

        /// <summary>8</summary>
        public const double Snug = 8;

        /// <summary>12</summary>
        public const double Base = 12;

        /// <summary>16</summary>
        public const double Roomy = 16;

        /// <summary>24</summary>
        public const double Wide = 24;

        /// <summary>32</summary>
        public const double Panel = 32;
    }

    /// <summary>
    /// Soft by design. Modern surfaces are rounded; nothing is machined.
    /// </summary>
    public static class Radius
    {
        /// <summary>Seams and dividers — square.</summary>
        public const double None = 0;

        /// <summary>Small chips, badges, dots.</summary>
        public const double Chip = 8;

        /// <summary>Buttons and controls.</summary>
        public const double Control = 12;

        /// <summary>Cards and wells.</summary>
        public const double Panel = 20;

        /// <summary>The window itself.</summary>
        public const double Window = 28;

        /// <summary>Fully round — pills and the hero.</summary>
        public const double Pill = 999;
    }

    /// <summary>Line weights.</summary>
    public static class Border
    {
        /// <summary>A drawn hairline.</summary>
        public const double Hairline = 1;

        /// <summary>The seam between two panels.</summary>
        public const double Seam = 1;

        /// <summary>Bevel thickness on raised controls.</summary>
        public const double Bevel = 1;
    }

    // ---- Material ----

    /// <summary>
    /// Sizes for the remaining physical controls (the VU meter and friends are kept for
    /// compatibility with the equipment controls; the modern UI does not use them).
    /// </summary>
    public static class Material
    {
        /// <summary>Opacity of the lighter striations in brushed metal.</summary>
        public const double GrainLight = 0.055;

        /// <summary>Opacity of the darker striations.</summary>
        public const double GrainDark = 0.07;

        /// <summary>Distance between striations.</summary>
        public const double GrainPitch = 2;

        /// <summary>Diameter of a panel screw head.</summary>
        public const double ScrewSize = 9;

        /// <summary>A single vent slot.</summary>
        public const double VentSlotWidth = 3;

        /// <summary>Height of a vent slot.</summary>
        public const double VentSlotHeight = 22;

        /// <summary>Gap between vent slots.</summary>
        public const double VentSlotGap = 4;

        /// <summary>Indicator lamp diameter.</summary>
        public const double LampSize = 8;

        /// <summary>A lit lamp's lens highlight — a specular dot, not a bloom.</summary>
        public const double LampSpecular = 0.45;

        /// <summary>How far an unlit lamp sits below the lit value.</summary>
        public const double LampUnlitOpacity = 0.22;

        /// <summary>Transport key height.</summary>
        public const double KeyHeight = 44;

        /// <summary>Minimum transport key width.</summary>
        public const double KeyMinWidth = 64;

        /// <summary>How far a key sinks when pressed.</summary>
        public const double KeyTravel = 1;

        /// <summary>Total sweep of the VU needle, in degrees, centred on vertical.</summary>
        public const double NeedleSweepDegrees = 96;

        /// <summary>Needle thickness.</summary>
        public const double NeedleWidth = 1.5;

        /// <summary>Where 0 VU sits along the scale, 0…1. The red zone begins here.</summary>
        public const double MeterZeroPoint = 0.72;

        /// <summary>Number of waveform bars in the hero pill.</summary>
        public const int WaveformBars = 7;

        /// <summary>Waveform bar width.</summary>
        public const double WaveformBarWidth = 3.5;

        /// <summary>Waveform bar corner radius.</summary>
        public const double WaveformBarRadius = 2;

        /// <summary>Height of the hero pill.</summary>
        public const double HeroPillHeight = 68;

        /// <summary>Minimum width of the hero pill.</summary>
        public const double HeroPillMinWidth = 300;
    }

    // ---- Motion ----

    /// <summary>Soft and quick; modern surfaces settle rather than snap.</summary>
    public static class Motion
    {
        /// <summary>Press feedback.</summary>
        public static TimeSpan Press { get; } = TimeSpan.FromMilliseconds(80);

        /// <summary>Release feedback.</summary>
        public static TimeSpan Release { get; } = TimeSpan.FromMilliseconds(140);

        /// <summary>Panel and view changes.</summary>
        public static TimeSpan Panel { get; } = TimeSpan.FromMilliseconds(220);

        /// <summary>The record state coming on — instant, like a filament.</summary>
        public static TimeSpan Lamp { get; } = TimeSpan.FromMilliseconds(80);

        /// <summary>
        /// VU ballistics: seconds to reach a step going up.
        /// </summary>
        /// <remarks>
        /// Kept for the VU meter control, which is no longer part of the modern UI.
        /// </remarks>
        public const double NeedleAttackSeconds = 0.30;

        /// <summary>Seconds for the needle to fall back.</summary>
        public const double NeedleReleaseSeconds = 0.42;

        /// <summary>Peak overshoot as a fraction of the step, before settling.</summary>
        public const double NeedleOvershoot = 0.06;
    }
}
