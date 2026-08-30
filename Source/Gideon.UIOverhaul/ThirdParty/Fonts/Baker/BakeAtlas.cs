// Bakes a TTF into a PNG glyph atlas plus a metrics table, offline.
//
// WHY THIS EXISTS: Unity has no runtime API to build a Font from a file or a byte array -- the whole surface is
// Font(), Font(name) and CreateDynamicFontFromOSFont, all of which need the font installed on the machine. The
// alternative to this is an AssetBundle, which would have to be built with the Unity editor at RimWorld's exact
// version (2022.3.35f1). Baking an atlas needs no editor and no install, and it suits the mod because the label
// renderer already builds its own meshes -- it only ever wanted glyph rectangles and advances.
//
// OUTPUT, per font:
//   <name>.png   RGB white, alpha carrying glyph coverage. White RGB matters: a shader that multiplies the
//                texture by a tint then yields the tint itself, so the same atlas works whichever shader we
//                draw with. Unity's own dynamic atlases are black-with-alpha, which is what made the first
//                attempt render solid black.
//   <name>.txt   one header line then one line per glyph, plain ASCII, tab separated.
//
// The metrics convention is pixels with y up from the baseline, which is what the mesh builder wants. GDI+ works
// in y-down from the em box top, so every vertical is converted once, here, rather than at draw time.
//
// CODE POINTS, NOT CHARACTERS. This walked a List<char> until 2026-08-23, which was fine for the two Latin faces
// the floor labels use and impossible for the three scripts the research tab masks with: Imperial Aramaic,
// Mende Kikakui and Siddham all live above U+FFFF, where a char cannot reach at all. Everything below is keyed
// by int and rendered through char.ConvertFromUtf32, so a surrogate pair is one glyph rather than two halves of
// nothing.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;

internal static class BakeAtlas
{
    /// <summary>
    /// Em size the glyphs are rasterized at. Resolution, not display size -- but not unrelated to it either.
    ///
    /// <b>Bake near the size it will be drawn at.</b> The atlas is sampled bilinearly with mipmaps off, and
    /// bilinear reads four texels however far it is reducing. Drawing a 64 px master at 18 px means a footprint
    /// of about twelve texels, so two thirds of the coverage is discarded -- and which two thirds depends on
    /// where the glyph lands. On a condensed face that shows up as stems that vary in weight letter to letter
    /// and hairlines that partly disappear.
    ///
    /// So 64 suits a floor label, which is drawn large in world space, and is too big for interface text.
    /// Barlow Condensed is baked at 32 for that reason, on Aaron's word 2026-08-28, which also happens to fit
    /// its 523 glyphs in a 1024 square sheet rather than a 2048 one.
    ///
    /// It is per face rather than global because the two uses genuinely differ. Each metrics file records the
    /// size it was baked at, and every reader scales from that, so sheets of different sizes mix freely.
    /// </summary>
    private static int EmSize = 64;

    /// <summary>Transparent margin around each glyph, so bilinear sampling cannot bleed a neighbor in.</summary>
    private const int Padding = 2;

    /// <summary>
    /// The fewest and most columns the packer will consider. See <c>Pack</c>.
    ///
    /// This was a fixed 16, which suited a face baked at 95 glyphs and wastes most of a texture at 900: sixteen
    /// columns of a 900 glyph face is a tower 57 rows tall, and rounding that up to a power of two throws away
    /// nearly half the pixels. The bounds are wide because the best answer genuinely varies -- a script block of
    /// 22 letters and a Latin face with Cyrillic want very different shapes.
    /// </summary>
    private const int MinColumns = 4;

    private const int MaxColumns = 128;

    /// <summary>
    /// Glyphs baked when no ranges are given: printable ASCII plus the Latin-1 letters.
    ///
    /// Labels are drawn upper cased, so the lowercase range is strictly speaking unused -- it is included anyway
    /// because it costs a few hundred kilobytes of atlas and stops the whole thing being wasted if that decision
    /// ever changes. Anything outside this set makes a label fall back to the game's own font at runtime.
    /// </summary>
    private static IEnumerable<int> DefaultGlyphs()
    {
        for (int c = ' '; c <= '~'; c++)
            yield return c;

        for (int c = 0xC0; c <= 0xFF; c++)
            yield return c;
    }

