using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// The optional local-AI cleanup pass, served by Ollama on the same machine.
/// </summary>
/// <remarks>
/// <para>
/// Woffle deliberately bundles no language model: the PC it runs on already hosts one
/// (Ollama's default model, e.g. the Qwen 3.6 27B the user runs on their GPU). This client
/// speaks the Ollama HTTP API on localhost, so enabling the smart pass costs zero download
/// and zero extra RAM inside Woffle itself — the model already lives in Ollama's memory.
/// </para>
/// <para>
/// Failure is a fallback, never an error: if Ollama is not running, the model is missing,
/// the call times out, or the response is unusable, <see cref="CleanAsync"/> returns null
/// and the caller keeps the deterministic text. A cleanup pass that invents content is
/// worse than none, so the prompt forbids adding or dropping information and the response
/// is passed through unverified.
/// </para>
/// </remarks>
public sealed class OllamaCleaner : ISmartCleaner, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUri;
    private readonly string? _modelOverride;
    private string? _resolvedModel;
    private readonly object _modelLock = new();

    /// <summary>Builds a cleaner for <paramref name="baseUri"/> (defaults to Ollama's).</summary>
    /// <param name="model">
    /// The Ollama model tag to use, or null to auto-pick the first installed model.
    /// </param>
    /// <param name="baseUri">Ollama's API root; overridable for tests.</param>
    public OllamaCleaner(string? model = null, string baseUri = "http://127.0.0.1:11434")
    {
        _modelOverride = string.IsNullOrWhiteSpace(model) ? null : model;
        _baseUri = baseUri;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// Builds a cleaner over a pre-wired handler, so tests never touch the network.
    /// </summary>
    public OllamaCleaner(HttpMessageHandler handler, string? model = null, string baseUri = "http://127.0.0.1:11434")
    {
        _modelOverride = string.IsNullOrWhiteSpace(model) ? null : model;
        _baseUri = baseUri;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <inheritdoc />
    public Task<string?> CleanAsync(string text, CancellationToken cancellationToken) =>
        CompleteAsync(
            "You clean up dictated transcripts for typing into a document. " +
            "Fix punctuation, capitalization and spacing. Resolve spoken self-corrections " +
            "and arithmetic (\"three potatoes no one potato no three minus one potatoes\" " +
            "becomes \"2 potatoes\"). Remove spoken fillers like um and er. " +
            "Never invent, add or drop information: no new numbers, names or facts. " +
            "Keep the meaning and language exactly. Reply with only the cleaned text.",
            text, cancellationToken);

    /// <inheritdoc />
    public Task<string?> TransformAsync(string instruction, string text, CancellationToken cancellationToken) =>
        CompleteAsync(
            "You rewrite selected text according to a spoken instruction. " +
            "Follow the instruction exactly (make it more formal, turn it into bullet points, " +
            "fix the grammar, shorten it). Keep the meaning and all facts; never invent " +
            "information. Reply with only the rewritten text, no commentary.",
            $"Selected text:\n{text}\n\nInstruction: {instruction}", cancellationToken);

    /// <summary>One chat round-trip: system prompt, user text, greedy decoding.</summary>
    private async Task<string?> CompleteAsync(
        string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        try
        {
            var model = await ResolveModelAsync(cancellationToken).ConfigureAwait(false);
            if (model is null) return null;

            var request = new OllamaChatRequest(model, [
                new OllamaChatMessage("system", systemPrompt),
                new OllamaChatMessage("user", userText),
            ]);

            using var response = await _http
                .PostAsJsonAsync($"{_baseUri}/api/chat", request,
                    OllamaJsonContext.Default.OllamaChatRequest, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content
                .ReadFromJsonAsync(OllamaJsonContext.Default.OllamaChatResponse, cancellationToken)
                .ConfigureAwait(false);

            var cleaned = body?.Message?.Content?.Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Service down, timeout, or garbage response: the deterministic text stands.
            return null;
        }
    }

    /// <summary>Picks the model: the override, or the first installed on this Ollama.</summary>
    private async Task<string?> ResolveModelAsync(CancellationToken cancellationToken)
    {
        if (_modelOverride is not null) return _modelOverride;

        lock (_modelLock)
        {
            if (_resolvedModel is not null) return _resolvedModel;
        }

        try
        {
            using var response = await _http
                .GetAsync($"{_baseUri}/api/tags", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var tags = await response.Content
                .ReadFromJsonAsync(OllamaJsonContext.Default.OllamaTagsResponse, cancellationToken)
                .ConfigureAwait(false);

            var model = tags?.Models?.Select(m => m.Name).FirstOrDefault();
            if (model is null) return null;

            lock (_modelLock)
            {
                _resolvedModel ??= model;
            }
            return _resolvedModel;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
