# Changelog

## 0.6.0 - 2026-11-14

### Font hunting

- `hunt` finds fonts by structure (mirrored brackets, letters with matching tops, symmetric capitals, descenders and blank gutters), by addresses in the program code that point at them (including partial fonts), and by the ink profile of the capitals. Over a collection of 5,752 RAM dumps it now finds 4,742 fonts, up from 4,249.
- Fonts that only score a little under the usual threshold are accepted when nothing counts against them and nearly every capital is different.
- A real font found underneath a junk find replaces it, and a reading that the code points at wins over one a row off.
- `hunt --known <folder>` skips fonts you already have as `.ch8` files.
- Known limitation: a font whose digits sit next to its capitals is found as A-Z only, without the digits.

### Images

- Our own PNG, GIF and WebP code replaces ImageSharp, which had security advisories and needs a paid licence for newer versions. There is no image dependency any more.
- PNGs are written as indexed colour at the smallest bit depth that fits, so they are usually smaller. `pngtozx` reads PNGs only (all colour types, bit depths, interlacing and transparency).
- `screenshot --webp` and `preview --webp` write lossless WebP. Spectrum screens come out about 8% smaller than the PNG.
- `screenshot --gif` writes the same two-frame flashing animation as before.
- `preview --png` is the default output.

### Fixes

- Fixed about 25 bugs found in code review, among them:
  - preview images were overwritten in place, leaving stale bytes after a shorter image
  - `findmatches` matched every file when no glyphs were given
  - `Font.GetHashCode` disagreed with `Equals`
  - `--base binary` output for 68000asm was wrong
  - globs were not split on `/`
  - a failed snapshot decode stopped `dump`
  - the 128K SNA tail-bank loop was not bounded
  - unmapped glyphs made UFO output throw
  - the screenshot window was not checked against the memory it reads
  - negative widths could result from proportional padding
  - the success rate printed NaN when there were no inputs

### Other

- Targets .NET 9; the build and release workflows use the .NET 9 SDK.
- Packages updated (Spectre.Console.Cli 0.57, test packages); `Microsoft.Extensions.FileSystemGlobbing` stays on 10.x until we move to .NET 10.
- Commands take a cancellation token.
- The test suite grew from 44 to 184 tests.