    /// <summary>
    /// Code points from a comma separated list of hex ranges, such as <c>10840-10855,1E800-1E8C4</c>.
    ///
    /// Ranges rather than a list of code points, because a script block is contiguous and typing out two hundred
    /// numbers is two hundred chances to mistype one. What is actually in the font is checked below: a code point
    /// the face does not cover has no ink and is dropped.
    /// </summary>
    private static IEnumerable<int> Ranges(string text)
    {
        foreach (string part in text.Split(','))
        {
            string piece = part.Trim();

            if (piece.Length == 0)
                continue;

            string[] ends = piece.Split('-');
            int first = int.Parse(ends[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int last = ends.Length > 1
                ? int.Parse(ends[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : first;

            for (int code = first; code <= last; code++)
                yield return code;
        }
    }

    /// <summary>
    /// Every code point the face's character map claims, read out of the TTF directly.
    ///
    /// <b>Why not just probe a wide range.</b> The measuring pass already drops a code point the face does not
    /// cover, so feeding it 0x20-0xFFFF would produce the right answer -- after rasterizing sixty five thousand
    /// glyphs, almost all of them nothing. Reading the cmap turns that into a list of about a thousand before any
    /// drawing happens.
    ///
    /// <b>Ranges, not resolved glyph ids.</b> This returns the code points a subtable's segments span, which is a
    /// superset: a segment can map some of its codes to glyph zero. That is fine and is why it is done this way
    /// -- resolving ids properly means walking idDelta and idRangeOffset arrays and getting the modular
    /// arithmetic exactly right, and the reward would be skipping a handful of glyphs the ink test already
    /// skips. A superset costs a few wasted measurements; a subtle bug in id resolution costs missing letters.
    ///
    /// Formats 4 and 12 only. Those are what every font shipped this decade uses for Unicode, and a face with
    /// neither falls back to the default set with a warning rather than baking nothing.
    /// </summary>
    private static List<int> Coverage(string ttf)
    {
        List<int> codes = new List<int>();

        byte[] data = File.ReadAllBytes(ttf);

        int numTables = U16(data, 4);
        int cmap = -1;

        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + i * 16;

            if (data[record] == 'c' && data[record + 1] == 'm' && data[record + 2] == 'a'
                && data[record + 3] == 'p')
            {
                cmap = (int) U32(data, record + 8);

                break;
            }
        }

        if (cmap < 0)
            return codes;

        int subtables = U16(data, cmap + 2);

        int best = -1;
        int bestScore = -1;

        for (int i = 0; i < subtables; i++)
        {
            int record = cmap + 4 + i * 8;

            int platform = U16(data, record);
            int encoding = U16(data, record + 2);
            int offset = cmap + (int) U32(data, record + 4);

            // Windows UCS-4 first, then Windows BMP, then anything Unicode. The first reaches past U+FFFF, which
            // the other two cannot.
            int score = platform == 3 && encoding == 10 ? 3
                : platform == 3 && encoding == 1 ? 2
                : platform == 0 ? 1 : 0;

            if (score <= bestScore)
                continue;

            bestScore = score;
            best = offset;
        }

        if (best < 0)
            return codes;

        int format = U16(data, best);

        if (format == 4)
        {
            int segments = U16(data, best + 6) / 2;

            int endCodes = best + 14;
            int startCodes = endCodes + segments * 2 + 2;

            for (int s = 0; s < segments; s++)
            {
                int first = U16(data, startCodes + s * 2);
                int last = U16(data, endCodes + s * 2);

                // The final segment is the required 0xFFFF terminator and covers nothing.
                if (first > last || last == 0xFFFF)
                    continue;

                for (int code = first; code <= last; code++)
                    codes.Add(code);
            }
        }
        else if (format == 12)
        {
            int groups = (int) U32(data, best + 12);

            for (int gi = 0; gi < groups; gi++)
            {
                int record = best + 16 + gi * 12;

                long first = U32(data, record);
                long last = U32(data, record + 4);

                if (first > last || last > 0x10FFFF)
                    continue;

                for (long code = first; code <= last; code++)
                    codes.Add((int) code);
            }
        }

        return codes;
    }

    private static int U16(byte[] data, int at)
    {
        return (data[at] << 8) | data[at + 1];
    }

    private static long U32(byte[] data, int at)
    {
        return ((long) data[at] << 24) | ((long) data[at + 1] << 16) | ((long) data[at + 2] << 8) | data[at + 3];
    }

    /// <summary>
    /// The column count that wastes the least texture, given how many glyphs there are and how big a cell is.
    ///
    /// Both dimensions round up to a power of two, so the cost is a step function and the best answer is not the
    /// square one -- it is whichever shape lands just under two thresholds at once. Tried exhaustively because
    /// there are at most a hundred and twenty candidates and this runs once per font per bake.
    /// </summary>
    private static int Pack(int count, int cellWidth, int cellHeight, out int width, out int height)
    {
        int bestColumns = MinColumns;
        long bestArea = long.MaxValue;
        long bestSquareness = long.MaxValue;

        width = 0;
        height = 0;

        for (int columns = MinColumns; columns <= MaxColumns; columns++)
        {
            if (columns > count && columns > MinColumns)
                break;

            int rows = (count + columns - 1) / columns;

            int candidateWidth = Pot(cellWidth * columns);
            int candidateHeight = Pot(cellHeight * rows);

            // Unity refuses a texture over 16384 on any axis, and plenty of hardware stops at 8192. Anything
            // taller or wider than that is not a candidate however little it wastes.
            if (candidateWidth > 8192 || candidateHeight > 8192)
                continue;

            long area = (long) candidateWidth * candidateHeight;

            // <b>Squarer wins a tie, and ties are common.</b> Both dimensions round to powers of two, so many
            // column counts land on exactly the same area -- 523 glyphs packs into 512x8192 and 2048x2048 for
            // the same four million pixels. The strip is the worse of the two: 8192 is the ceiling on plenty of
            // hardware, and a very long thin texture is the shape drivers handle least well.
            long squareness = Math.Abs((long) candidateWidth - candidateHeight);

            if (area > bestArea || (area == bestArea && squareness >= bestSquareness))
                continue;

            bestArea = area;
            bestSquareness = squareness;
            bestColumns = columns;
            width = candidateWidth;
            height = candidateHeight;
        }

        if (width != 0)
            return bestColumns;

        // Nothing fit. Give back the squarest attempt so the caller fails with a real size in the message rather
        // than a zero.
        bestColumns = (int) Math.Ceiling(Math.Sqrt(count));
        width = Pot(cellWidth * bestColumns);
        height = Pot(cellHeight * ((count + bestColumns - 1) / bestColumns));

        return bestColumns;
    }

    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("BakeAtlas <font.ttf> <outputDir> <name> [hexRanges|all] [emSize]");
            Console.WriteLine("  hexRanges: 10840-10855,1E800-1E8C4   (default: ASCII plus Latin-1)");
            Console.WriteLine("  all:       every code point the font's cmap covers");
            Console.WriteLine("  emSize:    rasterized size, default 64. Use 32 for interface text.");
            Console.WriteLine("  --bold <ttf> --italic <ttf> --bolditalic <ttf>");
            Console.WriteLine("             bake these into the same sheet, tagged by style, so that rich text");
            Console.WriteLine("             tags can switch weight. A Unity Font has one material and so one");
            Console.WriteLine("             texture: bold can only reach a bold glyph if it is on this sheet.");

            return 2;
        }

        string ttf = args[0];
        string outDir = args[1];
        string name = args[2];

        if (args.Length >= 5)
        {
            int wanted;

            // Bounded rather than trusted. Below twelve a glyph has too few pixels to carry its own shape, and
            // above two hundred and fifty six a full Latin sheet stops fitting a texture the hardware will take.
            if (!int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out wanted)
                || wanted < 12 || wanted > 256)
            {
                Console.WriteLine("emSize must be a whole number between 12 and 256.");

                return 2;
            }

            EmSize = wanted;
        }

        Directory.CreateDirectory(outDir);

        // <b>One sheet, several styles.</b> A UnityEngine.Font carries a single material and therefore a single
        // texture, so a rich text bold tag can only reach a bold glyph if that glyph is on the same sheet as the
        // regular one. Each style is a separate TTF and a separate collection, all baked into one atlas with the
        // style recorded per glyph. Asked for on 2026-08-29, for applying the faces across the whole interface.
        List<PrivateFontCollection> collections = new List<PrivateFontCollection>();
        List<FontFamily> families = new List<FontFamily>();
        List<FontStyle> gdiStyles = new List<FontStyle>();
        List<int> styleCodes = new List<int>();

        try
        {
            AddSource(collections, families, gdiStyles, styleCodes, ttf, 0);
            AddSource(collections, families, gdiStyles, styleCodes, Flag(args, "--bold"), 1);
            AddSource(collections, families, gdiStyles, styleCodes, Flag(args, "--italic"), 2);
            AddSource(collections, families, gdiStyles, styleCodes, Flag(args, "--bolditalic"), 3);

            FontFamily family = families[0];

            // The regular source sets the vertical metrics for the whole sheet. Bold and italic of the same
            // family share them by design; taking each style's own would give one sheet several baselines.
            FontStyle style = gdiStyles[0];

            float emHeight = family.GetEmHeight(style);
            float ascent = family.GetCellAscent(style) / emHeight * EmSize;
            float descent = family.GetCellDescent(style) / emHeight * EmSize;
            float lineSpacing = family.GetLineSpacing(style) / emHeight * EmSize;

            // Each source's own baseline position, because the glyph geometry below is anchored to it. The
            // header still carries style zero's metrics for the sheet as a whole.
            float[] ascents = new float[families.Count];

            for (int s = 0; s < families.Count; s++)
                // The cast is the whole line. GetCellAscent and GetEmHeight are both ints, and integer division
                // truncates their ratio to zero for any face whose ascent is smaller than its em -- which baked
                // Cascadia and Hammersmith with every glyph placed as if the ascent were nothing, put B's ink
                // below the baseline, and left IBM Plex and Oswald quietly 0.4 and 2.3 pixels low. Barlow alone
                // survived, because its ratio is exactly 1. Found from the metrics on 2026-08-30.
                ascents[s] = (float) families[s].GetCellAscent(gdiStyles[s])
                             / families[s].GetEmHeight(gdiStyles[s]) * EmSize;

            List<int> wanted;

            if (args.Length > 3 && args[3].Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                wanted = Coverage(ttf);

                if (wanted.Count == 0)
                {
                    Console.WriteLine("{0}: no usable cmap, falling back to ASCII plus Latin-1.", name);

                    wanted = new List<int>(DefaultGlyphs());
                }
            }
            else
            {
                wanted = new List<int>(args.Length > 3 ? Ranges(args[3]) : DefaultGlyphs());
            }

            // Measured first so the atlas is sized to what is actually there rather than to a guess, and so a
            // code point the face does not cover can be dropped before it takes up a cell.
            List<int> chars = new List<int>();
            List<RectangleF> ink = new List<RectangleF>();
            List<float> advances = new List<float>();

            // Which source each entry came from, so the rasterizer below can draw it with the right face and the
            // metrics can record which style it is.
            List<int> from = new List<int>();

            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            {
                for (int source = 0; source < families.Count; source++)
                {
                FontFamily family2 = families[source];
                FontStyle style2 = gdiStyles[source];

                using (Font font = new Font(family2, EmSize, style2, GraphicsUnit.Pixel))
                foreach (int code in wanted)
                {
                    string text = char.ConvertFromUtf32(code);
                    bool blank = code <= 0xFFFF && char.IsWhiteSpace((char) code);

                    RectangleF bounds = RectangleF.Empty;

                    if (!blank)
                    {
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddString(text, family2, (int) style2, EmSize, new PointF(0f, 0f),
                                StringFormat.GenericTypographic);

                            if (path.PointCount > 0)
                                bounds = path.GetBounds();
                        }

                        // No ink and not a space: either the face does not cover this code point, or it covers
                        // it with an empty glyph. Either way there is nothing to mask with, and baking it would
                        // put a blank in the middle of every run.
                        if (bounds.Width <= 0f || bounds.Height <= 0f)
                            continue;
                    }

                    // GenericTypographic, or MeasureString pads every glyph with invisible side bearings and
                    // every label comes out spaced like a ransom note.
                    SizeF measured = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);

                    chars.Add(code);
                    ink.Add(bounds);
                    from.Add(source);
                    advances.Add(blank && measured.Width <= 0.01f ? EmSize * 0.28f : measured.Width);
                }
                }
            }

