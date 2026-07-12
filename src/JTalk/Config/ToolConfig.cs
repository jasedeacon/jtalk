using System.Text.Json.Serialization;

namespace JTalk.Config;

/// <summary>Per-tool voice and prefix settings (one entry each for claude and codex).</summary>
public sealed record ToolConfig
{
    public string Prefix { get; set; } = "";
    public string WindowsVoice { get; set; } = "";
    public string PiperVoice { get; set; } = "";

    [JsonPropertyName("openaiVoice")] // pinned: camelCase policy would emit "openAIVoice"
    public string OpenAIVoice { get; set; } = "";
}
