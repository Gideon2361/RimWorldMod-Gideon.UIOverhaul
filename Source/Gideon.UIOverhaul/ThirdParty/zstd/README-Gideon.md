# zstd educational decoder, vendored as reference

`educational_decoder/` is **third-party C source, copied byte for byte** from
[facebook/zstd](https://github.com/facebook/zstd), `doc/educational_decoder/`. Dual licensed BSD-3 and
GPLv2; we take the BSD-3 option.

## This C is not compiled into the mod

It is here to be **read and diffed against**, not built. The mod ships a C# port of it, in
`UIOverhaul/Features/Saves/Zstd/`. Keeping the original beside the port is what makes the port
maintainable: when facebook changes the decoder, the diff against this folder says exactly what has to
change on our side.

Do not edit anything under `educational_decoder/`, for the same reason nothing under `ThirdParty/LZMA/CS`
is edited.

## Why a port rather than the real libzstd

Three routes were considered for reading zstd saves, which exist only because
`AmCh.SaveFileCompression` wrote them:

* **Native libzstd.** Fastest and most complete. Rejected because a Workshop mod is cross-platform by
  default, being managed code, and a native dependency needs `.dll`, `.so` and `.dylib` built and
  maintained separately. The toolchain here produces win64 only, so shipping it would quietly break the mod
  for every Mac and Linux subscriber.
* **ZstdSharp.Port** (managed, MIT). Rejected because on `net481` it needs `System.Memory`,
  `System.Runtime.CompilerServices.Unsafe` and `Microsoft.Bcl.AsyncInterfaces`. RimWorld ships none of the
  three, and those two BCL shims are a known source of cross-mod binding failures.
* **This port.** Pure managed, no dependencies, no unsafe, works everywhere the mod does. Decompression
  only, which is the entire requirement: nothing needs to *write* zstd. Notably, official 7-Zip reached the
  same conclusion and also implements decompression only.

## Verification

The port must agree byte for byte with two independent implementations on real saves. Both are available
on this machine:

1. **7-Zip 26.01**, which carries Igor Pavlov's own BSD-3 zstd decoder, written using facebook's as
   reference. `7z.exe x file.zst`.
2. **This C, built with gcc.** `gcc -O2 -Wall -o zstddec.exe harness.c zstd_decompress.c` using the
   `harness.c` from the same upstream folder.

Those two were checked against each other first, over eight real saves totalling roughly 400 MB of
decompressed XML, and agreed on every byte. That makes either one a trustworthy oracle for the port.

A subtly wrong decoder does not fail loudly; it hands back a corrupted colony. Nothing here ships until
every one of those saves round trips to an identical SHA-256.
