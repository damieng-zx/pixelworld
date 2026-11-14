using System;
using System.Collections.Generic;
using System.Numerics;
using PixelWorld.Machines;

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
/// <param name="Range">The slots that were judged: those <see cref="FontStructure.DetectRange"/> found present.</param>
public readonly record struct FontScore(
    Int32 Mirror,
    Int32 Tops,
    Int32 Symmetric,
    Int32 Descenders,
    Int32 Gutter,
    Int32 DistinctLetters,
    Int32 Penalty,
    Int32 EdgeInk,
    CharRange Range)
{
    public Int32 Total => Mirror + Tops + Symmetric + Descenders + Gutter - Penalty;

    public override String ToString() =>
        $"score {Total} (mirror {Mirror}, tops {Tops}, symmetric {Symmetric}, descenders {Descenders}, gutter {Gutter}, penalty {Penalty}, distinct letters {DistinctLetters}, edge ink {EdgeInk})";
}

public static class FontStructure
{
    public const Int32 GlyphSize = 8;

    // Tuning, set by checking what was found in the RAM dump collection against the fonts already
    // collected from it. Re-run that check after changing any of these.

    /// <summary>Fewest inked glyphs before a gutter or baseline they share means anything.</summary>
    private const Int32 MinInkedToAgree = 20;

    /// <summary>Percentage of capitals that must leave an outer column or row blank for it to be a gutter.</summary>
    private const Int32 GutterAgreementPercent = 90;

    /// <summary>Percentage of capitals and digits that must end on the same row for it to be the baseline.</summary>
    private const Int32 BaselineAgreementPercent = 60;

    /// <summary>Percentage of the slots from a possible first character to Z that must fit the gutter.</summary>
    private const Int32 StartFitPercent = 92;

    /// <summary>Percentage of the slots from [ to (c) that must fit the gutter for lowercase to be present.</summary>
    private const Int32 LowercaseFitPercent = 90;

    /// <summary>Fewest inked lowercase letters for lowercase to be present.</summary>
    private const Int32 MinInkedLowercase = 20;

    private static readonly (Char Left, Char Right)[] MirrorPairs =
        [('(', ')'), ('[', ']'), ('{', '}'), ('<', '>'), ('/', '\\'), ('b', 'd'), ('p', 'q')];

    // Pairs that usually share their top rows, and how many rows to compare.
    private static readonly (Char First, Char Second, Int32 Rows)[] SharedTops =
        [('P', 'R', 4), ('O', 'Q', 4), ('E', 'F', 3), ('C', 'G', 3), ('b', 'h', 3), ('i', 'j', 4)];

    internal const String SymmetricChars = "AHIMOTUVWXY08";
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
    public static Int32 GlyphAt(Int32 fontBase, Int32 c) => fontBase + (c - CharRange.Space) * GlyphSize;

    public static Boolean InRange(Byte[] buffer, Int32 at) => at >= 0 && at + GlyphSize <= buffer.Length;

    /// <summary>
    /// The glyph's rows ORed together (the columns with any ink) and ANDed together (the columns inked
    /// in every row).
    /// </summary>
    private static (Byte Or, Byte And) Ink(Byte[] buffer, Int32 at)
    {
        Byte or = 0, and = 0xFF;
        for (var y = 0; y < GlyphSize; y++)
        {
            or |= buffer[at + y];
            and &= buffer[at + y];
        }

        return (or, and);
    }

    /// <summary>Has some ink but is not a solid block, so it could be a drawn character.</summary>
    public static Boolean IsInked(Byte[] buffer, Int32 at)
    {
        if (!InRange(buffer, at)) return false;
        var (or, and) = Ink(buffer, at);
        return or != 0 && and != 0xFF;
    }

