namespace Murmur.Core;

/// <summary>
/// Decides whether a transcript piece is an interview question worth answering.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately heuristic, not a model call: detection runs on every utterance, so it must
/// be instant, free, and predictable. The heuristics are generous on purpose — a false
/// positive costs one draft the user glances past; a false negative is a missed answer in a
/// live interview. The AI answer pass is where judgement belongs.
/// </para>
/// </remarks>
public static class QuestionDetector
{
    /// <summary>Interrogative openers, matched case-insensitively at the start.</summary>
    private static readonly string[] Openers =
    [
        "what", "how", "why", "when", "where", "which", "who", "whom", "whose",
        "can you", "could you", "would you", "will you", "do you", "does ", "did you",
        "have you", "has ", "are you", "is there", "are there", "tell me about",
        "tell me how", "tell me why", "describe", "explain", "walk me through",
        "talk me through", "give me an example", "give an example", "share an example",
        "imagine", "suppose", "in your experience", "what if",
    ];

    /// <summary>
    /// True when <paramref name="text"/> looks like a question directed at the candidate.
    /// </summary>
    public static bool IsQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();

        // An explicit question mark is the strongest signal there is.
        if (trimmed.Contains('?')) return true;

        var lowered = trimmed.ToLowerInvariant();
        foreach (var opener in Openers)
        {
            if (lowered.StartsWith(opener, StringComparison.Ordinal)) return true;
        }

        // "…, right?" / "…, yeah?" tails read as questions without a mark the model heard.
        if (lowered.EndsWith(", right", StringComparison.Ordinal) || lowered.EndsWith(", yeah", StringComparison.Ordinal) || lowered.EndsWith(", ok", StringComparison.Ordinal)) return true;

        return false;
    }
}
