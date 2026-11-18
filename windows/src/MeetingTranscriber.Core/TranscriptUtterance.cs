namespace MeetingTranscriber.Core;

/// <summary>Which audio feed an utterance arrived on. The mic maps to you, loopback maps to them.</summary>
public enum FeedKind
{
    /// <summary>Microphone feed, labelled YOU in the transcript.</summary>
    You,

    /// <summary>Loopback feed, labelled THEM in the transcript.</summary>
    Them,
}

/// <summary>One finished utterance with its arrival time and feed.</summary>
/// <param name="Id">Stable identity used as the transcript row key.</param>
/// <param name="At">Wall clock arrival time of the finished utterance.</param>
/// <param name="Feed">Which feed the utterance arrived on.</param>
/// <param name="Text">Verbatim transcript text.</param>
public sealed record TranscriptUtterance(
    Guid Id,
    DateTimeOffset At,
    FeedKind Feed,
    string Text);