            if (chars.Count == 0)
            {
                Console.WriteLine("{0}: no glyphs in range. Nothing written.", name);

                return 1;
            }

            // <b>Every cell is anchored on its pen and its baseline, at integer offsets shared by the whole
            // sheet.</b> The first version snapped each glyph's ink box to the cell corner, which threw the
            // fractional part of its position away -- and the renderer then had to reinvent it. Drawn at exact
            // fractional offsets the letters blurred unevenly; drawn at rounded offsets they displaced unevenly,
            // an F rising 0.4 of a pixel while the o beside it sank 0.25, which is the wave that was underlined
            // in red on 2026-08-29 after everything downstream had already been fixed.
            //
            // Anchoring the rasterization instead bakes each glyph at its true subpixel phase relative to the
            // baseline and the pen: the fraction is in the antialiasing, where it belongs. Everything the
            // metrics then say -- bearing, extents, and by rounding, the advance -- is a whole number, so no
            // renderer has anything left to round and both of ours agree with Unity's integer CharacterInfo to
            // the pixel.
            int maxAscent = 0;
            int maxDescent = 0;
            int maxLeft = 0;
            int maxRight = 0;

            for (int i = 0; i < chars.Count; i++)
            {
                RectangleF r = ink[i];

                if (r.Width <= 0f)
                    continue;

                float top = ascents[from[i]] - r.Y;

                maxAscent = Math.Max(maxAscent, (int) Math.Ceiling(top));
                maxDescent = Math.Max(maxDescent, -(int) Math.Floor(top - r.Height));
                maxLeft = Math.Max(maxLeft, -(int) Math.Floor(r.X));
                maxRight = Math.Max(maxRight, (int) Math.Ceiling(r.X + r.Width));
            }

