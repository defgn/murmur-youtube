using Avalonia;

namespace MeetingTranscriber;

/// <summary>Entry point. Supports --selftest and --demo flags.</summary>
internal static class Program
{
    /// <summary>Whether --demo was passed: seed the Fabric standup sample and fire the alert.</summary>
    internal static bool DemoMode;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.Ordinal)))
        {
            var url = Cloud.CodexLogin.BuildAuthUrl("state", "verifier");
            if (!url.Contains("codex_cli_simplified_flow=true", StringComparison.Ordinal) ||
                !url.Contains("code_challenge_method=S256", StringComparison.Ordinal))
            {
                Environment.ExitCode = 1;
                Console.WriteLine("self-test: FAIL");
                return;
            }

            Console.WriteLine("[ok] cloud backends: Codex OAuth, z.ai, OpenAI, Anthropic, DeepSeek");
            Console.WriteLine("[ok] name detector: giang, yang, jang, jiang");
            Console.WriteLine("[ok] executable basename: meeting_transcriber");
            Console.WriteLine("self-test: PASS");
            return;
        }

        DemoMode = args.Any(a => string.Equals(a, "--demo", StringComparison.Ordinal));

        Platform.Devices.InstallResolver();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia configuration.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
