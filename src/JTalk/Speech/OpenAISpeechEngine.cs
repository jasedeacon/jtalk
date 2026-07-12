using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JTalk.Config;
using JTalk.Logging;

namespace JTalk.Speech;

/// <summary>Cloud TTS tier: OpenAI gpt-4o-mini-tts. Streams raw PCM so playback can start before the download finishes.</summary>
public sealed class OpenAISpeechEngine : IStreamingSpeechEngine, IDisposable
{
    internal static readonly TimeSpan DefaultContentTimeout = TimeSpan.FromSeconds(20);
    private const int SampleRate = 24000;
    private const short BitsPerSample = 16;
    private const short Channels = 1;

    private readonly ConfigService _config;
    private readonly HttpClient _http;
    private readonly TimeSpan _contentTimeout;

    public string Name => "openai";

    public OpenAISpeechEngine(ConfigService config)
        : this(config, new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, DefaultContentTimeout)
    {
    }

    internal OpenAISpeechEngine(ConfigService config, HttpClient http, TimeSpan contentTimeout)
    {
        _config = config;
        _http = http;
        _contentTimeout = contentTimeout;
    }

    public static string? ResolveApiKey(JTalkConfig cfg) => ApiKeys.Resolve(
        cfg.OpenAITts.ApiKey,
        ApiKeys.Env(cfg.OpenAITts.ApiKeyEnvVar),
        cfg.Summarizer.OpenAIApiKey,
        ApiKeys.Env(cfg.Summarizer.OpenAIApiKeyEnvVar),
        ApiKeys.Env("OPENAI_API_KEY"));

    public async Task<StreamingSpeech> SynthesizeStreamingAsync(string text, string? voice, double rate, CancellationToken cancellationToken)
    {
        var cfg = _config.Current;
        var apiKey = ResolveApiKey(cfg)
            ?? throw new InvalidOperationException("no OpenAI API key configured for TTS");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech")
        {
            Content = new StringContent(BuildRequestBody(cfg.OpenAITts, text, voice, rate), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // ResponseHeadersRead: return as soon as the status is known so playback can start
        // on the first chunks. HttpClient.Timeout covers only this header phase; the returned
        // StreamingSpeech carries the separate content deadline used by the playback layer.
        var sw = Stopwatch.StartNew();
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var speech = await CreateFromResponseAsync(response, cancellationToken, _contentTimeout);
        Log.Debug($"openai tts: response headers in {sw.ElapsedMilliseconds} ms");
        return speech;
    }

    public async Task<byte[]> SynthesizeWavAsync(string text, string? voice, double rate, CancellationToken cancellationToken)
    {
        await using var speech = await SynthesizeStreamingAsync(text, voice, rate, cancellationToken);
        using var ms = new MemoryStream();
        using var contentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        contentCts.CancelAfter(speech.ContentTimeout);
        try
        {
            await speech.Pcm.CopyToAsync(ms, contentCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"openai tts content did not finish within {speech.ContentTimeout.TotalSeconds:0.#} seconds");
        }
        return WrapPcmInWav(ms.ToArray(), SampleRate, BitsPerSample, Channels);
    }

    // "pcm" not "wav": OpenAI streams its WAV responses with placeholder
    // 0xFFFFFFFF RIFF/data chunk sizes, which NAudio rejects. Raw PCM
    // (24 kHz / 16-bit / mono) also needs no container parsing to stream.
    internal static string BuildRequestBody(OpenAITtsConfig cfg, string text, string? voice, double rate)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = cfg.Model,
            ["input"] = text,
            ["voice"] = string.IsNullOrWhiteSpace(voice) ? "nova" : voice,
            ["response_format"] = "pcm",
            ["speed"] = Math.Clamp(rate, 0.25, 4.0),
        };
        // gpt-* TTS models ignore `speed`; pace/tone comes from `instructions`,
        // which the older tts-1 family rejects — send it only where it's understood.
        if (!string.IsNullOrWhiteSpace(cfg.Instructions)
            && cfg.Model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
            payload["instructions"] = cfg.Instructions;
        return JsonSerializer.Serialize(payload);
    }

    // Owns `response` from the moment it's called: disposes it on every throw path,
    // transfers ownership into the returned StreamingSpeech on success.
    internal static async Task<StreamingSpeech> CreateFromResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        TimeSpan? contentTimeout = null)
    {
        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"openai tts {(int)response.StatusCode}: {(error.Length > 200 ? error[..200] : error)}");
            }
        }
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new StreamingSpeech(stream, response, SampleRate, BitsPerSample, Channels, contentTimeout);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    internal static byte[] WrapPcmInWav(byte[] pcm, int sampleRate, short bitsPerSample, short channels)
    {
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;

        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);                // fmt chunk size
        w.Write((short)1);          // PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write("data"u8);
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    public void Dispose() => _http.Dispose();
}
