using CommandLine.Commands.Settings;
using System.Threading;
using PixelWorld;
using PixelWorld.BinarySource;
using PixelWorld.Display;
using PixelWorld.Machines;
using PixelWorld.Imaging;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Collections.Generic;

namespace CommandLine.Commands;

[Description("Create screenshots from memory dump or emulator snapshot")]
public class ScreenshotCommand : Command<ScreenshotSettings>
{
    public override Int32 Execute(CommandContext context, ScreenshotSettings settings, CancellationToken cancellationToken)
    {
        var fileNames = Utils.MatchGlobWithFiles(settings.Glob);
        foreach (var fileName in fileNames)
        {
            Out.Write($"Opening file {fileName}");
            using var file = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            SnapshotProcessor.ProcessStream(fileName, file, (a, b) => WriteScreenToDisk(a, b, settings), true);
        }

        return 0;
    }

    private const Int32 FlashDelay = 64;

    private static Boolean WriteScreenToDisk(String fileName, ArraySegment<Byte> memory, ScreenshotSettings settings)
    {
        var address = settings.Address ?? (memory.Count == Spectrum.Ram48K ? 0 : Spectrum.ScreenStart);

        // address is free-form from the command line and indexes the memory the decoder handed over,
        // which need not be a full address space: a failed decode leaves the segment empty and a 48K
        // dump is only 49152 bytes, so the screen window need not exist. Every format below reads
        // exactly these 6912 bytes, so check it once here rather than letting the first one throw.
        if (memory.Array is null || address < 0 || address + Spectrum.ScreenSize > memory.Count)
        {
            Out.Write($"  Skipping {fileName} at address {address} as it is outside the {memory.Count} byte memory");
            return false;
        }

        if (settings.Png)
            WritePng(fileName, SpectrumDisplay.GetBitmap(memory.ToArray(), address, settings.Flashed), settings, address);

        if (settings.Gif)
            WriteGif(fileName, memory, settings, address);

        // Write SCR if specified OR if nothing specified (so it is the default)
        if (settings.Scr || settings is {Png: false, Gif: false})
            WriteScr(fileName, memory, settings, address);

        return true;
    }

    private static void WriteScr(String sourceName, ArraySegment<Byte> memory, ScreenshotSettings settings, Int32 address)
    {
        var targetName = Utils.MakeFileName(sourceName, "scr", settings.OutputFolder);
        Out.Write($"  Screenshotting {sourceName} @ {address} to {targetName}");
        // memory.Offset is added in because address indexes the segment, and the segment need not start
        // at the beginning of its array.
        var screenBuffer = memory.Array.AsSpan(memory.Offset + address, Spectrum.ScreenSize).ToArray();
        File.WriteAllBytes(targetName, screenBuffer);
    }

    private static void WriteGif(String sourceName, ArraySegment<Byte> memory, ScreenshotSettings settings, Int32 address)
    {
        var targetName = Utils.MakeFileName(sourceName, "gif", settings.OutputFolder);
        Out.Write($"  Screenshotting {sourceName} @ {address} to {targetName}");
        var bytes = memory.ToArray();
        List<Bitmap> frames = [SpectrumDisplay.GetBitmap(bytes, address), SpectrumDisplay.GetBitmap(bytes, address, true)];
        using var output = File.Create(targetName);
        Gif.Write(frames, FlashDelay, output);
    }

    private static void WritePng(String sourceName, Bitmap image, ScreenshotSettings settings, Int32 address)
    {
        var targetName = Utils.MakeFileName(sourceName, "png", settings.OutputFolder);
        Out.Write($"  Screenshotting {sourceName} @ {address} to {targetName}");
        using var output = File.Create(targetName);
        Png.Write(image, output);
    }
}