            int penColumn = Padding + maxLeft;
            int baselineRow = Padding + maxAscent;
            int cellWidth = penColumn + maxRight + Padding;
            int cellHeight = baselineRow + maxDescent + Padding;

            int width;
            int height;

            int columns = Pack(chars.Count, cellWidth, cellHeight, out width, out height);

            StringBuilder metrics = new StringBuilder();

            metrics.Append("atlas\t").Append(width).Append('\t').Append(height).Append('\t')
                .Append(EmSize).Append('\t')
                .Append(F(ascent)).Append('\t').Append(F(descent)).Append('\t').Append(F(lineSpacing))
                .Append('\n');

            using (Bitmap atlas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(atlas))
            {
                g.Clear(Color.FromArgb(0, 255, 255, 255));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                for (int i = 0; i < chars.Count; i++)
                {
                    int code = chars[i];
                    RectangleF bounds = ink[i];

                    int cellX = i % columns * cellWidth;
                    int cellY = i / columns * cellHeight;

                    if (bounds.Width <= 0f || bounds.Height <= 0f)
                    {
                        // A space: an advance and nothing else. Rounded like every other advance, so the pen
                        // stays on whole pixels across it.
                        metrics.Append("g\t").Append(code).Append('\t')
                            .Append(cellX + penColumn).Append('\t').Append(cellY + baselineRow).Append('\t')
                            .Append(0).Append('\t').Append(0).Append('\t')
                            .Append(0).Append('\t').Append(0).Append('\t').Append(0)
                            .Append('\t').Append(F((float) Math.Round(advances[i])))
                            .Append('\t').Append(styleCodes[from[i]]).Append('\n');

                        continue;
                    }

                    // y up from the baseline. GDI+ measures down from the em box top, and this source's
                    // baseline sits its own ascent below that.
                    float top = ascents[from[i]] - bounds.Y;

                    // The whole-pixel box that contains the fractionally placed ink. Floor and ceiling rather
                    // than rounding, so the antialiased fringe on either side is inside the recorded rectangle
                    // instead of in the neighbouring cell's.
                    int maxYInt = (int) Math.Ceiling(top);
                    int minYInt = (int) Math.Floor(top - bounds.Height);
                    int leftInt = (int) Math.Floor(bounds.X);
                    int rightInt = (int) Math.Ceiling(bounds.X + bounds.Width);

                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddString(char.ConvertFromUtf32(code), families[from[i]],
                            (int) gdiStyles[from[i]], EmSize, new PointF(0f, 0f),
                            StringFormat.GenericTypographic);

                        // The pen lands on the cell's pen column and the baseline on its baseline row, both
                        // integers -- and the ink lands wherever the typeface puts it relative to them, at its
                        // true fraction of a pixel. That fraction is rasterized into the coverage here, once,
                        // instead of being reinvented by the renderer every frame.
                        using (Matrix move = new Matrix())
                        {
                            move.Translate(cellX + penColumn, cellY + baselineRow - ascents[from[i]]);
                            path.Transform(move);
                        }

                        g.FillPath(Brushes.White, path);
                    }

                    metrics.Append("g\t").Append(code).Append('\t')
                        .Append(cellX + penColumn + leftInt).Append('\t')
                        .Append(cellY + baselineRow - maxYInt).Append('\t')
                        .Append(rightInt - leftInt).Append('\t').Append(maxYInt - minYInt).Append('\t')
                        .Append(leftInt).Append('\t').Append(minYInt).Append('\t').Append(maxYInt)
                        .Append('\t').Append(F((float) Math.Round(advances[i])))
                        .Append('\t').Append(styleCodes[from[i]]).Append('\n');
                }

