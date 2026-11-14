using PixelWorld.OffsetFinders;

namespace PixelWorld.Tests;

public class StructuralFinderTests
{
    [Fact]
    public void FindOffsets_FindsEmbeddedFont()
    {
        var buffer = TestData.Embed(TestData.TestFont(), 40000);

        Assert.Contains(40000, StructuralFinder.FindOffsets(buffer));
    }

    [Fact]
    public void FindOffsets_RandomData_ReturnsEmpty()
    {
        var buffer = new byte[65536];
        new Random(3).NextBytes(buffer);

        Assert.Empty(StructuralFinder.FindOffsets(buffer));
    }

    private static FontScore Score(int total, int distinct, int penalty = 0) =>
        new(Mirror: total, Tops: 0, Symmetric: 0, Descenders: 0, Gutter: 0, DistinctLetters: distinct, Penalty: penalty, EdgeInk: 0, CharRange.Full);

    [Theory]
    [InlineData(12, 22, 0, true)]
    [InlineData(11, 22, 0, false)] // under the usual minimum and too few distinct letters to be clean
    [InlineData(8, 25, 0, true)] // clean and nearly every capital different
    [InlineData(8, 25, 1, false)] // something counts against it
    [InlineData(8, 24, 0, false)]
    [InlineData(7, 26, 0, false)]
    [InlineData(12, 21, 0, false)]
    public void Accepts_UsesTheLowerMinimumOnlyForCleanScores(int total, int distinct, int penalty, bool expected)
    {
        Assert.Equal(expected, StructuralFinder.Accepts(Score(total, distinct, penalty)));
    }
}
