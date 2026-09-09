namespace Murmur.Abstractions;

/// <summary>
/// One chat completion against any instruction model — bundled GGUF, Ollama, or a cloud API.
/// </summary>
/// <remarks>
/// <para>
/// The interview assistant needs raw completions with a system and a user message; the
/// smart-cleaner interface (<see cref="ISmartCleaner"/>) is shaped for transcript polishing
/// and does not fit. One narrow method keeps every backend interchangeable: the answer
/// engine cannot tell which model is answering, which is what makes the backend switchable
/// on the fly.
/// </para>
/// <para>
/// Like <see cref="ISmartCleaner"/>, failure is null — never an exception and never a
/// fabricated answer. The UI shows a readable failure state instead.
/// </para>
/// </remarks>
public interface IChatCompleter : IDisposable
{
    /// <summary>
    /// Runs one completion, or returns null when the backend cannot run (no key, service
    /// down, timeout, unusable response).
    /// </summary>
    Task<string?> CompleteAsync(
        string systemPrompt, string userText, CancellationToken cancellationToken);

    /// <summary>
    /// Why the most recent CompleteAsync returned null, in user-readable words, or null
    /// when it succeeded. Implementations must never include credentials here.
    /// </summary>
    string? LastError => null;
}
