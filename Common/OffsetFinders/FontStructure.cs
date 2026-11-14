using System;
using System.Collections.Generic;
using System.Numerics;

namespace PixelWorld.OffsetFinders;

/// <summary>
/// How font-like the 96 glyphs starting at a base offset are, judged by how the glyphs relate to each
/// other rather than by what any one glyph looks like. Random data, code and sprite graphics almost
/// never have mirror-image bracket pairs, letters that share top halves or a consistent baseline,
/// whereas nearly every hand-drawn font does.
/// </summary>
/// <param name="Mirror">Pairs such as ( and ) that are left-right mirror images of each other.</param>
/// <param name="Tops">Pairs such as P and R whose top rows are identical.</param>
/// <param name="Symmetric">Glyphs such as A, H and O that are left-right symmetric.</param>
/// <param name="Descenders">Lowercase letters g j p q y that drop below the uppercase baseline.</param>
/// <param name="Gutter">1 for a blank column shared by the letters, plus 1 for a blank row.</param>
/// <param name="DistinctLetters">How many A-Z glyphs have ink and differ from each other.</param>
/// <param name="Penalty">
/// Evidence the slots are wrong: symmetric glyphs where B, C, 2 and the like belong, or letters such as
/// a, c and o dropping below the baseline. Reading a real font a few characters off its true start
/// still finds symmetric glyphs and descenders, just in the wrong slots.
/// </param>
/// <param name="EdgeInk">
/// How many A-Z glyphs have ink in their top row plus how many have ink in their bottom row. Reading a
/// font a byte or two off its true start moves every row up or down without spoiling any of the other
/// measures; most fonts keep an empty row above and below the capitals, so the lowest wins a tie.
/// </param>
public readonly record struct FontScore(
    Int32 Mirror,
    Int32 Tops,
    Int32 Symmetric,
    Int32 Descenders,
    Int32 Gutter,
    Int32 DistinctLetters,
    Int32 Penalty,
    Int32 EdgeInk)
{
    public Int32 Total => Mirror + Tops + Symmetric + Descenders + Gutter - Penalty;

    public override String ToString() =>
        $"score {Total} (mirror {Mirror}, tops {Tops}, symmetric {Symmetric}, descenders {Descenders}, gutter {Gutter}, penalty {Penalty}, distinct letters {DistinctLetters}, edge ink {EdgeInk})";
}

/// <summary>The character codes of a font that are actually present in memory.</summary>
public readonly record struct CharRange(Int32 First, Int32 Last)
{
    public static readonly CharRange Full = new(32, 127);
    public Boolean IsFull => First == 32 && Last == 127;
    public override String ToString() => $"'{(Char)First}'..'{(Char)Last}'";
}

public static class FontStructure
{
    private const Int32 GlyphSize = 8;
    private const Int32 BaseChar = 32;

    private static readonly (Char Left, Char Right)[] MirrorPairs =
        [('(', ')'), ('[', ']'), ('{', '}'), ('<', '>'), ('/', '\\'), ('b', 'd'), ('p', 'q')];

    // Pairs that usually share their top rows, and how many rows to compare.
    private static readonly (Char First, Char Second, Int32 Rows)[] SharedTops =
        [('P', 'R', 4), ('O', 'Q', 4), ('E', 'F', 3), ('C', 'G', 3), ('b', 'h', 3), ('i', 'j', 4)];

    private const String SymmetricChars = "AHIMOTUVWXY08";
    private const String AsymmetricChars = "BCDEFGJKLNPRSZ234579";
    private const String Descenders = "gjpqy";
    private const String NonDescenders = "acemnorsuvwxz";
    private const String BaselineChars = "ABCDEFGHIJKLMNOPRSTUVWXYZ0123456789";

    private static readonly Byte[] Reverse = BuildReverseTable();

    private static Byte[] BuildReverseTable()
    {
        var table = new Byte[256];
        for (var i = 0; i < 256; i++)
        {
            var r = 0;
            for (var b = 0; b < 8; b++)
                if ((i & (1 << b)) != 0)
                    r |= 0x80 >> b;
            table[i] = (Byte)r;
        }

        return table;
    }

    /// <summary>Where the glyph for <paramref name="c"/> lives if the font's space is at <paramref name="fontBase"/>.</summary>
    public static Int32 GlyphAt(Int32 fontBase, Int32 c) => fontBase + (c - BaseChar) * GlyphSize;

