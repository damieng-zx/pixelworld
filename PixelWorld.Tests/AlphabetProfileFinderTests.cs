using PixelWorld.OffsetFinders;

namespace PixelWorld.Tests;

public class AlphabetProfileFinderTests
{
    private static byte[] Capitals() => TestData.TestFont().AsSpan(('A' - 32) * 8, 26 * 8).ToArray();

    [Fact]
    public void FindOffsets_CapitalsAloneInMemory_ReturnsWhereSpaceWouldBe()
    {
        var buffer = TestData.Embed(Capitals(), 50000);

        Assert.Contains(50000 - ('A' - 32) * 8, AlphabetProfileFinder.FindOffsets(buffer));
    }

    [Fact]
    public void FindOffsets_CapitalsInAnyOrderOfInk_AreNotFound()
    {
        // The same 26 glyphs sorted so that ink rises steadily from A to Z do not follow the profile.
        var glyphs = Enumerable.Range(0, 26)
            .Select(i => Capitals().AsSpan(i * 8, 8).ToArray())
            .OrderBy(g => g.Sum(b => System.Numerics.BitOperations.PopCount(b)))
            .SelectMany(g => g)
            .ToArray();
        var buffer = TestData.Embed(glyphs, 50000);

        Assert.DoesNotContain(50000 - ('A' - 32) * 8, AlphabetProfileFinder.FindOffsets(buffer));
    }

    [Fact]
    public void FindOffsets_RandomData_ReturnsEmpty()
    {
        var buffer = new byte[65536];
        new Random(5).NextBytes(buffer);

        Assert.Empty(AlphabetProfileFinder.FindOffsets(buffer));
    }
}
