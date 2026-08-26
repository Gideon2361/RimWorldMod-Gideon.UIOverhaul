using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// The mod's button. Every one of them, in every window.
    ///
    /// <b>One primary button per window, and it is the window's purpose.</b> Accept, Send, Add bill. The fill is
    /// what tells a player which control finishes the job they opened the window to do, so a second filled button
    /// on the same row does not emphasize twice -- it makes the emphasis mean nothing. Everything else is an
    /// outline, and outlines are peers of each other.
    ///
    /// <b>Hover is the accent border, on every variant without exception.</b> This is the part that took three
    /// goes to get right. The mod grew four button implementations -- a flat gray one, a rounded accent one, the
    /// tab strip's, and twenty-odd hand rolled copies -- and they disagreed about hover: some lifted the fill,
    /// some changed the border to a dimmed accent, the tab strip's never touched its border at all, and the bills
    /// toolbar's filter chips did not react to the mouse in any way. Aaron reported the last two on 2026-08-25
    /// with a screenshot of each. A button that does not answer the pointer reads as a label, and a set of
    /// buttons that answer differently reads as several products.
    ///
    /// <b>So the rule is one line and admits no exceptions:</b> pointer over the rect means the border is
    /// <see cref="UIColorPaletteDef.Accent"/>. An outline button lifts its fill as well, and a filled one washes
    /// its interior while the ring stays crisp, but the border is the constant and it is what a player learns.
    ///
    /// <b>The disabled state is drawn, not skipped.</b> A refusing button that looks identical to a working one
    /// reads as a window that has frozen. The primary drops to the muted accent and the label to the disabled
    /// text color, which keeps the button in place and in shape while saying plainly that it will not go. A
    /// disabled button never hovers, because there is nothing for the pointer to promise.
    /// </summary>
    internal static class UIActionButtonControl
    {
        /// <summary>
        /// Draws a button and reports whether it was clicked this frame. A disabled button never reports one.
        /// </summary>
        /// <param name="palette">Null takes the active one, which is what most call sites want.</param>
        /// <param name="primary">The window's own action, filled in the accent.</param>
        /// <param name="font">Tiny for chips and strips that cannot afford the body size.</param>
        /// <param name="toggled">A filter or mode that is currently on: accent border and accent text, no fill.</param>
        internal static bool Draw(Rect rect, string label, UIColorPaletteDef palette = null, bool primary = false,
            bool enabled = true, GameFont font = GameFont.Small, string tooltip = null, bool toggled = false)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            if (palette == null)
                return false;

            bool over = enabled && Mouse.IsOver(rect);
            bool held = over && Input.GetMouseButton(0);

            Face(rect, palette, primary, enabled, toggled, over, held);
            Label(rect, label, palette, primary, enabled, toggled, font);

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);

            if (!enabled)
                return false;

            if (!Widgets.ButtonInvisible(rect))
                return false;

            SoundDefOf.Click.PlayOneShotOnCamera();

            return true;
        }

        /// <summary>
        /// The same button, taking its flags in the order the mod's old gray button took them.
        ///
        /// <b>This overload is why the sweeps of 2026-08-25 were renames and not a hundred rewrites.</b> Between
        /// the old gray button and the tab strip's, a hundred and one call sites read
        /// <c>Button(rect, label, enabled, primary)</c> and almost none of them had a palette to hand. Giving the
        /// shape a home here converted them without touching an argument list, which is the difference between a
        /// change that can be read and one that has to be trusted.
        /// </summary>
        internal static bool Draw(Rect rect, string label, bool enabled, bool primary = false,
            string tooltip = null)
        {
            return Draw(rect, label, null, primary, enabled, GameFont.Small, tooltip);
        }

        /// <summary>
        /// Fill and border for the state the button is in.
        ///
        /// <b>The hover wash goes inside the border, not over it.</b> Painting it across the whole rect dulls the
        /// accent ring by exactly the amount it brightens the middle, which is the difference between a button
        /// that lights up and one that looks smudged.
        /// </summary>
        private static void Face(Rect rect, UIColorPaletteDef palette, bool primary, bool enabled, bool toggled,
            bool over, bool held)
        {
            if (primary)
            {
                Color fill = enabled ? palette.Accent : palette.AccentMuted;

                // Border and fill are the same color at rest, so there is no visible ring until the wash below
                // lightens the interior and leaves it standing.
                UIElementPainter.OutlineRounded(rect, fill, fill);

                if (held)
                    UIElementPainter.FillRounded(rect.ContractedBy(1f), palette.PressedOverlay);
                else if (over)
                    UIElementPainter.FillRounded(rect.ContractedBy(1f), palette.HoverOverlay);

                return;
            }

            // <b>A palette that ships its own button artwork keeps it.</b> The image is the button, and a rounded
            // outline drawn over a 9-slice authored for RimWorld's own frames would put our corners inside
            // theirs. Only the plain states go this way; a toggled chip is ours to draw either way, because no
            // vanilla button has that state for an atlas to have been authored for.
            if (palette.HasButtonTexture && !toggled)
            {
                UIElementPainter.PaintButton(rect, palette, over, held);

                return;
            }

            Color edge = over || toggled ? palette.Accent : palette.Border;

            Color inside = !enabled
                ? palette.PanelBackground
                : toggled
                    ? palette.AccentMuted
                    : over
                        ? palette.SurfaceRaised
                        : palette.SurfaceSunken;

            UIElementPainter.OutlineRounded(rect, edge, inside);

            if (held)
            {
                UIElementPainter.FillRounded(rect.ContractedBy(1f), palette.PressedOverlay);
            }
            else if (over && toggled)
            {
                // A toggled chip already holds the accent border and the muted accent fill, so the fill swap
                // that answers the pointer on a plain button has nothing left to say here. Without this the
                // chips that are switched on would go back to not reacting -- the same defect, one state along.
                UIElementPainter.FillRounded(rect.ContractedBy(1f), palette.HoverOverlay);
            }
        }

        /// <summary>
        /// The label, ellipsed rather than clipped.
        ///
        /// A centered label too wide for its rect loses both ends and gives no sign that it did: this is what
        /// turned "Default care: herbal medicine or worse" into "ault care: herbal medicine or wo" on the hospital
        /// strip. Routed through <see cref="UIRichText"/> so a label carrying markup keeps it.
        /// </summary>
        private static void Label(Rect rect, string label, UIColorPaletteDef palette, bool primary, bool enabled,
            bool toggled, GameFont font)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = font;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;

                // Near black on the accent fill. The accent is chosen to be bright enough to carry a window's
                // one important control, which is exactly the brightness that light text disappears into.
                GUI.color = !enabled
                    ? palette.TextDisabled
                    : primary
                        ? palette.WindowBackground
                        : toggled
                            ? palette.Accent
                            : palette.TextPrimary;

                UIRichText.Label(rect, label ?? string.Empty);
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