    public static Boolean InRange(Byte[] buffer, Int32 at) => at >= 0 && at + GlyphSize <= buffer.Length;

    /// <summary>Has some ink but is not a solid block, so it could be a drawn character.</summary>
    public static Boolean IsInked(Byte[] buffer, Int32 at)
    {
        if (!InRange(buffer, at)) return false;
        Byte or = 0, and = 0xFF;
        for (var y = 0; y < GlyphSize; y++)
        {
            or |= buffer[at + y];
            and &= buffer[at + y];
        }

        return or != 0 && and != 0xFF;
    }

    private static Boolean SameGlyph(Byte[] buffer, Int32 a, Int32 b)
    {
        for (var y = 0; y < GlyphSize; y++)
            if (buffer[a + y] != buffer[b + y])
                return false;
        return true;
    }

    /// <summary>
    /// Whether glyph <paramref name="b"/> flipped left-right is glyph <paramref name="a"/>, ignoring where each
    /// sits horizontally. A 7-pixel-wide ( drawn hard left mirrors to a ) drawn hard right, so both are
    /// slid to the left edge before comparing.
    /// </summary>
    public static Boolean IsMirrorOf(Byte[] buffer, Int32 a, Int32 b)
    {
        Byte orA = 0, orB = 0;
        for (var y = 0; y < GlyphSize; y++)
        {
            orA |= buffer[a + y];
            orB |= Reverse[buffer[b + y]];
        }

        if (orA == 0 || orB == 0) return false;

        var shiftA = BitOperations.LeadingZeroCount((UInt32)orA) - 24;
        var shiftB = BitOperations.LeadingZeroCount((UInt32)orB) - 24;
        for (var y = 0; y < GlyphSize; y++)
            if (((buffer[a + y] << shiftA) & 0xFF) != ((Reverse[buffer[b + y]] << shiftB) & 0xFF))
                return false;
        return true;
    }

    public static Boolean IsSymmetric(Byte[] buffer, Int32 at) =>
        IsSubstantial(buffer, at) && IsMirrorOf(buffer, at, at);

    /// <summary>
    /// At least two columns wide and three rows tall. A lone vertical line or a dot is trivially its own
    /// mirror image, and lines of such fragments are common in graphics data.
    /// </summary>
    public static Boolean IsSubstantial(Byte[] buffer, Int32 at)
    {
        if (!IsInked(buffer, at)) return false;
        Byte or = 0;
        var rows = 0;
        for (var y = 0; y < GlyphSize; y++)
        {
            or |= buffer[at + y];
            if (buffer[at + y] != 0) rows++;
        }

        // Span from the leftmost to the rightmost inked column.
        var width = 32 - BitOperations.LeadingZeroCount((UInt32)or) - BitOperations.TrailingZeroCount((UInt32)or);
        return rows >= 3 && width >= 2;
    }

    private static Int32 BottomInkRow(Byte[] buffer, Int32 at)
    {
        for (var y = GlyphSize - 1; y >= 0; y--)
            if (buffer[at + y] != 0)
                return y;
        return -1;
    }

