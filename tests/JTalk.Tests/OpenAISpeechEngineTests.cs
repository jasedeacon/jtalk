using System.Net;
using System.Net.Http;
using System.Text.Json;
using JTalk.Config;
using JTalk.Speech;
using Xunit;

namespace JTalk.Tests;

public sealed class OpenAISpeechEngineTests
{
    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class StreamingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingStream()),
            });
    }

    [Fact]
    public void WrapPcmInWavWritesAValidRiffHeader()
    {
        byte[] pcm = [1, 2, 3, 4];

        var wav = OpenAISpeechEngine.WrapPcmInWav(pcm, sampleRate: 24000, bitsPerSample: 16, channels: 1);

        Assert.Equal(48, wav.Length); // 44-byte header + 4 bytes of PCM
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(36 + pcm.Length, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVE"u8.ToArray(), wav[8..12]);
        Assert.Equal("fmt "u8.ToArray(), wav[12..16]);
        Assert.Equal(16, BitConverter.ToInt32(wav, 16));           // fmt chunk size
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));            // PCM format tag
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));            // channels
        Assert.Equal(24000, BitConverter.ToInt32(wav, 24));        // sample rate
        Assert.Equal(48000, BitConverter.ToInt32(wav, 28));        // byte rate
        Assert.Equal(2, BitConverter.ToInt16(wav, 32));            // block align
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));           // bits per sample
        Assert.Equal("data"u8.ToArray(), wav[36..40]);
        Assert.Equal(pcm.Length, BitConverter.ToInt32(wav, 40));
        Assert.Equal(pcm, wav[44..]);
    }

    [Fact]
    public void BuildRequestBodyUsesPcmAndDefaultVoice()
    {
        var body = OpenAISpeechEngine.BuildRequestBody(new OpenAITtsConfig(), "hello", voice: null, rate: 1.0);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("gpt-4o-mini-tts", root.GetProperty("model").GetString());
        Assert.Equal("hello", root.GetProperty("input").GetString());
        Assert.Equal("nova", root.GetProperty("voice").GetString());
        Assert.Equal("pcm", root.GetProperty("response_format").GetString());
        Assert.Equal(1.0, root.GetProperty("speed").GetDouble());
    }

    [Theory]
    [InlineData(0.1, 0.25)]
    [InlineData(9.0, 4.0)]
    public void BuildRequestBodyClampsSpeed(double rate, double expected)
    {
        var body = OpenAISpeechEngine.BuildRequestBody(new OpenAITtsConfig(), "hi", "onyx", rate);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(expected, doc.RootElement.GetProperty("speed").GetDouble());
        Assert.Equal("onyx", doc.RootElement.GetProperty("voice").GetString());
    }

    [Fact]
    public void BuildRequestBodySendsInstructionsOnlyToGptModels()
    {
        var gpt = new OpenAITtsConfig { Model = "gpt-4o-mini-tts", Instructions = "speak fast" };
        var tts1 = new OpenAITtsConfig { Model = "tts-1", Instructions = "speak fast" };
        var noInstructions = new OpenAITtsConfig { Model = "gpt-4o-mini-tts", Instructions = "" };

        using var gptDoc = JsonDocument.Parse(OpenAISpeechEngine.BuildRequestBody(gpt, "hi", null, 1.0));
        Assert.Equal("speak fast", gptDoc.RootElement.GetProperty("instructions").GetString());

        using var tts1Doc = JsonDocument.Parse(OpenAISpeechEngine.BuildRequestBody(tts1, "hi", null, 1.0));
        Assert.False(tts1Doc.RootElement.TryGetProperty("instructions", out _));

        using var bareDoc = JsonDocument.Parse(OpenAISpeechEngine.BuildRequestBody(noInstructions, "hi", null, 1.0));
        Assert.False(bareDoc.RootElement.TryGetProperty("instructions", out _));
    }

    [Fact]
    public async Task CreateFromResponseAsyncThrowsWithStatusAndBodyOnError()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"bad key"}}"""),
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => OpenAISpeechEngine.CreateFromResponseAsync(response, CancellationToken.None));

        Assert.StartsWith("openai tts 401: ", ex.Message);
        Assert.Contains("bad key", ex.Message);
    }

    [Fact]
    public async Task CreateFromResponseAsyncTruncatesLongErrorBodies()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(new string('x', 500)),
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => OpenAISpeechEngine.CreateFromResponseAsync(response, CancellationToken.None));

        Assert.Equal("openai tts 500: ".Length + 200, ex.Message.Length);
    }

    [Fact]
    public async Task CreateFromResponseAsyncReturnsStreamingSpeechOnSuccess()
    {
        byte[] pcm = [1, 2, 3, 4];
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(pcm)),
        };

        await using var speech = await OpenAISpeechEngine.CreateFromResponseAsync(response, CancellationToken.None);

        Assert.Equal(24000, speech.SampleRate);
        Assert.Equal(16, speech.BitsPerSample);
        Assert.Equal(1, speech.Channels);
        using var ms = new MemoryStream();
        await speech.Pcm.CopyToAsync(ms);
        Assert.Equal(pcm, ms.ToArray());
    }

    [Fact]
    public async Task StreamingPrebufferTimeoutThrowsBeforePlaybackBegins()
    {
        await using var speech = new StreamingSpeech(
            new BlockingStream(),
            response: null,
            sampleRate: 24000,
            bitsPerSample: 16,
            channels: 1);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => AudioPlayer.PlayStreamingAsync(
            speech,
            volume: 1f,
            contentTimeout: TimeSpan.FromMilliseconds(50),
            cancellationToken: CancellationToken.None));

        Assert.Contains("did not begin", ex.Message);
    }

    [Fact]
    public async Task BufferedCompatibilityPathAlsoTimesOutStalledContent()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"jtalk-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "config.json");
        Directory.CreateDirectory(dir);
        try
        {
            ConfigService.MutateFile(path, c => c with
            {
                OpenAITts = c.OpenAITts with { ApiKey = "test-key" },
            });
            using var config = new ConfigService(path);
            using var http = new HttpClient(new StreamingHandler()) { Timeout = TimeSpan.FromSeconds(1) };
            using var engine = new OpenAISpeechEngine(config, http, TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAsync<TimeoutException>(() => engine.SynthesizeWavAsync(
                "hello", voice: null, rate: 1.0, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
