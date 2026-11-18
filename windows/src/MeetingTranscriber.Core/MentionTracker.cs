namespace MeetingTranscriber.Core;

/// <summary>
/// Counts name mentions across detections. Dismissing the strip clears the visible
/// alert but never the count, so the next detection re-fires with "+N earlier".
/// </summary>
public sealed class MentionTracker
{
    /// <summary>Total detections so far this session.</summary>
    public int TotalMentions { get; private set; }

    /// <summary>Whether the strip is currently showing (set on detect, cleared on dismiss).</summary>
    public bool HasActiveAlert { get; private set; }

    /// <summary>When the latest detection fired, or null before the first one.</summary>
    public DateTimeOffset? LastMentionAt { get; private set; }

    /// <summary>Mentions before the one on screen: the "+N earlier" figure.</summary>
    public int EarlierMentions => HasActiveAlert ? TotalMentions - 1 : TotalMentions;

    /// <summary>Records one detection and raises the strip.</summary>
    /// <param name="at">Arrival time of the triggering utterance.</param>
    /// <returns>The new total.</returns>
    public int Record(DateTimeOffset at)
    {
        TotalMentions++;
        LastMentionAt = at;
        HasActiveAlert = true;
        return TotalMentions;
    }

    /// <summary>Dismisses the strip. The count survives for the next detection.</summary>
    public void Dismiss() => HasActiveAlert = false;
}
