using System.Text.Json;
using JTalk.Ipc;
using Xunit;

namespace JTalk.Tests;

public sealed class PipeJsonTests
{
    [Fact]
    public void RequestUsesPinnedVersionKeyAndOmitsNulls()
    {
        var json = JsonSerializer.Serialize(
            new PipeRequest { Type = "status" }, PipeJsonContext.Default.PipeRequest);

        Assert.Contains("\"v\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"status\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", json, StringComparison.Ordinal); // newline-delimited protocol
    }

    [Fact]
    public void StatusRoundTripsWithPinnedUptimeKey()
    {
        var response = new PipeResponse
        {
            Ok = true,
            Status = new DaemonStatus { Version = "1.2.3", Engine = "windows", UptimeSeconds = 42 },
        };

        var json = JsonSerializer.Serialize(response, PipeJsonContext.Default.PipeResponse);
        Assert.Contains("\"uptimeSec\":42", json, StringComparison.Ordinal);

        var parsed = JsonSerializer.Deserialize(json, PipeJsonContext.Default.PipeResponse);
        Assert.Equal(42, parsed!.Status!.UptimeSeconds);
    }

    [Fact]
    public void RequestRoundTripsAllFields()
    {
        var request = new PipeRequest
        {
            Type = "event",
            Source = "claude",
            Kind = "turn",
            Text = "héllo wörld", // non-ASCII must survive the pipe encoding
            SessionId = "s1",
            Cwd = @"C:\proj",
        };

        var json = JsonSerializer.Serialize(request, PipeJsonContext.Default.PipeRequest);
        var parsed = JsonSerializer.Deserialize(json, PipeJsonContext.Default.PipeRequest);

        Assert.Equal(request, parsed);
    }
}
