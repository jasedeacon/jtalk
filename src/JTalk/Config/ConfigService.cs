using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JTalk.Logging;

namespace JTalk.Config;

/// <summary>
/// Owns %APPDATA%\jtalk\config.json: load-or-create, save, and hot reload via
/// FileSystemWatcher (250 ms debounce, read retries for partial writes).
/// The tray and CLI both mutate config through this file; the daemon reacts via reload.
/// </summary>
public sealed class ConfigService : IDisposable
{
    public static string ConfigDir { get; } = AppPaths.Root;

    public static string ConfigPath { get; } = Path.Combine(ConfigDir, "config.json");

    private readonly Lock _sync = new();
    private readonly string _configDir;
    private readonly string _configPath;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;
    private volatile JTalkConfig _current;

    public event Action<JTalkConfig>? Changed;

    public JTalkConfig Current => _current;

    public ConfigService() : this(ConfigPath)
    {
    }

    internal ConfigService(string configPath)
    {
        _configPath = Path.GetFullPath(configPath);
        _configDir = Path.GetDirectoryName(_configPath)
            ?? throw new ArgumentException("config path must have a parent directory", nameof(configPath));
        _current = LoadOrCreate(_configPath);
        ApplyLogLevel(_current);
    }

    /// <summary>Loads config.json, creating it with defaults when missing. Static so the CLI can use it without watching.</summary>
    public static JTalkConfig LoadOrCreate() => LoadOrCreate(ConfigPath);

    internal static JTalkConfig LoadOrCreate(string configPath)
    {
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))
            ?? throw new ArgumentException("config path must have a parent directory", nameof(configPath));
        Directory.CreateDirectory(configDir);
        if (!File.Exists(configPath))
        {
            var defaults = new JTalkConfig();
            WriteFile(configPath, defaults);
            return defaults;
        }

        var loaded = TryRead(configPath);
        if (loaded is not null) return loaded;

        // Unreadable/corrupt user file: fall back to defaults in memory, never overwrite their file.
        Log.Error($"could not parse {configPath}; using defaults in memory (file left untouched)");
        return new JTalkConfig();
    }

    /// <summary>Applies a mutation, persists it, and updates the in-memory snapshot immediately.</summary>
    public JTalkConfig Update(Func<JTalkConfig, JTalkConfig> mutate)
    {
        JTalkConfig next;
        lock (_sync)
        {
            next = WithFileLock(_configPath, () =>
            {
                // Rebase on the latest valid disk state while holding the cross-process
                // mutex. If an editor is mid-write, retain the daemon's last good snapshot.
                var baseline = TryRead(_configPath) ?? _current;
                var mutated = mutate(baseline);
                WriteFile(_configPath, mutated);
                return mutated;
            });
            _current = next;
            ApplyLogLevel(next);
        }
        Changed?.Invoke(next);
        return next;
    }

    /// <summary>Static one-shot mutation for CLI verbs (mute/volume/voice) that run without a daemon.</summary>
    public static JTalkConfig MutateFile(Func<JTalkConfig, JTalkConfig> mutate) =>
        MutateFile(ConfigPath, mutate);

    internal static JTalkConfig MutateFile(string configPath, Func<JTalkConfig, JTalkConfig> mutate) =>
        WithFileLock(configPath, () =>
        {
            var next = mutate(LoadOrCreate(configPath));
            WriteFile(configPath, next);
            return next;
        });

    /// <summary>
    /// Cross-process lock around read-modify-write: the CLI and the daemon both mutate
    /// config.json, and without this a concurrent tray change and CLI verb lose one write.
    /// On timeout we proceed unlocked — a rare lost update beats a hung command.
    /// </summary>
    private static T WithFileLock<T>(string configPath, Func<T> action)
    {
        var canonical = Path.GetFullPath(configPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
        using var mutex = new Mutex(initiallyOwned: false, $@"Local\jtalk-config-{hash}");
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                owned = true; // previous holder died mid-write; the lock is now ours
            }
            return action();
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }

    public void StartWatching()
    {
        _watcher = new FileSystemWatcher(_configDir, Path.GetFileName(_configPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += (_, _) => Debounce();
        _watcher.Created += (_, _) => Debounce();
        _watcher.Renamed += (_, _) => Debounce();
        _watcher.EnableRaisingEvents = true;
    }

    private void Debounce()
    {
        lock (_sync)
        {
            _debounce ??= new System.Threading.Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
            _debounce.Change(250, Timeout.Infinite);
        }
    }

    private void Reload()
    {
        // Timer callback: an unhandled exception here would take down the daemon.
        try
        {
            var loaded = TryRead(_configPath);
            if (loaded is null) return; // parse error or still being written: keep last good config

            lock (_sync)
            {
                // Suppress the watcher echo of our own Update() writes. Record equality is
                // unreliable here (the Tools dictionary compares by reference), so compare
                // serialized forms instead.
                if (Serialize(loaded) == Serialize(_current)) return;
                _current = loaded;
            }
            ApplyLogLevel(loaded);
            Log.Debug("config reloaded");
            Changed?.Invoke(loaded);
        }
        catch (Exception ex)
        {
            Log.Error($"config reload failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static JTalkConfig? TryRead(string configPath)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var json = File.ReadAllText(configPath); // ReadAllText handles a BOM if an editor added one
                return JsonSerializer.Deserialize(json, JTalkJsonContext.Default.JTalkConfig)?.Normalized();
            }
            catch (IOException)
            {
                Thread.Sleep(100); // writer still holds the file
            }
            catch (JsonException ex)
            {
                Log.Warn($"config parse error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                // e.g. UnauthorizedAccessException; never let a config read escalate
                Log.Error($"config read failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
        return null;
    }

    private static string Serialize(JTalkConfig config) =>
        JsonSerializer.Serialize(config, JTalkJsonContext.Default.JTalkConfig);

    private static void WriteFile(string configPath, JTalkConfig config)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(configPath))
            ?? throw new ArgumentException("config path must have a parent directory", nameof(configPath));
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(configPath)}.{Guid.NewGuid():N}.tmp");
        var replaceBackup = tempPath + ".bak";
        try
        {
            File.WriteAllText(tempPath, Serialize(config) + Environment.NewLine, Encodings.Utf8NoBom);
            if (File.Exists(configPath))
                File.Replace(tempPath, configPath, replaceBackup);
            else
                File.Move(tempPath, configPath);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup after a failed replace */ }
            try { File.Delete(replaceBackup); } catch { /* best-effort cleanup after a failed replace */ }
        }
    }

    private static void ApplyLogLevel(JTalkConfig config)
    {
        Log.Level = config.LogLevel.ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "warn" => LogLevel.Warn,
            "error" => LogLevel.Error,
            _ => LogLevel.Info,
        };
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
