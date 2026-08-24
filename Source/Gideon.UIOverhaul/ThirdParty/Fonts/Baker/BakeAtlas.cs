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
    /// <summary>Em size the glyphs are rasterized at. Resolution, not display size.</summary>
    private const int EmSize = 64;

    /// <summary>Transparent margin around each glyph, so bilinear sampling cannot bleed a neighbor in.</summary>
    private const int Padding = 2;

    private const int Columns = 16;

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

    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("BakeAtlas <font.ttf> <outputDir> <name> [hexRanges]");
            Console.WriteLine("  hexRanges: 10840-10855,1E800-1E8C4   (default: ASCII plus Latin-1)");

            return 2;
        }

        string ttf = args[0];
        string outDir = args[1];
        string name = args[2];

        Directory.CreateDirectory(outDir);

        // Loaded rather than installed. PrivateFontCollection reads the file directly, so baking needs no
        // changes to the machine and works the same on a build agent.
        using (PrivateFontCollection collection = new PrivateFontCollection())
        {
            collection.AddFontFile(ttf);

            FontFamily family = collection.Families[0];
            FontStyle style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;

            float emHeight = family.GetEmHeight(style);
            float ascent = family.GetCellAscent(style) / emHeight * EmSize;
            float descent = family.GetCellDescent(style) / emHeight * EmSize;
            float lineSpacing = family.GetLineSpacing(style) / emHeight * EmSize;

            List<int> wanted = new List<int>(args.Length > 3 ? Ranges(args[3]) : DefaultGlyphs());

            // Measured first so the atlas is sized to what is actually there rather than to a guess, and so a
            // code point the face does not cover can be dropped before it takes up a cell.
            List<int> chars = new List<int>();
            List<RectangleF> ink = new List<RectangleF>();
            List<float> advances = new List<float>();

            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            using (Font font = new Font(family, EmSize, style, GraphicsUnit.Pixel))
            {
                foreach (int code in wanted)
                {
                    string text = char.ConvertFromUtf32(code);
                    bool blank = code <= 0xFFFF && char.IsWhiteSpace((char) code);

                    RectangleF bounds = RectangleF.Empty;

                    if (!blank)
                    {
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddString(text, family, (int) style, EmSize, new PointF(0f, 0f),
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
                    advances.Add(blank && measured.Width <= 0.01f ? EmSize * 0.28f : measured.Width);
                }
            }

            if (chars.Count == 0)
            {
                Console.WriteLine("{0}: no glyphs in range. Nothing written.", name);

                return 1;
            }

            int cellWidth = 0;
            int cellHeight = 0;

            foreach (RectangleF r in ink)
            {
                cellWidth = Math.Max(cellWidth, (int) Math.Ceiling(r.Width) + Padding * 2);
                cellHeight = Math.Max(cellHeight, (int) Math.Ceiling(r.Height) + Padding * 2);
            }

            int rows = (chars.Count + Columns - 1) / Columns;
            int width = Pot(cellWidth * Columns);
            int height = Pot(cellHeight * rows);

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

                    int cellX = i % Columns * cellWidth;
                    int cellY = i / Columns * cellHeight;

                    int w = (int) Math.Ceiling(bounds.Width);
                    int h = (int) Math.Ceiling(bounds.Height);

                    if (w > 0 && h > 0)
                    {
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddString(char.ConvertFromUtf32(code), family, (int) style, EmSize,
                                new PointF(0f, 0f), StringFormat.GenericTypographic);

                            // Shifted so the glyph's ink lands at the cell's padded corner, which is what makes
                            // the recorded rectangle exact rather than approximately right.
                            using (Matrix move = new Matrix())
                            {
                                move.Translate(cellX + Padding - bounds.X, cellY + Padding - bounds.Y);
                                path.Transform(move);
                            }

                            g.FillPath(Brushes.White, path);
                        }
                    }

                    // y up from the baseline. GDI+ measures down from the em box top, and the baseline sits
                    // `ascent` below that.
                    float maxY = ascent - bounds.Y;
                    float minY = maxY - bounds.Height;

                    metrics.Append("g\t").Append(code).Append('\t')
                        .Append(cellX + Padding).Append('\t').Append(cellY + Padding).Append('\t')
                        .Append(w).Append('\t').Append(h).Append('\t')
                        .Append(F(bounds.X)).Append('\t').Append(F(minY)).Append('\t').Append(F(maxY))
                        .Append('\t').Append(F(advances[i])).Append('\n');
                }

                atlas.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }

            File.WriteAllText(Path.Combine(outDir, name + ".txt"), metrics.ToString(), new UTF8Encoding(false));

            Console.WriteLine("{0,-28} {1} glyphs  atlas {2}x{3}  cell {4}x{5}  ascent {6:F1}  line {7:F1}",
                name, chars.Count, width, height, cellWidth, cellHeight, ascent, lineSpacing);
        }

        return 0;
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
