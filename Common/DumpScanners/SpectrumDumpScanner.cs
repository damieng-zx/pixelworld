using System;
using PixelWorld.Display;
using PixelWorld.Machines;
using PixelWorld.OffsetFinders;
using System.Collections.Generic;
using System.Linq;

namespace PixelWorld.DumpScanners;

/// <summary>A font found in a memory dump.</summary>
/// <param name="Offset">Where the font's space is, or would be if the font started there.</param>
/// <param name="Data">The 96 glyphs from space to ©, with any slot outside <paramref name="Range"/> blank.</param>
/// <param name="Methods">The finders that suggested this offset.</param>
/// <param name="RomGlyphsChanged">How many of the printable glyphs differ from the ROM font.</param>
/// <param name="Legacy">Accepted by the original rules (a complete font starting at a blank space).</param>
public sealed record FoundFont(
    Int32 Offset,
    Byte[] Data,
    CharRange Range,
    FontScore Score,
    IReadOnlyList<FinderMethod> Methods,
    Int32 RomGlyphsChanged,
    Boolean Legacy)
{
    /// <summary>The bytes of memory the font's present glyphs occupy.</summary>
    public Int32 Start => FontStructure.GlyphAt(Offset, Range.First);
    public Int32 End => FontStructure.GlyphAt(Offset, Range.Last + 1);
    public Boolean Overlaps(FoundFont other) => Start < other.End && other.Start < End;
}

public static class SpectrumDumpScanner
{
    /// <summary>Fonts with this few glyphs changed from the ROM are reported as ROM-derived.</summary>
    public const Int32 RomDerivedThreshold = 4;

    public static List<FoundFont> Scan(Byte[] buffer)
    {
        var suggested = GetOffsets(buffer);

        var accepted = new List<FoundFont>();
        foreach (var (offset, methods) in suggested)
        {
            var found = Accept(buffer, offset, methods);
            if (found is not null)
                accepted.Add(found);
        }

        return Merge(accepted);
    }

    /// <summary>
    /// An offset from the older finders that passes their original test (a complete font starting at a
    /// blank space) is accepted as a full font, as before. Anything else, including an older finder's
    /// offset that fails that test, is accepted only on its structure score, and may be a partial font.
    /// </summary>
    private static FoundFont? Accept(Byte[] buffer, Int32 offset, List<FinderMethod> methods)
    {
        var score = FontStructure.Score(buffer, offset);
        var legacy = methods.Any(m => m.IsOriginal());

        CharRange range;
        var acceptedAsLegacy = false;
        if (legacy && offset >= 0 && offset + Spectrum.FontSize <= buffer.Length
            && buffer.IsEmpty(offset) && !IsMissingTooManyGlyphs(buffer, offset))
        {
            range = CharRange.Full;
            acceptedAsLegacy = true;
        }
        else
        {
            var minScore = methods.Contains(FinderMethod.CodeRefs) ? CodeReferenceFinder.MinScore : StructuralFinder.MinScore;
            if (score.Total < minScore || score.DistinctLetters < StructuralFinder.MinDistinctLetters) return null;
            range = score.Range;
        }

        var data = FontStructure.Extract(buffer, offset, range);
        var romChanged = CountRomGlyphsChanged(data);
        if (romChanged == 0) return null;

        return new FoundFont(offset, data, range, score, methods, romChanged, acceptedAsLegacy);
    }

    /// <summary>
    /// Two fonts cannot occupy the same memory, so where finds overlap only one reading is right. Fonts
    /// accepted by the original rules are all kept, as before; a find from the newer finders is kept only
    /// if it overlaps none of those and no better-scoring new find. Then drop any font identical to one
    /// already kept from this dump.
    /// </summary>
    private static List<FoundFont> Merge(List<FoundFont> fonts)
    {
        var kept = fonts.Where(f => f.Legacy).ToList();

        foreach (var font in fonts.Where(f => !f.Legacy)
                     .OrderByDescending(f => f.Score.Total).ThenBy(f => f.Score.EdgeInk).ThenBy(f => f.Offset))
        {
            var overlapped = kept.FindIndex(font.Overlaps);
            if (overlapped < 0)
                kept.Add(font);
            else
                kept[overlapped] = kept[overlapped] with { Methods = [..kept[overlapped].Methods.Union(font.Methods)] };
        }

        // Where the same font is in memory twice, keep the copy the original rules found so file names
        // stay the same as earlier hunts.
        var seen = new HashSet<String>();
        return kept.OrderByDescending(f => f.Legacy).ThenBy(f => f.Offset)
            .Where(f => seen.Add(Convert.ToBase64String(f.Data)))
            .OrderBy(f => f.Offset)
            .ToList();
    }

