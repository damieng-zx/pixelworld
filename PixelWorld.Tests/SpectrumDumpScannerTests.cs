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
    public void Scan_CodePointsOneRowOffStructure_PrefersTheCode()
    {
        // Digits and capitals only, so only the newer finders are involved and the code's reading
        // (one row after the structurally better one) is not a legacy find.
        var font = TestData.TestFont();
        var buffer = TestData.Embed(font.AsSpan(('0' - 32) * 8, ('Z' - '0' + 1) * 8).ToArray(), 50000);
        const int spaceAt = 50000 - ('0' - 32) * 8;
        byte[] code = [0x21, (spaceAt + 1 - 256) & 0xFF, (spaceAt + 1 - 256) >> 8, 0x22, 0x36, 0x5C]; // LD HL,nn ; LD (23606),HL
        code.CopyTo(buffer, 32000);

        var fonts = SpectrumDumpScanner.Scan(buffer);

        var found = Assert.Single(fonts, f => Math.Abs(f.Offset - spaceAt) < 8);
        Assert.Equal(spaceAt + 1, found.Offset);
        Assert.Contains(FinderMethod.CodeRefs, found.Methods);
    }

    [Fact]
    public void Scan_PartialFont_IsFoundWithRange()
    {
        var font = TestData.TestFont();
        var buffer = TestData.Embed(font.AsSpan(('0' - 32) * 8, ('Z' - '0' + 1) * 8).ToArray(), 50000);

        var found = Assert.Single(SpectrumDumpScanner.Scan(buffer), f => f.Offset == 50000 - ('0' - 32) * 8);

        Assert.Equal(new CharRange('0', 'Z'), found.Range);
    }

    private static FoundFont Found(int offset, int total, bool legacy, params FinderMethod[] methods) =>
        new(offset, Enumerable.Repeat((byte)offset, Spectrum.FontSize).ToArray(), CharRange.Full,
            new FontScore(Mirror: total, 0, 0, 0, 0, 26, 0, 0, CharRange.Full), methods, 10, legacy);

    [Fact]
    public void Merge_RealFontReplacesOverlappingGarbageFromTheOriginalFinders()
    {
        var garbage = Found(40000, 0, true, FinderMethod.ScreenTiles);
        var real = Found(40100, 12, false, FinderMethod.Structure);

        var kept = SpectrumDumpScanner.Merge([garbage, real]);

        Assert.Equal([40100], kept.Select(f => f.Offset));
    }

    [Theory]
    [InlineData(FinderMethod.RomGlyphs)]
    [InlineData(FinderMethod.Chars)]
    public void Merge_OriginalFindWithOtherEvidenceIsNotReplaced(FinderMethod evidence)
    {
        var lowScoring = Found(40000, 0, true, FinderMethod.ScreenTiles, evidence);
        var real = Found(40100, 12, false, FinderMethod.Structure);

        var kept = SpectrumDumpScanner.Merge([lowScoring, real]);

        Assert.Equal([40000], kept.Select(f => f.Offset));
    }

    [Fact]
    public void Merge_GarbageIsKeptWhenNothingOverlapsIt()
    {
        var garbage = Found(40000, 0, true, FinderMethod.ScreenTiles);
        var real = Found(50000, 12, false, FinderMethod.Structure);

        Assert.Equal([40000, 50000], SpectrumDumpScanner.Merge([garbage, real]).Select(f => f.Offset));
    }
}