    /// <summary>Whether two different glyphs have the same, inked, top <paramref name="rows"/> rows.</summary>
    private static Boolean SharesTop(Byte[] buffer, Int32 a, Int32 b, Int32 rows)
    {
        if (!InRange(buffer, a) || !InRange(buffer, b)) return false;
        Byte ink = 0;
        for (var y = 0; y < rows; y++)
        {
            if (buffer[a + y] != buffer[b + y]) return false;
            ink |= buffer[a + y];
        }

        return ink != 0 && !SameGlyph(buffer, a, b);
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
        var orA = Ink(buffer, a).Or;
        var orB = Reverse[Ink(buffer, b).Or];
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
        var or = Ink(buffer, at).Or;
        var rows = 0;
        for (var y = 0; y < GlyphSize; y++)
            if (buffer[at + y] != 0)
                rows++;

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
        var gutter = LearnGutter(buffer, fontBase);

        // Only judge the slots that belong to the font: the bytes after a capitals-only font are not
        // lowercase letters and would otherwise count against it.
        var range = DetectRange(buffer, fontBase, gutter.ColumnMask);
        Boolean In(Char c) => c >= range.First && c <= range.Last;

        var mirror = MirrorCount(buffer, fontBase, range);

        var tops = 0;
        foreach (var (first, second, rows) in SharedTops)
        {
            if (In(first) && In(second) && SharesTop(buffer, GlyphAt(fontBase, first), GlyphAt(fontBase, second), rows))
                tops++;
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

        var gutterScore = (gutter.ColumnMask != 0 ? 1 : 0) + (gutter.TopRow || gutter.BottomRow ? 1 : 0);
        var distinct = CountDistinctLetters(buffer, fontBase);

        var edgeInk = 0;
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var at = GlyphAt(fontBase, c);
            if (!InRange(buffer, at)) continue;
            if (buffer[at] != 0) edgeInk++;
            if (buffer[at + GlyphSize - 1] != 0) edgeInk++;
        }

        return new FontScore(mirror, tops, symmetric, descenders, gutterScore, distinct, penalty, edgeInk, range);
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

        if (inked < MinInkedToAgree) return (0, 0);

        var baseline = 0;
        for (var y = 1; y < GlyphSize; y++)
            if (bottoms[y] > bottoms[baseline])
                baseline = y;

        // Most letters have to agree on a baseline for a descender to mean anything.
        if (bottoms[baseline] * 100 < inked * BaselineAgreementPercent || baseline == GlyphSize - 1) return (0, 0);

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

    /// <summary>
    /// Which outer columns (as a bit mask) and rows nearly all the letters A-Z leave blank
    /// (<see cref="GutterAgreementPercent"/>).
    /// </summary>
    public static (Byte ColumnMask, Boolean TopRow, Boolean BottomRow) LearnGutter(Byte[] buffer, Int32 fontBase)
    {
        Int32 inked = 0, left = 0, right = 0, top = 0, bottom = 0;
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var at = GlyphAt(fontBase, c);
            if (!IsInked(buffer, at)) continue;
            inked++;
            var or = Ink(buffer, at).Or;
            if ((or & 0x80) == 0) left++;
            if ((or & 0x01) == 0) right++;
            if (buffer[at] == 0) top++;
            if (buffer[at + GlyphSize - 1] == 0) bottom++;
        }

        if (inked < MinInkedToAgree) return (0, false, false);
        Boolean Mostly(Int32 n) => n * 100 >= inked * GutterAgreementPercent;
        var mask = (Byte)((Mostly(left) ? 0x80 : 0) | (Mostly(right) ? 0x01 : 0));
        return (mask, Mostly(top), Mostly(bottom));
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
    public static CharRange DetectRange(Byte[] buffer, Int32 fontBase) =>
        DetectRange(buffer, fontBase, LearnGutter(buffer, fontBase).ColumnMask);

    private static CharRange DetectRange(Byte[] buffer, Int32 fontBase, Byte mask)
    {
        Boolean Fits(Int32 c)
        {
            var at = GlyphAt(fontBase, c);
            if (!InRange(buffer, at)) return false;
            var (or, and) = Ink(buffer, at);
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
        foreach (var start in new[] { CharRange.Space, '!', '0', 'A' })
        {
            // A real space is blank; without one, the font starting at ! is the better reading.
            if (start == CharRange.Space && (!InRange(buffer, fontBase) || !buffer.IsEmpty(fontBase))) continue;
            var span = 'Z' - start + 1;
            if (FitCount(start, 'Z') * 100 >= span * StartFitPercent)
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

        const Int32 afterCapitals = 'Z' + 1;
        const Int32 afterCapitalsCount = CharRange.Copyright - afterCapitals + 1;
        var hasLowercase = FitCount(afterCapitals, CharRange.Copyright) * 100 >= afterCapitalsCount * LowercaseFitPercent
                           && lowerInked >= MinInkedLowercase;
        return new CharRange(first, hasLowercase ? CharRange.Copyright : 'Z');
    }

    /// <summary>The 768-byte font from space to ©, with any slot outside <paramref name="range"/> blank.</summary>
    public static Byte[] Extract(Byte[] buffer, Int32 fontBase, CharRange range)
    {
        var font = new Byte[Spectrum.FontSize];
        for (var c = range.First; c <= range.Last; c++)
        {
            var at = GlyphAt(fontBase, c);
            if (InRange(buffer, at))
                Array.Copy(buffer, at, font, GlyphAt(0, c), GlyphSize);
        }

        return font;
    }

    /// <summary>How many of the pairs such as ( and ) in <paramref name="range"/> are mirror images of each other.</summary>
    public static Int32 MirrorCount(Byte[] buffer, Int32 fontBase, CharRange range)
    {
        var n = 0;
        foreach (var (left, right) in MirrorPairs)
        {
            if (left < range.First || right > range.Last) continue;
            Int32 a = GlyphAt(fontBase, left), b = GlyphAt(fontBase, right);
            if (IsSubstantial(buffer, a) && IsSubstantial(buffer, b) && !SameGlyph(buffer, a, b) && IsMirrorOf(buffer, a, b))
                n++;
        }

        return n;
    }

    /// <summary>How many of the pairs such as P and R share their top rows, whatever the font's range.</summary>
    public static Int32 SharedTopCount(Byte[] buffer, Int32 fontBase)
    {
        var n = 0;
        foreach (var (first, second, rows) in SharedTops)
            if (SharesTop(buffer, GlyphAt(fontBase, first), GlyphAt(fontBase, second), rows))
                n++;

        return n;
    }
}
