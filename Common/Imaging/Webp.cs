using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace PixelWorld.Imaging;

/// <summary>
/// Writes lossless WebP files (the VP8L format). Pictures with 256 colours or fewer, which is every
/// Spectrum screen, are stored as a palette with several pixels packed into each symbol; everything
/// is then compressed with LZ77 back references and Huffman codes.
/// </summary>
public static class Webp
{
    /// <summary>The largest width or height a lossless WebP can have.</summary>
    public const Int32 MaxDimension = 1 << 14;

    public static void Write(Bitmap image, Stream output)
    {
        if (image.Width > MaxDimension || image.Height > MaxDimension)
            throw new ArgumentException($"The image is too large for a WebP, which allows {MaxDimension} pixels either way", nameof(image));

        var payload = Vp8lEncoder.Encode(image);
        var padding = payload.Length & 1;

        Span<Byte> number = stackalloc Byte[4];
        output.Write("RIFF"u8);
        BinaryPrimitives.WriteUInt32LittleEndian(number, (UInt32)(4 + 8 + payload.Length + padding));
        output.Write(number);
        output.Write("WEBPVP8L"u8);
        BinaryPrimitives.WriteUInt32LittleEndian(number, (UInt32)payload.Length);
        output.Write(number);
        output.Write(payload);
        if (padding != 0)
            output.WriteByte(0);
    }
}

internal static class Vp8lEncoder
{
    private const Int32 MaxCodeLength = 15;
    private const Int32 MaxCodeLengthCodeLength = 7;
    private const Int32 LiteralSymbols = 256;
    private const Int32 LengthPrefixSymbols = 24;
    private const Int32 DistancePrefixSymbols = 40;

    private const Int32 MinMatch = 4;
    private const Int32 MaxMatch = 4096;
    private const Int32 MaxDistance = (1 << 20) - 120;
    private const Int32 MaxChain = 1024;
    private const Int32 LazyMargin = 0;
    private const Int32 HashBits = 15;

    /// <summary>The order the lengths of the code length code are written in.</summary>
    private static readonly Int32[] CodeLengthOrder = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>
    /// The 120 nearest neighbours a back reference can name by position instead of distance: the high nibble
    /// is how many rows up and 8 minus the low nibble is how many pixels across.
    /// </summary>
    private static readonly Byte[] NeighbourCodes =
    [
        0x18, 0x07, 0x17, 0x19, 0x28, 0x06, 0x27, 0x29, 0x16, 0x1a,
        0x26, 0x2a, 0x38, 0x05, 0x37, 0x39, 0x15, 0x1b, 0x36, 0x3a,
        0x25, 0x2b, 0x48, 0x04, 0x47, 0x49, 0x14, 0x1c, 0x35, 0x3b,
        0x46, 0x4a, 0x24, 0x2c, 0x58, 0x45, 0x4b, 0x34, 0x3c, 0x03,
        0x57, 0x59, 0x13, 0x1d, 0x56, 0x5a, 0x23, 0x2d, 0x44, 0x4c,
        0x55, 0x5b, 0x33, 0x3d, 0x68, 0x02, 0x67, 0x69, 0x12, 0x1e,
        0x66, 0x6a, 0x22, 0x2e, 0x54, 0x5c, 0x43, 0x4d, 0x65, 0x6b,
        0x32, 0x3e, 0x78, 0x01, 0x77, 0x79, 0x53, 0x5d, 0x11, 0x1f,
        0x64, 0x6c, 0x42, 0x4e, 0x76, 0x7a, 0x21, 0x2f, 0x75, 0x7b,
        0x31, 0x3f, 0x63, 0x6d, 0x52, 0x5e, 0x00, 0x74, 0x7c, 0x41,
        0x4f, 0x10, 0x20, 0x62, 0x6e, 0x30, 0x73, 0x7d, 0x51, 0x5f,
        0x40, 0x72, 0x7e, 0x61, 0x6f, 0x50, 0x71, 0x7f, 0x60, 0x70
    ];

    private readonly record struct Token(UInt32 Argb, Int32 Length, Int32 DistanceCode);

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

        var bits = new BitWriter();
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

    private static void WritePalettised(BitWriter bits, UInt32[] pixels, Int32 width, Int32 height, Dictionary<UInt32, Int32> colours)
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

