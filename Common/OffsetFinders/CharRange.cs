using System;

namespace PixelWorld.OffsetFinders;

/// <summary>The character codes of a font that are actually present in memory.</summary>
public readonly record struct CharRange(Int32 First, Int32 Last)
{
    public const Int32 Space = 32;
    public const Int32 Copyright = 127;

    public static readonly CharRange Full = new(Space, Copyright);
    public Boolean IsFull => this == Full;
    public override String ToString() => $"'{(Char)First}'..'{(Char)Last}'";
}
