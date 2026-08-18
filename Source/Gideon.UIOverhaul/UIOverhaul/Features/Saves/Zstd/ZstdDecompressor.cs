using System;
using System.IO;

namespace Gideon.UIOverhaul.Features.Saves.Zstd
{
    /// <summary>
    /// Zstandard decompression, ported from facebook's educational decoder.
    ///
    /// <b>A port, not an interpretation.</b> The original C is vendored beside this at
    /// <c>ThirdParty/zstd/educational_decoder/zstd_decompress.c</c>, and this file deliberately keeps its
    /// structure, its function names, its section banners and its ordering so the two can be read side by
    /// side and diffed when facebook changes the decoder. Where a name here looks un-C-sharp, that is why.
    ///
    /// <b>Why a port rather than the real libzstd.</b> A native dependency would need a .dll, a .so and a
    /// .dylib built and maintained separately, and would break the mod for every Mac and Linux subscriber if
    /// any were missing. ZstdSharp needs three BCL shim assemblies RimWorld does not ship. This is pure
    /// managed, allocates nothing unmanaged, and works wherever the mod does. Full reasoning in the README
    /// beside the C.
    ///
    /// <b>Decompression only, which is the whole requirement.</b> Nothing needs to write zstd: the format
    /// only appears in a save because another mod put it there. Official 7-Zip made the same choice for the
    /// same reason.
    ///
    /// <b>Dictionaries are not supported.</b> The C carries them; RimWorld saves never use one. Rather than
    /// port a path that cannot be tested against real data, a framed dictionary is refused outright, which is
    /// the honest failure rather than a silent wrong answer.
    /// </summary>
    internal static class ZstdDecompressor
    {
        /******* IMPORTANT CONSTANTS *********************************************/

        private const uint ZSTD_MAGIC_NUMBER = 0xFD2FB528U;
        private const int ZSTD_BLOCK_SIZE_MAX = 128 * 1024;
        private const int MAX_LITERALS_SIZE = ZSTD_BLOCK_SIZE_MAX;

        private const int HUF_MAX_BITS = 16;
        private const int HUF_MAX_SYMBS = 256;

        private const int FSE_MAX_ACCURACY_LOG = 15;
        private const int FSE_MAX_SYMBS = 256;

        /// <summary>
        /// The C calls <c>exit(1)</c> on any fault, which a library plainly cannot do. Every ERROR,
        /// CORRUPTION, INP_SIZE, OUT_SIZE and IMPOSSIBLE macro becomes one of these.
        /// </summary>
        private static Exception Error(string what)
        {
            return new InvalidDataException("zstd: " + what);
        }

        /******* PUBLIC ENTRY POINTS *********************************************/

        /// <summary>
        /// The decompressed length recorded in the frame header, or -1 when the frame does not state it.
        ///
        /// Mirrors <c>ZSTD_get_decompressed_size</c>. Reads only the header, so it is cheap enough to call
        /// before allocating the output buffer, which is exactly what it is for.
        /// </summary>
        internal static long GetDecompressedSize(byte[] src, int srcLen)
        {
            IStream inStream = IO_make_istream(src, 0, srcLen);

            uint magic = (uint) IO_read_bits(inStream, 32);

            if (magic != ZSTD_MAGIC_NUMBER)
                throw Error("frame magic number did not match");

            FrameHeader header = new FrameHeader();
            parse_frame_header(header, inStream);

            if (header.FrameContentSize == 0 && !header.SingleSegmentFlag)
                return -1;

            return (long) header.FrameContentSize;
        }

        /// <summary>
        /// Decompresses one frame into <paramref name="dst"/>, returning how many bytes were written.
        ///
        /// Mirrors <c>ZSTD_decompress</c>. Like the original this handles a single frame, which is what every
        /// save written by the compression mod contains.
        /// </summary>
        internal static int Decompress(byte[] dst, int dstLen, byte[] src, int srcLen)
        {
            return Decompress(dst, dstLen, src, srcLen, false);
        }

        /// <summary>
        /// As above, optionally keeping whatever was produced when the output buffer fills.
        ///
        /// <b>For reading a save's header without decompressing the save.</b> zstd output is written strictly
        /// forwards, and a match copy can only reference bytes already written, so the buffer is final up to the
        /// point the decoder stopped. Handing back a prefix is therefore returning real output rather than
        /// salvaging a half-finished one.
        ///
        /// <b>Only the entry point knows about this.</b> The frame, block, sequence and Huffman code below is a
        /// verified port and is not touched: the flag turns one exception it already raises into a normal return,
        /// and nothing about how bytes are produced changes.
        /// </summary>
        /// <param name="allowTruncation">
        /// True to return the bytes produced so far when the buffer is exhausted, instead of failing. False keeps
        /// the original behaviour, where too small a buffer is an error the caller retries with a larger one.
        /// </param>
        internal static int Decompress(byte[] dst, int dstLen, byte[] src, int srcLen, bool allowTruncation)
        {
            if (dst == null || src == null)
                throw new ArgumentNullException(dst == null ? "dst" : "src");

            IStream inStream = IO_make_istream(src, 0, srcLen);
            OStream outStream = IO_make_ostream(dst, 0, dstLen);

            try
            {
                decode_frame(outStream, inStream);
            }
            catch (InvalidDataException) when (allowTruncation && outStream.Pos > 0)
            {
                // Ran out of room, which is the expected way a bounded read ends. Anything else that throws
                // with nothing written is a real fault and still propagates.
                return outStream.Pos;
            }

            return outStream.Pos;
        }
        /******* TYPES ***********************************************************/

