using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>Which cloud AI service to call.</summary>
public enum CloudProvider
{
    /// <summary>OpenAI (ChatGPT models) — api.openai.com.</summary>
    OpenAi,

    /// <summary>Anthropic (Claude models) — api.anthropic.com.</summary>
    Anthropic,

    /// <summary>Any OpenAI-compatible endpoint: Azure OpenAI, Groq, OpenRouter, LM Studio…</summary>
    OpenAiCompatible,
}

/// <summary>
/// Cloud chat completions: OpenAI, Anthropic, or any OpenAI-compatible endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The API key lives only in the settings file on the user's PC and travels in the
/// Authorization header of each request. It is never logged, never included in a
/// transcript, and never sent anywhere except the configured endpoint.
/// </para>
/// <para>
/// Anthropic's shape differs from OpenAI's (system prompt is a top-level field, and the
/// key rides a custom header), so both protocols are hand-rolled against source-generated
/// JSON contexts — no extra dependencies.
/// </para>
/// <para>
/// Like every completer, every failure returns null: unreachable service, rejected key,
/// timeout, or an unusable body. The caller shows a readable state instead of crashing.
/// </para>
/// </remarks>
public sealed class CloudChatCompleter : IChatCompleter
{
    private static readonly Dictionary<CloudProvider, string> BaseUris = new()
    {
        [CloudProvider.OpenAi] = "https://api.openai.com/v1/",
        [CloudProvider.Anthropic] = "https://api.anthropic.com/v1/",
        [CloudProvider.OpenAiCompatible] = "https://api.openai.com/v1/",   // placeholder; overridden
    };

    // Short by chat standards: an interview answer that takes 30 s to arrive is useless.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    private readonly HttpClient _http;
    private readonly CloudProvider _provider;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUri;
    private readonly bool _ownsHttpClient;

    /// <summary>Builds a completer for a cloud provider.</summary>
    /// <param name="provider">Which service to call.</param>
    /// <param name="apiKey">The user's API key; sent per-request, never persisted elsewhere.</param>
    /// <param name="model">The model id, e.g. "gpt-4o-mini" or "claude-sonnet-4-5".</param>
    /// <param name="customBaseUri">
    /// For <see cref="CloudProvider.OpenAiCompatible"/>: the endpoint root, e.g.
    /// "https://your-resource.openai.azure.com/openai/deployments/your-deploy/".
    /// </param>
    public CloudChatCompleter(
        CloudProvider provider, string apiKey, string model, string? customBaseUri = null)
    {
        _provider = provider;
        _apiKey = apiKey;
        _model = model;
        _baseUri = provider == CloudProvider.OpenAiCompatible && !string.IsNullOrWhiteSpace(customBaseUri)
            ? customBaseUri.TrimEnd('/') + "/"
            : BaseUris[provider];
        _http = new HttpClient { Timeout = Timeout };
        _ownsHttpClient = true;
    }

    /// <summary>Test constructor over a pre-wired handler — never touches the network.</summary>
    public CloudChatCompleter(HttpMessageHandler handler, CloudProvider provider, string apiKey, string model)
    {
        _provider = provider;
        _apiKey = apiKey;
        _model = model;
        _baseUri = BaseUris[provider];
        _http = new HttpClient(handler) { Timeout = Timeout };
        _ownsHttpClient = true;
    }

