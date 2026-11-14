using System;

namespace PixelWorld.DumpScanners;

/// <summary>The finders that can suggest where a font is in a memory dump.</summary>
public enum FinderMethod
{
    /// <summary>The CHARS system variable the ROM print routine (RST 16) uses.</summary>
    Chars,
    /// <summary>Glyphs that fonts rarely change from the ROM's.</summary>
    RomGlyphs,
    /// <summary>Character tiles on the screen.</summary>
    ScreenTiles,
    /// <summary>Relative pixel densities of glyphs.</summary>
    Heuristics,
    /// <summary>Font addresses in Z80 code.</summary>
    CodeRefs,
    /// <summary>How glyphs relate to each other.</summary>
    Structure
}

public static class FinderMethodExtensions
{
    /// <summary>
    /// The original finders only find complete fonts starting at a blank space, and what they find is
    /// accepted by the original rules.
    /// </summary>
    public static Boolean IsOriginal(this FinderMethod method) =>
        method is not (FinderMethod.CodeRefs or FinderMethod.Structure);

    public static String DisplayName(this FinderMethod method) => method switch
    {
        FinderMethod.Chars => "RST 16/CHARS",
        FinderMethod.RomGlyphs => "ROM Glyphs",
        FinderMethod.ScreenTiles => "SCREEN$ Tiles",
        FinderMethod.Heuristics => "Heuristics",
        FinderMethod.CodeRefs => "Code refs",
        FinderMethod.Structure => "Structure",
        _ => method.ToString()
    };
}
