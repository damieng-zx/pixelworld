using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace PixelWorld.Imaging;

/// <summary>Reads and writes PNG files.</summary>
public static class Png
{
    private static readonly Byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const Byte Greyscale = 0, Truecolor = 2, Indexed = 3, GreyscaleAlpha = 4, TruecolorAlpha = 6;

    /// <summary>Most pixels a decoded image may have, so a hostile header cannot ask for gigabytes.</summary>
    private const Int64 MaxPixels = 1 << 26;

    // ---- Writing ----

    /// <summary>
    /// Write the bitmap as a PNG: indexed colour at the smallest bit depth when it has 256 colours or
    /// fewer (as font previews and Spectrum screens do), otherwise 8-bit truecolor, with alpha only when
    /// some pixel is not opaque.
    /// </summary>
    public static void Write(Bitmap bitmap, Stream output)
    {
        var palette = BuildPalette(bitmap);

        Byte colorType, bitDepth;
        Byte[] scanlines;
        if (palette is not null)
        {
            colorType = Indexed;
            bitDepth = palette.Count switch { <= 2 => 1, <= 4 => 2, <= 16 => 4, _ => 8 };
            scanlines = IndexedScanlines(bitmap, palette, bitDepth);
        }
        else
        {
            var hasAlpha = HasTransparency(bitmap);
            colorType = hasAlpha ? TruecolorAlpha : Truecolor;
            bitDepth = 8;
            scanlines = TruecolorScanlines(bitmap, hasAlpha);
        }

        output.Write(Signature);

        Span<Byte> header = stackalloc Byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, bitmap.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], bitmap.Height);
        header[8] = bitDepth;
        header[9] = colorType;
        WriteChunk(output, "IHDR", header);

        if (palette is not null)
        {
            var plte = new Byte[palette.Count * 3];
            var alpha = new Byte[palette.Count];
            var lastTranslucent = -1;
            foreach (var (color, i) in palette)
            {
                plte[i * 3] = color.R;
                plte[i * 3 + 1] = color.G;
                plte[i * 3 + 2] = color.B;
                alpha[i] = color.A;
                if (color.A != 255) lastTranslucent = i;
            }

            WriteChunk(output, "PLTE", plte);
            if (lastTranslucent >= 0)
                WriteChunk(output, "tRNS", alpha.AsSpan(0, lastTranslucent + 1));
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            zlib.Write(scanlines);
        WriteChunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, (Int32)compressed.Length));
        WriteChunk(output, "IEND", []);
    }

    /// <summary>The colours in order of first use, or null if there are more than 256.</summary>
    internal static Dictionary<Color, Int32>? BuildPalette(Bitmap bitmap)
    {
        var palette = new Dictionary<Color, Int32>();
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap[x, y];
            if (palette.ContainsKey(color)) continue;
            if (palette.Count == 256) return null;
            palette.Add(color, palette.Count);
        }

        return palette;
    }

    private static Boolean HasTransparency(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap[x, y].A != 255)
                return true;
        return false;
    }

    private static Byte[] IndexedScanlines(Bitmap bitmap, Dictionary<Color, Int32> palette, Int32 bitDepth)
    {
        var rowBytes = (bitmap.Width * bitDepth + 7) / 8;
        var data = new Byte[(rowBytes + 1) * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            var row = y * (rowBytes + 1) + 1; // the byte before it is the filter type, 0 for none
            for (var x = 0; x < bitmap.Width; x++)
            {
                var bit = x * bitDepth;
                data[row + bit / 8] |= (Byte)(palette[bitmap[x, y]] << (8 - bitDepth - bit % 8));
            }
        }

        return data;
    }

    private static Byte[] TruecolorScanlines(Bitmap bitmap, Boolean alpha)
    {
        var channels = alpha ? 4 : 3;
        var rowBytes = bitmap.Width * channels;
        var data = new Byte[(rowBytes + 1) * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            var at = y * (rowBytes + 1) + 1;
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap[x, y];
                data[at++] = color.R;
                data[at++] = color.G;
                data[at++] = color.B;
                if (alpha) data[at++] = color.A;
            }
        }

        return data;
    }

    private static void WriteChunk(Stream output, String type, ReadOnlySpan<Byte> data)
    {
        Span<Byte> word = stackalloc Byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);

        var typeBytes = new Byte[4];
        for (var i = 0; i < 4; i++)
            typeBytes[i] = (Byte)type[i];
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32.Update(Crc32.Update(0, typeBytes), data);
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        output.Write(word);
    }

    // ---- Reading ----

    /// <summary>
    /// Read any PNG: every colour type and bit depth, with or without interlacing. Greyscale and palette
    /// images are expanded to colours, and transparency (an alpha channel or tRNS chunk) is kept.
    /// </summary>
    public static Bitmap Read(Stream input)
    {
        Span<Byte> signature = stackalloc Byte[8];
        input.ReadExactly(signature);
        if (!signature.SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file");

        Header? header = null;
        Byte[]? palette = null;
        Byte[]? transparency = null;
        using var idat = new MemoryStream();

        var done = false;
        var word = new Byte[4];
        while (!done)
        {
            input.ReadExactly(word);
            var length = BinaryPrimitives.ReadInt32BigEndian(word);
            if (length < 0) throw new InvalidDataException("PNG chunk is too long");
            input.ReadExactly(word);
            var type = System.Text.Encoding.ASCII.GetString(word);

            var data = new Byte[length];
            input.ReadExactly(data);
            input.ReadExactly(word); // the CRC is not checked: damaged image data fails to inflate anyway

            switch (type)
            {
                case "IHDR":
                    header = ParseHeader(data);
                    break;
                case "PLTE":
                    palette = data;
                    break;
                case "tRNS":
                    transparency = data;
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    done = true;
                    break;
            }
        }

        if (header is null) throw new InvalidDataException("PNG has no header");
        var h = header.Value;
        if (h.ColorType == Indexed && palette is null) throw new InvalidDataException("Indexed PNG has no palette");

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        return Decode(h, palette, transparency, zlib);
    }

    private readonly record struct Header(Int32 Width, Int32 Height, Int32 BitDepth, Byte ColorType, Boolean Interlaced)
    {
        public Int32 Channels => ColorType switch
        {
            Greyscale or Indexed => 1,
            GreyscaleAlpha => 2,
            Truecolor => 3,
            _ => 4
        };

        public Int32 BitsPerPixel => Channels * BitDepth;
    }

    private static Header ParseHeader(Byte[] data)
    {
        if (data.Length != 13) throw new InvalidDataException("PNG header is the wrong size");
        var width = BinaryPrimitives.ReadInt32BigEndian(data);
        var height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
        var depth = data[8];
        var colorType = data[9];

        if (width <= 0 || height <= 0 || (Int64)width * height > MaxPixels)
            throw new InvalidDataException($"PNG size {width}x{height} is not supported");

        var validDepths = colorType switch
        {
            Greyscale => new[] { 1, 2, 4, 8, 16 },
            Indexed => [1, 2, 4, 8],
            Truecolor or GreyscaleAlpha or TruecolorAlpha => [8, 16],
            _ => throw new InvalidDataException($"PNG colour type {colorType} is not valid")
        };
        if (Array.IndexOf(validDepths, (Int32)depth) < 0)
            throw new InvalidDataException($"PNG bit depth {depth} is not valid for colour type {colorType}");
        if (data[10] != 0 || data[11] != 0 || data[12] > 1)
            throw new InvalidDataException("PNG uses an unknown compression, filter or interlace method");

        return new Header(width, height, depth, colorType, data[12] == 1);
    }

    // Adam7: where each of the seven passes starts and how far apart its pixels are.
    private static readonly (Int32 X, Int32 Y, Int32 DX, Int32 DY)[] Passes =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    private static Bitmap Decode(Header h, Byte[]? palette, Byte[]? transparency, Stream source)
    {
        var bitmap = new Bitmap(h.Width, h.Height);
        var passes = h.Interlaced ? Passes : [(0, 0, 1, 1)];

        foreach (var (startX, startY, dx, dy) in passes)
        {
            var width = (h.Width - startX + dx - 1) / dx;
            var height = (h.Height - startY + dy - 1) / dy;
            if (width <= 0 || height <= 0) continue;

            var rowBytes = (width * h.BitsPerPixel + 7) / 8;
            var bytesPerPixel = Math.Max(1, h.BitsPerPixel / 8);
            var previous = new Byte[rowBytes];
            var row = new Byte[rowBytes];

            for (var y = 0; y < height; y++)
            {
                var filter = source.ReadByte();
                if (filter < 0) throw new InvalidDataException("PNG image data ends early");
                source.ReadExactly(row);
                Unfilter(filter, row, previous, bytesPerPixel);

                for (var x = 0; x < width; x++)
                    bitmap[startX + x * dx, startY + y * dy] = Pixel(h, row, x, palette, transparency);

                (previous, row) = (row, previous);
            }
        }

        return bitmap;
    }

    private static void Unfilter(Int32 filter, Byte[] row, Byte[] previous, Int32 bpp)
    {
        for (var i = 0; i < row.Length; i++)
        {
            var left = i >= bpp ? row[i - bpp] : 0;
            var up = previous[i];
            var upLeft = i >= bpp ? previous[i - bpp] : 0;
            row[i] = (Byte)(row[i] + filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"PNG filter type {filter} is not valid")
            });
        }
    }

    private static Int32 Paeth(Int32 a, Int32 b, Int32 c)
    {
        var p = a + b - c;
        Int32 pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>The raw value of one sample, at its full bit depth.</summary>
    private static Int32 Sample(Header h, Byte[] row, Int32 index)
    {
        switch (h.BitDepth)
        {
            case 8:
                return row[index];
            case 16:
                return row[index * 2] << 8 | row[index * 2 + 1];
            default:
                var bit = index * h.BitDepth;
                return (row[bit / 8] >> (8 - h.BitDepth - bit % 8)) & ((1 << h.BitDepth) - 1);
        }
    }

    private static Byte To8Bit(Header h, Int32 sample) => h.BitDepth switch
    {
        8 => (Byte)sample,
        16 => (Byte)(sample >> 8),
        _ => (Byte)(sample * 255 / ((1 << h.BitDepth) - 1))
    };

    private static Color Pixel(Header h, Byte[] row, Int32 x, Byte[]? palette, Byte[]? transparency)
    {
        var first = x * h.Channels;
        switch (h.ColorType)
        {
            case Indexed:
            {
                var i = Sample(h, row, x);
                if (i * 3 + 2 >= palette!.Length) throw new InvalidDataException("PNG uses a colour outside its palette");
                var alpha = transparency is not null && i < transparency.Length ? transparency[i] : (Byte)255;
                return new Color(palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2], alpha);
            }
            case Greyscale:
            {
                var s = Sample(h, row, first);
                var g = To8Bit(h, s);
                var alpha = transparency is { Length: >= 2 } && s == (transparency[0] << 8 | transparency[1]) ? (Byte)0 : (Byte)255;
                return new Color(g, g, g, alpha);
            }
            case GreyscaleAlpha:
            {
                var g = To8Bit(h, Sample(h, row, first));
                return new Color(g, g, g, To8Bit(h, Sample(h, row, first + 1)));
            }
            case Truecolor:
            {
                Int32 r = Sample(h, row, first), g = Sample(h, row, first + 1), b = Sample(h, row, first + 2);
                var alpha = transparency is { Length: >= 6 }
                            && r == (transparency[0] << 8 | transparency[1])
                            && g == (transparency[2] << 8 | transparency[3])
                            && b == (transparency[4] << 8 | transparency[5])
                    ? (Byte)0
                    : (Byte)255;
                return new Color(To8Bit(h, r), To8Bit(h, g), To8Bit(h, b), alpha);
            }
            default:
                return new Color(To8Bit(h, Sample(h, row, first)), To8Bit(h, Sample(h, row, first + 1)),
                    To8Bit(h, Sample(h, row, first + 2)), To8Bit(h, Sample(h, row, first + 3)));
        }
    }
}

/// <summary>The CRC-32 PNG uses to check each chunk.</summary>
internal static class Crc32
{
    private static readonly UInt32[] Table = BuildTable();

    private static UInt32[] BuildTable()
    {
        var table = new UInt32[256];
        for (UInt32 n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    /// <summary>The CRC of everything so far followed by <paramref name="data"/>; start from 0.</summary>
    public static UInt32 Update(UInt32 crc, ReadOnlySpan<Byte> data)
    {
        var c = ~crc;
        foreach (var b in data)
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return ~c;
    }
}
