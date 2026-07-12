namespace JTalk.Summarize;

public interface ISummarizer
{
    string Name { get; }

    /// <summary>Turns an assistant's final message into one short spoken sentence.</summary>
    Task<string> SummarizeAsync(string text, CancellationToken cancellationToken);
}
