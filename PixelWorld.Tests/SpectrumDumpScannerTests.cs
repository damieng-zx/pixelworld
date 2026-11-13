using PixelWorld.DumpScanners;
using PixelWorld.Machines;
using PixelWorld.OffsetFinders;

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

    [Fact]
    public void Scan_SkipsRomCopyAndFindsFontReferencedByCode()
    {
        var buffer = FontStructureTests.Embed(Spectrum.RomFont.ToArray(), 30000);
        FontStructureTests.TestFont().CopyTo(buffer, 50000);
        byte[] code = [0x21, 0x50, 0xC2, 0x22, 0x36, 0x5C]; // LD HL,49744 ; LD (23606),HL
        code.CopyTo(buffer, 32000);

        var fonts = SpectrumDumpScanner.Scan(buffer);

        Assert.DoesNotContain(fonts, f => f.Offset == 30000);
        var found = Assert.Single(fonts, f => f.Offset == 50000);
        Assert.Contains("Code refs", found.Methods);
        Assert.Equal(FontStructureTests.TestFont(), found.Data);
    }

    [Fact]
    public void Scan_PartialFont_IsFoundWithRange()
    {
        var font = FontStructureTests.TestFont();
        var buffer = FontStructureTests.Embed(font.AsSpan(('0' - 32) * 8, ('Z' - '0' + 1) * 8).ToArray(), 50000);

        var found = Assert.Single(SpectrumDumpScanner.Scan(buffer), f => f.Offset == 50000 - ('0' - 32) * 8);

        Assert.Equal(new CharRange('0', 'Z'), found.Range);
    }
}
