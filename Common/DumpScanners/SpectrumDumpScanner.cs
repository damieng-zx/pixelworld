using System;
using PixelWorld.Display;
using PixelWorld.Formatters;
using PixelWorld.Machines;
using PixelWorld.OffsetFinders;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Font = PixelWorld.Fonts.Font;

namespace PixelWorld.DumpScanners;

public static class SpectrumDumpScanner
{
    public static List<Font> Read(BinaryReader reader, String name)
    {
        var buffer = reader.ReadBytes(1024 * 2048); // 2MB is enough for any Speccy

        var offsets = GetOffsets(buffer);

        var fonts = new List<Font>();
        foreach (var offset in offsets)
            if (!IsRomFont(buffer, offset) && buffer.IsEmpty(offset) && !IsMissingTooManyGlyphs(buffer, offset))
                fonts.Add(ByteFontFormatter.Create(reader, $"{name}#{offset}", offset, Spectrum.UK));

        return fonts;
    }

    private static IEnumerable<Int32> GetOffsets(Byte[] buffer)
    {
        // 128K dumps keep the 64K address space at the start, so the screen is at 16384 in those too.
        var address = buffer.Length >= Spectrum.AddressSpace ? Spectrum.ScreenStart : 0;
        var candidates = SpectrumDisplay.GetCandidates(buffer, address);

        var rst = EnvironmentGuidedFinder.FindOffsets(buffer);
        var rom = KnownCharPatternFinder.FindOffsets(buffer, rarelyChangedRomChars);
        var scr = CandidatesInWindowFinder.FindOffsets(buffer, candidates);
        var heu = GeneralHeuristicFinder.FindOffsets(buffer);

        var offsets = new List<Int32>();
        offsets.AddRange(rst);
        offsets.AddRange(rom);
        offsets.AddRange(scr);
        offsets.AddRange(heu);

        var dupes = new HashSet<Int32>(offsets
            .GroupBy(o => o)
            .Where(o => o.Count() > 1)
            .Select(g => g.Key));

        OutFinderDetail(rst, "RST 16/CHARS", dupes);
        OutFinderDetail(rom, "ROM Glyphs", dupes);
        OutFinderDetail(scr, "SCREEN$ Tiles", dupes);
        OutFinderDetail(heu, "Heuristics", dupes);

        return new HashSet<Int32>(offsets);
    }

    public static void OutFinderDetail(List<Int32> offsets, String method, HashSet<Int32> dupes)
    {
        if (offsets.Count <= 0) return;

        var uniques = offsets.Count(o => !dupes.Contains(o));
        Out.Write($"  {method} found offsets ({uniques} uniques) {string.Join(", ", offsets)}");
    }

    public static Boolean IsMissingTooManyGlyphs(Byte[] buffer, Int32 offset)
    {
        return buffer.CountBlankGlyphs(offset, Spectrum.FontSize, 8) > 26;
    }

    public static Boolean IsRomFont(Byte[] buffer, Int32 offset)
    {
        // Compared byte for byte: the SHA-384 this used to check matched no copy of the ROM font.
        return offset >= 0 && offset + Spectrum.FontSize <= buffer.Length &&
               buffer.AsSpan(offset, Spectrum.FontSize).SequenceEqual(Spectrum.RomFont);
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