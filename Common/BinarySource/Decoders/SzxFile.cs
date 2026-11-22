using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PixelWorld.BinarySource.Decoders;

/// <summary>What is needed from a ZX-State (.szx) snapshot to rebuild the memory of a ZX Spectrum.</summary>
public sealed class SzxSnapshot
{
    public Byte MachineId { get; init; }

    /// <summary>The last value written to port 0x7FFD, which says which RAM page is at 0xC000.</summary>
    public Byte Port7ffd { get; init; }

    /// <summary>The last value written to port 0x1FFD (+2A and +3 only).</summary>
    public Byte Port1ffd { get; init; }

    /// <summary>The 16KB RAM pages in the file, by page number.</summary>
    public IReadOnlyDictionary<Int32, Byte[]> Pages { get; init; } = new Dictionary<Int32, Byte[]>();
}

/// <summary>
/// Reads ZX-State snapshots, the format used by Fuse, Spectaculator and others. A file is an 8 byte header
/// (<c>ZXST</c>, the version, the machine and some flags) followed by chunks, each a four character name, a
/// little-endian length and its data. Only the chunks that hold memory and paging are read.
/// </summary>
public static class SzxFile
{
    public const Int32 PageSize = 0x4000;

    private const Int32 HeaderSize = 8;
    private const Int32 ChunkHeaderSize = 8;
    private const Int32 RamPageFlagsSize = 3;
    private const UInt16 CompressedFlag = 1;

    public static SzxSnapshot Load(Stream source)
    {
        return Load(source.ReadAllBytes());
    }

    public static SzxSnapshot Load(Byte[] data)
    {
        if (data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, 4) != "ZXST")
            throw new InvalidDataException("Not a ZX-State snapshot");

        var machineId = data[6];
        Byte port7ffd = 0;
        Byte port1ffd = 0;
        var pages = new Dictionary<Int32, Byte[]>();

        var at = HeaderSize;
        while (at < data.Length)
        {
            if (data.Length - at < ChunkHeaderSize)
                throw new InvalidDataException("The snapshot ends part way through a chunk header");

            var id = Encoding.ASCII.GetString(data, at, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4));
            at += ChunkHeaderSize;
            if (size > data.Length - at)
                throw new InvalidDataException($"The {id} chunk is longer than the rest of the snapshot");

            var chunk = data.AsSpan(at, (Int32)size);
            at += (Int32)size;

            switch (id)
            {
                case "SPCR" when chunk.Length >= 3:
                    port7ffd = chunk[1];
                    port1ffd = chunk[2];
                    break;
                case "RAMP":
                    var (pageNumber, page) = ReadRamPage(chunk);
                    pages[pageNumber] = page;
                    break;
            }
        }

        return new SzxSnapshot { MachineId = machineId, Port7ffd = port7ffd, Port1ffd = port1ffd, Pages = pages };
    }

    /// <summary>A RAMP chunk is flags (bit 0 set when zlib compressed), the page number, then the page.</summary>
    private static (Int32 PageNumber, Byte[] Page) ReadRamPage(ReadOnlySpan<Byte> chunk)
    {
        if (chunk.Length < RamPageFlagsSize)
            throw new InvalidDataException("The RAMP chunk is too short");

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
        var pageNumber = chunk[2];
        var payload = chunk[RamPageFlagsSize..];

        var page = new Byte[PageSize];
        if ((flags & CompressedFlag) == 0)
        {
            if (payload.Length != PageSize)
                throw new InvalidDataException($"RAM page {pageNumber} is {payload.Length} bytes, not {PageSize}");
            payload.CopyTo(page);
            return (pageNumber, page);
        }

        // A page that inflates to more or less than 16KB is corrupt (or a decompression bomb), so read
        // one byte past the end to find out rather than trusting the stream.
        using var inflater = new ZLibStream(new MemoryStream(payload.ToArray()), CompressionMode.Decompress);
        inflater.ReadExactly(page);
        if (inflater.ReadByte() != -1)
            throw new InvalidDataException($"RAM page {pageNumber} is longer than {PageSize} bytes");

        return (pageNumber, page);
    }
}
