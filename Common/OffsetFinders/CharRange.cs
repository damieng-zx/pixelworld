using System;

namespace PixelWorld.OffsetFinders;

/// <summary>The character codes of a font that are actually present in memory.</summary>
public readonly record struct CharRange(Int32 First, Int32 Last)
{
    public static readonly CharRange Full = new(32, 127);
    public Boolean IsFull => First == 32 && Last == 127;
    public override String ToString() => $"'{(Char)First}'..'{(Char)Last}'";
}
