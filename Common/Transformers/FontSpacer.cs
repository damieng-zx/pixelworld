using System;
using PixelWorld.Fonts;
using System.Linq;

namespace PixelWorld.Transformers;

public static class FontSpacer
{
    public static Font MakeProportional(Font source, Int32 leftPad, Int32 rightPad, Int32 maxWidth)
    {
        var target = source.Copy();
        var allKeys = target.Glyphs.Keys.ToList();

        foreach (var key in allKeys.Where(key => key != ' '))
            target.Glyphs[key] = GlyphSpacer.Proportional(target.Glyphs[key], leftPad, rightPad, maxWidth);

        var spaceWidth = maxWidth - leftPad - rightPad;
        if (target.Glyphs.TryGetValue('{', out var braceGlyph))
            spaceWidth = braceGlyph.Width - leftPad - rightPad;

        // Padding wider than the glyph would leave a negative width, and new Boolean[negative, ...]
        // throws. Clamp so an over-padded space becomes zero width instead.
        if (spaceWidth < 0) spaceWidth = 0;

        target.Glyphs[' '] = new Glyph(spaceWidth, source.Height, new Boolean[spaceWidth, source.Height]);

        return target;
    }
}