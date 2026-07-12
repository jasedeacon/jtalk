namespace JTalk.Config;

public sealed record EventsConfig
{
    public bool TurnComplete { get; set; } = true;
    public bool Attention { get; set; } = true;
    public bool SessionEnd { get; set; } = true;
}
