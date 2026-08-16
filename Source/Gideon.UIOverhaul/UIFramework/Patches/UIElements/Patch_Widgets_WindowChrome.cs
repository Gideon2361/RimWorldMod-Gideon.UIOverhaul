using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Repaints window and menu-section backgrounds from the palette.
    ///
    /// Both vanilla methods are the same two steps -- fill from a static color, then a one-pixel border
    /// from another -- so replacing them is exact rather than approximate. Between them they cover almost
    /// every panel in the game: Window draws its background through IWindowDrawing, which lands on
    /// Widgets.DrawWindowBackground, and the inspect pane, its ITabs and most sub-panels use
    /// Widgets.DrawMenuSection.
    ///
    /// <b>Windows keep their border; menu sections do not.</b> This was borderless throughout at first, on the
    /// theory that fills which differ from one another give enough depth on their own. That holds inside a
    /// window, where a panel sits on a window fill it visibly differs from. It does not hold at the outer edge:
    /// a window is drawn over the map, over terrain of no fixed color, and without an edge it bleeds into
    /// whatever happens to be behind it. So the outer plate is outlined and the panels within it are not.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawWindowBackground), typeof(Rect))]
    public static class Patch_Widgets_DrawWindowBackground
    {
        public static bool Prefix(Rect rect)
        {
            return UIElementPainter.Paint(
                () => WindowChrome.Paint(rect, Color.white),
                "Window background");
        }
    }

    /// <summary>
    /// The tinted overload. colorFactor is how vanilla dims a window that is not the front one, so it is
    /// multiplied in rather than dropped -- losing it would make a stack of windows read as one slab.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawWindowBackground), typeof(Rect), typeof(Color))]
    public static class Patch_Widgets_DrawWindowBackgroundTinted
    {
        public static bool Prefix(Rect rect, Color colorFactor)
        {
            return UIElementPainter.Paint(
                () => WindowChrome.Paint(rect, colorFactor),
                "Window background");
        }
    }

    internal static class WindowChrome
    {
        /// <summary>
        /// Fill, then a one-pixel border, which is the shape of what vanilla does here.
        ///
        /// The border takes the tint as well. It is the edge of the same plate, so a background window whose
        /// fill has been dimmed and whose outline has not would have its edge come forward as the rest of it
        /// receded -- the opposite of what dimming a window behind another is for.
        /// </summary>
        internal static void Paint(Rect rect, Color colorFactor)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // A tab is square, and deliberately: it is anchored to the button bar and reads as part of the frame
            // around the game, so rounding its corners would detach it from the bar it grows out of. A dialog
            // floats and is rounded, at the one radius every other surface in this mod uses.
            if (IsTab())
            {
                Widgets.DrawBoxSolid(rect, palette.WindowBackground * colorFactor);

                return;
            }

            // The border and the fill in one pass. DrawBox cannot follow a curve, so the outline is a rounded
            // fill with the background punched out of it.
            UIElementPainter.OutlineRounded(rect, palette.Border * colorFactor,
                palette.WindowBackground * colorFactor);
        }

        /// <summary>
        /// Whether the window being drawn is a main tab rather than a dialog.
        ///
        /// <b>Tabs are not bordered.</b> A dialog floats over the map and needs an edge to separate it from
        /// terrain of no fixed color. A tab is anchored to the button bar and reads as part of the frame around
        /// the game, so an outline around it draws a box around something that is not a box -- and the bar it
        /// hangs from would be left outside its own panel's border.
        ///
        /// <c>currentlyDrawnWindow</c> is assigned immediately before the background is drawn and cleared
        /// immediately after, so it is exactly the window this call belongs to. Guarded because it is reachable
        /// before there is a window stack at all.
        /// </summary>
        private static bool IsTab()
        {
            return UIGuard.Try("Framework.WindowIsTab",
                () => Find.WindowStack?.currentlyDrawnWindow is MainTabWindow, false,
                "A main tab is drawn with a window border around it.");
        }
    }

    /// <summary>
    /// Menu sections: the inspect pane, ITab contents, and the many sub-panels that use the same look.
    ///
    /// Painted with the panel role rather than the window role, so a section still separates itself from
    /// the window it sits in without needing a border to do it.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawMenuSection))]
    public static class Patch_Widgets_DrawMenuSection
    {
        public static bool Prefix(Rect rect)
        {
            return UIElementPainter.Paint(
                () => Widgets.DrawBoxSolid(rect, UIColorPaletteDef.Active.PanelBackground),
                "Menu section");
        }
    }
}
