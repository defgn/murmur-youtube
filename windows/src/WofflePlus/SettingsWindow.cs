using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WofflePlus.Cloud;
using WofflePlus.Design;

namespace WofflePlus;

/// <summary>
/// Settings dialog: pick the answer backend (ChatGPT subscription via Codex sign-in,
/// z.ai key, OpenAI key, Anthropic key), see session defaults.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly PlusSettingsStore _store;
    private readonly InterviewSession _session;

    public SettingsWindow(PlusSettingsStore store, InterviewSession session)
    {
        _store = store;
        _session = session;

        Title = "Woffle+ Settings";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Plus.Brush.Bg;

        var stack = new StackPanel { Spacing = Plus.Space.Roomy };

        stack.Children.Add(Section("ANSWER MODEL", BuildBackendCards()));
        stack.Children.Add(Note(
            "The ChatGPT option uses your ChatGPT Plus/Pro subscription through Codex's " +
            "sign-in — no separate API billing. z.ai uses your z.ai subscription key. " +
            "Keys are stored on this PC and sent only to the provider you chose."));

        Content = new ScrollViewer
        {
            MaxHeight = 640,
            Content = new Border
            {
                Background = Plus.Brush.Bg,
                Padding = new Thickness(Plus.Space.Wide),
                Child = stack,
            },
        };
    }

    private StackPanel BuildBackendCards()
    {
        var panel = new StackPanel { Spacing = Plus.Space.Snug };

        // --- ChatGPT subscription (Codex OAuth) ---
        var codexStatus = new TextBlock
        {
            FontSize = Plus.Font.Small,
            Foreground = CodexLogin.IsSignedIn ? Plus.Brush.Green : Plus.Brush.Orange,
            Text = CodexLogin.IsSignedIn ? "Signed in to ChatGPT" : "Not signed in",
        };
        var codexButton = OutlineButton(CodexLogin.IsSignedIn ? "Re-sign in" : "Sign in with ChatGPT");
        codexButton.Click += async (_, _) =>
        {
            codexButton.IsEnabled = false;
            codexStatus.Text = "Waiting for browser sign-in…";
            codexStatus.Foreground = Plus.Brush.InkSecondary;
            var tokens = await _session.SignInCodexAsync(url => TopLevelHelper.OpenUrl(url));
            codexButton.IsEnabled = true;
            if (tokens is not null)
            {
                codexStatus.Text = "Signed in to ChatGPT ✓";
                codexStatus.Foreground = Plus.Brush.Green;
                _store.Update(_store.Data with { Backend = "Codex" });
            }
            else
            {
                codexStatus.Text = "Sign-in failed or cancelled";
                codexStatus.Foreground = Plus.Brush.Orange;
            }
        };
        var signOut = OutlineButton("Sign out");
        signOut.Click += (_, _) =>
        {
            _session.SignOutCodex();
            codexStatus.Text = "Signed out";
            codexStatus.Foreground = Plus.Brush.InkSecondary;
        };
        panel.Children.Add(BackendCard(
            "CHATGPT (PLUS/PRO SUBSCRIPTION)",
            "Uses your ChatGPT subscription. Sign in once with your OpenAI account.",
            new Control[] { codexStatus, new StackPanel { Orientation = Orientation.Horizontal, Spacing = Plus.Space.Snug, Children = { codexButton, signOut } } },
            selected: _store.Data.Backend == "Codex",
            onSelect: () => _store.Update(_store.Data with { Backend = "Codex" })));

        // --- z.ai ---
        var zaiKey = KeyBox(_store.Data.ZaiApiKey, "Paste your z.ai API key");
        zaiKey.TextChanged += (_, _) => _store.Update(_store.Data with { ZaiApiKey = NullIfEmpty(zaiKey.Text) });
        var zaiModel = ModelBox(_store.Data.ZaiModel, "glm-5.3");
        zaiModel.TextChanged += (_, _) => _store.Update(_store.Data with { ZaiModel = ModelOrDefault(zaiModel.Text, "glm-5.3") });
        panel.Children.Add(BackendCard(
            "Z.AI (GLM SUBSCRIPTION)",
            "Your z.ai API key from https://z.ai — OpenAI-wire compatible, subscription billing.",
            new Control[] { zaiKey, zaiModel },
            selected: _store.Data.Backend == "Zai",
            onSelect: () => _store.Update(_store.Data with { Backend = "Zai" })));

        // --- OpenAI platform key (optional alternative) ---
        var openAiKey = KeyBox(_store.Data.OpenAiApiKey, "sk-… (platform key, billed per token)");
        openAiKey.TextChanged += (_, _) => _store.Update(_store.Data with { OpenAiApiKey = NullIfEmpty(openAiKey.Text) });
        var openAiModel = ModelBox(_store.Data.OpenAiModel, "gpt-4o-mini");
        openAiModel.TextChanged += (_, _) => _store.Update(_store.Data with { OpenAiModel = ModelOrDefault(openAiModel.Text, "gpt-4o-mini") });
        panel.Children.Add(BackendCard(
            "OPENAI PLATFORM API",
            "Separate from ChatGPT — pay-per-token billing on platform.openai.com.",
            new Control[] { openAiKey, openAiModel },
            selected: _store.Data.Backend == "OpenAi",
            onSelect: () => _store.Update(_store.Data with { Backend = "OpenAi" })));

        // --- Anthropic ---
        var anthropicKey = KeyBox(_store.Data.AnthropicApiKey, "sk-ant-…");
        anthropicKey.TextChanged += (_, _) => _store.Update(_store.Data with { AnthropicApiKey = NullIfEmpty(anthropicKey.Text) });
        var anthropicModel = ModelBox(_store.Data.AnthropicModel, "claude-sonnet-4-5");
        anthropicModel.TextChanged += (_, _) => _store.Update(_store.Data with { AnthropicModel = ModelOrDefault(anthropicModel.Text, "claude-sonnet-4-5") });
        panel.Children.Add(BackendCard(
            "ANTHROPIC (CLAUDE)",
            "Your Anthropic API key from console.anthropic.com.",
            new Control[] { anthropicKey, anthropicModel },
            selected: _store.Data.Backend == "Anthropic",
            onSelect: () => _store.Update(_store.Data with { Backend = "Anthropic" })));

        return panel;
    }

    private Border BackendCard(string title, string detail, IEnumerable<Control> fields, bool selected, Action onSelect)
    {
        var header = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = Plus.Font.Small,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = selected ? Plus.Brush.Green : Plus.Brush.Ink,
                },
                new TextBlock
                {
                    Text = detail,
                    FontSize = Plus.Font.Small,
                    Foreground = Plus.Brush.InkSecondary,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        var body = new StackPanel { Spacing = Plus.Space.Snug };
        body.Children.Add(header);
        foreach (var field in fields) body.Children.Add(field);

        var card = new Border
        {
            Background = selected ? Plus.Brush.Header : Plus.Brush.Card,
            BorderBrush = selected ? Plus.Brush.Green : Plus.Brush.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Plus.Radius.Card),
            Padding = new Thickness(Plus.Space.Roomy),
            Child = body,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        card.PointerPressed += (_, _) =>
        {
            onSelect();
            card.Background = Plus.Brush.Header;
            card.BorderBrush = Plus.Brush.Green;
        };

        return card;
    }

    private static Button OutlineButton(string label) => new()
    {
        Content = label,
        FontSize = Plus.Font.Small,
        Foreground = Plus.Brush.Ink,
        Background = Plus.Brush.Header,
        BorderBrush = Plus.Brush.Border,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(Plus.Space.Base, Plus.Space.Snug),
    };

    private static TextBox KeyBox(string? value, string watermark) => new()
    {
        Text = value ?? string.Empty,
        Watermark = watermark,
        PasswordChar = '•',
        FontSize = Plus.Font.Small,
        Background = Plus.Brush.Bg,
        BorderBrush = Plus.Brush.Border,
        Foreground = Plus.Brush.Ink,
    };

    private static TextBox ModelBox(string value, string watermark) => new()
    {
        Text = value,
        Watermark = watermark,
        FontSize = Plus.Font.Small,
        Background = Plus.Brush.Bg,
        BorderBrush = Plus.Brush.Border,
        Foreground = Plus.Brush.Ink,
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string ModelOrDefault(string? s, string fallback) =>
        string.IsNullOrWhiteSpace(s) ? fallback : s;

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = Plus.Font.Small,
        Foreground = Plus.Brush.InkSecondary,
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock SmallCaps(string label) => new()
    {
        Text = label,
        FontSize = Plus.Font.Small,
        FontWeight = FontWeight.SemiBold,
        Foreground = Plus.Brush.InkSecondary,
    };

    private Control Section(string title, Control content) => new StackPanel
    {
        Spacing = Plus.Space.Snug,
        Children = { SmallCaps(title), content },
    };
}

/// <summary>Small helpers for opening the OAuth browser from a TopLevel.</summary>
internal static class TopLevelHelper
{
    /// <summary>Opens a URL in the user's default browser.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // Browser failed to open; the user can copy the URL from the error notice.
        }
    }
}
