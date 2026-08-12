using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Repaints window and menu-section backgrounds from the palette, flat and borderless.
    ///
    /// Both vanilla methods are the same two steps -- fill from a static color, then a one-pixel border
    /// from another -- so replacing them is exact rather than approximate. Between them they cover almost
    /// every panel in the game: Window draws its background through IWindowDrawing, which lands on
    /// Widgets.DrawWindowBackground, and the inspect pane, its ITabs and most sub-panels use
    /// Widgets.DrawMenuSection.
    ///
    /// Borderless on purpose. The border is what makes vanilla's panels read as separate plates; without
    /// it, and with panel fills that differ from the window fill, depth comes from the surfaces themselves
    /// the way the growing-zone windows already do it.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawWindowBackground), typeof(Rect))]
    public static class Patch_Widgets_DrawWindowBackground
    {
        public static bool Prefix(Rect rect)
        {
            return UIElementPainter.Paint(
                () => Widgets.DrawBoxSolid(rect, UIColorPaletteDef.Active.WindowBackground),
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
                () => Widgets.DrawBoxSolid(rect, UIColorPaletteDef.Active.WindowBackground * colorFactor),
                "Window background");
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