                atlas.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }

            File.WriteAllText(Path.Combine(outDir, name + ".txt"), metrics.ToString(), new UTF8Encoding(false));

            Console.WriteLine(
                "{0,-30} {1,5} glyphs  {9} styles  atlas {2}x{3}  {4} cols  cell {5}x{6}  ascent {7:F1}  "
                + "line {8:F1}",
                name, chars.Count, width, height, columns, cellWidth, cellHeight, ascent, lineSpacing,
                families.Count);
        }
        finally
        {
            // Disposed only after the last glyph is drawn. A collection freed while a GraphicsPath still refers
            // to its family takes the face out from under the rasterizer, and GDI+ answers that by drawing the
            // system default without saying so.
            for (int i = 0; i < collections.Count; i++)
                collections[i].Dispose();
        }

        return 0;
    }

    /// <summary>The value after a named flag, or null when it is not there.</summary>
    private static string Flag(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    /// <summary>
    /// Opens one style's TTF and records it as a source, or does nothing when the path is null.
    ///
    /// <b>Italic before bold in the fallback chain, which matters for a one-face file.</b> Each weight ships as
    /// its own TTF, so the family in a collection holds exactly one face -- and for BarlowCondensed-Italic that
    /// face is italic, not regular. GDI+ then refuses Regular, and falling straight to Bold asked it to
    /// synthesise a bold italic from an italic: a smeared, wrong-width face that would have baked without
    /// complaining.
    /// </summary>
    private static void AddSource(List<PrivateFontCollection> collections, List<FontFamily> families,
        List<FontStyle> gdiStyles, List<int> styleCodes, string ttf, int styleCode)
    {
        if (string.IsNullOrEmpty(ttf))
            return;

        PrivateFontCollection collection = new PrivateFontCollection();

        collection.AddFontFile(ttf);
        collections.Add(collection);

        FontFamily family = collection.Families[0];

        families.Add(family);
        styleCodes.Add(styleCode);

        gdiStyles.Add(family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
            : family.IsStyleAvailable(FontStyle.Italic) ? FontStyle.Italic
            : family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
            : FontStyle.Regular);
    }

    /// <summary>Rounds up to a power of two, which every GPU is happy with and some older ones require.</summary>
    private static int Pot(int value)
    {
        int size = 1;

        while (size < value)
            size <<= 1;

        return size;
    }

    private static string F(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
