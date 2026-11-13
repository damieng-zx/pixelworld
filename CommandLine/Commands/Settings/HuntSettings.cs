using System;
using System.ComponentModel;
using Spectre.Console.Cli;

namespace CommandLine.Commands.Settings;

public class HuntSettings : RequiredSettings
{
    [CommandOption("--known <FOLDER>")]
    [Description("Folder of .ch8 fonts already collected; identical finds are skipped")]
    public String? KnownFolder { get; set; }
}