    private static void WriteTruecolour(BitWriter bits, UInt32[] pixels, Int32 width, Int32 height)
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
    /// cache. The picture alone says whether it has a single set of codes (it does) and gets back references.
    /// </summary>
    private static void WriteImage(BitWriter bits, UInt32[] pixels, Int32 width, Boolean isMain)
    {
        var tokens = Tokenise(pixels, width, isMain);

        bits.Write(0, 1); // no colour cache
        if (isMain)
            bits.Write(0, 1); // one group of prefix codes for the whole image

        var green = new Int32[LiteralSymbols + LengthPrefixSymbols];
        var red = new Int32[LiteralSymbols];
        var blue = new Int32[LiteralSymbols];
        var alpha = new Int32[LiteralSymbols];
        var distance = new Int32[DistancePrefixSymbols];
        foreach (var t in tokens)
        {
            if (t.Length == 0)
            {
                green[(t.Argb >> 8) & 0xFF]++;
                red[(t.Argb >> 16) & 0xFF]++;
                blue[t.Argb & 0xFF]++;
                alpha[t.Argb >> 24]++;
            }
            else
            {
                green[LiteralSymbols + Prefix(t.Length).Code]++;
                distance[Prefix(t.DistanceCode).Code]++;
            }
        }

        var greenCode = WritePrefixCode(bits, green);
        var redCode = WritePrefixCode(bits, red);
        var blueCode = WritePrefixCode(bits, blue);
        var alphaCode = WritePrefixCode(bits, alpha);
        var distanceCode = WritePrefixCode(bits, distance);

        foreach (var t in tokens)
        {
            if (t.Length == 0)
            {
                greenCode.Write(bits, (Int32)((t.Argb >> 8) & 0xFF));
                redCode.Write(bits, (Int32)((t.Argb >> 16) & 0xFF));
                blueCode.Write(bits, (Int32)(t.Argb & 0xFF));
                alphaCode.Write(bits, (Int32)(t.Argb >> 24));
            }
            else
            {
                var length = Prefix(t.Length);
                greenCode.Write(bits, LiteralSymbols + length.Code);
                bits.Write((UInt32)length.ExtraValue, length.ExtraBits);
                var far = Prefix(t.DistanceCode);
                distanceCode.Write(bits, far.Code);
                bits.Write((UInt32)far.ExtraValue, far.ExtraBits);
            }
        }
    }

    /// <summary>
    /// How a length or distance is stored: a prefix symbol, then a few extra bits for the rest. Small
    /// values are their own symbol, and each doubling after that gets two symbols.
    /// </summary>
    private static (Int32 Code, Int32 ExtraBits, Int32 ExtraValue) Prefix(Int32 value)
    {
        if (value <= 4)
            return (value - 1, 0, 0);

        var rest = value - 1;
        var highest = 31 - BitOperations.LeadingZeroCount((UInt32)rest);
        var second = (rest >> (highest - 1)) & 1;
        var extraBits = highest - 1;
        return (2 * highest + second, extraBits, rest & ((1 << extraBits) - 1));
    }

    private static List<Token> Tokenise(UInt32[] pixels, Int32 width, Boolean backReferences)
    {
        var tokens = new List<Token>(pixels.Length);
        if (!backReferences)
        {
            foreach (var p in pixels)
                tokens.Add(new Token(p, 0, 0));
            return tokens;
        }

        var distanceCodes = new Dictionary<Int32, Int32>();
        for (var i = 0; i < NeighbourCodes.Length; i++)
            distanceCodes.TryAdd(Math.Max(1, (NeighbourCodes[i] >> 4) * width + 8 - (NeighbourCodes[i] & 0xF)), i + 1);

        Int32 CodeFor(Int32 distance) => distanceCodes.TryGetValue(distance, out var code) ? code : distance + NeighbourCodes.Length;

        var count = pixels.Length;
        var head = new Int32[1 << HashBits];
        Array.Fill(head, -1);
        var chain = new Int32[count];

        Int32 Hash(Int32 at) => (Int32)(unchecked(pixels[at] * 0x1E35A7BDu ^ pixels[at + 1] * 0x9E3779B1u ^ pixels[at + 2] * 0x85EBCA6Bu) >> (32 - HashBits));

        void Insert(Int32 at)
        {
            if (at + 2 >= count) return;
            var hash = Hash(at);
            chain[at] = head[hash];
            head[hash] = at;
        }

        (Int32 Length, Int32 Distance) FindMatch(Int32 at)
        {
            var bestLength = 0;
            var bestDistance = 0;
            if (at + 2 >= count) return (0, 0);

            var longest = Math.Min(MaxMatch, count - at);
            var candidate = head[Hash(at)];
            for (var depth = 0; candidate >= 0 && depth < MaxChain; depth++, candidate = chain[candidate])
            {
                var distance = at - candidate;
                if (distance > MaxDistance) break;

                var length = 0;
                while (length < longest && pixels[candidate + length] == pixels[at + length])
                    length++;

                if (length > bestLength)
                {
                    bestLength = length;
                    bestDistance = distance;
                    if (length == longest) break;
                }
            }

            return (bestLength, bestDistance);
        }

        var position = 0;
        while (position < count)
        {
            var (length, distance) = FindMatch(position);
            if (length < MinMatch)
            {
                tokens.Add(new Token(pixels[position], 0, 0));
                Insert(position);
                position++;
                continue;
            }

            // Lazy matching: if starting a pixel later gives a clearly longer match, spend a literal here.
            Insert(position);
            var (laterLength, laterDistance) = FindMatch(position + 1);
            if (laterLength > length + LazyMargin)
            {
                tokens.Add(new Token(pixels[position], 0, 0));
                position++;
                Insert(position);
                (length, distance) = (laterLength, laterDistance);
            }

            tokens.Add(new Token(0, length, CodeFor(distance)));
            for (var k = 1; k < length; k++)
                Insert(position + k);
            position += length;
        }

        return tokens;
    }

