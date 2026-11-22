using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelWorld.Imaging;

/// <summary>
/// Writes lossless WebP files (the VP8L format), still or animated. Pictures with 256 colours or fewer, which
/// is every Spectrum screen, are stored as a palette with several pixels packed into each symbol; everything
/// is then compressed with LZ77 back references and Huffman codes.
/// </summary>
public static class Webp
{
    /// <summary>The largest width or height a lossless WebP can have.</summary>
    public const Int32 MaxDimension = 1 << 14;

    /// <summary>The longest a frame of an animation can show for, in milliseconds (a 24 bit field).</summary>
    public const Int32 MaxFrameDuration = (1 << 24) - 1;

    private const Int32 ChunkHeaderSize = 8;
    private const Byte AnimationFlag = 0x02;
    private const Byte AlphaFlag = 0x10;
    private const Byte DoNotBlend = 0x02;

    public static void Write(Bitmap image, Stream output)
    {
        CheckSize(image);

        var chunk = Chunk("VP8L", Vp8lEncoder.Encode(image));
        WriteRiff(output, chunk);
    }

    /// <summary>
    /// Write same-sized frames as an animation that loops forever, each frame showing for the same time. After
    /// the first, a frame holds only the part of the picture that changed, so a screen whose flashing text
    /// toggles costs little more than the still.
    /// </summary>
    /// <param name="delay">How long each frame shows, in milliseconds. A frame the same as the one before it is
    /// not stored again; the earlier frame shows for the time of both.</param>
    public static void WriteAnimation(IReadOnlyList<Bitmap> frames, Int32 delay, Stream output)
    {
        if (frames.Count == 0) throw new ArgumentException("An animation needs at least one frame", nameof(frames));
        if (frames.Any(f => f.Width != frames[0].Width || f.Height != frames[0].Height))
            throw new ArgumentException("All frames must be the same size", nameof(frames));
        ArgumentOutOfRangeException.ThrowIfNegative(delay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, MaxFrameDuration);
        CheckSize(frames[0]);

        var width = frames[0].Width;
        var height = frames[0].Height;
        var hasAlpha = frames.Any(f => Enumerable.Range(0, height).Any(y => Enumerable.Range(0, width).Any(x => f[x, y].A != 255)));

        // The animation header: a background colour (blue, green, red, alpha) and the loop count, 0 for forever.
        var animation = new Byte[6];

        // A frame no different from the one before it is not worth storing: that one just shows for longer.
        var shown = new List<(Bitmap Picture, Int32 Duration)> { (frames[0], delay) };
        foreach (var frame in frames.Skip(1))
        {
            var (previous, duration) = shown[^1];
            if (Same(previous, frame) && duration + delay <= MaxFrameDuration)
                shown[^1] = (previous, duration + delay);
            else
                shown.Add((frame, delay));
        }

        var frameChunks = new List<Byte[]>();
        for (var i = 0; i < shown.Count; i++)
        {
            var (chunk, blended) = i == 0
                ? (Frame(shown[i].Picture, 0, 0, shown[i].Duration, DoNotBlend), false)
                : ChangesFrame(shown[i - 1].Picture, shown[i].Picture, shown[i].Duration);
            hasAlpha |= blended;
            frameChunks.Add(chunk);
        }

        // The canvas header: flags, three reserved bytes, then the canvas size minus one in 24 bits each.
        var canvas = new Byte[10];
        canvas[0] = (Byte)(AnimationFlag | (hasAlpha ? AlphaFlag : 0));
        WriteUInt24(canvas, 4, width - 1);
        WriteUInt24(canvas, 7, height - 1);

        WriteRiff(output, [..Chunk("VP8X", canvas), ..Chunk("ANIM", animation), ..frameChunks.SelectMany(c => c)]);
    }

