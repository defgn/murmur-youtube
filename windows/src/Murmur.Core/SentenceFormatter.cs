using System.Text;
using System.Text.RegularExpressions;

namespace Murmur.Core;

/// <summary>
/// The deterministic sentence tier: collapses spacing, capitalizes each sentence, and
/// adds terminal punctuation. No LLM, no ambiguity — the same philosophy as the filler
/// and arithmetic passes. This is what makes the deterministic output genuinely usable
/// even with Smart cleanup switched off.
/// </summary>
/// <remarks>
/// Deliberately conservative: it never rewrites words, never changes numbers (decimals
/// are protected), and never removes content. The LLM pass, when enabled, runs after
/// this and may refine further.
/// </remarks>
public static class SentenceFormatter
{
    // Standalone "i" is the pronoun and always capitalizes.
    private static readonly Regex LoneI = new(@"(?<![a-zA-Z])i(?![a-zA-Z])", RegexOptions.CultureInvariant);

    // A sentence ends at . ! ? — optionally followed by a closing quote or bracket.
    private static readonly Regex SentenceEnd = new(@"[.!?](?:\u201d|\u2019|""|'|\)|\])?", RegexOptions.CultureInvariant);

    /// <summary>Formats <paramref name="text"/>: spacing, sentence capitals, terminal punctuation.</summary>
    public static string Format(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // 1. Collapse any whitespace run to a single space.
        var collapsed = Regex.Replace(text, @"\s+", " ").Trim();

        // 2. No space before punctuation; a space after a sentence ending (but never
        //    between digits — "3.14" must survive).
        collapsed = Regex.Replace(collapsed, @"\s+([,.;:!?])", "$1");
        collapsed = Regex.Replace(collapsed, @"([.!?])(?=[A-Za-z])", "$1 ");

        // 3. The pronoun, anywhere in the sentence.
        collapsed = LoneI.Replace(collapsed, "I");

        // 4. Capitalize the start of each sentence.
        var sb = new StringBuilder(collapsed);
        CapitalizeAt(sb, 0);
        foreach (Match m in SentenceEnd.Matches(collapsed))
        {
            CapitalizeAt(sb, m.Index + m.Length);
        }

        // 5. Terminal punctuation, once.
        var result = sb.ToString().TrimEnd();
        if (result.Length > 0 && result[^1] is not ('.' or '!' or '?'))
        {
            result += ".";
        }

        return result;
    }

    private static void CapitalizeAt(StringBuilder sb, int index)
    {
        while (index < sb.Length && sb[index] == ' ') index++;
        if (index < sb.Length && char.IsLetter(sb[index]))
        {
            sb[index] = char.ToUpperInvariant(sb[index]);
        }
    }
}
