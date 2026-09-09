using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>Which side of the conversation an utterance came from.</summary>
public enum Speaker
{
    /// <summary>The interviewer — captured from the system/loopback feed.</summary>
    Interviewer,

    /// <summary>The candidate — captured from the microphone.</summary>
    Candidate,
}

/// <summary>One finished utterance from one side of the conversation.</summary>
public sealed record InterviewTurn(
    DateTimeOffset At,
    Speaker Speaker,
    string Text,
    bool IsQuestion);

/// <summary>
/// One drafted answer: the long form and the short talking points, plus bookkeeping.
/// </summary>
public sealed record InterviewAnswer(
    string Question,
    string FullAnswer,
    string ShortAnswer,
    int Attempt,
    TimeSpan Drafted);

/// <summary>What the assistant is doing right now.</summary>
public enum InterviewState
{
    /// <summary>Listening and transcribing; no question on screen.</summary>
    Listening,

    /// <summary>A question was detected and the answer is being drafted.</summary>
    Drafting,

    /// <summary>An answer is on screen.</summary>
    Answered,
}

/// <summary>
/// The interview assistant pipeline: rolling transcripts in, questions and answers out.
/// </summary>
/// <remarks>
/// <para>
/// Two capture feeds arrive as finished utterances (already transcribed by the dictation
/// engine's recognizer — speaker attribution is free because it comes from which device the
/// audio entered through, so no diarisation model is involved). The interviewer feed runs
/// through <see cref="QuestionDetector"/>; a hit triggers an answer draft on the
/// configured <see cref="IChatCompleter"/>.
/// </para>
/// <para>
/// Detection on the interviewer feed alone is the anti-echo design: anything the
/// candidate's mic picks up of the interviewer through speaker bleed arrives on the
/// candidate feed and never fires a question, so the assistant cannot answer its own user
/// mid-sentence.
/// </para>
/// <para>
/// Drafts are serialized per question — one at a time, latest question wins — and a
/// regen request re-drafts with a fresh angle. Every failure returns null and surfaces as
/// a state, never an exception.
/// </para>
/// </remarks>
public sealed class InterviewAssistant : IDisposable
{
    private IChatCompleter _completer;
    private readonly Func<bool> _isEnabled;
    private readonly SemaphoreSlim _draftGate = new(1, 1);
    private int _attempt;
    private string? _currentQuestion;
    private int _regenPending;
    private readonly Queue<string> _recentContext = new();

    /// <summary>How many recent utterances (both sides) colour the answer's context.</summary>
    public const int ContextTurns = 6;

    /// <summary>Raised when a new interviewer question is detected.</summary>
    public event EventHandler<InterviewTurn>? QuestionDetected;

    /// <summary>Raised when a draft completes — the answer to show.</summary>
    public event EventHandler<InterviewAnswer>? AnswerReady;

    /// <summary>Raised when drafting failed (backend unavailable, blank response).</summary>
    public event EventHandler<string>? DraftFailed;

    /// <summary>Builds the assistant over a completer and an on/off gate (the settings switch).</summary>
    /// <param name="completer">The model backend; swapped live by Settings → AI.</param>
    /// <param name="isEnabled">Read at draft time so the toggle applies to the next question.</param>
    public InterviewAssistant(IChatCompleter completer, Func<bool> isEnabled)
    {
        _completer = completer;
        _isEnabled = isEnabled;
    }

    /// <summary>Current state, for the panel.</summary>
    public InterviewState State { get; private set; } = InterviewState.Listening;

    /// <summary>
    /// Called by the engine for every finished utterance from either feed. Interviewer
    /// turns are screened by the question detector; candidate turns are recorded only.
    /// </summary>
    public void Observe(InterviewTurn turn)
    {
        _recentContext.Enqueue((turn.Speaker == Speaker.Interviewer ? "Interviewer: " : "Me: ") + turn.Text);
        while (_recentContext.Count > ContextTurns) _recentContext.Dequeue();

        if (turn.Speaker != Speaker.Interviewer || !turn.IsQuestion) return;

        _currentQuestion = turn.Text;
        _attempt = 1;
        State = InterviewState.Drafting;
        QuestionDetected?.Invoke(this, turn);
        _ = DraftAsync(turn.Text, freshAngle: false);
    }

