using JTalk.Config;
using Xunit;

namespace JTalk.Tests;

public sealed class ConfigServiceTests
{
    [Fact]
    public void StaleServiceUpdateRebasesOnLatestDiskState()
    {
        var dir = NewTempDirectory();
        var path = Path.Combine(dir, "config.json");
        try
        {
            using var first = new ConfigService(path);
            using var stale = new ConfigService(path);

            first.Update(c => c with { Volume = 25 });
            stale.Update(c => c with { Muted = true });

            var saved = ConfigService.LoadOrCreate(path);
            Assert.Equal(25, saved.Volume);
            Assert.True(saved.Muted);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WritesValidUtf8WithoutBomOrTemporaryFiles()
    {
        var dir = NewTempDirectory();
        var path = Path.Combine(dir, "config.json");
        try
        {
            ConfigService.MutateFile(path, c => c with { Volume = 42 });

            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.Equal(42, ConfigService.LoadOrCreate(path).Volume);
            Assert.Empty(Directory.EnumerateFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jtalk-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
