using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JTalk.Summarize;

/// <summary>
/// One-line spoken summaries via the OpenAI chat completions API. Raw HttpClient on
/// purpose — two fixed POST shapes don't justify an SDK dependency.
/// </summary>
public sealed class OpenAISummarizer : ISummarizer, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;

    public string Name => "openai";

    public OpenAISummarizer(string apiKey, string model)
    {
        _model = model;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> SummarizeAsync(string text, CancellationToken cancellationToken)
    {
        // Reasoning models (gpt-5 family) spend max_completion_tokens on hidden
        // reasoning before writing any content — without minimal effort and a
        // generous cap they can return an empty message for a one-line task.
        var payload = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["max_completion_tokens"] = 1024,
            ["messages"] = new object[]
            {
                new { role = "system", content = SummarizerPrompt.System },
                new { role = "user", content = SummarizerPrompt.User(text) },
            },
        };
        if (_model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
            payload["reasoning_effort"] = "minimal";
        var body = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"openai {(int)response.StatusCode}: {(json.Length > 200 ? json[..200] : json)}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    public void Dispose() => _http.Dispose();
}
