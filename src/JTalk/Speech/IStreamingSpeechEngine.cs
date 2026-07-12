namespace JTalk.Speech;

/// <summary>Engines that can begin playback before synthesis finishes downloading.</summary>
public interface IStreamingSpeechEngine : ISpeechEngine
{
    /// <summary>
    /// Starts synthesis and returns once response headers are validated (throws on HTTP
    /// error with the same shape as <see cref="ISpeechEngine.SynthesizeWavAsync"/>, so the
    /// engine fallback chain works unchanged); audio bytes then arrive progressively on
    /// the result's stream. The caller owns the result and must dispose it.
    /// </summary>
    Task<StreamingSpeech> SynthesizeStreamingAsync(string text, string? voice, double rate, CancellationToken cancellationToken);
}
