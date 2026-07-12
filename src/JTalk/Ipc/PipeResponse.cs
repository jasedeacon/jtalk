namespace JTalk.Ipc;

public sealed record PipeResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public DaemonStatus? Status { get; set; }
    public List<VoiceInfo>? Voices { get; set; }
}
