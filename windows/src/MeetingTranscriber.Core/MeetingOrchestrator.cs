using Murmur.Abstractions;

namespace MeetingTranscriber.Core;

/// <summary>A fresh name detection: trigger, quote window and mention counts.</summary>
/// <param name="Trigger">The utterance that mentioned the name.</param>
/// <param name="Quote">The verbatim quote window, trigger last.</param>
/// <param name="EarlierCount">Mentions before this one, for "+N earlier".</param>
/// <param name="TotalCount">Total mentions this session.</param>
public sealed record MeetingAlert(
    TranscriptUtterance Trigger,
    List<TranscriptUtterance> Quote,
    int EarlierCount,
    int TotalCount);

/// <summary>Summary regen outcome: cards plus when they landed and any failure reason.</summary>
/// <param name="Cards">Up to 4 briefing cards.</param>
/// <param name="UpdatedAt">When this update was produced.</param>
/// <param name="Notice">User readable reason when the LLM path failed, else null.</param>
public sealed record SummaryUpdate(
    List<SummaryCard> Cards,
    DateTimeOffset UpdatedAt,
    string? Notice);

/// <summary>
/// Feeds utterances to detection, alert windowing, mention counting and summary
/// throttling. Audio free: callers push finished utterances, no devices involved, so
/// the whole flow runs in unit tests. LLM calls are short async methods the UI fires
/// without awaiting, so the transcript never waits on the summary.
/// </summary>
public sealed class MeetingOrchestrator : IDisposable
{
    private readonly Func<DateTimeOffset> _clock;
    private IChatCompleter? _completer;
    private readonly SummaryScheduler _scheduler;
    private readonly MentionTracker _mentions = new();
    private readonly List<TranscriptUtterance> _history = new();
    private readonly SemaphoreSlim _summaryGate = new(1, 1);
    private bool _disposed;

    /// <summary>Builds an orchestrator over an optional completer and a clock.</summary>
    /// <param name="completer">The answer backend, or null when unconfigured.</param>
    /// <param name="clock">Current time source; pass a fake in tests.</param>
    public MeetingOrchestrator(IChatCompleter? completer, Func<DateTimeOffset> clock)
    {
        _completer = completer;
        _clock = clock;
        _scheduler = new SummaryScheduler(clock);
    }

    /// <summary>Mention counts across detections; dismiss survives here.</summary>
    public MentionTracker Mentions => _mentions;

    /// <summary>Swaps the answer backend live when settings change. Never disposes the old one.</summary>
    /// <param name="completer">The new backend, or null when unconfigured.</param>
    public void ConfigureCompleter(IChatCompleter? completer) => _completer = completer;

    /// <summary>All utterances added so far, arrival order.</summary>
    public List<TranscriptUtterance> History => [.. _history];

    /// <summary>Utterances noted since the last summary regen.</summary>
    public int UtterancesSinceSummary => _scheduler.UtterancesSinceRegen;

    /// <summary>Adds a finished utterance. Returns alert data when the name was heard.</summary>
    /// <param name="feed">Which feed the utterance arrived on.</param>
    /// <param name="text">Verbatim transcript text.</param>
    /// <param name="at">Arrival time.</param>
    /// <returns>Alert data on a hit, else null. Also notes the utterance for summary.</returns>
    public MeetingAlert? AddUtterance(FeedKind feed, string text, DateTimeOffset at)
    {
        var utterance = new TranscriptUtterance(Guid.NewGuid(), at, feed, text);
        _history.Add(utterance);
        _scheduler.NoteUtterance();

        if (!NameDetector.MentionsName(text))
        {
            return null;
        }

        var total = _mentions.Record(at);
        var quote = AlertWindow.SelectWindow(_history, utterance);
        return new MeetingAlert(utterance, quote, total - 1, total);
    }

    /// <summary>True when the summary throttle says a regen is due.</summary>
    /// <returns>True when the caller should regenerate.</returns>
    public bool IsSummaryDue() => _scheduler.CheckTime();

    /// <summary>Dismisses the visible alert. The count survives for the next detection.</summary>
    public void DismissAlert() => _mentions.Dismiss();

    /// <summary>
    /// Resolves the alert one-liners through the fast single prompt path. Never throws:
    /// without a backend or on an LLM failure it returns the verbatim quote with
    /// readable placeholders.
    /// </summary>
    /// <param name="quote">The window from <see cref="MeetingAlert"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Strip content with quote plus one-liners or placeholders.</returns>
    public async Task<AlertContent> ResolveAlertAsync(
        List<TranscriptUtterance> quote,
        CancellationToken cancellationToken)
    {
        if (_completer is null)
        {
            return MeetingPrompts.ParseAlertAnswer(quote, null, "no answer backend configured");
        }

        var prompt = MeetingPrompts.BuildAlertPrompt(quote);
        var answer = await _completer
            .CompleteAsync(prompt.System, prompt.User, cancellationToken)
            .ConfigureAwait(false);
        return MeetingPrompts.ParseAlertAnswer(quote, answer, _completer.LastError);
    }

    /// <summary>
    /// Regenerates the briefing cards. Serialised so overlapping throttles cannot stack,
    /// and never throws: failures return the previous cards with a readable notice.
    /// </summary>
    /// <param name="previous">Cards currently on screen.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The update: fresh cards on success, previous cards plus a notice otherwise.</returns>
    public async Task<SummaryUpdate> RegenerateSummaryAsync(
        List<SummaryCard> previous,
        CancellationToken cancellationToken)
    {
        await _summaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _scheduler.MarkRegenerated();
            var now = _clock();
            var prompt = MeetingPrompts.BuildSummaryPrompt(_history);
            if (_completer is null)
            {
                return new SummaryUpdate(
                    previous, now, "Summary unavailable: no answer backend configured.");
            }

            var answer = await _completer
                .CompleteAsync(prompt.System, prompt.User, cancellationToken)
                .ConfigureAwait(false);
            var cards = MeetingPrompts.ParseSummaryAnswer(answer);
            if (cards.Count == 0)
            {
                var reason = string.IsNullOrWhiteSpace(_completer.LastError)
                    ? "the provider returned an unreadable summary"
                    : _completer.LastError.Trim();
                return new SummaryUpdate(previous, now, "Summary unavailable: " + reason + ".");
            }

            return new SummaryUpdate(cards, now, null);
        }
        finally
        {
            _summaryGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _summaryGate.Dispose();
    }
}
