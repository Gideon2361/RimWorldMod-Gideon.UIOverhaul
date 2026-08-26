using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// The mod's action button: a rounded fill in the accent for the one thing the window is for, and a rounded
    /// outline for everything beside it.
    ///
    /// <b>One primary button per window, and it is the window's purpose.</b> Accept, Send, Add bill. The fill is
    /// what tells a player which control finishes the job they opened the window to do, so a second filled button
    /// on the same row does not emphasize twice -- it makes the emphasis mean nothing. Everything else is an
    /// outline, and outlines are peers of each other.
    ///
    /// <b>A control rather than a copied idiom.</b> This started in the bills toolbar and the trade footer drew
    /// its own gray boxes, so the mod had two answers to what a button looks like and they were both in the same
    /// screenshot. Anything that needs a button asks here; changing the look happens once. Reported 2026-08-25.
    ///
    /// <b>The disabled state is drawn, not skipped.</b> A refusing button that looks identical to a working one
    /// reads as a window that has frozen. The primary drops to the muted accent and the label to the disabled
    /// text color, which keeps the button in place and in shape while saying plainly that it will not go.
    /// </summary>
    internal static class UIActionButtonControl
    {
        /// <summary>
        /// Draws a button and reports whether it was clicked this frame. A disabled button never reports one.
        /// </summary>
        /// <param name="palette">Null takes the active one, which is what most call sites want.</param>
        internal static bool Draw(Rect rect, string label, UIColorPaletteDef palette = null, bool primary = false,
            bool enabled = true)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            if (palette == null)
                return false;

            bool over = enabled && Mouse.IsOver(rect);
            bool held = over && Input.GetMouseButton(0);

            if (primary)
            {
                UIElementPainter.FillRounded(rect, enabled ? palette.Accent : palette.AccentMuted);

                if (held)
                    UIElementPainter.FillRounded(rect, palette.PressedOverlay);
                else if (over)
                    UIElementPainter.FillRounded(rect, palette.HoverOverlay);
            }
            else if (enabled)
            {
                UIElementPainter.PaintButton(rect, palette, over, held);
            }
            else
            {
                // Not PaintButton with the state flags off: a palette that supplies its own button artwork would
                // draw a picture of a live button and no color we set afterwards could take that back.
                UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;

                // Near black on the accent fill. The accent is chosen to be bright enough to carry a window's
                // one important control, which is exactly the brightness that light text disappears into.
                GUI.color = !enabled
                    ? palette.TextDisabled
                    : primary
                        ? palette.WindowBackground
                        : palette.TextPrimary;

                Widgets.Label(rect, label ?? string.Empty);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!enabled)
                return false;

            if (!Widgets.ButtonInvisible(rect))
                return false;

            SoundDefOf.Click.PlayOneShotOnCamera();

            return true;
        }

        /// <summary>
        /// The same button, taking its two flags in the order the mod's old gray button took them.
        ///
        /// <b>This overload is why the sweep of 2026-08-25 was a rename and not thirty rewrites.</b> Twenty-nine
        /// call sites across bills, grow zones, the colonist bar and the filter templates read
        /// <c>GrayButton(rect, label, enabled, primary)</c>, and almost none of them had a palette to hand. Giving
        /// the shape a home here converted them all without touching a single argument list, which is the
        /// difference between a change that can be read and one that has to be trusted.
        /// </summary>
        internal static bool Draw(Rect rect, string label, bool enabled, bool primary = false)
        {
            return Draw(rect, label, null, primary, enabled);
        }
    }
}
