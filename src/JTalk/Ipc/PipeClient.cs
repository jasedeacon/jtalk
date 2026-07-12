using System.IO.Pipes;
using System.Text.Json;

namespace JTalk.Ipc;

public static class PipeClient
{
    public const string PipeName = "jtalk";

    /// <summary>
    /// Sends one request and reads one response. Returns null when the daemon
    /// is not reachable (no server, connect timeout, or I/O failure).
    /// </summary>
    public static PipeResponse? TrySend(PipeRequest request, int connectTimeoutMs = 300, int ioTimeoutMs = 3000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(connectTimeoutMs);

            using var io = new CancellationTokenSource(ioTimeoutMs);
            using var writer = new StreamWriter(pipe, Encodings.Utf8NoBom, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            writer.WriteLine(JsonSerializer.Serialize(request, PipeJsonContext.Default.PipeRequest));

            using var reader = new StreamReader(pipe, Encodings.Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var line = reader.ReadLineAsync(io.Token).AsTask().GetAwaiter().GetResult();
            return line is null ? null : JsonSerializer.Deserialize(line, PipeJsonContext.Default.PipeResponse);
        }
        catch
        {
            return null;
        }
    }

    public static bool Ping(int connectTimeoutMs = 300) =>
        TrySend(new PipeRequest { Type = "status" }, connectTimeoutMs)?.Ok == true;
}
