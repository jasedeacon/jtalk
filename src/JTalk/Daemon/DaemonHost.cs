using JTalk.Config;
using JTalk.Logging;

namespace JTalk.Daemon;

public static class DaemonHost
{
    private static volatile bool _shutdownRequested;

    public static int Run()
    {
        // Single instance: racing hook adapters may spawn several daemons; the mutex
        // picks a winner and losers exit 0 immediately.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\jtalk-daemon", out var createdNew);
        if (!createdNew) return 0;

        Log.Init("daemon");
        Log.Info($"daemon starting v{Program.Version}");

        using var config = new ConfigService();
        using var cts = new CancellationTokenSource();

        var queue = new SpeechQueue(config);
        using var router = new EventRouter(config, queue);

        TrayAppContext? tray = null;

        // Pipe first: hook adapters must be able to connect while the rest initializes.
        var server = new PipeServer(config, queue, router, Shutdown);
        server.Start(cts.Token);
        queue.Start(cts.Token);
        config.StartWatching();
        StartIdleMonitor(config, queue, Shutdown, cts.Token);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        tray = new TrayAppContext(config, queue, Shutdown);
        if (!_shutdownRequested)
            Application.Run(tray);

        cts.Cancel();
        tray.Dispose();
        Log.Info("daemon stopped");
        return 0;

        void Shutdown()
        {
            _shutdownRequested = true;
            tray?.Shutdown();
        }
    }

    private static void StartIdleMonitor(ConfigService config, SpeechQueue queue, Action shutdown, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), ct);
                    var minutes = config.Current.IdleExitMinutes;
                    if (minutes <= 0 || queue.Speaking || queue.PendingCount > 0) continue;
                    if (DateTime.UtcNow - queue.LastActivityUtc > TimeSpan.FromMinutes(minutes))
                    {
                        Log.Info($"idle for {minutes} min; exiting");
                        shutdown();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // daemon shutting down; don't fault the fire-and-forget task
            }
        }, CancellationToken.None);
    }
}
