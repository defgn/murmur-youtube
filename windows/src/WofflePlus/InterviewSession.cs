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
    private ITranscriber? _transcriber;
    private IChatCompleter? _completer;

    /// <summary>(speaker, text) per finished utterance.</summary>
    public event EventHandler<(string Speaker, string Text)>? TranscriptTurn;

    /// <summary>A question was detected and a draft is starting.</summary>
    public event EventHandler<string>? QuestionDetected;

    /// <summary>(full, short, attempt, seconds).</summary>
    public event EventHandler<(string Full, string Short, int Attempt, double Seconds)>? AnswerReady;

    /// <summary>User-readable notices (failures included).</summary>
    public event EventHandler<string>? Notice;

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
    }

    /// <summary>Starts both feeds once the platform layer is present.</summary>
    public void Start()
    {
        if (_sessions.Count > 0) return;
        if (!Platform.Devices.IsAvailable)
        {
            Notice?.Invoke(this, "Audio devices unavailable — Woffle+ needs the Windows platform layer.");
            return;
        }

        _transcriber = new ParakeetTranscriberLocate().Resolve() ?? new UnavailableTranscriber();

        var dictionaryPath = DictionaryFile.DefaultPath;
        var dictionary = new DictionaryFile(dictionaryPath);
        var bias = () => DictionaryCorrector.BiasPhrases(dictionary.Entries);

        var mic = Platform.Devices.CreateCapture(_settings.Data.MicDeviceId);
        var loopback = Platform.Devices.CreateLoopback(_settings.Data.OutputDeviceId);

        var micSession = new ConversationSession(mic!, _transcriber, bias, removeFillers: true, _transcribeGate);
        micSession.Utterance += (_, text) =>
        {
            TranscriptTurn?.Invoke(this, ("you", text));
            _assistant.Observe(new InterviewTurn(
                DateTimeOffset.Now, Speaker.Candidate, text, IsQuestion: false));
        };

        var loopbackSession = new ConversationSession(loopback!, _transcriber, bias, removeFillers: true, _transcribeGate);
        loopbackSession.Utterance += (_, text) =>
        {
            TranscriptTurn?.Invoke(this, ("interviewer", text));
            var turn = new InterviewTurn(DateTimeOffset.Now, Speaker.Interviewer, text,
                IsQuestion: QuestionDetector.IsQuestion(text));
            if (turn.IsQuestion) _assistant.Observe(turn);
        };

        micSession.Fault += (_, m) => Notice?.Invoke(this, m);
        loopbackSession.Fault += (_, m) => Notice?.Invoke(this, m);

        _sessions[true] = micSession;
        _sessions[false] = loopbackSession;

        foreach (var s in _sessions.Values) s.Start();
    }

    /// <summary>Applies device changes live.</summary>
    public void ConfigureDevices(string? micId, string? outputId)
    {
        if (_sessions.TryGetValue(true, out var micSession))
        {
            micSession.ConfigureDevice(micId);
            _ = micSession.RestartAsync();
        }
        if (_sessions.TryGetValue(false, out var loopbackSession))
        {
            loopbackSession.ConfigureDevice(outputId);
            _ = loopbackSession.RestartAsync();
        }
    }

    /// <summary>"Try another angle".</summary>
    public void Regenerate() => _assistant.Regenerate();

    /// <summary>Runs the ChatGPT subscription sign-in; returns tokens on success.</summary>
    public async Task<CodexTokens?> SignInCodexAsync(Action<string> openBrowser)
    {
        var tokens = await new CodexLogin().LoginAsync(openBrowser, CancellationToken.None).ConfigureAwait(true);
        if (tokens is not null)
        {
            CodexLoginState.AccountId = tokens.AccountId;
            RebuildCompleter();
        }
        return tokens;
    }

    private IChatCompleter BuildCompleter(PlusSettings data) => data.Backend switch
    {
        "Zai" => PlusCompleter.ForKey(CloudBackend.ZaiKey, data.ZaiApiKey ?? string.Empty, data.ZaiModel, PlusCompleter.ZaiBaseUri),
        "OpenAi" => PlusCompleter.ForKey(CloudBackend.OpenAiKey, data.OpenAiApiKey ?? string.Empty, data.OpenAiModel),
        "Anthropic" => PlusCompleter.ForKey(CloudBackend.AnthropicKey, data.AnthropicApiKey ?? string.Empty, data.AnthropicModel),
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
        foreach (var s in _sessions.Values) s.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _completer?.Dispose();
        _assistant.Dispose();
        _transcribeGate.Dispose();
    }
}

/// <summary>Resolves the speech model folder for the conversation feeds.</summary>
internal sealed class ParakeetTranscriberLocate
{
    /// <summary>Finds the installed model directory, preferring compact.</summary>
    public ITranscriber? Resolve()
    {
        var dir = Murmur.Speech.ParakeetTranscriber.Locate(Murmur.Speech.ParakeetTranscriber.CompactFolder)
                  ?? Murmur.Speech.ParakeetTranscriber.Locate(Murmur.Speech.ParakeetTranscriber.AccurateFolder);
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
