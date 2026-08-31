using System;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// How tall a line of text will actually be, as opposed to how tall the font you asked for would have been.
    ///
    /// <b>Setting a font is a request, not a result.</b> <c>Text.Font</c>'s setter substitutes <c>Small</c>
    /// whenever <c>Text.TinyFontSupported</c> is false, and that is false in more situations than it sounds:
    ///
    /// <list type="bullet">
    /// <item>a language whose <c>canBeTiny</c> is false, which covers several of them</item>
    /// <item>the "disable tiny text" accessibility preference</item>
    /// <item>the Steam Deck</item>
    /// <item>any draw that happens while a long event is running</item>
    /// </list>
    ///
    /// Small's line box is around half again Tiny's, so a row height tuned for Tiny clips the text for everyone in
    /// that list. And it clips rather than shrinks: <c>Widgets.Label</c> hands the rect to <c>GUI.Label</c> as the
    /// clip rectangle, so a line box taller than its rect loses its ascenders and descenders, and a centered
    /// anchor spends the overflow at both ends -- which is why the symptom is text with its top and bottom shaved
    /// off rather than text that overflows visibly.
    ///
    /// <b>This existed in three places before it existed here.</b> <c>UILoadingScreenControl</c> had a private
    /// version of <see cref="LineHeightOf"/>, <c>WorkPanel</c> reads <c>Text.LineHeight</c> after setting the
    /// font for the same reason, and <c>Dialog_UIOptions</c> sizes its cards for Small while asking for Tiny.
    /// Three correct answers and no shared one is how a fourth consumer gets it wrong, which is exactly what the
    /// calendar widget did.
    ///
    /// <b>When the font is already set, prefer <c>Text.LineHeight</c> directly.</b> That getter indexes by the
    /// current font, so it already reflects whatever substitution the setter made. This type is for sizing a
    /// layout <i>before</i> the font is set, which is the case a constant is usually standing in for.
    /// </summary>
    internal static class UIFonts
    {
        /// <summary>
        /// The font that will really be used if <paramref name="font"/> is requested.
        /// </summary>
        internal static GameFont Effective(GameFont font)
        {
            if (font == GameFont.Tiny && !Text.TinyFontSupported)
                return GameFont.Small;

            return font;
        }

        /// <summary>
        /// Line height for <paramref name="font"/>, accounting for the substitution.
        ///
        /// Rounded up, because a fractional row height leaves a sub-pixel of the line box outside the rect and
        /// clipping does not round in the caller's favor.
        /// </summary>
        internal static float LineHeightOf(GameFont font)
        {
            return UnityEngine.Mathf.Ceil(Text.LineHeightOf(Effective(font)));
        }

        /// <summary>
        /// Pixels to a point, fixed at the same ratio HTML uses: CSS defines 1pt as 4/3 of a px, which is 72
        /// points to 96 pixels per inch.
        ///
        /// <b>A convention, not a measurement.</b> A real point is a physical 1/72 inch, and nothing in this
        /// pipeline knows inches -- the game has a user interface scale on top of whatever DPI the monitor
        /// happens to report. Fixing the ratio is what every browser and word processor does, and it buys the
        /// thing that matters: "14pt" here means what "14pt" means in a word processor.
        /// </summary>
        internal const float PixelsPerPoint = 4f / 3f;

        /// <summary>Points to the pixel em size Unity actually rasterizes at.</summary>
        internal static float ToPixels(float points)
        {
            return points * PixelsPerPoint;
        }

        /// <summary>Unity's pixel em size back to points.</summary>
        internal static float ToPoints(float pixels)
        {
            return pixels / PixelsPerPoint;
        }

        /// <summary>
        /// The point size the game really draws <paramref name="font"/> at, on the HTML scale above.
        ///
        /// Read off the style rather than tabulated, because the three fonts are assets -- Calibri_tiny,
        /// Arial_small and Arial_medium -- and their imported sizes are the game's to change. A GUIStyle whose
        /// own fontSize is zero is deferring to the font, which is how RimWorld builds all three.
        /// </summary>
        internal static float PointsOf(GameFont font)
        {
            GameFont effective = Effective(font);

            GUIStyle style = Text.fontStyles != null && (int) effective < Text.fontStyles.Length
                ? Text.fontStyles[(int) effective]
                : null;

            if (style != null)
            {
                if (style.fontSize > 0)
                    return ToPoints(style.fontSize);

                if (style.font != null && style.font.fontSize > 0)
                    return ToPoints(style.font.fontSize);
            }

            // Only reached before Text has run its static constructor, which is not a frame anything draws in.
            switch (effective)
            {
                case GameFont.Tiny: return ToPoints(10f);
                case GameFont.Medium: return ToPoints(18f);
                default: return ToPoints(12f);
            }
        }

        /// <summary>
        /// The <see cref="GameFont"/> nearest a point size, for text the game font has to draw.
        ///
        /// <b>This is the whole fallback.</b> A bundled face is drawn at whatever size it is given; the game
        /// font comes in three and that is the end of it. So a caller names eleven points once and gets eleven
        /// points in a face that can do it and Tiny in the one that cannot, rather than every call site
        /// carrying two numbers and a rule for choosing between them.
        ///
        /// Ties go to the smaller font. Text a point small still reads; text a point large in a rect measured
        /// for the smaller one is clipped at the top and bottom, which is the failure this file exists over.
        /// </summary>
        internal static GameFont Nearest(float points)
        {
            GameFont best = GameFont.Tiny;
            float closest = float.MaxValue;

            foreach (GameFont font in Enum.GetValues(typeof(GameFont)))
            {
                float distance = Mathf.Abs(PointsOf(font) - points);

                if (distance < closest)
                {
                    closest = distance;
                    best = font;
                }
            }

            return best;
        }

        /// <summary>
        /// A row tall enough for one line of <paramref name="font"/> plus <paramref name="padding"/> above and
        /// below, which is what most single-line rows actually want.
        /// </summary>
        internal static float RowHeight(GameFont font, float padding = 2f)
        {
            return LineHeightOf(font) + padding * 2f;
        }
    }
}
