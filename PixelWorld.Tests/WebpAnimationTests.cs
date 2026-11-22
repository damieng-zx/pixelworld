using System.Buffers.Binary;
using System.Text;
using PixelWorld.Imaging;

namespace PixelWorld.Tests;

public class WebpAnimationTests
{
    private const byte AnimationFlag = 0x02;
    private const byte AlphaFlag = 0x10;
    private const byte DoNotBlend = 0x02;

    private static Color Shade(int i) => new((byte)(i * 16), (byte)(255 - i * 16), (byte)(i * 7));

    private static int Index(int x, int y) => (x / 4 + y / 3 * 5) % 16;

    private static Bitmap Make(int width, int height, Func<int, int, Color> pixel)
    {
        var image = new Bitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            image[x, y] = pixel(x, y);
        return image;
    }

    private static Bitmap Base() => Make(32, 24, (x, y) => Shade(Index(x, y)));

    /// <summary>A block in the middle swaps colours, as flashing text does, so the changes are packed together.</summary>
    private static Bitmap Dense() =>
        Make(32, 24, (x, y) => x is >= 8 and < 24 && y is >= 6 and < 18 ? Shade(15 - Index(x, y)) : Shade(Index(x, y)));

    /// <summary>Four pixels far apart change, so the rectangle round them is almost all unchanged.</summary>
    private static Bitmap Scattered()
    {
        var image = Base();
        foreach (var (x, y) in new[] { (1, 1), (30, 22), (15, 11), (2, 20) })
            image[x, y] = Color.Red;
        return image;
    }

    private static Bitmap SeeThrough()
    {
        var image = Base();
        image[5, 5] = new Color(255, 0, 0, 128);
        image[20, 15] = new Color(0, 0, 255, 128);
        return image;
    }

    private static byte[] Animate(IReadOnlyList<Bitmap> frames, int delay)
    {
        var stream = new MemoryStream();
        Webp.WriteAnimation(frames, delay, stream);
        return stream.ToArray();
    }

    private sealed record Frame(int X, int Y, int Width, int Height, int Duration, byte Flags);

    private sealed record Parsed(byte CanvasFlags, int CanvasWidth, int CanvasHeight, int LoopCount, List<Frame> Frames);

    private static int UInt24(byte[] data, int at) => data[at] | data[at + 1] << 8 | data[at + 2] << 16;

