using System.Text.Json;
using JTalk.Cli;
using Xunit;

namespace JTalk.Tests;

public sealed class HookCommandTests
{
    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void MapClaudeStopBecomesTurnEvent()
    {
        using var doc = Parse(
            """{"hook_event_name":"Stop","last_assistant_message":"hello","session_id":"s1","cwd":"C:\\proj"}""");

        var request = HookCommand.MapClaude(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal("event", request.Type);
        Assert.Equal("claude", request.Source);
        Assert.Equal("turn", request.Kind);
        Assert.Equal("hello", request.Text);
        Assert.Equal("s1", request.SessionId);
        Assert.Equal(@"C:\proj", request.Cwd);
    }

    [Fact]
    public void MapClaudeNotificationBecomesAttentionEvent()
    {
        using var doc = Parse("""{"hook_event_name":"Notification","message":"needs input"}""");

        var request = HookCommand.MapClaude(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal("attention", request.Kind);
        Assert.Equal("needs input", request.Text);
    }

    [Fact]
    public void MapClaudeSessionEndBecomesSessionEndEvent()
    {
        using var doc = Parse("""{"hook_event_name":"SessionEnd","reason":"clear"}""");

        var request = HookCommand.MapClaude(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal("session-end", request.Kind);
        Assert.Equal("clear", request.Text);
    }

    [Fact]
    public void MapClaudeIgnoresUnknownEvents()
    {
        using var doc = Parse("""{"hook_event_name":"PreToolUse"}""");

        Assert.Null(HookCommand.MapClaude(doc.RootElement));
    }

    [Fact]
    public void MapClaudeIgnoresNonObjectPayloads()
    {
        using var doc = Parse("[1, 2, 3]");

        Assert.Null(HookCommand.MapClaude(doc.RootElement));
    }

    [Fact]
    public void MapCodexStopBecomesTurnEvent()
    {
        using var doc = Parse("""{"hook_event_name":"Stop","last_assistant_message":"done"}""");

        var request = HookCommand.MapCodex(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal("codex", request.Source);
        Assert.Equal("turn", request.Kind);
        Assert.Equal("done", request.Text);
    }

    [Fact]
    public void MapCodexPermissionRequestNamesTheTool()
    {
        using var doc = Parse("""{"hook_event_name":"PermissionRequest","tool_name":"shell"}""");

        var request = HookCommand.MapCodex(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal("attention", request.Kind);
        Assert.Equal("approval requested for shell", request.Text);
    }

    [Fact]
    public void MapCodexPermissionRequestFallsBackToGenericText()
    {
        using var doc = Parse("""{"hook_event_name":"PermissionRequest"}""");

        var request = HookCommand.MapCodex(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal("approval requested", request.Text);
    }

    [Fact]
    public void MapCodexNotifyMapsKebabCasePayload()
    {
        using var doc = Parse(
            """{"type":"agent-turn-complete","last-assistant-message":"done","thread-id":"t1","cwd":"C:\\x"}""");

        var request = HookCommand.MapCodexNotify(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal("turn", request.Kind);
        Assert.Equal("done", request.Text);
        Assert.Equal("t1", request.SessionId);
    }

    [Fact]
    public void MapCodexNotifyIgnoresOtherTypes()
    {
        using var doc = Parse("""{"type":"agent-turn-started"}""");

        Assert.Null(HookCommand.MapCodexNotify(doc.RootElement));
    }

    [Fact]
    public void MapClaudeTruncatesOversizedText()
    {
        var big = new string('x', 70_000);
        using var doc = Parse($$"""{"hook_event_name":"Stop","last_assistant_message":"{{big}}"}""");

        var request = HookCommand.MapClaude(doc.RootElement);

        Assert.NotNull(request);
        Assert.Equal(64_000, request.Text!.Length);
    }
}
