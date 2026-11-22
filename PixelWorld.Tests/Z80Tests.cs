using PixelWorld.BinarySource;

namespace PixelWorld.Tests;

public class Z80Tests
{
    private const int HeaderEnd = 87; // 30 byte header, 2 byte length and a 55 byte version 3 extension
    private const int SpectrumPlus3 = 7;
    private const int Spectrum128K = 4;

    private static readonly int[] AllBanks = [0, 1, 2, 3, 4, 5, 6, 7];

    /// <summary>
    /// A version 3 snapshot with uncompressed pages. A 128K machine's RAM bank n is page n + 3 in the file.
    /// </summary>
    private static byte[] Snapshot(int hardware, byte port7ffd, byte port1ffd, int[] banks)
    {
        var file = new List<byte>(new byte[HeaderEnd]);
        file[30] = 55; // length of the extension; also what marks the file as version 3
        file[34] = (byte)hardware;
        file[35] = port7ffd;
        file[86] = port1ffd;

        foreach (var bank in banks)
        {
            file.AddRange([0xFF, 0xFF, (byte)(bank + 3)]); // a length of 0xFFFF means 16KB stored as it is
            file.AddRange(SpectrumMemory.Page(bank));
        }

        return [..file];
    }

    private static ArraySegment<byte> Load(byte[] snapshot) => Z80BinarySource.Instance.GetMemory(new MemoryStream(snapshot));

    [Fact]
    public void GetMemory_128K_PutsPagesWhereTheOtherSnapshotFormatsDo()
    {
        var memory = Load(Snapshot(Spectrum128K, 3, 0, AllBanks));

        Assert.Equal((16 + 128) * 1024, memory.Count);
        SpectrumMemory.AssertBlank(memory, 0);
        SpectrumMemory.AssertPageAt(memory, 0x4000, 5);
        SpectrumMemory.AssertPageAt(memory, 0x8000, 2);
        SpectrumMemory.AssertPageAt(memory, 0xC000, 3);
        SpectrumMemory.AssertPageAt(memory, 0x10000, 0);
        SpectrumMemory.AssertPageAt(memory, 0x14000, 1);
        SpectrumMemory.AssertPageAt(memory, 0x18000, 4);
        SpectrumMemory.AssertPageAt(memory, 0x1C000, 6);
        SpectrumMemory.AssertPageAt(memory, 0x20000, 7);
    }

    [Fact]
    public void GetMemory_Plus3_InTheUsualPagingMode_IsLikeA128K()
    {
        var plus3 = Load(Snapshot(SpectrumPlus3, 3, 0b0000_0100, AllBanks));

        Assert.Equal(Load(Snapshot(Spectrum128K, 3, 0, AllBanks)).ToArray(), plus3.ToArray());
    }

    [Theory]
    [InlineData(0b001)] // banks 0, 1, 2, 3
    [InlineData(0b011)] // banks 4, 5, 6, 7
    [InlineData(0b101)] // banks 4, 5, 6, 3
    [InlineData(0b111)] // banks 4, 7, 6, 3
    public void GetMemory_Plus3_WithRamOverRom_KeepsEveryBank(int port1ffd)
    {
        var special = Load(Snapshot(SpectrumPlus3, 6, (byte)port1ffd, AllBanks));

        // Which banks the processor sees where does not change what is in the snapshot.
        Assert.Equal(Load(Snapshot(SpectrumPlus3, 6, 0, AllBanks)).ToArray(), special.ToArray());
        SpectrumMemory.AssertPageAt(special, 0x4000, 5);
        SpectrumMemory.AssertPageAt(special, 0x8000, 2);
        SpectrumMemory.AssertPageAt(special, 0xC000, 6);
        foreach (var (bank, address) in new[] { (0, 0x10000), (1, 0x14000), (3, 0x18000), (4, 0x1C000), (7, 0x20000) })
            SpectrumMemory.AssertPageAt(special, address, bank);
    }

    [Fact]
    public void GetMemory_Plus3_WithRamOverRomAndFewerPages_LeavesTheRestBlank()
    {
        var memory = Load(Snapshot(SpectrumPlus3, 0, 0b001, [5, 2, 0]));

        SpectrumMemory.AssertPageAt(memory, 0x4000, 5);
        SpectrumMemory.AssertBlank(memory, 0x10000); // bank 1
    }
}
