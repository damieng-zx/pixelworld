using PixelWorld.Machines;
using PixelWorld.Tools;

namespace PixelWorld.Tests;

public class TileExtractorTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "pw-tiles-" + Guid.NewGuid().ToString("N"));

    public TileExtractorTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, true);
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A screen's worth of varied bytes, so the tiles on it are nearly all different.</summary>
    private static byte[] Screen(int length = Spectrum.ScreenSize)
    {
        var seed = 11u;
        var bytes = new byte[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            seed = seed * 1664525u + 1013904223u;
            bytes[i] = (byte)(seed >> 24);
        }

        return bytes;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2000)]
    [InlineData(Spectrum.ScreenSize - 1)]
    public void Extract_FileShorterThanAScreen_IsSkippedWithoutStoppingTheRest(int length)
    {
        var tooShort = WriteFile("short.bin", Screen(length));
        var screen = WriteFile("screen.bin", Screen());
        var output = Path.Combine(folder, "out");
        Directory.CreateDirectory(output);

        // The short file comes first, so a crash on it would leave the real screen unprocessed.
        TileExtractor.Extract([tooShort, screen], output, 1, 1000);

        Assert.False(File.Exists(Path.Combine(output, "short.tiles")));
        var tiles = Path.Combine(output, "screen.tiles");
        Assert.True(File.Exists(tiles));
        Assert.Equal(0, new FileInfo(tiles).Length % 8); // whole 8-byte tiles
        Assert.True(new FileInfo(tiles).Length > 0);
    }

    [Fact]
    public void Extract_FileExactlyOneScreenLong_IsProcessed()
    {
        var screen = WriteFile("exact.bin", Screen());
        var output = Path.Combine(folder, "out");
        Directory.CreateDirectory(output);

        TileExtractor.Extract([screen], output, 1, 1000);

        Assert.True(File.Exists(Path.Combine(output, "exact.tiles")));
    }
}
