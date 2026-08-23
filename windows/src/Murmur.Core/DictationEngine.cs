using System.Buffers;
using Murmur.Abstractions;
using Murmur.Dictionary;

namespace Murmur.Core;

/// <summary>What the engine is doing right now.</summary>
public enum DictationState
{
    /// <summary>Waiting for the hotkey.</summary>
    Idle,

    /// <summary>The key is held; audio is being captured.</summary>
    Recording,

    /// <summary>The key is released; the utterance is being transcribed.</summary>
    Transcribing,
}

/// <summary>One completed dictation.</summary>
public sealed record DictationResult(
    DateTimeOffset At,
    TimeSpan AudioDuration,
    TimeSpan ProcessingTime,
    string Text,
    IReadOnlyList<AppliedCorrection> Corrections);

/// <summary>
/// The whole dictation flow: hotkey down, capture, hotkey up, transcribe, correct, inject.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately platform-neutral. It targets plain <c>net10.0</c>, so <c>CA1416</c> turns any
/// accidental Windows API call in here into a build error. Everything platform-specific
/// arrives through the four interfaces it is constructed with.
/// </para>
/// <para>
/// That is what makes the interesting behaviour testable without Windows: hand it fakes and
/// the entire path — including chunking, the correction pass and the "nothing was said"
/// case — runs on any machine, in milliseconds.
/// </para>
/// </remarks>
public sealed class DictationEngine : IAsyncDisposable
{
    private readonly IAudioCapture _capture;
    private readonly IHotkeySource _hotkey;
    private readonly ITranscriber _transcriber;
    private readonly ITextInjector _injector;
    private readonly IClock _clock;
    private readonly Func<IReadOnlyList<DictionaryEntry>> _dictionary;
    private readonly bool _removeFillers;
    private readonly TimeSpan _partialInterval;
    private readonly TimeSpan _idleUnloadTimeout;
    private CancellationTokenSource? _idleUnload;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _recording;
    private List<float>? _buffer;

    /// <summary>Serializes the recognizer: partial previews and the final pass share it,
    /// and sherpa-onnx recognizers are not thread-safe.</summary>
    private readonly SemaphoreSlim _transcribeGate = new(1, 1);

    /// <summary>Guards <see cref="_buffer"/>: the capture loop writes it while the
    /// partial-transcript loop snapshots it.</summary>
    private readonly object _bufferLock = new();

    private DateTimeOffset _startedAt;

    /// <summary>Current state.</summary>
    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>Most recent input level, 0…1. Drives the meter.</summary>
    public float Level { get; private set; }

    /// <summary>Raised when a dictation completes and produced text.</summary>
    public event EventHandler<DictationResult>? Completed;

    /// <summary>Raised whenever <see cref="State"/> or <see cref="Level"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised for user-visible status, e.g. "loading model…" or "could not type the
    /// transcript". The panel shows the latest message so failures are never silent.
    /// </summary>
    public event EventHandler<string>? Notice;

    /// <summary>
    /// Raised with the latest partial transcript while recording, so the panel can show
    /// what is being heard before the key is released. Partials are never injected — the
    /// full transcript on release is the one that lands in the focused app.
    /// </summary>
    public event EventHandler<string>? PartialTranscript;

    /// <summary>Wires the engine to its platform implementations.</summary>
    /// <param name="capture">Microphone source.</param>
    /// <param name="hotkey">Push-to-talk source.</param>
    /// <param name="transcriber">Speech engine.</param>
    /// <param name="injector">Where finished text goes.</param>
    /// <param name="dictionary">
    /// Read fresh on every utterance rather than captured once, so edits take effect without
    /// a restart.
    /// </param>
    /// <param name="clock">Time source; defaults to the system clock.</param>
    /// <param name="removeFillers">
    /// Whether to strip spoken disfluencies ("um", "er", "uh") from transcripts. Default true.
    /// </param>
    /// <param name="partialInterval">
    /// How often the live preview refreshes while recording. Defaults to two seconds; tests
    /// pass something shorter.
    /// </param>
    /// <param name="idleUnloadTimeout">
    /// How long after the last dictation the model is unloaded (frees ~660 MB). Zero or
    /// negative disables unloading; the next dictation pays the reload.
    /// </param>
    public DictationEngine(
        IAudioCapture capture,
        IHotkeySource hotkey,
        ITranscriber transcriber,
        ITextInjector injector,
        Func<IReadOnlyList<DictionaryEntry>> dictionary,
        IClock? clock = null,
        bool removeFillers = true,
        TimeSpan? partialInterval = null,
        TimeSpan idleUnloadTimeout = default)
    {
        _capture = capture;
        _hotkey = hotkey;
        _transcriber = transcriber;
        _injector = injector;
        _dictionary = dictionary;
        _clock = clock ?? SystemClock.Instance;
        _removeFillers = removeFillers;
        _partialInterval = partialInterval ?? TimeSpan.FromSeconds(2);
        _idleUnloadTimeout = idleUnloadTimeout;

        _hotkey.Pressed += OnPressed;
        _hotkey.Released += OnReleased;
    }