    /// <summary>Reads the chunks of an animated file, checking the container as it goes.</summary>
    private static Parsed Parse(byte[] webp)
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(webp, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(webp, 8, 4));
        Assert.Equal(webp.Length - 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(4)));

        byte canvasFlags = 0;
        int width = 0, height = 0, loop = -1;
        var frames = new List<Frame>();
        var order = new List<string>();
        for (var at = 12; at < webp.Length;)
        {
            var id = Encoding.ASCII.GetString(webp, at, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(at + 4));
            var data = webp[(at + 8)..(at + 8 + size)];
            order.Add(id);
            at += 8 + size + (size & 1); // chunks are padded to an even length
            Assert.True(at <= webp.Length, $"{id} runs past the end of the file");

            switch (id)
            {
                case "VP8X":
                    Assert.Equal(10, size);
                    canvasFlags = data[0];
                    Assert.Equal([0, 0, 0], data[1..4]); // reserved
                    width = UInt24(data, 4) + 1;
                    height = UInt24(data, 7) + 1;
                    break;
                case "ANIM":
                    Assert.Equal(6, size);
                    loop = data[4] | data[5] << 8;
                    break;
                case "ANMF":
                    Assert.Equal("VP8L", Encoding.ASCII.GetString(data, 16, 4));
                    var pictureSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20));
                    Assert.Equal(16 + 8 + pictureSize + (pictureSize & 1), size); // the frame header, then one padded picture chunk
                    frames.Add(new Frame(UInt24(data, 0) * 2, UInt24(data, 3) * 2, UInt24(data, 6) + 1, UInt24(data, 9) + 1, UInt24(data, 12), data[15]));
                    break;
            }
        }

        Assert.Equal(["VP8X", "ANIM"], order.Take(2));
        Assert.All(order.Skip(2), id => Assert.Equal("ANMF", id));
        return new Parsed(canvasFlags, width, height, loop, frames);
    }

    [Fact]
    public void WriteAnimation_DenseChange_MatchesVerifiedOutput()
    {
        var webp = Animate([Base(), Dense()], 100);

        Assert.Equal(DenseAnimationWebp, Convert.ToBase64String(webp));

        // The second frame is just the block that changed, drawn over the first.
        var parsed = Parse(webp);
        Assert.Equal(new Frame(0, 0, 32, 24, 100, DoNotBlend), parsed.Frames[0]);
        Assert.Equal(new Frame(8, 6, 16, 12, 100, DoNotBlend), parsed.Frames[1]);
        Assert.Equal(AnimationFlag, parsed.CanvasFlags);
    }

    [Fact]
    public void WriteAnimation_ScatteredChanges_BlendTransparentPixelsOverTheOldPicture()
    {
        var webp = Animate([Base(), Scattered()], 100);

        Assert.Equal(ScatteredAnimationWebp, Convert.ToBase64String(webp));

        // The unchanged pixels become transparent, so the frame needs alpha and blends on to the previous one.
        var parsed = Parse(webp);
        Assert.Equal(0, parsed.Frames[1].Flags & DoNotBlend);
        Assert.Equal(AnimationFlag | AlphaFlag, parsed.CanvasFlags);
    }

    [Fact]
    public void WriteAnimation_ChangesThatAreNotOpaque_AreNotBlended()
    {
        var webp = Animate([Base(), SeeThrough()], 100);

        Assert.Equal(SeeThroughAnimationWebp, Convert.ToBase64String(webp));

        // Blending a see-through pixel would mix it with the old one, so the frame is drawn over instead.
        Assert.Equal(DoNotBlend, Parse(webp).Frames[1].Flags);
    }

    [Fact]
    public void WriteAnimation_FramesTheSameAsTheOneBefore_AreMergedIntoIt()
    {
        var webp = Animate([Base(), Base(), Dense(), Dense()], 50);

        Assert.Equal(MergedAnimationWebp, Convert.ToBase64String(webp));

        var frames = Parse(webp).Frames;
        Assert.Equal([100, 100], frames.Select(f => f.Duration));
    }

    [Fact]
    public void WriteAnimation_NothingChanges_IsOneFrameForTheWholeTime()
    {
        var frames = Parse(Animate([Base(), Base(), Base()], 70)).Frames;

        Assert.Equal(new Frame(0, 0, 32, 24, 210, DoNotBlend), Assert.Single(frames));
    }

    [Fact]
    public void WriteAnimation_FramesTooLongToMerge_StayApart()
    {
        var frames = Parse(Animate([Base(), Base()], Webp.MaxFrameDuration)).Frames;

        Assert.Equal([Webp.MaxFrameDuration, Webp.MaxFrameDuration], frames.Select(f => f.Duration));
    }

    [Fact]
    public void WriteAnimation_SingleFrame_IsAnAnimationOfOne()
    {
        var parsed = Parse(Animate([Base()], 70));

        Assert.Equal(new Frame(0, 0, 32, 24, 70, DoNotBlend), Assert.Single(parsed.Frames));
        Assert.Equal((32, 24), (parsed.CanvasWidth, parsed.CanvasHeight));
    }

    [Fact]
    public void WriteAnimation_LoopsForever()
    {
        Assert.Equal(0, Parse(Animate([Base(), Dense()], 100)).LoopCount);
    }

    [Fact]
    public void WriteAnimation_FramesWithAlpha_SetTheAlphaFlagOnTheCanvas()
    {
        var transparent = Make(32, 24, (x, _) => x < 16 ? Color.Transparent : Color.Red);

        Assert.Equal(AnimationFlag | AlphaFlag, Parse(Animate([transparent, Base()], 100)).CanvasFlags);
    }

    [Fact]
    public void WriteAnimation_FramesAfterTheFirst_CoverEveryChangedPixelFromAnEvenPosition()
    {
        // Frames with changes at random places, which has to find the rectangle round them every time.
        var seed = 99u;
        int Next(int below)
        {
            seed = seed * 1664525u + 1013904223u;
            return (int)(seed >> 8) % below;
        }

        for (var trial = 0; trial < 40; trial++)
        {
            var before = Make(27, 19, (x, y) => Shade((x + y * 3) % 12));
            var after = Make(27, 19, (x, y) => before[x, y]);
            var changes = 1 + Next(5);
            for (var i = 0; i < changes; i++)
                after[Next(27), Next(19)] = Shade(13 + Next(3));

            var frames = Parse(Animate([before, after], 10)).Frames;
            if (Enumerable.Range(0, 19).All(y => Enumerable.Range(0, 27).All(x => before[x, y] == after[x, y])))
            {
                Assert.Single(frames);
                continue;
            }

            var frame = frames[1];
            Assert.Equal(0, frame.X % 2);
            Assert.Equal(0, frame.Y % 2);
            Assert.InRange(frame.X + frame.Width, 1, 27);
            Assert.InRange(frame.Y + frame.Height, 1, 19);
            for (var y = 0; y < 19; y++)
            for (var x = 0; x < 27; x++)
            {
                var inside = x >= frame.X && x < frame.X + frame.Width && y >= frame.Y && y < frame.Y + frame.Height;
                Assert.True(inside || before[x, y] == after[x, y], $"trial {trial}: ({x},{y}) changed outside {frame}");
            }
        }
    }

    [Fact]
    public void WriteAnimation_NoFrames_Throws()
    {
        Assert.Throws<ArgumentException>(() => Animate([], 100));
    }

    [Fact]
    public void WriteAnimation_FramesOfDifferentSizes_Throws()
    {
        Assert.Throws<ArgumentException>(() => Animate([new Bitmap(4, 4), new Bitmap(5, 4)], 100));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(Webp.MaxFrameDuration + 1)]
    public void WriteAnimation_DurationOutOfRange_Throws(int delay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Animate([new Bitmap(4, 4)], delay));
    }

    [Fact]
    public void WriteAnimation_TooLarge_Throws()
    {
        Assert.Throws<ArgumentException>(() => Animate([new Bitmap(16385, 1)], 100));
    }

    // Files this encoder wrote, checked by libwebp (through Pillow, which composes the frames) to show each
    // moment of the animation came back pixel for pixel, and by webpinfo, which validates the container.
    private const string DenseAnimationWebp = "UklGRiQBAABXRUJQVlA4WAoAAAACAAAAHwAAFwAAQU5JTQYAAAAAAAAAAABBTk1GggAAAAAAAAAAAB8AABcAAGQAAAJWUDhMagAAAC8fwAUAfyAWTPxtM+icwkDMwfznbAJqCnCSQgoppJBCCimkkEIKKaQwhSlMYR/teu8MIvpPJGwyzwUxUsq5lFpb632MOXlxt9be59wrwos7VTP3V+CF3TfKwz8wysM/MMrDPzDKwz8wygNBTk1GbgAAAAQAAAMAAA8AAAsAAGQAAAJWUDhMVgAAAC8PwAIAZ0AQQKIwQv4yM30SAoFEaX+aEEYQEhArHPQ/adr8L0Jj3kB/xIdBBkxSSCGFFFJIIYUUUkghhRTCartCRP+HgYhZRBUzc4/IxKq6Z3axu59i";
    private const string ScatteredAnimationWebp = "UklGRvYAAABXRUJQVlA4WAoAAAASAAAAHwAAFwAAQU5JTQYAAAAAAAAAAABBTk1GggAAAAAAAAAAAB8AABcAAGQAAAJWUDhMagAAAC8fwAUAfyAWTPxtM+icwkDMwfznbAJqCnCSQgoppJBCCimkkEIKKaQwhSlMYR/teu8MIvpPJGwyzwUxUsq5lFpb632MOXlxt9be59wrwos7VTP3V+CF3TfKwz8wysM/MMrDPzDKwz8wygNBTk1GQAAAAAAAAAAAAB4AABYAAGQAAABWUDhMJwAAAC8egAUQDxDzH/MfDDINCDPBRJPd4ZAgov9CG2IUyR8orCfymGq1AQA=";
    private const string MergedAnimationWebp = "UklGRiQBAABXRUJQVlA4WAoAAAACAAAAHwAAFwAAQU5JTQYAAAAAAAAAAABBTk1GggAAAAAAAAAAAB8AABcAAGQAAAJWUDhMagAAAC8fwAUAfyAWTPxtM+icwkDMwfznbAJqCnCSQgoppJBCCimkkEIKKaQwhSlMYR/teu8MIvpPJGwyzwUxUsq5lFpb632MOXlxt9be59wrwos7VTP3V+CF3TfKwz8wysM/MMrDPzDKwz8wygNBTk1GbgAAAAQAAAMAAA8AAAsAAGQAAAJWUDhMVgAAAC8PwAIAZ0AQQKIwQv4yM30SAoFEaX+aEEYQEhArHPQ/adr8L0Jj3kB/xIdBBkxSSCGFFFJIIYUUUkghhRTCartCRP+HgYhZRBUzc4/IxKq6Z3axu59i";
    private const string SeeThroughAnimationWebp = "UklGRjYBAABXRUJQVlA4WAoAAAASAAAAHwAAFwAAQU5JTQYAAAAAAAAAAABBTk1GggAAAAAAAAAAAB8AABcAAGQAAAJWUDhMagAAAC8fwAUAfyAWTPxtM+icwkDMwfznbAJqCnCSQgoppJBCCimkkEIKKaQwhSlMYR/teu8MIvpPJGwyzwUxUsq5lFpb632MOXlxt9be59wrwos7VTP3V+CF3TfKwz8wysM/MMrDPzDKwz8wygNBTk1GgAAAAAIAAAIAABAAAAsAAGQAAAJWUDhMZwAAAC8QwAIQj0CgYRHRJDQNn4ESBIEgChk+4huSBZicc8klvlxiGQyBNsmbO5K7C2kOTPP07g8DnGdQ1EBsMuiY/avdfZpUQUT/BQLJGYzh4BEQIS6pZRRUNHSLBiYWNo5FF8oDi+RTpjwA";
}
