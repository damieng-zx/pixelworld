using System;
using System.Collections.Generic;
using PixelWorld.Machines;

namespace PixelWorld.OffsetFinders;

/// <summary>
/// Find font addresses in the Z80 code that uses them. A game that prints text has to hold the address
/// of its font somewhere, either to point the CHARS system variable at it for the ROM print routine,
/// or as the constant its own print routine adds to character code * 8. Each address found is
/// returned as the possible bases (where space would be) it implies, to be confirmed by scoring.
/// </summary>
public static class CodeReferenceFinder
{
    /// <summary>Lowest <see cref="FontScore.Total"/> for a base the code points at; lower than a blind scan needs.</summary>
    public const Int32 MinScore = 7;

    public static List<Int32> FindOffsets(Byte[] buffer)
    {
        var bases = new HashSet<Int32>();

        for (var i = 0; i + 4 <= buffer.Length; i++)
        {
            FindCharsWrite(buffer, i, bases);
            FindPrintRoutine(buffer, i, bases);
        }

        return [..bases];
    }

    /// <summary>
    /// LD (23606),rr after LD rr,nn sets CHARS, which holds the font address minus 256 (the ROM indexes
    /// it with the character code, and codes start at 32).
    /// </summary>
    private static void FindCharsWrite(Byte[] buffer, Int32 i, HashSet<Int32> bases)
    {
        const Byte lo = Spectrum.CharsSysVar & 0xFF, hi = Spectrum.CharsSysVar >> 8;

        // The opcode that loads the register the store writes from.
        Int32 loadOpcode;
        Int32 loadPrefix = 0;
        if (buffer[i] == 0x22 && buffer[i + 1] == lo && buffer[i + 2] == hi) loadOpcode = 0x21; // LD (nn),HL
        else if (i + 4 > buffer.Length || buffer[i + 2] != lo || buffer[i + 3] != hi) return;
        else if (buffer[i] == 0xED && buffer[i + 1] == 0x63) loadOpcode = 0x21; // LD (nn),HL (ED form)
        else if (buffer[i] == 0xED && buffer[i + 1] == 0x43) loadOpcode = 0x01; // LD (nn),BC
        else if (buffer[i] == 0xED && buffer[i + 1] == 0x53) loadOpcode = 0x11; // LD (nn),DE
        else if (buffer[i] is 0xDD or 0xFD && buffer[i + 1] == 0x22)
        {
            loadPrefix = buffer[i]; // LD (nn),IX / IY
            loadOpcode = 0x21;
        }
        else return;

        for (var j = i - 3; j >= Math.Max(0, i - 12); j--)
        {
            if (buffer[j] != loadOpcode) continue;
            if (loadPrefix != 0 && (j == 0 || buffer[j - 1] != loadPrefix)) continue;
            var value = buffer[j + 1] | buffer[j + 2] << 8;
            AddAddress(buffer, value + 256, bases);
            return;
        }
    }

    /// <summary>
    /// ADD HL,HL three times multiplies a character code by 8; a constant loaded nearby and added on is
    /// the font address. It may be the address of space (code had 32 subtracted), of character 0, of 0
    /// or of A, so all four readings are kept for scoring to choose between.
    /// </summary>
    private static void FindPrintRoutine(Byte[] buffer, Int32 i, HashSet<Int32> bases)
    {
        if (buffer[i] != 0x29 || buffer[i + 1] != 0x29 || buffer[i + 2] != 0x29) return;

        var from = Math.Max(0, i - 12);
        var to = Math.Min(buffer.Length - 3, i + 14);
        for (var j = from; j < to; j++)
        {
            // LD DE,nn ... ADD HL,DE  /  LD BC,nn ... ADD HL,BC  /  LD HL,nn ... ADD HL,DE (after EX DE,HL)
            var add = buffer[j] switch
            {
                0x11 => 0x19,
                0x01 => 0x09,
                0x21 when j > i => 0x19,
                _ => -1
            };

            if (add >= 0 && Follows(buffer, j + 3, add, i + 3))
                AddAllReadings(buffer, buffer[j + 1] | buffer[j + 2] << 8, bases);

            // ADD A,n then LD H,A: the font sits on a 256-byte boundary and only its high byte is added.
            if (j > i && buffer[j] == 0xC6 && j + 2 < buffer.Length && (buffer[j + 2] == 0x67 || buffer[j - 1] == 0x7C))
                AddAllReadings(buffer, buffer[j + 1] << 8, bases);
        }
    }

    /// <summary>Whether <paramref name="opcode"/> appears within a few bytes after both positions.</summary>
    private static Boolean Follows(Byte[] buffer, Int32 start, Int32 opcode, Int32 alsoAfter)
    {
        var from = Math.Max(start, alsoAfter);
        for (var k = from; k < Math.Min(buffer.Length, from + 6); k++)
            if (buffer[k] == opcode)
                return true;
        return false;
    }

    private static void AddAllReadings(Byte[] buffer, Int32 address, HashSet<Int32> bases)
    {
        AddAddress(buffer, address, bases); // code - 32
        AddAddress(buffer, address + 32 * 8, bases); // code used as-is
        AddAddress(buffer, address - 16 * 8, bases); // code - '0'
        AddAddress(buffer, address - 33 * 8, bases); // code - 'A'
    }

    /// <summary>
    /// Map a Z80 address of a font's space to every buffer offset it could be. A 48K dump starts at
    /// 16384; 64K and larger dumps start at 0, with any extra 128K banks after 65536 each of which might
    /// have been paged in at 49152 when the address was used.
    /// </summary>
    private static void AddAddress(Byte[] buffer, Int32 address, HashSet<Int32> bases)
    {
        address &= 0xFFFF;
        if (address < Spectrum.ScreenStart + Spectrum.ScreenSize) return; // ROM, screen or wrapped

        if (buffer.Length == Spectrum.Ram48K)
        {
            bases.Add(address - Spectrum.ScreenStart);
            return;
        }

        bases.Add(address);

        if (address < 0xC000) return;
        for (var bank = Spectrum.AddressSpace; bank + 0x4000 <= buffer.Length; bank += 0x4000)
            bases.Add(bank + address - 0xC000);
    }
}
