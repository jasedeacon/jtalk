using JTalk.Config;
using JTalk.Daemon;
using Xunit;

namespace JTalk.Tests;

public sealed class SpeechQueueTests
{
    [Fact]
    public async Task QueueOverflowCancelsDroppedOldestItem()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"jtalk-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "config.json");
        Directory.CreateDirectory(dir);
        try
        {
            ConfigService.MutateFile(path, c => c with { MaxQueue = 1 });
            using var config = new ConfigService(path);
            var queue = new SpeechQueue(config);
            using var firstCts = new CancellationTokenSource();
            using var secondCts = new CancellationTokenSource();
            var firstTask = WaitForeverAsync(firstCts.Token);
            var secondTask = Task.FromResult("second");

            queue.Enqueue(new SpeechItem("codex", "turn", firstTask, firstCts));
            queue.Enqueue(new SpeechItem("claude", "turn", secondTask, secondCts));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstTask);
            Assert.False(secondCts.IsCancellationRequested);
            queue.CancelPending();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<string> WaitForeverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return "unreachable";
    }
}
