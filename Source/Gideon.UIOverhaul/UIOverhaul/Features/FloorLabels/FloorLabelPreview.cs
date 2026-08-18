using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// Draws a sample of a typeface in a window, so choosing one is not guesswork.
    ///
    /// <b>The same glyphs the map uses, not an approximation.</b> A preview that rendered the name in RimWorld's
    /// font with a label saying "Oswald" would be worse than none: the whole reason to offer a choice is that the
    /// faces look different, and the only honest way to show that is to draw the real thing.
    ///
    /// <b>Screen space here, world space on the map, from one atlas.</b> The map builds meshes; this walks the
    /// same glyph metrics and blits each one with <c>GUI.DrawTextureWithTexCoords</c>. Two renderers over one
    /// source of truth, which is why a face that previews correctly also draws correctly.
    ///
    /// <b>The game font is drawn with a plain label instead.</b> Its atlas can hold glyphs rotated, so the four
    /// UV corners are not always an upright rectangle and blitting is not safe -- and there is no need, since
    /// drawing RimWorld's font with RimWorld's own label is exactly what it would look like.
    /// </summary>
    internal static class FloorLabelPreview
    {
        /// <summary>
        /// Draws <paramref name="text"/> inside <paramref name="rect"/>, scaled to fit and vertically centered.
        /// </summary>
        internal static void Draw(Rect rect, string text, FloorLabelFace face, UIColorPaletteDef palette)
        {
            if (text.NullOrEmpty() || rect.width < 8f || rect.height < 6f)
                return;

            UIGuard.Try("FloorLabels.Preview", () => Render(rect, text.ToUpperInvariant(), face, palette), null);
        }

        private static void Render(Rect rect, string text, FloorLabelFace face, UIColorPaletteDef palette)
        {
            if (face == FloorLabelFace.GameFont)
            {
                DrawWithGameFont(rect, text, palette);

                return;
            }

            IFloorGlyphSource source = FloorLabelFont.For(face);

            if (source == null || !source.Available || source.Texture == null)
            {
                DrawWithGameFont(rect, text, palette);

                return;
            }

            source.Request(text);

            // Measured before anything is drawn, because the scale depends on the whole string.
            float width = 0f;
            float top = 0f;
            float bottom = 0f;

            foreach (char c in text)
            {
                FloorGlyph glyph;

                if (!source.TryGlyph(c, out glyph))
                    continue;

                width += glyph.Advance;

                if (glyph.MaxY > top)
                    top = glyph.MaxY;

                if (glyph.MinY < bottom)
                    bottom = glyph.MinY;
            }

            if (width <= 0f || top <= bottom)
                return;

            float scale = Mathf.Min(rect.width / width, rect.height / (top - bottom));

            if (scale <= 0f)
                return;

            // Baseline placed so the ink is centered in the rect rather than sitting on its bottom edge.
            float baseline = rect.y + (rect.height + (top + bottom) * scale) * 0.5f;
            float pen = rect.x;

            Color previous = GUI.color;

            GUI.color = palette == null ? Color.white : palette.TextPrimary;

            foreach (char c in text)
            {
                FloorGlyph glyph;

                if (!source.TryGlyph(c, out glyph))
                    continue;

                if (glyph.Drawable)
                {
                    // Screen y grows downward, so the glyph's top edge is the smaller coordinate.
                    Rect quad = new Rect(pen + glyph.MinX * scale, baseline - glyph.MaxY * scale,
                        (glyph.MaxX - glyph.MinX) * scale, (glyph.MaxY - glyph.MinY) * scale);

                    Rect uv = new Rect(glyph.UvBottomLeft.x, glyph.UvBottomLeft.y,
                        glyph.UvBottomRight.x - glyph.UvBottomLeft.x,
                        glyph.UvTopLeft.y - glyph.UvBottomLeft.y);

                    GUI.DrawTextureWithTexCoords(quad, source.Texture, uv);
                }

                pen += glyph.Advance * scale;
            }

            GUI.color = previous;
        }

        private static void DrawWithGameFont(Rect rect, string text, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = palette == null ? Color.white : palette.TextPrimary;

                Widgets.Label(rect, text);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }
    }
}
