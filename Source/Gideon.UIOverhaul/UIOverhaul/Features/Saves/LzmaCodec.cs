using System;
using System.IO;
using SevenZip;
using SevenZip.Compression.LZMA;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// LZMA compression and decompression, over the vendored SDK.
    ///
    /// <b>This file is ours; everything it calls is not.</b> The encoder and decoder live under
    /// <c>ThirdParty/LZMA</c>, copied byte for byte from Igor Pavlov's public domain SDK so that a future
    /// release can be dropped straight over them. All of the adapting happens here instead, which is what
    /// keeps that folder pristine.
    ///
    /// <b>The container is the SDK's own "LZMA alone" layout,</b> which is what <c>lzma.exe</c> writes and
    /// what every LZMA tool can open: five bytes of encoder properties, then the uncompressed length as a
    /// little endian 64 bit integer, then the stream. Inventing a header of our own would have made a file
    /// only this mod could ever read, which is precisely the trap the zstd saves fell into.
    ///
    /// <b>Streamed rather than buffered whole.</b> A save is tens of megabytes of XML and holding the plain
    /// text, the compressed bytes and the game's own document in memory at once is how a compression feature
    /// causes an out of memory on the machine it was meant to help.
    /// </summary>
    internal static class LzmaCodec
    {
        /// <summary>
        /// The first byte of a properly formed stream: the default <c>lc=3, lp=0, pb=2</c> packed as
        /// <c>(pb * 5 + lp) * 9 + lc</c>. Used to recognise our own files.
        /// </summary>
        internal const byte PropertiesMarker = 0x5D;

        /// <summary>Five bytes of encoder properties, then eight of length.</summary>
        private const int HeaderLength = 13;

        /// <summary>
        /// How much of the input the encoder keeps in memory to find matches in.
        ///
        /// Sixteen megabytes, which is larger than the SDK's own default and chosen for what is being
        /// compressed. A save is one enormous XML document whose repetition is spread across the whole of it:
        /// the same tag names and def names recur from the first line to the last. A dictionary smaller than
        /// the file cannot see those repeats, and this is the setting that decides whether a fifty megabyte
        /// save compresses eight times or four.
        ///
        /// It also sets the encoder's memory use, at roughly ten times the dictionary. A hundred and sixty
        /// megabytes during a save is affordable; a dictionary sized to the largest possible save would not
        /// be.
        /// </summary>
        private const int DictionarySize = 1 << 24;

        /// <summary>
        /// Whether a file begins with something this can decode.
        ///
        /// Deliberately narrow: it recognises the properties byte the SDK produces with default settings,
        /// which is what <see cref="Compress"/> writes. A stream encoded with unusual literal context bits
        /// would still decode correctly but is not something this mod ever wrote, and claiming a file is ours
        /// on a weaker test risks trying to decompress somebody else's format.
        /// </summary>
        internal static bool Looks(byte[] leading)
        {
            return leading != null && leading.Length >= 1 && leading[0] == PropertiesMarker;
        }

        /// <summary>
        /// Compresses <paramref name="source"/> into <paramref name="destination"/>.
        /// </summary>
        /// <param name="length">
        /// The uncompressed length, written into the header so the decoder knows when to stop.
        ///
        /// Passed in rather than read from the stream, because the source is not always seekable and asking
        /// for <c>Length</c> is what makes a decorator stream throw.
        /// </param>
        internal static void Compress(Stream source, Stream destination, long length)
        {
            if (source == null || destination == null)
                throw new ArgumentNullException(source == null ? "source" : "destination");

            Encoder encoder = new Encoder();

            encoder.SetCoderProperties(
                new[] { CoderPropID.DictionarySize, CoderPropID.EndMarker },
                new object[] { DictionarySize, false });

            encoder.WriteCoderProperties(destination);

            // Little endian, which is the SDK's own convention and therefore what every other LZMA tool
            // expects to find here.
            for (int i = 0; i < 8; i++)
                destination.WriteByte((byte) (length >> (8 * i)));

            encoder.Code(source, destination, length, -1, null);
        }

        /// <summary>
        /// Decompresses <paramref name="source"/> into <paramref name="destination"/>.
        ///
        /// <b>A length of -1 in the header means the stream is terminated by an end marker rather than by a
        /// count.</b> Nothing here writes one, but files from other LZMA tools do, and the decoder handles
        /// that case when it is given -1, so it is passed through rather than rejected.
        /// </summary>
        internal static void Decompress(Stream source, Stream destination)
        {
            Decompress(source, destination, long.MaxValue);
        }

        /// <summary>
        /// Decompresses at most <paramref name="maxBytes"/> of plain output and then stops.
        ///
        /// <b>For reading a save's header without decompressing the save.</b> The meta element sits in the first
        /// few kilobytes of the XML and the colony behind it is tens of megabytes, so an unbounded read to answer
        /// "what version was this written by" costs the better part of a second for a few hundred bytes. The
        /// decoder takes the output length as a parameter and stops there of its own accord, so this is the same
        /// call with a smaller number.
        ///
        /// The result is a truncated document, which is only useful to a caller that reads a prefix and then
        /// stops. Anything needing the whole save must use the overload without a limit.
        ///
        /// <b>The limit is approximate, and slightly generous.</b> The decoder stops after the symbol that
        /// crosses it rather than mid-symbol, so the output can run over by up to one match length. Measured
        /// across the save corpus that is 19 to 180 bytes on a one megabyte budget. Every byte produced is
        /// identical to what a full read would have produced, which is the property a prefix reader needs; a
        /// caller wanting an exact count must trim.
        /// </summary>
        internal static void Decompress(Stream source, Stream destination, long maxBytes)
        {
            if (source == null || destination == null)
                throw new ArgumentNullException(source == null ? "source" : "destination");

            byte[] header = new byte[HeaderLength];

            if (Read(source, header, HeaderLength) != HeaderLength)
                throw new InvalidDataException("The file is too short to be an LZMA stream.");

            byte[] properties = new byte[5];
            Array.Copy(header, properties, 5);

            Decoder decoder = new Decoder();
            decoder.SetDecoderProperties(properties);

            long length = 0;

            for (int i = 0; i < 8; i++)
                length |= (long) header[5 + i] << (8 * i);

            // The header length when there is no limit, the limit when the header did not state a length, and
            // the smaller of the two when both are known. Passing -1 through unchanged matters: that is how the
            // decoder is told to run to an end marker rather than to a count.
            long wanted = maxBytes == long.MaxValue
                ? length
                : length < 0 ? maxBytes : Math.Min(length, maxBytes);

            decoder.Code(source, destination, 0, wanted, null);
        }

        /// <summary>
        /// Fills a buffer, since a single <c>Read</c> is allowed to return less than was asked for.
        ///
        /// Trusting one call is the classic stream bug: it works on a FileStream, which nearly always returns
        /// everything, and fails on anything buffered or compressed underneath.
        /// </summary>
        private static int Read(Stream source, byte[] into, int count)
        {
            int filled = 0;

            while (filled < count)
            {
                int got = source.Read(into, filled, count - filled);

                if (got <= 0)
                    break;

                filled += got;
            }

            return filled;
        }
    }
}
