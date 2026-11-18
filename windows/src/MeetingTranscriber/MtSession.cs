using MeetingTranscriber.Cloud;
using MeetingTranscriber.Core;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;

namespace MeetingTranscriber;

/// <summary>
/// Meeting engine: two ConversationSession feeds (mic as YOU, loopback as THEM),
/// live device swap, pause and resume. Speaker attribution comes free from the feed.
/// </summary>
internal sealed class MtSession : IDisposable
{
    private readonly MeetingSettingsStore _settings;
    private readonly Dictionary<bool, ConversationSession> _sessions = new();
    private readonly SemaphoreSlim _transcribeGate = new(1, 1);
    private readonly SemaphoreSlim _swapGate = new(1, 1);
    private readonly object _gate = new();
    private ITranscriber? _transcriber;
    private MtCompleter? _completer;
    private bool _listening = true;
    private bool _started;
    private bool _disposed;
    private CancellationTokenSource? _heartbeatCts;

    private DateTime _micHeardUtc = DateTime.UtcNow;
    private DateTime _speakerHeardUtc = DateTime.UtcNow;
    private string _lastHint = string.Empty;
    private DateTime _lastHintUtc = DateTime.MinValue;

    /// <summary>Raised with each finished utterance, feed already mapped to YOU/THEM.</summary>
    public event EventHandler<TranscriptUtterance>? UtteranceReady;

    /// <summary>User readable notices (failures and silent feed hints included).</summary>
    public event EventHandler<string>? Notice;

    /// <summary>Raised when the ChatGPT (Codex) sign-in state changes.</summary>
    public event EventHandler? CodexAuthChanged;

    /// <summary>Raised when the listening toggle changes state.</summary>
    public event EventHandler<bool>? ListeningChanged;

    /// <summary>Whether the feeds are currently being captured.</summary>
    public bool Listening
    {
        get { lock (_gate) return _listening; }
    }

    /// <summary>The answer backend; rebuilt when the settings change.</summary>
    public MtCompleter? Completer => _completer;

    /// <summary>Builds the session over <paramref name="settings"/>.</summary>
    /// <param name="settings">Shared settings store.</param>
    public MtSession(MeetingSettingsStore settings)
    {
        _settings = settings;
        _completer = BuildCompleter(settings.Data);
        _settings.Changed += (_, _) => RebuildCompleter();
        _heartbeatCts = new CancellationTokenSource();
        _ = HeartbeatAsync(_heartbeatCts.Token);
    }

    /// <summary>Starts both feeds once the platform layer is present.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        if (!Platform.Devices.IsAvailable)
        {
            Notice?.Invoke(
                this,
                "Audio devices unavailable: Meeting Transcriber needs the Windows platform layer. " +
                "Transcript, alert and summary still work from typed or demo input.");
            return;
        }

        _started = true;
        _transcriber = TranscriberLocate.Resolve() ?? new UnavailableTranscriber();

        var dictionary = new DictionaryFile(DictionaryFile.DefaultPath);
        Func<IReadOnlyList<string>> bias = () => DictionaryCorrector.BiasPhrases(dictionary.Entries);

        var mic = Platform.Devices.CreateCapture(_settings.Data.MicDeviceId);
        var loopback = Platform.Devices.CreateLoopback(_settings.Data.OutputDeviceId);

        var micSession = new ConversationSession(mic!, _transcriber, bias, removeFillers: true, _transcribeGate);
        micSession.Utterance += (_, text) =>
        {
            _micHeardUtc = DateTime.UtcNow;
            UtteranceReady?.Invoke(this, new TranscriptUtterance(Guid.NewGuid(), DateTimeOffset.Now, FeedKind.You, text));
        };

        var loopbackSession = new ConversationSession(loopback!, _transcriber, bias, removeFillers: true, _transcribeGate);
        loopbackSession.Utterance += (_, text) =>
        {
            _speakerHeardUtc = DateTime.UtcNow;
            UtteranceReady?.Invoke(this, new TranscriptUtterance(Guid.NewGuid(), DateTimeOffset.Now, FeedKind.Them, text));
        };

        micSession.Fault += (_, m) => Notice?.Invoke(this, m);
        loopbackSession.Fault += (_, m) => Notice?.Invoke(this, m);

        _sessions[true] = micSession;
        _sessions[false] = loopbackSession;

        var now = DateTime.UtcNow;
        _micHeardUtc = now;
        _speakerHeardUtc = now;

