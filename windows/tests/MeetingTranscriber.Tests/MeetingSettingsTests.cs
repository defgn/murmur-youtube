using MeetingTranscriber.Core;
using Xunit;

namespace MeetingTranscriberTests;

public sealed class MeetingSettingsTests
{
    [Fact]
    public void Round_trip_preserves_values()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new MeetingSettingsStore(path);
            var edited = store.Data with
            {
                MicDeviceId = "mic-1",
                OutputDeviceId = "out-2",
                Backend = "DeepSeek",
                DeepSeekApiKey = "secret",
                DeepSeekModel = "deepseek-chat",
                KeepTranscript = false,
            };
            store.Update(edited);

            var reloaded = new MeetingSettingsStore(path);

            Assert.Equal("mic-1", reloaded.Data.MicDeviceId);
            Assert.Equal("out-2", reloaded.Data.OutputDeviceId);
            Assert.Equal("DeepSeek", reloaded.Data.Backend);
            Assert.Equal("secret", reloaded.Data.DeepSeekApiKey);
            Assert.False(reloaded.Data.KeepTranscript);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unknown_backend_heals_to_codex()
    {
        var healed = MeetingSettingsStore.Heal(new MeetingSettings(Backend: "Local"));

        Assert.Equal("Codex", healed.Backend);
    }

    [Fact]
    public void Blank_models_heal_to_defaults()
    {
        var healed = MeetingSettingsStore.Heal(
            new MeetingSettings(ZaiModel: " ", DeepSeekModel: "", OpenAiModel: "", AnthropicModel: ""));

        Assert.Equal(MeetingCompleterDefaults.ZaiModel, healed.ZaiModel);
        Assert.Equal(MeetingCompleterDefaults.DeepSeekModel, healed.DeepSeekModel);
        Assert.Equal("gpt-4o-mini", healed.OpenAiModel);
        Assert.Equal("claude-sonnet-4-5", healed.AnthropicModel);
    }

    [Fact]
    public void Missing_file_loads_defaults()
    {
        var store = new MeetingSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));

        Assert.Equal("Codex", store.Data.Backend);
        Assert.True(store.Data.KeepTranscript);
    }
}
