using JTalk.Config;
using JTalk.Logging;

namespace JTalk.Summarize;

/// <summary>
/// Picks the LLM backend per config ("auto" = whichever API key is present),
/// enforces the time budget, and always lands on FallbackSummarizer on any failure.
/// </summary>
public sealed class SummarizerPipeline : IDisposable
{
    private readonly ConfigService _config;
    private readonly Func<SummarizerConfig, ISummarizer?>? _backendResolver;
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _remoteSlots = new(initialCount: 2, maxCount: 2);
    private readonly List<IDisposable> _retired = [];
    private (string Backend, string Key, string Model)? _cachedFor;
    private ISummarizer? _cached;

    public SummarizerPipeline(ConfigService config) => _config = config;

    internal SummarizerPipeline(ConfigService config, Func<SummarizerConfig, ISummarizer?> backendResolver)
    {
        _config = config;
        _backendResolver = backendResolver;
    }

    /// <summary>Prepares LLM input: markdown-cleaned, then first 1500 + last 500 chars of long messages.</summary>
    public static string PrepareInput(string text, int maxChars)
    {
        var cleaned = FallbackSummarizer.CleanForLlm(text);
        if (cleaned.Length <= maxChars) return cleaned;
        var head = (int)(maxChars * 0.75);
        var tail = maxChars - head;
        return cleaned[..head] + " … " + cleaned[^tail..];
    }

    public async Task<string> SummarizeAsync(string text, CancellationToken cancellationToken)
    {
        var logPayloads = _config.Current.LogPayloads;
        var cfg = _config.Current.Summarizer;
        var backend = ResolveBackend(cfg);
        var input = PrepareInput(text, cfg.MaxInputChars);

        string? result = null;
        var via = "fallback";
        if (backend is not null)
        {
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(cfg.TimeoutMs);
                await _remoteSlots.WaitAsync(budget.Token);
                string summary;
                try
                {
                    summary = await backend.SummarizeAsync(input, budget.Token);
                }
                finally
                {
                    _remoteSlots.Release();
                }
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    result = summary.Trim();
                    via = backend.Name;
                }
                else
                {
                    Log.Warn($"{backend.Name} summarizer returned empty output; using fallback");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"{backend.Name} summarizer failed ({ex.GetType().Name}: {ex.Message}); using fallback");
            }
        }
        result ??= FallbackSummarizer.Clean(text);
        if (logPayloads)
            Log.Info($"summarize result ({via}): {result}");
        return result;
    }

    private ISummarizer? ResolveBackend(SummarizerConfig cfg)
    {
        var backend = cfg.Backend.ToLowerInvariant();
        if (backend == "off") return null;
        if (_backendResolver is not null) return _backendResolver(cfg);

        var anthropicKey = ApiKeys.Resolve(
            cfg.AnthropicApiKey,
            ApiKeys.Env(cfg.AnthropicApiKeyEnvVar),
            ApiKeys.Env("ANTHROPIC_API_KEY"));
        var openaiKey = ApiKeys.Resolve(
            cfg.OpenAIApiKey,
            ApiKeys.Env(cfg.OpenAIApiKeyEnvVar),
            ApiKeys.Env("OPENAI_API_KEY"));

        (string Backend, string Key, string Model)? want = backend switch
        {
            "anthropic" when anthropicKey is not null => ("anthropic", anthropicKey, cfg.AnthropicModel),
            "openai" when openaiKey is not null => ("openai", openaiKey, cfg.OpenAIModel),
            "auto" when anthropicKey is not null => ("anthropic", anthropicKey, cfg.AnthropicModel),
            "auto" when openaiKey is not null => ("openai", openaiKey, cfg.OpenAIModel),
            _ => null,
        };
        if (want is null)
        {
            if (backend is "anthropic" or "openai")
                Log.WarnOnce($"summarizer backend '{backend}' selected but no API key found; using fallback");
            return null;
        }

        lock (_sync)
        {
            if (_cachedFor != want)
            {
                // Never dispose a swapped-out backend eagerly: overlapping turn events may
                // still be summarizing on it. Retire it and dispose at daemon shutdown.
                if (_cached is IDisposable retired) _retired.Add(retired);
                _cached = want.Value.Backend == "anthropic"
                    ? new AnthropicSummarizer(want.Value.Key, want.Value.Model)
                    : new OpenAISummarizer(want.Value.Key, want.Value.Model);
                _cachedFor = want;
                Log.Info($"summarizer backend: {want.Value.Backend} ({want.Value.Model})");
            }
            return _cached;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var disposable in _retired) disposable.Dispose();
            _retired.Clear();
            (_cached as IDisposable)?.Dispose();
            _cached = null;
            _cachedFor = null;
        }
    }
}
