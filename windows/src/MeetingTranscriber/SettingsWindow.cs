using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MeetingTranscriber.Cloud;
using MeetingTranscriber.Core;

namespace MeetingTranscriber;

/// <summary>
/// Settings dialog: answer backend (ChatGPT subscription via Codex sign-in, z.ai key,
/// OpenAI key, Anthropic key, DeepSeek key) plus the two device ids are edited here.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static readonly Color Bg = Color.FromRgb(0x0a, 0x12, 0x30);
    private static readonly Color HeaderBg = Color.FromRgb(0x10, 0x1c, 0x46);
    private static readonly Color Line = Color.FromRgb(0x2a, 0x3a, 0x6e);
    private static readonly Color Ink = Color.FromRgb(0xe8, 0xed, 0xf3);
    private static readonly Color Secondary = Color.FromRgb(0x93, 0xa1, 0xc7);
    private static readonly Color Accent = Color.FromRgb(0x7a, 0xa2, 0xff);

    private readonly MeetingSettingsStore _store;
    private readonly MtSession _session;

    private readonly ComboBox _backendPicker = new()
    {
        Background = new SolidColorBrush(HeaderBg),
        Foreground = new SolidColorBrush(Ink),
        BorderBrush = new SolidColorBrush(Line),
        FontSize = 14,
        MinWidth = 220,
    };

    private readonly TextBox _zaiKey = SecretBox();
    private readonly TextBox _zaiKeyId = SecretBox();
    private readonly TextBox _zaiKeySecret = SecretBox();
    private readonly TextBox _zaiModel = PlainBox();
    private readonly TextBox _openAiKey = SecretBox();
    private readonly TextBox _openAiModel = PlainBox();
    private readonly TextBox _anthropicKey = SecretBox();
    private readonly TextBox _anthropicModel = PlainBox();
    private readonly TextBox _deepSeekKey = SecretBox();
    private readonly TextBox _deepSeekModel = PlainBox();
    private readonly TextBlock _codexStatus;
    private readonly TextBlock _notice;

    /// <summary>Builds the settings dialog.</summary>
    /// <param name="store">Shared settings store.</param>
    /// <param name="session">Live session, for Codex sign-in.</param>
    public SettingsWindow(MeetingSettingsStore store, MtSession session)
    {
        _store = store;
        _session = session;

        Title = "Meeting Transcriber Settings";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Bg);

        _codexStatus = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Secondary) };
        _notice = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Secondary) };

        var data = _store.Data;
        _backendPicker.ItemsSource = new List<string>(MeetingCompleterDefaults.Backends);
        _backendPicker.SelectedItem = data.Backend;
        _zaiKey.Text = data.ZaiApiKey ?? string.Empty;
        _zaiKeyId.Text = data.ZaiApiKeyId ?? string.Empty;
        _zaiKeySecret.Text = data.ZaiApiKeySecret ?? string.Empty;
        _zaiModel.Text = data.ZaiModel;
        _openAiKey.Text = data.OpenAiApiKey ?? string.Empty;
        _openAiModel.Text = data.OpenAiModel;
        _anthropicKey.Text = data.AnthropicApiKey ?? string.Empty;
        _anthropicModel.Text = data.AnthropicModel;
        _deepSeekKey.Text = data.DeepSeekApiKey ?? string.Empty;
        _deepSeekModel.Text = data.DeepSeekModel;

        UpdateCodexStatus();

        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(Section("ANSWER BACKEND", BuildBackendSection()));
        stack.Children.Add(Section("CHATGPT SUBSCRIPTION", BuildCodexSection()));
        stack.Children.Add(Section("Z.AI", BuildZaiSection()));
        stack.Children.Add(Section("OPENAI", BuildKeyModelSection(_openAiKey, _openAiModel)));
        stack.Children.Add(Section("ANTHROPIC", BuildKeyModelSection(_anthropicKey, _anthropicModel)));
        stack.Children.Add(Section("DEEPSEEK", BuildKeyModelSection(_deepSeekKey, _deepSeekModel)));
        stack.Children.Add(Note(
            "The ChatGPT option uses your ChatGPT Plus or Pro subscription through Codex " +
            "sign-in: no separate API billing. z.ai uses your z.ai subscription key. " +
            "Keys stay on this PC and travel only to the provider you chose."));

        var save = new Button
        {
            Content = "Save",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Bg),
            Background = new SolidColorBrush(Accent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(28, 8, 28, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        save.Click += (_, _) => Save();
        stack.Children.Add(save);
        stack.Children.Add(_notice);

        Content = new ScrollViewer
        {
            MaxHeight = 640,
            Content = new Border
            {
                Background = new SolidColorBrush(Bg),
                Padding = new Thickness(24, 24, 24, 24),
                Child = stack,
            },
        };
    }

    private StackPanel BuildBackendSection()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Field("Backend", _backendPicker));
        return panel;
    }

    private StackPanel BuildCodexSection()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(_codexStatus);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var signIn = new Button
        {
            Content = "Sign in",
            FontSize = 14,
            Foreground = new SolidColorBrush(Ink),
            Background = new SolidColorBrush(HeaderBg),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 8, 16, 8),
        };
        signIn.Click += (_, _) => _ = SignInAsync();
        var signOut = new Button
        {
            Content = "Sign out",
            FontSize = 14,
            Foreground = new SolidColorBrush(Ink),
            Background = new SolidColorBrush(HeaderBg),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 8, 16, 8),
        };
        signOut.Click += (_, _) =>
        {
            _session.SignOutCodex();
            UpdateCodexStatus();
        };
        row.Children.Add(signIn);
        row.Children.Add(signOut);
        panel.Children.Add(row);
        return panel;
    }

    private StackPanel BuildZaiSection()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Field("API key (combined id.secret)", _zaiKey));
        panel.Children.Add(Field("Key ID (JWT flow)", _zaiKeyId));
        panel.Children.Add(Field("Key secret (JWT flow)", _zaiKeySecret));
        panel.Children.Add(Field("Model", _zaiModel));
        return panel;
    }

    private StackPanel BuildKeyModelSection(TextBox key, TextBox model)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Field("API key", key));
        panel.Children.Add(Field("Model", model));
        return panel;
    }

    private async Task SignInAsync()
    {
        _notice.Text = "Opening the browser for ChatGPT sign-in…";
        var tokens = await _session.SignInCodexAsync(url =>
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })).ConfigureAwait(true);
        _notice.Text = tokens is null
            ? "Sign-in failed or was cancelled. A key backend still works."
            : "Signed in to ChatGPT.";
        UpdateCodexStatus();
    }

    private void UpdateCodexStatus() =>
        _codexStatus.Text = CodexLogin.IsSignedIn
            ? "Signed in to ChatGPT."
            : "Not signed in. Sign in, or pick a key backend above.";

    private void Save()
    {
        var data = _store.Data with
        {
            Backend = _backendPicker.SelectedItem as string ?? "Codex",
            ZaiApiKey = BlankToNull(_zaiKey.Text),
            ZaiApiKeyId = BlankToNull(_zaiKeyId.Text),
            ZaiApiKeySecret = BlankToNull(_zaiKeySecret.Text),
            ZaiModel = _zaiModel.Text ?? string.Empty,
            OpenAiApiKey = BlankToNull(_openAiKey.Text),
            OpenAiModel = _openAiModel.Text ?? string.Empty,
            AnthropicApiKey = BlankToNull(_anthropicKey.Text),
            AnthropicModel = _anthropicModel.Text ?? string.Empty,
            DeepSeekApiKey = BlankToNull(_deepSeekKey.Text),
            DeepSeekModel = _deepSeekModel.Text ?? string.Empty,
        };
        _store.Update(data);
        _notice.Text = "Saved.";
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Control Section(string title, Control body)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Secondary),
        });
        stack.Children.Add(body);
        return stack;
    }

    private Control Field(string label, Control control)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 14, Foreground = new SolidColorBrush(Ink) });
        stack.Children.Add(control);
        return stack;
    }

    private Control Note(string text) => new TextBlock
    {
        Text = text,
        FontSize = 14,
        LineHeight = 21,
        Foreground = new SolidColorBrush(Secondary),
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBox SecretBox() => NewBox(true);

    private static TextBox PlainBox() => NewBox(false);

    private static TextBox NewBox(bool secret) => new()
    {
        FontSize = 14,
        Foreground = new SolidColorBrush(Ink),
        Background = new SolidColorBrush(HeaderBg),
        BorderBrush = new SolidColorBrush(Line),
        PasswordChar = secret ? '•' : default,
    };
}
