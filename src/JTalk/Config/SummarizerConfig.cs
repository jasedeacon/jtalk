using System.Text.Json.Serialization;

namespace JTalk.Config;

public sealed record SummarizerConfig
{
    public string Backend { get; set; } = "off";    // auto | anthropic | openai | off
    public string AnthropicModel { get; set; } = "claude-haiku-4-5";

    [JsonPropertyName("openaiModel")] // pinned: camelCase policy would emit "openAIModel"
    public string OpenAIModel { get; set; } = "gpt-5-mini";

    public int TimeoutMs { get; set; } = 5000;
    public int MaxInputChars { get; set; } = 2000;
    public string? AnthropicApiKey { get; set; }

    [JsonPropertyName("openaiApiKey")]
    public string? OpenAIApiKey { get; set; }

    // custom env var names; ANTHROPIC_API_KEY / OPENAI_API_KEY still checked last
    public string? AnthropicApiKeyEnvVar { get; set; }

    [JsonPropertyName("openaiApiKeyEnvVar")]
    public string? OpenAIApiKeyEnvVar { get; set; }
}