        /// <summary>
        /// The C wraps a pointer and a length; this wraps the array, a position into it and a length, which
        /// is the same thing with bounds the runtime can check.
        ///
        /// A class rather than a struct because every C use is through a pointer to a mutable stream, and a
        /// struct would need <c>ref</c> at every one of those call sites for the same behaviour.
        /// </summary>
        private sealed class IStream
        {
            public byte[] Buf;
            public int Pos;
            public int Len;
            public int BitOffset;
        }

        private sealed class OStream
        {
            public byte[] Buf;
            public int Pos;
            public int Len;
        }

        private sealed class HufDtable
        {
            public byte[] Symbols;
            public byte[] NumBits;
            public int MaxBits;
        }

        private sealed class FseDtable
        {
            public byte[] Symbols;
            public byte[] NumBits;
            public ushort[] NewStateBase;
            public int AccuracyLog;
        }

        private sealed class FrameHeader
        {
            public ulong WindowSize;
            public ulong FrameContentSize;
            public uint DictionaryId;
            public bool ContentChecksumFlag;
            public bool SingleSegmentFlag;
        }

        private sealed class FrameContext
        {
            public readonly FrameHeader Header = new FrameHeader();
            public ulong CurrentTotalOutput;

            public readonly HufDtable LiteralsDtable = new HufDtable();
            public readonly FseDtable LlDtable = new FseDtable();
            public readonly FseDtable MlDtable = new FseDtable();
            public readonly FseDtable OfDtable = new FseDtable();

            public readonly ulong[] PreviousOffsets = new ulong[3];
        }

        private struct SequenceCommand
        {
            public uint LiteralLength;
            public uint MatchLength;
            public uint Offset;
        }

        private sealed class SequenceStates
        {
            public FseDtable LlTable;
            public FseDtable OfTable;
            public FseDtable MlTable;

            public ushort LlState;
            public ushort OfState;
            public ushort MlState;
        }

        private const int seq_literal_length = 0;
        private const int seq_offset = 1;
        private const int seq_match_length = 2;

        private const int seq_predefined = 0;
        private const int seq_rle = 1;
        private const int seq_fse = 2;
        private const int seq_repeat = 3;

        /******* FRAME DECODING **************************************************/

        private static void decode_frame(OStream outStream, IStream inStream)
        {
            uint magic = (uint) IO_read_bits(inStream, 32);

            if (magic != ZSTD_MAGIC_NUMBER)
                throw Error("tried to decode non-ZSTD frame");

            decode_data_frame(outStream, inStream);
        }

        private static void decode_data_frame(OStream outStream, IStream inStream)
        {
            FrameContext ctx = new FrameContext();

            init_frame_context(ctx, inStream);

            if (ctx.Header.FrameContentSize != 0 && ctx.Header.FrameContentSize > (ulong) outStream.Len)
                throw Error("output buffer too small for output");

            decompress_data(ctx, outStream, inStream);
        }

        private static void init_frame_context(FrameContext context, IStream inStream)
        {
            parse_frame_header(context.Header, inStream);

            context.PreviousOffsets[0] = 1;
            context.PreviousOffsets[1] = 4;
            context.PreviousOffsets[2] = 8;

            // frame_context_apply_dict. A frame naming a dictionary cannot be decoded without it, and this
            // port carries none, so say so rather than producing plausible rubbish.
            if (context.Header.DictionaryId != 0)
                throw Error("this save needs a zstd dictionary, which is not supported");
        }

        private static void parse_frame_header(FrameHeader header, IStream inStream)
        {
            byte descriptor = (byte) IO_read_bits(inStream, 8);

            int frameContentSizeFlag = descriptor >> 6;
            bool singleSegmentFlag = ((descriptor >> 5) & 1) != 0;
            int reservedBit = (descriptor >> 3) & 1;
            bool contentChecksumFlag = ((descriptor >> 2) & 1) != 0;
            int dictionaryIdFlag = descriptor & 3;

            if (reservedBit != 0)
                throw Error("corruption detected while decompressing");

            header.SingleSegmentFlag = singleSegmentFlag;
            header.ContentChecksumFlag = contentChecksumFlag;

            if (!singleSegmentFlag)
            {
                byte windowDescriptor = (byte) IO_read_bits(inStream, 8);
                int exponent = windowDescriptor >> 3;
                int mantissa = windowDescriptor & 7;

                ulong windowBase = 1UL << (10 + exponent);
                ulong windowAdd = (windowBase / 8) * (ulong) mantissa;

                header.WindowSize = windowBase + windowAdd;
            }

            if (dictionaryIdFlag != 0)
            {
                int[] bytesArray = { 0, 1, 2, 4 };
                int bytes = bytesArray[dictionaryIdFlag];

                header.DictionaryId = (uint) IO_read_bits(inStream, bytes * 8);
            }
            else
            {
                header.DictionaryId = 0;
            }

            if (singleSegmentFlag || frameContentSizeFlag != 0)
            {
                int[] bytesArray = { 1, 2, 4, 8 };
                int bytes = bytesArray[frameContentSizeFlag];

                header.FrameContentSize = IO_read_bits(inStream, bytes * 8);

                if (bytes == 2)
                    header.FrameContentSize += 256;
            }
            else
            {
                header.FrameContentSize = 0;
            }

            if (singleSegmentFlag)
                header.WindowSize = header.FrameContentSize;
        }

