using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PixelWorld.BinarySource;
using PixelWorld.BinarySource.Decoders;

namespace PixelWorld.Tests;

public class SzxTests
{
    private const int Spectrum16K = 0;
    private const int Spectrum48K = 1;
    private const int Spectrum128K = 2;
    private const int SpectrumPlus2A = 4;
    private const int SpectrumPlus3 = 5;
    private const int SpectrumPlus3E = 6;
    private const int Timex2068 = 9;

    private static byte[] Compress(byte[] data)
    {
        var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
            zlib.Write(data);
        return output.ToArray();
    }

    private static void Chunk(MemoryStream file, string id, byte[] data)
    {
        file.Write(Encoding.ASCII.GetBytes(id));
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
        file.Write(size);
        file.Write(data);
    }

    private static byte[] RamPage(int number, byte[] contents, bool compressed)
    {
        var data = new List<byte> { compressed ? (byte)1 : (byte)0, 0, (byte)number };
        data.AddRange(compressed ? Compress(contents) : contents);
        return [..data];
    }

    private static MemoryStream Header(int machine)
    {
        var file = new MemoryStream();
        file.Write("ZXST"u8);
        file.Write([1, 5, (byte)machine, 0]);
        return file;
    }

    /// <summary>A snapshot of the given machine with the given pages; the other chunks are the kind real files carry.</summary>
    private static byte[] Snapshot(int machine, byte port7ffd, byte port1ffd, int[] pages, bool compressed = true)
    {
        var file = Header(machine);
        Chunk(file, "CRTR", new byte[16]);
        Chunk(file, "SPCR", [1, port7ffd, port1ffd, 0, 0, 0, 0, 0]);
        Chunk(file, "KEYB", new byte[5]);
        foreach (var page in pages)
            Chunk(file, "RAMP", RamPage(page, SpectrumMemory.Page(page), compressed));
        return file.ToArray();
    }

    private static ArraySegment<byte> Load(byte[] snapshot) => SzxBinarySource.Instance.GetMemory(new MemoryStream(snapshot));

