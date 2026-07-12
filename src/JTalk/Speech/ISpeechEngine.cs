namespace JTalk.Speech;

public interface ISpeechEngine
{
    string Name { get; }

    /// <summary>Synthesizes text to a complete in-memory WAV file (RIFF bytes).</summary>
    Task<byte[]> SynthesizeWavAsync(string text, string? voice, double rate, CancellationToken cancellationToken);
}
