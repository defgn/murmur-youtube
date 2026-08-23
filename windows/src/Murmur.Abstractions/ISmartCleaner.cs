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
/// </remarks>
public interface ISmartCleaner
{
    /// <summary>
    /// Cleans <paramref name="text"/>, or returns null when the cleaner cannot run and the
    /// deterministic result should stand.
    /// </summary>
    Task<string?> CleanAsync(string text, CancellationToken cancellationToken);
}
