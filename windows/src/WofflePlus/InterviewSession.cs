using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;
using WofflePlus.Cloud;

namespace WofflePlus;

/// <summary>
/// Woffle+'s engine: two ConversationSession feeds (mic + loopback), question detection,
/// cloud-only answers. Cloud completer is rebuilt whenever the backend or key changes.
/// </summary>
internal sealed class InterviewSession : IDisposable
{
    private readonly PlusSettingsStore _settings;
    private readonly InterviewAssistant _assistant;
    private readonly Dictionary<bool, ConversationSession> _sessions = new();   // true = mic
    private readonly SemaphoreSlim _transcribeGate = new(1, 1);
    private readonly object _gate = new();
    private ITranscriber? _transcriber;
    private IChatCompleter? _completer;
    private bool _listening = true;
    private bool _started;

    // Per-feed "heard something" timestamps feed the silent-feed hint.
    private DateTime _micHeardUtc = DateTime.UtcNow;
    private DateTime _speakerHeardUtc = DateTime.UtcNow;
    private string _lastHint = string.Empty;
    private DateTime _lastHintUtc = DateTime.MinValue;
    private CancellationTokenSource? _heartbeatCts;

    /// <summary>(speaker, text) per finished utterance.</summary>
    public event EventHandler<(string Speaker, string Text)>? TranscriptTurn;

    /// <summary>A question was detected and a draft is starting.</summary>
    public event EventHandler<string>? QuestionDetected;

    /// <summary>(full, short, attempt, seconds).</summary>
    public event EventHandler<(string Full, string Short, int Attempt, double Seconds)>? AnswerReady;

    /// <summary>User-readable notices (failures and silent-feed hints included).</summary>
    public event EventHandler<string>? Notice;

    /// <summary>Raised when the ChatGPT (Codex) sign-in state changes.</summary>
    public event EventHandler? CodexAuthChanged;

    /// <summary>(isMic, level 0…1) roughly 50×/second. Drives the header level meters.</summary>
    public event EventHandler<(bool IsMic, float Level)>? FeedLevel;

    /// <summary>Raised when the listening toggle changes state.</summary>
    public event EventHandler<bool>? ListeningChanged;

    /// <summary>Whether the feeds are currently being captured.</summary>
    public bool Listening
    {
        get { lock (_gate) return _listening; }
    }

    public InterviewSession(PlusSettingsStore settings)
    {
        _settings = settings;
        _completer = BuildCompleter(settings.Data);
        _assistant = new InterviewAssistant(
            _completer,
            () => true);
        _assistant.QuestionDetected += (_, t) => QuestionDetected?.Invoke(this, t.Text);
        _assistant.AnswerReady += (_, a) => AnswerReady?.Invoke(this, (a.FullAnswer, a.ShortAnswer, a.Attempt, a.Drafted.TotalSeconds));
        _assistant.DraftFailed += (_, m) => Notice?.Invoke(this, m);

        // Live backend switch: any settings change rebuilds the completer.
        _settings.Changed += (_, _) => RebuildCompleter();

        _heartbeatCts = new CancellationTokenSource();
        _ = HeartbeatAsync(_heartbeatCts.Token);
    }

