using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PixelWorld.Formatters;
using PixelWorld.Machines;
using PixelWorld.Transformers;
using PixelWorld.Imaging;

namespace PixelWorld.Tools;

public static class ConvertToGameBoy
{
    public static void GbStudio(List<String> fileNames, IReadOnlyDictionary<Int32, Char> sourceCharset, String outputFolder, Boolean dark, Boolean proportional)
    {
        foreach (var fileName in fileNames)
        {
            Out.Write($"Generating GBStudio files for {fileName}");
            using var source = File.OpenRead(fileName);
            using var reader = new BinaryReader(source);
            var fontName = Path.GetFileNameWithoutExtension(fileName);
            var font = ByteFontFormatter.Create(reader, fontName, 0, sourceCharset);

            var outFileName = Path.Combine(outputFolder, fontName);
            {
                var image = CreateFilledGbsBitmap(GameBoy.Palette[3]);
                font.DrawImage(image, 16, GameBoy.Studio, GameBoy.Palette[0], GameBoy.Palette[3]);
                SavePng(image, Path.ChangeExtension(outFileName, "png"));
                File.WriteAllText(Path.ChangeExtension(outFileName, "json"), JsonSerializer.Serialize(new GBJson(fontName + " Mono"), gbStudioJsonOptions));
            }

            if (dark)
            {
                var darkFileName = outFileName + "-dark";
                var image = CreateFilledGbsBitmap(GameBoy.Palette[0]);
                font.DrawImage(image, 16, GameBoy.Studio, GameBoy.Palette[3], GameBoy.Palette[0]);
                SavePng(image, Path.ChangeExtension(darkFileName, "png"));
                File.WriteAllText(Path.ChangeExtension(darkFileName, "json"), JsonSerializer.Serialize(new GBJson(fontName + " Mono Dark"), gbStudioJsonOptions));
            }

            if (proportional)
            {
                var varFileName = outFileName + "-var";
                var varFont = FontSpacer.MakeProportional(font, 0, 1, 8);
                var image = CreateFilledGbsBitmap(Color.Magenta);
                varFont.DrawImage(image, 16, GameBoy.Studio, GameBoy.Palette[0], GameBoy.Palette[3], 8);
                SavePng(image, Path.ChangeExtension(varFileName, "png"));
                File.WriteAllText(Path.ChangeExtension(varFileName, "json"), JsonSerializer.Serialize(new GBJson(fontName + " Variable Width"), gbStudioJsonOptions));
            }
        }
    }

    private record GBJson(String name)
    {
        public Object mapping = new { };
    };

    private static readonly JsonSerializerOptions gbStudioJsonOptions = new() {WriteIndented = true, IncludeFields = true};

    private static Bitmap CreateFilledGbsBitmap(Color fill)
    {
        var image = new Bitmap(128, 112);
        image.Fill(fill);
        return image;
    }

    private static void SavePng(Bitmap image, String fileName)
    {
        using var output = File.Create(fileName);
        Png.Write(image, output);
    }
}