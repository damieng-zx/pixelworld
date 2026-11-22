using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelWorld.Imaging;

/// <summary>
/// Encodes a picture as the bitstream of a lossless WebP (VP8L). Pictures with 256 colours or fewer, which is
/// every Spectrum screen, are stored as a palette with several pixels packed into each symbol; everything is
/// then compressed with LZ77 back references, chosen for what they cost to write, and Huffman codes.
/// </summary>
internal static class Vp8lEncoder
{
    public static Byte[] Encode(Bitmap image)
    {
        var width = image.Width;
        var height = image.Height;
        var pixels = new UInt32[width * height];
        var colours = new Dictionary<UInt32, Int32>();
        var hasAlpha = false;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var c = image[x, y];
            var argb = (UInt32)c.A << 24 | (UInt32)c.R << 16 | (UInt32)c.G << 8 | c.B;
            pixels[y * width + x] = argb;
            hasAlpha |= c.A != 255;
            if (colours.Count <= 256)
                colours.TryAdd(argb, colours.Count);
        }

        var bits = new Vp8lBitWriter();
        bits.Write(0x2F, 8); // signature
        bits.Write((UInt32)(width - 1), 14);
        bits.Write((UInt32)(height - 1), 14);
        bits.Write(hasAlpha ? 1u : 0u, 1);
        bits.Write(0, 3); // version

        if (colours.Count <= 256)
            WritePalettised(bits, pixels, width, height, colours);
        else
            WriteTruecolour(bits, pixels, width, height);

        return bits.ToArray();
    }

    private static void WritePalettised(Vp8lBitWriter bits, UInt32[] pixels, Int32 width, Int32 height, Dictionary<UInt32, Int32> colours)
    {
        var size = colours.Count;
        var table = new UInt32[size];
        foreach (var (colour, index) in colours)
            table[index] = colour;

        // Each palette entry is stored as the difference from the one before it.
        var deltas = new UInt32[size];
        deltas[0] = table[0];
        for (var i = 1; i < size; i++)
            deltas[i] = SubtractPixels(table[i], table[i - 1]);

        bits.Write(1, 1); // a transform follows
        bits.Write(3, 2); // colour indexing
        bits.Write((UInt32)(size - 1), 8);
        WriteImage(bits, deltas, size, isMain: false);

        // With few colours several indexes fit in the green channel of one pixel.
        var packing = size <= 2 ? 3 : size <= 4 ? 2 : size <= 16 ? 1 : 0;
        var bitsPerIndex = 8 >> packing;
        var mask = (1 << packing) - 1;
        var packedWidth = (width + mask) >> packing;
        var packed = new UInt32[packedWidth * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            packed[y * packedWidth + (x >> packing)] |= (UInt32)colours[pixels[y * width + x]] << (bitsPerIndex * (x & mask));

        for (var i = 0; i < packed.Length; i++)
            packed[i] = 0xFF000000 | packed[i] << 8;

        bits.Write(0, 1); // no more transforms
        WriteImage(bits, packed, packedWidth, isMain: true);
    }

    private static void WriteTruecolour(Vp8lBitWriter bits, UInt32[] pixels, Int32 width, Int32 height)
    {
        // Subtracting green from red and blue makes photographs and shaded graphics more compressible.
        var transformed = new UInt32[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            var green = (p >> 8) & 0xFF;
            var red = (((p >> 16) & 0xFF) - green) & 0xFF;
            var blue = ((p & 0xFF) - green) & 0xFF;
            transformed[i] = (p & 0xFF00FF00) | red << 16 | blue;
        }

        bits.Write(1, 1);
        bits.Write(2, 2); // subtract green
        bits.Write(0, 1);
        WriteImage(bits, transformed, width, isMain: true);
    }

    private static UInt32 SubtractPixels(UInt32 a, UInt32 b)
    {
        var alpha = ((a >> 24) - (b >> 24)) & 0xFF;
        var red = (((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) & 0xFF;
        var green = (((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) & 0xFF;
        var blue = ((a & 0xFF) - (b & 0xFF)) & 0xFF;
        return alpha << 24 | red << 16 | green << 8 | blue;
    }

    /// <summary>
    /// An "entropy-coded image": the palette is one, and so is the picture itself. Neither uses a colour
    /// cache, and the picture is one group of codes throughout rather than several for different tiles.
    /// </summary>
    private static void WriteImage(Vp8lBitWriter bits, UInt32[] pixels, Int32 width, Boolean isMain)
    {
        var tokens = Vp8lLz77.Tokenise(pixels, width);

        bits.Write(0, 1); // no colour cache
        if (isMain)
            bits.Write(0, 1); // one group of prefix codes for the whole image

        // Count what the codes will have to write.
        var histogram = new Int32[Vp8lSymbols.TotalSymbols];
        Span<Int32> symbols = stackalloc Int32[4];
        foreach (var token in tokens)
        {
            var n = Vp8lSymbols.TokenSymbols(token, symbols);
            for (var i = 0; i < n; i++)
                histogram[symbols[i]]++;
        }

        var codes = Vp8lSymbols.Alphabets
            .Select(a => Vp8lPrefixCode.WriteNew(bits, histogram[a.Start..(a.Start + a.Length)])).ToArray();

        foreach (var token in tokens)
        {
            if (token.Length == 0)
            {
                codes[0].Write(bits, (Int32)((token.Argb >> 8) & 0xFF));
                codes[1].Write(bits, (Int32)((token.Argb >> 16) & 0xFF));
                codes[2].Write(bits, (Int32)(token.Argb & 0xFF));
                codes[3].Write(bits, (Int32)(token.Argb >> 24));
            }
            else
            {
                var length = Vp8lSymbols.Prefix(token.Length);
                codes[0].Write(bits, Vp8lSymbols.LiteralSymbols + length.Code);
                bits.Write((UInt32)length.ExtraValue, length.ExtraBits);
                var far = Vp8lSymbols.Prefix(token.DistanceCode);
                codes[4].Write(bits, far.Code);
                bits.Write((UInt32)far.ExtraValue, far.ExtraBits);
            }
        }
    }
}
