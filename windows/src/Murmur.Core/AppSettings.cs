using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Core;

/// <summary>User preferences.</summary>
public sealed record SettingsData
{
    /// <summary>
    /// Virtual-key code of the push-to-talk key. Defaults to Right Ctrl (0xA3).
    /// </summary>
    /// <remarks>
    /// <b>Not Right Alt.</b> On German, Polish, UK, Nordic and most Latin-American layouts
    /// Right Alt is AltGr — it is how those users type <c>@</c>, <c>€</c>, <c>\</c> and
    /// <c>|</c>. Right Ctrl produces no character on any layout.
    /// </remarks>
    public int PushToTalkKey { get; init; } = 0xA3;

    /// <summary>Where the speech model lives, or null to search the default locations.</summary>
    public string? ModelDirectory { get; init; }

    /// <summary>
    /// The WASAPI endpoint id of the microphone to capture from, or null for the system
    /// default. Persisted so a multi-mic setup picks the same mic every launch.
    /// </summary>
    public string? InputDeviceId { get; init; }

    /// <summary>Linear gain applied to captured audio. 1.0 is unity (no boost).</summary>
    public float InputGain { get; init; } = 1f;

    /// <summary>Whether to type the transcript into the focused app.</summary>
    public bool InjectText { get; init; } = true;

    /// <summary>
    /// Whether to strip spoken disfluencies ("um", "er", "uh", "actually,") from
    /// transcripts — the Wispr Flow-style clean-up.
    /// </summary>
    public bool RemoveFillers { get; init; } = true;

    /// <summary>
    /// Whether to resolve spoken quantity corrections ("three potatoes no one potato no
    /// three minus one potatoes" → "2 potatoes") — the Wispr-style arithmetic trick.
    /// </summary>
    public bool SimplifyArithmetic { get; init; } = true;

    /// <summary>
    /// Whether to run the optional local-AI cleanup pass over the finished transcript.
    /// On by default since v16: the bundled model makes it self-contained. It adds roughly
    /// a second per dictation; the deterministic passes are always the baseline.
    /// </summary>
    public bool SmartClean { get; init; } = true;

    /// <summary>
    /// Which local model serves the smart pass: "Bundled" (the GGUF shipped in the zip —
    /// self-contained, default) or "Ollama" (a model already running in Ollama on this PC).
    /// </summary>
    public string SmartCleanBackend { get; init; } = "Bundled";

    /// <summary>
    /// The Ollama model tag for the smart pass, or null/empty to auto-pick the first
    /// installed model.
    /// </summary>
    public string? SmartCleanModel { get; init; }

    /// <summary>Whether to keep a transcript history.</summary>
    public bool KeepHistory { get; init; } = true;

    /// <summary>
    /// Command Mode's hold-to-speak key (0xA1 = Right Shift). Hold it, say what to do with
    /// the selected text ("make this more formal"), and the selection is rewritten in
    /// place. Applies on the next launch, like <see cref="PushToTalkKey"/>.
    /// </summary>
    public int CommandKey { get; init; } = 0xA1;

    /// <summary>
    /// What the window close button does. Null = the user has not decided yet and the first
    /// close asks; true = hide to the notification area and keep dictating; false = quit.
    /// </summary>
    public bool? CloseToTray { get; init; }

    /// <summary>
    /// Whether to unload the speech model after a period of no dictation. An idle Woffle
    /// should not be holding 660 MB; the next dictation pays a ~2s reload.
    /// </summary>
    public bool UnloadWhenIdle { get; init; } = true;
}

/// <summary>Settings, persisted as JSON.</summary>
public sealed class AppSettings
{
    private readonly string _path;

    /// <summary>Loads settings from <paramref name="path"/>, or defaults if absent.</summary>
    public AppSettings(string path)
    {
        _path = path;
        Data = Load(path);
    }

    /// <summary>The default location.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Woffle", "settings.json");

    /// <summary>Current values.</summary>
    public SettingsData Data { get; private set; }

    /// <summary>Raised after a successful save.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces and persists the settings.</summary>
    public void Update(SettingsData data)
    {
        Data = data;

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(data, SettingsJsonContext.Default.SettingsData));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static SettingsData Load(string path)
    {
        // Corrupt or unreadable settings must never stop the app launching — defaults are
        // always a working configuration.
        try
        {
            if (!File.Exists(path)) return new SettingsData();

            return JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.SettingsData)
                   ?? new SettingsData();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SettingsData();
        }
    }
}

/// <summary>Source-generated JSON for settings.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsData))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
