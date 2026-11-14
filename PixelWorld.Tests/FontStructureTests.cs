using PixelWorld.Machines;
using PixelWorld.OffsetFinders;

namespace PixelWorld.Tests;

public class FontStructureTests
{
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
        var buffer = TestData.Embed(TestData.TestFont(), 40000);

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
        var buffer = TestData.Embed(TestData.TestFont(), 40000);

        Assert.True(FontStructure.DetectRange(buffer, 40000).IsFull);
    }

    [Fact]
    public void DetectRange_DigitsAndCapitalsOnly_StartsAtZeroAndEndsAtZ()
    {
        var font = TestData.TestFont();
        var digitsToZ = font.AsSpan(('0' - 32) * 8, ('Z' - '0' + 1) * 8).ToArray();
        var buffer = TestData.Embed(digitsToZ, 40000);
        var fontBase = 40000 - ('0' - 32) * 8;

        var range = FontStructure.DetectRange(buffer, fontBase);

        Assert.Equal('0', range.First);
        Assert.Equal('Z', range.Last);
    }

    [Fact]
    public void Extract_PartialRange_BlanksOtherSlots()
    {
        var buffer = TestData.Embed(TestData.TestFont(), 40000);

        var data = FontStructure.Extract(buffer, 40000, new CharRange('A', 'Z'));

        Assert.All(data.AsSpan(0, ('A' - 32) * 8).ToArray(), b => Assert.Equal(0, b));
        Assert.Equal(buffer.AsSpan(40000 + ('A' - 32) * 8, 8).ToArray(), data.AsSpan(('A' - 32) * 8, 8).ToArray());
    }
}