    private static List<(Int32 Offset, List<FinderMethod> Methods)> GetOffsets(Byte[] buffer)
    {
        var address = buffer.Length >= Spectrum.AddressSpace ? Spectrum.ScreenStart : 0;
        var candidates = SpectrumDisplay.GetCandidates(buffer, address);

        var finders = new (FinderMethod Method, List<Int32> Offsets)[]
        {
            (FinderMethod.Chars, EnvironmentGuidedFinder.FindOffsets(buffer)),
            (FinderMethod.RomGlyphs, KnownCharPatternFinder.FindOffsets(buffer, rarelyChangedRomChars)),
            (FinderMethod.ScreenTiles, CandidatesInWindowFinder.FindOffsets(buffer, candidates)),
            (FinderMethod.Heuristics, GeneralHeuristicFinder.FindOffsets(buffer)),
            (FinderMethod.CodeRefs, CodeReferenceFinder.FindOffsets(buffer)),
            (FinderMethod.Structure, StructuralFinder.FindOffsets(buffer))
        };

        var byOffset = new SortedDictionary<Int32, List<FinderMethod>>();
        foreach (var (method, offsets) in finders)
        foreach (var offset in offsets)
        {
            if (!byOffset.TryGetValue(offset, out var methods))
                byOffset[offset] = methods = [];
            methods.Add(method);
        }

        // Code references suggest many readings of every address, nearly all wrong; only report those
        // that went on to be confirmed.
        foreach (var (method, offsets) in finders.Where(f => f.Method != FinderMethod.CodeRefs))
            OutFinderDetail(offsets, method, byOffset);

        return byOffset.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private static void OutFinderDetail(List<Int32> offsets, FinderMethod method, SortedDictionary<Int32, List<FinderMethod>> byOffset)
    {
        if (offsets.Count <= 0) return;

        var uniques = offsets.Count(o => byOffset[o].Count == 1);
        Out.Write($"  {method.DisplayName()} found offsets ({uniques} uniques) {String.Join(", ", offsets)}");
    }

    public static Boolean IsMissingTooManyGlyphs(Byte[] buffer, Int32 offset)
    {
        return buffer.CountBlankGlyphs(offset, Spectrum.FontSize, 8) > 26;
    }

    /// <summary>How many glyphs from ! to ~ differ from the ROM font; 0 means it is a copy of the ROM font.</summary>
    public static Int32 CountRomGlyphsChanged(Byte[] font)
    {
        var rom = Spectrum.RomFont;
        var changed = 0;
        for (var c = 1; c < 95; c++)
            if (!font.AsSpan(c * 8, 8).SequenceEqual(rom.Slice(c * 8, 8)))
                changed++;
        return changed;
    }

    private static readonly KnownCharPattern[] rarelyChangedRomChars =
    [
        new(3, [
            0b00000000,
            0b00100100,
            0b01111110,
            0b00100100,
            0b00100100,
            0b01111110,
            0b00100100,
            0b00000000
        ]),
        new(4, [
            0b00000000,
            0b00001000,
            0b00111110,
            0b00101000,
            0b00111110,
            0b00001010,
            0b00111110,
            0b00001000
        ]),
        new(6, [
            0b00000000,
            0b00010000,
            0b00101000,
            0b00010000,
            0b00101010,
            0b01000100,
            0b00111010,
            0b00000000
        ]),
        new(32, [
            0b00000000,
            0b00111100,
            0b01001010,
            0b01010110,
            0b01011110,
            0b01000000,
            0b00111100,
            0b00000000
        ]),
        new(64, [
            0b00000000,
            0b00011100,
            0b00100010,
            0b01111000,
            0b00100000,
            0b00100000,
            0b01111110,
            0b00000000
        ]),
        new(95, [
            0b00111100,
            0b01000010,
            0b10011001,
            0b10100001,
            0b10100001,
            0b10011001,
            0b01000010,
            0b00111100
        ])
    ];
}
