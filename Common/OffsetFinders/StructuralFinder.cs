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

    /// <summary>
    /// Lowest <see cref="FontScore.Total"/> when nothing counts against the offset and nearly every capital
    /// is different (<see cref="MinDistinctLettersWhenClean"/>). Fonts that score a little under
    /// <see cref="MinScore"/> are mostly real, with a few odd glyphs; the penalty and the distinct letters
    /// are what separate them from graphics.
    /// </summary>
    public const Int32 MinCleanScore = 8;

    public const Int32 MinDistinctLettersWhenClean = 25;

    /// <summary>
    /// Fewest symmetric glyphs plus letter pairs sharing their tops for a base to be worth scoring. Low
    /// enough that no font found by full scoring is skipped.
    /// </summary>
    private const Int32 MinPrecheckMatches = 4;

    /// <summary>Whether a score is font-like enough to report an offset found with no other evidence.</summary>
    public static Boolean Accepts(FontScore score) =>
        score.DistinctLetters >= MinDistinctLetters && score.Total >= MinScore
        || score.DistinctLetters >= MinDistinctLettersWhenClean && score.Penalty == 0 && score.Total >= MinCleanScore;

    public static List<Int32> FindOffsets(Byte[] buffer)
    {
        var offsets = new List<Int32>();
        var index = new SymmetricIndex(buffer);

        // The base is where space would be; A to Z have to be in memory even when space is not.
        var first = -FontStructure.GlyphAt(0, 'A');
        var last = buffer.Length - FontStructure.GlyphAt(0, 'Z') - FontStructure.GlyphSize;

        for (var fontBase = Math.Max(first, 0); fontBase <= last; fontBase++)
        {
            // Cheap pre-check, since full scoring every offset is too slow for thousands of dumps.
            if (index.SymmetricCount(fontBase) + FontStructure.SharedTopCount(buffer, fontBase) < MinPrecheckMatches) continue;

            if (Accepts(FontStructure.Score(buffer, fontBase)))
                offsets.Add(fontBase);
        }

        return offsets;
    }

    /// <summary>
    /// Which offsets hold a symmetric glyph, worked out once so the scan can cheaply reject most bases
    /// before computing a full score.
    /// </summary>
    private sealed class SymmetricIndex
    {
        private readonly Byte[] symmetric;

        public SymmetricIndex(Byte[] buffer)
        {
            symmetric = new Byte[buffer.Length];
            for (var i = 0; i + FontStructure.GlyphSize <= buffer.Length; i++)
                symmetric[i] = FontStructure.IsSymmetric(buffer, i) ? (Byte)1 : (Byte)0;
        }

        /// <summary>How many of the glyphs that should be symmetric are, for the font based here.</summary>
        public Int32 SymmetricCount(Int32 fontBase)
        {
            var n = 0;
            foreach (var c in FontStructure.SymmetricChars)
            {
                var at = FontStructure.GlyphAt(fontBase, c);
                if (at >= 0 && at < symmetric.Length) n += symmetric[at];
            }

            return n;
        }
    }
}
