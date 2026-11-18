namespace MeetingTranscriber.Core;

/// <summary>
/// Throttle for discussion summary regeneration: a regen is due every 45 seconds or
/// every 6 new utterances, whichever comes first. The clock is injected so tests can
/// drive time deterministically instead of waiting on it.
/// </summary>
public sealed class SummaryScheduler
{
    /// <summary>Default time based throttle.</summary>
    public static TimeSpan DefaultInterval => TimeSpan.FromSeconds(45);

    /// <summary>Default utterance count throttle.</summary>
    public const int DefaultThreshold = 6;

    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _interval;
    private readonly int _threshold;

    /// <summary>Builds a scheduler over <paramref name="clock"/>.</summary>
    /// <param name="clock">Current time source; pass a fake in tests.</param>
    /// <param name="interval">Time throttle, or null for the 45 second default.</param>
    /// <param name="threshold">Utterance throttle, or null for the 6 utterance default.</param>
    public SummaryScheduler(
        Func<DateTimeOffset> clock,
        TimeSpan? interval = null,
        int? threshold = null)
    {
        _clock = clock;
        _interval = interval ?? DefaultInterval;
        _threshold = threshold ?? DefaultThreshold;
        LastRegeneratedUtc = _clock();
    }

    /// <summary>When the last regen was marked (creation time before the first one).</summary>
    public DateTimeOffset LastRegeneratedUtc { get; private set; }

    /// <summary>Utterances noted since the last regen.</summary>
    public int UtterancesSinceRegen { get; private set; }

    /// <summary>Notes one new utterance. True when a regen is due now.</summary>
    /// <returns>True when the caller should regenerate.</returns>
    public bool NoteUtterance()
    {
        UtterancesSinceRegen++;
        return IsDue(_clock());
    }

    /// <summary>Polls the time throttle (for a periodic timer). True when a regen is due.</summary>
    /// <returns>True when the interval elapsed and new material arrived since.</returns>
    public bool CheckTime() => IsDue(_clock());

    /// <summary>Resets the counters after a regen attempt, success or recorded failure.</summary>
    public void MarkRegenerated()
    {
        UtterancesSinceRegen = 0;
        LastRegeneratedUtc = _clock();
    }

    private bool IsDue(DateTimeOffset now) =>
        UtterancesSinceRegen >= _threshold ||
        (UtterancesSinceRegen > 0 && now - LastRegeneratedUtc >= _interval);
}
