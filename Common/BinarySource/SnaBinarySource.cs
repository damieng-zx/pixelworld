using System;
using System.IO;
using System.Text;

namespace PixelWorld.BinarySource;

public class SnaBinarySource : IBinarySource
{
    public static IBinarySource Instance { get; } = new SnaBinarySource();

    public ArraySegment<Byte> GetMemory(Stream source)
    {
        // Read up to the signature rather than demanding it: this runs before the ZX handler that
        // makes unreadable files non-fatal, so a truncated .sna would otherwise throw out of the
        // whole run. Loops because a single Read is allowed to return a partial result.
        var signatureBuffer = new Byte[8];
        var signatureLength = 0;
        while (signatureLength < signatureBuffer.Length)
        {
            var read = source.Read(signatureBuffer, signatureLength, signatureBuffer.Length - signatureLength);
            if (read == 0) break;
            signatureLength += read;
        }

        if (signatureLength == signatureBuffer.Length
            && Encoding.ASCII.GetString(signatureBuffer, 0, signatureBuffer.Length) == "MV - SNA")
        {
            Out.Write("  Loading as Amstrad CPC");
            // Amstrad CPC SNA file
            source.Seek(0x100, SeekOrigin.Begin);
            return new ArraySegment<Byte>(source.ReadAllBytes());
        }

        // Anything too short to hold the signature is not a CPC snapshot, so hand it to the ZX
        // handler, which reports it as undecodable instead of throwing.
        source.Seek(0, SeekOrigin.Begin);
        return ZXSnaBinarySource.Instance.GetMemory(source);
    }
}