    /// <inheritdoc />
    public async Task<string?> CompleteAsync(
        string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return null;

        try
        {
            return _provider == CloudProvider.Anthropic
                ? await CompleteAnthropicAsync(systemPrompt, userText, cancellationToken).ConfigureAwait(false)
                : await CompleteOpenAiAsync(systemPrompt, userText, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>POST /chat/completions (OpenAI shape; works for compatible endpoints too).</summary>
    private async Task<string?> CompleteOpenAiAsync(
        string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        var request = new OpenAiChatRequest(
            Model: _model,
            Messages:
            [
                new OpenAiMessage("system", systemPrompt),
                new OpenAiMessage("user", userText),
            ],
            Temperature: 0.7,
            MaxTokens: 700);

        using var message = new HttpRequestMessage(HttpMethod.Post, _baseUri + "chat/completions")
        {
            Content = JsonContent.Create(request, CloudJsonContext.Default.OpenAiChatRequest),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http
            .SendAsync(message, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content
            .ReadFromJsonAsync(CloudJsonContext.Default.OpenAiChatResponse, cancellationToken)
            .ConfigureAwait(false);

        var text = body?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>POST /messages (Anthropic shape: system on top, key in x-api-key).</summary>
    private async Task<string?> CompleteAnthropicAsync(
        string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        var request = new AnthropicRequest(
            Model: _model,
            System: systemPrompt,
            Messages: [new AnthropicMessage("user", userText)],
            MaxTokens: 700,
            Temperature: 0.7);

        using var message = new HttpRequestMessage(HttpMethod.Post, _baseUri + "messages")
        {
            Content = JsonContent.Create(request, CloudJsonContext.Default.AnthropicRequest),
        };
        message.Headers.Add("x-api-key", _apiKey);
        message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var response = await _http
            .SendAsync(message, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content
            .ReadFromJsonAsync(CloudJsonContext.Default.AnthropicResponse, cancellationToken)
            .ConfigureAwait(false);

        // Content is a list of blocks; concatenate the text ones.
        var text = string.Concat(
            (body?.Content ?? [])
            .Where(b => b.Type == "text")
            .Select(b => b.Text ?? string.Empty)).Trim();

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}

// ----- wire shapes (source-generated JSON, no reflection) -----

/// <summary>An OpenAI-shape chat completion request.</summary>
/// <param name="Model">The model id.</param>
/// <param name="Messages">The conversation: system prompt then user message.</param>
/// <param name="Temperature">Sampling temperature; raised a little so re-drafts differ.</param>
/// <param name="MaxTokens">Answer length cap.</param>
public sealed record OpenAiChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] OpenAiMessage[] Messages,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("max_tokens")] int MaxTokens);

/// <summary>One chat message.</summary>
/// <param name="Role">"system" or "user".</param>
/// <param name="Content">The message text.</param>
public sealed record OpenAiMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

/// <summary>The chat completion response, choices only — usage fields are ignored.</summary>
public sealed record OpenAiChatResponse(
    [property: JsonPropertyName("choices")] OpenAiChoice[]? Choices);

/// <summary>One choice in the response.</summary>
public sealed record OpenAiChoice(
    [property: JsonPropertyName("message")] OpenAiResponseMessage? Message);

/// <summary>The assistant message inside a choice.</summary>
public sealed record OpenAiResponseMessage(
    [property: JsonPropertyName("content")] string? Content);

/// <summary>An Anthropic-shape messages request.</summary>
/// <param name="Model">The model id.</param>
/// <param name="System">The system prompt (a top-level field in Anthropic's API).</param>
/// <param name="Messages">The user turn.</param>
/// <param name="MaxTokens">Answer length cap (required by Anthropic).</param>
/// <param name="Temperature">Sampling temperature.</param>
public sealed record AnthropicRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("system")] string? System,
    [property: JsonPropertyName("messages")] AnthropicMessage[] Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("temperature")] double Temperature);

/// <summary>One message in an Anthropic conversation.</summary>
/// <param name="Role">"user".</param>
/// <param name="Content">The message text.</param>
public sealed record AnthropicMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

/// <summary>An Anthropic response: a list of content blocks.</summary>
public sealed record AnthropicResponse(
    [property: JsonPropertyName("content")] AnthropicBlock[]? Content);

/// <summary>One content block; only "text" blocks carry answer text.</summary>
/// <param name="Type">"text" or "tool_use" — this client reads text only.</param>
/// <param name="Text">The block's text, when <paramref name="Type"/> is "text".</param>
public sealed record AnthropicBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text);

/// <summary>Source-generated JSON for the cloud wire shapes.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAiChatRequest))]
[JsonSerializable(typeof(OpenAiChatResponse))]
[JsonSerializable(typeof(AnthropicRequest))]
[JsonSerializable(typeof(AnthropicResponse))]
public sealed partial class CloudJsonContext : JsonSerializerContext;
