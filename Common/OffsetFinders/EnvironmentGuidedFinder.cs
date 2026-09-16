using System;
using PixelWorld.Machines;
using System.Collections.Generic;

namespace PixelWorld.OffsetFinders;

public static class EnvironmentGuidedFinder
{
    public static List<Int32> FindOffsets(Byte[] buffer)
    {
        var results = new List<Int32>();

        // The CHARS system variable is a 16-bit pointer held at 23606. dump always supplies a full
        // address space, but hunt runs these finders over arbitrary files, so a shorter buffer has no
        // such location to read.
        if (buffer.Length < Spectrum.CharsSysVar + 2) return results;

        var spectrumSysChars = buffer[Spectrum.CharsSysVar] + buffer[Spectrum.CharsSysVar + 1] * 256 + 256;

        if (spectrumSysChars <= Spectrum.ScreenStart) return results; // Was not pointing to the ROM
        
        if (spectrumSysChars + Spectrum.FontSize < buffer.Length && buffer.IsEmpty(spectrumSysChars))
            results.Add(spectrumSysChars);
        
        return results;
    }
}