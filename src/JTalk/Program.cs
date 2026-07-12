using JTalk.Cli;
using JTalk.Daemon;
using JTalk.Logging;

namespace JTalk;

internal static class Program
{
    public static string Version { get; } =
        typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            // Daemon and hook modes own their log names; every other verb logs as "cli".
            if (args is not (["daemon"] or ["hook", ..]))
                Log.Init("cli");

            return args switch
            {
                ["daemon"] => DaemonHost.Run(),
                ["hook", var source] => HookCommand.Run(source, null),
                ["hook", var source, var payload] => HookCommand.Run(source, payload),
                ["say", .. var rest] when rest.Length > 0 => ClientCommands.Say(string.Join(' ', rest)),
                ["status"] => ClientCommands.Status(),
                ["quit"] => ClientCommands.Quit(),
                ["voices"] or ["voice", "list"] => ClientCommands.Voices(),
                ["mute"] => ClientCommands.SetMuted(true),
                ["unmute"] => ClientCommands.SetMuted(false),
                ["volume", var volume] => ClientCommands.SetVolume(volume),
                ["engine", var engine] => ClientCommands.SetEngine(engine),
                ["summarizer", var backend] => ClientCommands.SetSummarizer(backend),
                ["voice", var tool, .. var name] when name.Length > 0 =>
                    ClientCommands.SetVoice(tool, string.Join(' ', name)),
                ["version"] or ["--version"] or ["-v"] => ClientCommands.Version(),
                _ => ClientCommands.Help(),
            };
        }
        catch (Exception ex)
        {
            Log.Error($"fatal: {ex}");
            // Hook adapter mode must never disturb the calling CLI.
            if (args is ["hook", ..]) return 0;
            Console.Error.WriteLine($"jtalk: {ex.Message}");
            return 1;
        }
    }
}
