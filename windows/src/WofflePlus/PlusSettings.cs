using System.Text.Json;
using System.Text.Json.Serialization;

namespace WofflePlus;

/// <summary>Woffle+ user preferences.</summary>
/// <param name="MicDeviceId">The capture endpoint, or null for the OS default.</param>
/// <param name="OutputDeviceId">
/// The render endpoint loopback-captured for the interviewer, or null for the default.
/// </param>
/// <param name="Backend">
/// "Codex" (ChatGPT subscription), "Zai", "OpenAi", or "Anthropic".
/// </param>
/// <param name="ZaiApiKeyId">
/// The z.ai key ID half. z.ai keys are issued as id.secret; when both halves are set the
/// request authenticates with a signed JWT (their documented high-security flow). Leave
/// both empty and paste the full combined key into ZaiApiKey to use plain Bearer instead.
/// </param>
/// <param name="ZaiApiKeySecret">The z.ai key secret half.</param>
/// <param name="ZaiApiKey">The z.ai key (combined id.secret, or Bearer-only usage).</param>
/// <param name="ZaiModel">The GLM model id.</param>
/// <param name="OpenAiApiKey">An OpenAI platform key (optional alternative).</param>
/// <param name="OpenAiModel">Its model id.</param>
/// <param name="AnthropicApiKey">An Anthropic key (optional alternative).</param>
/// <param name="AnthropicModel">Its model id.</param>
/// <param name="DeepSeekApiKey">A DeepSeek platform key (optional alternative).</param>
/// <param name="DeepSeekModel">Its model id.</param>
/// <param name="KeepTranscript">Whether the session transcript survives app restarts.</param>
internal sealed record PlusSettings(
    string? MicDeviceId = null,
    string? OutputDeviceId = null,
    string Backend = "Codex",
    string? ZaiApiKeyId = null,
    string? ZaiApiKeySecret = null,
    string? ZaiApiKey = null,
    string ZaiModel = PlusCompleterDefaults.ZaiModel,
    string? DeepSeekApiKey = null,
    string DeepSeekModel = PlusCompleterDefaults.DeepSeekModel,
    string? OpenAiApiKey = null,
    string OpenAiModel = "gpt-4o-mini",
    string? AnthropicApiKey = null,
    string AnthropicModel = "claude-sonnet-4-5",
    bool KeepTranscript = true);

/// <summary>Model id defaults, kept out of the record default so it can reference the cloud constants.</summary>
internal static class PlusCompleterDefaults
{
    /// <summary>z.ai's current flagship GLM model.</summary>
    public const string ZaiModel = "glm-5.3";

    /// <summary>DeepSeek's general-purpose chat model.</summary>
    public const string DeepSeekModel = "deepseek-chat";
}

/// <summary>Settings, persisted as JSON.</summary>
internal sealed class PlusSettingsStore
{
    private readonly string _path;

    /// <summary>Loads from <paramref name="path"/>, defaults when absent or unreadable.</summary>
    public PlusSettingsStore(string path)
    {
        _path = path;
        Data = Load(path);
    }

    /// <summary>%LOCALAPPDATA%\WofflePlus\settings.json</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WofflePlus", "settings.json");

    /// <summary>Current values.</summary>
    public PlusSettings Data { get; private set; }

    /// <summary>Raised after a successful save.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces and persists.</summary>
    public void Update(PlusSettings data)
    {
        Data = data;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(data, PlusJsonContext.Default.PlusSettings));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static PlusSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new PlusSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(path), PlusJsonContext.Default.PlusSettings)
                   ?? new PlusSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new PlusSettings();
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PlusSettings))]
internal sealed partial class PlusJsonContext : JsonSerializerContext;
