using System.Text.Json.Serialization;

namespace JTalk.Ipc;

/// <summary>
/// One newline-delimited UTF-8 JSON request + response per pipe connection (\\.\pipe\jtalk).
/// </summary>
// Protocol records use `set`, not `init`, for the same reason as the config records:
// source-generated deserialization of init-only records discards declared defaults
// for properties missing from the JSON (Version must stay 1, not become 0).
public sealed record PipeRequest
{
    [JsonPropertyName("v")] // pinned: wire format predates the descriptive name
    public int Version { get; set; } = 1;

    public string Type { get; set; } = ""; // event | say | status | voices | quit
    public string? Source { get; set; }    // claude | codex (event only)
    public string? Kind { get; set; }      // turn | attention | session-end (event only)
    public string? Text { get; set; }
    public string? SessionId { get; set; }
    public string? Cwd { get; set; }
}
