using JTalk.Config;
using JTalk.Daemon;
using JTalk.Ipc;
using Xunit;

namespace JTalk.Tests;

public sealed class EventRouterTests
{
    private static PipeRequest ClaudeRequest(string? cwd = null) =>
        new() { Type = "event", Source = "claude", Kind = "turn", Cwd = cwd };

    [Fact]
    public void BuildPrefixUsesToolPrefix() =>
        Assert.Equal("Claude", EventRouter.BuildPrefix(new JTalkConfig(), ClaudeRequest()));

    [Fact]
    public void BuildPrefixIsEmptyWhenDisabled()
    {
        var cfg = new JTalkConfig { PrefixEnabled = false };

        Assert.Equal("", EventRouter.BuildPrefix(cfg, ClaudeRequest()));
    }

    [Fact]
    public void BuildPrefixIsEmptyWithoutSource()
    {
        var request = new PipeRequest { Type = "event", Kind = "turn" };

        Assert.Equal("", EventRouter.BuildPrefix(new JTalkConfig(), request));
    }

    [Fact]
    public void BuildPrefixIsEmptyForUnknownSource()
    {
        var request = new PipeRequest { Type = "event", Source = "mystery", Kind = "turn" };

        Assert.Equal("", EventRouter.BuildPrefix(new JTalkConfig(), request));
    }

    [Fact]
    public void BuildPrefixAddsProjectLeafWhenEnabled()
    {
        var cfg = new JTalkConfig { SpeakProject = true };

        Assert.Equal("Claude, in myproj", EventRouter.BuildPrefix(cfg, ClaudeRequest(@"F:\Dev\myproj\")));
    }

    [Fact]
    public void BuildPrefixSkipsProjectWhenCwdMissing()
    {
        var cfg = new JTalkConfig { SpeakProject = true };

        Assert.Equal("Claude", EventRouter.BuildPrefix(cfg, ClaudeRequest()));
    }

    [Theory]
    [InlineData("", "body", "body")]
    [InlineData("Claude", "body", "Claude: body")]
    [InlineData("Claude", "  body  ", "Claude: body")]
    public void ComposeJoinsPrefixAndTrimmedBody(string prefix, string body, string expected) =>
        Assert.Equal(expected, EventRouter.Compose(prefix, body));
}
