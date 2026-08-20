using HarmonyLib;
using RimWorld;

namespace Gideon.UIOverhaul.Features.Minimap
{
    /// <summary>
    /// Draws the minimap once a frame, over the map and under the windows.
    ///
    /// <b>After the main tabs, which is what puts it in the right layer.</b> RimWorld splits its map interface
    /// into a pass before the tab windows and a pass after them, and a HUD panel belongs in the second: drawn
    /// before them it would be covered by an open tab it is meant to sit beside.
    ///
    /// <b>Hooking here does NOT keep it off the world view, which an earlier version of this note claimed.</b>
    /// The method guards its own body with <c>WorldRendererUtility.DrawingMap</c>, but a postfix runs whether or
    /// not that body did, so the minimap drew over the planet until the widget started checking for itself. The
    /// hook decides the layer; it decides nothing about when there is something to draw.
    ///
    /// <b>A postfix that only calls out.</b> Everything about whether there is anything to draw, and every
    /// guard around drawing it, lives in the widget. This exists to be the hook and nothing else, so the
    /// feature can be read without reading a patch.
    /// </summary>
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_AfterMainTabs))]
    internal static class Patch_MinimapOnGUI
    {
        public static void Postfix()
        {
            MinimapWidget.Draw();
        }
    }
}