    /// <summary>Starts both feeds once the platform layer is present.</summary>
    public void Start()
    {
        if (_started) return;
        if (!Platform.Devices.IsAvailable)
        {
            Notice?.Invoke(this, "Audio devices unavailable — Woffle+ needs the Windows platform layer.");
            return;
        }

        _started = true;
        _transcriber = new ParakeetTranscriberLocate().Resolve() ?? new UnavailableTranscriber();

        var dictionaryPath = DictionaryFile.DefaultPath;
        var dictionary = new DictionaryFile(dictionaryPath);
        var bias = () => DictionaryCorrector.BiasPhrases(dictionary.Entries);

        var mic = Platform.Devices.CreateCapture(_settings.Data.MicDeviceId);
        var loopback = Platform.Devices.CreateLoopback(_settings.Data.OutputDeviceId);

        var micSession = new ConversationSession(mic!, _transcriber, bias, removeFillers: true, _transcribeGate);
        micSession.Utterance += (_, text) =>
        {
            _micHeardUtc = DateTime.UtcNow;
            TranscriptTurn?.Invoke(this, ("you", text));
            _assistant.Observe(new InterviewTurn(
                DateTimeOffset.Now, Speaker.Candidate, text, IsQuestion: false));
        };

        var loopbackSession = new ConversationSession(loopback!, _transcriber, bias, removeFillers: true, _transcribeGate);
        loopbackSession.Utterance += (_, text) =>
        {
            _speakerHeardUtc = DateTime.UtcNow;
            TranscriptTurn?.Invoke(this, ("interviewer", text));
            var turn = new InterviewTurn(DateTimeOffset.Now, Speaker.Interviewer, text,
                IsQuestion: QuestionDetector.IsQuestion(text));
            if (turn.IsQuestion) _assistant.Observe(turn);
        };

        micSession.Fault += (_, m) => Notice?.Invoke(this, m);
        loopbackSession.Fault += (_, m) => Notice?.Invoke(this, m);

        micSession.FeedLevel += (_, level) => FeedLevel?.Invoke(this, (true, level));
        loopbackSession.FeedLevel += (_, level) => FeedLevel?.Invoke(this, (false, level));

        _sessions[true] = micSession;
        _sessions[false] = loopbackSession;

        var now = DateTime.UtcNow;
        _micHeardUtc = now;
        _speakerHeardUtc = now;

        if (_listening) RunAll();
    }

    /// <summary>Turns listening on or off across both feeds.</summary>
    public void SetListening(bool on)
    {
        bool changed;
        lock (_gate)
        {
            changed = _listening != on;
            _listening = on;
        }

        if (!changed) return;

        if (on)
        {
            var now = DateTime.UtcNow;
            _micHeardUtc = now;
            _speakerHeardUtc = now;
            if (_started) RunAll();
        }
        else
        {
            _ = StopAllAsync();
        }

        ListeningChanged?.Invoke(this, on);
    }

    /// <summary>Flips the listening state; returns the new state.</summary>
    public bool ToggleListening()
    {
        bool next;
        lock (_gate) next = !_listening;
        SetListening(next);
        return next;
    }

    private void RunAll()
    {
        foreach (var s in _sessions.Values) s.Start();
    }

    private async Task StopAllAsync()
    {
        foreach (var s in _sessions.Values)
        {
            try { await s.StopAsync().ConfigureAwait(false); }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
        }
    }

    /// <summary>Applies device changes live.</summary>
    public void ConfigureDevices(string? micId, string? outputId)
    {
        if (!_started) return;

        if (_sessions.TryGetValue(true, out var micSession))
        {
            micSession.ConfigureDevice(micId);
            if (_listening) _ = SafeRestartAsync(micSession);
        }
        if (_sessions.TryGetValue(false, out var loopbackSession))
        {
            loopbackSession.ConfigureDevice(outputId);
            if (_listening) _ = SafeRestartAsync(loopbackSession);
        }
    }

