using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// A live conversation feed over any capture: listen continuously, transcribe in
/// utterance-sized pieces, raise finished utterances.
/// </summary>
/// <remarks>
/// <para>
/// One instance serves one side of the interview — the microphone (the candidate) or a
/// loopback capture (the interviewer). Speaker attribution is free because it comes from
/// which device the audio entered through; no diarisation model is involved.
/// </para>
/// <para>
/// Platform-neutral by construction: it consumes <see cref="IAudioCapture"/>, so the whole
/// utterance pipeline is testable with fakes and runs on any OS.
/// </para>
/// </remarks>
public sealed class ConversationSession : IConversationSession
{
    private readonly IAudioCapture _capture;
    private readonly ITranscriber _transcriber;
    private readonly Func<IReadOnlyList<string>> _bias;
    private readonly bool _removeFillers;
    private readonly SemaphoreSlim _transcribeGate;

    private CancellationTokenSource? _run;

    /// <summary>Builds a session over <paramref name="capture"/>.</summary>
    /// <param name="capture">The feed: microphone or loopback.</param>
    /// <param name="transcriber">The shared speech model.</param>
    /// <param name="bias">Dictionary bias phrases, read fresh per utterance.</param>
    /// <param name="removeFillers">Whether to strip "um"/"er" before raising.</param>
    /// <param name="transcribeGate">
    /// Serializes recognition with the dictation engine's partial loop and final passes —
    /// sherpa-onnx recognizers are not thread-safe.
    /// </param>
    public ConversationSession(
        IAudioCapture capture,
        ITranscriber transcriber,
        Func<IReadOnlyList<string>> bias,
        bool removeFillers,
        SemaphoreSlim transcribeGate)
    {
        _capture = capture;
        _transcriber = transcriber;
        _bias = bias;
        _removeFillers = removeFillers;
        _transcribeGate = transcribeGate;
    }

    /// <inheritdoc />
    public event EventHandler<string>? Utterance;

    /// <inheritdoc />
    public event EventHandler<string>? Fault;

    /// <summary>Swaps the feed's device live; takes effect from the next (re)start.</summary>
    public void ConfigureDevice(string? deviceId) => _capture.DeviceId = deviceId;

    /// <inheritdoc />
    public void Start()
    {
        if (_run is not null) return;
        _run = new CancellationTokenSource();
        _ = RunAsync(_run.Token);
    }

    /// <summary>Stops the feed so the device can be changed, then restarts.</summary>
    public async Task RestartAsync()
    {
        if (_run is null) { Start(); return; }

        var old = _run;
        _run = null;
        await old.CancelAsync().ConfigureAwait(false);
        old.Dispose();

        Start();
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await foreach (var chunk in _capture.CaptureAsync(token).ConfigureAwait(false))
            {
                await ProcessChunkAsync(chunk, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal: the feed is being restarted or the app is closing.
        }
        catch (Exception e)
        {
            Fault?.Invoke(this, $"The conversation feed stopped: {e.Message}");
        }
    }

    // ---- utterance assembly (the streaming shape of AudioSegmenter's batch cutting) ----

    private List<float>? _open;
    private int _trailingSilence;
    private float _peakRms;

    private async Task ProcessChunkAsync(AudioChunk chunk, CancellationToken token)
    {
        var rms = chunk.Rms();
        if (rms >= UtteranceSegmenter.SpeechRmsThreshold)
        {
            _trailingSilence = 0;
            _peakRms = Math.Max(_peakRms, rms);
        }
        else
        {
            _trailingSilence += chunk.Samples.Length;
        }

        _open ??= [];
        _open.AddRange(chunk.Samples.Span);

        var boundary = UtteranceSegmenter.IsBoundary(_open.Count, _trailingSilence, _peakRms);
        var mustFlush = UtteranceSegmenter.MustFlush(_open.Count);

        if (!boundary && !mustFlush) return;

        var samples = _open.ToArray();
        _open = null;
        _trailingSilence = 0;
        _peakRms = 0;

        await TranscribeAsync(samples, token).ConfigureAwait(false);
    }

    private async Task TranscribeAsync(float[] samples, CancellationToken token)
    {
        try
        {
            if (!_transcriber.IsReady)
            {
                var loaded = await _transcriber.LoadAsync(token).ConfigureAwait(false);
                if (!loaded)
                {
                    Fault?.Invoke(this, "The speech model is not installed — see Settings → Model.");
                    return;
                }
            }

            var bias = _bias();
            var pieces = AudioSegmenter.Split(samples);
            var texts = new List<string>(pieces.Count);

            await _transcribeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                foreach (var piece in pieces)
                {
                    var text = await _transcriber
                        .TranscribeAsync(piece, bias, token)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(text)) texts.Add(text.Trim());
                }
            }
            finally
            {
                _transcribeGate.Release();
            }

            var raw = string.Join(' ', texts);
            if (string.IsNullOrWhiteSpace(raw)) return;

            if (_removeFillers)
            {
                raw = DisfluencyCleaner.Clean(raw);
                if (string.IsNullOrWhiteSpace(raw)) return;
            }

            raw = SentenceFormatter.Format(raw);
            if (string.IsNullOrWhiteSpace(raw)) return;

            Utterance?.Invoke(this, raw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Fault?.Invoke(this, $"Transcription failed: {e.Message}");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_run is not null)
        {
            await _run.CancelAsync().ConfigureAwait(false);
            _run.Dispose();
            _run = null;
        }

        await _capture.DisposeAsync().ConfigureAwait(false);
    }
}
