namespace MeetingTranscriber.Core;

/// <summary>One summary card: a short label plus a single line.</summary>
/// <param name="Label">Uppercase tag such as BRIDGE or OPEN FOR GIANG.</param>
/// <param name="Line">One line of content.</param>
public sealed record SummaryCard(string Label, string Line);

/// <summary>The alert strip content: the verbatim quote plus two one-liners.</summary>
/// <param name="Quote">Verbatim trigger window text.</param>
/// <param name="AskLine">One line on what they are asking.</param>
/// <param name="ReplyLine">One line suggested reply.</param>
/// <param name="Notice">User readable reason when the LLM path failed, else null.</param>
public sealed record AlertContent(string Quote, string AskLine, string ReplyLine, string? Notice);

/// <summary>
/// Prompt builders and answer parsers for the two LLM calls: the name alert fast path
/// and the discussion summary regen. Pure string work, no network, fully testable.
/// </summary>
public static class MeetingPrompts
{
    /// <summary>Maximum transcript lines sent with a summary request.</summary>
    public const int MaxSummaryLines = 30;

    /// <summary>Builds the short prompt that turns a name trigger into two one-liners.</summary>
    /// <param name="quote">The verbatim quote window.</param>
    /// <returns>System and user prompts for the chat completer.</returns>
    public static (string System, string User) BuildAlertPrompt(
        IReadOnlyList<TranscriptUtterance> quote)
    {
        const string system =
            "You help Giang in a live meeting. Answer in UK English. " +
            "Reply with exactly two lines and nothing else. " +
            "First line starts with ASK: followed by one line on what they are asking Giang. " +
            "Second line starts with REPLY: followed by one short suggested reply under 25 words.";
        var user = "Giang was just addressed by name. Quote: \"" +
            AlertWindow.FormatQuote(quote) + "\"";
        return (system, user);
    }

    /// <summary>Parses the alert answer, falling back to readable placeholders.</summary>
    /// <param name="quote">The verbatim quote window.</param>
    /// <param name="answer">The completer response, or null on failure.</param>
    /// <param name="error">The completer reason, or null when it succeeded.</param>
    /// <returns>Strip content; never null, never silent.</returns>
    public static AlertContent ParseAlertAnswer(
        IReadOnlyList<TranscriptUtterance> quote,
        string? answer,
        string? error)
    {
        var formatted = AlertWindow.FormatQuote(quote);
        if (string.IsNullOrWhiteSpace(answer))
        {
            var reason = string.IsNullOrWhiteSpace(error) ? "no answer backend configured" : error.Trim();
            return new AlertContent(
                formatted,
                "Unavailable: " + reason + ". See the quote above.",
                "Unavailable: " + reason + ".",
                reason);
        }

        string? ask = null;
        string? reply = null;
        var lines = answer.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("ASK:", StringComparison.OrdinalIgnoreCase))
            {
                ask ??= line["ASK:".Length..].Trim();
            }
            else if (line.StartsWith("REPLY:", StringComparison.OrdinalIgnoreCase))
            {
                reply ??= line["REPLY:".Length..].Trim();
            }
        }

        ask = string.IsNullOrWhiteSpace(ask) ? answer.Trim() : ask;
        reply = string.IsNullOrWhiteSpace(reply)
            ? "No suggested reply parsed; see the quote above."
            : reply;
        return new AlertContent(formatted, ask, reply, null);
    }

    /// <summary>Builds the prompt that turns recent transcript into briefing cards.</summary>
    /// <param name="recent">Recent utterances, oldest first.</param>
    /// <returns>System and user prompts for the chat completer.</returns>
    public static (string System, string User) BuildSummaryPrompt(
        IReadOnlyList<TranscriptUtterance> recent)
    {
        const string system =
            "You brief Giang on a live meeting. Answer in UK English. " +
            "Reply with at most 4 lines and nothing else. " +
            "Each line is LABEL: one line, where LABEL is an uppercase topic tag. " +
            "If Giang was asked something still open, make the last card start with OPEN FOR GIANG: " +
            "followed by the open question in under 15 words.";
        var take = Math.Min(recent.Count, MaxSummaryLines);
        var lines = new List<string>(take);
        for (var i = recent.Count - take; i < recent.Count; i++)
        {
            var utterance = recent[i];
            var who = utterance.Feed == FeedKind.You ? "YOU" : "THEM";
            lines.Add(who + ": " + utterance.Text.Trim());
        }

        return (system, "Transcript so far:\n" + string.Join("\n", lines));
    }

    /// <summary>Parses summary cards, keeping at most 4.</summary>
    /// <param name="answer">The completer response, or null on failure.</param>
    /// <returns>The parsed cards, possibly empty when nothing parsed.</returns>
    public static List<SummaryCard> ParseSummaryAnswer(string? answer)
    {
        var cards = new List<SummaryCard>(4);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return cards;
        }

        var lines = answer.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            if (cards.Count >= 4)
            {
                break;
            }

            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var label = line[..colon].Trim();
            var body = line[(colon + 1)..].Trim();
            if (label.Length == 0 || body.Length == 0)
            {
                continue;
            }

            cards.Add(new SummaryCard(label, body));
        }

        return cards;
    }
}
