using System;
using System.Collections.Generic;

namespace PixelWorld.OffsetFinders;

/// <summary>
/// Scan every offset for a font by how its glyphs relate to each other (mirror-image brackets, letters
/// with matching tops, symmetric capitals, descenders, blank gutters) rather than requiring particular
/// glyphs at particular slots. Works on fonts that lack a space, symbols or lowercase.
/// </summary>
public static class StructuralFinder
{
    /// <summary>Lowest <see cref="FontScore.Total"/> to report an offset found with no other evidence.</summary>
    public const Int32 MinScore = 12;

    /// <summary>Fewest distinct A-Z glyphs, so tables of one repeated tile are not mistaken for letters.</summary>
    public const Int32 MinDistinctLetters = 22;

    public static List<Int32> FindOffsets(Byte[] buffer, Int32 minScore = MinScore)
    {
        var offsets = new List<Int32>();
        var index = new FontStructure.Index(buffer);

        // The base is where space would be; A to Z have to be in memory even when space is not.
        var first = -FontStructure.GlyphAt(0, 'A');
        var last = buffer.Length - FontStructure.GlyphAt(0, 'Z') - 8;

        for (var fontBase = Math.Max(first, 0); fontBase <= last; fontBase++)
        {
            // Cheap pre-check, since full scoring every offset is too slow for thousands of dumps.
            if (index.SymmetricCount(fontBase) + FontStructure.SharedTopCount(buffer, fontBase) < 4) continue;

            var score = FontStructure.Score(buffer, fontBase);
            if (score.Total >= minScore && score.DistinctLetters >= MinDistinctLetters)
                offsets.Add(fontBase);
        }

        return offsets;
    }
}
