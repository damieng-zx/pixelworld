using System;
using System.Collections.Generic;
using System.Numerics;

namespace PixelWorld.OffsetFinders;

/// <summary>
/// Find the capitals by how much ink each one has. Whatever its style, a font draws M and B with more
/// pixels than I and L, so 26 glyphs in a row whose ink counts follow the average A-Z profile, and
/// that share a blank column or row, are almost certainly the alphabet. It needs nothing else to be
/// present, so it finds fonts the other finders cannot, such as those with digits straight after the
/// capitals or in a different style.
/// </summary>
public static class AlphabetProfileFinder
{
    /// <summary>Lowest correlation with the average profile.</summary>
    public const Double MinCorrelation = 0.75;

    /// <summary>
    /// Lowest <see cref="FontScore.Total"/>. Ink counts alone cannot tell letters from tiles that happen
    /// to be as dense, and what does is the structure the letters share: a score under 2 was nearly
    /// always graphics.
    /// </summary>
    public const Int32 MinScore = 2;

    /// <summary>
    /// Highest <see cref="FontScore.EdgeInk"/>. Capitals that nearly all touch both the top and bottom row
    /// are a font read a row or two off, or graphics; the fonts collected from RAM dumps almost never do.
    /// </summary>
    public const Int32 MaxEdgeInk = 40;

    /// <summary>
    /// Ink in each of A-Z relative to the mean of the 26, averaged over the fonts already collected from
    /// RAM dumps that the structural finder is sure of.
    /// </summary>
    private static readonly Double[] Profile =
    [
        1.13, 1.23, 0.87, 1.09, 1.10, 0.91, 1.03, 1.10, 0.81, 0.79, 1.00, 0.77, 1.19,
        1.17, 1.05, 1.00, 1.13, 1.16, 0.96, 0.76, 1.02, 0.90, 1.12, 0.95, 0.77, 0.99
    ];

    private const Int32 Letters = 26;

    public static List<Int32> FindOffsets(Byte[] buffer)
    {
        var offsets = new List<Int32>();
        var ink = InkCounts(buffer);

        var profileMean = Mean(Profile);
        var profileSpread = 0.0;
        foreach (var p in Profile)
            profileSpread += (p - profileMean) * (p - profileMean);

        var last = buffer.Length - FontStructure.GlyphAt(0, 'Z') - FontStructure.GlyphSize;
        Span<Double> counts = stackalloc Double[Letters];
        for (var fontBase = Math.Max(-FontStructure.GlyphAt(0, 'A'), 0); fontBase <= last; fontBase++)
        {
            if (!TryCount(ink, FontStructure.GlyphAt(fontBase, 'A'), counts)) continue;
            if (Correlation(counts, profileMean, profileSpread) < MinCorrelation) continue;

            var gutter = FontStructure.LearnGutter(buffer, fontBase);
            if (gutter.ColumnMask == 0 && !gutter.TopRow && !gutter.BottomRow) continue;

            var score = FontStructure.Score(buffer, fontBase);
            if (score.Total >= MinScore && score.EdgeInk <= MaxEdgeInk && score.DistinctLetters >= StructuralFinder.MinDistinctLetters)
                offsets.Add(fontBase);
        }

        return offsets;
    }

    /// <summary>How many pixels the glyph starting at each offset has.</summary>
    private static Byte[] InkCounts(Byte[] buffer)
    {
        var ink = new Byte[buffer.Length];
        var window = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            window += BitOperations.PopCount(buffer[i]);
            if (i >= FontStructure.GlyphSize)
                window -= BitOperations.PopCount(buffer[i - FontStructure.GlyphSize]);
            if (i >= FontStructure.GlyphSize - 1)
                ink[i - FontStructure.GlyphSize + 1] = (Byte)window;
        }

        return ink;
    }

    /// <summary>The ink counts of 26 glyphs, or false if one is blank or solid and so cannot be a letter.</summary>
    private static Boolean TryCount(Byte[] ink, Int32 first, Span<Double> counts)
    {
        for (var k = 0; k < Letters; k++)
        {
            var n = ink[first + k * FontStructure.GlyphSize];
            if (n == 0 || n == 64) return false;
            counts[k] = n;
        }

        return true;
    }

    private static Double Mean(ReadOnlySpan<Double> values)
    {
        var sum = 0.0;
        foreach (var v in values)
            sum += v;
        return sum / values.Length;
    }

    private static Double Correlation(ReadOnlySpan<Double> counts, Double profileMean, Double profileSpread)
    {
        var mean = Mean(counts);
        Double covariance = 0, spread = 0;
        for (var k = 0; k < Letters; k++)
        {
            var d = counts[k] - mean;
            covariance += d * (Profile[k] - profileMean);
            spread += d * d;
        }

        return spread == 0 ? 0 : covariance / Math.Sqrt(spread * profileSpread);
    }
}
