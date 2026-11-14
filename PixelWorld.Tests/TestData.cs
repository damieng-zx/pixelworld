using PixelWorld.Machines;

namespace PixelWorld.Tests;

/// <summary>Fonts and memory images shared by the font-hunting tests.</summary>
internal static class TestData
{
    /// <summary>The ROM font slid one pixel left: just as font-like, but not a copy of the ROM.</summary>
    internal static byte[] TestFont() => Spectrum.RomFont.ToArray().Select(b => (byte)(b << 1)).ToArray();

    /// <summary>A memory image (64K by default) of seeded random bytes with <paramref name="font"/> copied in at <paramref name="at"/>.</summary>
    internal static byte[] Embed(byte[] font, int at, int size = 65536)
    {
        var buffer = new byte[size];
        var random = new Random(1);
        random.NextBytes(buffer);
        font.CopyTo(buffer, at);
        return buffer;
    }
}
