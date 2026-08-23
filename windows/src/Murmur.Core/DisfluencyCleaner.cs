using System.Text.RegularExpressions;

namespace Murmur.Core;

/// <summary>
/// Removes spoken disfluencies — the "um", "er", "uh" hesitations and the discourse
/// fillers ("actually", "you know") that a speech engine transcribes faithfully and a
/// dictation user does not want on the page.
/// </summary>
/// <remarks>
/// <para>
/// Three tiers, deliberately conservative about meaning:
/// </para>
/// <list type="number">
/// <item><b>Unconditional fillers</b> — "um", "uh", "er", "hmm" and friends. Never
/// meaningful on their own, removed wherever they stand, case insensitive, with any
/// attached punctuation.</item>
/// <item><b>Sentence-initial filler markers</b> — "actually", "basically", "literally",
/// "honestly", "frankly". At the start of a sentence these are almost always fillers
/// (the user's "…like, actually…"), so they are removed whether or not a comma follows.
/// "Actually he did it" loses emphasis but keeps its meaning.</item>
/// <item><b>Comma-gated markers</b> — "well", "so", "right", "okay", "like", "you know",
/// "i mean". Removed only when they open a sentence <i>and</i> are set off by a comma,
/// the form a genuine filler takes ("Well, I think…"). "So the answer is 42" or
/// "Like I said…" survive, because they carry meaning.</item>
/// </list>
/// <para>
/// <c>CultureInvariant</c> matters here for the same reason it does in the dictionary:
/// a culture-sensitive case-insensitive match would let Turkish <c>İ</c> behave
/// differently. The patterns are deliberately ASCII-only.
/// </para>
/// </remarks>
public static class DisfluencyCleaner
{
    /// <summary>
    /// Words that are never meaningful on their own. Removed unconditionally, anywhere.
    /// Order matters: longer alternatives first, so the alternation matches "uh-huh"
    /// whole instead of letting the bare "uh" alternative eat its first half.
    /// </summary>
    private static readonly string[] Fillers =
    [
        "uh-huh", "mm-hmm", "mhmm", "ahem",
        "hmm", "mmm", "erm", "mhm",
        "um", "uh", "er", "ah", "eh", "hm", "mm",
    ];

    /// <summary>
    /// Removed at the start of a sentence whether or not a comma follows.
    /// </summary>
    private static readonly string[] FillerMarkers =
    [
        "actually", "basically", "literally", "honestly", "frankly",
    ];

    /// <summary>
    /// Removed at the start of a sentence only when set off by a comma.
    /// </summary>
    private static readonly string[] CommaGatedMarkers =
    [
        "well", "so", "right", "okay", "like", "you know", "i mean",
    ];

    private static readonly Regex FillerRegex = new(
        $@"\b(?:{string.Join("|", Fillers)})\b[,.;!?]*\s?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FillerMarkerRegex = new(
        $@"(?<=^|[.!?]\s+)(?:{string.Join("|", FillerMarkers)}),?\s?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CommaGatedMarkerRegex = new(
        $@"(?<=^|[.!?]\s+)(?:{string.Join("|", CommaGatedMarkers)})\s*,",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ExtraSpaceRegex = new(
        @"\s{2,}",
        RegexOptions.CultureInvariant);

    /// <summary>Removes disfluencies from a transcript.</summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Order matters: fillers first, so "um, actually" becomes " actually" and the
        // marker pass then sees a clean sentence start.
        var result = FillerRegex.Replace(text, string.Empty);
        result = FillerMarkerRegex.Replace(result, string.Empty);
        result = CommaGatedMarkerRegex.Replace(result, string.Empty);

        result = ExtraSpaceRegex.Replace(result, " ");

        // A marker removed at the very start leaves a leading space.
        return result.Trim();
    }
}
