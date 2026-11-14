using System.Buffers.Binary;
using PixelWorld.Imaging;

namespace PixelWorld.Tests;

public class PngTests
{
    // 7x5 images written by an independent encoder, so the reader is checked against more than this
    // project's own writer. The values of each pixel follow the formulas in the tests below.
    private const string Gray1 =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFAQAAAACh4ct1AAAAAnRSTlMAAQGU/a4AAAASSURBVHicYwhhXMW0irmBZRUADUgC3RexPfUAAAAASUVORK5CYII=";

    private const string Gray16 =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFEAAAAAD8YXVHAAAAQElEQVR4nGNgYFDWcAvIrOhZsP7EpQ+MTHuUNVQ0lKGQiXkP8x4mIGSGQLYfwp/AsEi4SKSIBSiuwfwEApn2AAB60BjKOLXzdgAAAABJRU5ErkJggg==";

    private const string Rgb8Interlaced =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAIAAAFx/1EZAAAAV0lEQVR4nF2LoRHAMAwDlcLgYONgYeHi4GAP4UkySYbIWPX1iqp7pPsHcoEOnB5nQGgbKjp6T2/yHq7tA4Y6YQss5pZecqXwAVZjE22yB7mozbtgWWY/HjcTFKZc0OqpAAAAAElFTkSuQmCC";

    private const string Rgb16 =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFEAIAAABWaL3MAAAAbUlEQVR4nGNgAANlDQYG/gVuAQwM8g6ZFQwMeg96FjAw2DWsP8HA4Kdw6QMDQ+wBRpAw/wKQUoEFEA0INqoIEwOD/gOBBSCSfwGCjU2EmYEhzkH+gJCIxAeBBQiSfwGmCAtMH8gKEMmPxEYVAQDyDzD3Atf0fwAAAABJRU5ErkJggg==";

    private const string Indexed4Trns =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFBAMAAAB7tOvrAAAADFBMVEX/AAAA/wAAAP8KFB4iiCkEAAAAAnRSTlP/gAgPs2oAAAAiSURBVHicY2BUZlRgFJJ7JMckeFHwArP8L7EfLBeVLsoDAETHB00lwyTCAAAAAElFTkSuQmCC";

    private const string Indexed2Interlaced =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFAgMAAAGD8y7dAAAADFBMVEUBAgMEBQYHCAkKCwzcYzesAAAAF0lEQVR4nGNggABGCLmCKYJBWoLR8j8ACu0CcT0TeTIAAAAASUVORK5CYII=";

    private const string GrayAlpha8 =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAQAAAAjkz5TAAAALUlEQVR4nGNgYJBjsGGIYqhgmMawhYGRwUiOAQGZGIyQITNDCr8kArIAxZAAACZMBktGvwigAAAAAElFTkSuQmCC";

    private const string Rgba8Interlaced =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAYAAAH+ncZOAAAAd0lEQVR4nG3MrQHEIAwF4EdlNToaHR19uro6Q7ABG2A6BCfYoIYNuB24HVJqqio+9X4AwBARCtCCxbYVCHyvkOSkSZ/RF1BvoqFHlVJ1SyCsYwedGXw4Uhpz87sts4oHeDViP4Sp7xzOyFwyy1H5kxwy2fz5v7kAJucus26spR0AAAAASUVORK5CYII=";

    private const string Rgb8Paeth =
        "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAIAAAAG+GGPAAAAGklEQVR4nGNhYGCQY+BCQywMRlwMDOiIclEAueYC5aIyYPIAAAAASUVORK5CYII=";

    private static Bitmap Read(string base64) => Png.Read(new MemoryStream(Convert.FromBase64String(base64)));

    private static void AssertEveryPixel(Bitmap image, Func<int, int, Color> expected)
    {
        Assert.Equal(7, image.Width);
        Assert.Equal(5, image.Height);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            Assert.True(expected(x, y) == image[x, y], $"({x},{y}) expected {expected(x, y)} but was {image[x, y]}");
    }

    [Fact]
    public void Read_OneBitGreyscaleWithTransparentSample()
    {
        AssertEveryPixel(Read(Gray1), (x, y) => (x + y) % 2 == 1 ? new Color(255, 255, 255, 0) : Color.Black);
    }

    [Fact]
    public void Read_SixteenBitGreyscale_KeepsTheHighByte()
    {
        AssertEveryPixel(Read(Gray16), (x, y) =>
        {
            var g = (byte)(((x * 9000 + y * 700) % 65536) >> 8);
            return new Color(g, g, g);
        });
    }

    [Fact]
    public void Read_InterlacedTruecolor()
    {
        AssertEveryPixel(Read(Rgb8Interlaced), (x, y) => new Color((byte)(x * 30), (byte)(y * 50), (byte)((x + y) * 10)));
    }

    [Fact]
    public void Read_SixteenBitTruecolor()
    {
        AssertEveryPixel(Read(Rgb16), (x, y) => new Color((byte)((x * 9000) >> 8), (byte)((y * 12000) >> 8), (byte)(((x + y) * 4000) >> 8)));
    }

