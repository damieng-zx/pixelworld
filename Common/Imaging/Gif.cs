using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelWorld.Imaging;

/// <summary>Writes GIF files.</summary>
public static class Gif
{
    private const Int32 MaxCodeBits = 12;
    private const Int32 MaxCodes = 1 << MaxCodeBits;

    /// <summary>
    /// Write one or more same-sized frames as a GIF, looping forever if there is more than one. All the
    /// frames share one palette, so together they can use at most 256 colours. Alpha is ignored.
    /// </summary>
    /// <param name="delay">How long each frame shows, in hundredths of a second.</param>
    public static void Write(IReadOnlyList<Bitmap> frames, Int32 delay, Stream output)
    {
        if (frames.Count == 0) throw new ArgumentException("A GIF needs at least one frame", nameof(frames));
        if (frames.Any(f => f.Width != frames[0].Width || f.Height != frames[0].Height))
            throw new ArgumentException("All frames must be the same size", nameof(frames));
        if (frames[0].Width > UInt16.MaxValue || frames[0].Height > UInt16.MaxValue)
            throw new ArgumentException("The image is too large for a GIF", nameof(frames));

        var palette = new Dictionary<(Byte R, Byte G, Byte B), Int32>();
        foreach (var frame in frames)
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
        {
            var c = frame[x, y];
            if (palette.TryAdd((c.R, c.G, c.B), palette.Count) && palette.Count > 256)
                throw new InvalidOperationException("A GIF can use at most 256 colours");
        }

        // The colour table is a power of two long, and the LZW codes start one bit wider than an index.
        var tableBits = 1;
        while (1 << tableBits < palette.Count)
            tableBits++;
        var codeBits = Math.Max(2, tableBits);

        var width = frames[0].Width;
        var height = frames[0].Height;

        output.Write("GIF89a"u8);
        WriteUInt16(output, width);
        WriteUInt16(output, height);
        output.WriteByte((Byte)(0x80 | 0x70 | (tableBits - 1))); // global table, 8 bits of colour resolution
        output.WriteByte(0); // background colour index
        output.WriteByte(0); // pixel aspect ratio

        var table = new Byte[(1 << tableBits) * 3];
        foreach (var ((r, g, b), i) in palette)
        {
            table[i * 3] = r;
            table[i * 3 + 1] = g;
            table[i * 3 + 2] = b;
        }

        output.Write(table);

        if (frames.Count > 1)
        {
            output.Write([0x21, 0xFF, 0x0B]);
            output.Write("NETSCAPE2.0"u8);
            output.Write([0x03, 0x01, 0x00, 0x00, 0x00]); // loop forever
        }

        foreach (var frame in frames)
        {
            output.Write([0x21, 0xF9, 0x04, 0x00]); // graphic control: no disposal, no transparency
            WriteUInt16(output, delay);
            output.Write([0x00, 0x00]);

            output.WriteByte(0x2C);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt16(output, width);
            WriteUInt16(output, height);
            output.WriteByte(0); // no local colour table

            var indices = new Byte[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var c = frame[x, y];
                indices[y * width + x] = (Byte)palette[(c.R, c.G, c.B)];
            }

            output.WriteByte((Byte)codeBits);
            WriteSubBlocks(output, Lzw(indices, codeBits));
        }

        output.WriteByte(0x3B);
    }

    private static void WriteUInt16(Stream output, Int32 value)
    {
        output.WriteByte((Byte)value);
        output.WriteByte((Byte)(value >> 8));
    }

    private static void WriteSubBlocks(Stream output, Byte[] data)
    {
        for (var at = 0; at < data.Length; at += 255)
        {
            var length = Math.Min(255, data.Length - at);
            output.WriteByte((Byte)length);
            output.Write(data, at, length);
        }

        output.WriteByte(0);
    }

    /// <summary>GIF's flavour of LZW: variable-width codes, least significant bit first.</summary>
    internal static Byte[] Lzw(Byte[] indices, Int32 minCodeSize)
    {
        var clear = 1 << minCodeSize;
        var endOfInformation = clear + 1;

        var output = new List<Byte>();
        var buffer = 0;
        var bits = 0;
        var codeSize = minCodeSize + 1;
        var next = endOfInformation + 1;
        var dictionary = new Dictionary<Int32, Int32>(); // (prefix code << 8 | next index) to code

        void Emit(Int32 code)
        {
            buffer |= code << bits;
            bits += codeSize;
            while (bits >= 8)
            {
                output.Add((Byte)buffer);
                buffer >>= 8;
                bits -= 8;
            }
        }

        // A code is written, and only then does the table grow, so the width goes up once the next free
        // code no longer fits in the current one.
        void GrowCodeSize()
        {
            if (next >= 1 << codeSize && codeSize < MaxCodeBits)
                codeSize++;
        }

        Emit(clear);
        var prefix = (Int32)indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            var key = prefix << 8 | indices[i];
            if (dictionary.TryGetValue(key, out var existing))
            {
                prefix = existing;
                continue;
            }

            Emit(prefix);
            GrowCodeSize();
            if (next < MaxCodes)
            {
                dictionary[key] = next++;
            }
            else
            {
                Emit(clear);
                dictionary.Clear();
                next = endOfInformation + 1;
                codeSize = minCodeSize + 1;
            }

            prefix = indices[i];
        }

        Emit(prefix);
        GrowCodeSize();
        Emit(endOfInformation);
        if (bits > 0)
            output.Add((Byte)buffer);
        return [..output];
    }
}
