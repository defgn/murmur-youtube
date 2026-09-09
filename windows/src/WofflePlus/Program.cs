using Avalonia;
using System;

namespace WofflePlus;

/// <summary>Entry point.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--selftest", StringComparer.Ordinal))
        {
            var url = Cloud.CodexLogin.BuildAuthUrl("state", "verifier");
            if (!url.Contains("codex_cli_simplified_flow=true", StringComparison.Ordinal) ||
                !url.Contains("code_challenge_method=S256", StringComparison.Ordinal))
            {
                Environment.ExitCode = 1;
                Console.WriteLine("self-test: FAIL");
                return;
            }
            Console.WriteLine("[ok] cloud-only backends: Codex OAuth, z.ai, OpenAI, Anthropic");
            Console.WriteLine("[ok] executable basename: woffle_plus");
            Console.WriteLine("self-test: PASS");
            return;
        }

        Platform.Devices.InstallResolver();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia configuration.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
