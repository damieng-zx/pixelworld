using PixelWorld.DumpScanners;
using PixelWorld.Machines;

namespace PixelWorld.Tests;

public class SpectrumDumpScannerTests
{
    private static byte[] Embed(byte[] font, int at, int size = 65536)
    {
        var buffer = new byte[size];
        new Random(1).NextBytes(buffer);
        font.CopyTo(buffer, at);
        return buffer;
    }

    [Fact]
    public void IsRomFont_RomFont_IsTrue()
    {
        var buffer = Embed(Spectrum.RomFont.ToArray(), 30000);

        Assert.True(SpectrumDumpScanner.IsRomFont(buffer, 30000));
    }

    [Fact]
    public void IsRomFont_OneGlyphChanged_IsFalse()
    {
        var font = Spectrum.RomFont.ToArray();
        font[('A' - 32) * 8 + 3] ^= 0x18;
        var buffer = Embed(font, 30000);

        Assert.False(SpectrumDumpScanner.IsRomFont(buffer, 30000));
    }

    [Fact]
    public void IsRomFont_TooCloseToEnd_IsFalse()
    {
        var buffer = new byte[1000];

        Assert.False(SpectrumDumpScanner.IsRomFont(buffer, 500));
    }
}
