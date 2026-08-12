using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Architect
{
    /// <summary>
    /// Hands the architect window over to <see cref="ArchitectPanel"/>.
    ///
    /// Three seams. DoWindowContents is the whole of what vanilla draws inside the window, so replacing
    /// it replaces the category list. WinHeight is the property everything else derives from -- the
    /// requested tab size, PaneTopY, the info box rect -- so reporting our height there keeps the rest of
    /// the UI aware of how much of the screen the architect now covers. RequestedTabSize supplies the
    /// width, which vanilla takes from a static field rather than a property.
    ///
    /// Every patch is off once <see cref="Failed"/> is set, and it is set the first time drawing throws.
    /// The architect is not an optional part of the game; a broken one has to fall back to something that
    /// works, and vanilla's is right there.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Architect))]
    public static class Patch_MainTabWindow_Architect
    {
        /// <summary>Set once drawing has thrown, after which vanilla's architect comes back for good.</summary>
        public static bool Failed { get; private set; }

        [HarmonyPatch("DoWindowContents")]
        [HarmonyPrefix]
        public static bool DoWindowContents(MainTabWindow_Architect __instance, Rect inRect)
        {
            if (Failed)
                return true;

            try
            {
                ArchitectPanel.Draw(__instance, inRect);
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Gideon.UIOverhaul] The architect tab failed to draw; falling back to the "
                              + "vanilla architect.\n" + ex, 0x17C0_10C1);
                Failed = true;
                return true;
            }
        }

        [HarmonyPatch("get_WinHeight")]
        [HarmonyPostfix]
        public static void WinHeight(ref float __result)
        {
            if (!Failed)
                __result = ArchitectPanel.WindowHeight;
        }

        [HarmonyPatch("get_RequestedTabSize")]
        [HarmonyPostfix]
        public static void RequestedTabSize(ref Vector2 __result)
        {
            if (!Failed)
                __result = new Vector2(ArchitectPanel.WindowWidth, ArchitectPanel.WindowHeight);
        }
    }

    /// <summary>
    /// Stops vanilla drawing the designator grid across the bottom of the screen, now that the
    /// designators are inside the window.
    ///
    /// This method is the whole of what the architect draws outside its window: the gizmo grid, the info
    /// box under it, and the selected designator's extra controls. The first two have moved into the
    /// window. The third has not -- a designator's extra controls draw their own panel in screen space,
    /// above the architect, and belong there -- so it is the one thing this prefix still does, using the
    /// same position vanilla did. PaneTopY follows the patched WinHeight, so it lands just above the
    /// window at whatever size that now is.
    /// </summary>
    [HarmonyPatch(typeof(ArchitectCategoryTab), "DesignationTabOnGUI")]
    public static class Patch_ArchitectCategoryTab_DesignationTabOnGUI
    {
        private const float ExtraControlsInset = 10f;

        public static bool Prefix()
        {
            if (Patch_MainTabWindow_Architect.Failed)
                return true;

            Designator selected = Find.DesignatorManager?.SelectedDesignator;
            MainTabWindow_Architect window = MainButtonDefOf.Architect.TabWindow as MainTabWindow_Architect;

            if (selected != null && window != null)
                selected.DoExtraGuiControls(ExtraControlsInset, window.PaneTopY);

            return false;
        }
    }
}
