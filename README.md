# PixelWorld

ZX Spectrum font conversion, ripping and manipulation tool for 8x8 bitmap fonts.

This is a tool I have been using for a number of years to convert my own fonts into a variety of formats available from https://damieng.com/fonts/zx-origins/ 

It also contains bulk-ripping `dump` and `hunt` commands for obtaining ZX Spectrum RAW 768 byte/.ch8 files from memory dumps and snapshots.

## Commands

### Extraction

- `dump` to RAM-dump snapshot files (currently supports `.z80` and `.sna` and recurses through `.zip` archives)
- `hunt` to look through RAM dumps for possible bitmap fonts (currently supports only ZX Spectrum fonts); `--known <folder>` skips fonts already in a folder of `.ch8` files
- `screenshot` to create a screenshots from RAM-dumps or snapshot files (in PNG, SCR or animated GIF format)
- `extracttiles` to create a files with unique 8x8 character tiles found on the screen in RAM-dumps or snapshot files

### Conversion

The following commands work with ZX Spectrum RAW 768 byte/.ch8 files:

- `preview` to create a `.png` preview file
- `pngtozx` to convert a `.png` back to a ZX Spectrum `.ch8`
- `c64tozx` to create a ZX Spectrum `.ch8` file from a C64 binary file

You can also generate directly-usable files (all binary except the CPC)

- `zxtoa8` to create a `.fnt` binary version for the Atari 8-bit series
- `zxtoc64` to create `.c64` and `.bin` binary ROM versions for the Commodore 64
- `zxtoccv` to create a `.chr` binary version for the CoCoVGA adapter
- `zxtocpc` to create a `.bas` BASIC file for use with the Amstrad CPC range
- `zxtofzx` to create a fixed-width `.fzx` font for the FZX renderer
- `zxtofzx --proportional` to create a proportional `.fzx` by auto left aligning and measuring widths
- `zxtogbs` to create a `.gbs` binary version for GameBoy Studio
- `zxtomsx` to create a `.msx` binary version for the MSX range
- `zxtoufo` to create a `.ufo` UFO font file for various tools and editors

### Source generation

You can convert ZX Spectrum RAW 768 byte/.ch8 files to a variety of assembly and header file formats.

The following assembly generating commands can use the `--base hex|decimal` flags. The z80 one can also use `--base binary`.

- `6502asm` to create MOS 6502 assembler source with `.byte` hex
- `68000asm` to create Motorola 68000 assembler source with `DB.B` hex
- `x86asm` to create Intel 8086 assembler source with `db` hex
- `z80asm` to create Zilog Z80 assembler source with `defb` hex

And the header-generating commands:

- `chead` to generate C-compatible header files
- `rusthead` to generate Rust-compatible header files

### Comparison

- `findmatches` to find glyphs from a source font in as many possible target .ch8 files

Any command can be further detailed using the `--help` option which will detail the additional flags and options available.

## Hunt strategies

Finding bitmap fonts in a raw memory dump isn't trivial as there's no specific header however there are a few different strategies available in the code that can be combined and tweaked, they are:

### [EnvironmentGuidedFinder](https://github.com/damieng/pixelworld/blob/main/Common/OffsetFinders/EnvironmentGuidedFinder.cs)

This relies on programmers using the ROM routines to print text to the screen and is surprisingly successful. It basically looks for a font at whatever RAM location is specified at memory locations 23606 & 23607. It can only find one font however so games like Millionaire that use several get missed.

### [KnownCharPatternFinder](https://github.com/damieng/pixelworld/blob/main/Common/OffsetFinders/KnownCharPatternFinder.cs)

This looks for fonts based on well-known glyphs that designers rarely changed such as the copyright symbol and by using their known position in the font works out where the font is located.

### [CandidatesInWindowFinder](https://github.com/damieng/pixelworld/blob/main/Common/OffsetFinders/CandidatesInWindowFinder.cs)

This provides an array of potential glyphs without any knowledge of what they actually are. It requires you pass a certain number of glyphs and looks to find a minimum amount of them in a sequence in RAM.

