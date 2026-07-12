using Anthropic;
using Anthropic.Models.Messages;

namespace JTalk.Summarize;

/// <summary>One-line spoken summaries via the Anthropic API (claude-haiku-4-5 by default).</summary>
public sealed class AnthropicSummarizer : ISummarizer, IDisposable
{
    private readonly AnthropicClient _client;
    private readonly string _model;

    public string Name => "anthropic";

    public AnthropicSummarizer(string apiKey, string model)
    {
        _model = model;
        // The pipeline enforces the overall budget; keep the client inside it:
        // tight timeout, no retries (a late summary is worthless).
        _client = new AnthropicClient
        {
            ApiKey = apiKey,
            Timeout = TimeSpan.FromSeconds(4),
            MaxRetries = 0,
        };
    }

    public async Task<string> SummarizeAsync(string text, CancellationToken cancellationToken)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 60,
            System = SummarizerPrompt.System,
            Messages = [new() { Role = Role.User, Content = SummarizerPrompt.User(text) }],
        }, cancellationToken: cancellationToken);

        return string.Concat(response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text));
    }

    public void Dispose() => _client.Dispose();
}
