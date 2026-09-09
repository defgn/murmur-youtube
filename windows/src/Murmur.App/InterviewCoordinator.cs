using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;

namespace Murmur.App;

/// <summary>
/// Owns the Woffle+ interview assistant: the two conversation feeds, question detection,
/// and the answer backend.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Murmur.App</c>'s neighbourhood because it bridges the platform layer
/// (loopback capture) to the Core pipeline. The microphone feed reuses the dictation
/// engine's capture device choice, so the two features share one mic.
/// </para>
/// <para>
/// The answer backend (<see cref="IChatCompleter"/>) is rebuilt whenever the AI settings
/// change, exactly like the smart-clean pass. Both feeds restart on a device change
/// without touching the transcript panel or the current answer.
/// </para>
/// </remarks>
public sealed class InterviewCoordinator : IDisposable
{
    private readonly AppSettings _settings;
    private readonly InterviewAssistant _assistant;
    private readonly Dictionary<Speaker, ConversationSession> _sessions = [];
    private readonly SemaphoreSlim _transcribeGate;
    private readonly TranscriptStore _transcripts;
    private readonly DictionaryFile _dictionaryFile;

    private IChatCompleter? _completer;

    private InterviewCoordinator(
        AppSettings settings,
        InterviewAssistant assistant,
        SemaphoreSlim transcribeGate,
        TranscriptStore transcripts,
        DictionaryFile dictionaryFile)
    {
        _settings = settings;
        _assistant = assistant;
        _transcribeGate = transcribeGate;
        _transcripts = transcripts;
        _dictionaryFile = dictionaryFile;

        _assistant.QuestionDetected += (_, turn) => QuestionSeen?.Invoke(this, turn);
        _assistant.AnswerReady += (_, answer) => AnswerArrived?.Invoke(this, answer);
        _assistant.DraftFailed += (_, message) => AssistantNotice?.Invoke(this, message);
    }

    /// <summary>Raised when the interviewer asked something worth answering.</summary>
    public event EventHandler<InterviewTurn>? QuestionSeen;

    /// <summary>Raised with a finished draft.</summary>
    public event EventHandler<InterviewAnswer>? AnswerArrived;

    /// <summary>Raised with user-readable assistant status (failures included).</summary>
    public event EventHandler<string>? AssistantNotice;

    /// <summary>The assistant state machine, for panel polling.</summary>
    public InterviewAssistant Assistant => _assistant;

    /// <summary>
    /// Builds both feeds and wires utterances into the assistant. Returns null when the
    /// platform layer is absent (headless test host).
    /// </summary>
    public static InterviewCoordinator? Create(
        AppSettings settings,
        DictionaryFile dictionary,
        ITranscriber transcriber,
        DictationEngine engine)
    {
        var bias = () => DictionaryCorrector.BiasPhrases(dictionary.Entries);
        var gate = new SemaphoreSlim(1, 1);
        var store = new TranscriptStore(TranscriptStore.DefaultPath);

        var mic = PlatformFactory.CreateAudioCapture(settings.Data.InputDeviceId);
        var loopback = PlatformFactory.CreateLoopbackCapture(settings.Data.LoopbackDeviceId);
        if (mic is null || loopback is null) { gate.Dispose(); return null; }

        var coordinator = new InterviewCoordinator(
            settings,
            new InterviewAssistant(
                BuildCompleter(settings.Data),
                () => settings.Data.AssistantEnabled),
            gate, store, dictionary);

        // Feed 1: the microphone = the candidate. Shares the dictation device so both
        // features use one mic; it follows the same live device setting.
        var micSession = new ConversationSession(mic, transcriber, bias, removeFillers: true, gate);
        micSession.Utterance += (_, text) =>
            coordinator._assistant.Observe(new InterviewTurn(
                DateTimeOffset.Now, Speaker.Candidate, text, IsQuestion: false));

        // Feed 2: loopback of the output device = the interviewer. Anything question-like
        // here triggers a draft; the candidate feed never does, so speaker bleed cannot
        // make the assistant answer mid-sentence or echo its own user.
        var loopbackSession = new ConversationSession(loopback, transcriber, bias, removeFillers: true, gate);
        loopbackSession.Utterance += (_, text) =>
        {
            var turn = new InterviewTurn(
                DateTimeOffset.Now, Speaker.Interviewer, text,
                IsQuestion: QuestionDetector.IsQuestion(text));
            coordinator.RecordUtterance(turn);
            coordinator._assistant.Observe(turn);
        };

        micSession.Fault += (_, m) => coordinator.AssistantNotice?.Invoke(coordinator, m);
        loopbackSession.Fault += (_, m) => coordinator.AssistantNotice?.Invoke(coordinator, m);

        coordinator._sessions[Speaker.Candidate] = micSession;
        coordinator._sessions[Speaker.Interviewer] = loopbackSession;

        // Follow AI backend changes live, like the smart-clean pass does.
        settings.Changed += (_, _) =>
        {
            if (coordinator.RebuildCompleterIfChanged()) coordinator._assistant.RedraftWithCurrentModel();
        };

        return coordinator;
    }

