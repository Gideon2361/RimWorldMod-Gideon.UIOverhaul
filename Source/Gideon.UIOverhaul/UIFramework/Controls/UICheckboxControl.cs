using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIFramework.Controls
{
    /// <summary>Which side of the row the box sits on.</summary>
    public enum UICheckboxSide
    {
        /// <summary>Box first, then the label. Lines up with a column of boxes.</summary>
        Left,

        /// <summary>Label first, box against the right edge. Vanilla's settings-page arrangement.</summary>
        Right
    }

    /// <summary>
    /// A checkbox in the theme, in place of <c>Widgets.CheckboxLabeled</c>.
    ///
    /// A static helper rather than an object, unlike <see cref="UICardControl"/> or
    /// <see cref="UIRichButtonControl"/>: a checkbox holds nothing between frames -- its state is the bool the
    /// caller already owns -- so there is nothing for an instance to be for. Same reasoning as
    /// <see cref="UIProgressBarControl"/>.
    ///
    /// The look is a sunken box with an accent border when checked and a filled accent square inside it. Not
    /// vanilla's textures, because those are stock chrome and would be the only piece of it left in a themed
    /// window.
    ///
    /// <code>
    /// if (UICheckboxControl.Draw(rect, ref autoUnsuspend, label: "Auto-unsuspend"))
    ///     Save();
    /// </code>
    /// </summary>
    public static class UICheckboxControl
    {
        /// <summary>Height of the switch itself. The row it sits in may be taller.</summary>
        public const float BoxSize = 20f;

        /// <summary>
        /// Width of the switch, which is no longer square.
        ///
        /// A toggle is drawn instead of a box, so the slot it needs is wider than it is tall. Kept as its own
        /// constant rather than derived at each call site, because every layout that reserves room for one has
        /// to reserve the same amount or the labels beside them stop lining up.
        /// </summary>
        public const float BoxWidth = BoxSize * 2f;

        private const float LabelGap = 8f;
        private const float EdgePad = 4f;

        /// <summary>
        /// Draws a checkbox and reports whether it was just changed.
        ///
        /// The whole row is the hit target, as it is in vanilla, so a label is as clickable as its box.
        /// </summary>
        /// <param name="value">Flipped in place when clicked.</param>
        /// <param name="label">Null or empty draws the box alone, which is what a grid cell wants.</param>
        /// <param name="disabled">Drawn dimmed and reports no changes.</param>
        public static bool Draw(Rect rect, ref bool value, UIColorPaletteDef palette = null,
            string label = null, string tooltip = null, UICheckboxSide side = UICheckboxSide.Left,
            bool disabled = false)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            bool hover = !disabled && Mouse.IsOver(rect);
            bool boxOnly = label.NullOrEmpty();

            if (hover)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            // Centered when there is no label: a bare box in a grid cell should sit in the middle of it
            // rather than hug an edge that only matters when text follows.
            float boxX = boxOnly
                ? rect.center.x - BoxWidth * 0.5f
                : side == UICheckboxSide.Right
                    ? rect.xMax - BoxWidth - EdgePad
                    : rect.x + EdgePad;

            Rect box = new Rect(boxX, rect.y + (rect.height - BoxSize) * 0.5f, BoxWidth, BoxSize);
            DrawBox(box, value, palette, disabled);

            if (!boxOnly)
            {
                float labelX = side == UICheckboxSide.Right ? rect.x + EdgePad : box.xMax + LabelGap;

                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = disabled ? palette.TextDisabled
                    : hover ? palette.TextPrimary
                    : palette.TextSecondary;

                Widgets.Label(new Rect(labelX, rect.y, Mathf.Max(0f, rect.width - BoxWidth - EdgePad * 2f
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

            value = !value;
            (value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            return true;
        }

        /// <summary>
        /// The box alone, with no hit handling.
        ///
        /// For a caller that owns the click already -- a grid cell whose whole rect does something larger, or
        /// a read-only indicator standing in for a state the player changes elsewhere.
        /// </summary>
        public static void DrawBox(Rect box, bool value, UIColorPaletteDef palette = null,
            bool disabled = false)
        {
            DrawBox(box, value ? MultiCheckboxState.On : MultiCheckboxState.Off, palette, disabled);
        }

        /// <summary>
        /// The box in any of vanilla's three states, including <c>Partial</c>.
        ///
        /// Here because the shared painter has to draw a partial box for the tri-state checkboxes in thing
        /// filter trees, and a control that could not ask for the same thing would leave our own windows
        /// unable to show a state the rest of the game can.
        /// </summary>
        public static void DrawBox(Rect box, MultiCheckboxState state, UIColorPaletteDef palette = null,
            bool disabled = false)
        {
            // Drawn by the shared painter rather than here, so this control and the patched vanilla
            // checkboxes cannot drift apart. See UIElementPainter.PaintCheckbox.
            UIElementPainter.PaintCheckbox(box, state, palette, disabled);
        }
    }
}
