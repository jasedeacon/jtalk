using System.Net;
using System.Net.Http;
using JTalk.Speech;
using Xunit;

namespace JTalk.Tests;

public sealed class StreamingSpeechTests
{
    private sealed class ProbeStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        public bool ThrowOnDispose { get; set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
            if (ThrowOnDispose) throw new IOException("aborted connection");
        }
    }

    [Fact]
    public async Task DisposeAsyncDisposesStreamAndResponse()
    {
        var pcm = new ProbeStream();
        var content = new ProbeStream();
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(content) };
        var speech = new StreamingSpeech(pcm, response, sampleRate: 24000, bitsPerSample: 16, channels: 1);

        await speech.DisposeAsync();

        Assert.True(pcm.Disposed);
        Assert.True(content.Disposed); // response.Dispose() disposes its content stream
    }

    [Fact]
    public async Task DisposeAsyncIsDoubleDisposeSafe()
    {
        var speech = new StreamingSpeech(new MemoryStream(), response: null, sampleRate: 24000, bitsPerSample: 16, channels: 1);

        await speech.DisposeAsync();
        await speech.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncSwallowsStreamDisposeExceptionAndStillDisposesResponse()
    {
        var pcm = new ProbeStream { ThrowOnDispose = true };
        var content = new ProbeStream();
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(content) };
        var speech = new StreamingSpeech(pcm, response, sampleRate: 24000, bitsPerSample: 16, channels: 1);

        await speech.DisposeAsync();

        Assert.True(pcm.Disposed);
        Assert.True(content.Disposed);
    }

    [Fact]
    public void FormatPropertiesRoundTrip()
    {
        var speech = new StreamingSpeech(new MemoryStream(), response: null, sampleRate: 24000, bitsPerSample: 16, channels: 1);

        Assert.Equal(24000, speech.SampleRate);
        Assert.Equal(16, speech.BitsPerSample);
        Assert.Equal(1, speech.Channels);
    }
}
