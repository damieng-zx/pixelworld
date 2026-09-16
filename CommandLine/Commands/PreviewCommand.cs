using System;
using PixelWorld;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.IO;
using CommandLine.Commands.Settings;
using PixelWorld.Formatters;
using PixelWorld.Machines;

namespace CommandLine.Commands;

[Description("Create preview images from ZX font")]
public class PreviewCommand : Command<PreviewSettings>
{
    public override Int32 Execute(CommandContext context, PreviewSettings settings)
    {
        foreach (var fileName in Utils.MatchGlobWithFiles(settings.Glob))
        {
            Out.Write($"Generating preview files for {fileName}");
            var sourceFont = ByteFontFormatter.Load(fileName, Spectrum.UK);

            if (settings.Webp)
            {
                var targetName = Utils.MakeFileName(fileName, "webp", settings.OutputFolder);
                Out.Write($"  Previewing {fileName} to {targetName}");
                // File.Create truncates; File.OpenWrite would leave any trailing bytes from a longer
                // previous file after the new image, corrupting the output on a re-run.
                using var output = File.Create(targetName);
                ImageFontFormatter.Write(sourceFont, output, ImageFontFormatter.WebpEncoder, settings.Transparent);
            }

            if (settings.Png || !settings.Webp)
            {
                var targetName = Utils.MakeFileName(fileName, "png", settings.OutputFolder);
                Out.Write($"  Previewing {fileName} to {targetName}");
                // File.Create truncates; File.OpenWrite would leave any trailing bytes from a longer
                // previous file after the new image, corrupting the output on a re-run.
                using var output = File.Create(targetName);
                ImageFontFormatter.Write(sourceFont, output, ImageFontFormatter.PngEncoder, settings.Transparent);
            }
        }

        return 0;
    }
}