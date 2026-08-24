namespace Murmur.Abstractions;

/// <summary>
/// An optional, intent-aware transcript cleanup pass — the Wispr-grade layer.
/// </summary>
/// <remarks>
/// <para>
/// The deterministic passes (filler removal, arithmetic simplification) handle the known
/// shapes quickly and without hallucinating. This interface is the escape hatch for
/// everything else: a local language model that reads the transcript and polishes it —
/// punctuation, casing, and spoken self-corrections in any phrasing.
/// </para>
/// <para>
/// It must never fabricate. Implementations return null when they cannot run (service down,
/// timeout, model missing) so the caller falls back to the deterministic text — a cleanup
/// pass that invents content is worse than no cleanup at all.
/// </para>
/// <para>
/// Implementations own a model or service connection and release it in
/// <see cref="IDisposable.Dispose"/>.
/// </para>
/// </remarks>
public interface ISmartCleaner : IDisposable
{
    /// <summary>
    /// Cleans <paramref name="text"/>, or returns null when the cleaner cannot run and the
    /// deterministic result should stand.
    /// </summary>
    Task<string?> CleanAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites <paramref name="text"/> per a spoken <paramref name="instruction"/> —
    /// Command Mode ("make this more formal"). Returns the rewritten text, or null when
    /// the pass could not run or the model declined to change anything.
    /// </summary>
    Task<string?> TransformAsync(string instruction, string text, CancellationToken cancellationToken);

    /// <summary>
    /// True when the cleaner holds resident model memory in <i>this process</i> that
    /// idle-unloading frees (the bundled GGUF). Ollama holds its model in its own process,
    /// so it returns false and <see cref="Unload"/> is a no-op.
    /// </summary>
    bool CanUnload { get; }

    /// <summary>
    /// Frees the resident model, if any; the next call reloads it. Called when Woffle has
    /// been idle — a lean app should not hold the LLM's ~1 GB when nobody is dictating.
    /// </summary>
    void Unload();
}
