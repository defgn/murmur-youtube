using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingTranscriber.Core;

/// <summary>Meeting Transcriber preferences.</summary>
/// <param name="MicDeviceId">The capture endpoint, or null for the OS default.</param>
/// <param name="OutputDeviceId">The render endpoint loopback captured for THEM, or null for the default.</param>
/// <param name="Backend">One of Codex, Zai, OpenAi, Anthropic or DeepSeek.</param>
/// <param name="ZaiApiKeyId">The z.ai key ID half.</param>
/// <param name="ZaiApiKeySecret">The z.ai key secret half.</param>
/// <param name="ZaiApiKey">The z.ai key (combined id.secret, or Bearer only usage).</param>
/// <param name="ZaiModel">The GLM model id.</param>
/// <param name="DeepSeekApiKey">A DeepSeek platform key (optional alternative).</param>
/// <param name="DeepSeekModel">Its model id.</param>
/// <param name="OpenAiApiKey">An OpenAI platform key (optional alternative).</param>
/// <param name="OpenAiModel">Its model id.</param>
/// <param name="AnthropicApiKey">An Anthropic key (optional alternative).</param>
/// <param name="AnthropicModel">Its model id.</param>
/// <param name="KeepTranscript">Whether the session transcript survives app restarts.</param>
public sealed record MeetingSettings(
    string? MicDeviceId = null,
    string? OutputDeviceId = null,
    string Backend = "Codex",
    string? ZaiApiKeyId = null,
    string? ZaiApiKeySecret = null,
    string? ZaiApiKey = null,
    string ZaiModel = MeetingCompleterDefaults.ZaiModel,
    string? DeepSeekApiKey = null,
    string DeepSeekModel = MeetingCompleterDefaults.DeepSeekModel,
    string? OpenAiApiKey = null,
    string OpenAiModel = "gpt-4o-mini",
    string? AnthropicApiKey = null,
    string AnthropicModel = "claude-sonnet-4-5",
    bool KeepTranscript = true);

/// <summary>Model id defaults, kept out of the record default so it can reference constants.</summary>
public static class MeetingCompleterDefaults
{
    /// <summary>z.ai current flagship GLM model.</summary>
    public const string ZaiModel = "glm-5.3";

    /// <summary>DeepSeek general purpose chat model.</summary>
    public const string DeepSeekModel = "deepseek-chat";

    /// <summary>Backends the settings UI offers.</summary>
    public static string[] Backends => ["Codex", "Zai", "OpenAi", "Anthropic", "DeepSeek"];
}

/// <summary>Settings, persisted as JSON with load time healing.</summary>
public sealed class MeetingSettingsStore
{
    private readonly string _path;

    /// <summary>Loads from <paramref name="path"/>, defaults when absent or unreadable.</summary>
    /// <param name="path">Full path of the settings file.</param>
    public MeetingSettingsStore(string path)
    {
        _path = path;
        Data = Heal(Load(path));
    }

    /// <summary>Local application data folder for Meeting Transcriber.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeetingTranscriber", "settings.json");

    /// <summary>Current values.</summary>
    public MeetingSettings Data { get; private set; }

    /// <summary>Raised after a successful save.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces and persists.</summary>
    /// <param name="data">New values.</param>
    public void Update(MeetingSettings data)
    {
        Data = Heal(data);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Data, MeetingJsonContext.Default.MeetingSettings));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Normalises a loaded or edited record: unknown backends fall back to Codex.</summary>
    /// <param name="data">The record to heal.</param>
    /// <returns>A valid record.</returns>
    public static MeetingSettings Heal(MeetingSettings data)
    {
        var backend = MeetingCompleterDefaults.Backends.Contains(data.Backend, StringComparer.Ordinal)
            ? data.Backend
            : "Codex";
        var zaiModel = string.IsNullOrWhiteSpace(data.ZaiModel)
            ? MeetingCompleterDefaults.ZaiModel
            : data.ZaiModel.Trim();
        var deepSeekModel = string.IsNullOrWhiteSpace(data.DeepSeekModel)
            ? MeetingCompleterDefaults.DeepSeekModel
            : data.DeepSeekModel.Trim();
        var openAiModel = string.IsNullOrWhiteSpace(data.OpenAiModel) ? "gpt-4o-mini" : data.OpenAiModel.Trim();
        var anthropicModel = string.IsNullOrWhiteSpace(data.AnthropicModel)
            ? "claude-sonnet-4-5"
            : data.AnthropicModel.Trim();
        return data with
        {
            Backend = backend,
            ZaiModel = zaiModel,
            DeepSeekModel = deepSeekModel,
            OpenAiModel = openAiModel,
            AnthropicModel = anthropicModel,
        };
    }

    private static MeetingSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new MeetingSettings();
            }

            return JsonSerializer.Deserialize(File.ReadAllText(path), MeetingJsonContext.Default.MeetingSettings)
                ?? new MeetingSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new MeetingSettings();
        }
    }
}

/// <summary>Source generated JSON context for settings.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MeetingSettings))]
internal sealed partial class MeetingJsonContext : JsonSerializerContext;
