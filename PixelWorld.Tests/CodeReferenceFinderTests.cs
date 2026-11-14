using PixelWorld.Machines;
using PixelWorld.OffsetFinders;

namespace PixelWorld.Tests;

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
