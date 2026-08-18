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
    /// Glyphs baked: printable ASCII plus the Latin-1 letters.
    ///
    /// Labels are drawn upper cased, so the lowercase range is strictly speaking unused -- it is included anyway
    /// because it costs a few hundred kilobytes of atlas and stops the whole thing being wasted if that decision
    /// ever changes. Anything outside this set makes a label fall back to the game's own font at runtime.
    /// </summary>
    private static IEnumerable<char> Glyphs()
    {
        for (char c = ' '; c <= '~'; c++)
            yield return c;

        for (char c = 'À'; c <= 'ÿ'; c++)
            yield return c;
    }

    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("BakeAtlas <font.ttf> <outputDir> <name>");

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

            List<char> chars = new List<char>(Glyphs());

            // Measured first so the atlas is sized to what is actually there rather than to a guess.
            List<RectangleF> ink = new List<RectangleF>();
            List<float> advances = new List<float>();

            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            using (Font font = new Font(family, EmSize, style, GraphicsUnit.Pixel))
            {
                foreach (char c in chars)
                {
                    RectangleF bounds = RectangleF.Empty;

                    if (!char.IsWhiteSpace(c))
                    {
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddString(c.ToString(), family, (int) style, EmSize, new PointF(0f, 0f),
                                StringFormat.GenericTypographic);

                            if (path.PointCount > 0)
                                bounds = path.GetBounds();
                        }
                    }

                    ink.Add(bounds);

                    // GenericTypographic, or MeasureString pads every glyph with invisible side bearings and
                    // every label comes out spaced like a ransom note.
                    SizeF measured = g.MeasureString(c.ToString(), font, PointF.Empty,
                        StringFormat.GenericTypographic);

                    advances.Add(c == ' ' && measured.Width <= 0.01f ? EmSize * 0.28f : measured.Width);
                }
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
                    char c = chars[i];
                    RectangleF bounds = ink[i];

                    int cellX = i % Columns * cellWidth;
                    int cellY = i / Columns * cellHeight;

                    int w = (int) Math.Ceiling(bounds.Width);
                    int h = (int) Math.Ceiling(bounds.Height);

                    if (w > 0 && h > 0)
                    {
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddString(c.ToString(), family, (int) style, EmSize, new PointF(0f, 0f),
                                StringFormat.GenericTypographic);

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

                    metrics.Append("g\t").Append((int) c).Append('\t')
                        .Append(cellX + Padding).Append('\t').Append(cellY + Padding).Append('\t')
                        .Append(w).Append('\t').Append(h).Append('\t')
                        .Append(F(bounds.X)).Append('\t').Append(F(minY)).Append('\t').Append(F(maxY))
                        .Append('\t').Append(F(advances[i])).Append('\n');
                }

                atlas.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }

            File.WriteAllText(Path.Combine(outDir, name + ".txt"), metrics.ToString(), new UTF8Encoding(false));

            Console.WriteLine("{0,-16} {1} glyphs  atlas {2}x{3}  cell {4}x{5}  ascent {6:F1}  line {7:F1}",
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