    private static async Task SafeRestartAsync(ConversationSession session)
    {
        try { await session.RestartAsync().ConfigureAwait(false); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    /// <summary>"Try another angle".</summary>
    public void Regenerate() => _assistant.Regenerate();

    /// <summary>
    /// Applies a user edit to the current question (typo fix) and re-answers with the
    /// corrected text. Empty edits are ignored so clearing the box mid-typing doesn't
    /// fire a broken draft.
    /// </summary>
    public void UpdateQuestion(string edited)
    {
        if (string.IsNullOrWhiteSpace(edited)) return;
        _assistant.ReplaceQuestion(edited.Trim());
    }

    /// <summary>
    /// Answers an arbitrary question — typed by hand into the question box, or forced
    /// after an edit — regardless of what the detector heard.
    /// </summary>
    public void AskQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return;
        QuestionDetected?.Invoke(this, question.Trim());
        _assistant.AskDirect(question.Trim());
    }

    /// <summary>Runs the ChatGPT subscription sign-in; returns tokens on success.</summary>
    public async Task<CodexTokens?> SignInCodexAsync(Action<string> openBrowser)
    {
        var tokens = await new CodexLogin().LoginAsync(openBrowser, CancellationToken.None).ConfigureAwait(true);
        if (tokens is not null)
        {
            CodexLoginState.AccountId = tokens.AccountId;
            RebuildCompleter();
            CodexAuthChanged?.Invoke(this, EventArgs.Empty);
        }
        return tokens;
    }

    /// <summary>Signs out of the ChatGPT subscription and repaints the header chip.</summary>
    public void SignOutCodex()
    {
        CodexLogin.SignOut();
        CodexLoginState.AccountId = string.Empty;
        RebuildCompleter();
        CodexAuthChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Watches both feeds and raises a hint when one has heard nothing for a while — a
    /// dead speaker feed used to look identical to a working one.
    /// </summary>
    private async Task HeartbeatAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
                if (!_listening || !_started) continue;

                var now = DateTime.UtcNow;
                var micSilent = (now - _micHeardUtc).TotalSeconds > 25;
                var speakerSilent = (now - _speakerHeardUtc).TotalSeconds > 25;
                var anyHeard = (now - _micHeardUtc).TotalSeconds <= 25 || (now - _speakerHeardUtc).TotalSeconds <= 25;

                string? hint = null;
                if (!anyHeard)
                {
                    hint = "No audio detected yet — speak to test the mic, or play a video to test the SPEAKER feed.";
                }
                else if (speakerSilent)
                {
                    hint = "SPEAKER feed is silent — is the call audio going to the output device selected above?";
                }
                else if (micSilent)
                {
                    hint = "MIC feed is silent — check the microphone picker and Windows mic privacy settings.";
                }

                if (hint is null) { _lastHint = string.Empty; continue; }
                if (hint == _lastHint && (now - _lastHintUtc).TotalSeconds < 60) continue;

                _lastHint = hint;
                _lastHintUtc = now;
                Notice?.Invoke(this, hint);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private IChatCompleter BuildCompleter(PlusSettings data) => data.Backend switch
    {
        "Zai" => PlusCompleter.ForZai(data.ZaiApiKeyId, data.ZaiApiKeySecret, data.ZaiApiKey, data.ZaiModel),
        "OpenAi" => PlusCompleter.ForKey(CloudBackend.OpenAiKey, data.OpenAiApiKey ?? string.Empty, data.OpenAiModel),
        "Anthropic" => PlusCompleter.ForKey(CloudBackend.AnthropicKey, data.AnthropicApiKey ?? string.Empty, data.AnthropicModel),
        "DeepSeek" => PlusCompleter.ForKey(CloudBackend.DeepSeekKey, data.DeepSeekApiKey ?? string.Empty, data.DeepSeekModel, PlusCompleter.DeepSeekBaseUri),
        _ => PlusCompleter.ForCodexSubscription(),
    };

    private void RebuildCompleter()
    {
        _completer?.Dispose();
        _completer = BuildCompleter(_settings.Data);
        _assistant.ConfigureCompleter(_completer);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_heartbeatCts is not null)
        {
            _heartbeatCts.Cancel();
            _heartbeatCts.Dispose();
            _heartbeatCts = null;
        }

        foreach (var s in _sessions.Values) s.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _completer?.Dispose();
        _assistant.Dispose();
        _transcribeGate.Dispose();
    }
}

/// <summary>Resolves the speech model folder for the conversation feeds.</summary>
internal sealed class ParakeetTranscriberLocate
{
    /// <summary>
    /// Finds the installed model directory, preferring the accurate 0.6B TDT model for
    /// transcription quality; the compact CTC 110M is the fallback (roughly 2 GB vs 450 MB,
    /// but noticeably better on accents and call audio).
    /// </summary>
    public ITranscriber? Resolve()
    {
        var dir = Murmur.Speech.ParakeetTranscriber.Locate();
        return dir is null ? null : new Murmur.Speech.ParakeetTranscriber(dir);
    }
}

/// <summary>Reports the speech model as missing rather than crashing.</summary>
internal sealed class UnavailableTranscriber : ITranscriber
{
    /// <inheritdoc />
    public bool IsReady => false;

    /// <inheritdoc />
    public ValueTask<bool> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);

    /// <inheritdoc />
    public ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        IReadOnlyList<string> biasPhrases,
        CancellationToken cancellationToken) => ValueTask.FromResult(string.Empty);

    /// <inheritdoc />
    public ValueTask UnloadAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
