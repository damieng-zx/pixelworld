using PixelWorld.BinarySource.Decoders;
using System;
using System.Collections.Generic;
using System.IO;

namespace PixelWorld.BinarySource;

/// <summary>Memory from ZX-State (.szx) snapshots of ZX Spectrums, laid out as the .z80 and .sna sources do.</summary>
public class SzxBinarySource : SpectrumBinarySource, IBinarySource
{
    public static IBinarySource Instance { get; } = new SzxBinarySource();

    // The machine numbers the format gives each model, grouped by how their RAM is arranged.
    private const Int32 Spectrum16K = 0;
    private const Int32 Spectrum48K = 1;
    private const Int32 Spectrum128K = 2;
    private const Int32 SpectrumPlus2 = 3;
    private const Int32 SpectrumPlus2A = 4;
    private const Int32 SpectrumPlus3 = 5;
    private const Int32 SpectrumPlus3E = 6;
    private const Int32 Pentagon128 = 7;
    private const Int32 Timex2048 = 8;
    private const Int32 Spectrum48KNtsc = 15;
    private const Int32 Spectrum128Ke = 16;

    private const Int32 PagesInA128K = 8;
    private const Int32 BanksPerPage = 2;
    private const Int32 BankSize = SzxFile.PageSize / BanksPerPage;

    public ArraySegment<Byte> GetMemory(Stream source)
    {
        try
        {
            var snapshot = SzxFile.Load(source);

            return snapshot.MachineId switch
            {
                Spectrum16K or Spectrum48K or Timex2048 or Spectrum48KNtsc => Setup48KMemory(snapshot),
                Spectrum128K or SpectrumPlus2 or Pentagon128 or Spectrum128Ke => Setup128KMemory(snapshot),
                SpectrumPlus2A or SpectrumPlus3 or SpectrumPlus3E => SetupPlus3Memory(snapshot),
                _ => throw new NotSupportedException($"Unsupported SZX machine {snapshot.MachineId}")
            };
        }
        catch (Exception e)
        {
            Out.Write($"  Unable to process {e.Message}");
            return new ArraySegment<Byte>();
        }
    }

    private static ArraySegment<Byte> Setup48KMemory(SzxSnapshot snapshot)
    {
        Out.Write("  Loading as ZX Spectrum 48K");
        var memory = new Byte[(16 + 48) * 1024]; // Leave first 16KB ROM blank

        // A 48K machine's three 16KB blocks are RAM pages 5, 2 and 0; a 16K one has only the first.
        CopyPage(snapshot, 5, memory, 0x4000);
        CopyPage(snapshot, 2, memory, 0x8000);
        CopyPage(snapshot, 0, memory, 0xC000);
        return new ArraySegment<Byte>(memory);
    }

    private static ArraySegment<Byte> Setup128KMemory(SzxSnapshot snapshot)
    {
        return Setup128KMemory(ToBanks(snapshot), snapshot.Port7ffd);
    }

    private static ArraySegment<Byte> SetupPlus3Memory(SzxSnapshot snapshot)
    {
        return SetupPlus3Memory(ToBanks(snapshot), snapshot.Port7ffd, snapshot.Port1ffd);
    }

    private static void CopyPage(SzxSnapshot snapshot, Int32 page, Byte[] memory, Int32 address)
    {
        if (snapshot.Pages.TryGetValue(page, out var data))
            Array.Copy(data, 0, memory, address, SzxFile.PageSize);
    }

    /// <summary>The 8KB banks the shared 128K memory layout works in: page n is banks 2n and 2n+1.</summary>
    private static Byte[][] ToBanks(SzxSnapshot snapshot)
    {
        var banks = new Byte[PagesInA128K * BanksPerPage][];
        for (var page = 0; page < PagesInA128K; page++)
        {
            var data = snapshot.Pages.GetValueOrDefault(page) ?? new Byte[SzxFile.PageSize];
            for (var half = 0; half < BanksPerPage; half++)
                banks[page * BanksPerPage + half] = data.AsSpan(half * BankSize, BankSize).ToArray();
        }

        return banks;
    }
}
