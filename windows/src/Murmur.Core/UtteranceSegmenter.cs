using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Splits a continuous audio stream into utterances at silence gaps.
/// </summary>
/// <remarks>
/// <para>
/// Dictation has a key release to mark "utterance over"; a live conversation has nothing
/// but the pause between sentences. This segmenter watches the rolling energy of the feed
/// and cuts after <see cref="TrailingSilenceMilliseconds"/> of quiet, provided the piece
/// holds at least <see cref="MinUtteranceMilliseconds"/> of sound — so a breath or a click
/// never becomes an utterance, and a slow speaker is never cut mid-flow.
/// </para>
/// <para>
/// Pure and static so the cutting policy is unit-testable without audio hardware.
/// </para>
/// </remarks>
public static class UtteranceSegmenter
{
    /// <summary>Quiet tail that closes an utterance.</summary>
    public const int TrailingSilenceMilliseconds = 900;

    /// <summary>Shortest piece considered speech at all.</summary>
    public const int MinUtteranceMilliseconds = 400;

    /// <summary>Absolute longest piece produced, honoring the recogniser's ceiling.</summary>
    public const int MaxUtteranceSeconds = AudioSegmenter.MaxSegmentSeconds;

    /// <summary>
    /// RMS above which the feed counts as speech. Deliberately low: interview rooms are
    /// quiet and laptop mics are quieter.
    /// </summary>
    public const float SpeechRmsThreshold = 0.012f;

    /// <summary>
    /// Computes whether an utterance boundary falls at sample offset
    /// <paramref name="samplesSoFar"/>, given how much trailing quiet there has been.
    /// </summary>
    /// <param name="samplesSoFar">Samples accumulated in the open utterance.</param>
    /// <param name="trailingSilentSamples">Samples of continuous recent silence.</param>
    /// <param name="peakRms">The loudest chunk RMS seen in the open utterance.</param>
    public static bool IsBoundary(int samplesSoFar, int trailingSilentSamples, float peakRms)
    {
        var minSamples = MinUtteranceMilliseconds * AudioChunk.SampleRate / 1000;
        if (samplesSoFar < minSamples) return false;

        // Speech must have been loud enough at some point; the tail must be quiet enough now.
        if (peakRms < SpeechRmsThreshold) return false;

        var silenceSamples = TrailingSilenceMilliseconds * AudioChunk.SampleRate / 1000;
        return trailingSilentSamples >= silenceSamples;
    }

    /// <summary>
    /// True when an open utterance has exceeded the hard length cap and must be flushed
    /// even though nobody paused.
    /// </summary>
    public static bool MustFlush(int samplesSoFar) =>
        samplesSoFar >= MaxUtteranceSeconds * AudioChunk.SampleRate;
}
