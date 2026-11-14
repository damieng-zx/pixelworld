using System;

namespace PixelWorld.Imaging;

/// <summary>A rectangle of <see cref="Color"/> pixels, with (0, 0) at the top left.</summary>
public sealed class Bitmap
{
    private readonly Color[] pixels;

    public Bitmap(Int32 width, Int32 height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        pixels = new Color[checked(width * height)];
    }

    public Int32 Width { get; }
    public Int32 Height { get; }

    public Color this[Int32 x, Int32 y]
    {
        get => pixels[Index(x, y)];
        set => pixels[Index(x, y)] = value;
    }

    public void Fill(Color color) => Array.Fill(pixels, color);

    private Int32 Index(Int32 x, Int32 y)
    {
        if ((UInt32)x >= (UInt32)Width) throw new ArgumentOutOfRangeException(nameof(x), x, null);
        if ((UInt32)y >= (UInt32)Height) throw new ArgumentOutOfRangeException(nameof(y), y, null);
        return y * Width + x;
    }
}
