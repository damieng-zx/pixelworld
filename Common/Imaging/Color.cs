using System;
using System.Globalization;

namespace PixelWorld.Imaging;

/// <summary>An 8-bit-per-channel colour.</summary>
public readonly record struct Color(Byte R, Byte G, Byte B, Byte A = 255)
{
    public static readonly Color Transparent = new(0, 0, 0, 0);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Gray = new(0x80, 0x80, 0x80);
    public static readonly Color Red = new(0xFF, 0, 0);
    public static readonly Color Green = new(0, 0x80, 0);
    public static readonly Color Blue = new(0, 0, 0xFF);
    public static readonly Color Yellow = new(0xFF, 0xFF, 0);
    public static readonly Color Cyan = new(0, 0xFF, 0xFF);
    public static readonly Color Magenta = new(0xFF, 0, 0xFF);
    public static readonly Color Olive = new(0x80, 0x80, 0);
    public static readonly Color LimeGreen = new(0x32, 0xCD, 0x32);
    public static readonly Color DarkRed = new(0x8B, 0, 0);
    public static readonly Color DarkBlue = new(0, 0, 0x8B);
    public static readonly Color DarkMagenta = new(0x8B, 0, 0x8B);
    public static readonly Color DarkCyan = new(0, 0x8B, 0x8B);

    /// <summary>Parse <c>#rrggbb</c> or <c>#rrggbbaa</c>; the <c>#</c> is optional.</summary>
    public static Color ParseHex(String hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length is not (6 or 8) || !Int64.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
            throw new FormatException($"'{hex}' is not a colour in #rrggbb or #rrggbbaa form");

        Byte Part(Int32 i) => Byte.Parse(digits.AsSpan(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return new Color(Part(0), Part(1), Part(2), digits.Length == 8 ? Part(3) : (Byte)255);
    }
}
