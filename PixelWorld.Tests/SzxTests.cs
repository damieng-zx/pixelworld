using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PixelWorld.BinarySource;
using PixelWorld.BinarySource.Decoders;

namespace PixelWorld.Tests;

public class SzxTests
{
    private const int PageSize = 0x4000;

    private const int Spectrum16K = 0;
    private const int Spectrum48K = 1;
    private const int Spectrum128K = 2;
    private const int SpectrumPlus3 = 5;
    private const int Timex2068 = 9;

    /// <summary>The contents of RAM page n, different for every page and every position in it.</summary>
    private static byte[] Page(int number)
    {
        var page = new byte[PageSize];
        for (var i = 0; i < page.Length; i++)
            page[i] = (byte)(i * 7 + number * 31 + 1);
        return page;
    }

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
            Chunk(file, "RAMP", RamPage(page, Page(page), compressed));
        return file.ToArray();
    }

    private static ArraySegment<byte> Load(byte[] snapshot) => SzxBinarySource.Instance.GetMemory(new MemoryStream(snapshot));

    private static void AssertPageAt(ArraySegment<byte> memory, int address, int page)
    {
        Assert.Equal(Page(page), memory.Array!.AsSpan(memory.Offset + address, PageSize).ToArray());
    }

    /// <summary>What an undecodable file gives: a default segment, which cannot even be enumerated.</summary>
    private static void AssertNothing(ArraySegment<byte> memory) => Assert.Equal(0, memory.Count);

    private static void AssertBlank(ArraySegment<byte> memory, int address)
    {
        Assert.All(memory.Array!.AsSpan(memory.Offset + address, PageSize).ToArray(), b => Assert.Equal(0, b));
    }

    private static readonly int[] AllPages = [0, 1, 2, 3, 4, 5, 6, 7];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetMemory_128K_PutsPagesWhereTheOtherSnapshotFormatsDo(bool compressed)
    {
        // Page 3 is switched in at 0xC000, so the other five follow the 48K address space in page order.
        var memory = Load(Snapshot(Spectrum128K, 3, 0, AllPages, compressed));

        Assert.Equal((16 + 128) * 1024, memory.Count);
        AssertBlank(memory, 0);
        AssertPageAt(memory, 0x4000, 5);
        AssertPageAt(memory, 0x8000, 2);
        AssertPageAt(memory, 0xC000, 3);
        AssertPageAt(memory, 0x10000, 0);
        AssertPageAt(memory, 0x14000, 1);
        AssertPageAt(memory, 0x18000, 4);
        AssertPageAt(memory, 0x1C000, 6);
        AssertPageAt(memory, 0x20000, 7);
    }

    [Fact]
    public void GetMemory_128K_IgnoresTheBitsOfPort7ffdThatAreNotTheBank()
    {
        // Bit 4 selects the ROM and bit 3 the shadow screen; only the low three bits pick the bank.
        var memory = Load(Snapshot(Spectrum128K, 0x10 | 0x08 | 6, 0, AllPages));

        AssertPageAt(memory, 0xC000, 6);
    }

    [Fact]
    public void GetMemory_128K_WithTheScreenPageAtTheTop_LeavesRoomForTheDuplicate()
    {
        // The other loaders leave extra room when page 5 or 2 is also at 0xC000.
        var memory = Load(Snapshot(Spectrum128K, 5, 0, AllPages));

        Assert.Equal((32 + 128) * 1024, memory.Count);
        AssertPageAt(memory, 0xC000, 5);
    }

    [Fact]
    public void GetMemory_128K_MissingPagesAreBlank()
    {
        var memory = Load(Snapshot(Spectrum128K, 0, 0, [5, 2, 0]));

        AssertPageAt(memory, 0x4000, 5);
        AssertBlank(memory, 0x10000); // page 1
    }

    [Fact]
    public void GetMemory_48K_PutsThePagesInTheThreeRamBlocks()
    {
        var memory = Load(Snapshot(Spectrum48K, 0, 0, [5, 2, 0]));

        Assert.Equal(64 * 1024, memory.Count);
        AssertBlank(memory, 0);
        AssertPageAt(memory, 0x4000, 5);
        AssertPageAt(memory, 0x8000, 2);
        AssertPageAt(memory, 0xC000, 0);
    }

    [Fact]
    public void GetMemory_16K_HasOnlyTheFirstBlock()
    {
        var memory = Load(Snapshot(Spectrum16K, 0, 0, [5]));

        Assert.Equal(64 * 1024, memory.Count);
        AssertPageAt(memory, 0x4000, 5);
        AssertBlank(memory, 0x8000);
        AssertBlank(memory, 0xC000);
    }

    [Fact]
    public void GetMemory_Plus3_InTheUsualPagingMode_IsLikeA128K()
    {
        var memory = Load(Snapshot(SpectrumPlus3, 4, 0b0000_0100, AllPages));

        AssertPageAt(memory, 0xC000, 4);
        AssertPageAt(memory, 0x10000, 0);
    }

    [Fact]
    public void GetMemory_Plus3_InSpecialPagingMode_IsNotSupported()
    {
        // Bit 0 of port 0x1FFD maps RAM over the ROM area, which this does not model.
        AssertNothing(Load(Snapshot(SpectrumPlus3, 0, 0b0000_0001, AllPages)));
    }

    [Fact]
    public void GetMemory_UnsupportedMachine_GivesNothing()
    {
        AssertNothing(Load(Snapshot(Timex2068, 0, 0, [5, 2, 0])));
    }

    [Fact]
    public void GetMemory_SkipsChunksItDoesNotKnow()
    {
        var file = Header(Spectrum48K);
        Chunk(file, "ZZZZ", new byte[1000]);
        Chunk(file, "RAMP", RamPage(5, Page(5), true));
        Chunk(file, "AY\0\0", new byte[18]);

        AssertPageAt(Load(file.ToArray()), 0x4000, 5);
    }

    [Fact]
    public void GetMemory_ReadsSnapshotsFromStreamsThatCannotSeek()
    {
        // Entries of a .zip arrive as forward-only streams.
        var forwardOnly = new ForwardOnlyStream(Snapshot(Spectrum128K, 0, 0, AllPages));

        AssertPageAt(SzxBinarySource.Instance.GetMemory(forwardOnly), 0x4000, 5);
    }

    [Fact]
    public void GetMemory_NotASnapshot_GivesNothing()
    {
        AssertNothing(Load("This is not a ZX-State file at all"u8.ToArray()));
        AssertNothing(Load([]));
    }

    [Fact]
    public void GetMemory_Truncated_GivesNothing()
    {
        var snapshot = Snapshot(Spectrum128K, 0, 0, AllPages);

        AssertNothing(Load(snapshot[..(snapshot.Length - 100)])); // inside the last page
        AssertNothing(Load(snapshot[..12])); // inside the first chunk header
    }

    [Fact]
    public void GetMemory_PageOfTheWrongSize_GivesNothing()
    {
        var file = Header(Spectrum48K);
        Chunk(file, "RAMP", RamPage(5, new byte[1000], false));

        AssertNothing(Load(file.ToArray()));
    }

    [Fact]
    public void GetMemory_CompressedPageThatInflatesTooFar_GivesNothing()
    {
        var file = Header(Spectrum48K);
        Chunk(file, "RAMP", RamPage(5, new byte[PageSize * 8], true));

        AssertNothing(Load(file.ToArray()));
    }

    [Fact]
    public void GetMemory_CompressedPageThatInflatesTooLittle_GivesNothing()
    {
        var file = Header(Spectrum48K);
        Chunk(file, "RAMP", RamPage(5, new byte[PageSize / 2], true));

        AssertNothing(Load(file.ToArray()));
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
