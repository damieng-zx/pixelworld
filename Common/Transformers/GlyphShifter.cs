using System;
using PixelWorld.Fonts;

namespace PixelWorld.Transformers;

public static class GlyphShifter
{
    public static Glyph Shift(Glyph source, Int32 horizontal, Int32 vertical, Boolean wrap, Int32? newWidth = null, Int32? newHeight = null)
    {
        var width = newWidth ?? source.Width;
        var height = newHeight ?? source.Height;

        var data = new Boolean[width, height];

        // Nothing can be placed in a zero-sized destination, and the wrap below would divide by zero.
        if (width == 0 || height == 0) return new Glyph(width, height, data);

        for (var y = 0; y < source.Height; y++)
        {
            // Clip against the destination, not the source, so newHeight is honoured.
            var ty = y + vertical;
            if (wrap)
                ty = (ty % height + height) % height;   // % keeps the sign, so normalise for negative shifts
            else if (ty < 0 || ty >= height)
                continue;

            for (var x = 0; x < source.Width; x++)
            {
                var tx = x + horizontal;
                if (wrap)
                    tx = (tx % width + width) % width;
                else if (tx < 0 || tx >= width)
                    continue;

                data[tx, ty] = source.Data[x, y];
            }
        }

        return new Glyph(width, height, data);
    }
}