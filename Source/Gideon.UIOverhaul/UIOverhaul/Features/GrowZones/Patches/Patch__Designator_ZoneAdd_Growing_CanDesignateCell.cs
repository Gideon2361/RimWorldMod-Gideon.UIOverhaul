using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones.Patches
{
    /// <summary>
    /// Honours the "allow grow zones anywhere" setting when drawing a new growing zone.
    ///
    /// Designator_ZoneAdd_Growing.CanDesignateCell layers a fertility minimum on top of
    /// Designator_ZoneAdd.CanDesignateCell, which in turn defers to the static IsZoneableCell for
    /// fog, the map-edge buffer and things that refuse to overlap zones. IsZoneableCell is shared
    /// with the stockpile and fishing designators, so it cannot be patched without affecting those.
    /// The whole chain is therefore replaced here for growing zones only.
    /// </summary>
    [HarmonyPatch(typeof(Designator_ZoneAdd_Growing), nameof(Designator_ZoneAdd_Growing.CanDesignateCell))]
    public static class PatchDesignatorZoneAddGrowingCanDesignateCell
    {
        private static bool Prefix(Designator_ZoneAdd_Growing __instance, IntVec3 c, ref AcceptanceReport __result)
        {
            if (GrowZonesFeature.Settings == null || !GrowZonesFeature.Settings.allowZonesAnywhere)
                return true;

            AcceptanceReport? report = GzpZonePlacement.CanDesignateAnywhere(__instance, c);
            if (!report.HasValue)
                return true;

            __result = report.Value;
            return false;
        }
    }
}
