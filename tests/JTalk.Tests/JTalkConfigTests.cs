using System.Text.Json;
using JTalk.Config;
using Xunit;

namespace JTalk.Tests;

public sealed class JTalkConfigTests
{
    [Fact]
    public void ToolForIsCaseInsensitiveOnDefaults() =>
        Assert.Equal("Claude", new JTalkConfig().ToolFor("CLAUDE").Prefix);

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public void ToolForReturnsEmptyDefaultsForUnknownSource(string? source) =>
        Assert.Equal("", new JTalkConfig().ToolFor(source).Prefix);

    [Fact]
    public void NormalizedLowercasesAndTrimsEngine()
    {
        var cfg = new JTalkConfig { Engine = "  PIPER " };

        Assert.Equal("piper", cfg.Normalized().Engine);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    [InlineData(80, 80)]
    public void NormalizedClampsVolume(int volume, int expected) =>
        Assert.Equal(expected, new JTalkConfig { Volume = volume }.Normalized().Volume);

    [Fact]
    public void NormalizedClampsNumericRanges()
    {
        var cfg = new JTalkConfig
        {
            Rate = 10.0,
            MaxQueue = 0,
            Summarizer = new SummarizerConfig { TimeoutMs = 50, MaxInputChars = 5 },
        };

        var normalized = cfg.Normalized();

        Assert.Equal(6.0, normalized.Rate);
        Assert.Equal(1, normalized.MaxQueue);
        Assert.Equal(500, normalized.Summarizer.TimeoutMs);
        Assert.Equal(200, normalized.Summarizer.MaxInputChars);
    }

    [Fact]
    public void NormalizedRestoresIgnoreCaseToolLookupAfterDeserialization()
    {
        // Deserialization replaces the default dictionary with a case-sensitive one;
        // Normalized() must bring the ignore-case comparer back.
        var json = """{"tools":{"claude":{"prefix":"C"}}}""";
        var cfg = JsonSerializer.Deserialize(json, JTalkJsonContext.Default.JTalkConfig)!;

        Assert.Equal("C", cfg.Normalized().ToolFor("Claude").Prefix);
    }

    [Fact]
    public void PartialConfigKeepsDeclaredDefaults()
    {
        // Regression: init-only records deserialized through the source generator
        // used to lose every default for keys absent from the JSON (volume -> 0).
        var json = """{"engine":"piper"}""";
        var cfg = JsonSerializer.Deserialize(json, JTalkJsonContext.Default.JTalkConfig)!;

        Assert.Equal("piper", cfg.Engine);
        Assert.Equal(80, cfg.Volume);
        Assert.Equal(1.0, cfg.Rate);
        Assert.True(cfg.PrefixEnabled);
        Assert.Equal(20, cfg.MaxQueue);
        Assert.Equal(5000, cfg.Summarizer.TimeoutMs);
        Assert.True(cfg.Events.TurnComplete);
        Assert.Equal("Claude", cfg.ToolFor("claude").Prefix);
        Assert.Equal("off", cfg.Summarizer.Backend);
    }

    [Fact]
    public void ConfigJsonKeysAreStable()
    {
        // Serialized names are user-facing (config.json) and must never drift,
        // even though the C# properties were renamed to OpenAI casing.
        var json = JsonSerializer.Serialize(new JTalkConfig(), JTalkJsonContext.Default.JTalkConfig);

        Assert.Contains("\"openaiTts\"", json, StringComparison.Ordinal);
        Assert.Contains("\"openaiModel\"", json, StringComparison.Ordinal);
        Assert.Contains("\"openaiVoice\"", json, StringComparison.Ordinal);
    }
}
