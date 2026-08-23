using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;
using Murmur.Speech;

namespace Murmur.App;

/// <summary>
/// Wires the app together: settings, storage, the platform layer, and the engine.
/// </summary>
/// <remarks>
/// <para>
/// The only place that knows about every layer at once. Views take what they need; nothing
/// else reaches across.
/// </para>
/// <para>
/// The platform implementations are resolved by reflection rather than referenced directly,
/// so <c>Murmur.App</c> can target plain <c>net10.0</c> and therefore be built, run and
/// headless-tested on macOS — which is the entire reason Avalonia was chosen over WPF. On any
/// non-Windows machine the lookup simply finds nothing and the app runs with inert stand-ins,
/// which is exactly what a UI test wants.
/// </para>
/// </remarks>
public sealed class Composition : IAsyncDisposable
{
    private Composition(
        AppSettings settings,
        DictionaryFile dictionary,
        TranscriptStore transcripts,
        DictationEngine? engine,
        bool platformAvailable)
    {
        Settings = settings;
        Dictionary = dictionary;
        Transcripts = transcripts;
        Engine = engine;
        IsPlatformAvailable = platformAvailable;
    }

    /// <summary>User preferences.</summary>
    public AppSettings Settings { get; }

    /// <summary>The correction dictionary.</summary>
    public DictionaryFile Dictionary { get; }

    /// <summary>Transcript history.</summary>
    public TranscriptStore Transcripts { get; }

    /// <summary>The dictation engine, or null when no platform layer is available.</summary>
    public DictationEngine? Engine { get; }

    /// <summary>Whether real audio and hotkey support were found.</summary>
    public bool IsPlatformAvailable { get; }

    /// <summary>Whether the speech model is present on disk.</summary>
    public static bool IsModelInstalled => ParakeetTranscriber.Locate() is not null;

    /// <summary>
    /// Selects the microphone and gain, persists them, and applies them to the running
    /// engine immediately (the capture reads the device at the start of each recording).
    /// </summary>
    public void ConfigureInput(string? deviceId, float gain)
    {
        Settings.Update(Settings.Data with { InputDeviceId = deviceId, InputGain = gain });
        Engine?.ConfigureInput(deviceId, gain);
    }

    /// <summary>Builds the object graph.</summary>
    public static Composition Create()
    {
        var settings = new AppSettings(AppSettings.DefaultPath);
        var dictionary = new DictionaryFile(DictionaryFile.DefaultPath);
        var transcripts = new TranscriptStore(TranscriptStore.DefaultPath);

        var capture = PlatformFactory.CreateAudioCapture(settings.Data.InputDeviceId);
        var hotkey = PlatformFactory.CreateHotkeySource(settings.Data.PushToTalkKey);
        var injector = PlatformFactory.CreateTextInjector();

        DictationEngine? engine = null;
        var available = capture is not null && hotkey is not null && injector is not null;

        if (available)
        {
            var modelDirectory = settings.Data.ModelDirectory ?? ParakeetTranscriber.Locate();

            ITranscriber transcriber = modelDirectory is not null
                ? new ParakeetTranscriber(modelDirectory)
                : new UnavailableTranscriber();

            engine = new DictationEngine(
                capture!, hotkey!, transcriber, injector!,
                () => dictionary.Entries,
                clock: null,
                removeFillers: settings.Data.RemoveFillers,
                simplifyArithmetic: settings.Data.SimplifyArithmetic,
                idleUnloadTimeout: settings.Data.UnloadWhenIdle ? TimeSpan.FromMinutes(5) : TimeSpan.Zero);

            // Persisted mic choice + boost apply live, without a restart.
            engine.ConfigureInput(settings.Data.InputDeviceId, settings.Data.InputGain);

            engine.Completed += (_, result) =>
            {
                if (!settings.Data.KeepHistory) return;

                transcripts.Add(new TranscriptRecord
                {
                    At = result.At,
                    AudioSeconds = result.AudioDuration.TotalSeconds,
                    ProcessingSeconds = result.ProcessingTime.TotalSeconds,
                    Text = result.Text,
                    Corrections = result.Corrections.Count > 0 ? result.Corrections : null,
                });
            };
        }

        return new Composition(settings, dictionary, transcripts, engine, available);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Engine is not null) await Engine.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// A transcriber that reports the model is missing rather than throwing.
/// </summary>
/// <remarks>
/// A fresh Windows install has no model, and that has to be a readable message in Settings
/// rather than a crash on first press.
/// </remarks>
internal sealed class UnavailableTranscriber : ITranscriber
{
    public bool IsReady => false;

    public ValueTask<bool> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        IReadOnlyList<string> biasPhrases,
        CancellationToken cancellationToken) => ValueTask.FromResult(string.Empty);

    public ValueTask UnloadAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
