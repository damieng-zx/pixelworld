using System;
using PixelWorld.Fonts;
using PixelWorld.Imaging;
using System.Collections.Generic;
using System.IO;

namespace PixelWorld.Formatters;

public static class ImageFontFormatter
{
    private const Int32 CharWidth = 8;
    private const Int32 CharHeight = 8;

    public static void Read(Font font, Stream source, IReadOnlyDictionary<Int32, Char> charset)
    {
        var image = Png.Read(source);

        if (image.Width % CharWidth != 0)
            throw new InvalidDataException($"Image width must be multiple of {CharWidth}");
        if (image.Height % CharHeight != 0)
            throw new InvalidDataException($"Image height must be multiple of {CharHeight}");

        var offColor = image[0, 0];

        var c = 0;
        for (var charY = 0; charY < image.Height; charY += CharHeight)
        {
            for (var charX = 0; charX < image.Width; charX += CharWidth)
            {
                var data = new Boolean[CharWidth, CharHeight];
                for (var y = 0; y < CharHeight; y++)
                for (var x = 0; x < CharWidth; x++)
                    data[x, y] = image[charX + x, charY + y] != offColor;
                var glyph = new Glyph(CharWidth, CharHeight, data);
                if (charset.TryGetValue(c++, out var mappedChar))
                    font.Glyphs.Add(mappedChar, glyph);
            }
        }
    }

    public static void Write(Font font, Stream output, Boolean transparent)
    {
        Png.Write(font.CreateImage(3, transparent), output);
    }

    public static void WriteWebp(Font font, Stream output, Boolean transparent)
    {
        Webp.Write(font.CreateImage(3, transparent), output);
    }
}