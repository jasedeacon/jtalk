using JTalk.Speech;
using NAudio.Wave;
using Xunit;

namespace JTalk.Tests;

public sealed class StreamingWaveProviderTests
{
    private static StreamingWaveProvider NewProvider() => new(new WaveFormat(24000, 16, 1)); // BlockAlign 2

    [Fact]
    public void ReadWhileProducingWithNoDataReturnsFullCountOfSilence()
    {
        var provider = NewProvider();
        var buffer = new byte[] { 0xAA, 0xAA, 0xAA, 0xAA };

        var n = provider.Read(buffer, 0, 4);

        Assert.Equal(4, n);
        Assert.Equal(new byte[4], buffer);
    }

    [Fact]
    public void WrittenBytesRoundTripThroughRead()
    {
        var provider = NewProvider();
        provider.Write([1, 2, 3, 4]);
        var buffer = new byte[4];

        var n = provider.Read(buffer, 0, 4);

        Assert.Equal(4, n);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer);
        Assert.Equal(0, provider.BufferedBytes);
    }

    [Fact]
    public void PartialDataWhileProducingIsSilenceFilledToFullCount()
    {
        var provider = NewProvider();
        provider.Write([1, 2]);
        var buffer = new byte[] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };

        var n = provider.Read(buffer, 0, 6);

        Assert.Equal(6, n);
        Assert.Equal(new byte[] { 1, 2, 0, 0, 0, 0 }, buffer);
    }

    [Fact]
    public void PartialFrameIsHeldBackUntilItsMateArrives()
    {
        var provider = NewProvider();
        provider.Write([1, 2, 3]); // 1.5 frames

        var buffer = new byte[4];
        provider.Read(buffer, 0, 4);
        Assert.Equal(new byte[] { 1, 2, 0, 0 }, buffer); // odd byte 3 held, not byte-shifted

        provider.Write([4]); // completes the held frame
        provider.Read(buffer, 0, 4);
        Assert.Equal(new byte[] { 3, 4, 0, 0 }, buffer);
    }

    [Fact]
    public void CompletedRemainderIsServedShortThenZeroRepeatedly()
    {
        var provider = NewProvider();
        provider.Write([1, 2, 3, 4]);
        provider.Complete();
        var buffer = new byte[] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };

        Assert.Equal(4, provider.Read(buffer, 0, 8)); // short final read, no silence fill
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer[..4]);
        Assert.Equal(new byte[] { 0xAA, 0xAA, 0xAA, 0xAA }, buffer[4..]);
        Assert.Equal(0, provider.Read(buffer, 0, 8));
        Assert.Equal(0, provider.Read(buffer, 0, 8));
    }

    [Fact]
    public void CompletedAndDrainedReturnsZeroImmediately()
    {
        var provider = NewProvider();
        provider.Complete();

        Assert.Equal(0, provider.Read(new byte[4], 0, 4));
    }

    [Fact]
    public void StrandedOddByteAtCompletionIsDroppedNotEmitted()
    {
        var provider = NewProvider();
        provider.Write([1, 2, 3]);
        provider.Complete();
        var buffer = new byte[4];

        Assert.Equal(2, provider.Read(buffer, 0, 4)); // whole frame only
        Assert.Equal(0, provider.Read(buffer, 0, 4)); // stranded byte 3 never emitted
    }

    [Fact]
    public void OneReadSpansMultipleWrittenChunks()
    {
        var provider = NewProvider();
        provider.Write([1, 2]);
        provider.Write([3, 4]);
        provider.Write([5, 6]);
        var buffer = new byte[6];

        var n = provider.Read(buffer, 0, 6);

        Assert.Equal(6, n);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, buffer);
    }

    [Fact]
    public void CountersTrackWritesAndReads()
    {
        var provider = NewProvider();
        Assert.Equal(0, provider.TotalBytesWritten);
        Assert.False(provider.IsCompleted);

        provider.Write([1, 2, 3, 4]);
        provider.Write([5, 6]);
        Assert.Equal(6, provider.TotalBytesWritten);
        Assert.Equal(6, provider.BufferedBytes);

        provider.Read(new byte[4], 0, 4);
        Assert.Equal(6, provider.TotalBytesWritten); // unchanged by reads
        Assert.Equal(2, provider.BufferedBytes);

        provider.Complete();
        Assert.True(provider.IsCompleted);
    }

    [Fact]
    public void WriteAfterCompleteThrows()
    {
        var provider = NewProvider();
        provider.Complete();

        Assert.Throws<InvalidOperationException>(() => provider.Write([1, 2]));
    }

    [Fact]
    public void ReadRespectsBufferOffset()
    {
        var provider = NewProvider();
        provider.Write([1, 2]);
        var buffer = new byte[] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };

        var n = provider.Read(buffer, 2, 4);

        Assert.Equal(4, n);
        Assert.Equal(new byte[] { 0xAA, 0xAA, 1, 2, 0, 0 }, buffer);
    }

    [Fact]
    public async Task ConcurrentWriterAndReaderRoundTripAllData()
    {
        var provider = NewProvider();
        var source = new byte[16 * 1024];
        new Random(42).NextBytes(source);

        var writer = Task.Run(() =>
        {
            var rng = new Random(7);
            var offset = 0;
            while (offset < source.Length)
            {
                var n = Math.Min(rng.Next(1, 517), source.Length - offset);
                provider.Write(source.AsSpan(offset, n));
                offset += n;
            }
            provider.Complete();
        });

        // Request only bytes known to be buffered, so every read is pure data (buffered
        // can only grow between the snapshot and the read) and silence fill never mixes in.
        using var received = new MemoryStream();
        var buffer = new byte[640];
        while (true)
        {
            // IsCompleted first: once true no more writes can arrive, so BufferedBytes == 0
            // really means fully drained.
            if (provider.IsCompleted && provider.BufferedBytes == 0) break;
            var request = (int)Math.Min(provider.BufferedBytes, buffer.Length);
            request -= request % 2;
            if (request == 0)
            {
                await Task.Yield(); // let the writer make progress
                continue;
            }
            var n = provider.Read(buffer, 0, request);
            Assert.Equal(request, n);
            received.Write(buffer, 0, n);
        }
        await writer;

        Assert.Equal(source, received.ToArray());
    }
}