    /// <summary>A canonical Huffman code, ready to write symbols with.</summary>
    private sealed class PrefixCode(Int32[] lengths)
    {
        public Int32[] Lengths { get; } = lengths;

        // Stored back to front so they can go into the stream least significant bit first.
        private readonly UInt32[] codes = CanonicalCodes(lengths);

        // One symbol is free: the decoder knows which it is without any bits.
        private readonly Boolean single = lengths.Count(l => l > 0) == 1;

        public void Write(BitWriter bits, Int32 symbol)
        {
            if (single) return;
            bits.Write(codes[symbol], Lengths[symbol]);
        }

        private static UInt32[] CanonicalCodes(Int32[] lengths)
        {
            var longest = lengths.Max();
            var perLength = new Int32[longest + 2];
            foreach (var l in lengths.Where(l => l > 0))
                perLength[l]++;

            var next = new UInt32[longest + 2];
            UInt32 code = 0;
            for (var l = 1; l <= longest; l++)
            {
                code = (code + (UInt32)perLength[l - 1]) << 1;
                next[l] = code;
            }

            var codes = new UInt32[lengths.Length];
            for (var s = 0; s < lengths.Length; s++)
            {
                var l = lengths[s];
                if (l == 0) continue;

                var forward = next[l]++;
                UInt32 reversed = 0;
                for (var b = 0; b < l; b++)
                    reversed |= ((forward >> b) & 1) << (l - 1 - b);
                codes[s] = reversed;
            }

            return codes;
        }
    }

    /// <summary>Work out a code for the symbols used and write it in the stream's own compact form.</summary>
    private static PrefixCode WritePrefixCode(BitWriter bits, Int32[] frequencies)
    {
        var lengths = BuildLengths(frequencies, MaxCodeLength);
        var used = Enumerable.Range(0, lengths.Length).Where(s => lengths[s] > 0).ToArray();

        // One or two symbols, both under 256, have a short special form.
        if (used.Length <= 2 && used.All(s => s < LiteralSymbols))
        {
            bits.Write(1, 1);
            if (used.Length == 0)
            {
                bits.Write(0, 3); // one symbol, a one bit symbol, which is 0
                lengths[0] = 1;
            }
            else
            {
                bits.Write((UInt32)(used.Length - 1), 1);
                if (used[0] <= 1)
                {
                    bits.Write(0, 1);
                    bits.Write((UInt32)used[0], 1);
                }
                else
                {
                    bits.Write(1, 1);
                    bits.Write((UInt32)used[0], 8);
                }

                if (used.Length == 2)
                    bits.Write((UInt32)used[1], 8);
            }

            return new PrefixCode(lengths);
        }

        bits.Write(0, 1);
        WriteCodeLengths(bits, lengths);
        return new PrefixCode(lengths);
    }

