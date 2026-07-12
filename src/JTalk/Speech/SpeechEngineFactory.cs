using JTalk.Config;
using JTalk.Ipc;
using JTalk.Logging;

namespace JTalk.Speech;

public static class SpeechEngineFactory
{
    public static IReadOnlyList<string> OpenAIVoices { get; } =
        ["alloy", "ash", "ballad", "coral", "echo", "fable", "onyx", "nova", "sage", "shimmer", "verse"];

    public static ISpeechEngine? TryCreatePiper(ConfigService config)
    {
        var exe = PiperSpeechEngine.ResolveExe(config.Current);
        if (File.Exists(exe)) return new PiperSpeechEngine(config);
        Log.WarnOnce($"piper.exe not found at '{exe}' (run install\\setup-piper.ps1); falling back to windows");
        return null;
    }

    public static ISpeechEngine? TryCreateOpenAI(ConfigService config)
    {
        if (OpenAISpeechEngine.ResolveApiKey(config.Current) is not null)
            return new OpenAISpeechEngine(config);
        Log.WarnOnce("no OpenAI API key for TTS (config openaiTts.apiKey or OPENAI_API_KEY); falling back");
        return null;
    }

    /// <summary>All selectable voices across engines, for the tray menu and `jtalk voice list`.</summary>
    public static List<VoiceInfo> CatalogVoices(JTalkConfig cfg)
    {
        var list = WindowsSpeechEngine.EnumerateVoices()
            .Select(n => new VoiceInfo { Engine = "windows", Name = n })
            .ToList();

        var piperDir = Environment.ExpandEnvironmentVariables(cfg.Piper.VoicesDir);
        if (Directory.Exists(piperDir))
        {
            list.AddRange(Directory.EnumerateFiles(piperDir, "*.onnx")
                .Select(f => new VoiceInfo { Engine = "piper", Name = Path.GetFileNameWithoutExtension(f) })
                .OrderBy(v => v.Name));
        }

        list.AddRange(OpenAIVoices.Select(n => new VoiceInfo { Engine = "openai", Name = n }));
        return list;
    }
}