    [Fact]
    public void Read_PaethFilteredTruecolor()
    {
        AssertEveryPixel(Read(Rgb8Paeth), (x, y) => new Color((byte)(x * 30), (byte)(y * 50), (byte)((x + y) * 10)));
    }

    [Fact]
    public void Read_FourBitIndexedWithTransparencyChunk()
    {
        Color[] palette = [new(255, 0, 0), new(0, 255, 0, 128), new(0, 0, 255), new(10, 20, 30)];
        AssertEveryPixel(Read(Indexed4Trns), (x, y) => palette[(x + y) % 4]);
    }

    [Fact]
    public void Read_InterlacedTwoBitIndexed()
    {
        Color[] palette = [new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12)];
        AssertEveryPixel(Read(Indexed2Interlaced), (x, y) => palette[x * y % 4]);
    }

    [Fact]
    public void Read_GreyscaleWithAlpha()
    {
        AssertEveryPixel(Read(GrayAlpha8), (x, y) => new Color((byte)(x * 30), (byte)(x * 30), (byte)(x * 30), (byte)(y * 50)));
    }

    [Fact]
    public void Read_InterlacedTruecolorWithAlpha()
    {
        AssertEveryPixel(Read(Rgba8Interlaced), (x, y) => new Color((byte)(x * 30), (byte)(y * 50), (byte)((x + y) * 10), (byte)(255 - x * 20)));
    }

    [Fact]
    public void Read_NotAPng_Throws()
    {
        Assert.Throws<InvalidDataException>(() => Png.Read(new MemoryStream("GIF89a not a png at all"u8.ToArray())));
    }

    [Fact]
    public void Read_TruncatedImageData_Throws()
    {
        var bytes = Convert.FromBase64String(Rgb8Paeth);
        var cut = bytes.AsSpan(0, bytes.Length - 20).ToArray();

        Assert.ThrowsAny<Exception>(() => Png.Read(new MemoryStream(cut)));
    }

    private static Bitmap RoundTrip(Bitmap image, out byte[] written)
    {
        var stream = new MemoryStream();
        Png.Write(image, stream);
        written = stream.ToArray();
        return Png.Read(new MemoryStream(written));
    }

    private static void AssertSame(Bitmap expected, Bitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
            Assert.True(expected[x, y] == actual[x, y], $"({x},{y})");
    }

    // The IHDR bit depth and colour type sit at fixed offsets: signature (8), length (4), type (4), then width and height (8).
    private static (int BitDepth, int ColorType) Header(byte[] png) => (png[24], png[25]);

    [Fact]
    public void Write_TwoColours_IsOneBitIndexedAndRoundTrips()
    {
        var image = new Bitmap(13, 7);
        for (var y = 0; y < 7; y++)
        for (var x = 0; x < 13; x++)
            image[x, y] = x * y % 3 == 0 ? Color.Black : Color.White;

        AssertSame(image, RoundTrip(image, out var written));
        Assert.Equal((1, 3), Header(written));
    }

    [Fact]
    public void Write_TransparentBackground_KeepsAlpha()
    {
        var image = new Bitmap(9, 9);
        image.Fill(Color.Transparent);
        image[4, 4] = Color.Black;

        AssertSame(image, RoundTrip(image, out var written));
        Assert.Equal((1, 3), Header(written));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 4)]
    [InlineData(16, 4)]
    [InlineData(17, 8)]
    [InlineData(256, 8)]
    public void Write_ChoosesTheSmallestIndexedBitDepth(int colours, int expectedDepth)
    {
        var image = new Bitmap(colours, 3);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < colours; x++)
            image[x, y] = new Color((byte)x, (byte)(x / 7), 0);

        AssertSame(image, RoundTrip(image, out var written));
        Assert.Equal((expectedDepth, 3), Header(written));
    }

    [Fact]
    public void Write_MoreThan256Colours_IsTruecolor()
    {
        var image = new Bitmap(20, 20);
        for (var y = 0; y < 20; y++)
        for (var x = 0; x < 20; x++)
            image[x, y] = new Color((byte)(x * 12), (byte)(y * 12), (byte)(x + y));

        AssertSame(image, RoundTrip(image, out var written));
        Assert.Equal((8, 2), Header(written));
    }

    [Fact]
    public void Write_MoreThan256ColoursWithAlpha_IsTruecolorWithAlpha()
    {
        var image = new Bitmap(20, 20);
        for (var y = 0; y < 20; y++)
        for (var x = 0; x < 20; x++)
            image[x, y] = new Color((byte)(x * 12), (byte)(y * 12), (byte)(x + y), (byte)(255 - x));

        AssertSame(image, RoundTrip(image, out var written));
        Assert.Equal((8, 6), Header(written));
    }

    [Fact]
    public void Write_Chunks_HaveValidCrcs()
    {
        var image = new Bitmap(5, 5);
        image[2, 2] = Color.Red;
        var stream = new MemoryStream();
        Png.Write(image, stream);
        var png = stream.ToArray();

        var at = 8;
        var chunks = 0;
        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(Crc32Of(png.AsSpan(at + 4, 4 + length)), crc);
            at += 12 + length;
            chunks++;
        }

        Assert.True(chunks >= 3);
    }

    private static uint Crc32Of(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }
}
