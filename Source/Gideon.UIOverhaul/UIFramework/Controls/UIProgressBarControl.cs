using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// A horizontal progress bar drawn from the active palette.
    ///
    /// Stateless and static: a bar has nothing to remember between frames, and making callers
    /// construct one would buy nothing. Anything that does need state -- an animated fill, say --
    /// belongs in the caller, which can pass the eased value in.
    /// </summary>
    public static class UIProgressBarControl
    {
        /// <summary>
        /// Draws a bar filled to <paramref name="fraction"/> (0 to 1, clamped).
        /// </summary>
        /// <param name="rect">The whole bar, trough included.</param>
        /// <param name="fraction">How full, 0 to 1.</param>
        /// <param name="palette">Palette to draw from. Defaults to the active one.</param>
        /// <param name="fill">Fill color. Defaults to the palette's accent.</param>
        public static void Draw(Rect rect, float fraction, UIColorPaletteDef palette = null, Color? fill = null)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);

            float clamped = Mathf.Clamp01(fraction);
            if (clamped > 0f)
            {
                // Rounded so the fill lands on a whole pixel. A fractional width leaves the leading
                // edge blurred, which is very visible on a bar that creeps forward a pixel at a time.
                float width = Mathf.Round(rect.width * clamped);
                if (width > 0f)
                    Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, width, rect.height), fill ?? palette.Accent);
            }

            Color previous = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previous;
        }

        /// <summary>
        /// As <see cref="Draw"/>, with the percentage centered in the bar. Only worth using on a bar
        /// tall enough for text -- roughly 18px and up.
        ///
        /// Note what the text sits on. Centered in the bar, it straddles both the fill and the trough,
        /// and the boundary between them travels through it as the bar advances. TextPrimary is chosen
        /// to read against a window or panel background, not against Accent, so on some palettes the
        /// number washes out for part of its travel. Pass a fill that keeps its contrast, or put the
        /// figure beside the bar rather than inside it. The loading screen does the latter, by not
        /// showing a percentage at all.
        /// </summary>
        public static void DrawWithPercent(Rect rect, float fraction, UIColorPaletteDef palette = null,
            Color? fill = null)
        {
            palette = palette ?? UIColorPaletteDef.Active;
            Draw(rect, fraction, palette, fill);

            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextPrimary;
            Widgets.Label(rect, Mathf.Clamp01(fraction).ToStringPercent());

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            GUI.color = previousColor;
        }
    }
}
