namespace Murmur.Abstractions;

/// <summary>
/// One side of a two-feed conversation session: continuous listen-and-transcribe.
/// </summary>
/// <remarks>
/// <para>
/// The dictation engine's push-to-talk shape does not fit an interview, where both sides
/// must be transcribed continuously for an hour. A session is a long-running capture +
/// transcribe loop over one feed, delivering finished utterance texts as they complete.
/// Which feed a session serves — microphone or loopback — is decided by the device id it is
/// constructed with; the assistant uses that to attribute speakers for free (mic = the
/// candidate, loopback = the interviewer), with no diarisation model.
/// </para>
/// <para>
/// Utterances are delimited by a silence gap (see <c>UtteranceSegmenter</c>), and segments
/// honor the recogniser's length ceiling the same way dictation does.
/// </para>
/// </remarks>
public interface IConversationSession : IAsyncDisposable
{
    /// <summary>Starts the capture-and-transcribe loop.</summary>
    void Start();

    /// <summary>Raised with each finished utterance (deterministically cleaned text).</summary>
    event EventHandler<string>? Utterance;

    /// <summary>Raised per chunk with feed loudness, 0…1 RMS. Drives level meters.</summary>
    event EventHandler<float>? FeedLevel;

    /// <summary>Raised with user-readable faults (device lost, model load failure).</summary>
    event EventHandler<string>? Fault;
}
