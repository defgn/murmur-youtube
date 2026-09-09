using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Abstractions;

namespace WofflePlus.Cloud;

/// <summary>
/// Answers questions through the configured cloud backend — never a local model.
/// </summary>
/// <remarks>
/// <para>
/// Backends: the ChatGPT subscription (Codex OAuth identity → ChatGPT backend API),
/// z.ai (GLM subscription keys, OpenAI-wire compatible at their OpenAI-compatible
/// endpoint), OpenAI platform keys, and Anthropic keys.
/// </para>
/// <para>
/// Key handling: every secret lives only in Woffle+'s local settings (or Codex's token
/// cache) and rides only in request headers to its own provider. Nothing is logged.
/// </para>
/// </remarks>
internal sealed class PlusCompleter : IChatCompleter, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    /// <summary>z.ai's OpenAI-compatible endpoint (subscription keys).</summary>
    public const string ZaiBaseUri = "https://api.z.ai/api/paas/v4/";
    public const string ZaiDefaultModel = "glm-5.3";

    /// <summary>The ChatGPT backend the Codex identity talks to.</summary>
    public const string ChatGptBaseUri = "https://chatgpt.com/backend-api/";
    public const string ChatGptDefaultModel = "gpt-5.5";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly CloudBackend _backend;
    private readonly Func<CancellationToken, Task<string?>> _credential;
    private readonly string _model;
    private readonly string _baseUri;
    private readonly HttpClient _http;

    /// <summary>Builds a completer for a key-based backend.</summary>
    public static PlusCompleter ForKey(CloudBackend backend, string key, string model, string? baseUri = null)
    {
        return new PlusCompleter(backend, _ => Task.FromResult<string?>(key), model, baseUri);
    }

    /// <summary>Builds the ChatGPT-subscription completer (token resolved per call, refreshed silently).</summary>
    public static PlusCompleter ForCodexSubscription(string model = ChatGptDefaultModel) =>
        new(CloudBackend.CodexSubscription,
            async ct =>
            {
                var tokens = await CodexLogin.GetValidTokensAsync(ct).ConfigureAwait(false);
                CodexLoginState.AccountId = tokens?.AccountId ?? string.Empty;
                return tokens?.AccessToken;
            },
            model,
            ChatGptBaseUri);

    private PlusCompleter(
        CloudBackend backend, Func<CancellationToken, Task<string?>> credential, string model, string? baseUri)
    {
        _backend = backend;
        _credential = credential;
        _model = model;
        _baseUri = backend switch
        {
            CloudBackend.ZaiKey => ZaiBaseUri,
            CloudBackend.CodexSubscription => ChatGptBaseUri,
            CloudBackend.OpenAiKey => "https://api.openai.com/v1/",
            CloudBackend.AnthropicKey => "https://api.anthropic.com/v1/",
            _ => baseUri ?? string.Empty,
        };
        if (!string.IsNullOrEmpty(baseUri) && backend == CloudBackend.OpenAiKey) _baseUri = baseUri;
        _http = new HttpClient { Timeout = Timeout };
    }

    /// <summary>Test seam: pre-wired handler.</summary>
    public PlusCompleter(HttpMessageHandler handler, CloudBackend backend, Func<CancellationToken, Task<string?>> credential, string model)
    {
        _backend = backend;
        _credential = credential;
        _model = model;
        _baseUri = backend switch
        {
            CloudBackend.ZaiKey => ZaiBaseUri,
            CloudBackend.CodexSubscription => ChatGptBaseUri,
            CloudBackend.OpenAiKey => "https://api.openai.com/v1/",
            CloudBackend.AnthropicKey => "https://api.anthropic.com/v1/",
            _ => string.Empty,
        };
        _http = new HttpClient(handler) { Timeout = Timeout };
    }

    /// <summary>
    /// The user-facing model id shown in Settings (empty for subscription defaults).
    /// </summary>
    public string Model => _model;

    /// <inheritdoc />
    public async Task<string?> CompleteAsync(
        string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        var credential = await _credential(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credential)) return null;

        try
        {
            return _backend switch
            {
                CloudBackend.AnthropicKey => await CompleteAnthropicAsync(
                    credential, systemPrompt, userText, cancellationToken).ConfigureAwait(false),
                CloudBackend.CodexSubscription => await CompleteCodexAsync(
                    credential, systemPrompt, userText, cancellationToken).ConfigureAwait(false),
                _ => await CompleteOpenAiWireAsync(
                    credential, systemPrompt, userText, cancellationToken).ConfigureAwait(false),
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>OpenAI-wire /chat/completions — also what z.ai and OpenAI keys speak.</summary>
    private async Task<string?> CompleteOpenAiWireAsync(
        string credential, string systemPrompt, string userText, CancellationToken ct)
    {
        var request = new OpenAiWire.Request(
            Model: _model,
            Messages:
            [
                new OpenAiWire.Message("system", systemPrompt),
                new OpenAiWire.Message("user", userText),
            ],
            Temperature: 0.7,
            MaxTokens: 700);

        using var message = new HttpRequestMessage(HttpMethod.Post, _baseUri + "chat/completions")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content
            .ReadFromJsonAsync<OpenAiWire.Response>(JsonOptions, ct)
            .ConfigureAwait(false);
        var text = body?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Anthropic's /messages shape.</summary>
    private async Task<string?> CompleteAnthropicAsync(
        string credential, string systemPrompt, string userText, CancellationToken ct)
    {
        var request = new AnthropicWire.Request(
            Model: _model,
            System: systemPrompt,
            Messages: [new AnthropicWire.Message("user", userText)],
            MaxTokens: 700,
            Temperature: 0.7);

        using var message = new HttpRequestMessage(HttpMethod.Post, _baseUri + "messages")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        message.Headers.Add("x-api-key", credential);
        message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content
            .ReadFromJsonAsync<AnthropicWire.Response>(JsonOptions, ct)
            .ConfigureAwait(false);
        var text = string.Concat(
            (body?.Content ?? []).Where(b => b.Type == "text").Select(b => b.Text ?? string.Empty)).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// The ChatGPT backend (Codex subscription): the Responses API with the OAuth
    /// identity as a bearer token. Same wire shape Codex CLI speaks.
    /// </summary>
    private async Task<string?> CompleteCodexAsync(
        string accessToken, string systemPrompt, string userText, CancellationToken ct)
    {
        var request = new CodexWire.Request(
            Model: _model,
            Instructions: systemPrompt,
            Input: [new CodexWire.InputItem(new CodexWire.InputText(userText))],
            Store: false,
            Stream: true);

        using var message = new HttpRequestMessage(HttpMethod.Post, _baseUri + "codex/responses")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Add("chatgpt-account-id", CodexLoginState.AccountId);
        message.Headers.TryAddWithoutValidation("OpenAI-Beta", "responses=experimental");
        message.Headers.TryAddWithoutValidation("originator", "codex_cli_rs");

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        // The Responses API streams SSE lines; collect the response.output_text.done events.
        var text = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var payload = line["data: ".Length..];
            if (payload == "[DONE]") break;

            CodexWire.StreamEvent? ev;
            try { ev = JsonSerializer.Deserialize<CodexWire.StreamEvent>(payload, JsonOptions); }
            catch (JsonException) { continue; }

            if (ev?.Type == "response.output_text.done" && !string.IsNullOrEmpty(ev.Text))
            {
                text.Append(ev.Text);
            }
        }

        var answer = text.ToString().Trim();
        return string.IsNullOrWhiteSpace(answer) ? null : answer;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}

/// <summary>Mutable static for the account header; set at draft time from the cached identity.</summary>
/// <remarks>Single-user desktop app — the coordinator sets this before each draft.</remarks>
internal static class CodexLoginState
{
    /// <summary>The ChatGPT account id from the cached tokens, empty when signed out.</summary>
    public static string AccountId { get; set; } = string.Empty;
}

// ---- wire shapes ----

internal static class OpenAiWire
{
    public sealed record Request(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] Message[] Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    public sealed record Message(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    public sealed record Response(
        [property: JsonPropertyName("choices")] Choice[]? Choices);

    public sealed record Choice(
        [property: JsonPropertyName("message")] MessageBody? Message);

    public sealed record MessageBody(
        [property: JsonPropertyName("content")] string? Content);
}

internal static class AnthropicWire
{
    public sealed record Request(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("system")] string? System,
        [property: JsonPropertyName("messages")] Message[] Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] double Temperature);

    public sealed record Message(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    public sealed record Response(
        [property: JsonPropertyName("content")] Block[]? Content);

    public sealed record Block(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}

internal static class CodexWire
{
    public sealed record Request(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("instructions")] string Instructions,
        [property: JsonPropertyName("input")] InputItem[] Input,
        [property: JsonPropertyName("store")] bool Store,
        [property: JsonPropertyName("stream")] bool Stream);

    public sealed record InputItem(InputText Text);

    public sealed record InputText(
        [property: JsonPropertyName("text")] string Text);

    public sealed record StreamEvent(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);
}

