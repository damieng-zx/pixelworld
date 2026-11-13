using PixelWorld.DumpScanners;
using PixelWorld.Machines;
using PixelWorld.OffsetFinders;

namespace PixelWorld.Tests;

public class FontStructureTests
{
    /// <summary>The ROM font slid one pixel left: just as font-like, but not a copy of the ROM.</summary>
    internal static byte[] TestFont() => Spectrum.RomFont.ToArray().Select(b => (byte)(b << 1)).ToArray();

    internal static byte[] Embed(byte[] font, int at, int size = 65536)
    {
        var buffer = new byte[size];
        var random = new Random(1);
        random.NextBytes(buffer);
        font.CopyTo(buffer, at);
        return buffer;
    }

    [Fact]
    public void Score_RomFont_IsHigh()
    {
        var score = FontStructure.Score(Spectrum.RomFont.ToArray(), 0);

        Assert.True(score.Total >= StructuralFinder.MinScore, score.ToString());
        Assert.Equal(0, score.Penalty);
        Assert.Equal(26, score.DistinctLetters);
    }

    [Fact]
    public void Score_RandomData_IsLow()
    {
        var buffer = new byte[768];
        new Random(7).NextBytes(buffer);

        Assert.True(FontStructure.Score(buffer, 0).Total < CodeReferenceFinder.MinScore);
    }

    [Fact]
    public void Score_FontReadOneCharacterOff_IsLowerThanAligned()
    {
        var buffer = Embed(TestFont(), 40000);

        var aligned = FontStructure.Score(buffer, 40000).Total;
        var shifted = FontStructure.Score(buffer, 40008).Total;

        Assert.True(aligned > shifted + 5, $"aligned {aligned}, shifted {shifted}");
    }

    [Fact]
    public void IsMirrorOf_Brackets_IgnoresHorizontalPosition()
    {
        byte[] buffer =
        [
            0x00, 0x08, 0x10, 0x10, 0x10, 0x10, 0x08, 0x00, // ( towards the left
            0x00, 0x40, 0x20, 0x20, 0x20, 0x20, 0x40, 0x00 // ) further right
        ];

        Assert.True(FontStructure.IsMirrorOf(buffer, 0, 8));
    }

    [Fact]
    public void IsSymmetric_SingleVerticalLine_IsNotCounted()
    {
        byte[] buffer = [0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01];

        Assert.False(FontStructure.IsSymmetric(buffer, 0));
    }

    [Fact]
    public void DetectRange_CompleteFont_IsFull()
    {
        var buffer = Embed(TestFont(), 40000);

        Assert.True(FontStructure.DetectRange(buffer, 40000).IsFull);
    }

    [Fact]
    public void DetectRange_DigitsAndCapitalsOnly_StartsAtZeroAndEndsAtZ()
    {
        var font = TestFont();
        var digitsToZ = font.AsSpan(('0' - 32) * 8, ('Z' - '0' + 1) * 8).ToArray();
        var buffer = Embed(digitsToZ, 40000);
        var fontBase = 40000 - ('0' - 32) * 8;

        var range = FontStructure.DetectRange(buffer, fontBase);

        Assert.Equal('0', range.First);
        Assert.Equal('Z', range.Last);
    }

    [Fact]
    public void Extract_PartialRange_BlanksOtherSlots()
    {
        var buffer = Embed(TestFont(), 40000);

        var data = FontStructure.Extract(buffer, 40000, new CharRange('A', 'Z'));

        Assert.All(data.AsSpan(0, ('A' - 32) * 8).ToArray(), b => Assert.Equal(0, b));
        Assert.Equal(buffer.AsSpan(40000 + ('A' - 32) * 8, 8).ToArray(), data.AsSpan(('A' - 32) * 8, 8).ToArray());
    }
}

public class StructuralFinderTests
{
    [Fact]
    public void FindOffsets_FindsEmbeddedFont()
    {
        var buffer = FontStructureTests.Embed(FontStructureTests.TestFont(), 40000);

        Assert.Contains(40000, StructuralFinder.FindOffsets(buffer));
    }

    [Fact]
    public void FindOffsets_RandomData_ReturnsEmpty()
    {
        var buffer = new byte[65536];
        new Random(3).NextBytes(buffer);

        Assert.Empty(StructuralFinder.FindOffsets(buffer));
    }
}

public class CodeReferenceFinderTests
{
    [Fact]
    public void FindOffsets_CharsSetFromHl_ReturnsAddressPlus256()
    {
        var buffer = new byte[65536];
        // LD HL,39744 ; LD (23606),HL
        byte[] code = [0x21, 0x40, 0x9B, 0x22, 0x36, 0x5C];
        code.CopyTo(buffer, 30000);

        Assert.Contains(39744 + 256, CodeReferenceFinder.FindOffsets(buffer));
    }

    [Fact]
    public void FindOffsets_PrintRoutine_ReturnsConstantReadings()
    {
        var buffer = new byte[65536];
        // LD L,A ; LD H,0 ; ADD HL,HL x3 ; LD DE,40000 ; ADD HL,DE
        byte[] code = [0x6F, 0x26, 0x00, 0x29, 0x29, 0x29, 0x11, 0x40, 0x9C, 0x19];
        code.CopyTo(buffer, 30000);

        var offsets = CodeReferenceFinder.FindOffsets(buffer);

        Assert.Contains(40000, offsets); // code - 32
        Assert.Contains(40000 + 256, offsets); // code used directly
    }

    [Fact]
    public void FindOffsets_48KDump_MapsAddressToOffset()
    {
        var buffer = new byte[Spectrum.Ram48K];
        byte[] code = [0x21, 0x40, 0x9B, 0x22, 0x36, 0x5C];
        code.CopyTo(buffer, 10000);

        Assert.Contains(39744 + 256 - 16384, CodeReferenceFinder.FindOffsets(buffer));
    }
}
