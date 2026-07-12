using JTalk.Config;
using JTalk.Ipc;

namespace JTalk.Cli;

public static class ClientCommands
{
    public static int Say(string text)
    {
        if (!DaemonLauncher.EnsureDaemon()) return Fail("could not start the daemon");
        var resp = PipeClient.TrySend(new PipeRequest { Type = "say", Text = text }, connectTimeoutMs: 1000);
        return resp?.Ok == true ? 0 : Fail(resp?.Error ?? "daemon did not respond");
    }

    public static int Status()
    {
        if (PipeClient.TrySend(new PipeRequest { Type = "status" })?.Status is not { } s)
        {
            Console.WriteLine("daemon not running");
            return 0;
        }
        Console.WriteLine($"jtalk daemon v{s.Version}");
        Console.WriteLine($"  engine:  {s.Engine}");
        Console.WriteLine($"  summary: {s.Summarizer}");
        Console.WriteLine($"  muted:   {(s.Muted ? "yes" : "no")}");
        Console.WriteLine($"  queue:   {s.Queue}{(s.Speaking ? " (speaking)" : "")}");
        Console.WriteLine($"  uptime:  {TimeSpan.FromSeconds(s.UptimeSeconds):hh\\:mm\\:ss}");
        return 0;
    }

    public static int Quit()
    {
        var resp = PipeClient.TrySend(new PipeRequest { Type = "quit" });
        Console.WriteLine(resp?.Ok == true ? "daemon stopping" : "daemon not running");
        return 0;
    }

    public static int Voices()
    {
        if (!DaemonLauncher.EnsureDaemon()) return Fail("could not start the daemon");
        if (PipeClient.TrySend(new PipeRequest { Type = "voices" }, connectTimeoutMs: 1000)?.Voices is not { } voices)
            return Fail("daemon did not respond");
        foreach (var group in voices.GroupBy(v => v.Engine))
        {
            Console.WriteLine($"{group.Key}:");
            foreach (var v in group) Console.WriteLine($"  {v.Name}");
        }
        return 0;
    }

    public static int SetMuted(bool muted)
    {
        ConfigService.MutateFile(c => c with { Muted = muted });
        Console.WriteLine(muted ? "jtalk muted" : "jtalk unmuted");
        return 0;
    }

    public static int SetVolume(string arg)
    {
        if (!int.TryParse(arg, out var volume) || volume is < 0 or > 100)
            return Fail("volume must be a number from 0 to 100");
        ConfigService.MutateFile(c => c with { Volume = volume });
        Console.WriteLine($"volume set to {volume}%");
        return 0;
    }

    public static int SetEngine(string engine)
    {
        engine = engine.ToLowerInvariant();
        if (engine is not ("windows" or "piper" or "openai"))
            return Fail("engine must be 'windows', 'piper', or 'openai'");
        ConfigService.MutateFile(c => c with { Engine = engine });
        Console.WriteLine($"engine set to {engine}");
        return 0;
    }

    public static int SetSummarizer(string backend)
    {
        backend = backend.ToLowerInvariant();
        if (backend is not ("off" or "auto" or "anthropic" or "openai"))
            return Fail("summarizer must be 'off', 'auto', 'anthropic', or 'openai'");

        var cfg = ConfigService.MutateFile(c => c with
        {
            Summarizer = c.Summarizer with { Backend = backend },
        });
        if (backend == "off")
        {
            Console.WriteLine("cloud summaries disabled; using the offline fallback");
            return 0;
        }

        var hasKey = backend switch
        {
            "anthropic" => ApiKeys.Resolve(
                cfg.Summarizer.AnthropicApiKey,
                ApiKeys.Env(cfg.Summarizer.AnthropicApiKeyEnvVar),
                ApiKeys.Env("ANTHROPIC_API_KEY")) is not null,
            "openai" => ApiKeys.Resolve(
                cfg.Summarizer.OpenAIApiKey,
                ApiKeys.Env(cfg.Summarizer.OpenAIApiKeyEnvVar),
                ApiKeys.Env("OPENAI_API_KEY")) is not null,
            _ => ApiKeys.Resolve(
                cfg.Summarizer.AnthropicApiKey,
                ApiKeys.Env(cfg.Summarizer.AnthropicApiKeyEnvVar),
                ApiKeys.Env("ANTHROPIC_API_KEY"),
                cfg.Summarizer.OpenAIApiKey,
                ApiKeys.Env(cfg.Summarizer.OpenAIApiKeyEnvVar),
                ApiKeys.Env("OPENAI_API_KEY")) is not null,
        };
        Console.WriteLine($"summarizer set to '{backend}' ({(hasKey ? "API key found" : "no API key found; offline fallback remains active")})");
        return 0;
    }

    public static int SetVoice(string tool, string name)
    {
        tool = tool.ToLowerInvariant();
        if (tool is not ("claude" or "codex"))
            return Fail("tool must be 'claude' or 'codex'");
        var cfg = ConfigService.MutateFile(c =>
        {
            var tools = new Dictionary<string, ToolConfig>(c.Tools, StringComparer.OrdinalIgnoreCase);
            var toolConfig = c.ToolFor(tool);
            tools[tool] = c.Engine switch
            {
                "piper" => toolConfig with { PiperVoice = name },
                "openai" => toolConfig with { OpenAIVoice = name },
                _ => toolConfig with { WindowsVoice = name },
            };
            return c with { Tools = tools };
        });
        Console.WriteLine($"{tool} voice for engine '{cfg.Engine}' set to '{name}'");
        return 0;
    }

    public static int Version()
    {
        Console.WriteLine(Program.Version);
        return 0;
    }

    public static int Help()
    {
        Console.WriteLine(
            """
            jtalk — spoken notifications for Claude Code and Codex CLI

            usage:
              jtalk say <text...>          speak text (starts the daemon if needed)
              jtalk status                 show daemon status
              jtalk quit                   stop the daemon
              jtalk mute | unmute          toggle speech (persists in config)
              jtalk volume <0-100>         set volume
              jtalk engine <name>          set TTS engine: windows | piper | openai
              jtalk summarizer <backend>   off | auto | anthropic | openai
              jtalk voice list             list available voices per engine
              jtalk voice <tool> <name>    set voice for 'claude' or 'codex' (current engine)
              jtalk daemon                 run the daemon in the foreground
              jtalk hook <source>          hook adapter mode (called by CLI hooks, reads stdin)
              jtalk version                print version

            config: %APPDATA%\jtalk\config.json (hot-reloaded)
            logs:   %APPDATA%\jtalk\logs\
            env:    JTALK_MUTE=1 makes hook adapters no-op (for scripted runs)
            """);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"jtalk: {message}");
        return 1;
    }
}
