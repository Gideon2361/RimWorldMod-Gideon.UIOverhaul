using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// One glyph of a baked interface face: where it sits in the sheet, and where it sits on the line.
    ///
    /// <b>Everything is in atlas pixels, which is to say in units of the em the sheet was baked at.</b> A caller
    /// scales all of it by one factor to reach the size it wants to draw at, so nothing here has to know what
    /// that size will be.
    ///
    /// <b>y runs up from the baseline,</b> the convention the baker converts to once when it writes the file.
    /// GDI+ measures downward from the top of the em box; doing that flip at draw time is how text ends up
    /// sitting a few pixels off with nobody able to say why.
    /// </summary>
    internal struct UITypefaceGlyph
    {
        /// <summary>Where the ink is in the sheet, ready for <c>GUI.DrawTextureWithTexCoords</c>.</summary>
        internal Rect Uv;

        /// <summary>Pen to the left edge of the ink. Negative for the few glyphs that hang backwards.</summary>
        internal float Bearing;

        internal float InkWidth;
        internal float InkHeight;

        /// <summary>Baseline to the top of the ink, positive upward.</summary>
        internal float MaxY;

        /// <summary>How far the pen moves after this glyph, which is not the ink width.</summary>
        internal float Advance;

        /// <summary>The character this draws.</summary>
        internal char Code;

        /// <summary>
        /// Which style this glyph is: 0 normal, 1 bold, 2 italic, 3 both.
        ///
        /// <b>Deliberately the same numbering as <c>UnityEngine.FontStyle</c>,</b> so a sheet's style column can
        /// be handed to a <c>CharacterInfo</c> without a translation table that could drift from it. One sheet
        /// carries every style because a Unity font has one material and therefore one texture -- a bold tag can
        /// only reach a bold glyph that is already on the same sheet.
        /// </summary>
        internal int Style;

        /// <summary>A space has an advance and no ink, and must still move the pen.</summary>
        internal bool Drawable
        {
            get { return InkWidth > 0f && InkHeight > 0f; }
        }
    }

    /// <summary>
    /// One baked weight of the interface face, read from the PNG and metrics table beside the assembly.
    ///
    /// <b>Why a baked atlas at all.</b> Unity cannot build a <c>Font</c> from a file or a byte array: the whole
    /// surface is <c>Font()</c>, <c>Font(name)</c> and <c>CreateDynamicFontFromOSFont</c>, and every one of them
    /// wants the typeface installed on the player's machine. The alternative is an AssetBundle built with the
    /// editor at RimWorld's exact Unity version. See <c>ThirdParty/Fonts/README-Gideon.md</c> for the baker.
    ///
    /// <b>This is the third reader of the same file format, and the first that sets running text.</b>
    /// <c>FloorLabelAtlas</c> hands out a <c>Material</c> for a world-space mesh drawn under the colony's walls;
    /// <c>ResearchScriptAtlas</c> draws single marks into a grid and needs no baseline at all. Neither carries
    /// the ascent or the line spacing, because neither ever had to put two glyphs beside each other and land
    /// them on the same line. Those numbers are the whole difference, and they are what this adds.
    ///
    /// <b>A missing sheet is not an error.</b> The face is only as good as the files beside the assembly, and a
    /// player who deleted the Fonts folder should get RimWorld's own text rather than a stack trace or a row of
    /// blanks. <see cref="Available"/> is what every caller asks first.
    /// </summary>
    internal sealed class UITypefaceAtlas
    {
        private readonly string fileName;

        private readonly Dictionary<int, UITypefaceGlyph> glyphs = new Dictionary<int, UITypefaceGlyph>();

        private readonly List<UITypefaceGlyph> all = new List<UITypefaceGlyph>();

        /// <summary>Code and style in one key, so a sheet can hold four of every letter.</summary>
        private static int Key(char code, int style)
        {
            return code | (style << 16);
        }

        private Texture2D texture;

        private float em = 64f;
        private float ascent = 64f;
        private float lineSpacing = 76.8f;

        private bool tried;
        private bool broken;

        internal UITypefaceAtlas(string fileName)
        {
            this.fileName = fileName;
        }

        internal bool Available
        {
            get
            {
                Load();

                return !broken && texture != null && glyphs.Count > 0;
            }
        }

        internal Texture Texture
        {
            get
            {
                Load();

                return texture;
            }
        }

        /// <summary>The size the glyphs were rasterized at, which is the unit all their metrics are in.</summary>
        internal float Em
        {
            get
            {
                Load();

                return em;
            }
        }

        /// <summary>Top of the line box to the baseline, in em units.</summary>
        internal float AscentRatio
        {
            get
            {
                Load();

                return em > 0f ? ascent / em : 1f;
            }
        }

        /// <summary>One line's full height, in em units. 1.2 for the shipped face.</summary>
        internal float LineRatio
        {
            get
            {
                Load();

                return em > 0f ? lineSpacing / em : 1.2f;
            }
        }

        internal bool TryGlyph(char c, out UITypefaceGlyph glyph)
        {
            return TryGlyph(c, 0, out glyph);
        }

        /// <summary>
        /// One glyph in a particular style, falling back to the regular one when the sheet has no such style.
        ///
        /// <b>The fallback is why a face with no italic is still usable.</b> Oswald ships as a single instance
        /// and Hammersmith as one weight; asking either for bold gets the regular glyph rather than nothing,
        /// which is what Unity does with a font that lacks a style and is the only answer that keeps the text
        /// readable.
        /// </summary>
        internal bool TryGlyph(char c, int style, out UITypefaceGlyph glyph)
        {
            Load();

            return glyphs.TryGetValue(Key(c, style), out glyph)
                   || glyphs.TryGetValue(Key(c, 0), out glyph);
        }

        /// <summary>
        /// Every baked glyph at once.
        ///
        /// For <see cref="UIRuntimeFont"/>, which has to hand Unity the whole sheet in one array rather than
        /// asking for characters as they turn up. Exposed as a read-only view so a caller cannot add a glyph the
        /// texture has no ink for.
        /// </summary>
        internal IEnumerable<UITypefaceGlyph> Glyphs
        {
            get
            {
                Load();

                return all;
            }
        }

        private void Load()
        {
            if (tried)
                return;

            tried = true;

            // Silent on failure, deliberately. The caller falls back to RimWorld's own text, which costs the
            // look rather than the feature, and a packaging fault would otherwise be reported once per weight
            // per session.
            broken = !UIGuard.Try("UIText.LoadAtlas", Read, false, null);
        }

        private bool Read()
        {
            string folder = OurFontsFolder();

            if (folder == null)
                return false;

            string metricsPath = Path.Combine(folder, fileName + ".txt");
            string imagePath = Path.Combine(folder, fileName + ".png");

            if (!File.Exists(metricsPath) || !File.Exists(imagePath))
                return false;

            // Mipmaps off and clamped, for the reason the other two readers give: on a glyph sheet drawn at
            // eighteen pixels a mipmap blends neighbouring cells together and every letter grows a ghost of the
            // one baked beside it.
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (!texture.LoadImage(File.ReadAllBytes(imagePath)))
            {
                Object.Destroy(texture);
                texture = null;

                return false;
            }

            return Parse(File.ReadAllLines(metricsPath));
        }

        /// <summary>
        /// Reads the metrics table: one <c>atlas</c> header line, then a <c>g</c> line per glyph.
        ///
        /// The header is width, height, em, ascent, descent and line spacing. A glyph line is the code point,
        /// the cell's position and size in the sheet, then the bearing, the ink's bottom and top against the
        /// baseline, and the advance.
        ///
        /// Tab separated and invariant, because it is generated rather than written by hand and a shipped data
        /// file must not parse differently on a machine that writes decimals with a comma.
        /// </summary>
        private bool Parse(string[] lines)
        {
            float sheetWidth = texture.width;
            float sheetHeight = texture.height;

            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');

                if (parts.Length >= 7 && parts[0] == "atlas")
                {
                    float measured = Number(parts[3]);

                    if (measured > 0f)
                        em = measured;

                    ascent = Number(parts[4]);
                    lineSpacing = Number(parts[6]);

                    continue;
                }

                if (parts.Length < 10 || parts[0] != "g")
                    continue;

                int code = (int) Number(parts[1]);

                // The interface face is baked over everything it covers, and a face can cover code points above
                // U+FFFF. Those cannot be keyed by char and are not text this sets, so they are dropped here
                // rather than wrapping round onto some unrelated letter.
                if (code <= 0 || code > char.MaxValue)
                    continue;

                float x = Number(parts[2]);
                float y = Number(parts[3]);
                float w = Number(parts[4]);
                float h = Number(parts[5]);

                // Absent on a sheet baked before styles were carried, and zero is the right reading of that: it
                // was a regular-only sheet. The floor labels and the research masks still write ten fields.
                int style = parts.Length >= 11 ? (int) Number(parts[10]) : 0;

                // The PNG's rows run downward and a texture's V runs upward, so the flip happens here once
                // rather than being rediscovered at every call site.
                UITypefaceGlyph glyph = new UITypefaceGlyph
                {
                    Uv = new Rect(x / sheetWidth, 1f - (y + h) / sheetHeight, w / sheetWidth, h / sheetHeight),
                    Bearing = Number(parts[6]),
                    InkWidth = w,
                    InkHeight = h,
                    MaxY = Number(parts[8]),
                    Advance = Number(parts[9]),
                    Code = (char) code,
                    Style = style
                };

                all.Add(glyph);

                // Keyed by code and style together, so the four A's on a sheet do not overwrite each other.
                glyphs[Key(glyph.Code, style)] = glyph;
            }

            return glyphs.Count > 0;
        }

        private static float Number(string text)
        {
            float value;

            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

        /// <summary>
        /// Our own mod folder's Fonts directory.
        ///
        /// Found through the running mod list rather than assumed, because the folder name is whatever the
        /// player or Steam called it: a local checkout, a Workshop id, or a renamed copy.
        /// </summary>
        private static string OurFontsFolder()
        {
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod == null || mod.assemblies == null || mod.assemblies.loadedAssemblies == null)
                    continue;

                foreach (System.Reflection.Assembly loaded in mod.assemblies.loadedAssemblies)
                {
                    if (loaded != typeof(UITypefaceAtlas).Assembly)
                        continue;

                    string folder = Path.Combine(mod.RootDir, "Fonts");

                    return Directory.Exists(folder) ? folder : null;
                }
            }

            return null;
        }
    }
}
