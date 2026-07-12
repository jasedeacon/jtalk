using System.Text;
using System.Text.Json;
using JTalk.Ipc;
using JTalk.Logging;

namespace JTalk.Cli;

/// <summary>
/// Adapter mode invoked by CLI hooks. Contract: read the payload (stdin for Claude Code
/// and Codex hooks, argv for legacy Codex notify), forward to the daemon, and get out of
/// the way — always exit 0, never write to stdout, auto-start the daemon when needed.
/// This path must not touch WinForms/WinRT/NAudio types so cold start stays fast.
/// </summary>
public static class HookCommand
{
    private const int MaxTextChars = 64_000;

    public static int Run(string source, string? argvPayload)
    {
        if (Environment.GetEnvironmentVariable("JTALK_MUTE") == "1") return 0;
        Log.Init("hook");
        try
        {
            var json = argvPayload ?? ReadStdin(timeoutMs: 2000);
            if (string.IsNullOrWhiteSpace(json)) return 0;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var request = source.ToLowerInvariant() switch
            {
                "claude" => MapClaude(root),
                "codex" => MapCodex(root),
                "codex-notify" => MapCodexNotify(root),
                _ => null,
            };
            if (request is null) return 0;

            Log.Info($"{source}: {request.Kind} ({request.Text?.Length ?? 0} chars)");
            if (!DaemonLauncher.EnsureDaemon()) return 0;
            PipeClient.TrySend(request, connectTimeoutMs: 500);
        }
        catch (Exception ex)
        {
            Log.Error($"hook {source}: {ex.GetType().Name}: {ex.Message}");
        }
        return 0;
    }

    internal static PipeRequest? MapClaude(JsonElement root)
    {
        var (kind, text) = GetString(root, "hook_event_name") switch
        {
            "Stop" => ("turn", GetString(root, "last_assistant_message")),
            "Notification" => ("attention", GetString(root, "message")),
            "SessionEnd" => ("session-end", GetString(root, "reason")),
            _ => ("", null),
        };
        return kind.Length == 0 ? null : Build("claude", kind, text, GetString(root, "session_id"), GetString(root, "cwd"));
    }

    internal static PipeRequest? MapCodex(JsonElement root)
    {
        switch (GetString(root, "hook_event_name"))
        {
            case "Stop":
                return Build("codex", "turn", GetString(root, "last_assistant_message"),
                    GetString(root, "session_id"), GetString(root, "cwd"));
            case "PermissionRequest":
                var subject = GetString(root, "tool_name") ?? GetString(root, "title") ?? GetString(root, "message");
                var text = subject is null ? "approval requested" : $"approval requested for {subject}";
                return Build("codex", "attention", text, GetString(root, "session_id"), GetString(root, "cwd"));
            default:
                return null;
        }
    }

    /// <summary>Legacy Codex `notify` fallback: kebab-case payload passed as one argv argument.</summary>
    internal static PipeRequest? MapCodexNotify(JsonElement root)
    {
        if (GetString(root, "type") != "agent-turn-complete") return null;
        return Build("codex", "turn", GetString(root, "last-assistant-message"),
            GetString(root, "thread-id"), GetString(root, "cwd"));
    }

    private static PipeRequest Build(string source, string kind, string? text, string? sessionId, string? cwd) => new()
    {
        Type = "event",
        Source = source,
        Kind = kind,
        Text = text is { Length: > MaxTextChars } ? text[..MaxTextChars] : text,
        SessionId = sessionId,
        Cwd = cwd,
    };

    private static string? GetString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var prop)
        && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static string ReadStdin(int timeoutMs)
    {
        // Raw UTF-8 bytes, never Console.In: the console codepage would mangle non-ASCII payloads.
        using var stdin = Console.OpenStandardInput();
        using var ms = new MemoryStream();
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            // WaitAsync, not just the token: a blocking console read is not interruptible,
            // so on timeout we abandon the copy (background thread) and parse what arrived
            // instead of hanging until the host closes stdin.
            stdin.CopyToAsync(ms, cts.Token)
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs))
                .GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // slow writer; try to parse what arrived
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
