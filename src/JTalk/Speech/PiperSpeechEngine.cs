using System.Diagnostics;
using System.Globalization;
using JTalk.Config;

namespace JTalk.Speech;

/// <summary>
/// Local neural TTS via the Piper CLI (venv install under %APPDATA%\jtalk\piper,
/// created by install\setup-piper.ps1). Subprocess per utterance: model load costs
/// ~0.5-2 s, acceptable for notifications; piper's http_server mode is the future
/// optimization if that ever grates.
/// </summary>
public sealed class PiperSpeechEngine : ISpeechEngine
{
    private readonly ConfigService _config;

    public string Name => "piper";

    public PiperSpeechEngine(ConfigService config) => _config = config;

    public static string ResolveExe(JTalkConfig cfg) => Environment.ExpandEnvironmentVariables(cfg.Piper.Exe);

    public async Task<byte[]> SynthesizeWavAsync(string text, string? voice, double rate, CancellationToken cancellationToken)
    {
        var cfg = _config.Current;
        var exe = ResolveExe(cfg);
        var voicesDir = Environment.ExpandEnvironmentVariables(cfg.Piper.VoicesDir);
        if (!File.Exists(exe))
            throw new FileNotFoundException($"piper.exe not found at '{exe}'; run install\\setup-piper.ps1");

        var model = string.IsNullOrWhiteSpace(voice) ? "en_GB-alan-medium" : voice;
        var tmpWav = Path.Combine(Path.GetTempPath(), $"jtalk-piper-{Guid.NewGuid():N}.wav");
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                // Text goes in via stdin, never argv: command lines are visible to every
                // local process, and spoken payloads can be sensitive.
                RedirectStandardInput = true,
                StandardInputEncoding = Encodings.Utf8NoBom,
            };
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add(model);
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(voicesDir);
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(tmpWav);
            if (Math.Abs(rate - 1.0) > 0.01)
            {
                // piper speaks faster with smaller length-scale (inverse of rate)
                psi.ArgumentList.Add("--length-scale");
                psi.ArgumentList.Add((1.0 / Math.Clamp(rate, 0.5, 3.0)).ToString("0.###", CultureInfo.InvariantCulture));
            }

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("failed to start piper");

            using var cap = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cap.CancelAfter(TimeSpan.FromSeconds(30));
            // Registered on the linked token so both queue shutdown AND the 30 s cap kill piper.
            await using var kill = cap.Token.Register(() =>
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            });

            var stderrTask = proc.StandardError.ReadToEndAsync(CancellationToken.None);

            // Piper synthesizes one utterance per stdin line, so collapse to a single line.
            await proc.StandardInput.WriteLineAsync(text.ReplaceLineEndings(" ").AsMemory(), cap.Token);
            proc.StandardInput.Close();

            await proc.WaitForExitAsync(cap.Token);

            if (proc.ExitCode != 0)
            {
                // Last stderr line, flattened: piper failures are Python tracebacks
                // and the final line carries the actual error.
                var stderr = (await stderrTask).Trim();
                var lastLine = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .LastOrDefault() ?? "";
                throw new InvalidOperationException(
                    $"piper exit {proc.ExitCode}: {(lastLine.Length > 300 ? lastLine[..300] : lastLine)}");
            }
            return await File.ReadAllBytesAsync(tmpWav, cancellationToken);
        }
        finally
        {
            try { File.Delete(tmpWav); } catch { /* temp cleanup is best-effort */ }
        }
    }
}
