using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// The bundled GGUF answering interview questions in-process.
/// </summary>
/// <remarks>
/// Shares its model with the smart-clean <see cref="BundledCleaner"/> shape but owns its
/// own instance: the interview persona prompt differs, and a 1.5B model holds two small
/// contexts comfortably. Lazy-load on first answer, same as the cleaner.
/// </remarks>
public sealed class BundledChatCompleter : IChatCompleter
{
    private BundledCleaner? _inner;

    /// <inheritdoc />
    public async Task<string?> CompleteAsync(
        string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        // The bundled cleaner exposes Clean/Transform only; the general chat shape maps to
        // Transform with the system prompt folded into the instruction.
        _inner ??= new BundledCleaner();
        var instruction = systemPrompt + "\n\n" + userText;
        return await _inner.TransformAsync("Answer now.", instruction, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _inner?.Dispose();
}

/// <summary>
/// An Ollama model answering interview questions over the local HTTP API.
/// </summary>
public sealed class OllamaChatCompleter : IChatCompleter
{
    private readonly OllamaCleaner _inner;

    /// <summary>Builds over the given Ollama model tag (null = auto-pick).</summary>
    public OllamaChatCompleter(string? model) =>
        _inner = new OllamaCleaner(model);

    /// <inheritdoc />
    public async Task<string?> CompleteAsync(
        string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        var instruction = systemPrompt + "\n\n" + userText;
        return await _inner.TransformAsync("Answer now.", instruction, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