This works in conjunction with [SpectrumDisplay.GetCandidates](https://github.com/damieng/pixelworld/blob/main/Common/Display/SpectrumDisplay.cs#L69) to divide the current screen memory into as many unique 8x8 character blocks as it can. The principle here is that the font is probably used on-screen and is aligned to the usual (32x24) text grid.

### [GeneralHeuristicFinder](https://github.com/damieng/pixelworld/blob/main/Common/OffsetFinders/GeneralHeuristicFinder.cs)

This finder looks for fonts by expecting certain pixel densities in relative to each other. For example ! should have less pixels than m. c should have less pixels than o etc.

### [StructuralFinder](https://github.com/damieng/pixelworld/blob/main/Common/OffsetFinders/StructuralFinder.cs)

This scores every offset by how the glyphs relate to each other, using [FontStructure](https://github.com/damieng/pixelworld/blob/main/Common/OffsetFinders/FontStructure.cs): bracket pairs such as ( and ) that are mirror images, letters such as P and R that share their top rows, symmetric capitals such as A, H and O, descenders on g j p q y, and a blank column or row shared by the letters. Symmetric glyphs where B, C or 2 belong, or a, c and o dropping below the baseline, count against an offset, which is what tells a correct reading from one a few characters off.

It doesn't need a space, symbols or lowercase, so it also finds fonts that only define digits and capitals. The slots that belong to the font are worked out from which ones respect the capitals' blank columns, and the rest are left blank in the extracted font.

### [CodeReferenceFinder](https://github.com/damieng/pixelworld/blob/main/Common/OffsetFinders/CodeReferenceFinder.cs)

A game that prints text has to have its font address somewhere in its code. This looks for `LD (23606),rr` after `LD rr,nn` (setting CHARS even if it has since been changed), and for print routines that multiply the character code by 8 with three `ADD HL,HL` and then add a constant. The constant could be the address of space, of character 0, of 0 or of A, so each reading is scored and the best kept. Because the code backs up the address, a lower score is accepted than for a blind scan.

### How finds are combined

Several finders often suggest the same font, and the newer ones suggest many readings of it a few bytes or characters apart. Finds from the original four finders are kept as before. A find from StructuralFinder or CodeReferenceFinder is kept only if it doesn't overlap one of those or a better-scoring new find, since two fonts can't occupy the same memory. Copies of the ROM font are skipped, and where the same font is in a dump twice only one copy is kept.

`hunt --known <folder>` also skips any font identical to one already in a folder of `.ch8` files, and reports how many it skipped.

### Hunt output

Each font is written as `<dump name>#<offset>.ch8`, where the offset is where the font's space is in the dump, or would be if the font started there. A partial font keeps the characters it defines in their usual places and leaves the rest blank, so it is still a 768-byte font.

The log line for each font explains why it was kept:

- `score` is the StructuralFinder score with its parts, which shows how font-like it is
- `via` lists the finders that suggested it
- `partial '0'..'Z'` gives the characters present when the font doesn't define them all
- `ROM-derived (n glyphs changed)` marks fonts that differ from the ROM font in only a few glyphs

## Setup

The projects target .NET 9, so build them with the .NET 9 SDK (`dotnet build`) or Visual Studio 2022 17.12 or later.

Some [template files are necessary for the conversions](https://github.com/damieng/pixelworld/discussions/14) and should have their location specified by the `--templatePath` parameter.

- `atari8.fnt` - A dump of the Atari system font that includes all the symbols characters etc.
- `c64-both.c64` - A dump of the Commodore 64 upper+lower case
- `c64-upper.c64` - A dump of the Commodore 64 upper case + symbols
- `cocovga.chr` - A dump of the CoCoVGa font (3082 bytes)

## Example usage

Various different usages of this command can be fount in my [ZX Origins font-publishing script](https://gist.github.com/damieng/d2519cda1c674b4ede74f154f05f2431) - every reference there to `pw.exe` is this.