    /// <summary>Arms the hotkey.</summary>
    /// <returns>False if the hook could not be installed.</returns>
    public bool Start() => _hotkey.Start();

    /// <summary>
    /// Selects the microphone and input gain. Applied live: the capture reads the device
    /// at the start of every recording, so the next hold uses the new choice.
    /// </summary>
    public void ConfigureInput(string? deviceId, float gain)
    {
        _capture.DeviceId = deviceId;
        _capture.Gain = gain;
    }

    /// <summary>
    /// Starts or stops recording from a button rather than the hotkey.
    /// </summary>
    /// <remarks>
    /// Routed through the same state machine as the hotkey, deliberately. Two independent
    /// paths into recording would eventually disagree about whether it is running.
    /// </remarks>
    public void TogglePushToTalk()
    {
        if (State == DictationState.Idle) _ = BeginAsync();
        else if (State == DictationState.Recording) _ = EndAsync();
    }

    private void OnPressed(object? sender, EventArgs e) => _ = BeginAsync();

    private void OnReleased(object? sender, EventArgs e) => _ = EndAsync();

    private async Task BeginAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != DictationState.Idle) return;

            // A dictation is starting: cancel any pending idle unload so the model stays
            // warm for it.
            CancelIdleUnload();

            _buffer = [];
            _startedAt = _clock.Now;
            _recording = new CancellationTokenSource();
            SetState(DictationState.Recording);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            // The live preview runs alongside the capture loop and dies with the recording.
            _ = RunPartialLoopAsync(_recording!.Token);

            await foreach (var chunk in _capture.CaptureAsync(_recording!.Token).ConfigureAwait(false))
            {
                // Stop consuming the moment recording ends. Cancellation is cooperative, so
                // chunks already queued still arrive after EndAsync has moved on — and
                // without this guard one of them sets Level back to a reading that has
                // already been zeroed.
                if (State != DictationState.Recording) break;

                // Copied, not referenced: capture implementations are entitled to reuse
                // their buffer the moment this returns.
                lock (_bufferLock)
                {
                    _buffer?.AddRange(chunk.Samples.Span);
                }

                Level = chunk.Rms();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal: the key was released.
        }
        finally
        {
            // Authoritative: this runs only once the capture loop has genuinely finished, so
            // nothing can raise the level afterwards and leave the meter stuck.
            Level = 0;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task EndAsync()
    {
        List<float>? samples;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != DictationState.Recording) return;

            await _recording!.CancelAsync().ConfigureAwait(false);
            lock (_bufferLock)
            {
                samples = _buffer;
                _buffer = null;
            }
            Level = 0;
            SetState(DictationState.Transcribing);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await ProcessAsync(samples).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A fire-and-forget task that throws is an unobserved exception: the app keeps
            // running and the user sees nothing — the worst possible outcome for a dictation
            // app. Surface it instead.
            Notice?.Invoke(this, $"Something went wrong: {e.Message}");
        }
        finally
        {
            _recording?.Dispose();
            _recording = null;
            SetState(DictationState.Idle);

            // A fresh idle countdown starts after every dictation.
            _ = ArmIdleUnloadAsync();
        }
    }

    /// <summary>
    /// Arms the idle model unload: after <see cref="_idleUnloadTimeout"/> with no dictation,
    /// the recognizer is disposed and its ~660 MB freed. The next dictation reloads it.
    /// </summary>
    private async Task ArmIdleUnloadAsync()
    {
        CancelIdleUnload();

        if (_idleUnloadTimeout <= TimeSpan.Zero || !_transcriber.IsReady) return;

        var cts = new CancellationTokenSource();
        _idleUnload = cts;
        try
        {
            await Task.Delay(_idleUnloadTimeout, cts.Token).ConfigureAwait(false);
            if (cts.IsCancellationRequested || State != DictationState.Idle || !_transcriber.IsReady) return;

            await _transcriber.UnloadAsync().ConfigureAwait(false);
            Notice?.Invoke(this,
                "Speech model unloaded after being idle — the next dictation reloads it in a couple of seconds.");
        }
        catch (OperationCanceledException)
        {
            // Normal: a dictation started, or the app is shutting down.
        }
        catch (Exception e)
        {
            Notice?.Invoke(this, $"Could not unload the speech model: {e.Message}");
        }
    }

    /// <summary>Cancels any pending idle unload — a dictation is starting.</summary>
    private void CancelIdleUnload()
    {
        _idleUnload?.Cancel();
        _idleUnload?.Dispose();
        _idleUnload = null;
    }

    private async Task ProcessAsync(List<float>? samples)
    {
        if (samples is null || samples.Count == 0) return;

        // Measured from key release, because that is the wait the user actually feels — and
        // it is the only figure on which a streaming and a batch engine compare honestly.
        var releasedAt = _clock.Now;
        var audio = new ReadOnlyMemory<float>(samples.ToArray());

        // The model loads lazily on first use: ~2 s on a fresh launch, then never again.
        // Engines must be loaded before they are asked to transcribe — a recognizer that
        // was never constructed returns empty text rather than erroring, which would look
        // like "it heard me but typed nothing".
        if (!_transcriber.IsReady)
        {
            Notice?.Invoke(this, "Loading speech model…");
            var loaded = await _transcriber.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            if (!loaded)
            {
                Notice?.Invoke(this, "Speech model failed to load — check Settings → Model.");
                return;
            }
        }

        var entries = _dictionary();
        var bias = DictionaryCorrector.BiasPhrases(entries);

        var pieces = AudioSegmenter.Split(audio);
        var transcripts = new List<string>(pieces.Count);

        // The recognizer is shared with the (now cancelled) partial loop; hold the gate so
        // an in-flight preview can never run concurrently with the real transcription.
        await _transcribeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var piece in pieces)
            {
                var text = await _transcriber
                    .TranscribeAsync(piece, bias, CancellationToken.None)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(text)) transcripts.Add(text.Trim());
            }
        }
        finally
        {
            _transcribeGate.Release();
        }

        var raw = string.Join(' ', transcripts);
        if (string.IsNullOrWhiteSpace(raw)) return;

        // Spoken disfluencies ("um", "er", "actually,") are transcribed faithfully by the
        // engine and almost never wanted on the page. Cleaned before the dictionary pass so
        // correction rules still see the words that remain.
        if (_removeFillers)
        {
            raw = DisfluencyCleaner.Clean(raw);
            if (string.IsNullOrWhiteSpace(raw)) return;
        }

        // The dictionary runs last and unconditionally. Biasing only raises the odds of the
        // right word; this is the pass that guarantees it.
        var (corrected, applied) = new DictionaryCorrector(entries).Apply(raw);

        var result = new DictationResult(
            At: releasedAt,
            AudioDuration: TimeSpan.FromSeconds((double)audio.Length / AudioChunk.SampleRate),
            ProcessingTime: _clock.Now - releasedAt,
            Text: corrected,
            Corrections: applied);

        Completed?.Invoke(this, result);
        var injected = await _injector.InjectAsync(corrected, CancellationToken.None).ConfigureAwait(false);

        Notice?.Invoke(this, injected
            ? "Typed into the focused app."
            : "Transcribed, but could not type it — the focused window may be elevated. "
              + "The text is in the transcriptions list.");
    }

    /// <summary>
    /// Live preview loop: while recording, periodically transcribe what has been captured
    /// so far and raise <see cref="PartialTranscript"/>. Dies with the recording token.
    /// </summary>
    private async Task RunPartialLoopAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(_partialInterval);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                // Partials never trigger the model load — the first dictation still pays
                // the one-time ~2s load on release, exactly as before.
                if (!_transcriber.IsReady) continue;

                await PublishPartialAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal: the key was released.
        }
        catch (Exception e)
        {
            // A failing preview must never take the recording down with it.
            Notice?.Invoke(this, $"Live preview stopped: {e.Message}");
        }
    }

    /// <summary>Transcribes the audio captured so far and raises it as a partial.</summary>
    private async Task PublishPartialAsync(CancellationToken token)
    {
        List<float> snapshot;
        lock (_bufferLock)
        {
            if (_buffer is null || _buffer.Count < AudioChunk.SampleRate / 2) return;
            snapshot = new List<float>(_buffer);
        }

        var entries = _dictionary();
        var bias = DictionaryCorrector.BiasPhrases(entries);

        // The same segmentation as the final pass, so a long recording does not hand the
        // recognizer a clip it cannot digest.
        var pieces = AudioSegmenter.Split(new ReadOnlyMemory<float>(snapshot.ToArray()));

        await _transcribeGate.WaitAsync(token).ConfigureAwait(false);
        string raw;
        try
        {
            var texts = new List<string>(pieces.Count);
            foreach (var piece in pieces)
            {
                var text = await _transcriber
                    .TranscribeAsync(piece, bias, token)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text)) texts.Add(text.Trim());
            }

            raw = string.Join(' ', texts);
        }
        finally
        {
            _transcribeGate.Release();
        }

        if (string.IsNullOrWhiteSpace(raw)) return;

        if (_removeFillers)
        {
            raw = DisfluencyCleaner.Clean(raw);
            if (string.IsNullOrWhiteSpace(raw)) return;
        }

        PartialTranscript?.Invoke(this, new DictionaryCorrector(entries).Apply(raw).Text);
    }

    private void SetState(DictationState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _hotkey.Pressed -= OnPressed;
        _hotkey.Released -= OnReleased;
        _hotkey.Dispose();

        if (_recording is not null)
        {
            await _recording.CancelAsync().ConfigureAwait(false);
            _recording.Dispose();
        }

        await _capture.DisposeAsync().ConfigureAwait(false);
        await _transcriber.DisposeAsync().ConfigureAwait(false);
        CancelIdleUnload();
        _transcribeGate.Dispose();
        _gate.Dispose();
    }
}
