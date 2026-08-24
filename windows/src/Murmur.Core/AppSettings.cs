using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Core;

/// <summary>User preferences.</summary>
/// <param name="PushToTalkKey">
/// Virtual-key code of the push-to-talk key (0xA3 = Right Ctrl). <b>Not Right Alt:</b> on
/// German, Polish, UK, Nordic and most Latin-American layouts Right Alt is AltGr — it is how
/// those users type <c>@</c>, <c>€</c>, <c>\\</c> and <c>|</c>. Right Ctrl produces no
/// character on any layout.
/// </param>
/// <param name="SpeechModel">
/// Which speech model to use: "Accurate" (Parakeet TDT 0.6B, ~660 MB) or "Compact"
/// (Parakeet CTC 110M, ~126 MB, ~4× faster — the default since v20).
/// </param>
/// <param name="ModelDirectory">Where the speech model lives, or null to search the defaults.</param>
/// <param name="InputDeviceId">
/// The WASAPI endpoint id of the microphone, or null for the system default.
/// </param>
/// <param name="InputGain">Linear gain applied to captured audio. 1.0 is unity.</param>
/// <param name="InjectText">Whether to type the transcript into the focused app.</param>
/// <param name="RemoveFillers">Whether to strip spoken disfluencies ("um", "er", "actually,").</param>
/// <param name="SimplifyArithmetic">
/// Whether to resolve spoken quantity corrections ("three potatoes no one potato no three
/// minus one potatoes" → "2 potatoes").
/// </param>
/// <param name="SmartClean">
/// Whether to run the optional local-AI cleanup pass over the finished transcript.
/// </param>
/// <param name="SmartCleanBackend">
/// Which local model serves the smart pass: "Bundled" (the GGUF shipped in the zip) or
/// "Ollama" (a model already running in Ollama on this PC).
/// </param>
/// <param name="SmartCleanModel">
/// The Ollama model tag, or null/empty to auto-pick the first installed model.
/// </param>
/// <param name="KeepHistory">Whether to keep a transcript history.</param>
/// <param name="CommandKey">
/// Command Mode's hold-to-speak key (0xA1 = Right Shift).
/// </param>
/// <param name="CloseToTray">
/// What the window close button does. Null = the user has not decided yet and the first
/// close asks; true = hide to the notification area; false = quit.
/// </param>
/// <param name="UnloadWhenIdle">
/// Whether to unload the speech model after a period of no dictation.
/// </param>
/// <remarks>
/// <para>
/// A positional record on purpose: System.Text.Json binds missing JSON fields to
/// <i>constructor parameter defaults</i>, which init-only property initializers do not
/// provide — before this shape, a settings file without the newer fields silently loaded
/// them as zero/null (CommandKey 0 = a dead command hotkey) and re-saved that corruption.
/// </para>
/// </remarks>
public sealed record SettingsData(
    int PushToTalkKey = 0xA3,
    string SpeechModel = "Compact",
    string? ModelDirectory = null,
    string? InputDeviceId = null,
    float InputGain = 1f,
    bool InjectText = true,
    bool RemoveFillers = true,
    bool SimplifyArithmetic = true,
    bool SmartClean = true,
    string SmartCleanBackend = "Bundled",
    string? SmartCleanModel = null,
    bool KeepHistory = true,
    int CommandKey = 0xA1,
    bool? CloseToTray = null,
    bool UnloadWhenIdle = true);

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

            var data = JsonSerializer.Deserialize(
                File.ReadAllText(path), SettingsJsonContext.Default.SettingsData)
                ?? new SettingsData();

            // Heal files corrupted by an older deserializer bug: before the record carried
            // constructor defaults, missing JSON fields deserialized to default(T) — 0 for
            // the keys, null for the strings — and were then re-saved that way. A zero
            // CommandKey is a dead command hotkey; a null SpeechModel falls back to Compact.
            if (data.CommandKey == 0) data = data with { CommandKey = 0xA1 };
            if (data.PushToTalkKey == 0) data = data with { PushToTalkKey = 0xA3 };
            if (data.SpeechModel is null) data = data with { SpeechModel = "Compact" };
            if (data.SmartCleanBackend is null) data = data with { SmartCleanBackend = "Bundled" };
            return data;
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