        private static void decompress_data(FrameContext ctx, OStream outStream, IStream inStream)
        {
            int lastBlock;

            do
            {
                lastBlock = (int) IO_read_bits(inStream, 1);
                int blockType = (int) IO_read_bits(inStream, 2);
                int blockLen = (int) IO_read_bits(inStream, 21);

                switch (blockType)
                {
                    case 0:
                    {
                        int readPtr = IO_get_read_ptr(inStream, blockLen);
                        int writePtr = IO_get_write_ptr(outStream, blockLen);

                        Array.Copy(inStream.Buf, readPtr, outStream.Buf, writePtr, blockLen);

                        ctx.CurrentTotalOutput += (ulong) blockLen;
                        break;
                    }

                    case 1:
                    {
                        int readPtr = IO_get_read_ptr(inStream, 1);
                        int writePtr = IO_get_write_ptr(outStream, blockLen);

                        byte value = inStream.Buf[readPtr];

                        for (int i = 0; i < blockLen; i++)
                            outStream.Buf[writePtr + i] = value;

                        ctx.CurrentTotalOutput += (ulong) blockLen;
                        break;
                    }

                    case 2:
                    {
                        IStream blockStream = IO_make_sub_istream(inStream, blockLen);
                        decompress_block(ctx, outStream, blockStream);
                        break;
                    }

                    default:
                        throw Error("corruption detected while decompressing");
                }
            }
            while (lastBlock == 0);

            // The checksum is not verified, matching the C. The frame's own length and the strict stream
            // accounting below are what catch corruption here.
            if (ctx.Header.ContentChecksumFlag)
                IO_advance_input(inStream, 4);
        }

        /******* BLOCK DECOMPRESSION *********************************************/

        private static void decompress_block(FrameContext ctx, OStream outStream, IStream inStream)
        {
            byte[] literals;
            int literalsSize = decode_literals(ctx, inStream, out literals);

            SequenceCommand[] sequences;
            int numSequences = decode_sequences(ctx, inStream, out sequences);

            execute_sequences(ctx, outStream, literals, literalsSize, sequences, numSequences);
        }

        /******* LITERALS DECODING ***********************************************/

        private static int decode_literals(FrameContext ctx, IStream inStream, out byte[] literals)
        {
            int blockType = (int) IO_read_bits(inStream, 2);
            int sizeFormat = (int) IO_read_bits(inStream, 2);

            return blockType <= 1
                ? decode_literals_simple(inStream, out literals, blockType, sizeFormat)
                : decode_literals_compressed(ctx, inStream, out literals, blockType, sizeFormat);
        }

        private static int decode_literals_simple(IStream inStream, out byte[] literals, int blockType,
            int sizeFormat)
        {
            int size;

            switch (sizeFormat)
            {
                case 0:
                case 2:
                    IO_rewind_bits(inStream, 1);
                    size = (int) IO_read_bits(inStream, 5);
                    break;

                case 1:
                    size = (int) IO_read_bits(inStream, 12);
                    break;

                case 3:
                    size = (int) IO_read_bits(inStream, 20);
                    break;

                default:
                    throw Error("an impossibility has occurred");
            }

            if (size > MAX_LITERALS_SIZE)
                throw Error("corruption detected while decompressing");

            literals = new byte[size];

            switch (blockType)
            {
                case 0:
                {
                    int readPtr = IO_get_read_ptr(inStream, size);
                    Array.Copy(inStream.Buf, readPtr, literals, 0, size);
                    break;
                }

                case 1:
                {
                    int readPtr = IO_get_read_ptr(inStream, 1);
                    byte value = inStream.Buf[readPtr];

                    for (int i = 0; i < size; i++)
                        literals[i] = value;

                    break;
                }

                default:
                    throw Error("an impossibility has occurred");
            }

            return size;
        }

        private static int decode_literals_compressed(FrameContext ctx, IStream inStream, out byte[] literals,
            int blockType, int sizeFormat)
        {
            int regeneratedSize;
            int compressedSize;
            int numStreams = 4;

            switch (sizeFormat)
            {
                case 0:
                    numStreams = 1;
                    goto case 1;

                case 1:
                    regeneratedSize = (int) IO_read_bits(inStream, 10);
                    compressedSize = (int) IO_read_bits(inStream, 10);
                    break;

                case 2:
                    regeneratedSize = (int) IO_read_bits(inStream, 14);
                    compressedSize = (int) IO_read_bits(inStream, 14);
                    break;

                case 3:
                    regeneratedSize = (int) IO_read_bits(inStream, 18);
                    compressedSize = (int) IO_read_bits(inStream, 18);
                    break;

                default:
                    throw Error("an impossibility has occurred");
            }

            if (regeneratedSize > MAX_LITERALS_SIZE)
                throw Error("corruption detected while decompressing");

            literals = new byte[regeneratedSize];

            OStream litStream = IO_make_ostream(literals, 0, regeneratedSize);
            IStream hufStream = IO_make_sub_istream(inStream, compressedSize);

            if (blockType == 2)
            {
                HUF_free_dtable(ctx.LiteralsDtable);
                decode_huf_table(ctx.LiteralsDtable, hufStream);
            }
            else if (ctx.LiteralsDtable.Symbols == null)
            {
                throw Error("corruption detected while decompressing");
            }

            int symbolsDecoded = numStreams == 1
                ? HUF_decompress_1stream(ctx.LiteralsDtable, litStream, hufStream)
                : HUF_decompress_4stream(ctx.LiteralsDtable, litStream, hufStream);

            if (symbolsDecoded != regeneratedSize)
                throw Error("corruption detected while decompressing");

            return regeneratedSize;
        }

        private static void decode_huf_table(HufDtable dtable, IStream inStream)
        {
            byte header = (byte) IO_read_bits(inStream, 8);

            byte[] weights = new byte[HUF_MAX_SYMBS];
            int numSymbs;

            if (header >= 128)
            {
                numSymbs = header - 127;
                int bytes = (numSymbs + 1) / 2;

                int weightSrc = IO_get_read_ptr(inStream, bytes);

                for (int i = 0; i < numSymbs; i++)
                {
                    weights[i] = i % 2 == 0
                        ? (byte) (inStream.Buf[weightSrc + i / 2] >> 4)
                        : (byte) (inStream.Buf[weightSrc + i / 2] & 0xf);
                }
            }
            else
            {
                IStream fseStream = IO_make_sub_istream(inStream, header);
                OStream weightStream = IO_make_ostream(weights, 0, HUF_MAX_SYMBS);

                numSymbs = fse_decode_hufweights(weightStream, fseStream);
            }

            HUF_init_dtable_usingweights(dtable, weights, numSymbs);
        }

