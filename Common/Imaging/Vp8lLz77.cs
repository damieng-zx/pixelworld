using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PixelWorld.Imaging;

/// <summary>One thing the pixel stream says: a pixel as it is, or a copy of earlier pixels.</summary>
/// <param name="Argb">The pixel, when this is not a copy.</param>
/// <param name="Length">How many pixels to copy, or 0 for a pixel that is written out.</param>
/// <param name="DistanceCode">Where to copy from, in the format's own distance codes.</param>
internal readonly record struct Vp8lToken(UInt32 Argb, Int32 Length, Int32 DistanceCode);

/// <summary>How the symbols of an image are numbered, and how lengths and distances become symbols.</summary>
internal static class Vp8lSymbols
{
    public const Int32 LiteralSymbols = 256;
    public const Int32 LengthPrefixSymbols = 24;
    public const Int32 DistancePrefixSymbols = 40;

    /// <summary>The first 256 symbols of the green alphabet are pixel values, then come the length prefixes.</summary>
    public const Int32 GreenSymbols = LiteralSymbols + LengthPrefixSymbols;

    // An image has five alphabets: green with the length prefixes, red, blue, alpha and the distance prefixes.
    // Counting them in one array, one after the other, keeps a histogram a single flat array.
    public const Int32 RedStart = GreenSymbols;
    public const Int32 BlueStart = RedStart + LiteralSymbols;
    public const Int32 AlphaStart = BlueStart + LiteralSymbols;
    public const Int32 DistanceStart = AlphaStart + LiteralSymbols;
    public const Int32 TotalSymbols = DistanceStart + DistancePrefixSymbols;

    /// <summary>Where each of the five alphabets starts in a flat histogram, and how long it is.</summary>
    public static readonly (Int32 Start, Int32 Length)[] Alphabets =
    [
        (0, GreenSymbols), (RedStart, LiteralSymbols), (BlueStart, LiteralSymbols), (AlphaStart, LiteralSymbols), (DistanceStart, DistancePrefixSymbols)
    ];

    /// <summary>The flat-histogram symbols a token is written with: four for a pixel, two for a copy.</summary>
    public static Int32 TokenSymbols(Vp8lToken token, Span<Int32> symbols)
    {
        if (token.Length == 0)
        {
            symbols[0] = (Int32)((token.Argb >> 8) & 0xFF);
            symbols[1] = RedStart + (Int32)((token.Argb >> 16) & 0xFF);
            symbols[2] = BlueStart + (Int32)(token.Argb & 0xFF);
            symbols[3] = AlphaStart + (Int32)(token.Argb >> 24);
            return 4;
        }

        symbols[0] = LiteralSymbols + Prefix(token.Length).Code;
        symbols[1] = DistanceStart + Prefix(token.DistanceCode).Code;
        return 2;
    }

    /// <summary>
    /// How a length or distance is stored: a prefix symbol, then a few extra bits for the rest. Small
    /// values are their own symbol, and each doubling after that gets two symbols.
    /// </summary>
    public static (Int32 Code, Int32 ExtraBits, Int32 ExtraValue) Prefix(Int32 value)
    {
        if (value <= 4)
            return (value - 1, 0, 0);

        var rest = value - 1;
        var highest = 31 - BitOperations.LeadingZeroCount((UInt32)rest);
        var second = (rest >> (highest - 1)) & 1;
        var extraBits = highest - 1;
        return (2 * highest + second, extraBits, rest & ((1 << extraBits) - 1));
    }
}

/// <summary>
/// Finds runs of pixels that repeat earlier ones, so they can be copied rather than written out. A first pass
/// takes the longest copy it can find; the passes after it work out what each symbol costs from the pass before
/// and then find the cheapest way through the whole image, so a slightly shorter copy from a nearby pixel can
/// win over a longer one from far away, whose distance takes many more bits to write.
/// </summary>
internal static class Vp8lLz77
{
    private const Int32 MinMatch = 4;
    private const Int32 MinOptimalMatch = 2;
    private const Int32 MaxMatch = 4096;
    private const Int32 MaxDistance = (1 << 20) - 120;
    private const Int32 MaxChain = 256;
    private const Int32 HashBits = 15;

    /// <summary>How many times to work out the costs again and search for a cheaper route.</summary>
    private const Int32 OptimalPasses = 3;

    /// <summary>A copy this long is simply taken: it is hard to beat, and looking inside it would be slow.</summary>
    private const Int32 LongMatch = 192;