    /// <summary>
    /// The lengths of a normal code are themselves run-length encoded and Huffman coded, using symbols 0 to 15
    /// as lengths, 16 to repeat the last length 3-6 times, 17 for 3-10 zeros and 18 for 11-138 zeros.
    /// </summary>
    private static void WriteCodeLengths(BitWriter bits, Int32[] lengths)
    {
        var tokens = new List<(Int32 Symbol, Int32 ExtraBits, Int32 ExtraValue)>();
        for (var i = 0; i < lengths.Length;)
        {
            var length = lengths[i];
            var run = 1;
            while (i + run < lengths.Length && lengths[i + run] == length)
                run++;
            i += run;

            if (length == 0)
            {
                while (run >= 11)
                {
                    var chunk = Math.Min(run, 138);
                    tokens.Add((18, 7, chunk - 11));
                    run -= chunk;
                }

                if (run >= 3)
                {
                    tokens.Add((17, 3, run - 3));
                    run = 0;
                }

                for (; run > 0; run--)
                    tokens.Add((0, 0, 0));
            }
            else
            {
                tokens.Add((length, 0, 0));
                run--;
                while (run >= 3)
                {
                    var chunk = Math.Min(run, 6);
                    tokens.Add((16, 2, chunk - 3));
                    run -= chunk;
                }

                for (; run > 0; run--)
                    tokens.Add((length, 0, 0));
            }
        }

        var frequencies = new Int32[CodeLengthOrder.Length];
        foreach (var t in tokens)
            frequencies[t.Symbol]++;

        var code = new PrefixCode(BuildLengths(frequencies, MaxCodeLengthCodeLength));
        var written = CodeLengthOrder.Length;
        while (written > 4 && code.Lengths[CodeLengthOrder[written - 1]] == 0)
            written--;

        bits.Write((UInt32)(written - 4), 4);
        for (var i = 0; i < written; i++)
            bits.Write((UInt32)code.Lengths[CodeLengthOrder[i]], 3);

        bits.Write(0, 1); // all of the symbols have lengths
        foreach (var t in tokens)
        {
            code.Write(bits, t.Symbol);
            bits.Write((UInt32)t.ExtraValue, t.ExtraBits);
        }
    }

    /// <summary>
    /// Huffman code lengths for the given frequencies, none longer than <paramref name="maxLength"/>. When the
    /// tree is too deep the rare symbols are treated as more common until it is shallow enough.
    /// </summary>
    internal static Int32[] BuildLengths(Int32[] frequencies, Int32 maxLength)
    {
        var lengths = new Int32[frequencies.Length];
        var symbols = Enumerable.Range(0, frequencies.Length).Where(s => frequencies[s] > 0).ToArray();
        switch (symbols.Length)
        {
            case 0:
                return lengths;
            case 1:
                lengths[symbols[0]] = 1;
                return lengths;
        }

        for (Int64 floor = 1;; floor *= 2)
        {
            var parent = new Int32[symbols.Length * 2 - 1];
            var weight = new Int64[parent.Length];
            var queue = new PriorityQueue<Int32, (Int64 Weight, Int32 Order)>();
            for (var i = 0; i < symbols.Length; i++)
            {
                weight[i] = Math.Max(frequencies[symbols[i]], floor);
                queue.Enqueue(i, (weight[i], i));
            }

            var node = symbols.Length;
            while (queue.Count > 1)
            {
                var a = queue.Dequeue();
                var b = queue.Dequeue();
                weight[node] = weight[a] + weight[b];
                parent[a] = parent[b] = node;
                queue.Enqueue(node, (weight[node], node));
                node++;
            }

            parent[node - 1] = -1;

            var depths = new Int32[symbols.Length];
            for (var i = 0; i < symbols.Length; i++)
                for (var at = i; parent[at] >= 0; at = parent[at])
                    depths[i]++;

            if (depths.Max() > maxLength) continue;

            for (var i = 0; i < symbols.Length; i++)
                lengths[symbols[i]] = depths[i];
            return lengths;
        }
    }

    /// <summary>Writes bits least significant first, as the format wants.</summary>
    private sealed class BitWriter
    {
        private readonly List<Byte> bytes = [];
        private UInt64 buffer;
        private Int32 count;

        public void Write(UInt32 value, Int32 length)
        {
            buffer |= (UInt64)value << count;
            count += length;
            while (count >= 8)
            {
                bytes.Add((Byte)buffer);
                buffer >>= 8;
                count -= 8;
            }
        }

        public Byte[] ToArray()
        {
            if (count > 0)
            {
                bytes.Add((Byte)buffer);
                buffer = 0;
                count = 0;
            }

            return [..bytes];
        }
    }
}
