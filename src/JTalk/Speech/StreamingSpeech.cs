using System.Net.Http;

namespace JTalk.Speech;

/// <summary>
/// Raw PCM audio arriving progressively from an engine; owns the underlying HTTP response
/// so disposing aborts a still-downloading connection. Format is plain ints (not NAudio
/// types) to keep NAudio confined to the playback layer.
/// </summary>
public sealed class StreamingSpeech : IAsyncDisposable
{
    private readonly HttpResponseMessage? _response;
    private bool _disposed;

    internal StreamingSpeech(
        Stream pcm,
        HttpResponseMessage? response,
        int sampleRate,
        int bitsPerSample,
        int channels,
        TimeSpan? contentTimeout = null)
    {
        Pcm = pcm;
        _response = response;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        Channels = channels;
        ContentTimeout = contentTimeout ?? TimeSpan.FromSeconds(20);
    }

    public Stream Pcm { get; }
    public int SampleRate { get; }
    public int BitsPerSample { get; }
    public int Channels { get; }
    public TimeSpan ContentTimeout { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await Pcm.DisposeAsync();
        }
        catch
        {
            // disposing the content stream of an aborted connection can throw
        }
        _response?.Dispose();
    }
}
