using System;
using Gideon.UIFramework.Helpers;
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
                UIGuard.Report("Architect.Draw", ex,
                    "The architect falls back to vanilla's for the rest of the session.");
                Failed = true;
                return true;
            }
        }

        /// <summary>
        /// Guarded, like the size below it: both are measured from the designator categories rather than being
        /// constants, and vanilla's own value is the right thing to keep if the measurement fails.
        /// </summary>
        [HarmonyPatch("get_WinHeight")]
        [HarmonyPostfix]
        public static void WinHeight(ref float __result)
        {
            if (Failed)
                return;

            __result = UIGuard.Try("Architect.WinHeight", () => ArchitectPanel.WindowHeight, __result,
                "The architect window uses vanilla's height.");
        }

        [HarmonyPatch("get_RequestedTabSize")]
        [HarmonyPostfix]
        public static void RequestedTabSize(ref Vector2 __result)
        {
            if (Failed)
                return;

            __result = UIGuard.Try("Architect.TabSize",
                () => new Vector2(ArchitectPanel.WindowWidth, ArchitectPanel.WindowHeight), __result,
                "The architect window opens at vanilla's size.");
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

        /// <summary>
        /// Guarded with a fall through to vanilla. The extra controls belong to whichever designator is selected,
        /// ours and every other mod's alike, so this hands off work we do not own -- and a designator that throws
        /// from its own panel must not take the architect down with it.
        ///
        /// Falling back means vanilla draws the full bottom-of-screen grid again, doubling the designators that are
        /// already in the window. Visibly wrong, and still better than a selected designator whose controls have
        /// vanished.
        /// </summary>
        public static bool Prefix()
        {
            if (Patch_MainTabWindow_Architect.Failed)
                return true;

            return UIGuard.Replaced("Architect.ExtraGuiControls", () =>
            {
                Designator selected = Find.DesignatorManager?.SelectedDesignator;
                MainTabWindow_Architect window =
                    MainButtonDefOf.Architect.TabWindow as MainTabWindow_Architect;

                if (selected != null && window != null)
                    selected.DoExtraGuiControls(ExtraControlsInset, window.PaneTopY);
            }, "Vanilla's designator grid is drawn across the bottom of the screen as well as in the window.");
        }
    }
}
