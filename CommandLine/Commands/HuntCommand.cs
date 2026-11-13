using System;
using PixelWorld;
using PixelWorld.Tools;
using Spectre.Console.Cli;
using System.ComponentModel;
using CommandLine.Commands.Settings;

namespace CommandLine.Commands;

[Description("Hunt memory dumps for possible ZX fonts")]
public class HuntCommand : Command<HuntSettings>
{
    public override Int32 Execute(CommandContext context, HuntSettings settings)
    {
        var files = Utils.MatchGlobWithFiles(settings.Glob);
        FontHunter.Hunt(files, settings.OutputFolder, settings.KnownFolder);
        return 0;
    }
}