    public static FontScore Score(Byte[] buffer, Int32 fontBase)
    {
        // Only judge the slots that belong to the font: the bytes after a capitals-only font are not
        // lowercase letters and would otherwise count against it.
        var range = DetectRange(buffer, fontBase);
        Boolean In(Char c) => c >= range.First && c <= range.Last;

        var mirror = 0;
        foreach (var (left, right) in MirrorPairs)
        {
            if (!In(left) || !In(right)) continue;
            Int32 a = GlyphAt(fontBase, left), b = GlyphAt(fontBase, right);
            if (IsSubstantial(buffer, a) && IsSubstantial(buffer, b) && !SameGlyph(buffer, a, b) && IsMirrorOf(buffer, a, b))
                mirror++;
        }

        var tops = 0;
        foreach (var (first, second, rows) in SharedTops)
        {
            if (!In(first) || !In(second)) continue;
            Int32 a = GlyphAt(fontBase, first), b = GlyphAt(fontBase, second);
            if (!InRange(buffer, a) || !InRange(buffer, b) || SameGlyph(buffer, a, b)) continue;
            var same = true;
            Byte ink = 0;
            for (var y = 0; y < rows && same; y++)
            {
                same = buffer[a + y] == buffer[b + y];
                ink |= buffer[a + y];
            }

            if (same && ink != 0) tops++;
        }

        // Distinct ones only: a tile repeated across the slots is not 13 symmetric letters.
        var symmetricGlyphs = new HashSet<UInt64>();
        foreach (var c in SymmetricChars)
        {
            var at = GlyphAt(fontBase, c);
            if (In(c) && IsSymmetric(buffer, at))
                symmetricGlyphs.Add(BitConverter.ToUInt64(buffer, at));
        }

        var symmetric = symmetricGlyphs.Count;

        var penalty = 0;
        foreach (var c in AsymmetricChars)
            if (In(c) && IsSymmetric(buffer, GlyphAt(fontBase, c)))
                penalty++;

        var (descenders, wrongDescenders) = range.Last >= 'z' ? CountDescenders(buffer, fontBase, range.First) : (0, 0);
        penalty += wrongDescenders;

        var gutter = GutterScore(buffer, fontBase);
        var distinct = CountDistinctLetters(buffer, fontBase);

        var edgeInk = 0;
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var at = GlyphAt(fontBase, c);
            if (!InRange(buffer, at)) continue;
            if (buffer[at] != 0) edgeInk++;
            if (buffer[at + GlyphSize - 1] != 0) edgeInk++;
        }