    /// <summary>Every length up to this is tried for each copy found; beyond it only the full length is.</summary>
    private const Int32 ShortLengthsTried = 40;

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

    public static List<Vp8lToken> Tokenise(UInt32[] pixels, Int32 width)
    {
        var finder = new MatchFinder(pixels, width);
        var tokens = Greedy(pixels, finder);

        // Keep the cheapest of the passes by its own estimate, as a pass can occasionally do worse than the last.
        var best = tokens;
        var bestCost = new CostModel(tokens).Total(tokens);
        for (var pass = 0; pass < OptimalPasses; pass++)
        {
            var model = new CostModel(tokens);
            tokens = Optimal(pixels, finder, model);
            var cost = new CostModel(tokens).Total(tokens);
            if (cost < bestCost)
            {
                best = tokens;
                bestCost = cost;
            }
        }

        return best;
    }

    private static List<Vp8lToken> Greedy(UInt32[] pixels, MatchFinder finder)
    {
        var tokens = new List<Vp8lToken>(pixels.Length);
        var found = new List<(Int32 Length, Int32 Distance)>();
        var position = 0;
        while (position < pixels.Length)
        {
            finder.Find(position, found);
            var (length, distance) = found.Count > 0 ? found.MaxBy(m => m.Length) : (0, 0);
            if (length < MinMatch)
            {
                tokens.Add(new Vp8lToken(pixels[position], 0, 0));
                position++;
            }
            else
            {
                tokens.Add(new Vp8lToken(0, length, finder.CodeFor(distance)));
                position += length;
            }
        }

        return tokens;
    }

    /// <summary>The cheapest way to write the image, found by working forward and keeping the best route to each pixel.</summary>
    private static List<Vp8lToken> Optimal(UInt32[] pixels, MatchFinder finder, CostModel model)
    {
        var count = pixels.Length;
        var cost = new Double[count + 1];
        Array.Fill(cost, Double.MaxValue);
        cost[0] = 0;

        // How each pixel was reached: the length of the copy that ended there, or 0 for a pixel written out.
        var copyLength = new Int32[count + 1];
        var copyDistance = new Int32[count + 1];

        void Reach(Int32 at, Double bits, Int32 length, Int32 distance)
        {
            if (bits >= cost[at]) return;
            cost[at] = bits;
            copyLength[at] = length;
            copyDistance[at] = distance;
        }

        var found = new List<(Int32 Length, Int32 Distance)>();
        for (var at = 0; at < count; at++)
        {
            if (cost[at] == Double.MaxValue) continue;

            Reach(at + 1, cost[at] + model.Literal(pixels[at]), 0, 0);

            finder.Find(at, found);
            if (found.Count == 0) continue;

            var longest = found.MaxBy(m => m.Length);
            if (longest.Length >= LongMatch)
            {
                // Take it whole, and do not look at the pixels inside it, which a route would only reach by
                // doing worse.
                Reach(at + longest.Length, cost[at] + model.Copy(longest.Length, finder.CodeFor(longest.Distance)),
                    longest.Length, longest.Distance);
                at += longest.Length - 1;
                continue;
            }

            foreach (var (length, distance) in found)
            {
                var code = finder.CodeFor(distance);
                for (var l = MinOptimalMatch; l <= Math.Min(length, ShortLengthsTried); l++)
                    Reach(at + l, cost[at] + model.Copy(l, code), l, distance);

                if (length > ShortLengthsTried)
                    Reach(at + length, cost[at] + model.Copy(length, code), length, distance);
            }
        }

        var tokens = new List<Vp8lToken>();
        for (var at = count; at > 0;)
        {
            var length = copyLength[at];
            if (length == 0)
            {
                at--;
                tokens.Add(new Vp8lToken(pixels[at], 0, 0));
            }
            else
            {
                tokens.Add(new Vp8lToken(0, length, finder.CodeFor(copyDistance[at])));
                at -= length;
            }
        }

        tokens.Reverse();
        return tokens;
    }

    /// <summary>
    /// What each symbol costs to write, going by how often a set of tokens used it. Symbols not used at all are
    /// not free, only dear, because a better route may well use them.
    /// </summary>
    private sealed class CostModel
    {
        private const Double Smoothing = 0.1;

        private readonly Double[] bits = new Double[Vp8lSymbols.TotalSymbols];