    private static readonly int[] AllPages = [0, 1, 2, 3, 4, 5, 6, 7];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetMemory_128K_PutsPagesWhereTheOtherSnapshotFormatsDo(bool compressed)
    {
        // Page 3 is switched in at 0xC000, so the other five follow the 48K address space in page order.
        var memory = Load(Snapshot(Spectrum128K, 3, 0, AllPages, compressed));

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
    public void GetMemory_128K_IgnoresTheBitsOfPort7ffdThatAreNotTheBank()
    {
        // Bit 4 selects the ROM and bit 3 the shadow screen; only the low three bits pick the bank.
        var memory = Load(Snapshot(Spectrum128K, 0x10 | 0x08 | 6, 0, AllPages));

        SpectrumMemory.AssertPageAt(memory, 0xC000, 6);
    }

    [Fact]
    public void GetMemory_128K_WithTheScreenPageAtTheTop_LeavesRoomForTheDuplicate()
    {
        // The other loaders leave extra room when page 5 or 2 is also at 0xC000.
        var memory = Load(Snapshot(Spectrum128K, 5, 0, AllPages));

        Assert.Equal((32 + 128) * 1024, memory.Count);
        SpectrumMemory.AssertPageAt(memory, 0xC000, 5);
    }

    [Fact]
    public void GetMemory_128K_MissingPagesAreBlank()
    {
        var memory = Load(Snapshot(Spectrum128K, 0, 0, [5, 2, 0]));

        SpectrumMemory.AssertPageAt(memory, 0x4000, 5);
        SpectrumMemory.AssertBlank(memory, 0x10000); // page 1
    }

    [Fact]
    public void GetMemory_48K_PutsThePagesInTheThreeRamBlocks()
    {
        var memory = Load(Snapshot(Spectrum48K, 0, 0, [5, 2, 0]));

        Assert.Equal(64 * 1024, memory.Count);
        SpectrumMemory.AssertBlank(memory, 0);
        SpectrumMemory.AssertPageAt(memory, 0x4000, 5);
        SpectrumMemory.AssertPageAt(memory, 0x8000, 2);
        SpectrumMemory.AssertPageAt(memory, 0xC000, 0);
    }

    [Fact]
    public void GetMemory_16K_HasOnlyTheFirstBlock()
    {
        var memory = Load(Snapshot(Spectrum16K, 0, 0, [5]));

        Assert.Equal(64 * 1024, memory.Count);
        SpectrumMemory.AssertPageAt(memory, 0x4000, 5);
        SpectrumMemory.AssertBlank(memory, 0x8000);
        SpectrumMemory.AssertBlank(memory, 0xC000);
    }

    [Fact]
    public void GetMemory_Plus3_InTheUsualPagingMode_IsLikeA128K()
    {
        var memory = Load(Snapshot(SpectrumPlus3, 4, 0b0000_0100, AllPages));

        SpectrumMemory.AssertPageAt(memory, 0xC000, 4);
        SpectrumMemory.AssertPageAt(memory, 0x10000, 0);
    }

    [Theory]
    [InlineData(0b001)] // banks 0, 1, 2, 3
    [InlineData(0b011)] // banks 4, 5, 6, 7
    [InlineData(0b101)] // banks 4, 5, 6, 3
    [InlineData(0b111)] // banks 4, 7, 6, 3
    public void GetMemory_Plus3_WithRamOverRom_KeepsEveryBank(int port1ffd)
    {
        var special = Load(Snapshot(SpectrumPlus3, 4, (byte)port1ffd, AllPages));

        // Which banks the processor sees where does not change what is in the snapshot.
        Assert.Equal(Load(Snapshot(SpectrumPlus3, 4, 0, AllPages)).ToArray(), special.ToArray());
        SpectrumMemory.AssertPageAt(special, 0x4000, 5);
        SpectrumMemory.AssertPageAt(special, 0x8000, 2);
        SpectrumMemory.AssertPageAt(special, 0xC000, 4);
        foreach (var (page, address) in new[] { (0, 0x10000), (1, 0x14000), (3, 0x18000), (6, 0x1C000), (7, 0x20000) })
            SpectrumMemory.AssertPageAt(special, address, page);
    }

    [Theory]
    [InlineData(SpectrumPlus2A)]
    [InlineData(SpectrumPlus3E)]
    public void GetMemory_Plus2AAndPlus3E_WithRamOverRom_AreLoadedToo(int machine)
    {
        var memory = Load(Snapshot(machine, 0, 0b101, AllPages));

        SpectrumMemory.AssertPageAt(memory, 0x4000, 5);
        SpectrumMemory.AssertPageAt(memory, 0x10000, 1);
    }

    [Fact]
    public void GetMemory_UnsupportedMachine_GivesNothing()
    {
        SpectrumMemory.AssertNothing(Load(Snapshot(Timex2068, 0, 0, [5, 2, 0])));
    }

    [Fact]
    public void GetMemory_SkipsChunksItDoesNotKnow()
    {
        var file = Header(Spectrum48K);
        Chunk(file, "ZZZZ", new byte[1000]);
        Chunk(file, "RAMP", RamPage(5, SpectrumMemory.Page(5), true));
        Chunk(file, "AY\0\0", new byte[18]);

        SpectrumMemory.AssertPageAt(Load(file.ToArray()), 0x4000, 5);
    }

    [Fact]
    public void GetMemory_ReadsSnapshotsFromStreamsThatCannotSeek()
    {
        // Entries of a .zip arrive as forward-only streams.
        var forwardOnly = new ForwardOnlyStream(Snapshot(Spectrum128K, 0, 0, AllPages));

        SpectrumMemory.AssertPageAt(SzxBinarySource.Instance.GetMemory(forwardOnly), 0x4000, 5);
    }

    [Fact]
    public void GetMemory_NotASnapshot_GivesNothing()
    {
        SpectrumMemory.AssertNothing(Load("This is not a ZX-State file at all"u8.ToArray()));
        SpectrumMemory.AssertNothing(Load([]));
    }

    [Fact]
    public void GetMemory_Truncated_GivesNothing()
    {
        var snapshot = Snapshot(Spectrum128K, 0, 0, AllPages);

        SpectrumMemory.AssertNothing(Load(snapshot[..(snapshot.Length - 100)])); // inside the last page
        SpectrumMemory.AssertNothing(Load(snapshot[..12])); // inside the first chunk header
    }

    [Fact]
    public void GetMemory_PageOfTheWrongSize_GivesNothing()
    {
        var file = Header(Spectrum48K);
        Chunk(file, "RAMP", RamPage(5, new byte[1000], false));

        SpectrumMemory.AssertNothing(Load(file.ToArray()));
    }

    [Fact]
    public void GetMemory_CompressedPageThatInflatesTooFar_GivesNothing()
    {
        var file = Header(Spectrum48K);
        Chunk(file, "RAMP", RamPage(5, new byte[SpectrumMemory.PageSize * 8], true));

        SpectrumMemory.AssertNothing(Load(file.ToArray()));
    }

    [Fact]
    public void GetMemory_CompressedPageThatInflatesTooLittle_GivesNothing()
    {
        var file = Header(Spectrum48K);
        Chunk(file, "RAMP", RamPage(5, new byte[SpectrumMemory.PageSize / 2], true));

        SpectrumMemory.AssertNothing(Load(file.ToArray()));
    }

    [Theory]
    [InlineData(".szx")]
    [InlineData(".SZX")]
    public void Factory_HandlesSzxFiles(string extension)
    {
        Assert.Same(SzxBinarySource.Instance, BinarySourceFactory.Create(extension));
    }

    [Fact]
    public void Load_ReportsTheMachineAndPaging()
    {
        var snapshot = SzxFile.Load(Snapshot(SpectrumPlus3, 0x17, 0x04, [0, 1]));

        Assert.Equal(SpectrumPlus3, snapshot.MachineId);
        Assert.Equal(0x17, snapshot.Port7ffd);
        Assert.Equal(0x04, snapshot.Port1ffd);
        Assert.Equal([0, 1], snapshot.Pages.Keys.Order());
    }

    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
