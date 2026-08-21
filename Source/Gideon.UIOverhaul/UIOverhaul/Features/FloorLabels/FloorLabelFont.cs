using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// Which typeface the labels are currently drawn with, and the glyphs it provides.
    ///
    /// <b>A facade over two very different sources.</b> Two of the three faces are baked atlases shipped with the
    /// mod; the third is RimWorld's own dynamic font. They have almost nothing in common mechanically -- one is a
    /// PNG and a table, the other rebuilds itself at unpredictable moments -- so everything that draws a label
    /// asks here and never learns which it got.
    ///
    /// <b>Changing the face throws away every mesh.</b> A mesh's UVs address one specific atlas, so a mesh built
    /// from Oswald is meaningless against Hammersmith. The setting is watched rather than trusted to notify,
    /// because it can be edited in the config file while the game runs.
    /// </summary>
    internal static class FloorLabelFont
    {
        private static readonly FloorLabelAtlas Oswald = new FloorLabelAtlas("OswaldBold");
        private static readonly FloorLabelAtlas Hammersmith = new FloorLabelAtlas("HammersmithOne");
        private static readonly GameFontSource GameFont = new GameFontSource();

        private static FloorLabelFace lastFace = (FloorLabelFace) (-1);

        /// <summary>Raised when the glyphs move: a face change, or the game font's atlas rebuilding.</summary>
        internal static event Action Invalidated;

        /// <summary>
        /// The source for the chosen face, falling back when a baked atlas will not load.
        ///
        /// <b>The fallback is the game's own font,</b> which is always available. A missing or corrupt atlas
        /// therefore costs the look and not the feature -- and it is reported once by the atlas itself, so the
        /// reason is in the log rather than left as labels that mysteriously match the interface.
        /// </summary>
        internal static IFloorGlyphSource Source
        {
            get
            {
                FloorLabelFace wanted = UIOverhaulSettingsFile.Current.roomLabelFace;

                if (wanted != lastFace)
                {
                    lastFace = wanted;
                    Raise();
                }

                IFloorGlyphSource chosen = For(wanted);

                return chosen != null && chosen.Available ? chosen : GameFont;
            }
        }

        /// <summary>The source for one named face, whether or not it is the chosen one. For the preview.</summary>
        internal static IFloorGlyphSource For(FloorLabelFace face)
        {
            switch (face)
            {
                case FloorLabelFace.OswaldBold: return Oswald;
                case FloorLabelFace.HammersmithOne: return Hammersmith;
                default: return GameFont;
            }
        }

        internal static bool Available => Source.Available;

        /// <summary>The unit a mesh is built in: the size the active face's glyphs were measured at.</summary>
        internal static float EmSize => Source.EmSize;

        internal static void Request(string text)
        {
            Source.Request(text);
        }

        internal static bool TryGlyph(char c, out FloorGlyph glyph)
        {
            return Source.TryGlyph(c, out glyph);
        }

        /// <summary>
        /// Render queue the label materials are forced into, which is what puts them under the colony.
        ///
        /// <b>The altitude cannot do this on its own, and that is why the first attempt failed.</b> The material is
        /// cloned from Unity's own font material, whose shader is <c>ZTest Always</c> in the Transparent queue
        /// (3000). Ignoring the depth buffer means no <c>AltitudeLayer</c> can put it behind anything, and being in
        /// a later queue than RimWorld's things means it is drawn after them regardless. Aaron reported labels
        /// still over the furniture on 2026-08-21 after the layer alone was changed.
        ///
        /// 2200 sits between Unity's Geometry (2000), where RimWorld's terrain and floors draw, and AlphaTest
        /// (2450), where its Cutout shaders draw every building, item and pawn. So the label lands on the floor and
        /// everything standing on the floor lands on the label.
        /// </summary>
        internal const int LabelQueue = 2200;

        internal static Material MaterialFor(Color color)
        {
            return Source.MaterialFor(color);
        }

        internal static void Raise()
        {
            Action invalidated = Invalidated;

            if (invalidated != null)
                invalidated();
        }

        /// <summary>
        /// RimWorld's own font, as a glyph source.
        ///
        /// <b>Kept as an option and as the fallback,</b> for anybody who would rather the floor matched the
        /// interface, and because it works when a shipped file does not.
        ///
        /// <b>Its atlas rebuilds without warning and that must be watched.</b> Unity rebuilds a dynamic font's
        /// texture whenever anything in the game asks for a glyph size that does not fit, so the trigger is
        /// usually unrelated code. Every UV taken before a rebuild is wrong after it, and the symptom is labels
        /// turning into other people's letters.
        /// </summary>
        private sealed class GameFontSource : IFloorGlyphSource
        {
            /// <summary>Pixel size glyphs are requested at. Resolution, not display size.</summary>
            private const int Size = 32;

            private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

            private Font font;
            private bool resolved;
            private bool listening;

            public bool Available => Resolve() != null;

            public float EmSize => Size;

            public Texture Texture
            {
                get
                {
                    Font resolvedFont = Resolve();

                    return resolvedFont == null || resolvedFont.material == null
                        ? null
                        : resolvedFont.material.mainTexture;
                }
            }

            public void Request(string text)
            {
                Font resolvedFont = Resolve();

                if (resolvedFont != null && !text.NullOrEmpty())
                    resolvedFont.RequestCharactersInTexture(text, Size);
            }

            public bool TryGlyph(char c, out FloorGlyph glyph)
            {
                glyph = default(FloorGlyph);

                Font resolvedFont = Resolve();
                CharacterInfo info;

                if (resolvedFont == null || !resolvedFont.GetCharacterInfo(c, out info, Size))
                    return false;

                glyph = new FloorGlyph
                {
                    MinX = info.minX,
                    MaxX = info.maxX,
                    MinY = info.minY,
                    MaxY = info.maxY,
                    Advance = info.advance,
                    UvBottomLeft = info.uvBottomLeft,
                    UvBottomRight = info.uvBottomRight,
                    UvTopLeft = info.uvTopLeft,
                    UvTopRight = info.uvTopRight
                };

                return true;
            }

            /// <summary>
            /// A tinted copy of the font's own material.
            ///
            /// <b>Copied from the font rather than built on a map shader,</b> because a dynamic atlas keeps the
            /// glyph in alpha and leaves the color channels black -- so a shader that multiplies texture RGB by a
            /// tint produces black text whatever tint it is handed. The font's material carries the text shader,
            /// which takes its color from <c>_Color</c> instead. Copied rather than mutated: that material draws
            /// every label in the game.
            /// </summary>
            public Material MaterialFor(Color color)
            {
                Font resolvedFont = Resolve();

                if (resolvedFont == null || resolvedFont.material == null)
                    return null;

                Material existing;

                if (materials.TryGetValue(color, out existing) && existing != null)
                    return existing;

                Material made = UIGuard.Try("FloorLabels.GameFontMaterial", () =>
                {
                    Material material = new Material(resolvedFont.material);

                    material.color = color;
                    material.renderQueue = LabelQueue;

                    return material;
                }, null, null);

                if (made != null)
                    materials[color] = made;

                return made;
            }

            private Font Resolve()
            {
                if (resolved)
                    return font;

                resolved = true;

                font = UIGuard.Try("FloorLabels.ResolveGameFont", () =>
                {
                    // fontStyles[1] is Small. Indexed rather than Text.CurFontStyle, which follows whatever the
                    // current draw happens to have set.
                    if (Text.fontStyles != null && Text.fontStyles.Length > 1 && Text.fontStyles[1] != null
                        && Text.fontStyles[1].font != null)
                        return Text.fontStyles[1].font;

                    return GUI.skin == null ? null : GUI.skin.font;
                }, null, null);

                if (font != null)
                    Listen();

                return font;
            }

            private void Listen()
            {
                if (listening)
                    return;

                listening = true;

                Font.textureRebuilt += rebuilt =>
                {
                    if (rebuilt != font)
                        return;

                    // The texture object itself can be replaced, so the tinted copies are dropped rather than
                    // repointed. There are at most a handful.
                    foreach (KeyValuePair<Color, Material> pair in materials)
                    {
                        if (pair.Value != null)
                            UnityEngine.Object.Destroy(pair.Value);
                    }

                    materials.Clear();

                    Raise();
                };
            }
        }
    }
}
