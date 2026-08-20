using System;
using System.IO;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using SevenZip;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Compresses a save once RimWorld has finished writing it.
    ///
    /// <b>After the write rather than during it, deliberately.</b> The obvious design is to put an encoder
    /// between the scribe and the disk, and it does not work: LZMA's encoder pulls from its source, so
    /// feeding it a document the game is still pushing out needs a second thread and a pipe between them. The
    /// alternative is buffering the whole save in memory to hand over at the end, which on a large colony is
    /// fifty megabytes of XML held beside the game's own copy of it. Letting vanilla write the file and then
    /// rewriting it costs one extra pass over the disk and keeps <c>SafeSaver</c>, which writes to a
    /// temporary file and swaps it in only on success, exactly as it was.
    ///
    /// <b>Nothing is replaced until the result has been read back.</b> A compressor that silently produces a
    /// corrupt file does not announce itself at save time; it announces itself weeks later when a colony will
    /// not open. Every save is decompressed again and checked against the original before the original is
    /// allowed to go. See <see cref="Verifies"/>.
    ///
    /// <b>Every failure leaves the plain save.</b> That is the whole safety argument for this feature: an
    /// uncompressed save is a perfectly good save, so there is no failure here that costs anybody a colony.
    /// Compression is an optimisation and is treated as one.
    /// </summary>
    internal static class SaveCompressor
    {
        /// <summary>Where the compressed copy is built, beside the save it came from.</summary>
        private const string WorkingSuffix = ".compressing";

        /// <summary>
        /// What was chosen for the save currently being written, or null when nobody chose.
        ///
        /// <b>Null is what tells an autosave apart from a save somebody asked for.</b> Both arrive at
        /// <c>GameDataSaveLoader.SaveGame</c> and are indistinguishable there, and they want different
        /// answers: a save the player is waiting on can afford a few seconds, whereas an autosave firing
        /// mid-raid cannot. <see cref="SaveWriter"/> arms this and clears it in a finally, exactly as it does
        /// with <c>SaveFolders.Redirect</c> and for the same reason: a value left standing would apply the
        /// dialog's choice to every autosave for the rest of the session.
        /// </summary>
        internal static bool? Requested;

        /// <summary>
        /// Compresses the save just written to <paramref name="path"/>, if that was wanted.
        ///
        /// Guarded rather than allowed to throw: this runs inside <c>SaveGame</c>, which has already put the
        /// player's colony safely on disk by the time it is reached.
        /// </summary>
        internal static void AfterWrite(string path)
        {
            AfterWrite(path, Requested ?? UIOverhaulSettingsFile.Current.compressAutosaves);
        }

        /// <summary>
        /// Compresses a file that was written by something other than <c>SaveGame</c>.
        ///
        /// <b>The caller says whether it wants compression rather than leaving a static armed.</b>
        /// <see cref="Requested"/> exists to tell an autosave apart from a save the player asked for, and both of
        /// those arrive on the main thread inside one call. The sweep writes from a long event on another thread,
        /// where arming a static would race any save happening at the same time and could hand the dialog's
        /// answer to an autosave. Passing the answer in costs one parameter and cannot race anything.
        /// </summary>
        internal static void AfterWrite(string path, bool wanted)
        {
            if (!wanted)
                return;

            UIGuard.Try("Saves.Compress", () => Compress(path),
                "The save was written but could not be compressed. It is complete and will load normally.");
        }

        private static void Compress(string path)
        {
            // SaveGame catches its own exceptions and logs them, so it returns normally after a failed write.
            // A missing file here means there is nothing to compress rather than nothing to worry about.
            if (path.NullOrEmpty() || !File.Exists(path))
                return;

            // Anything already compressed is left alone. This costs one read of four bytes and means the
            // postfix cannot compress a file twice if it is ever reached twice.
            if (SaveArchive.DetectFile(path) != SaveFormat.Plain)
                return;

            long plainLength = new FileInfo(path).Length;

            if (plainLength <= 0)
                return;

            string working = path + WorkingSuffix;

            try
            {
                uint plainCrc;

                using (FileStream source = File.OpenRead(path))
                using (FileStream destination = File.Create(working))
                {
                    // The checksum is taken from the bytes the encoder reads, so the file is not walked a
                    // second time purely to fingerprint it.
                    Tally counted = new Tally(source);

                    LzmaCodec.Compress(counted, destination, plainLength);

                    plainCrc = counted.Digest;
                }

                if (!Verifies(working, plainLength, plainCrc))
                {
                    Discard(working);

                    UIGuard.Report("Saves.CompressVerify",
                        new InvalidDataException("Compressed save did not read back identically: " + path),
                        "The save was written but not compressed, because the compressed copy did not match. "
                        + "The save itself is complete and will load normally.");

                    return;
                }

                long compressedLength = new FileInfo(working).Length;

                Swap(working, path);

                Log.Message(UILogTag.Prefix + "Compressed " + Path.GetFileNameWithoutExtension(path) + ": "
                            + SavesChrome.Size(plainLength) + " to " + SavesChrome.Size(compressedLength)
                            + " (" + Ratio(plainLength, compressedLength) + ").");
            }
            catch
            {
                // The half-written working file goes before the exception is reported, or the Saves folder
                // accumulates one of these for every save that ever ran out of disk.
                Discard(working);

                throw;
            }
        }

        /// <summary>
        /// Whether the compressed file decompresses back to exactly what went in.
        ///
        /// <b>Length and checksum, not length alone.</b> A truncated stream is the easy failure and a length
        /// check would find it; the failure worth guarding against is the quiet one, where the right number of
        /// bytes come back with the wrong contents somewhere in the middle. That is what took eight test saves
        /// and two independent oracles to rule out when the zstd decoder was written, and it is not a class of
        /// bug to leave undetected on the writing side.
        ///
        /// <b>CRC32 from the vendored SDK rather than a cryptographic hash.</b> Nothing here is defending
        /// against a forged save; the question is only whether a round trip through code we wrote returns the
        /// bytes it was given, and a checksum already sitting in the assembly answers that without reaching
        /// for the crypto stack.
        /// </summary>
        private static bool Verifies(string working, long plainLength, uint plainCrc)
        {
            using (FileStream compressed = File.OpenRead(working))
            {
                // No inner stream: the decompressed bytes are measured and thrown away rather than written
                // anywhere, so verifying costs a read of the compressed file and no disk at all.
                Tally counted = new Tally(null);

                LzmaCodec.Decompress(compressed, counted);

                return counted.Moved == plainLength && counted.Digest == plainCrc;
            }
        }

        /// <summary>
        /// Puts the compressed copy in the save's place.
        ///
        /// <b><c>File.Replace</c> first, because it never leaves the save absent.</b> Deleting and then moving
        /// has a window, however short, in which the colony exists only as a file named <c>.compressing</c>,
        /// and a power cut inside that window is unrecoverable for anybody who does not know to rename it.
        /// Replace is the operation that has no such window. It is not available on every filesystem, so a fallback
        /// is needed. But the fallback must not be delete-then-move.
        ///
        /// <b>The old fallback lost a colony.</b> It ran <c>File.Delete(path)</c> and then <c>File.Move</c>, so if
        /// the move failed for any reason the save existed nowhere at all: the original was already gone and the
        /// working file was still under its own name. Compression had verified its output and reported success by
        /// then, so the log showed a clean save and the file was missing. Reported by Aaron 2026-08-18 for a save
        /// named "Northern Hibum - LZMA", which vanished entirely.
        ///
        /// <b>Now the old save is moved aside rather than removed,</b> and only deleted once the new one is in
        /// place. At no instant do zero copies exist, and if the second move fails the original is put back where
        /// it was. Nothing is deleted before something else is known to be readable.
        /// </summary>
        private static void Swap(string working, string path)
        {
            UIDebug.Log("Saves.Swap: putting " + working + " in place of " + path);

            try
            {
                File.Replace(working, path, null);
                UIDebug.Log("Saves.Swap: File.Replace succeeded");

                return;
            }
            catch (PlatformNotSupportedException ex)
            {
                UIDebug.Log("Saves.Swap: File.Replace unsupported here, falling back: " + ex.Message);
            }
            catch (IOException ex)
            {
                UIDebug.Log("Saves.Swap: File.Replace failed, falling back: " + ex.Message);
            }

            // Named beside the save rather than in a temp folder, so it is on the same volume and the move is a
            // rename rather than a copy. A rename cannot half succeed.
            string aside = path + ".previous";

            Discard(aside);

            bool movedAside = false;

            try
            {
                if (File.Exists(path))
                {
                    File.Move(path, aside);
                    movedAside = true;
                    UIDebug.Log("Saves.Swap: moved the previous save aside to " + aside);
                }

                File.Move(working, path);
                UIDebug.Log("Saves.Swap: moved the compressed copy into place");
            }
            catch (Exception ex)
            {
                UIDebug.Warning("Saves.Swap: fallback failed: " + ex.Message);

                // Put it back. This is the whole reason the old save was moved instead of deleted, and it runs
                // before the exception is allowed to continue so the restore happens even though the save failed.
                if (movedAside && !File.Exists(path) && File.Exists(aside))
                {
                    File.Move(aside, path);
                    UIDebug.Warning("Saves.Swap: restored the previous save, which is unchanged");
                }

                throw;
            }

            Discard(aside);
        }

        private static void Discard(string working)
        {
            try
            {
                if (File.Exists(working))
                    File.Delete(working);
            }
            catch (Exception)
            {
                // Already handling a failure. A working file that cannot be removed is untidy and is not worth
                // replacing the real problem in the log with.
            }
        }

        private static string Ratio(long plain, long compressed)
        {
            if (compressed <= 0)
                return "unknown";

            return ((float) plain / compressed).ToString("F1") + " times smaller";
        }

        /// <summary>
        /// A stream that checksums and counts everything passing through it.
        ///
        /// Used in both directions: wrapped around the source while compressing, so the original is
        /// fingerprinted by the encoder's own reads, and used as the destination while verifying, so the
        /// decompressed bytes are measured without being written anywhere.
        ///
        /// <b>Deliberately not seekable.</b> LZMA reads its input strictly forward and writes its output
        /// strictly forward, so refusing to seek costs nothing and means a future caller that needs seeking
        /// fails loudly here instead of silently checksumming the wrong bytes.
        /// </summary>
        private sealed class Tally : Stream
        {
            private readonly Stream inner;
            private readonly CRC crc = new CRC();
            private long moved;

            /// <param name="inner">Null makes this a sink, which is what verifying wants.</param>
            internal Tally(Stream inner)
            {
                this.inner = inner;
            }

            /// <summary>How many bytes have passed through.</summary>
            internal long Moved => moved;

            /// <summary>The checksum so far. Reading it does not end the run.</summary>
            internal uint Digest => crc.GetDigest();

            public override bool CanRead => inner != null && inner.CanRead;

            public override bool CanWrite => true;

            public override bool CanSeek => false;

            public override int Read(byte[] buffer, int offset, int count)
            {
                int got = inner.Read(buffer, offset, count);

                // Only what was actually returned. A stream is allowed to hand back less than was asked for,
                // and checksumming the whole buffer would fold in whatever was left there from last time.
                if (got <= 0)
                    return got;

                crc.Update(buffer, (uint) offset, (uint) got);
                moved += got;

                return got;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (count <= 0)
                    return;

                crc.Update(buffer, (uint) offset, (uint) count);
                moved += count;

                if (inner != null)
                    inner.Write(buffer, offset, count);
            }

            public override void Flush()
            {
                if (inner != null)
                    inner.Flush();
            }

            public override long Length
            {
                get { throw new NotSupportedException(); }
            }

            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }
        }
    }
}
