namespace JTalk.Daemon;

/// <summary>One queued utterance; the text may still be resolving (summarizer) when enqueued.</summary>
public sealed record SpeechItem(
    string? Source,
    string Kind,
    Task<string> TextTask,
    CancellationTokenSource? WorkCancellation = null) : IDisposable
{
    public DateTime EnqueuedUtc { get; } = DateTime.UtcNow;

    public void Cancel()
    {
        try { WorkCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void Dispose() => WorkCancellation?.Dispose();

    public override string ToString() => $"{Source ?? "manual"}/{Kind}";
}
