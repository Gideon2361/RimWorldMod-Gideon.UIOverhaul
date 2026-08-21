using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// Which typeface the labels are drawn in. Baked atlases only.
    ///
    /// <b>RimWorld's own font was a third option and is gone, on Aaron's instruction 2026-08-21.</b> It is what
    /// stood between these labels and drawing under the colony rather than over it: a tinting shader takes its
    /// colour from the material and its coverage from the texture's alpha, which works on an atlas baked white,
    /// and renders solid black on Unity's dynamic atlas whatever colour it is given. So the dynamic font forced a
    /// draw-on-top material, and keeping it as an option would have meant one face layering differently from the
    /// other two. Labels on Floor solves the same problem the same way -- it ships a font grid and never touches
    /// the dynamic one.
    ///
    /// A saved setting naming the removed face reads back as <see cref="OswaldBold"/>, which is what
    /// <c>ParseFace</c> already does with any name it does not recognise.
    /// </summary>
    public enum FloorLabelFace
    {
        /// <summary>Oswald Bold. Condensed, so long names shrink less to fit a room.</summary>
        OswaldBold,

        /// <summary>Hammersmith One. Wider and more geometric.</summary>
        HammersmithOne
    }

    /// <summary>
    /// One glyph, in the shape the mesh builder wants regardless of where it came from.
    ///
    /// <b>Pixels with y up from the baseline,</b> which is the one convention both sources are converted to. The
    /// baked atlases are measured that way when they are built and Unity's <c>CharacterInfo</c> already uses it,
    /// so nothing downstream has to know which source it is looking at.
    ///
    /// <b>Four UV corners rather than a rectangle,</b> because Unity is free to rotate a glyph inside its dynamic
    /// atlas and reports the corners individually when it does. A rectangle would render those sideways.
    /// </summary>
    internal struct FloorGlyph
    {
        internal float MinX;
        internal float MaxX;
        internal float MinY;
        internal float MaxY;
        internal float Advance;

        internal Vector2 UvBottomLeft;
        internal Vector2 UvBottomRight;
        internal Vector2 UvTopLeft;
        internal Vector2 UvTopRight;

        internal bool Drawable => MaxX > MinX && MaxY > MinY;
    }

    /// <summary>Where glyphs come from. Implemented by the baked atlases and by the game's own font.</summary>
    internal interface IFloorGlyphSource
    {
        bool Available { get; }

        /// <summary>Size the glyphs were measured at, which is the unit a mesh is built in.</summary>
        float EmSize { get; }

        /// <summary>The atlas texture, for anything that wants to draw glyphs itself.</summary>
        Texture Texture { get; }

        /// <summary>Asks for these characters, where that means anything.</summary>
        void Request(string text);

        bool TryGlyph(char c, out FloorGlyph glyph);

        Material MaterialFor(Color color);
    }

    /// <summary>
    /// A typeface baked into a PNG and a metrics table, read at runtime.
    ///
    /// <b>This exists because Unity cannot load a font file.</b> Its entire API for building a <c>Font</c> is
    /// <c>Font()</c>, <c>Font(name)</c> and <c>CreateDynamicFontFromOSFont</c> -- all of which need the typeface
    /// installed on the player's machine, which is no way to ship a consistent look. The alternative is an
    /// AssetBundle built with the editor at RimWorld's exact Unity version. Baking sidesteps both, and it suits
    /// this feature because the label renderer already builds its own meshes: it only ever wanted glyph
    /// rectangles and advances, which is precisely what the metrics file holds.
    ///
    /// See <c>ThirdParty/Fonts/README-Gideon.md</c> for the baker and how to regenerate these.
    /// </summary>
    internal sealed class FloorLabelAtlas : IFloorGlyphSource
    {
        private readonly string fileName;
        private readonly Dictionary<char, FloorGlyph> glyphs = new Dictionary<char, FloorGlyph>();
        private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

        private Texture2D texture;
        private float em;
        private bool tried;
        private bool broken;

        internal FloorLabelAtlas(string fileName)
        {
            this.fileName = fileName;
        }

        public bool Available
        {
            get
            {
                Load();

                return !broken && texture != null && glyphs.Count > 0;
            }
        }

        public float EmSize
        {
            get
            {
                Load();

                return em <= 0f ? 64f : em;
            }
        }

        public Texture Texture
        {
            get
            {
                Load();

                return texture;
            }
        }

        /// <summary>Nothing to do: every glyph is already in the atlas or was never in it.</summary>
        public void Request(string text)
        {
        }

        public bool TryGlyph(char c, out FloorGlyph glyph)
        {
            Load();

            return glyphs.TryGetValue(c, out glyph);
        }

        /// <summary>
        /// A material tinted for one label color.
        ///
        /// <b>The tint works because of the texture, not the shader.</b> The baker writes white into the colour
        /// channels and puts the glyph in alpha, so multiplying by a colour yields that colour. Unity's dynamic
        /// atlases are black with alpha, which is what made an earlier attempt render every label solid black
        /// whatever colour it was given, and ultimately why that face was dropped.
        ///
        /// <b><c>Transparent</c> rather than <c>MetaOverlay</c>, which is what puts the labels on the floor.</b>
        /// MetaOverlay exists to draw over everything, so no <c>AltitudeLayer</c> could place these under a wall
        /// however low it was set -- the layer was never the problem. RimWorld's altitude system works because its
        /// shaders share a queue and sort along the view axis, so a material in that same queue obeys the altitude
        /// it is given. Read from Labels on Floor, which uses this shader for the same reason.
        ///
        /// Forcing the render queue was tried instead and failed twice over: on top at 3000, invisible under the
        /// floors at 2200. The shader was the knob all along.
        /// </summary>
        public Material MaterialFor(Color color)
        {
            if (!Available)
                return null;

            Material existing;

            if (materials.TryGetValue(color, out existing) && existing != null)
                return existing;

            Material made = UIGuard.Try("FloorLabels.AtlasMaterial", () =>
            {
                Material material = new Material(ShaderDatabase.Transparent);

                material.mainTexture = texture;
                material.color = color;

                return material;
            }, null, null);

            if (made != null)
                materials[color] = made;

            return made;
        }

        private void Load()
        {
            if (tried)
                return;

            tried = true;

            bool loaded = UIGuard.Try("FloorLabels.LoadAtlas", Read, false,
                "One floor label typeface could not be loaded. The game's own font is used instead.");

            broken = !loaded;
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

            // Mipmaps off and clamped, deliberately, which is why this does not go through UIImageLoader: that
            // builds mipmapped, repeating textures for panel art. On a glyph atlas drawn small, a mipmap blends
            // neighboring glyphs into each other and letters grow ghosts of their neighbors.
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (!texture.LoadImage(File.ReadAllBytes(imagePath)))
            {
                UnityEngine.Object.Destroy(texture);
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
        /// </summary>
        private bool Parse(string[] lines)
        {
            float width = texture.width;
            float height = texture.height;

            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');

                if (parts.Length == 0)
                    continue;

                if (parts[0] == "atlas" && parts.Length >= 5)
                {
                    em = Number(parts[3]);

                    continue;
                }

                if (parts[0] != "g" || parts.Length < 10)
                    continue;

                int code = (int) Number(parts[1]);
                float x = Number(parts[2]);
                float y = Number(parts[3]);
                float w = Number(parts[4]);
                float h = Number(parts[5]);
                float bearing = Number(parts[6]);

                // The PNG's rows run downward and a texture's V runs upward, so the vertical flip happens here
                // once rather than being rediscovered at every call site.
                float v0 = 1f - (y + h) / height;
                float v1 = 1f - y / height;
                float u0 = x / width;
                float u1 = (x + w) / width;

                glyphs[(char) code] = new FloorGlyph
                {
                    MinX = bearing,
                    MaxX = bearing + w,
                    MinY = Number(parts[7]),
                    MaxY = Number(parts[8]),
                    Advance = Number(parts[9]),
                    UvBottomLeft = new Vector2(u0, v0),
                    UvBottomRight = new Vector2(u1, v0),
                    UvTopLeft = new Vector2(u0, v1),
                    UvTopRight = new Vector2(u1, v1)
                };
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
        /// Found through the running mod list rather than assumed, because the folder name is whatever the player
        /// or Steam called it -- a local checkout, a Workshop id, or a renamed copy.
        /// </summary>
        private static string OurFontsFolder()
        {
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod == null || mod.assemblies == null || mod.assemblies.loadedAssemblies == null)
                    continue;

                foreach (System.Reflection.Assembly loaded in mod.assemblies.loadedAssemblies)
                {
                    if (loaded != typeof(FloorLabelAtlas).Assembly)
                        continue;

                    string folder = Path.Combine(mod.RootDir, "Fonts");

                    return Directory.Exists(folder) ? folder : null;
                }
            }

            return null;
        }
    }
}
