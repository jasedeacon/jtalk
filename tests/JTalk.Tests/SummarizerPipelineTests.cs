using JTalk.Config;
using JTalk.Summarize;
using Xunit;

namespace JTalk.Tests;

public sealed class SummarizerPipelineTests
{
    private sealed class ConcurrencyProbeSummarizer : ISummarizer
    {
        private int _active;
        private int _maxActive;
        public int MaxActive => Volatile.Read(ref _maxActive);
        public string Name => "probe";

        public async Task<string> SummarizeAsync(string text, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            int observed;
            do
            {
                observed = Volatile.Read(ref _maxActive);
            }
            while (active > observed && Interlocked.CompareExchange(ref _maxActive, active, observed) != observed);
            try
            {
                await Task.Delay(50, cancellationToken);
                return text;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    [Fact]
    public void PrepareInputPassesShortTextThrough() =>
        Assert.Equal("short text", SummarizerPipeline.PrepareInput("short text", 100));

    [Fact]
    public void PrepareInputKeepsHeadAndTailOfLongText()
    {
        var text = new string('a', 100) + new string('b', 100);

        var result = SummarizerPipeline.PrepareInput(text, 100);

        // 75% head + separator + 25% tail
        Assert.Equal(new string('a', 75) + " … " + new string('b', 25), result);
    }

    [Fact]
    public void PrepareInputCleansMarkdownBeforeTruncating() =>
        Assert.Equal("done", SummarizerPipeline.PrepareInput("**done**", 100));

    [Fact]
    public async Task RemoteSummariesAreLimitedToTwoConcurrentRequests()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"jtalk-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "config.json");
        Directory.CreateDirectory(dir);
        try
        {
            ConfigService.MutateFile(path, c => c with
            {
                Summarizer = c.Summarizer with { Backend = "auto", TimeoutMs = 2000 },
            });
            using var config = new ConfigService(path);
            var probe = new ConcurrencyProbeSummarizer();
            using var pipeline = new SummarizerPipeline(config, _ => probe);

            await Task.WhenAll(Enumerable.Range(0, 6)
                .Select(i => pipeline.SummarizeAsync($"message {i}", CancellationToken.None)));

            Assert.Equal(2, probe.MaxActive);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
