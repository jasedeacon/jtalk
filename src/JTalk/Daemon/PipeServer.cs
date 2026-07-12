using System.IO.Pipes;
using System.Text.Json;
using JTalk.Config;
using JTalk.Ipc;
using JTalk.Logging;
using JTalk.Speech;

namespace JTalk.Daemon;

/// <summary>
/// Accept loop for \\.\pipe\jtalk. Started before engine/tray init so hook adapters
/// racing the daemon's startup can connect immediately; their events simply queue.
/// </summary>
public sealed class PipeServer
{
    private readonly ConfigService _config;
    private readonly SpeechQueue _queue;
    private readonly EventRouter _router;
    private readonly Action _shutdown;
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    public PipeServer(ConfigService config, SpeechQueue queue, EventRouter router, Action shutdown)
    {
        _config = config;
        _queue = queue;
        _router = router;
        _shutdown = shutdown;
    }

    public void Start(CancellationToken ct) => _ = Task.Run(() => AcceptLoopAsync(ct), CancellationToken.None);

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    PipeClient.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }
            catch (IOException ex)
            {
                // Another process owns the pipe name (should not happen behind the mutex).
                Log.Error($"cannot create pipe: {ex.Message}");
                return;
            }

            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }
            catch (Exception ex)
            {
                Log.Warn($"pipe accept failed: {ex.Message}");
                await server.DisposeAsync();
                continue;
            }

            _ = Task.Run(() => HandleAsync(server, ct), CancellationToken.None);
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe)
        {
            try
            {
                using var io = CancellationTokenSource.CreateLinkedTokenSource(ct);
                io.CancelAfter(5000); // a wedged client must not pin a handler task

                using var reader = new StreamReader(
                    pipe, Encodings.Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var line = await reader.ReadLineAsync(io.Token);
                if (line is null) return;

                var request = JsonSerializer.Deserialize(line, PipeJsonContext.Default.PipeRequest);
                var response = request is null
                    ? new PipeResponse { Ok = false, Error = "bad request" }
                    : Dispatch(request);

                using var writer = new StreamWriter(pipe, Encodings.Utf8NoBom, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
                var json = JsonSerializer.Serialize(response, PipeJsonContext.Default.PipeResponse);
                await writer.WriteLineAsync(json.AsMemory(), io.Token);
                // Deliberately synchronous: there is no async drain API, and closing the pipe
                // before the client has read the response would truncate it. Responses are one
                // short line, so the block is brief.
                pipe.WaitForPipeDrain();

                if (request?.Type == "quit")
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100, CancellationToken.None);
                        _shutdown();
                    }, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // shutdown or slow client; drop the connection
            }
            catch (Exception ex)
            {
                Log.Warn($"pipe handler error: {ex.Message}");
            }
        }
    }

    private PipeResponse Dispatch(PipeRequest req)
    {
        // Client and daemon ship in one exe, so a mismatch only happens across an upgrade
        // window (new CLI, old resident daemon or vice versa); refuse rather than misparse.
        if (req.Version != 1)
            return new PipeResponse { Ok = false, Error = $"unsupported protocol version {req.Version}" };

        switch (req.Type)
        {
            case "event":
                var accepted = _router.TryEnqueue(req);
                Log.Info($"event {req.Source}/{req.Kind} {(accepted ? "queued" : "ignored")}");
                return new PipeResponse { Ok = true };

            case "say":
                if (string.IsNullOrWhiteSpace(req.Text))
                    return new PipeResponse { Ok = false, Error = "nothing to say" };
                _queue.Enqueue(new SpeechItem(null, "say", Task.FromResult(req.Text)));
                return new PipeResponse { Ok = true };

            case "status":
                var cfg = _config.Current;
                return new PipeResponse
                {
                    Ok = true,
                    Status = new DaemonStatus
                    {
                        Version = Program.Version,
                        Muted = cfg.Muted,
                        Engine = cfg.Engine,
                        Summarizer = cfg.Summarizer.Backend,
                        Queue = _queue.PendingCount,
                        Speaking = _queue.Speaking,
                        UptimeSeconds = (long)(DateTime.UtcNow - _startedUtc).TotalSeconds,
                    },
                };

            case "voices":
                return new PipeResponse { Ok = true, Voices = SpeechEngineFactory.CatalogVoices(_config.Current) };

            case "quit":
                Log.Info("quit requested via pipe");
                return new PipeResponse { Ok = true };

            default:
                return new PipeResponse { Ok = false, Error = $"unknown request type '{req.Type}'" };
        }
    }
}
