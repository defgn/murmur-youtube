using System.Text.RegularExpressions;

namespace MeetingTranscriber.Core;

/// <summary>
/// Detects the user's spoken name in a finalised utterance. The recogniser renders the
/// name several ways, so giang, yang, jang and jiang all count as a hit.
/// </summary>
/// <remarks>
/// Matching is letter-boundary based: the variant must sit between non-letters (or the
/// ends of the text). That keeps "Giang?" and "yang," positive while "giant" and
/// "Jiangsu" stay negative, and a lone "G." can never match.
/// </remarks>
public static partial class NameDetector
{
    /// <summary>The matched variant in upper invariant form (GIANG, YANG, JANG or JIANG).</summary>
    /// <param name="text">Finalised utterance text, or null.</param>
    /// <returns>The variant heard, or null when the name was not said.</returns>
    public static string? MatchedVariant(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = NamePattern().Match(text);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    /// <summary>True when <paramref name="text"/> contains the name in any recognised spelling.</summary>
    /// <param name="text">Finalised utterance text, or null.</param>
    /// <returns>True on a hit, false otherwise.</returns>
    public static bool MentionsName(string? text) => MatchedVariant(text) is not null;

    [GeneratedRegex(
        @"(?<![A-Za-z])(?:giang|yang|jang|jiang)(?![A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
