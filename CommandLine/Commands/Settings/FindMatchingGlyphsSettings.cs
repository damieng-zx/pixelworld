using System;
using Spectre.Console.Cli;

namespace CommandLine.Commands.Settings;

public class FindMatchingGlyphsSettings : CommandSettings
{
    // All three are required: without a source font or a glyph list there is nothing to match on, and
    // an empty glyph list would previously match every file in the glob.
    [CommandArgument(0, "<SourceFontFile>")]
    public String SourceFontFile { get; set; } = "";

    [CommandArgument(1, "<FileGlob>")]
    public String Glob { get; set; } = "";

    [CommandArgument(2, "<MatchGlyphs>")]
    public String MatchGlyphs { get; set; } = "";
}