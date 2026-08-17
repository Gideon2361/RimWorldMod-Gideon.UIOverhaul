# LZMA SDK, vendored

This folder is **third-party source, copied byte for byte** from the official LZMA SDK at
<https://www.7-zip.org/sdk.html>. Version **26.02**, archive `lzma2602.7z`.

## Do not edit anything under `CS/`

The SDK's own directory layout is preserved exactly (`CS/7zip/...`) so that a future SDK release can be
dropped straight over the top of it and any local difference shows up as a real diff rather than as noise.
Editing these files, even to fix a warning or restyle a brace, destroys that property: the next upgrade
becomes a manual merge instead of a copy.

Anything this mod needs *around* the codec lives outside this folder, in
`UIOverhaul/Features/Saves/LzmaCodec.cs`, in our own namespace.

## What was taken, and what was left

Only what the encoder and decoder need:

    CS/7zip/ICoder.cs
    CS/7zip/Common/CRC.cs
    CS/7zip/Common/InBuffer.cs
    CS/7zip/Common/OutBuffer.cs
    CS/7zip/Compress/LZ/IMatchFinder.cs
    CS/7zip/Compress/LZ/LzBinTree.cs
    CS/7zip/Compress/LZ/LzInWindow.cs
    CS/7zip/Compress/LZ/LzOutWindow.cs
    CS/7zip/Compress/LZMA/LzmaBase.cs
    CS/7zip/Compress/LZMA/LzmaDecoder.cs
    CS/7zip/Compress/LZMA/LzmaEncoder.cs
    CS/7zip/Compress/RangeCoder/RangeCoder.cs
    CS/7zip/Compress/RangeCoder/RangeCoderBit.cs
    CS/7zip/Compress/RangeCoder/RangeCoderBitTree.cs

`CRC.cs` looks like it belongs to the console tool and does not: `LzBinTree` uses its table for the match
finder's hash function, and leaving it out fails the build rather than merely losing a feature.

Deliberately not taken:

* `CS/7zip/Compress/LzmaAlone/*` is a console application. It carries a `Main`, a benchmark and its own
  assembly properties, none of which belong in a library that ships inside a game.
* `CS/7zip/Common/CommandLineParser.cs` is used only by that application.

The project compiles every `.cs` under it by glob, so adding files back here is enough to build them. That
is also why the console app was left out rather than merely unreferenced.

## Licence

Public domain. From the SDK's own `lzma-sdk.txt`, kept beside this file:

> LZMA SDK is written and placed in the public domain by Igor Pavlov.
>
> Anyone is free to copy, modify, publish, use, compile, sell, or distribute the original LZMA SDK code,
> either in source code form or as a compiled binary, for any purpose, commercial or non-commercial, and by
> any means.

No attribution is required. It is recorded here and in `THIRD-PARTY-NOTICES.txt` anyway, because a reader
finding several thousand lines of unfamiliar range-coder mathematics in a UI mod deserves to know instantly
where it came from and that it is safe to ship.

## Upgrading

1. Download the new `lzma####.7z` from 7-zip.org.
2. Copy the files listed above over `CS/`, keeping the paths.
3. Update the version at the top of this file.
4. Build. Anything that breaks is a genuine API change in the SDK, not a local edit coming back.
