using System.Threading.Channels;
using JTalk.Config;
using JTalk.Logging;
using JTalk.Speech;

namespace JTalk.Daemon;

/// <summary>
/// Strict-FIFO speech pipeline: items are enqueued in arrival order; a single worker
/// awaits each item's text (summaries resolve concurrently with earlier playback),
/// synthesizes with the configured engine (falling down the chain on failure), and
/// plays back sequentially — the daemon never talks over itself.
/// </summary>
public sealed class SpeechQueue
{
    private readonly ConfigService _config;
    private readonly Channel<SpeechItem> _channel;

    private ISpeechEngine? _windows;
    private ISpeechEngine? _piper;
    private ISpeechEngine? _openai;

    // Written by the worker thread, read from pipe handlers and the idle monitor.
    private volatile bool _speaking;
    private long _lastActivityUtcTicks = DateTime.UtcNow.Ticks;

    public bool Speaking => _speaking;
    public int PendingCount => _channel.Reader.CanCount ? _channel.Reader.Count : 0;
    public DateTime LastActivityUtc => new(Volatile.Read(ref _lastActivityUtcTicks), DateTimeKind.Utc);

    public SpeechQueue(ConfigService config)
    {
        _config = config;
        _channel = Channel.CreateBounded<SpeechItem>(
            new BoundedChannelOptions(Math.Max(1, config.Current.MaxQueue))
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            },
            dropped =>
            {
                dropped.Cancel();
                dropped.Dispose();
                Log.Warn($"queue full; dropped oldest item: {dropped}");
            });
    }

    public void Enqueue(SpeechItem item)
    {
        Volatile.Write(ref _lastActivityUtcTicks, DateTime.UtcNow.Ticks);
        if (!_channel.Writer.TryWrite(item))
        {
            item.Cancel();
            item.Dispose();
            Log.Warn($"queue rejected item: {item}");
        }
    }

    public void Start(CancellationToken ct) => _ = Task.Run(() => WorkerAsync(ct), CancellationToken.None);

    private async Task WorkerAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var cfg = _config.Current;
                    if (cfg.Muted)
                    {
                        item.Cancel();
                        Log.Info($"muted; dropped {item}");
                        continue;
                    }

                    string text;
                    try
                    {
                        // The text task already owns its own timeout/fallback; this is a hard cap
                        // so a wedged summarizer can never stall the whole queue.
                        text = await item.TextTask.WaitAsync(
                            TimeSpan.FromMilliseconds(cfg.Summarizer.TimeoutMs + 5000), ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        item.Cancel();
                        break;
                    }
                    catch (Exception ex)
                    {
                        item.Cancel();
                        Log.Warn($"text task failed for {item}: {ex.Message}");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(text)) continue;

                    if (cfg.LogPayloads)
                        Log.Info($"speaking ({item}): {text}");

                    _speaking = true;
                    try
                    {
                        await SpeakAsync(item, text, cfg, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"speak failed for {item}: {ex.Message}");
                    }
                    finally
                    {
                        _speaking = false;
                        Volatile.Write(ref _lastActivityUtcTicks, DateTime.UtcNow.Ticks);
                    }
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // daemon shutting down
        }
        finally
        {
            CancelPending();
        }
    }

    internal void CancelPending()
    {
        while (_channel.Reader.TryRead(out var pending))
        {
            pending.Cancel();
            pending.Dispose();
        }
    }

    private async Task SpeakAsync(SpeechItem item, string text, JTalkConfig cfg, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var engine in FallbackChain(cfg.Engine))
        {
            try
            {
                var voice = VoiceFor(engine.Name, item.Source, cfg);
                if (engine is IStreamingSpeechEngine streaming)
                {
                    // Playback starts on the first audio chunks; PlayStreamingAsync only
                    // throws while nothing has been spoken yet, so falling back is safe.
                    await using var speech = await streaming.SynthesizeStreamingAsync(text, voice, cfg.Rate, ct);
                    await AudioPlayer.PlayStreamingAsync(speech, cfg.Volume / 100f, ct);
                    return;
                }
                var wav = await engine.SynthesizeWavAsync(text, voice, cfg.Rate, ct);
                await AudioPlayer.PlayAsync(wav, cfg.Volume / 100f, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"engine '{engine.Name}' failed ({ex.GetType().Name}: {ex.Message}); trying next in chain");
            }
        }
        if (last is not null) throw last;
    }

    private static string? VoiceFor(string engineName, string? source, JTalkConfig cfg)
    {
        if (source is null) return null; // manual `say`: engine default voice
        var tool = cfg.ToolFor(source);
        return engineName switch
        {
            "piper" => tool.PiperVoice,
            "openai" => tool.OpenAIVoice,
            _ => tool.WindowsVoice,
        };
    }

    private IEnumerable<ISpeechEngine> FallbackChain(string primary)
    {
        switch (primary)
        {
            case "openai":
                if (GetEngine("openai") is { } openai) yield return openai;
                if (GetEngine("piper") is { } piperAfterOpenAI) yield return piperAfterOpenAI;
                break;
            case "piper":
                if (GetEngine("piper") is { } piper) yield return piper;
                break;
            default:
                if (primary is not "windows")
                    Log.WarnOnce($"unknown engine '{primary}'; using windows");
                break;
        }
        yield return GetEngine("windows")!; // always-works terminus of the chain
    }

    private ISpeechEngine? GetEngine(string name) => name switch
    {
        "windows" => _windows ??= new WindowsSpeechEngine(),
        "piper" => _piper ??= SpeechEngineFactory.TryCreatePiper(_config),
        "openai" => _openai ??= SpeechEngineFactory.TryCreateOpenAI(_config),
        _ => null, // FallbackChain only passes known names
    };
}
