using System.IO;
using System.IO.Compression;
using System.Text;
using Gideon.UIOverhaul.Features.Saves.Zstd;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>What a save file turned out to be.</summary>
    internal enum SaveFormat
    {
        /// <summary>Plain XML, which is what RimWorld writes without help.</summary>
        Plain,

        /// <summary>Ours.</summary>
        Lzma,

        /// <summary>Written by AmCh's Save File Compression.</summary>
        Zstd,

        Gzip,
        Deflate
    }

    /// <summary>
    /// Opens a save whatever it was compressed with.
    ///
    /// <b>Reading is generous, writing is not.</b> This mod writes LZMA and nothing else, but a player's
    /// Saves folder is a historical record: plain XML from vanilla, zstd and gzip from Save File Compression,
    /// LZMA from us. Refusing to read any of those would mean a save manager that cannot open half the saves
    /// it lists, so everything gets read and only one thing gets written.
    ///
    /// <b>Detected by content, never by extension.</b> Every one of these files is named <c>.rws</c>,
    /// because each tool wrote through RimWorld's own path. The first four bytes are the only honest answer
    /// to what a file actually is.
    ///
    /// <b>Nothing here needs another mod loaded.</b> LZMA is the vendored SDK, zstd is our own port of
    /// facebook's decoder, and gzip and deflate come from the framework. A save written by a mod that has
    /// since been removed still opens, which is the entire point.
    /// </summary>
    internal static class SaveArchive
    {
        /// <summary>
        /// Enough bytes to tell every format apart. Four for zstd's magic, one for LZMA's properties byte.
        /// </summary>
        private const int SniffLength = 4;

        /// <summary>
        /// How much plain XML a header read is allowed to decompress.
        ///
        /// One megabyte, which is generous on purpose. The meta element is a version string, a timestamp and a
        /// mod list, and even a three hundred mod list is tens of kilobytes; the budget is set well clear of that
        /// so the fallback to a full read stays a genuine edge case rather than a routine second pass. A megabyte
        /// of LZMA or zstd is a few milliseconds against most of a second for a whole colony.
        /// </summary>
        private const int HeaderBudget = 1024 * 1024;

        /// <summary>
        /// What this file is, from its leading bytes.
        ///
        /// <b>Order matters.</b> zstd and gzip have real multi-byte magic numbers and are tested first. LZMA
        /// has no magic at all, only a properties byte that is 0x5D for every stream this mod writes, so it
        /// is tested last and anything unrecognised is treated as plain XML. That bias is deliberate: reading
        /// plain XML as plain XML always works, whereas guessing a compression format wrong fails loudly.
        /// </summary>
        internal static SaveFormat Detect(byte[] leading, int length)
        {
            if (leading == null || length <= 0)
                return SaveFormat.Plain;

            if (ZstdCodec.Looks(leading))
                return SaveFormat.Zstd;

            if (length >= 2 && leading[0] == 0x1F && leading[1] == 0x8B)
                return SaveFormat.Gzip;

            // A UTF-8 BOM or an opening angle bracket is plainly XML, and settles it before the LZMA
            // properties byte is considered at all.
            if (leading[0] == 0xEF || leading[0] == 0x3C)
                return SaveFormat.Plain;

            if (leading[0] == LzmaCodec.PropertiesMarker)
                return SaveFormat.Lzma;

            // Raw deflate has no header to recognise. 0x78 is the common zlib marker, which is the only
            // deflate variant likely to reach here.
            if (leading[0] == 0x78)
                return SaveFormat.Deflate;

            return SaveFormat.Plain;
        }

        internal static SaveFormat DetectFile(string path)
        {
            using (FileStream file = File.OpenRead(path))
            {
                byte[] leading = new byte[SniffLength];
                int read = file.Read(leading, 0, SniffLength);

                return Detect(leading, read);
            }
        }

        /// <summary>How a format should be described to somebody looking at a list of saves.</summary>
        internal static string Describe(SaveFormat format)
        {
            switch (format)
            {
                case SaveFormat.Lzma: return "LZMA";
                case SaveFormat.Zstd: return "zstd";
                case SaveFormat.Gzip: return "gzip";
                case SaveFormat.Deflate: return "deflate";
                default: return "uncompressed";
            }
        }

        /// <summary>
        /// A reader over a save's XML, decompressing on the way if it needs to.
        ///
        /// <b>This is what the transpiler substitutes for <c>new StreamReader(path)</c></b> inside RimWorld's
        /// three save-reading methods, so the return type has to be <c>StreamReader</c> exactly: the IL that
        /// follows expects one on the stack. See <c>Patch_SaveArchive</c>.
        ///
        /// <b>Decompressed whole rather than streamed.</b> LZMA and zstd both need the full window available
        /// for back references, and the game is about to build an XmlDocument several times larger than
        /// either buffer, so streaming would add real complexity to save memory that is spent moments later
        /// anyway.
        /// </summary>
        internal static StreamReader OpenReader(string path)
        {
            SaveFormat format = DetectFile(path);

            if (format == SaveFormat.Plain)
                return new StreamReader(path);

            return new StreamReader(OpenDecompressed(path, format), Encoding.UTF8);
        }

        /// <summary>
        /// A reader over enough of a save's start to find its meta element, and no more.
        ///
        /// <b>Reading a header used to cost decompressing the colony.</b> The meta element sits in the first few
        /// kilobytes of the XML; the game node behind it is tens of megabytes. <see cref="OpenReader"/> hands back
        /// the whole thing, so selecting a save in the load window paid most of a second to read a version string
        /// and a mod list. This asks each codec for a prefix instead.
        ///
        /// <b>gzip, deflate and plain XML need nothing:</b> all three are already streams that read on demand, so
        /// the reader stops pulling as soon as the caller stops asking. Only LZMA and zstd buffer whole, and both
        /// take a limit.
        ///
        /// <b>The result is a truncated document</b> and is only safe for a caller that reads a prefix and stops.
        /// A caller must also be ready for the element it wants to be missing, since a save with an unusually
        /// large mod list could in principle run past the budget. See <c>SaveHeader</c>, which falls back to a
        /// full read in exactly that case rather than reporting an empty header.
        /// </summary>
        internal static StreamReader OpenHeaderReader(string path)
        {
            SaveFormat format = DetectFile(path);

            switch (format)
            {
                case SaveFormat.Lzma:
                {
                    MemoryStream plain = new MemoryStream();

                    using (FileStream file = File.OpenRead(path))
                        LzmaCodec.Decompress(file, plain, HeaderBudget);

                    plain.Position = 0;

                    return new StreamReader(plain, Encoding.UTF8);
                }

                case SaveFormat.Zstd:
                    return new StreamReader(ZstdCodec.OpenReadPrefix(path, HeaderBudget), Encoding.UTF8);

                default:
                    return OpenReader(path);
            }
        }

        private static Stream OpenDecompressed(string path, SaveFormat format)
        {
            switch (format)
            {
                case SaveFormat.Zstd:
                    return ZstdCodec.OpenRead(path);

                case SaveFormat.Lzma:
                {
                    MemoryStream plain = new MemoryStream();

                    using (FileStream file = File.OpenRead(path))
                        LzmaCodec.Decompress(file, plain);

                    plain.Position = 0;

                    return plain;
                }

                case SaveFormat.Gzip:
                    return new GZipStream(File.OpenRead(path), CompressionMode.Decompress);

                case SaveFormat.Deflate:
                {
                    FileStream file = File.OpenRead(path);

                    // Skip the two byte zlib header, which DeflateStream does not accept: it wants the raw
                    // deflate payload rather than the wrapper around it.
                    file.ReadByte();
                    file.ReadByte();

                    return new DeflateStream(file, CompressionMode.Decompress);
                }

                default:
                    return File.OpenRead(path);
            }
        }
    }
}
