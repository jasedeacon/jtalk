using JTalk.Logging;
using Windows.Media.SpeechSynthesis;

namespace JTalk.Speech;

/// <summary>
/// Zero-setup default engine: WinRT Windows.Media.SpeechSynthesis (OneCore voices).
/// Chosen over legacy SAPI System.Speech because it returns a WAV stream (shared
/// playback path with the other engines) and exposes more voices, including a male one.
/// </summary>
public sealed class WindowsSpeechEngine : ISpeechEngine
{
    public string Name => "windows";

    public async Task<byte[]> SynthesizeWavAsync(string text, string? voice, double rate, CancellationToken cancellationToken)
    {
        using var synth = new SpeechSynthesizer();

        if (!string.IsNullOrWhiteSpace(voice))
        {
            var match = SpeechSynthesizer.AllVoices
                .FirstOrDefault(v => v.DisplayName.Contains(voice, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                synth.Voice = match;
            else
                Log.WarnOnce($"windows voice '{voice}' not found; using default '{synth.Voice.DisplayName}'");
        }

        synth.Options.SpeakingRate = Math.Clamp(rate, 0.5, 6.0);

        using var winStream = await synth.SynthesizeTextToStreamAsync(text).AsTask(cancellationToken);
        using var stream = winStream.AsStreamForRead();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    public static IEnumerable<string> EnumerateVoices() =>
        SpeechSynthesizer.AllVoices.Select(v => v.DisplayName).OrderBy(n => n);
}
