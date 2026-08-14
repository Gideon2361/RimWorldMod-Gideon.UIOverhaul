using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// The card every notification in this mod is drawn as: a colored left edge, a background, an optional icon and
    /// one or two lines of text.
    ///
    /// <b>One control for five surfaces, which is the whole reason it is in the framework.</b> Messages, alerts,
    /// letters, the information panel and the calendar all say "something wants your attention", and RimWorld draws
    /// each of them in a different style in a different corner. Replacing them one at a time with five bespoke
    /// implementations would reproduce that inconsistency in our own colors -- so the shape lives here once, and each
    /// surface supplies content and a color role.
    ///
    /// <b>The left edge carries the meaning.</b> Color is the fastest thing to read and the only part of a card that
    /// can be read without reading, so the tone -- threat, setback, task, good news -- goes on a solid bar at the
    /// leading edge rather than into the text color. Text stays at full contrast, which matters more than tone: a
    /// notification tinted red is harder to read at the moment it matters most.
    ///
    /// <b>Alpha is a parameter, not a state.</b> Every one of these surfaces fades something: a message expiring, an
    /// alert draining, a letter pulsing. The caller owns the timing, because the timing is vanilla's and differs per
    /// surface; this only applies it.
    /// </summary>
    public class UINotificationCard
    {
        /// <summary>Width of the colored edge. Enough to read as a deliberate bar rather than as a border.</summary>
        public float EdgeWidth = 3f;

        /// <summary>Inset between the edge and the content.</summary>
        public float ContentInset = 7f;

        /// <summary>Square icon slot at the leading edge of the content, or zero for no icon at all.</summary>
        public float IconSize = 0f;

        public float IconGap = 6f;

        /// <summary>Vertical padding above and below the text.</summary>
        public float VerticalPad = 3f;

        /// <summary>
        /// How solid the card's own background is over the map, before the caller's alpha.
        ///
        /// Not fully opaque, because these draw over a live map the player is often watching while the notification
        /// is what they are reading. Vanilla's message background is 0.8 over a near-black, which is the value this
        /// starts from.
        /// </summary>
        public float BackgroundAlpha = 0.82f;

        /// <summary>
        /// Draws the chrome and returns the rect the caller should put its content in.
        ///
        /// Split that way rather than taking a text argument, because the surfaces disagree about content in ways a
        /// parameter list would not survive: an alert has a label and a count, a letter has an icon and a pulse, a
        /// message has one line that may be clipped. What they agree on is the frame around it.
        /// </summary>
        public Rect DrawChrome(Rect card, UIColorPaletteDef palette, Color edgeColor, float alpha,
            bool hovered = false)
        {
            Color previous = GUI.color;

            // Every color here is multiplied by the caller's alpha rather than set through GUI.color alone, so a
            // fading card fades as one thing. Setting GUI.color once and letting Widgets multiply would work for the
            // solid fills and quietly not work for the ones that already carry alpha of their own.
            Widgets.DrawBoxSolid(card, Fade(palette.PanelBackground, BackgroundAlpha * alpha));

            Widgets.DrawBoxSolid(new Rect(card.x, card.y, EdgeWidth, card.height), Fade(edgeColor, alpha));

            if (hovered)
                Widgets.DrawBoxSolid(card, Fade(palette.HoverOverlay, alpha));

            // A hairline in the border role, which is what stops a stack of cards from reading as one block on a
            // busy map. Drawn after the hover wash so hovering does not wash out the card's own outline.
            GUI.color = Fade(palette.Border, alpha);
            Widgets.DrawBox(card, 1);

            GUI.color = previous;

            float x = card.x + EdgeWidth + ContentInset;

            if (IconSize > 0f)
                x += IconSize + IconGap;

            return new Rect(x, card.y + VerticalPad, card.xMax - ContentInset - x,
                card.height - VerticalPad * 2f);
        }

        /// <summary>Where the icon goes, for a caller that has one. Vertically centered on the whole card.</summary>
        public Rect IconRect(Rect card)
        {
            return new Rect(card.x + EdgeWidth + ContentInset, card.y + (card.height - IconSize) * 0.5f,
                IconSize, IconSize);
        }

        /// <summary>
        /// How tall a card has to be to hold <paramref name="lines"/> lines at the current font.
        ///
        /// Measured from <c>Text.LineHeight</c> rather than from a constant for the reason the work tab's skill
        /// readout is: the font is a request rather than a result -- <c>Tiny</c> silently becomes <c>Small</c> for a
        /// language that cannot be tiny, on the Steam Deck, and during a long event -- so a height tuned for one
        /// clips the other. Callers set the font before asking.
        /// </summary>
        public float HeightFor(int lines)
        {
            return Text.LineHeight * Mathf.Max(1, lines) + VerticalPad * 2f;
        }

        private static Color Fade(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(alpha));
        }
    }
}
