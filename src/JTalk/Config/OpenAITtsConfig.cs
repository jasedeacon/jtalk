namespace JTalk.Config;

public sealed record OpenAITtsConfig
{
    public string Model { get; set; } = "gpt-4o-mini-tts";
    public string Instructions { get; set; } = // pace/tone steering; only sent to gpt-* models
        "Speak at maximum pace while keeping every word crisp and intelligible. " +
        "Brief, efficient, energetic status update; no filler, no dramatic pauses.";
    public string? ApiKey { get; set; }
    public string? ApiKeyEnvVar { get; set; } // custom env var name; falls back to summarizer key, then OPENAI_API_KEY
}
