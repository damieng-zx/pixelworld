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
}