        if (Listening)
        {
            RunAll();
        }
    }

    /// <summary>Turns listening on or off across both feeds.</summary>
    /// <param name="on">True to capture, false to pause.</param>
    public void SetListening(bool on)
    {
        bool changed;
        lock (_gate)
        {
            changed = _listening != on;
            _listening = on;
        }

        if (!changed)
        {
            return;
        }

        if (on)
        {
            var now = DateTime.UtcNow;
            _micHeardUtc = now;
            _speakerHeardUtc = now;
            if (_started)
            {
                RunAll();
            }
        }
        else
        {
            _ = StopAllAsync();
        }

        ListeningChanged?.Invoke(this, on);
    }

    /// <summary>Flips the listening state; returns the new state.</summary>
    /// <returns>True when now listening.</returns>
    public bool ToggleListening()
    {
        bool next;
        lock (_gate)
        {
            next = !_listening;
            _listening = next;
        }

        SetListening(next);
        return next;
    }

    /// <summary>Applies device changes live: serialised stop, swap, restart per feed.</summary>
    /// <param name="micId">Mic device id, or null for default.</param>
    /// <param name="outputId">Loopback device id, or null for default.</param>
    public void ConfigureDevices(string? micId, string? outputId)
    {
        if (!_started)
        {
            return;
        }

        _ = SwapAsync(micId, outputId);
    }

    private async Task SwapAsync(string? micId, string? outputId)
    {
        await _swapGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_sessions.TryGetValue(true, out var micSession))
            {
                micSession.ConfigureDevice(micId);
                if (Listening)
                {
                    await SafeRestartAsync(micSession).ConfigureAwait(false);
                }
            }

            if (_sessions.TryGetValue(false, out var loopbackSession))
            {
                loopbackSession.ConfigureDevice(outputId);
                if (Listening)
                {
                    await SafeRestartAsync(loopbackSession).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _swapGate.Release();
        }
    }

    private void RunAll()
    {
        foreach (var session in _sessions.Values)
        {
            session.Start();
        }
    }

    private async Task StopAllAsync()
    {
        foreach (var session in _sessions.Values)
        {
            try
            {
                await session.StopAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }

    private static async Task SafeRestartAsync(ConversationSession session)
    {
        try
        {
            await session.RestartAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    /// <summary>Runs the ChatGPT subscription sign-in; returns tokens on success.</summary>
    /// <param name="openBrowser">Opens the browser at the sign-in URL.</param>
    /// <returns>The cached tokens, or null when sign-in failed.</returns>
    public async Task<CodexTokens?> SignInCodexAsync(Action<string> openBrowser)
    {
        var tokens = await new CodexLogin().LoginAsync(openBrowser, CancellationToken.None).ConfigureAwait(true);
        if (tokens is not null)
        {
            RebuildCompleter();
            CodexAuthChanged?.Invoke(this, EventArgs.Empty);
        }

        return tokens;
    }

    /// <summary>Signs out of the ChatGPT subscription.</summary>
    public void SignOutCodex()
    {
        CodexLogin.SignOut();
        RebuildCompleter();
        CodexAuthChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
                if (!Listening || !_started)
                {
                    continue;
                }

                var now = DateTime.UtcNow;
                var micSilent = (now - _micHeardUtc).TotalSeconds > 25;
                var speakerSilent = (now - _speakerHeardUtc).TotalSeconds > 25;
                var anyHeard = (now - _micHeardUtc).TotalSeconds <= 25 ||
                    (now - _speakerHeardUtc).TotalSeconds <= 25;

                string? hint = null;
                if (!anyHeard)
                {
                    hint = "No audio detected yet: speak to test the mic, or play a video to test the SYSTEM feed.";
                }
                else if (speakerSilent)
                {
                    hint = "SYSTEM feed is silent: is the call audio going to the output device selected above?";
                }
                else if (micSilent)
                {
                    hint = "MIC feed is silent: check the microphone picker and Windows mic privacy settings.";
                }

                if (hint is null)
                {
                    _lastHint = string.Empty;
                    continue;
                }

                if (hint == _lastHint && (now - _lastHintUtc).TotalSeconds < 60)
                {
                    continue;
                }

                _lastHint = hint;
                _lastHintUtc = now;
                Notice?.Invoke(this, hint);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private MtCompleter BuildCompleter(MeetingSettings data) => data.Backend switch
    {
        "Zai" => MtCompleter.ForZai(data.ZaiApiKeyId, data.ZaiApiKeySecret, data.ZaiApiKey, data.ZaiModel),
        "OpenAi" => MtCompleter.ForKey(CloudBackend.OpenAiKey, data.OpenAiApiKey ?? string.Empty, data.OpenAiModel),
        "Anthropic" => MtCompleter.ForKey(CloudBackend.AnthropicKey, data.AnthropicApiKey ?? string.Empty, data.AnthropicModel),
        "DeepSeek" => MtCompleter.ForKey(CloudBackend.DeepSeekKey, data.DeepSeekApiKey ?? string.Empty, data.DeepSeekModel, MtCompleter.DeepSeekBaseUri),
        _ => MtCompleter.ForCodexSubscription(),
    };

    private void RebuildCompleter()
    {
        var old = Interlocked.Exchange(ref _completer, BuildCompleter(_settings.Data));
        old?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_heartbeatCts is not null)
        {
            _heartbeatCts.Cancel();
            _heartbeatCts.Dispose();
            _heartbeatCts = null;
        }

        foreach (var session in _sessions.Values)
        {
            session.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }

        _completer?.Dispose();
        _transcribeGate.Dispose();
        _swapGate.Dispose();
    }
}

/// <summary>Resolves the speech model folder for the conversation feeds.</summary>
internal static class TranscriberLocate
{
    /// <summary>Finds the installed model directory, or null when no model is installed.</summary>
    /// <returns>The model directory, or null.</returns>
    public static ITranscriber? Resolve()
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
