using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// One glyph in a baked script atlas: where it is in the sheet and how wide it draws.
    ///
    /// <b>A UV rectangle rather than four corners.</b> The floor labels keep corners because Unity may rotate a
    /// glyph inside its own dynamic atlas and reports them individually; a baked sheet never rotates anything, and
    /// <c>GUI.DrawTextureWithTexCoords</c> takes a rectangle.
    /// </summary>
    internal struct ScriptGlyph
    {
        internal Rect Uv;

        /// <summary>Ink width and height in atlas pixels, which is what sets the aspect it draws at.</summary>
        internal float InkWidth;

        internal float InkHeight;

        internal bool Drawable
        {
            get { return InkWidth > 0f && InkHeight > 0f; }
        }
    }

    /// <summary>
    /// A script typeface baked into a PNG and a metrics table, read at runtime and drawn into the interface.
    ///
    /// <b>Why a baked atlas at all.</b> Unity cannot build a <c>Font</c> from a file or a byte array: the whole
    /// surface is <c>Font()</c>, <c>Font(name)</c> and <c>CreateDynamicFontFromOSFont</c>, and all three want the
    /// typeface installed on the player's machine. The alternative is an AssetBundle built with the editor at
    /// RimWorld's exact Unity version. See <c>ThirdParty/Fonts/README-Gideon.md</c> for the baker.
    ///
    /// <b>Why this is not <c>FloorLabelAtlas</c>, which reads the same file format.</b> Two differences, and each
    /// on its own would be enough. That one is keyed by <c>char</c>, and every script here lives above U+FFFF
    /// where a <c>char</c> cannot reach -- these are keyed by code point. And that one hands out a
    /// <c>Material</c> for a world-space mesh drawn under the colony's walls, while this draws GUI textures in
    /// screen space. Sharing the parse would mean one type serving two draw paths and two key types, which is how
    /// a change made for the map breaks the interface.
    ///
    /// <b>A missing atlas is not an error.</b> The three script options are only as good as the files beside the
    /// assembly, and a player who deleted the Fonts folder should get the generated marks rather than a stack
    /// trace. <see cref="Available"/> is what the picker and the mask ask.
    /// </summary>
    internal sealed class ResearchScriptAtlas
    {
        private readonly string fileName;

        private readonly Dictionary<int, ScriptGlyph> glyphs = new Dictionary<int, ScriptGlyph>();

        /// <summary>Every code point the sheet holds, in order, so a mask can index into it.</summary>
        private readonly List<int> codePoints = new List<int>();

        private Texture2D texture;

        /// <summary>The em the glyphs were rasterized at, which is the unit their ink is measured in.</summary>
        private float em = 64f;

        private bool tried;
        private bool broken;

        internal ResearchScriptAtlas(string fileName)
        {
            this.fileName = fileName;
        }

        internal bool Available
        {
            get
            {
                Load();

                return !broken && texture != null && codePoints.Count > 0;
            }
        }

        /// <summary>How many distinct marks this script offers, which is the alphabet a mask draws from.</summary>
        internal int Count
        {
            get
            {
                Load();

                return codePoints.Count;
            }
        }

        /// <summary>
        /// Draws one mark in the given cell, at the size it would be if the script were being set as text.
        ///
        /// <b>Scaled against the em, not fitted to the cell.</b> Fitting each glyph to its cell was the first
        /// version and it is wrong for a script: a short character and a tall one would come out the same height,
        /// which flattens exactly the rhythm that makes a run read as writing. Scaling by the em keeps the
        /// proportions the typeface was drawn with.
        ///
        /// The width is still clamped, since a cell is narrower than it is tall and a wide character would
        /// otherwise run into its neighbour.
        /// </summary>
        internal void Draw(int index, Rect cell)
        {
            Load();

            if (broken || texture == null || codePoints.Count == 0)
                return;

            ScriptGlyph glyph;

            if (!glyphs.TryGetValue(codePoints[Mathf.Abs(index) % codePoints.Count], out glyph) || !glyph.Drawable)
                return;

            // Against a cap height rather than the full em: an em box holds the ascender and the descender, so
            // scaling a plain letter against it leaves it noticeably smaller than the text beside it.
            float scale = cell.height / (em * 0.72f);

            if (glyph.InkWidth * scale > cell.width)
                scale = cell.width / glyph.InkWidth;

            float width = glyph.InkWidth * scale;
            float height = glyph.InkHeight * scale;

            Rect fitted = new Rect(cell.center.x - width * 0.5f, cell.center.y - height * 0.5f, width, height);

            GUI.DrawTextureWithTexCoords(fitted, texture, glyph.Uv);
        }

        private void Load()
        {
            if (tried)
                return;

            tried = true;

            broken = !UIGuard.Try("Research.LoadScriptAtlas", Read, false, null);
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

            // Mipmaps off and clamped, for the reason the floor label atlas gives: on a glyph sheet drawn small a
            // mipmap blends neighbouring cells together and every mark grows a ghost of the one beside it.
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
        /// Tab separated and invariant, because it is generated rather than written by hand and a shipped data
        /// file must not parse differently on a machine that writes decimals with a comma.
        ///
        /// The vertical flip happens here: the PNG's rows run downward and a texture's V runs upward.
        /// </summary>
        private bool Parse(string[] lines)
        {
            float sheetWidth = texture.width;
            float sheetHeight = texture.height;

            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');

                if (parts.Length >= 5 && parts[0] == "atlas")
                {
                    float measured = Number(parts[3]);

                    if (measured > 0f)
                        em = measured;

                    continue;
                }

                if (parts.Length < 10 || parts[0] != "g")
                    continue;

                int code = (int) Number(parts[1]);
                float x = Number(parts[2]);
                float y = Number(parts[3]);
                float w = Number(parts[4]);
                float h = Number(parts[5]);

                if (w <= 0f || h <= 0f || glyphs.ContainsKey(code))
                    continue;

                glyphs[code] = new ScriptGlyph
                {
                    Uv = new Rect(x / sheetWidth, 1f - (y + h) / sheetHeight, w / sheetWidth, h / sheetHeight),
                    InkWidth = w,
                    InkHeight = h
                };

                codePoints.Add(code);
            }

            return codePoints.Count > 0;
        }

        private static float Number(string text)
        {
            float value;

            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

        /// <summary>
        /// Our own mod folder's Fonts directory.
        ///
        /// Found through the running mod list rather than assumed, because the folder name is whatever the player
        /// or Steam called it: a local checkout, a Workshop id, or a renamed copy.
        /// </summary>
        private static string OurFontsFolder()
        {
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod == null || mod.assemblies == null || mod.assemblies.loadedAssemblies == null)
                    continue;

                foreach (System.Reflection.Assembly loaded in mod.assemblies.loadedAssemblies)
                {
                    if (loaded != typeof(ResearchScriptAtlas).Assembly)
                        continue;

                    string folder = Path.Combine(mod.RootDir, "Fonts");

                    return Directory.Exists(folder) ? folder : null;
                }
            }

            return null;
        }
    }
}
