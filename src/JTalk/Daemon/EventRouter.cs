using JTalk.Config;
using JTalk.Ipc;
using JTalk.Logging;
using JTalk.Summarize;

namespace JTalk.Daemon;

/// <summary>
/// Turns incoming hook events into speech items: turn-complete goes through the
/// summarizer (async, overlapped with playback); attention and session-end are
/// spoken immediately from their short built-in text — no LLM latency where it hurts.
/// </summary>
public sealed class EventRouter : IDisposable
{
    private readonly ConfigService _config;
    private readonly SpeechQueue _queue;
    private readonly SummarizerPipeline _summarizer;

    public EventRouter(ConfigService config, SpeechQueue queue)
    {
        _config = config;
        _queue = queue;
        _summarizer = new SummarizerPipeline(config);
    }

    public void Dispose() => _summarizer.Dispose();

    public bool TryEnqueue(PipeRequest req)
    {
        var cfg = _config.Current;
        var kind = req.Kind ?? "";

        var enabled = kind switch
        {
            "turn" => cfg.Events.TurnComplete,
            "attention" => cfg.Events.Attention,
            "session-end" => cfg.Events.SessionEnd,
            _ => false,
        };
        if (!enabled)
        {
            Log.Debug($"event {req.Source}/{kind} disabled or unknown; ignored");
            return false;
        }

        if (cfg.LogPayloads)
            Log.Debug($"payload {req.Source}/{kind}: {req.Text}");

        var prefix = BuildPrefix(cfg, req);
        CancellationTokenSource? workCancellation = null;
        var textTask = kind switch
        {
            "turn" => BuildTurnTextAsync(
                prefix,
                cfg.ToolFor(req.Source).Prefix,
                req.Text ?? "",
                (workCancellation = new CancellationTokenSource()).Token),
            "attention" => Task.FromResult(Compose(prefix,
                string.IsNullOrWhiteSpace(req.Text) ? "needs your attention" : FallbackSummarizer.Clean(req.Text))),
            _ => Task.FromResult(Compose(prefix, "session ended")),
        };

        _queue.Enqueue(new SpeechItem(req.Source, kind, textTask, workCancellation));
        return true;
    }

    private async Task<string> BuildTurnTextAsync(
        string prefix,
        string toolName,
        string raw,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Compose(prefix, "finished a turn");
        var summary = await _summarizer.SummarizeAsync(raw, cancellationToken);
        // The LLM sometimes echoes the spoken name despite instructions; never say it twice.
        if (toolName.Length > 0 && summary.StartsWith(toolName + ":", StringComparison.OrdinalIgnoreCase))
            summary = summary[(toolName.Length + 1)..].TrimStart();
        return Compose(prefix, summary);
    }

    internal static string BuildPrefix(JTalkConfig cfg, PipeRequest req)
    {
        if (!cfg.PrefixEnabled || req.Source is null) return "";
        var prefix = cfg.ToolFor(req.Source).Prefix;
        if (prefix.Length == 0) return "";

        if (cfg.SpeakProject && !string.IsNullOrWhiteSpace(req.Cwd))
        {
            var leaf = Path.GetFileName(req.Cwd.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(leaf)) return $"{prefix}, in {leaf}";
        }
        return prefix;
    }

    internal static string Compose(string prefix, string body)
    {
        body = body.Trim();
        return prefix.Length == 0 ? body : $"{prefix}: {body}";
    }
}
