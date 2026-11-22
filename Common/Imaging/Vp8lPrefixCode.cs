using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelWorld.Imaging;

/// <summary>A canonical Huffman code, ready to write symbols with.</summary>
internal sealed class Vp8lPrefixCode
{
    private const Int32 MaxCodeLength = 15;
    private const Int32 MaxCodeLengthCodeLength = 7;
    private const Int32 LiteralSymbols = 256;

    /// <summary>The order the lengths of the code length code are written in.</summary>
    private static readonly Int32[] CodeLengthOrder = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    // Stored back to front so they can go into the stream least significant bit first.
    private readonly UInt32[] codes;

    // One symbol is free: the decoder knows which it is without any bits.
    private readonly Boolean single;

    private Vp8lPrefixCode(Int32[] lengths)
    {
        Lengths = lengths;
        codes = CanonicalCodes(lengths);
        single = lengths.Count(l => l > 0) == 1;
    }

    public Int32[] Lengths { get; }

    public void Write(Vp8lBitWriter bits, Int32 symbol)
    {
        if (single) return;
        bits.Write(codes[symbol], Lengths[symbol]);
    }

    /// <summary>Work out a code for the symbols used and write it in the stream's own compact form.</summary>
    public static Vp8lPrefixCode WriteNew(Vp8lBitWriter bits, Int32[] frequencies)
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

            return new Vp8lPrefixCode(lengths);
        }

        bits.Write(0, 1);
        WriteCodeLengths(bits, lengths);
        return new Vp8lPrefixCode(lengths);
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

    /// <summary>
    /// The lengths of a normal code are themselves run-length encoded and Huffman coded, using symbols 0 to 15
    /// as lengths, 16 to repeat the last length 3-6 times, 17 for 3-10 zeros and 18 for 11-138 zeros.
    /// </summary>
    private static void WriteCodeLengths(Vp8lBitWriter bits, Int32[] lengths)
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

        var code = new Vp8lPrefixCode(BuildLengths(frequencies, MaxCodeLengthCodeLength));
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
}
