using PixelWorld.DumpScanners;
using PixelWorld.Machines;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelWorld.Tools;

public static class FontHunter
{
    /// <param name="knownFolder">
    /// Optional folder of .ch8 fonts already collected; a found font identical to one of them is
    /// counted but not written.
    /// </param>
    public static void Hunt(List<String> fileNames, String outputFolder, String? knownFolder = null)
    {
        var known = LoadKnown(knownFolder);
        var inputsWithOutputsCount = 0;
        var outputCount = 0;
        var knownCount = 0;

        var inputCount = fileNames.Count;
        Out.Write($"\nHunting {inputCount} files");

        foreach (var fileName in fileNames)
        {
            Out.Write($"Opening file {fileName}");
            var (written, alreadyKnown) = ExtractFontsFromDumpFile(fileName, outputFolder, known);
            knownCount += alreadyKnown;
            if (written + alreadyKnown > 0)
            {
                inputsWithOutputsCount++;
                outputCount += written;
            }
        }

        Out.Write($"{inputsWithOutputsCount} files yielded {outputCount} new results");
        if (known.Count > 0)
            Out.Write($"{knownCount} more were already in {knownFolder}");

        // No inputs means there is no rate to report; 0/0 would print NaN.
        if (inputCount > 0)
            Out.Write($"{Math.Floor((Double)inputsWithOutputsCount / inputCount * 100)}% success rate");
    }

    private static HashSet<String> LoadKnown(String? knownFolder)
    {
        if (String.IsNullOrEmpty(knownFolder)) return [];

        var known = Directory.EnumerateFiles(knownFolder, "*.ch8")
            .Select(File.ReadAllBytes)
            .Where(b => b.Length >= Spectrum.FontSize)
            .Select(b => Convert.ToBase64String(b, 0, Spectrum.FontSize))
            .ToHashSet();
        Out.Write($"Loaded {known.Count} known fonts from {knownFolder}");
        return known;
    }

    private static (Int32 Written, Int32 Known) ExtractFontsFromDumpFile(String fileName, String outputFolder,
        HashSet<String> known)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var buffer = File.ReadAllBytes(fileName);
        Int32 written = 0, alreadyKnown = 0;

        foreach (var found in SpectrumDumpScanner.Scan(buffer))
        {
            var description = Describe(found);
            if (known.Contains(Convert.ToBase64String(found.Data)))
            {
                Out.Write($"  Already known #{found.Offset} {description}");
                alreadyKnown++;
                continue;
            }

            // Not Utils.MakeFileName: ChangeExtension would cut names containing a dot, such as "(U.S. Gold)".
            var newFileName = Path.Combine(outputFolder, $"{name}#{found.Offset}.ch8");
            Out.Write($"  Creating byte font {newFileName} {description}");
            File.WriteAllBytes(newFileName, found.Data);
            written++;
        }

        return (written, alreadyKnown);
    }

    private static String Describe(FoundFont found)
    {
        var parts = new List<String> { found.Score.ToString(), $"via {String.Join(", ", found.Methods.Select(m => m.DisplayName()))}" };
        if (!found.Range.IsFull)
            parts.Add($"partial {found.Range}");
        if (found.RomGlyphsChanged <= SpectrumDumpScanner.RomDerivedThreshold)
            parts.Add($"ROM-derived ({found.RomGlyphsChanged} glyphs changed)");
        return "[" + String.Join("; ", parts) + "]";
    }
}
