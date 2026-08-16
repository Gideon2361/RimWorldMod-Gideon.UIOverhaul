using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// A short word set in a solid rounded badge: ERROR, WARN, 911.
    ///
    /// <b>What makes a tag read as a tag,</b> learned by getting it wrong twice in the loading console. The
    /// first attempt was a fixed-width square fill behind text set at the row's own size, which is
    /// indistinguishable from a selection highlight. The second tinted the interior and set the word in the
    /// same hue, which at this size is barely legible and reads as smudged rather than labelled. What works is
    /// a solid fill in the meaning color, near black text on top of it, and a box sized to the word rather
    /// than padded out to a constant.
    ///
    /// <b>A control rather than a copied idiom.</b> Three unrelated panels draw these now, and a badge that
    /// looked slightly different in each would read as three designers rather than one product. Anything that
    /// needs to flag a row this way calls here; adjusting the look happens once.
    ///
    /// Stateless and static, for the same reason <see cref="UIProgressBarControl"/> is: a badge has nothing to
    /// remember between frames.
    /// </summary>
    public static class UITagControl
    {
        /// <summary>Space either side of the word, inside the badge.</summary>
        private const float SidePadding = 7f;

        /// <summary>
        /// How much shorter than its row a badge sits.
        ///
        /// A badge drawn to the full row height touches its neighbours above and below and stops looking like
        /// an object sitting on the row. Two pixels of air at each end is enough to separate it without
        /// shrinking the text.
        /// </summary>
        private const float VerticalInset = 2f;

        /// <summary>The shortest a badge may be drawn, so a tiny row still gets a legible one.</summary>
        private const float MinHeight = 11f;

        /// <summary>
        /// How wide <see cref="Draw"/> will come out for this text, so a caller can lay out around it.
        ///
        /// Measured at the ambient <c>Text.Font</c>, which is what Draw uses too. Callers that set a font for
        /// the badge must set it before asking, or the answer describes a badge of a different size.
        /// </summary>
        public static float WidthFor(string text)
        {
            return text.NullOrEmpty() ? 0f : Text.CalcSize(text).x + SidePadding * 2f;
        }

        /// <summary>
        /// Draws a badge filling <paramref name="rect"/>.
        /// </summary>
        /// <param name="color">
        /// The meaning color: the badge is filled with it and the text is drawn near black on top. Pass a
        /// palette role rather than a literal, so a theme restating what danger looks like carries here.
        /// </param>
        /// <param name="palette">Palette to draw from. Defaults to the active one.</param>
        public static void Draw(Rect rect, string text, Color color, UIColorPaletteDef palette = null)
        {
            if (text.NullOrEmpty() || rect.width <= 0f || rect.height <= 0f)
                return;

            palette = palette ?? UIColorPaletteDef.Active;

            Rect badge = new Rect(rect.x, rect.y + VerticalInset, rect.width,
                Mathf.Max(MinHeight, rect.height - VerticalInset * 2f));

            UIElementPainter.FillRounded(badge, color);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleCenter;

            // The window's own background, not black. On a solid accent fill this is the highest contrast the
            // theme actually contains, and it is the idiom the selected tab already uses, so a badge and a
            // selected tab read as the same kind of object rather than as two opinions.
            GUI.color = palette.WindowBackground;

            Widgets.Label(badge, text);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
        }

        /// <summary>
        /// Draws a badge at the left of <paramref name="rect"/>, sized to its text.
        ///
        /// <b>The common case, and the one worth having a method for:</b> a badge followed by a label on one
        /// row. Returns where the label should start, so the caller never computes the badge's width itself
        /// and the two cannot drift apart.
        /// </summary>
        /// <param name="gap">Space between the badge and whatever follows it.</param>
        /// <returns>The x the following text should begin at. Unchanged when there is no badge.</returns>
        public static float DrawLeading(Rect rect, string text, Color color, UIColorPaletteDef palette = null,
            float gap = 7f)
        {
            if (text.NullOrEmpty())
                return rect.x;

            float width = WidthFor(text);

            Draw(new Rect(rect.x, rect.y, width, rect.height), text, color, palette);

            return rect.x + width + gap;
        }
    }
}