        public CostModel(List<Vp8lToken> tokens)
        {
            var counts = new Int32[Vp8lSymbols.TotalSymbols];
            Span<Int32> symbols = stackalloc Int32[4];
            foreach (var token in tokens)
            {
                var n = Vp8lSymbols.TokenSymbols(token, symbols);
                for (var i = 0; i < n; i++)
                    counts[symbols[i]]++;
            }

            foreach (var (start, length) in Vp8lSymbols.Alphabets)
            {
                Double total = 0;
                for (var s = start; s < start + length; s++)
                    total += counts[s] + Smoothing;

                for (var s = start; s < start + length; s++)
                    bits[s] = Math.Log2(total / (counts[s] + Smoothing));
            }
        }

        public Double Literal(UInt32 argb) =>
            bits[(argb >> 8) & 0xFF]
            + bits[Vp8lSymbols.RedStart + ((argb >> 16) & 0xFF)]
            + bits[Vp8lSymbols.BlueStart + (argb & 0xFF)]
            + bits[Vp8lSymbols.AlphaStart + (argb >> 24)];

        public Double Copy(Int32 length, Int32 distanceCode)
        {
            var l = Vp8lSymbols.Prefix(length);
            var d = Vp8lSymbols.Prefix(distanceCode);
            return bits[Vp8lSymbols.LiteralSymbols + l.Code] + l.ExtraBits + bits[Vp8lSymbols.DistanceStart + d.Code] + d.ExtraBits;
        }

        public Double Total(List<Vp8lToken> tokens) =>
            tokens.Sum(t => t.Length == 0 ? Literal(t.Argb) : Copy(t.Length, t.DistanceCode));
    }

    /// <summary>Looks up earlier runs of pixels that match the ones at a position.</summary>
    private sealed class MatchFinder
    {
        private readonly UInt32[] pixels;
        private readonly Int32[] chain;
        private readonly Int32[] neighbourDistances;
        private readonly Dictionary<Int32, Int32> distanceCodes = [];

        public MatchFinder(UInt32[] pixels, Int32 width)
        {
            this.pixels = pixels;

            for (var i = 0; i < NeighbourCodes.Length; i++)
                distanceCodes.TryAdd(Math.Max(1, (NeighbourCodes[i] >> 4) * width + 8 - (NeighbourCodes[i] & 0xF)), i + 1);
            neighbourDistances = [..distanceCodes.Keys];

            // Every position is linked to the last earlier one that starts with the same three pixels.
            chain = new Int32[pixels.Length];
            var head = new Int32[1 << HashBits];
            Array.Fill(head, -1);
            for (var at = 0; at + 2 < pixels.Length; at++)
            {
                var hash = Hash(at);
                chain[at] = head[hash];
                head[hash] = at;
            }
        }

        public Int32 CodeFor(Int32 distance) =>
            distanceCodes.TryGetValue(distance, out var code) ? code : distance + NeighbourCodes.Length;

        /// <summary>Every copy worth considering at this position: ever longer ones further back, and the nearest neighbours.</summary>
        public void Find(Int32 at, List<(Int32 Length, Int32 Distance)> found)
        {
            found.Clear();
            var longest = Math.Min(MaxMatch, pixels.Length - at);
            if (longest < MinOptimalMatch) return;

            if (at + 2 < pixels.Length)
            {
                var bestLength = 0;
                var candidate = chain[at];
                for (var depth = 0; candidate >= 0 && depth < MaxChain; depth++, candidate = chain[candidate])
                {
                    var distance = at - candidate;
                    if (distance > MaxDistance) break;

                    var length = MatchLength(candidate, at, longest);
                    if (length <= bestLength) continue;

                    found.Add((length, distance));
                    bestLength = length;
                    if (length == longest) return;
                }
            }

            foreach (var distance in neighbourDistances)
            {
                if (distance > at) continue;

                var length = MatchLength(at - distance, at, longest);
                if (length >= MinOptimalMatch)
                    found.Add((length, distance));
            }
        }

        private Int32 MatchLength(Int32 earlier, Int32 later, Int32 longest)
        {
            var length = 0;
            while (length < longest && pixels[earlier + length] == pixels[later + length])
                length++;
            return length;
        }

        private Int32 Hash(Int32 at) =>
            (Int32)(unchecked(pixels[at] * 0x1E35A7BDu ^ pixels[at + 1] * 0x9E3779B1u ^ pixels[at + 2] * 0x85EBCA6Bu) >> (32 - HashBits));
    }
}
