using NAudio.Wave;

namespace JTalk.Speech;

/// <summary>
/// Bridges a producer pumping PCM chunks to NAudio's pull-based playback: silence-fills
/// while producing so the device never underruns, then signals natural end-of-stream
/// (Read returns 0) once completed and drained. The buffer is unbounded by design — the
/// OpenAI speech API caps input at 4096 chars, so clips are small and peak memory matches
/// the old fully-buffered path anyway.
/// </summary>
internal sealed class StreamingWaveProvider : IWaveProvider
{
    private readonly Lock _lock = new();
    private readonly Queue<byte[]> _chunks = new();
    private int _headOffset;   // consumed bytes of _chunks.Peek()
    private long _buffered;
    private long _written;
    private bool _completed;

    public StreamingWaveProvider(WaveFormat waveFormat) => WaveFormat = waveFormat;

    public WaveFormat WaveFormat { get; }

    /// <summary>Bytes written but not yet read.</summary>
    public long BufferedBytes
    {
        get { lock (_lock) return _buffered; }
    }

    /// <summary>Total bytes ever written; bounds real playback time for the caller's watchdog.</summary>
    public long TotalBytesWritten
    {
        get { lock (_lock) return _written; }
    }

    public bool IsCompleted
    {
        get { lock (_lock) return _completed; }
    }

    /// <summary>Queues a copy of <paramref name="data"/> (callers may reuse their buffer).</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;
        var chunk = data.ToArray();
        lock (_lock)
        {
            if (_completed) throw new InvalidOperationException("write after Complete()");
            _chunks.Enqueue(chunk);
            _buffered += chunk.Length;
            _written += chunk.Length;
        }
    }

    /// <summary>Marks end of input: buffered audio drains, then Read returns 0. Idempotent.</summary>
    public void Complete()
    {
        lock (_lock) _completed = true;
    }

    // Runs on the playback device's callback thread. While producing, always returns
    // `count` (data then silence) so the device never sees a short read and stops early;
    // only whole BlockAlign frames of real data are served — serving a partial frame
    // before silence would byte-shift every later 16-bit sample into noise.
    public int Read(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            var serve = (int)Math.Min(_buffered, count);
            serve -= serve % WaveFormat.BlockAlign; // hold back a partial frame

            for (var copied = 0; copied < serve;)
            {
                var head = _chunks.Peek();
                var n = Math.Min(head.Length - _headOffset, serve - copied);
                Buffer.BlockCopy(head, _headOffset, buffer, offset + copied, n);
                copied += n;
                _headOffset += n;
                if (_headOffset == head.Length)
                {
                    _chunks.Dequeue();
                    _headOffset = 0;
                }
            }
            _buffered -= serve;

            if (_completed)
                return serve; // short final read, then 0 once drained — natural end-of-stream

            Array.Clear(buffer, offset + serve, count - serve); // silence-fill: keep the device fed
            return count;
        }
    }
}
