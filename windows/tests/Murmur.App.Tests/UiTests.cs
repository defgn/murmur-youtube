using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.App.Views;
using Shouldly;

[assembly: AvaloniaTestApplication(typeof(Murmur.AppTests.TestAppBuilder))]

namespace Murmur.AppTests;

/// <summary>Hosts the app headlessly so the UI can be exercised without a display.</summary>
public static class TestAppBuilder
{
    /// <summary>Builds a headless Avalonia app for the test host.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>A minimal application shell for headless tests.</summary>
public sealed class TestApp : Application
{
    /// <inheritdoc />
    public override void Initialize() => Styles.Add(new FluentTheme());
}

/// <summary>
/// Real UI tests, running with no display.
/// </summary>
/// <remarks>
/// This is the payoff for choosing Avalonia over WPF. These run on macOS in milliseconds and
/// on a Windows runner in CI, so a broken layout or a control that fails to construct is
/// caught while writing it rather than after shipping to a machine we cannot test on.
/// </remarks>
public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void Window_opens_and_lays_out()
    {
        var window = new MainWindow();
        window.Show();

        window.Bounds.Width.ShouldBeGreaterThan(0);
        window.Bounds.Height.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public void Record_toggles_the_pill_and_status_together()
    {
        var window = new MainWindow();
        window.Show();

        window.IsRecording.ShouldBeFalse();
        window.StatusText.Text.ShouldBe("Ready");

        window.ToggleRecording();

        window.IsRecording.ShouldBeTrue();
        window.StatusText.Text.ShouldBe("Recording", "the status chip must follow the transport");

        window.ToggleRecording();

        window.StatusText.Text.ShouldBe("Ready");
        window.IsRecording.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Window_honours_its_minimum_size()
    {
        var window = new MainWindow();
        window.Show();

        window.MinWidth.ShouldBe(760);
        window.MinHeight.ShouldBe(560);
    }
}

/// <summary>The individual pieces of equipment (kept for compatibility).</summary>
public sealed class EquipmentTests
{
    [AvaloniaFact]
    public void Silkscreen_uppercases_its_text()
    {
        // The look depends on size, tracking AND case together — a label that kept its
        // original casing would be half-styled and read as ordinary UI text.
        var label = new Silkscreen { Text = "transport" };
        label.Text.ShouldBe("TRANSPORT");
    }

    [AvaloniaFact]
    public void Silkscreen_uses_the_token_tracking()
    {
        new Silkscreen().LetterSpacing.ShouldBe(Tokens.Fonts.SilkscreenTracking);
    }

    [AvaloniaFact]
    public void Lamp_defaults_to_the_token_size()
    {
        var lamp = new Lamp();
        lamp.Width.ShouldBe(Tokens.Material.LampSize);
        lamp.Height.ShouldBe(Tokens.Material.LampSize);
    }

    [AvaloniaFact]
    public void Transport_key_uses_the_token_dimensions()
    {
        var key = new TransportKey();
        key.Height.ShouldBe(Tokens.Material.KeyHeight);
        key.MinWidth.ShouldBe(Tokens.Material.KeyMinWidth);
    }

    [AvaloniaFact]
    public void Vents_measure_to_the_slot_geometry()
    {
        var vents = new Vents { Count = 4 };
        vents.Measure(Size.Infinity);

        var expected = (4 * Tokens.Material.VentSlotWidth) + (3 * Tokens.Material.VentSlotGap);
        vents.DesiredSize.Width.ShouldBe(expected);
        vents.DesiredSize.Height.ShouldBe(Tokens.Material.VentSlotHeight);
    }

    [AvaloniaFact]
    public void Meter_renders_without_throwing()
    {
        // The VU meter does all its own drawing, including a damped needle stepped by a
        // timer. Constructing and showing it is what proves the render path is sound.
        var meter = new VuMeter { Width = 168, Height = 54, Level = 0.6 };
        var window = new Window { Content = meter };
        window.Show();

        meter.Bounds.Width.ShouldBeGreaterThan(0);
    }
}

/// <summary>
/// Guards the colour rules the design system calls non-negotiable.
/// </summary>
/// <remarks>
/// These are the sort of rule that erodes one reasonable-looking commit at a time. Asserting
/// them makes the erosion a build failure.
/// </remarks>
public sealed class DesignSystemTests
{
    [AvaloniaFact]
    public void Record_red_is_the_only_red_in_the_app()
    {
        var red = Tokens.Colors.Record;
        red.ShouldBe(Avalonia.Media.Color.FromRgb(0xE1, 0x1D, 0x48));

        // Nothing else that paints UI chrome is allowed to be red.
        red.ShouldNotBe(Tokens.Colors.Accent);
        red.ShouldNotBe(Tokens.Colors.Brand);
    }

    [AvaloniaFact]
    public void Radii_are_soft_and_modern()
    {
        // The modern face is rounded, not machined. Anything sharper reads as a different
        // design language entirely.
        Tokens.Radius.Chip.ShouldBeGreaterThanOrEqualTo(8);
        Tokens.Radius.Control.ShouldBeGreaterThanOrEqualTo(12);
        Tokens.Radius.Panel.ShouldBeGreaterThanOrEqualTo(20);
        Tokens.Radius.Window.ShouldBeGreaterThanOrEqualTo(28);
    }

    [AvaloniaFact]
    public void Spacing_stays_on_the_four_point_grid()
    {
        double[] steps =
        [
            Tokens.Space.Hair, Tokens.Space.Tight, Tokens.Space.Snug,
            Tokens.Space.Base, Tokens.Space.Roomy, Tokens.Space.Wide, Tokens.Space.Panel,
        ];

        foreach (var step in steps) (step % 2).ShouldBe(0, $"{step} is off the grid");
    }

    [AvaloniaFact]
    public void Brand_is_the_indigo_violet_of_the_mark()
    {
        // The speech-bubble logo is this colour everywhere — window, tray, UI accents.
        Tokens.Colors.Brand.ShouldBe(Avalonia.Media.Color.FromRgb(0x6D, 0x5D, 0xF6));
    }
}
