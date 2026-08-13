using System;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Work
{
    /// <summary>
    /// Hands the work tab over to <see cref="WorkPanel"/>.
    ///
    /// Patched on MainTabWindow_Work.DoWindowContents, which is the tab's own override. Patching the base
    /// MainTabWindow_PawnTable instead only intercepts the base call the override makes, so everything the
    /// override draws afterwards survives -- the vanilla manual-priorities checkbox, the "Priority 1 is done
    /// first. Priority 4 is done last." line under it, and the "&lt;= Higher priority" / "Lower priority =&gt;"
    /// hints. All three are wrong for this tab: the range is now 0-9, and <see cref="WorkPanel"/> draws a
    /// themed toggle of its own. Replacing the override drops them without touching any translated string, so
    /// there is nothing to re-translate for every language.
    ///
    /// Drawing is off once <see cref="Failed"/> is set, which happens the first time it throws. The work tab is
    /// not optional, so a broken one falls back to the vanilla table that already worked.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
    public static class Patch_MainTabWindow_Work
    {
        public static bool Failed { get; private set; }

        public static bool Prefix(Rect rect)
        {
            if (Failed)
                return true;

            try
            {
                WorkPanel.Draw(rect);
                return false;
            }
            catch (Exception ex)
            {
                UIGuard.Report("Work.Draw", ex,
                    "The work tab falls back to vanilla's table for the rest of the session.");
                Failed = true;
                return true;
            }
        }
    }

    /// <summary>
    /// Sizes the work tab's window to <see cref="WorkPanel"/>.
    ///
    /// Patched on the base rather than on MainTabWindow_Work, because the work tab does not override the
    /// property -- which is also why the instance is tested here: Assign, Animals, Wildlife and Schedule share
    /// the same base and must keep their own sizes.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), "get_RequestedTabSize")]
    public static class Patch_MainTabWindow_PawnTable_RequestedTabSize
    {
        /// <summary>
        /// Guarded because the size is measured rather than constant, and measuring means reading fonts and def
        /// lists. __result keeps vanilla's value if that fails, so the tab opens at the wrong size instead of not
        /// opening.
        /// </summary>
        public static void Postfix(MainTabWindow_PawnTable __instance, ref Vector2 __result)
        {
            if (Patch_MainTabWindow_Work.Failed || !(__instance is MainTabWindow_Work))
                return;

            __result = UIGuard.Try("Work.TabSize",
                () => new Vector2(WorkPanel.WindowWidth, WorkPanel.WindowHeight), __result,
                "The work tab opens at vanilla's size.");
        }
    }
}
