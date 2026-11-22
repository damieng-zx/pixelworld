using System;

namespace PixelWorld.BinarySource;

public abstract class SpectrumBinarySource
{
    private static readonly Int32[] mappablePages = [0, 2, 6, 8, 12, 14];

    protected static ArraySegment<Byte> Setup128KMemory(Byte[][] ramBanks, Byte port7ffd)
    {
        Out.Write("  Loading as ZX Spectrum 128K");

        var extraSpace = 16;
        var bankAtC8000 = GetC800RamBank(port7ffd);
        if (bankAtC8000 is 10 or 04)
        {
            // This means the top bank is duplicated. Which is stupid but happens.
            extraSpace = 32;
        }

        var memory = new Byte[(extraSpace + 128) * 1024]; // Leave first 16KB ROM blank

        CopyBank(ramBanks, 10, memory, 0x4000);
        CopyBank(ramBanks, 04, memory, 0x8000);
        CopyBank(ramBanks, bankAtC8000, memory, 0xC000);

        var nextAddress = 0x10000;
        foreach (var page in mappablePages)
        {
            if (page != bankAtC8000)
            {
                CopyBank(ramBanks, page, memory, nextAddress);
                nextAddress += 0x4000;
            }
        }

        return new ArraySegment<Byte>(memory);
    }

    /// <summary>
    /// A +2A or +3 snapshot. With bit 0 of port 0x1FFD set the machine runs "special paging", where RAM banks
    /// replace the ROM and the memory at 0x4000, 0x8000 and 0xC000 in one of four arrangements. That only
    /// changes which bank the processor can see where, and the snapshot still holds every bank, so they are
    /// all kept in the usual layout: bank 5 at 0x4000 (where the screen and system variables are), bank 2 at
    /// 0x8000, the bank port 0x7FFD names at 0xC000 and the rest after them.
    /// </summary>
    protected static ArraySegment<Byte> SetupPlus3Memory(Byte[][] ramBanks, Byte port7ffd, Byte port1ffd)
    {
        Out.Write("  Loading as ZX Spectrum 128K +3");

        if ((port1ffd & 1) != 0)
            Out.Write("  RAM is paged over the ROM, so keeping all eight banks in the usual layout");

        return Setup128KMemory(ramBanks, port7ffd);
    }

    protected static void CopyBank(Byte[][] ramBanks, Int32 bank, Byte[] ram, Int32 address)
    {
        Array.Copy(ramBanks[bank], 0, ram, address, 8192);
        Array.Copy(ramBanks[bank + 1], 0, ram, address + 8192, 8192);
    }

    protected static Int32 GetC800RamBank(Int32 out7ffd)
    {
        return (out7ffd & 0x07) * 2;
    }
}