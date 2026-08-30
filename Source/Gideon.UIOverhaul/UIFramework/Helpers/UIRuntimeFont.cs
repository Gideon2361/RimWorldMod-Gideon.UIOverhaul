using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Builds a real <c>UnityEngine.Font</c> out of a baked atlas, so IMGUI can set a whole label at once.
    ///
    /// <b>This is the answer to the draw call problem, and it is not a way around IMGUI.</b> Drawing a label
    /// glyph by glyph costs one <c>GUI.DrawTextureWithTexCoords</c> per character because a texture draw is one
    /// quad and nothing batches quads. But IMGUI's text path is not a texture draw: <c>GUI.Label</c> builds one
    /// mesh for the whole string and issues one draw with the font's material. The limit was never IMGUI. It was
    /// that we were handing it rectangles instead of text.
    ///
    /// <b>Unity cannot load a font file at runtime, but it can be handed one glyph at a time.</b> The three
    /// constructors all want a typeface installed on the player's machine, which is what sent this mod to baked
    /// atlases in the first place. <c>Font.characterInfo</c> and <c>Font.material</c> both have setters, though,
    /// and an empty <c>new Font()</c> is not dynamic -- so a sheet baked offline can be poured into one. That is
    /// the same shape a BMFont importer produces, arrived at without an editor or an AssetBundle.
    ///
    /// <b>What Unity gives back in exchange for the mesh:</b> clipping through the GUI clip stack, word wrap,
    /// alignment, and rich text markup. <see cref="Controls.UITextControl"/> gives all four up to draw glyphs
    /// itself, which is why it refuses text carrying markup or newlines. A font does not have to.
    ///
    /// <b>One font per face per size, because a non-dynamic font ignores <c>fontSize</c>.</b> There is nowhere
    /// to put a scale: <c>lineHeight</c>, <c>ascent</c> and <c>fontSize</c> are all get-only, so the only place
    /// a size can be expressed is in the glyph metrics themselves. Each font is therefore built with its
    /// metrics already scaled to the size it will draw at, and the sheet is shared between them -- the cost of a
    /// second size is an array of structs, not a second texture.
    ///
    /// <b>The metrics are scaled and the UVs are not.</b> A glyph baked at 64 pixels and drawn at 18 samples the
    /// sheet down, which is where the edge quality comes from. It is the same bargain the icon canvas makes.
    /// </summary>
    internal static class UIRuntimeFont
    {
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        private static readonly Dictionary<string, GUIStyle> Styles = new Dictionary<string, GUIStyle>();

        /// <summary>
        /// The font for a face at a size, built once and kept for the session.
        ///
        /// Null when the face has no sheet or its sheet would not load, which is the caller's signal to draw the
        /// vanilla way.
        /// </summary>
        internal static Font For(UIFace face, GameFont size)
        {
            UITypefaceAtlas atlas = UIFaces.AtlasFor(face, size);

            if (atlas == null || !atlas.Available)
                return null;

            string key = face + ":" + size;
            Font existing;

            // The null is cached as well as the font. A face that failed to build will fail the same way every
            // frame, and rebuilding a 523 glyph array to find that out is not free.
            if (Fonts.TryGetValue(key, out existing))
                return existing;

            Font built = UIGuard.Try("UIText.BuildFont", () => Build(atlas, size), null, null);

            Fonts[key] = built;

            return built;
        }

        /// <summary>
        /// A style that draws in this face, cached per face, size and anchor.
        ///
        /// Anchor is part of the key because <c>GUIStyle.alignment</c> is state on the style, and a style handed
        /// out and then mutated by one caller would silently realign every other caller sharing it.
        ///
        /// <b>The colour is left to <c>GUI.color</c>,</b> which is where <c>Widgets.Label</c> takes it from too.
        /// A style carrying its own text colour would need one style per colour, and the palette has plenty.
        /// White here means "unmodified", because IMGUI multiplies the two.
        /// </summary>
        internal static GUIStyle StyleFor(UIFace face, GameFont size, TextAnchor anchor, bool wrap,
            FontStyle weight = FontStyle.Normal)
        {
            Font font = For(face, size);

            if (font == null)
                return null;

            string key = face + ":" + size + ":" + anchor + ":" + wrap + ":" + weight;
            GUIStyle existing;

            if (Styles.TryGetValue(key, out existing))
                return existing;

            GUIStyle style = new GUIStyle
            {
                font = font,
                alignment = anchor,
                fontStyle = weight,
                wordWrap = wrap,
                richText = true,
                clipping = TextClipping.Clip
            };

            style.normal.textColor = Color.white;

            // <b>Unity cannot be told where the baseline is, so the text is moved instead.</b> lineHeight and
            // ascent are get-only on Font and both come back zero for one built this way -- confirmed on screen
            // 2026-08-28, where every glyph drew a full ascent too high and sat across the top edge of its rect.
            // With an ascent of zero the mesh generator puts the baseline at the top of the line box, so pushing
            // the content down by the ascent we would have declared lands it where the glyph path puts it.
            //
            // contentOffset rather than padding: padding also changes what CalcSize reports and what wordWrap
            // measures against, and this is a correction to where the ink sits and to nothing else.
            style.contentOffset = new Vector2(0f, AscentOf(face, size));

            Styles[key] = style;

            return style;
        }

        /// <summary>
        /// Top of the line box to the baseline, in the pixels this face draws at.
        ///
        /// The same arithmetic <c>UITextControl</c> does, so the two paths land text on the same line. If they
        /// ever disagree, the spike window shows it as one column sitting above or below the other.
        /// </summary>
        private static float AscentOf(UIFace face, GameFont size)
        {
            UITypefaceAtlas atlas = UIFaces.AtlasFor(face, size);

            if (atlas == null || atlas.LineRatio <= 0f)
                return 0f;

            // Rounded, because it is the offset the whole line is drawn from. A fractional baseline puts every
            // glyph on the sheet half a pixel off the grid at once, which undoes the rounding of the metrics.
            return Mathf.Round(UIFonts.LineHeightOf(size) / atlas.LineRatio * atlas.AscentRatio);
        }

        /// <summary>
        /// Pours a baked sheet into an empty font.
        ///
        /// <b>Sorted by code point.</b> Unity looks a character up in this array, and a sorted array is what
        /// every font asset it has ever loaded gives it. Whether the lookup is a scan or a search is not
        /// documented, so the array is handed over in the order that is safe under either.
        ///
        /// <b><c>size</c> zero and <c>style</c> normal on every glyph.</b> That is what marks an entry as
        /// matching any size and style asked for. A non-zero size would make the font answer only requests for
        /// exactly that size, and IMGUI asks for zero unless a style sets <c>fontSize</c>, which a non-dynamic
        /// font ignores anyway.
        ///
        /// <b>Both objects are marked not to be saved and not to be destroyed.</b> RimWorld loads a scene when
        /// it moves between the main menu and a game, and an ordinary runtime object does not survive that. A
        /// font that quietly died on the way into a save would draw nothing, in a session where the same font
        /// had just worked on the menu.
        /// </summary>
        private static Font Build(UITypefaceAtlas atlas, GameFont size)
        {
            float em = UIFonts.LineHeightOf(size) / atlas.LineRatio;
            float scale = em / atlas.Em;

            if (scale <= 0f)
                return null;

            List<CharacterInfo> table = new List<CharacterInfo>();

            foreach (UITypefaceGlyph glyph in atlas.Glyphs)
            {

                // uv, vert and width are all marked obsolete in favour of uvBottomLeft, minX/maxX/minY/maxY and
                // advance. Three of those replacements are int properties whose setters write to these very
                // fields -- advance is literally "width = value" -- so taking the advice would quantize every
                // metric to a whole pixel. At an 18 pixel em that is a quarter pixel lost per character, which
                // is a whole pixel of drift by the fourth letter of a word. The deprecated fields are the
                // storage; the modern API is a lossy view of it. So we write the storage.
#pragma warning disable 618
                CharacterInfo info = new CharacterInfo
                {
                    index = glyph.Code,
                    size = 0,

                    // <b>This is what makes a bold tag reach a bold glyph.</b> Unity looks a character up by
                    // index and style together, so a sheet carrying all four styles lets its own text generator
                    // switch weight -- which it can do no other way, a font having one material and therefore
                    // one texture. The sheet numbers styles exactly as FontStyle does, so this is a cast rather
                    // than a translation table that could drift from it.
                    style = (FontStyle) glyph.Style,
                    uv = glyph.Uv,

                    // vert is the ink quad against the pen at the baseline, and its height is negative: Unity
                    // reads the top from vert.y and the bottom from vert.y + vert.height. Getting that sign
                    // wrong draws every glyph mirrored about its own baseline, which is the failure to look for
                    // if this ever comes out upside down.
                    //
                    // <b>Every number here is a whole pixel, and at this scale that is what makes it sharp.</b>
                    // The sheets are baked so one texel is one pixel, but a texel grid only lines up with the
                    // pixel grid if the glyph is placed on a whole number too. Left fractional -- a bearing of
                    // 0.99, a maxY of 9.196 -- each letter lands at its own fraction of a pixel and is filtered
                    // differently from the next, which reads as letters sitting at slightly different heights.
                    // Reported and photographed 2026-08-29, after the 1:1 bake had removed the coarser version
                    // of the same fault.
                    //
                    // This is why Unity exposes advance, minX/maxY and glyphWidth as ints: a bitmap font is
                    // integer positioned by design. The float fields were written here instead to avoid
                    // quantizing -- correct while a sheet was being scaled, wrong once it is not, because
                    // quantizing is exactly what aligns it.
                    vert = new Rect(
                        Mathf.Round(glyph.Bearing * scale),
                        Mathf.Round(glyph.MaxY * scale),
                        Mathf.Round(glyph.InkWidth * scale),
                        -Mathf.Round(glyph.InkHeight * scale)),

                    // Rounded with the rest. Tracking quantizes by up to half a pixel per character, which is
                    // the price of every letter landing on the grid -- and it is what every bitmap font pays.
                    width = Mathf.Round(glyph.Advance * scale)
                };
#pragma warning restore 618

                table.Add(info);
            }

            if (table.Count == 0)
                return null;

            table.Sort((a, b) => a.index.CompareTo(b.index));

            Material material = new Material(TextShader())
            {
                mainTexture = atlas.Texture,
                hideFlags = HideFlags.HideAndDontSave
            };

            Font font = new Font
            {
                hideFlags = HideFlags.HideAndDontSave,
                material = material
            };

            font.characterInfo = table.ToArray();

            return font;
        }

        /// <summary>
        /// The shader the font material draws with.
        ///
        /// <b>Unity's own text shader first, and our sheet does not actually need it.</b> That shader exists
        /// because Unity's dynamic atlases carry coverage in alpha and nothing in the colour channels, so it
        /// takes the colour from the vertex and only the alpha from the texture. Ours is baked white with
        /// coverage in alpha, which multiplies to the same answer under any ordinary modulating shader. So the
        /// fallbacks are not a degraded path -- they are a different route to the same pixels.
        ///
        /// Asked for by name rather than assumed, because <c>Shader.Find</c> only sees what the build shipped.
        /// </summary>
        private static Shader TextShader()
        {
            Shader found = Shader.Find("GUI/Text Shader");

            if (found == null)
                found = Shader.Find("UI/Default");

            if (found == null)
                found = Shader.Find("Unlit/Transparent");

            return found != null ? found : ShaderDatabase.Transparent;
        }

        /// <summary>
        /// What the built font actually came out as, for the spike window.
        ///
        /// Reads the properties that cannot be set -- <c>dynamic</c>, <c>lineHeight</c>, <c>ascent</c> -- since
        /// those are what decide whether this approach carries its own vertical layout or needs ours.
        /// </summary>
        internal static string Diagnose(UIFace face, GameFont size)
        {
            UITypefaceAtlas atlas = UIFaces.AtlasFor(face, size);

            if (atlas == null)
                return UIFaces.Named(face) + ": no sheet, drawn by RimWorld.";

            if (!atlas.Available)
                return UIFaces.Named(face) + ": sheet did not load.";

            Font font = For(face, size);

            if (font == null)
                return UIFaces.Named(face) + ": sheet loaded, font would not build.";

            string shader = font.material == null || font.material.shader == null
                ? "no material"
                : font.material.shader.name;

            return string.Format(
                "{0} at {1}: dynamic={2} lineHeight={3} ascent={4} fontSize={5} glyphs={6} hasA={7} shader={8}",
                UIFaces.Named(face), size, font.dynamic, font.lineHeight, font.ascent, font.fontSize,
                font.characterInfo == null ? 0 : font.characterInfo.Length, font.HasCharacter('A'), shader);
        }
    }
}
