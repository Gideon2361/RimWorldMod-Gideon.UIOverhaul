using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Every typeface a control may be told to draw in.
    ///
    /// <b>This enum is the whole registry.</b> A control names a face here and never learns where the glyphs
    /// came from. The faces live in the mod's font AssetBundle -- built with the game's own editor version,
    /// carrying the TTFs inside, loaded by RimWorld itself -- so text drawn in them is rasterized by the
    /// engine's FreeType exactly as vanilla text is: hinted at every size, bold and italic from tags, full
    /// coverage with per-glyph fallback to system fonts for anything a face lacks.
    ///
    /// <b>This is the fourth architecture and the survivor.</b> Baked glyph sheets drawn by hand reimplemented
    /// a font engine and showed it; loading a TTF from disk is a stub in the shipped engine; registering one
    /// with the OS is invisible to an engine whose font list is sealed before mod code runs. The bundle is the
    /// same road RimWorld's own fonts travel, which is why it behaves like them. Settled with Aaron 2026-08-30.
    ///
    /// <b><see cref="Game"/> is a real option and the default.</b> It means RimWorld's own text, drawn by
    /// <c>Widgets.Label</c> as it always was, and it is what every face falls back to when the bundle is
    /// missing or a font fails to load.
    /// </summary>
    internal enum UIFace
    {
        /// <summary>RimWorld's own interface font, at whatever size was asked for.</summary>
        Game,

        /// <summary>
        /// Barlow. The upright sibling of the condensed face, at normal width, for a label with room to
        /// breathe. Bold and italic are real files here, not synthesis.
        /// </summary>
        Barlow,

        /// <summary>Barlow Condensed. Narrow, so a long label fits where the game's font would not.</summary>
        BarlowCondensed,

        /// <summary>
        /// Barlow Condensed Thin. Very light; reads at Medium and above, worth checking anywhere smaller.
        ///
        /// Its own face rather than a weight, because <c>FontStyle</c> offers bold and italic and nothing
        /// else: a weight lighter than regular can only be asked for by name.
        /// </summary>
        BarlowCondensedThin,

        /// <summary>Cascadia Mono. Fixed width, so digits and anything tabular line up in a column.</summary>
        CascadiaMono,

        /// <summary>Hammersmith One. Wide and geometric, for a heading that wants to be a heading.</summary>
        HammersmithOne,

        /// <summary>IBM Plex Mono. Fixed width, and quieter than Cascadia at the same size.</summary>
        IBMPlexMono,

        /// <summary>
        /// IBM Plex Sans. The proportional sibling of the mono face, for running text rather than columns.
        /// Bold, italic and bold italic are the family's own drawn faces.
        /// </summary>
        IBMPlexSans,

        /// <summary>Oswald. Condensed and tall; its long line box makes it draw smaller than the others at the
        /// same <c>GameFont</c>, so reach for a larger size with it.</summary>
        Oswald,

        /// <summary>
        /// Source Sans 3. A neutral, wide-coverage sans for running text, where the condensed faces are
        /// working against the reader rather than for them. Bold and italic are real files.
        /// </summary>
        SourceSans3
    }

    /// <summary>
    /// What each <see cref="UIFace"/> is made of: which bundled font serves it, at which weight.
    ///
    /// <b>Real files beat synthesis.</b> A dynamic font can fake bold by emboldening and italic by shearing,
    /// and inline rich text tags get exactly that. But a control that names its weight deserves the letterforms
    /// the type designer drew, so where a weight's own TTF is in the bundle it is used -- Barlow's semibold and
    /// italics, IBM Plex's semibold -- and only the faces with no such file fall back to synthesis.
    /// </summary>
    internal static class UIFaces
    {
        /// <summary>
        /// The bundled font for a face at a weight, and what style the renderer must still apply.
        ///
        /// <paramref name="synthesize"/> comes back <c>Normal</c> when the returned font already is the asked
        /// weight, and the asked weight when the face has no file for it and FreeType has to fake it.
        /// </summary>
        internal static Font FontFor(UIFace face, FontStyle weight, out FontStyle synthesize)
        {
            string asset = AssetFor(face, weight, out synthesize);

            return asset == null ? null : UIBundledFonts.Get(asset);
        }

        private static string AssetFor(UIFace face, FontStyle weight, out FontStyle synthesize)
        {
            synthesize = FontStyle.Normal;

            switch (face)
            {
                case UIFace.Barlow:
                    switch (weight)
                    {
                        case FontStyle.Bold: return "Barlow-SemiBold";
                        case FontStyle.Italic: return "Barlow-Italic";
                        case FontStyle.BoldAndItalic: return "Barlow-SemiBoldItalic";
                        default: return "Barlow-Regular";
                    }

                case UIFace.BarlowCondensed:
                    switch (weight)
                    {
                        case FontStyle.Bold: return "BarlowCondensed-SemiBold";
                        case FontStyle.Italic: return "BarlowCondensed-Italic";
                        case FontStyle.BoldAndItalic: return "BarlowCondensed-SemiBoldItalic";
                        default: return "BarlowCondensed-Regular";
                    }

                case UIFace.BarlowCondensedThin:
                    switch (weight)
                    {
                        case FontStyle.Italic: return "BarlowCondensed-ThinItalic";

                        // A bold thin is a contradiction the family has no file for; emboldened thin is the
                        // nearest true answer.
                        case FontStyle.Bold:
                        case FontStyle.BoldAndItalic:
                            synthesize = weight;

                            return "BarlowCondensed-Thin";

                        default: return "BarlowCondensed-Thin";
                    }

                case UIFace.CascadiaMono:
                    synthesize = weight;

                    return "CascadiaMono-VariableFont_wght";

                case UIFace.HammersmithOne:
                    synthesize = weight;

                    return "HammersmithOne-Regular";

                case UIFace.IBMPlexMono:
                    switch (weight)
                    {
                        case FontStyle.Bold: return "IBMPlexMono-SemiBold";

                        case FontStyle.Italic:
                            synthesize = FontStyle.Italic;

                            return "IBMPlexMono-Regular";

                        case FontStyle.BoldAndItalic:
                            synthesize = FontStyle.Italic;

                            return "IBMPlexMono-SemiBold";

                        default: return "IBMPlexMono-Regular";
                    }

                case UIFace.IBMPlexSans:
                    switch (weight)
                    {
                        case FontStyle.Bold: return "IBMPlexSans-SemiBold";
                        case FontStyle.Italic: return "IBMPlexSans-Italic";
                        case FontStyle.BoldAndItalic: return "IBMPlexSans-SemiBoldItalic";
                        default: return "IBMPlexSans-Regular";
                    }

                case UIFace.SourceSans3:
                    switch (weight)
                    {
                        case FontStyle.Bold: return "SourceSans3-Semibold";
                        case FontStyle.Italic: return "SourceSans3-It";
                        case FontStyle.BoldAndItalic: return "SourceSans3-SemiboldIt";
                        default: return "SourceSans3-Regular";
                    }

                case UIFace.Oswald:
                    synthesize = weight;

                    return "Oswald-VariableFont_wght";

                default: return null;
            }
        }

        /// <summary>
        /// The <c>GUIStyle.fontSize</c> that makes this font occupy one RimWorld line at this interface size.
        ///
        /// <b>Read from the font's own metrics rather than tabulated.</b> The imported font knows its line
        /// height at its import size, so the ratio between them scales any wanted line height to a point size
        /// -- which is how Oswald's tall line box and Barlow's short one both come out occupying the same row.
        /// The lesson of a session of hardcoded ratios: the face is the authority on its own geometry.
        /// </summary>
        internal static int PointSizeFor(Font font, GameFont size)
        {
            float wanted = UIFonts.LineHeightOf(size);

            if (font == null || font.lineHeight <= 0 || font.fontSize <= 0)
                return Mathf.Max(1, Mathf.RoundToInt(wanted * 0.8f));

            return Mathf.Max(1, Mathf.RoundToInt(wanted * font.fontSize / font.lineHeight));
        }

        /// <summary>Whether text asked for in this face will really be drawn in it.</summary>
        internal static bool Available(UIFace face)
        {
            if (face == UIFace.Game)
                return true;

            FontStyle ignored;

            return FontFor(face, FontStyle.Normal, out ignored) != null;
        }

        /// <summary>What to call a face in the interface. Not the enum name, which is an identifier.</summary>
        internal static string Named(UIFace face)
        {
            switch (face)
            {
                case UIFace.Barlow: return "Barlow";
                case UIFace.BarlowCondensed: return "Barlow Condensed";
                case UIFace.BarlowCondensedThin: return "Barlow Condensed Thin";
                case UIFace.CascadiaMono: return "Cascadia Mono";
                case UIFace.HammersmithOne: return "Hammersmith One";
                case UIFace.IBMPlexMono: return "IBM Plex Mono";
                case UIFace.IBMPlexSans: return "IBM Plex Sans";
                case UIFace.Oswald: return "Oswald";
                case UIFace.SourceSans3: return "Source Sans 3";
                default: return "RimWorld";
            }
        }

        /// <summary>
        /// A face from its saved name, falling back to <see cref="UIFace.Game"/>. Unrecognized rather than
        /// invalid: a settings file written by a later version can name a face this one has never heard of.
        /// </summary>
        internal static UIFace Parse(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                foreach (UIFace face in (UIFace[]) System.Enum.GetValues(typeof(UIFace)))
                {
                    if (face.ToString() == name)
                        return face;
                }
            }

            return UIFace.Game;
        }
    }
}