    /// <summary>
    /// Re-drafts the current question with a different instruction — the
    /// "Try another angle" button.
    /// </summary>
    public void Regenerate()
    {
        if (_currentQuestion is null) return;
        Interlocked.Increment(ref _regenPending);
        _ = DraftAsync(_currentQuestion, freshAngle: true);
    }

    /// <summary>Swaps the model backend live (Settings → AI changed).</summary>
    public void ConfigureCompleter(IChatCompleter completer)
    {
        // The field is readonly by discipline, not by contract; swap via reflection-free
        // wrapper: the completer is held in a field that this method replaces.
        _completer = completer;
    }


    /// <summary>Re-drafts the current question after the model choice changed.</summary>
    public void RedraftWithCurrentModel()
    {
        if (_currentQuestion is null) return;
        _ = DraftAsync(_currentQuestion, freshAngle: false);
    }

    /// <summary>Clears the session (new interview).</summary>
    public void Reset()
    {
        _currentQuestion = null;
        _attempt = 0;
        _recentContext.Clear();
        State = InterviewState.Listening;
    }

    /// <summary>One draft cycle. Serialized; a queued regen supersedes the answer being drawn.</summary>
    private async Task DraftAsync(string question, bool freshAngle)
    {
        if (!_isEnabled())
        {
            State = InterviewState.Listening;
            DraftFailed?.Invoke(this, "The AI answer pass is off — turn it on in Settings → AI.");
            return;
        }

        await _draftGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A regen pressed while a draft was in flight means the in-flight draft is
            // already stale: loop until the queue is empty, then answer once.
            do
            {
                var answer = await DraftOnceAsync(question, freshAngle).ConfigureAwait(false);
                if (answer is null)
                {
                    State = InterviewState.Answered;
                    DraftFailed?.Invoke(this, "Answer failed: "
                        + (_completer.LastError ?? "check the AI backend in Settings."));
                    return;
                }

                State = InterviewState.Answered;
                AnswerReady?.Invoke(this, answer);
                freshAngle = false;
            }
            while (Interlocked.Exchange(ref _regenPending, 0) > 0);
        }
        finally
        {
            _draftGate.Release();
        }
    }

    private async Task<InterviewAnswer?> DraftOnceAsync(string question, bool freshAngle)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var attempt = Interlocked.Increment(ref _attempt) - 1;

        var context = freshAngle
            ? SystemPrompt + "\n\nThis is a re-draft. Give a genuinely different structure " +
              "and emphasis than a typical first answer: lead with a different strength, " +
              "use different examples."
            : SystemPrompt;

        var full = await _completer
            .CompleteAsync(context, BuildUserPrompt(question, shortForm: false), CancellationToken.None)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(full)) return null;

        var shortAnswer = await _completer
            .CompleteAsync(context, BuildUserPrompt(question, shortForm: true), CancellationToken.None)
            .ConfigureAwait(false);

        return new InterviewAnswer(
            Question: question,
            FullAnswer: full.Trim(),
            ShortAnswer: (shortAnswer ?? full).Trim(),
            Attempt: attempt,
            Drafted: DateTimeOffset.UtcNow - startedAt);
    }

    /// <summary>The persona the model answers as: a coach feeding the candidate lines.</summary>
    public const string SystemPrompt =
        "You are an interview coach whispering answers to a job candidate in real time. " +
        "The candidate can see your text at a glance and speaks it aloud almost as-is. " +
        "Answer interview questions the candidate was just asked: first person, natural " +
        "spoken English (UK spelling), confident but not arrogant. Structure with short " +
        "paragraphs or bullets. Prefer the candidate's real experience framing " +
        "(\"In my last role…\") over textbook definitions. Keep the full answer under 250 " +
        "words — about 90 seconds spoken. Never mention being an AI or this coaching.";

    /// <summary>The per-question user message, with the recent conversation for context.</summary>
    private string BuildUserPrompt(string question, bool shortForm)
    {
        var context = _recentContext.Count == 0
            ? string.Empty
            : "Recent conversation:\n" + string.Join("\n", _recentContext) + "\n\n";
        return context
            + "Question just asked: \"" + question + "\"\n\n"
            + (shortForm
                ? "Give ONLY the short version: 3-4 talking-point bullets, each one line, " +
                  "that the candidate can glance at and speak from. ~30 seconds spoken. " +
                  "No introduction, no closing."
                : "Give the full spoken answer now.");
    }

    /// <inheritdoc />
    public void Dispose() => _draftGate.Dispose();
}
