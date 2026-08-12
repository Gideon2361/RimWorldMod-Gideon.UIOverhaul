using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// A radio button in the theme, in place of <c>Widgets.RadioButtonLabeled</c>.
    ///
    /// Static for the same reason <see cref="UICheckboxControl"/> is: it holds nothing between frames.
    ///
    /// Unlike a checkbox, it does not change the value it is given. A radio button belongs to a group, and
    /// only the caller knows what else is in that group and what selecting this one should turn off -- so
    /// <see cref="Draw"/> reports the click and leaves the decision where it belongs. That is also what
    /// vanilla's <c>Widgets.RadioButton</c> does.
    ///
    /// <code>
    /// if (UIRadioButtonControl.Draw(rect, mode == Mode.Fast, label: "Fast"))
    ///     mode = Mode.Fast;
    /// </code>
    /// </summary>
    public static class UIRadioButtonControl
    {
        /// <summary>
        /// Diameter of the circle. Vanilla's <c>Widgets.RadioButtonSize</c>, so a row of ours lines up with a
        /// row of theirs down the same edge.
        /// </summary>
        public const float ButtonSize = 24f;

        private const float LabelGap = 8f;
        private const float EdgePad = 4f;

        /// <summary>
        /// Draws a radio button and reports whether it was clicked.
        ///
        /// The whole row is the hit target, as it is in vanilla, so the label is as clickable as the circle.
        /// </summary>
        /// <param name="selected">Whether this option is the chosen one. Never written to.</param>
        /// <param name="label">Null or empty draws the circle alone, centered, which is what a grid cell wants.</param>
        /// <param name="side">
        /// Which side the circle sits on. <see cref="UICheckboxSide.Right"/> matches vanilla's labeled radio
        /// button, which puts its circle against the right edge.
        /// </param>
        /// <param name="disabled">Drawn dimmed, and reports no clicks.</param>
        public static bool Draw(Rect rect, bool selected, UIColorPaletteDef palette = null,
            string label = null, string tooltip = null, UICheckboxSide side = UICheckboxSide.Left,
            bool disabled = false)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            bool over = !disabled && Mouse.IsOver(rect);
            bool circleOnly = label.NullOrEmpty();

            if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            float circleX = circleOnly
                ? rect.center.x - ButtonSize * 0.5f
                : side == UICheckboxSide.Right
                    ? rect.xMax - ButtonSize - EdgePad
                    : rect.x + EdgePad;

            Rect circle = new Rect(circleX, rect.y + (rect.height - ButtonSize) * 0.5f,
                ButtonSize, ButtonSize);

            // Hover is passed from the row rather than measured on the circle, because the row is what
            // responds to a click.
            DrawButton(circle, selected, palette, disabled, over);

            if (!circleOnly)
            {
                float labelX = side == UICheckboxSide.Right ? rect.x + EdgePad : circle.xMax + LabelGap;

                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = disabled ? palette.TextDisabled
                    : over || selected ? palette.TextPrimary
                    : palette.TextSecondary;

                Widgets.Label(new Rect(labelX, rect.y, Mathf.Max(0f, rect.width - ButtonSize - EdgePad * 2f
                                                                 - LabelGap), rect.height), label);

                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);

            if (disabled)
            {
                // Consumed rather than left to fall through to whatever is underneath.
                Widgets.ButtonInvisible(rect);
                return false;
            }

            if (!Widgets.ButtonInvisible(rect))
                return false;

            // Tick_Tiny is what vanilla's radio buttons play, unlike a checkbox's on and off pair -- there is
            // no "off" click on a radio button to have a second sound for.
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            return true;
        }

        /// <summary>
        /// The circle alone, with no hit handling, for a caller that already owns the click.
        ///
        /// Drawn by the shared painter rather than here, so this control and the patched vanilla radio
        /// buttons cannot drift apart. See <c>UIElementPainter.PaintRadioButton</c>.
        /// </summary>
        public static void DrawButton(Rect circle, bool selected, UIColorPaletteDef palette = null,
            bool disabled = false, bool over = false)
        {
            UIElementPainter.PaintRadioButton(circle, selected, palette, disabled, over);
        }
    }
}
