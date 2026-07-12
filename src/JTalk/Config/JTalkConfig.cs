using System.Text.Json.Serialization;

namespace JTalk.Config;

// Config records use `set`, not `init`: the JSON source generator constructs
// init-only records via an object initializer, which silently replaces every
// property missing from the JSON with default(T) instead of keeping the
// declared defaults (volume 80 became 0 on partial configs). With settable
// properties it uses the parameterless constructor and assigns only the
// properties actually present.
public sealed record JTalkConfig
{
    public string Engine { get; set; } = "windows"; // windows | piper | openai
    public bool Muted { get; set; }
    public int Volume { get; set; } = 80;           // 0-100
    public double Rate { get; set; } = 1.0;         // 0.5-6.0 (engine-clamped)
    public bool PrefixEnabled { get; set; } = true;
    public bool SpeakProject { get; set; }

    public IReadOnlyDictionary<string, ToolConfig> Tools { get; set; } = new Dictionary<string, ToolConfig>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = new ToolConfig
        {
            Prefix = "Claude",
            WindowsVoice = "Hazel",
            PiperVoice = "en_GB-alba-medium",
            OpenAIVoice = "nova",
        },
        ["codex"] = new ToolConfig
        {
            Prefix = "Codex",
            WindowsVoice = "George",
            PiperVoice = "en_GB-alan-medium",
            OpenAIVoice = "onyx",
        },
    };

    public SummarizerConfig Summarizer { get; set; } = new();

    [JsonPropertyName("openaiTts")] // pinned: camelCase policy would emit "openAITts"
    public OpenAITtsConfig OpenAITts { get; set; } = new();

    public PiperConfig Piper { get; set; } = new();
    public EventsConfig Events { get; set; } = new();

    public int MaxQueue { get; set; } = 20;
    public int IdleExitMinutes { get; set; } // 0 = stay resident
    public string LogLevel { get; set; } = "info";
    public bool LogPayloads { get; set; }

    public ToolConfig ToolFor(string? source) =>
        source is not null && Tools.TryGetValue(source, out var tool) ? tool : new ToolConfig();

    /// <summary>
    /// Applied after every load: hand-edited config must not crash or silently misbehave.
    /// Normalizes engine casing, clamps numeric ranges, and rebuilds Tools with the
    /// ignore-case comparer that deserialization silently drops.
    /// </summary>
    public JTalkConfig Normalized() => this with
    {
        Engine = Engine.Trim().ToLowerInvariant(),
        Volume = Math.Clamp(Volume, 0, 100),
        Rate = Math.Clamp(Rate, 0.25, 6.0),
        MaxQueue = Math.Max(1, MaxQueue),
        Tools = new Dictionary<string, ToolConfig>(Tools, StringComparer.OrdinalIgnoreCase),
        Summarizer = Summarizer with
        {
            TimeoutMs = Math.Clamp(Summarizer.TimeoutMs, 500, 60_000),
            MaxInputChars = Math.Clamp(Summarizer.MaxInputChars, 200, 20_000),
        },
    };
}
