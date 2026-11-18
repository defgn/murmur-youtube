namespace MeetingTranscriber.Core;

/// <summary>
/// Selects the verbatim quote window for a name alert: the utterances in the last
/// 20 seconds ending at the trigger. A long trigger stands alone; a short one keeps
/// the preceding utterances in the window so the quote still reads as a question.
/// </summary>
public static class AlertWindow
{
    /// <summary>Utterances at or above this trimmed length quote alone.</summary>
    public const int LongTriggerChars = 60;

    /// <summary>How far back the quote window reaches from the trigger.</summary>
    public static TimeSpan Window => TimeSpan.FromSeconds(20);

    /// <summary>Builds the quote for <paramref name="trigger"/> from <paramref name="history"/>.</summary>
    /// <param name="history">Utterances in arrival order; may or may not include the trigger.</param>
    /// <param name="trigger">The utterance that mentioned the name.</param>
    /// <returns>The quoted utterances in arrival order, trigger last. Never empty.</returns>
    public static List<TranscriptUtterance> SelectWindow(
        IReadOnlyList<TranscriptUtterance> history,
        TranscriptUtterance trigger)
    {
        var floor = trigger.At - Window;
        var inWindow = new List<TranscriptUtterance>();
        foreach (var utterance in history)
        {
            if (utterance.At <= trigger.At && utterance.At >= floor)
            {
                inWindow.Add(utterance);
            }
        }

        if (!inWindow.Any(u => u.Id == trigger.Id))
        {
            inWindow.Add(trigger);
        }

        inWindow.Sort(static (a, b) => a.At.CompareTo(b.At));

        if (trigger.Text.Trim().Length >= LongTriggerChars)
        {
            return [trigger];
        }

        return inWindow;
    }

    /// <summary>Joins a quote window into one verbatim string, arrival order preserved.</summary>
    /// <param name="quote">The window from <see cref="SelectWindow"/>.</param>
    /// <returns>The utterances joined with single spaces.</returns>
    public static string FormatQuote(IReadOnlyList<TranscriptUtterance> quote)
    {
        var parts = new List<string>(quote.Count);
        foreach (var utterance in quote)
        {
            var text = utterance.Text.Trim();
            if (text.Length > 0)
            {
                parts.Add(text);
            }
        }

        return string.Join(" ", parts);
    }
}