    /// <summary>Starts both feeds (called once the engine is running).</summary>
    public void Start()
    {
        foreach (var session in _sessions.Values) session.Start();
    }

    /// <summary>
    /// Applies a mic or output device change on the fly: the feed restarts on the new
    /// endpoint, the transcript and any answer on screen are untouched.
    /// </summary>
    public void ConfigureDevices(string? micDeviceId, string? loopbackDeviceId)
    {
        if (_sessions.TryGetValue(Speaker.Candidate, out var micSession))
        {
            micSession.ConfigureDevice(micDeviceId);
            _ = micSession.RestartAsync();
        }

        if (_sessions.TryGetValue(Speaker.Interviewer, out var loopbackSession))
        {
            loopbackSession.ConfigureDevice(loopbackDeviceId);
            _ = loopbackSession.RestartAsync();
        }
    }

    /// <summary>"Try another angle": re-draft the current question.</summary>
    public void Regenerate() => _assistant.Regenerate();

    private void RecordUtterance(InterviewTurn turn)
    {
        if (!_settings.Data.KeepHistory) return;
        _transcripts.Add(new TranscriptRecord
        {
            At = turn.At,
            Text = (turn.Speaker == Speaker.Interviewer ? "[Interviewer] " : "[You] ") + turn.Text,
        });
    }

    /// <summary>Builds the completer for the current settings.</summary>
    private static IChatCompleter BuildCompleter(SettingsData data)
    {
        if (string.Equals(data.AnswerBackend, "Cloud", StringComparison.OrdinalIgnoreCase))
        {
            var provider = data.CloudProviderName switch
            {
                "Anthropic" => CloudProvider.Anthropic,
                "OpenAiCompatible" => CloudProvider.OpenAiCompatible,
                _ => CloudProvider.OpenAi,
            };
            return new CloudChatCompleter(
                provider, data.CloudApiKey ?? string.Empty, data.CloudModel, data.CloudBaseUri);
        }

        if (string.Equals(data.AnswerBackend, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return new OllamaChatCompleter(data.SmartCleanModel);
        }

        return new BundledChatCompleter();
    }

    /// <summary>Swaps the completer when the AI settings changed; returns whether it did.</summary>
    private bool RebuildCompleterIfChanged()
    {
        // The assistant reads its completer through a level of indirection: swap it here
        // and the next draft uses the new backend. The old one is disposed.
        var data = _settings.Data;
        var key = (data.AnswerBackend, data.CloudProviderName, data.CloudApiKey, data.CloudModel, data.CloudBaseUri);
        if (key == _lastCompleterKey) return false;
        _lastCompleterKey = key;

        _completer?.Dispose();
        _completer = BuildCompleter(data);
        _assistant.ConfigureCompleter(_completer);
        return true;
    }

    private (string, string, string?, string, string?) _lastCompleterKey;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var session in _sessions.Values) session.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _completer?.Dispose();
        _assistant.Dispose();
        _transcribeGate.Dispose();
    }
}
