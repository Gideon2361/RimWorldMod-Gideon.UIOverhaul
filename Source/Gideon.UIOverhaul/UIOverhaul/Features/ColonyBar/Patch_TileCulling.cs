using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.ColonyBar
{
    /// <summary>
    /// Makes RimWorld draw the small patches of map that the live colonist tiles need.
    ///
    /// <b>Two patches rather than one, because the widening has to be scoped.</b> Both of the cullers that matter
    /// read <c>Find.CameraDriver.CurrentViewRect</c>: <c>MapDrawer.DrawMapMesh</c> for terrain sections and
    /// <c>DynamicDrawManager.ComputeCulledThings</c> for things. Widening that property outright would hand a
    /// wrong answer to everything else that reads it, so instead a flag is raised for the duration of
    /// <c>Map.MapUpdate</c> -- which is where both of those calls live -- and the property only answers wider
    /// while it is up.
    ///
    /// <b>This is the mechanism vanilla uses for the same problem.</b> Both cullers already encapsulate
    /// <c>WorldComponent_GravshipController.GravshipRenderBounds</c> when a gravship render is in progress. That
    /// flag is not reused here, because setting it would claim a gravship render is happening and other code
    /// believes it; this is the same shape with its own flag.
    ///
    /// <b>Cost is bounded by the setting, not by the colony.</b> Only the regions chosen for this frame are added,
    /// and how many that is comes from the refresh interval, so widening never grows with headcount. With the live
    /// view off, <see cref="PawnTileView.PendingRegions"/> is empty and both patches fall through immediately.
    /// </summary>
    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    public static class Patch_MapUpdate_TileRegions
    {
        /// <summary>Raised only while the drawn map is updating, which is the only window the cullers run in.</summary>
        internal static bool Drawing;

        public static void Prefix(Map __instance)
        {
            try
            {
                Drawing = PawnTileView.Enabled
                          && __instance != null
                          && __instance == Find.CurrentMap
                          && PawnTileView.PendingRegions.Count > 0;
            }
            catch (Exception ex)
            {
                Drawing = false;

                UIGuard.Report("Bar.TileCullingEnter", ex, null);
            }
        }

        /// <summary>
        /// Lowers the flag however the update ended.
        ///
        /// A finalizer rather than a postfix: a postfix does not run when the body throws, and a flag left raised
        /// would widen the view rect for every reader for the rest of the session.
        /// </summary>
        public static void Finalizer()
        {
            Drawing = false;
        }
    }

    /// <summary>
    /// Widens the camera's view rect to take in the live tile regions, while and only while the drawn map is
    /// updating.
    ///
    /// <b>Encapsulate rather than replace,</b> so the player's own view is always still included: the regions are
    /// added to what the camera can see, never substituted for it.
    /// </summary>
    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.CurrentViewRect), MethodType.Getter)]
    public static class Patch_CameraDriver_CurrentViewRect
    {
        public static void Postfix(ref CellRect __result)
        {
            if (!Patch_MapUpdate_TileRegions.Drawing)
                return;

            try
            {
                List<CellRect> regions = PawnTileView.PendingRegions;

                for (int i = 0; i < regions.Count; i++)
                    __result = __result.Encapsulate(regions[i]);
            }
            catch (Exception ex)
            {
                // Reported once and then left alone: this runs inside the draw loop, so a repeating report would
                // be thousands of lines. The tiles simply stay blank and fall back to portraits.
                Patch_MapUpdate_TileRegions.Drawing = false;

                UIGuard.Report("Bar.TileCulling", ex, null);
            }
        }
    }
}
