using System.Text.Json.Serialization;

namespace JTalk.Ipc;

public sealed record DaemonStatus
{
    public string Version { get; set; } = "";
    public bool Muted { get; set; }
    public string Engine { get; set; } = "";
    public string Summarizer { get; set; } = "";
    public int Queue { get; set; }
    public bool Speaking { get; set; }

    [JsonPropertyName("uptimeSec")] // pinned: wire format predates the unabbreviated name
    public long UptimeSeconds { get; set; }
}
