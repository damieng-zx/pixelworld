using PixelWorld.Imaging;

namespace PixelWorld.Tests;

public class GifTests
{
    /// <summary>A small GIF reader, just enough to check what the writer wrote.</summary>
    private sealed record Decoded(int Width, int Height, int Loop, List<(int Delay, Color[] Pixels)> Frames);

    private static Decoded Decode(byte[] gif)
    {
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(gif, 0, 6));
        var width = gif[6] | gif[7] << 8;
        var height = gif[8] | gif[9] << 8;
        var tableLength = 1 << ((gif[10] & 7) + 1);
        var at = 13;
        var table = new Color[tableLength];
        for (var i = 0; i < tableLength; i++, at += 3)
            table[i] = new Color(gif[at], gif[at + 1], gif[at + 2]);

        var loop = -1;
        var delay = 0;
        var frames = new List<(int, Color[])>();
        while (gif[at] != 0x3B)
        {
            if (gif[at] == 0x21)
            {
                var label = gif[at + 1];
                at += 2;
                if (label == 0xF9)
                    delay = gif[at + 2] | gif[at + 3] << 8;
                if (label == 0xFF)
                    loop = gif[at + 12 + 1 + 1] | gif[at + 12 + 1 + 2] << 8;
                while (gif[at] != 0)
                    at += gif[at] + 1;
                at++;
            }
            else
            {
                Assert.Equal(0x2C, gif[at]);
                var minCodeSize = gif[at + 10];
                at += 11;
                var data = new List<byte>();
                while (gif[at] != 0)
                {
                    data.AddRange(gif.AsSpan(at + 1, gif[at]).ToArray());
                    at += gif[at] + 1;
                }

                at++;
                var indices = Unlzw(data.ToArray(), minCodeSize, width * height);
                frames.Add((delay, indices.Select(i => table[i]).ToArray()));
            }
        }

        return new Decoded(width, height, loop, frames);
    }

    private static byte[] Unlzw(byte[] data, int minCodeSize, int expected)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var dictionary = new List<byte[]>();

        void Reset()
        {
            dictionary.Clear();
            for (var i = 0; i < clear; i++)
                dictionary.Add([(byte)i]);
            dictionary.Add([]);
            dictionary.Add([]);
        }

        Reset();
        var output = new List<byte>();
        var codeSize = minCodeSize + 1;
        var bits = 0;
        var buffer = 0;
        var at = 0;
        byte[]? previous = null;
        while (true)
        {
            while (bits < codeSize)
            {
                buffer |= data[at++] << bits;
                bits += 8;
            }

            var code = buffer & ((1 << codeSize) - 1);
            buffer >>= codeSize;
            bits -= codeSize;

            if (code == clear)
            {
                Reset();
                codeSize = minCodeSize + 1;
                previous = null;
                continue;
            }

            if (code == end) break;

            byte[] entry;
            if (code < dictionary.Count)
                entry = dictionary[code];
            else
                entry = [..previous!, previous![0]]; // the one code that is not in the table yet

            output.AddRange(entry);
            if (previous is not null)
                dictionary.Add([..previous, entry[0]]);
            previous = entry;

            if (dictionary.Count == 1 << codeSize && codeSize < 12)
                codeSize++;
        }

        Assert.Equal(expected, output.Count);
        return output.ToArray();
    }

    private static byte[] Encode(IReadOnlyList<Bitmap> frames, int delay = 7)
    {
        var stream = new MemoryStream();
        Gif.Write(frames, delay, stream);
        return stream.ToArray();
    }

    private static void AssertFrame(Bitmap expected, Color[] actual)
    {
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
            Assert.True(expected[x, y] == actual[y * expected.Width + x], $"({x},{y})");
    }

    [Fact]
    public void Write_SingleFrame_RoundTripsAndDoesNotLoop()
    {
        var image = new Bitmap(11, 9);
        for (var y = 0; y < 9; y++)
        for (var x = 0; x < 11; x++)
            image[x, y] = (x + y) % 3 == 0 ? Color.Red : Color.Blue;

        var decoded = Decode(Encode([image]));

        Assert.Equal((11, 9), (decoded.Width, decoded.Height));
        Assert.Equal(-1, decoded.Loop);
        AssertFrame(image, Assert.Single(decoded.Frames).Pixels);
    }

    [Fact]
    public void Write_TwoFrames_KeepsBothWithTheirDelayAndLoopsForever()
    {
        var first = new Bitmap(16, 16);
        first.Fill(Color.Black);
        var second = new Bitmap(16, 16);
        second.Fill(Color.White);
        second[3, 3] = Color.Black;

        var decoded = Decode(Encode([first, second], 64));

        Assert.Equal(0, decoded.Loop);
        Assert.Equal(2, decoded.Frames.Count);
        Assert.All(decoded.Frames, f => Assert.Equal(64, f.Delay));
        AssertFrame(first, decoded.Frames[0].Pixels);
        AssertFrame(second, decoded.Frames[1].Pixels);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(200)]
    [InlineData(256)]
    public void Write_NoisyImageWithManyColours_RoundTrips(int colours)
    {
        // Random pixels barely compress, so this fills the LZW table many times over, forcing the code
        // width to grow to 12 bits and the table to be cleared and restarted.
        var random = new Random(colours);
        var image = new Bitmap(120, 90);
        var palette = Enumerable.Range(0, colours).Select(i => new Color((byte)i, (byte)(255 - i), (byte)(i * 7))).ToArray();
        for (var y = 0; y < 90; y++)
        for (var x = 0; x < 120; x++)
            image[x, y] = palette[random.Next(colours)];

        var decoded = Decode(Encode([image]));

        AssertFrame(image, Assert.Single(decoded.Frames).Pixels);
    }

    [Fact]
    public void Write_LongRunOfOneColour_RoundTrips()
    {
        var image = new Bitmap(256, 192);
        image.Fill(Color.Green);

        AssertFrame(image, Assert.Single(Decode(Encode([image])).Frames).Pixels);
    }

    [Fact]
    public void Write_MoreThan256Colours_Throws()
    {
        var image = new Bitmap(20, 20);
        for (var y = 0; y < 20; y++)
        for (var x = 0; x < 20; x++)
            image[x, y] = new Color((byte)x, (byte)y, 0);

        Assert.Throws<InvalidOperationException>(() => Encode([image]));
    }

    [Fact]
    public void Write_FramesOfDifferentSizes_Throws()
    {
        Assert.Throws<ArgumentException>(() => Encode([new Bitmap(4, 4), new Bitmap(5, 4)]));
    }

    [Fact]
    public void Write_NoFrames_Throws()
    {
        Assert.Throws<ArgumentException>(() => Encode([]));
    }
}

public class ColorTests
{
    [Theory]
    [InlineData("#071821", 0x07, 0x18, 0x21, 255)]
    [InlineData("86c06c", 0x86, 0xC0, 0x6C, 255)]
    [InlineData("#11223344", 0x11, 0x22, 0x33, 0x44)]
    public void ParseHex_ReadsRgbAndOptionalAlpha(string hex, int r, int g, int b, int a)
    {
        Assert.Equal(new Color((byte)r, (byte)g, (byte)b, (byte)a), Color.ParseHex(hex));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#zzzzzz")]
    public void ParseHex_BadInput_Throws(string hex)
    {
        Assert.Throws<FormatException>(() => Color.ParseHex(hex));
    }
}
