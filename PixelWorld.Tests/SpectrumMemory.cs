namespace PixelWorld.Tests;

/// <summary>Helpers for tests of snapshot loaders, which all produce the same layout of RAM pages.</summary>
public static class SpectrumMemory
{
    public const int PageSize = 0x4000;

    /// <summary>The contents of RAM page n, different for every page and every position in it.</summary>
    public static byte[] Page(int number)
    {
        var page = new byte[PageSize];
        for (var i = 0; i < page.Length; i++)
            page[i] = (byte)(i * 7 + number * 31 + 1);
        return page;
    }

    public static void AssertPageAt(ArraySegment<byte> memory, int address, int page)
    {
        Assert.Equal(Page(page), memory.Array!.AsSpan(memory.Offset + address, PageSize).ToArray());
    }

    public static void AssertBlank(ArraySegment<byte> memory, int address)
    {
        Assert.All(memory.Array!.AsSpan(memory.Offset + address, PageSize).ToArray(), b => Assert.Equal(0, b));
    }

    /// <summary>What an undecodable file gives: a default segment, which cannot even be enumerated.</summary>
    public static void AssertNothing(ArraySegment<byte> memory) => Assert.Equal(0, memory.Count);
}
