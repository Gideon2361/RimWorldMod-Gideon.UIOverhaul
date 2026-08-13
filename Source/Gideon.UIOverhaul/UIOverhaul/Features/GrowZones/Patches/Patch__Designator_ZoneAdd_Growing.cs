using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones.Patches
{
    /// <summary>
    /// Makes the growing-zone designator create our zone type instead of the vanilla one.
    ///
    /// Guarded with a fall through to vanilla, which is the one fallback that leaves the designator usable: the
    /// player draws an ordinary growing zone rather than getting nothing at all when they drag out a zone.
    /// </summary>
    [HarmonyPatch(typeof (Designator_ZoneAdd_Growing), "MakeNewZone")]
    public static class PatchDesignatorZoneAddGrowing
    {
        private static bool Prefix(ref Zone __result)
        {
            try
            {
                // Null in the world view and while a map is being swapped. Vanilla reads the same field, so
                // handing the method back is no worse than what it would have done unpatched.
                ZoneManager zones = Find.CurrentMap?.zoneManager;

                if (zones == null)
                    return true;

                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.GrowingFood, KnowledgeAmount.Total);
                __result = new Zone_GrowingPlus(zones);
                return false;
            }
            catch (Exception ex)
            {
                UIGuard.Report("GrowZones.MakeNewZone", ex);
                return true;
            }
        }
    }
}
