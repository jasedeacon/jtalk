using System.Diagnostics;
using JTalk.Ipc;
using JTalk.Logging;

namespace JTalk.Cli;

public static class DaemonLauncher
{
    /// <summary>
    /// Ensures a daemon is reachable, spawning one detached if needed. Safe under races:
    /// simultaneous callers may both spawn; the daemon's named mutex picks a winner and
    /// the loser exits instantly, so the retry loop converges on the survivor.
    /// </summary>
    public static bool EnsureDaemon(int spawnWaitMs = 3000)
    {
        if (PipeClient.Ping(300)) return true;

        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            Log.Error("cannot determine own exe path to spawn daemon");
            return false;
        }

        try
        {
            // ShellExecuteEx launch: the daemon must inherit NO handles. With
            // UseShellExecute=false the daemon ends up holding the calling CLI's
            // stdout pipe (directly, or leaked via bInheritHandles when redirecting),
            // and Claude Code/Codex would then wait on the hook's stdout until the
            // daemon — not the hook — exits. Verified: that stalls callers for the
            // daemon's whole lifetime.
            Process.Start(new ProcessStartInfo(exe, "daemon")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"failed to spawn daemon: {ex.Message}");
            return false;
        }

        var deadline = Environment.TickCount64 + spawnWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (PipeClient.Ping(100)) return true;
            Thread.Sleep(100);
        }
        Log.Error("daemon did not become reachable after spawn");
        return false;
    }
}
