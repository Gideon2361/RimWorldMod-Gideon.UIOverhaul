using System;
using System.IO;
using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.Saves.Zstd
{
    /// <summary>
    /// The mod-facing side of zstd decompression.
    ///
    /// <b>Kept apart from <see cref="ZstdDecompressor"/> on purpose.</b> That file is a faithful port of
    /// facebook's C and must stay diffable against it, so everything this mod needs *around* the decoder
    /// lives here instead: buffer sizing, the growth strategy, and the stream-shaped API the rest of the
    /// save code wants.
    /// </summary>
    internal static class ZstdCodec
    {
        /// <summary>The four bytes every zstd frame starts with, little endian 0xFD2FB528.</summary>
        private static readonly byte[] Magic = { 0x28, 0xB5, 0x2F, 0xFD };

        /// <summary>
        /// What to assume a frame expands to when it does not say.
        ///
        /// <b>Not a hypothetical case: every save the compression mod wrote omits its content size.</b> All
        /// eight test saves report unknown, and 7-Zip lists them as <c>unknown-content-size</c>, so this is
        /// the normal path rather than the fallback. Sixteen is what facebook's own harness assumes; the real
        /// ratio on save XML is about ten, so the first attempt is comfortably large and the growth loop
        /// below almost never runs.
        /// </summary>
        private const int AssumedRatio = 16;

        /// <summary>
        /// A ceiling on the guess, so a corrupt frame cannot be talked into an enormous allocation.
        ///
        /// Well above any real save: the largest here expands to 59 MB.
        /// </summary>
        private const long MaxGuess = 1536L * 1024 * 1024;

        internal static bool Looks(byte[] leading)
        {
            if (leading == null || leading.Length < 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (leading[i] != Magic[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Decompresses a whole zstd frame.
        ///
        /// <b>The output size is discovered rather than trusted.</b> When the frame states its content size
        /// that value is used directly. When it does not, which is every save written by the compression mod,
        /// the buffer starts at a generous multiple of the input and doubles if the decoder runs out of room.
        /// Doubling rather than guessing again from scratch means a pathological ratio costs a few extra
        /// passes rather than failing outright.
        /// </summary>
        internal static byte[] Decompress(byte[] source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            long stated = ZstdDecompressor.GetDecompressedSize(source, source.Length);
            long guess = stated >= 0 ? stated : (long) source.Length * AssumedRatio;

            if (guess > MaxGuess)
                throw new InvalidDataException("zstd: this save claims to be implausibly large.");

            while (true)
            {
                byte[] destination = new byte[guess];

                try
                {
                    int written = ZstdDecompressor.Decompress(destination, (int) guess, source,
                        source.Length);

                    if (written == destination.Length)
                        return destination;

                    byte[] exact = new byte[written];
                    Array.Copy(destination, exact, written);

                    return exact;
                }
                catch (InvalidDataException ex) when (stated < 0 && IsTooSmall(ex))
                {
                    // Only retried when the frame never told us the size. A frame that stated its size and
                    // then overflowed it is corrupt, and growing the buffer would be hiding that.
                    guess *= 2;

                    if (guess > MaxGuess)
                        throw;
                }
            }
        }

        /// <summary>
        /// Whether a failure was simply "the buffer was too small", which is the one recoverable error here.
        ///
        /// Matched on the decoder's own wording, which is the C's message and therefore stable: it is the
        /// text upstream has used for this condition unchanged.
        /// </summary>
        private static bool IsTooSmall(Exception ex)
        {
            return ex.Message.IndexOf("output buffer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Decompresses a file into a readable stream.
        ///
        /// Whole-buffer rather than streaming, because the decoder needs the entire window available for back
        /// references and a save is read once, immediately, into a document that is far larger than either
        /// buffer. Streaming would add complexity to save memory that the XML reader is about to spend anyway.
        /// </summary>
        internal static Stream OpenRead(string path)
        {
            return new MemoryStream(Decompress(File.ReadAllBytes(path)), false);
        }

        /// <summary>Whether a file on disk is a zstd frame, by its leading bytes.</summary>
        internal static bool IsZstd(string path)
        {
            return UIGuard.Try("Saves.SniffZstd", () =>
            {
                using (FileStream file = File.OpenRead(path))
                {
                    byte[] leading = new byte[4];

                    return file.Read(leading, 0, 4) == 4 && Looks(leading);
                }
            }, false, null);
        }
    }
}