        private static int fse_decode_hufweights(OStream weights, IStream inStream)
        {
            const int MAX_ACCURACY_LOG = 7;

            FseDtable dtable = new FseDtable();

            FSE_decode_header(dtable, inStream, MAX_ACCURACY_LOG);

            return FSE_decompress_interleaved2(dtable, weights, inStream);
        }

        /******* SEQUENCE DECODING ***********************************************/

        private static readonly short[] SEQ_LITERAL_LENGTH_DEFAULT_DIST =
        {
            4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 3, 2, 1, 1, 1, 1, 1, -1, -1, -1, -1
        };

        private static readonly short[] SEQ_OFFSET_DEFAULT_DIST =
        {
            1, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1
        };

        private static readonly short[] SEQ_MATCH_LENGTH_DEFAULT_DIST =
        {
            1, 4, 3, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1, -1, -1
        };

        private static readonly uint[] SEQ_LITERAL_LENGTH_BASELINES =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11,
            12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 40,
            48, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536
        };

        private static readonly byte[] SEQ_LITERAL_LENGTH_EXTRA_BITS =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1,
            1, 1, 2, 2, 3, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16
        };

        private static readonly uint[] SEQ_MATCH_LENGTH_BASELINES =
        {
            3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
            31, 32, 33, 34, 35, 37, 39, 41, 43, 47, 51, 59, 67, 83,
            99, 131, 259, 515, 1027, 2051, 4099, 8195, 16387, 32771, 65539
        };

        private static readonly byte[] SEQ_MATCH_LENGTH_EXTRA_BITS =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1,
            2, 2, 3, 3, 4, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16
        };

        /// <summary>Offsets have no table maximum, which the C spells as <c>(u8)-1</c>.</summary>
        private static readonly byte[] SEQ_MAX_CODES = { 35, 255, 52 };

        private static int decode_sequences(FrameContext ctx, IStream inStream, out SequenceCommand[] sequences)
        {
            int numSequences;

            byte header = (byte) IO_read_bits(inStream, 8);

            if (header < 128)
                numSequences = header;
            else if (header < 255)
                numSequences = ((header - 128) << 8) + (int) IO_read_bits(inStream, 8);
            else
                numSequences = (int) IO_read_bits(inStream, 16) + 0x7F00;

            if (numSequences == 0)
            {
                sequences = null;

                return 0;
            }

            sequences = new SequenceCommand[numSequences];

            decompress_sequences(ctx, inStream, sequences, numSequences);

            return numSequences;
        }

        private static void decompress_sequences(FrameContext ctx, IStream inStream,
            SequenceCommand[] sequences, int numSequences)
        {
            byte compressionModes = (byte) IO_read_bits(inStream, 8);

            if ((compressionModes & 3) != 0)
                throw Error("corruption detected while decompressing");

            decode_seq_table(ctx.LlDtable, inStream, seq_literal_length, (compressionModes >> 6) & 3);
            decode_seq_table(ctx.OfDtable, inStream, seq_offset, (compressionModes >> 4) & 3);
            decode_seq_table(ctx.MlDtable, inStream, seq_match_length, (compressionModes >> 2) & 3);

            SequenceStates states = new SequenceStates
            {
                LlTable = ctx.LlDtable,
                OfTable = ctx.OfDtable,
                MlTable = ctx.MlDtable
            };

            int len = IO_istream_len(inStream);
            int src = IO_get_read_ptr(inStream, len);
            byte[] buf = inStream.Buf;

            int padding = 8 - highest_set_bit(buf[src + len - 1]);
            long bitOffset = (long) len * 8 - padding;

            FSE_init_state(states.LlTable, ref states.LlState, buf, src, ref bitOffset);
            FSE_init_state(states.OfTable, ref states.OfState, buf, src, ref bitOffset);
            FSE_init_state(states.MlTable, ref states.MlState, buf, src, ref bitOffset);

            for (int i = 0; i < numSequences; i++)
                sequences[i] = decode_sequence(states, buf, src, ref bitOffset, i == numSequences - 1);

            if (bitOffset != 0)
                throw Error("corruption detected while decompressing");
        }

        private static SequenceCommand decode_sequence(SequenceStates states, byte[] buf, int src,
            ref long offset, bool lastSequence)
        {
            byte ofCode = FSE_peek_symbol(states.OfTable, states.OfState);
            byte llCode = FSE_peek_symbol(states.LlTable, states.LlState);
            byte mlCode = FSE_peek_symbol(states.MlTable, states.MlState);

            if (llCode > SEQ_MAX_CODES[seq_literal_length] || mlCode > SEQ_MAX_CODES[seq_match_length])
                throw Error("corruption detected while decompressing");

            SequenceCommand seq;

            seq.Offset = (uint) ((1U << ofCode) + STREAM_read_bits(buf, src, ofCode, ref offset));

            seq.MatchLength = (uint) (SEQ_MATCH_LENGTH_BASELINES[mlCode]
                                      + STREAM_read_bits(buf, src, SEQ_MATCH_LENGTH_EXTRA_BITS[mlCode],
                                          ref offset));

            seq.LiteralLength = (uint) (SEQ_LITERAL_LENGTH_BASELINES[llCode]
                                        + STREAM_read_bits(buf, src, SEQ_LITERAL_LENGTH_EXTRA_BITS[llCode],
                                            ref offset));

            if (!lastSequence)
            {
                FSE_update_state(states.LlTable, ref states.LlState, buf, src, ref offset);
                FSE_update_state(states.MlTable, ref states.MlState, buf, src, ref offset);
                FSE_update_state(states.OfTable, ref states.OfState, buf, src, ref offset);
            }

            return seq;
        }

        private static void decode_seq_table(FseDtable table, IStream inStream, int type, int mode)
        {
            short[][] defaultDistributions =
            {
                SEQ_LITERAL_LENGTH_DEFAULT_DIST,
                SEQ_OFFSET_DEFAULT_DIST,
                SEQ_MATCH_LENGTH_DEFAULT_DIST
            };

            int[] defaultDistributionLengths = { 36, 29, 53 };
            int[] defaultDistributionAccuracies = { 6, 5, 6 };
            int[] maxAccuracies = { 9, 8, 9 };

            if (mode != seq_repeat)
                FSE_free_dtable(table);

            switch (mode)
            {
                case seq_predefined:
                    FSE_init_dtable(table, defaultDistributions[type], defaultDistributionLengths[type],
                        defaultDistributionAccuracies[type]);
                    break;

                case seq_rle:
                {
                    int ptr = IO_get_read_ptr(inStream, 1);
                    FSE_init_dtable_rle(table, inStream.Buf[ptr]);
                    break;
                }

                case seq_fse:
                    FSE_decode_header(table, inStream, maxAccuracies[type]);
                    break;

                case seq_repeat:
                    if (table.Symbols == null)
                        throw Error("corruption detected while decompressing");

                    break;

                default:
                    throw Error("an impossibility has occurred");
            }
        }

        /******* SEQUENCE EXECUTION **********************************************/

        private static void execute_sequences(FrameContext ctx, OStream outStream, byte[] literals,
            int literalsLen, SequenceCommand[] sequences, int numSequences)
        {
            IStream litStream = IO_make_istream(literals, 0, literalsLen);

            ulong[] offsetHist = ctx.PreviousOffsets;
            ulong totalOutput = ctx.CurrentTotalOutput;

            for (int i = 0; i < numSequences; i++)
            {
                SequenceCommand seq = sequences[i];

                uint literalsSize = copy_literals(seq.LiteralLength, litStream, outStream);
                totalOutput += literalsSize;

                ulong offset = compute_offset(seq, offsetHist);
                ulong matchLength = seq.MatchLength;

                execute_match_copy(ctx, offset, matchLength, totalOutput, outStream);

                totalOutput += matchLength;
            }

            int leftover = IO_istream_len(litStream);
            copy_literals((uint) leftover, litStream, outStream);
            totalOutput += (ulong) leftover;

            ctx.CurrentTotalOutput = totalOutput;
        }

        private static uint copy_literals(uint literalLength, IStream litStream, OStream outStream)
        {
            if (literalLength > (uint) IO_istream_len(litStream))
                throw Error("corruption detected while decompressing");

            int writePtr = IO_get_write_ptr(outStream, (int) literalLength);
            int readPtr = IO_get_read_ptr(litStream, (int) literalLength);

            Array.Copy(litStream.Buf, readPtr, outStream.Buf, writePtr, (int) literalLength);

            return literalLength;
        }

        private static ulong compute_offset(SequenceCommand seq, ulong[] offsetHist)
        {
            ulong offset;

            if (seq.Offset <= 3)
            {
                uint idx = seq.Offset - 1;

                if (seq.LiteralLength == 0)
                    idx++;

                if (idx == 0)
                {
                    offset = offsetHist[0];
                }
                else
                {
                    offset = idx < 3 ? offsetHist[idx] : offsetHist[0] - 1;

                    if (idx > 1)
                        offsetHist[2] = offsetHist[1];

                    offsetHist[1] = offsetHist[0];
                    offsetHist[0] = offset;
                }
            }
            else
            {
                offset = seq.Offset - 3;

                offsetHist[2] = offsetHist[1];
                offsetHist[1] = offsetHist[0];
                offsetHist[0] = offset;
            }

            return offset;
        }

        private static void execute_match_copy(FrameContext ctx, ulong offset, ulong matchLength,
            ulong totalOutput, OStream outStream)
        {
            int writePtr = IO_get_write_ptr(outStream, (int) matchLength);

            if (totalOutput <= ctx.Header.WindowSize)
            {
                // The dictionary branch of the C lives here. With no dictionary the only way offset can
                // exceed what has been produced is corruption, so the two cases collapse into one test.
                if (offset > totalOutput)
                    throw Error("corruption detected while decompressing");
            }
            else if (offset > ctx.Header.WindowSize)
            {
                throw Error("corruption detected while decompressing");
            }

            // Byte by byte, because the match may be longer than the offset and must read bytes it is in the
            // middle of writing. An Array.Copy here would be wrong for exactly the overlapping case that
            // makes this format work.
            byte[] buf = outStream.Buf;
            int back = (int) offset;

            for (ulong j = 0; j < matchLength; j++)
            {
                buf[writePtr] = buf[writePtr - back];
                writePtr++;
            }
        }

        /******* IO STREAM OPERATIONS ********************************************/

        private static ulong IO_read_bits(IStream inStream, int numBits)
        {
            if (numBits > 64 || numBits <= 0)
                throw Error("attempt to read an invalid number of bits");

            int bytes = (numBits + inStream.BitOffset + 7) / 8;
            int fullBytes = (numBits + inStream.BitOffset) / 8;

            if (bytes > inStream.Len)
                throw Error("input buffer smaller than it should be or input is corrupted");

            ulong result = read_bits_LE(inStream.Buf, inStream.Pos, numBits, inStream.BitOffset);

            inStream.BitOffset = (numBits + inStream.BitOffset) % 8;
            inStream.Pos += fullBytes;
            inStream.Len -= fullBytes;

            return result;
        }

        private static void IO_rewind_bits(IStream inStream, int numBits)
        {
            if (numBits < 0)
                throw Error("attempting to rewind stream by a negative number of bits");

            int newOffset = inStream.BitOffset - numBits;
            long bytes = -(newOffset - 7) / 8;

            inStream.Pos -= (int) bytes;
            inStream.Len += (int) bytes;

            // C's % keeps the sign of the dividend, so the double modulo in the original is load bearing.
            // C# behaves the same way, so it is kept rather than "simplified".
            inStream.BitOffset = ((newOffset % 8) + 8) % 8;
        }

        private static void IO_align_stream(IStream inStream)
        {
            if (inStream.BitOffset == 0)
                return;

            if (inStream.Len == 0)
                throw Error("input buffer smaller than it should be or input is corrupted");

            inStream.Pos++;
            inStream.Len--;
            inStream.BitOffset = 0;
        }

        private static void IO_write_byte(OStream outStream, byte symb)
        {
            if (outStream.Len == 0)
                throw Error("output buffer too small for output");

            outStream.Buf[outStream.Pos] = symb;
            outStream.Pos++;
            outStream.Len--;
        }

        private static int IO_istream_len(IStream inStream)
        {
            return inStream.Len;
        }

        /// <summary>Returns the index the caller may read <paramref name="len"/> bytes from.</summary>
        private static int IO_get_read_ptr(IStream inStream, int len)
        {
            if (len > inStream.Len)
                throw Error("input buffer smaller than it should be or input is corrupted");

            if (inStream.BitOffset != 0)
                throw Error("attempting to operate on a non-byte aligned stream");

            int ptr = inStream.Pos;

            inStream.Pos += len;
            inStream.Len -= len;

            return ptr;
        }

        private static int IO_get_write_ptr(OStream outStream, int len)
        {
            if (len > outStream.Len)
                throw Error("output buffer too small for output");

            int ptr = outStream.Pos;

            outStream.Pos += len;
            outStream.Len -= len;

            return ptr;
        }

        private static void IO_advance_input(IStream inStream, int len)
        {
            if (len > inStream.Len)
                throw Error("input buffer smaller than it should be or input is corrupted");

            if (inStream.BitOffset != 0)
                throw Error("attempting to operate on a non-byte aligned stream");

            inStream.Pos += len;
            inStream.Len -= len;
        }

        private static OStream IO_make_ostream(byte[] buf, int pos, int len)
        {
            return new OStream { Buf = buf, Pos = pos, Len = len };
        }

        private static IStream IO_make_istream(byte[] buf, int pos, int len)
        {
            return new IStream { Buf = buf, Pos = pos, Len = len, BitOffset = 0 };
        }

        private static IStream IO_make_sub_istream(IStream inStream, int len)
        {
            int ptr = IO_get_read_ptr(inStream, len);

            return IO_make_istream(inStream.Buf, ptr, len);
        }

        /******* BITSTREAM OPERATIONS ********************************************/

        private static ulong read_bits_LE(byte[] buf, int src, int numBits, long offset)
        {
            if (numBits > 64)
                throw Error("attempt to read an invalid number of bits");

            src += (int) (offset / 8);

            int bitOffset = (int) (offset % 8);
            ulong res = 0;

            int shift = 0;
            int left = numBits;

            while (left > 0)
            {
                ulong mask = left >= 8 ? 0xff : (1UL << left) - 1;

                res += (((ulong) buf[src++] >> bitOffset) & mask) << shift;

                shift += 8 - bitOffset;
                left -= 8 - bitOffset;
                bitOffset = 0;
            }

            return res;
        }

        private static ulong STREAM_read_bits(byte[] buf, int src, int bits, ref long offset)
        {
            offset = offset - bits;

            long actualOff = offset;
            int actualBits = bits;

            if (offset < 0)
            {
                actualBits += (int) offset;
                actualOff = 0;
            }

            // The C reads zero bits happily; guarding keeps read_bits_LE's contract intact.
            ulong res = actualBits <= 0 ? 0 : read_bits_LE(buf, src, actualBits, actualOff);

            if (offset < 0)
                res = -offset >= 64 ? 0 : res << (int) -offset;

            return res;
        }

        /******* BIT COUNTING OPERATIONS *****************************************/

        private static int highest_set_bit(ulong num)
        {
            for (int i = 63; i >= 0; i--)
            {
                if (1UL << i <= num)
                    return i;
            }

            return -1;
        }

        /******* HUFFMAN PRIMITIVES **********************************************/

        private static byte HUF_decode_symbol(HufDtable dtable, ref ushort state, byte[] buf, int src,
            ref long offset)
        {
            byte symb = dtable.Symbols[state];
            byte bits = dtable.NumBits[state];
            ushort rest = (ushort) STREAM_read_bits(buf, src, bits, ref offset);

            state = (ushort) (((state << bits) + rest) & ((1 << dtable.MaxBits) - 1));

            return symb;
        }

        private static void HUF_init_state(HufDtable dtable, ref ushort state, byte[] buf, int src,
            ref long offset)
        {
            state = (ushort) STREAM_read_bits(buf, src, dtable.MaxBits, ref offset);
        }

        private static int HUF_decompress_1stream(HufDtable dtable, OStream outStream, IStream inStream)
        {
            int len = IO_istream_len(inStream);

            if (len == 0)
                throw Error("input buffer smaller than it should be or input is corrupted");

            int src = IO_get_read_ptr(inStream, len);
            byte[] buf = inStream.Buf;

            int padding = 8 - highest_set_bit(buf[src + len - 1]);
            long bitOffset = (long) len * 8 - padding;

            ushort state = 0;
            HUF_init_state(dtable, ref state, buf, src, ref bitOffset);

            int symbolsWritten = 0;

            while (bitOffset > -dtable.MaxBits)
            {
                IO_write_byte(outStream, HUF_decode_symbol(dtable, ref state, buf, src, ref bitOffset));
                symbolsWritten++;
            }

            if (bitOffset != -dtable.MaxBits)
                throw Error("corruption detected while decompressing");

            return symbolsWritten;
        }

        private static int HUF_decompress_4stream(HufDtable dtable, OStream outStream, IStream inStream)
        {
            int csize1 = (int) IO_read_bits(inStream, 16);
            int csize2 = (int) IO_read_bits(inStream, 16);
            int csize3 = (int) IO_read_bits(inStream, 16);

            IStream in1 = IO_make_sub_istream(inStream, csize1);
            IStream in2 = IO_make_sub_istream(inStream, csize2);
            IStream in3 = IO_make_sub_istream(inStream, csize3);
            IStream in4 = IO_make_sub_istream(inStream, IO_istream_len(inStream));

            int totalOutput = 0;

            totalOutput += HUF_decompress_1stream(dtable, outStream, in1);
            totalOutput += HUF_decompress_1stream(dtable, outStream, in2);
            totalOutput += HUF_decompress_1stream(dtable, outStream, in3);
            totalOutput += HUF_decompress_1stream(dtable, outStream, in4);

            return totalOutput;
        }

        private static void HUF_init_dtable(HufDtable table, byte[] bits, int numSymbs)
        {
            HUF_free_dtable(table);

            if (numSymbs > HUF_MAX_SYMBS)
                throw Error("too many symbols for Huffman");

            int maxBits = 0;
            ushort[] rankCount = new ushort[HUF_MAX_BITS + 1];

            for (int i = 0; i < numSymbs; i++)
            {
                if (bits[i] > HUF_MAX_BITS)
                    throw Error("Huffman table depth too large");

                if (bits[i] > maxBits)
                    maxBits = bits[i];

                rankCount[bits[i]]++;
            }

            int tableSize = 1 << maxBits;

            table.MaxBits = maxBits;
            table.Symbols = new byte[tableSize];
            table.NumBits = new byte[tableSize];

            uint[] rankIdx = new uint[HUF_MAX_BITS + 1];
            rankIdx[maxBits] = 0;

            for (int i = maxBits; i >= 1; i--)
            {
                rankIdx[i - 1] = rankIdx[i] + (uint) (rankCount[i] * (1 << (maxBits - i)));

                for (uint j = rankIdx[i]; j < rankIdx[i - 1]; j++)
                    table.NumBits[j] = (byte) i;
            }

            if (rankIdx[0] != tableSize)
                throw Error("corruption detected while decompressing");

            for (int i = 0; i < numSymbs; i++)
            {
                if (bits[i] == 0)
                    continue;

                ushort code = (ushort) rankIdx[bits[i]];
                ushort len = (ushort) (1 << (maxBits - bits[i]));

                for (int j = 0; j < len; j++)
                    table.Symbols[code + j] = (byte) i;

                rankIdx[bits[i]] += len;
            }
        }

        private static void HUF_init_dtable_usingweights(HufDtable table, byte[] weights, int numSymbs)
        {
            if (numSymbs + 1 > HUF_MAX_SYMBS)
                throw Error("too many symbols for Huffman");

            byte[] bits = new byte[HUF_MAX_SYMBS];

            ulong weightSum = 0;

            for (int i = 0; i < numSymbs; i++)
            {
                if (weights[i] > HUF_MAX_BITS)
                    throw Error("corruption detected while decompressing");

                weightSum += weights[i] > 0 ? 1UL << (weights[i] - 1) : 0;
            }

            int maxBits = highest_set_bit(weightSum) + 1;
            ulong leftOver = (1UL << maxBits) - weightSum;

            if ((leftOver & (leftOver - 1)) != 0)
                throw Error("corruption detected while decompressing");

            int lastWeight = highest_set_bit(leftOver) + 1;

            for (int i = 0; i < numSymbs; i++)
                bits[i] = weights[i] > 0 ? (byte) (maxBits + 1 - weights[i]) : (byte) 0;

            bits[numSymbs] = (byte) (maxBits + 1 - lastWeight);

            HUF_init_dtable(table, bits, numSymbs + 1);
        }

        private static void HUF_free_dtable(HufDtable dtable)
        {
            dtable.Symbols = null;
            dtable.NumBits = null;
            dtable.MaxBits = 0;
        }

        /******* FSE PRIMITIVES **************************************************/

        private static byte FSE_peek_symbol(FseDtable dtable, ushort state)
        {
            return dtable.Symbols[state];
        }

        private static void FSE_update_state(FseDtable dtable, ref ushort state, byte[] buf, int src,
            ref long offset)
        {
            byte bits = dtable.NumBits[state];
            ushort rest = (ushort) STREAM_read_bits(buf, src, bits, ref offset);

            state = (ushort) (dtable.NewStateBase[state] + rest);
        }

        private static byte FSE_decode_symbol(FseDtable dtable, ref ushort state, byte[] buf, int src,
            ref long offset)
        {
            byte symb = FSE_peek_symbol(dtable, state);

            FSE_update_state(dtable, ref state, buf, src, ref offset);

            return symb;
        }

        private static void FSE_init_state(FseDtable dtable, ref ushort state, byte[] buf, int src,
            ref long offset)
        {
            state = (ushort) STREAM_read_bits(buf, src, dtable.AccuracyLog, ref offset);
        }

        private static int FSE_decompress_interleaved2(FseDtable dtable, OStream outStream, IStream inStream)
        {
            int len = IO_istream_len(inStream);

            if (len == 0)
                throw Error("input buffer smaller than it should be or input is corrupted");

            int src = IO_get_read_ptr(inStream, len);
            byte[] buf = inStream.Buf;

            int padding = 8 - highest_set_bit(buf[src + len - 1]);
            long offset = (long) len * 8 - padding;

            ushort state1 = 0;
            ushort state2 = 0;

            FSE_init_state(dtable, ref state1, buf, src, ref offset);
            FSE_init_state(dtable, ref state2, buf, src, ref offset);

            int symbolsWritten = 0;

            while (true)
            {
                IO_write_byte(outStream, FSE_decode_symbol(dtable, ref state1, buf, src, ref offset));
                symbolsWritten++;

                if (offset < 0)
                {
                    IO_write_byte(outStream, FSE_peek_symbol(dtable, state2));
                    symbolsWritten++;
                    break;
                }

                IO_write_byte(outStream, FSE_decode_symbol(dtable, ref state2, buf, src, ref offset));
                symbolsWritten++;

                if (offset < 0)
                {
                    IO_write_byte(outStream, FSE_peek_symbol(dtable, state1));
                    symbolsWritten++;
                    break;
                }
            }

            return symbolsWritten;
        }

        private static void FSE_init_dtable(FseDtable dtable, short[] normFreqs, int numSymbs,
            int accuracyLog)
        {
            if (accuracyLog > FSE_MAX_ACCURACY_LOG)
                throw Error("FSE accuracy too large");

            if (numSymbs > FSE_MAX_SYMBS)
                throw Error("too many symbols for FSE");

            dtable.AccuracyLog = accuracyLog;

            int size = 1 << accuracyLog;

            dtable.Symbols = new byte[size];
            dtable.NumBits = new byte[size];
            dtable.NewStateBase = new ushort[size];

            ushort[] stateDesc = new ushort[FSE_MAX_SYMBS];

            int highThreshold = size;

            for (int s = 0; s < numSymbs; s++)
            {
                if (normFreqs[s] != -1)
                    continue;

                dtable.Symbols[--highThreshold] = (byte) s;
                stateDesc[s] = 1;
            }

            ushort step = (ushort) ((size >> 1) + (size >> 3) + 3);
            ushort mask = (ushort) (size - 1);
            ushort pos = 0;

            for (int s = 0; s < numSymbs; s++)
            {
                if (normFreqs[s] <= 0)
                    continue;

                stateDesc[s] = (ushort) normFreqs[s];

                for (int i = 0; i < normFreqs[s]; i++)
                {
                    dtable.Symbols[pos] = (byte) s;

                    do
                    {
                        pos = (ushort) ((pos + step) & mask);
                    }
                    while (pos >= highThreshold);
                }
            }

            if (pos != 0)
                throw Error("corruption detected while decompressing");

            for (int i = 0; i < size; i++)
            {
                byte symbol = dtable.Symbols[i];
                ushort nextStateDesc = stateDesc[symbol]++;

                dtable.NumBits[i] = (byte) (accuracyLog - highest_set_bit(nextStateDesc));
                dtable.NewStateBase[i] = (ushort) ((nextStateDesc << dtable.NumBits[i]) - size);
            }
        }

        private static void FSE_decode_header(FseDtable dtable, IStream inStream, int maxAccuracyLog)
        {
            if (maxAccuracyLog > FSE_MAX_ACCURACY_LOG)
                throw Error("FSE accuracy too large");

            int accuracyLog = 5 + (int) IO_read_bits(inStream, 4);

            if (accuracyLog > maxAccuracyLog)
                throw Error("FSE accuracy too large");

            int remaining = 1 << accuracyLog;
            short[] frequencies = new short[FSE_MAX_SYMBS];

            int symb = 0;

            while (remaining > 0 && symb < FSE_MAX_SYMBS)
            {
                int bits = highest_set_bit((ulong) (remaining + 1)) + 1;

                ushort val = (ushort) IO_read_bits(inStream, bits);

                ushort lowerMask = (ushort) ((1 << (bits - 1)) - 1);
                ushort threshold = (ushort) ((1 << bits) - 1 - (remaining + 1));

                if ((val & lowerMask) < threshold)
                {
                    IO_rewind_bits(inStream, 1);
                    val = (ushort) (val & lowerMask);
                }
                else if (val > lowerMask)
                {
                    val = (ushort) (val - threshold);
                }

                short proba = (short) (val - 1);

                remaining -= proba < 0 ? -proba : proba;

                frequencies[symb] = proba;
                symb++;

                if (proba != 0)
                    continue;

                int repeat = (int) IO_read_bits(inStream, 2);

                while (true)
                {
                    for (int i = 0; i < repeat && symb < FSE_MAX_SYMBS; i++)
                        frequencies[symb++] = 0;

                    if (repeat != 3)
                        break;

                    repeat = (int) IO_read_bits(inStream, 2);
                }
            }

            IO_align_stream(inStream);

            if (remaining != 0 || symb >= FSE_MAX_SYMBS)
                throw Error("corruption detected while decompressing");

            FSE_init_dtable(dtable, frequencies, symb, accuracyLog);
        }

        private static void FSE_init_dtable_rle(FseDtable dtable, byte symb)
        {
            dtable.Symbols = new byte[1];
            dtable.NumBits = new byte[1];
            dtable.NewStateBase = new ushort[1];

            dtable.Symbols[0] = symb;
            dtable.NumBits[0] = 0;
            dtable.NewStateBase[0] = 0;
            dtable.AccuracyLog = 0;
        }

        private static void FSE_free_dtable(FseDtable dtable)
        {
            dtable.Symbols = null;
            dtable.NumBits = null;
            dtable.NewStateBase = null;
            dtable.AccuracyLog = 0;
        }
    }
}
