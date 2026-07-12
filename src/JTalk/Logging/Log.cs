namespace JTalk.Logging;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
}

/// <summary>
/// Minimal rolling file logger. Hook adapter mode must never write to stdout/stderr,
/// so all diagnostics go here: %APPDATA%\jtalk\logs\{name}.log, rolled at 1 MB.
/// </summary>
public static class Log
{
    private static readonly Lock Sync = new();
    private static readonly HashSet<string> OnceKeys = [];
    private const long RollBytes = 1024 * 1024;

    private static string? _path;

    public static LogLevel Level { get; set; } = LogLevel.Info;

    public static string LogsDir { get; } = Path.Combine(AppPaths.Root, "logs");

    public static void Init(string name)
    {
        try
        {
            Directory.CreateDirectory(LogsDir);
            _path = Path.Combine(LogsDir, name + ".log");
        }
        catch
        {
            _path = null; // logging is best-effort, never fatal
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    /// <summary>Logs a warning only the first time this exact message is seen in this process.</summary>
    public static void WarnOnce(string message)
    {
        lock (Sync)
        {
            if (!OnceKeys.Add(message)) return;
        }
        Warn(message);
    }

    private static void Write(LogLevel level, string message)
    {
        if (level < Level || _path is null) return;
        var line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToLowerInvariant()}] {message}{Environment.NewLine}";
        lock (Sync)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > RollBytes)
                {
                    var old = _path + ".old";
                    File.Delete(old);
                    File.Move(_path, old);
                }
                File.AppendAllText(_path, line);
            }
            catch
            {
                // never let logging take down the process
            }
        }
    }
}
