using System.Diagnostics;
using JTalk.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JTalk.Speech;

/// <summary>Single playback path for all engines: WAV bytes or a PCM stream → volume → default output device.</summary>
public static class AudioPlayer
{
    // Matches WaveOutEvent's default DesiredLatency (2 × 150 ms buffers), so Play() starts
    // with both device buffers holding real audio rather than silence fill.
    private const int PrebufferMs = 300;

    public static async Task PlayAsync(byte[] wav, float volume, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream(wav);
        await using var reader = new WaveFileReader(ms);
        var provider = new VolumeSampleProvider(reader.ToSampleProvider())
        {
            Volume = Math.Clamp(volume, 0f, 1f),
        };

        using var output = new WaveOutEvent();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, _) => done.TrySetResult();

        output.Init(provider);
        output.Play();

        await using var reg = cancellationToken.Register(() =>
        {
            try { output.Stop(); } catch { /* device may already be gone */ }
        });

        // Safety net: PlaybackStopped is raised by a device callback that can be lost
        // if the audio device disappears mid-utterance.
        var maxWait = reader.TotalTime + TimeSpan.FromSeconds(5);
        await Task.WhenAny(done.Task, Task.Delay(maxWait, CancellationToken.None));

        // A cancelled utterance stops playback via the registration above and lands here
        // normally; rethrow so callers see cancellation, not success.
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Plays progressively-arriving PCM, starting output after a short prebuffer.
    /// Contract for the fallback chain: throws ⇔ no audio was played (callers may retry
    /// with another engine); returns normally ⇔ playback happened, even if the stream died
    /// early. Does not dispose <paramref name="speech"/> — the caller owns it.
    /// </summary>
    public static Task PlayStreamingAsync(StreamingSpeech speech, float volume, CancellationToken cancellationToken) =>
        PlayStreamingAsync(speech, volume, speech.ContentTimeout, cancellationToken);

    internal static async Task PlayStreamingAsync(
        StreamingSpeech speech,
        float volume,
        TimeSpan contentTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(contentTimeout, TimeSpan.Zero);
        var format = new WaveFormat(speech.SampleRate, speech.BitsPerSample, speech.Channels);
        var provider = new StreamingWaveProvider(format);
        var buffer = new byte[16 * 1024];
        var sw = Stopwatch.StartNew();
        using var contentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        contentCts.CancelAfter(contentTimeout);

        // Prebuffer: any failure here means nothing has been spoken yet, so exceptions
        // escape raw and the engine fallback chain may still take over.
        var prebufferBytes = format.AverageBytesPerSecond * PrebufferMs / 1000;
        var eof = false;
        while (provider.BufferedBytes < prebufferBytes)
        {
            int n;
            try
            {
                n = await speech.Pcm.ReadAsync(buffer, contentCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && contentCts.IsCancellationRequested)
            {
                throw new TimeoutException($"speech content did not begin within {contentTimeout.TotalSeconds:0.#} seconds");
            }
            if (n == 0)
            {
                eof = true;
                break;
            }
            provider.Write(buffer.AsSpan(0, n));
        }
        if (eof && provider.BufferedBytes == 0) return; // empty clip: nothing to play
        if (eof) provider.Complete();

        using var output = new WaveOutEvent();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, _) => done.TrySetResult();
        output.Init(new VolumeSampleProvider(provider.ToSampleProvider())
        {
            Volume = Math.Clamp(volume, 0f, 1f),
        });
        output.Play();
        Log.Debug($"streaming playback started after {sw.ElapsedMilliseconds} ms ({provider.BufferedBytes} bytes prebuffered)");

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(contentCts.Token);
        var pump = eof
            ? Task.CompletedTask
            : PumpAsync(
                speech.Pcm,
                provider,
                buffer,
                contentTimeout,
                pumpCts.Token,
                contentCts.Token,
                cancellationToken);

        await using var reg = cancellationToken.Register(() =>
        {
            try { output.Stop(); } catch { /* device may already be gone */ }
        });

        // Safety net (PlaybackStopped can be lost if the device disappears): once the pump
        // completes, written audio bounds real playback. The content CTS separately bounds
        // a stalled network stream, so only the standard five-second device grace is needed.
        var playClock = Stopwatch.StartNew();
        while (!done.Task.IsCompleted)
        {
            await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
            if (provider.IsCompleted && playClock.Elapsed >
                TimeSpan.FromSeconds(provider.TotalBytesWritten / (double)format.AverageBytesPerSecond) + TimeSpan.FromSeconds(5))
                break;
        }

        Log.Debug($"streaming playback finished: {provider.TotalBytesWritten} bytes "
            + $"({provider.TotalBytesWritten / (double)format.AverageBytesPerSecond:F1} s audio) "
            + $"in {playClock.Elapsed.TotalSeconds:F1} s wall{(done.Task.IsCompleted ? "" : " (watchdog)")}");
        pumpCts.Cancel(); // abort a still-downloading read when playback ended early
        await pump;       // never throws; must finish before the caller disposes speech.Pcm
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task PumpAsync(
        Stream pcm,
        StreamingWaveProvider provider,
        byte[] buffer,
        TimeSpan contentTimeout,
        CancellationToken pumpToken,
        CancellationToken contentToken,
        CancellationToken callerToken)
    {
        try
        {
            // No throttling: drain at network speed so the 20 s HTTP window keeps its meaning.
            int n;
            while ((n = await pcm.ReadAsync(buffer, pumpToken)) > 0)
                provider.Write(buffer.AsSpan(0, n));
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            // daemon/utterance cancellation — benign
        }
        catch (OperationCanceledException) when (contentToken.IsCancellationRequested)
        {
            // Playback already started, so finish buffered audio instead of replaying the
            // utterance through another engine.
            Log.Warn($"speech stream timed out after {contentTimeout.TotalSeconds:0.#} seconds and "
                + $"{provider.TotalBytesWritten} bytes; finishing buffered audio");
        }
        catch (OperationCanceledException) when (pumpToken.IsCancellationRequested)
        {
            // Playback/device ended before the download — benign.
        }
        catch (Exception ex)
        {
            // Playback already started: never rethrow — falling back to another engine
            // now would replay the words already spoken. Finish what's buffered instead.
            Log.Warn($"speech stream failed mid-playback after {provider.TotalBytesWritten} bytes; finishing buffered audio: {ex.Message}");
        }
        finally
        {
            provider.Complete(); // drain what we have → natural PlaybackStopped
        }
    }
}