    /// <summary>
    /// A frame that follows another holds only the part of the picture that changed. That can be the rectangle
    /// of changes drawn over the old picture, or the same rectangle with every pixel that did not change made
    /// transparent and blended on, which is smaller when the changes are spread out (flashing text, say) so
    /// that the rectangle is mostly unchanged pixels. Whichever is smaller is used.
    /// </summary>
    private static (Byte[] Chunk, Boolean Blended) ChangesFrame(Bitmap before, Bitmap after, Int32 delay)
    {
        var (x, y, width, height) = ChangedArea(before, after);
        var overwrite = Frame(Crop(after, x, y, width, height), x, y, delay, DoNotBlend);

        // Blending is only right when every pixel that changed is opaque; a see-through one would mix with the old.
        var blend = new Bitmap(width, height);
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
        {
            var now = after[x + column, y + row];
            if (now == before[x + column, y + row])
                blend[column, row] = Color.Transparent;
            else if (now.A != 255)
                return (overwrite, false);
            else
                blend[column, row] = now;
        }

        var blended = Frame(blend, x, y, delay, 0);
        return blended.Length < overwrite.Length ? (blended, true) : (overwrite, false);
    }

    /// <summary>
    /// A frame: X and Y (halved, which is why they must be even), the size minus one, the duration and the
    /// blending flag, then the picture. Each of those but the last is 24 bits.
    /// </summary>
    private static Byte[] Frame(Bitmap picture, Int32 x, Int32 y, Int32 delay, Byte flags)
    {
        var frame = new List<Byte>();
        foreach (var value in new[] { x / 2, y / 2, picture.Width - 1, picture.Height - 1, delay })
        {
            var field = new Byte[3];
            WriteUInt24(field, 0, value);
            frame.AddRange(field);
        }

        frame.Add(flags);
        frame.AddRange(Chunk("VP8L", Vp8lEncoder.Encode(picture)));
        return Chunk("ANMF", [..frame]);
    }

    private static Boolean Same(Bitmap a, Bitmap b)
    {
        for (var y = 0; y < a.Height; y++)
        for (var x = 0; x < a.Width; x++)
            if (a[x, y] != b[x, y])
                return false;
        return true;
    }

    private static void CheckSize(Bitmap image)
    {
        if (image.Width > MaxDimension || image.Height > MaxDimension)
            throw new ArgumentException($"The image is too large for a WebP, which allows {MaxDimension} pixels either way", nameof(image));
    }

    /// <summary>
    /// The smallest rectangle holding every pixel that differs. A frame's position has to be even, so the
    /// rectangle grows left and up to the next even pixel; one pixel stands in when nothing changed.
    /// </summary>
    private static (Int32 X, Int32 Y, Int32 Width, Int32 Height) ChangedArea(Bitmap before, Bitmap after)
    {
        Int32 left = after.Width, top = after.Height, right = -1, bottom = -1;
        for (var y = 0; y < after.Height; y++)
        for (var x = 0; x < after.Width; x++)
        {
            if (before[x, y] == after[x, y]) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        if (right < 0)
            return (0, 0, 1, 1);

        left &= ~1;
        top &= ~1;
        return (left, top, right - left + 1, bottom - top + 1);
    }

    private static Bitmap Crop(Bitmap image, Int32 x, Int32 y, Int32 width, Int32 height)
    {
        if (x == 0 && y == 0 && width == image.Width && height == image.Height)
            return image;

        var cropped = new Bitmap(width, height);
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
            cropped[column, row] = image[x + column, y + row];
        return cropped;
    }

    private static void WriteUInt24(Byte[] buffer, Int32 offset, Int32 value)
    {
        buffer[offset] = (Byte)value;
        buffer[offset + 1] = (Byte)(value >> 8);
        buffer[offset + 2] = (Byte)(value >> 16);
    }

    /// <summary>A chunk is its four character name, its size, its data, and a zero if the size is odd.</summary>
    private static Byte[] Chunk(String name, Byte[] data)
    {
        var padding = data.Length & 1;
        var chunk = new Byte[ChunkHeaderSize + data.Length + padding];
        System.Text.Encoding.ASCII.GetBytes(name, chunk);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (UInt32)data.Length);
        data.CopyTo(chunk, ChunkHeaderSize);
        return chunk;
    }

    private static void WriteRiff(Stream output, Byte[] chunks)
    {
        Span<Byte> size = stackalloc Byte[4];
        output.Write("RIFF"u8);
        BinaryPrimitives.WriteUInt32LittleEndian(size, (UInt32)(4 + chunks.Length));
        output.Write(size);
        output.Write("WEBP"u8);
        output.Write(chunks);
    }
}
