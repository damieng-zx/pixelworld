using PixelWorld.DumpScanners;
using PixelWorld.Machines;
using PixelWorld.OffsetFinders;

namespace PixelWorld.Tests;

public class SpectrumDumpScannerTests
{
    [Fact]
    public void CountRomGlyphsChanged_RomFont_IsZero()
    {
        Assert.Equal(0, SpectrumDumpScanner.CountRomGlyphsChanged(Spectrum.RomFont.ToArray()));
    }

    [Fact]
    public void CountRomGlyphsChanged_OneGlyphChanged_IsOne()
    {
        var font = Spectrum.RomFont.ToArray();
        font[('A' - 32) * 8 + 3] ^= 0x18;

        Assert.Equal(1, SpectrumDumpScanner.CountRomGlyphsChanged(font));
    }

    [Fact]
    public void CountRomGlyphsChanged_IgnoresSpaceAndCopyright()
    {
        var font = Spectrum.RomFont.ToArray();
        font[3] = 0xFF;
        font[95 * 8 + 3] = 0xFF;

        Assert.Equal(0, SpectrumDumpScanner.CountRomGlyphsChanged(font));
    }

    [Fact]
    public void Scan_SkipsRomCopyAndFindsFontReferencedByCode()
    {
        var buffer = TestData.Embed(Spectrum.RomFont.ToArray(), 30000);
        TestData.TestFont().CopyTo(buffer, 50000);
        byte[] code = [0x21, 0x50, 0xC2, 0x22, 0x36, 0x5C]; // LD HL,49744 ; LD (23606),HL
        code.CopyTo(buffer, 32000);

        var fonts = SpectrumDumpScanner.Scan(buffer);

        Assert.DoesNotContain(fonts, f => f.Offset == 30000);
        var found = Assert.Single(fonts, f => f.Offset == 50000);
        Assert.Contains(FinderMethod.CodeRefs, found.Methods);
        Assert.Equal(TestData.TestFont(), found.Data);
    }

    [Fact]
    public void Scan_PartialFont_IsFoundWithRange()
    {
        var font = TestData.TestFont();
        var buffer = TestData.Embed(font.AsSpan(('0' - 32) * 8, ('Z' - '0' + 1) * 8).ToArray(), 50000);

        var found = Assert.Single(SpectrumDumpScanner.Scan(buffer), f => f.Offset == 50000 - ('0' - 32) * 8);

        Assert.Equal(new CharRange('0', 'Z'), found.Range);
    }
}
