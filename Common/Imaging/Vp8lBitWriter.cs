using System;
using System.Collections.Generic;

namespace PixelWorld.Imaging;

/// <summary>Writes bits least significant first, as the lossless WebP format wants.</summary>
internal sealed class Vp8lBitWriter
{
    private readonly List<Byte> bytes = [];
    private UInt64 buffer;
    private Int32 count;

    /// <summary>How many bits have been written so far.</summary>
    public Int64 BitLength => bytes.Count * 8L + count;

    /// <param name="value">The bits to write; nothing above <paramref name="length"/> may be set.</param>
    public void Write(UInt32 value, Int32 length)
    {
        buffer |= (UInt64)value << count;
        count += length;
        while (count >= 8)
        {
            bytes.Add((Byte)buffer);
            buffer >>= 8;
            count -= 8;
        }
    }

    public Byte[] ToArray()
    {
        if (count > 0)
        {
            bytes.Add((Byte)buffer);
            buffer = 0;
            count = 0;
        }

        return [..bytes];
    }
}