        return new FontScore(mirror, tops, symmetric, descenders, gutter, distinct, penalty, edgeInk);
    }

    /// <summary>
    /// How many of g j p q y drop below the baseline the capitals and digits agree on, and how many
    /// letters that should sit on it, such as a, c and o, drop below it too.
    /// </summary>
    private static (Int32 Descenders, Int32 Wrong) CountDescenders(Byte[] buffer, Int32 fontBase, Int32 first)
    {
        Span<Int32> bottoms = stackalloc Int32[GlyphSize];
        var inked = 0;
        foreach (var c in BaselineChars)
        {
            var at = GlyphAt(fontBase, c);
            if (c < first || !IsInked(buffer, at)) continue;
            bottoms[BottomInkRow(buffer, at)]++;
            inked++;
        }

        if (inked < 20) return (0, 0);

        var baseline = 0;
        for (var y = 1; y < GlyphSize; y++)
            if (bottoms[y] > bottoms[baseline])
                baseline = y;

        // Most letters have to agree on a baseline for a descender to mean anything.
        if (bottoms[baseline] * 10 < inked * 6 || baseline == GlyphSize - 1) return (0, 0);

        Int32 Below(String chars)
        {
            var count = 0;
            foreach (var c in chars)
            {
                var at = GlyphAt(fontBase, c);
                if (IsInked(buffer, at) && BottomInkRow(buffer, at) > baseline)
                    count++;
            }

            return count;
        }

        return (Below(Descenders), Below(NonDescenders));
    }

    /// <summary>Which outer columns (as a bit mask) and rows the letters A-Z leave blank in at least 90% of glyphs.</summary>
    public static (Byte ColumnMask, Boolean TopRow, Boolean BottomRow) LearnGutter(Byte[] buffer, Int32 fontBase)
    {
        Int32 inked = 0, left = 0, right = 0, top = 0, bottom = 0;
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var at = GlyphAt(fontBase, c);
            if (!IsInked(buffer, at)) continue;
            inked++;
            Byte or = 0;
            for (var y = 0; y < GlyphSize; y++) or |= buffer[at + y];
            if ((or & 0x80) == 0) left++;
            if ((or & 0x01) == 0) right++;
            if (buffer[at] == 0) top++;
            if (buffer[at + GlyphSize - 1] == 0) bottom++;
        }

        if (inked < 20) return (0, false, false);
        Boolean Mostly(Int32 n) => n * 10 >= inked * 9;
        var mask = (Byte)((Mostly(left) ? 0x80 : 0) | (Mostly(right) ? 0x01 : 0));
        return (mask, Mostly(top), Mostly(bottom));
    }

    private static Int32 GutterScore(Byte[] buffer, Int32 fontBase)
    {
        var (mask, top, bottom) = LearnGutter(buffer, fontBase);
        return (mask != 0 ? 1 : 0) + (top || bottom ? 1 : 0);
    }

    private static Int32 CountDistinctLetters(Byte[] buffer, Int32 fontBase)
    {
        var seen = new HashSet<UInt64>();
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var at = GlyphAt(fontBase, c);
            if (IsInked(buffer, at))
                seen.Add(BitConverter.ToUInt64(buffer, at));
        }

        return seen.Count;
    }

    /// <summary>
    /// Work out which part of the font is really there. Many games only define digits and capitals, so the
    /// slots around them hold unrelated data. A slot belongs to the font when it is blank or respects the
    /// blank columns the capitals share; the font starts at the earliest of space, !, 0 or A from which
    /// nearly every slot up to Z belongs, and keeps lowercase only when that block belongs too.
    /// </summary>
    public static CharRange DetectRange(Byte[] buffer, Int32 fontBase)
    {
        var (mask, _, _) = LearnGutter(buffer, fontBase);

        Boolean Fits(Int32 c)
        {
            var at = GlyphAt(fontBase, c);
            if (!InRange(buffer, at)) return false;
            Byte or = 0, and = 0xFF;
            for (var y = 0; y < GlyphSize; y++)
            {
                or |= buffer[at + y];
                and &= buffer[at + y];
            }

            return or == 0 || (and != 0xFF && (or & mask) == 0);
        }

        Int32 FitCount(Int32 from, Int32 to)
        {
            var n = 0;
            for (var c = from; c <= to; c++)
                if (Fits(c))
                    n++;
            return n;
        }

        var first = 'A';
        foreach (var start in new[] { 32, 33, '0', 'A' })
        {
            // A real space is blank; without one, the font starting at ! is the better reading.
            if (start == 32 && (!InRange(buffer, fontBase) || !buffer.IsEmpty(fontBase))) continue;
            var span = 'Z' - start + 1;
            if (FitCount(start, 'Z') * 100 >= span * 92)
            {
                first = (Char)start;
                break;
            }
        }

        // Lowercase counts when its slots fit and enough of them have ink: a run of blanks or a run of
        // copies of the capitals is a font without lowercase, not a font with blank lowercase.
        var lowerInked = 0;
        for (var c = 'a'; c <= 'z'; c++)
            if (IsInked(buffer, GlyphAt(fontBase, c)))
                lowerInked++;

        var last = FitCount('[', 127) * 100 >= 37 * 90 && lowerInked >= 20 ? 127 : 'Z';
        return new CharRange(first, last);
    }

    /// <summary>The 768-byte font from space to ©, with any slot outside <paramref name="range"/> blank.</summary>
    public static Byte[] Extract(Byte[] buffer, Int32 fontBase, CharRange range)
    {
        var font = new Byte[96 * GlyphSize];
        for (var c = range.First; c <= range.Last; c++)
        {
            var at = GlyphAt(fontBase, c);
            if (InRange(buffer, at))
                Array.Copy(buffer, at, font, (c - BaseChar) * GlyphSize, GlyphSize);
        }

        return font;
    }

    /// <summary>
    /// Precomputed per-offset flags so a scan of every offset can cheaply reject most of them before
    /// computing a full score.
    /// </summary>
    public sealed class Index
    {
        private readonly Byte[] symmetric;

        public Index(Byte[] buffer)
        {
            symmetric = new Byte[buffer.Length];
            for (var i = 0; i + GlyphSize <= buffer.Length; i++)
                symmetric[i] = IsSymmetric(buffer, i) ? (Byte)1 : (Byte)0;
        }

        public Int32 SymmetricCount(Int32 fontBase)
        {
            var n = 0;
            foreach (var c in SymmetricChars)
            {
                var at = GlyphAt(fontBase, c);
                if (at >= 0 && at < symmetric.Length) n += symmetric[at];
            }

            return n;
        }
    }

    public static Int32 SharedTopCount(Byte[] buffer, Int32 fontBase)
    {
        var n = 0;
        foreach (var (first, second, rows) in SharedTops)
        {
            Int32 a = GlyphAt(fontBase, first), b = GlyphAt(fontBase, second);
            if (!InRange(buffer, a) || !InRange(buffer, b)) continue;
            var same = true;
            Byte ink = 0;
            for (var y = 0; y < rows && same; y++)
            {
                same = buffer[a + y] == buffer[b + y];
                ink |= buffer[a + y];
            }

            if (same && ink != 0 && !SameGlyph(buffer, a, b)) n++;
        }

        return n;
    }
}
