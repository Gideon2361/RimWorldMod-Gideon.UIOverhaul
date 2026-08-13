using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.GrowZones.Patches;

/// <summary>
/// Applies the growing zone's own sowing rules: whether a bill has to want the plant before anyone sows it, and
/// whether sowing over an existing crop is allowed.
///
/// <b>Guarded because of the path it is on.</b> This is job assignment, called for every growing cell every time
/// a pawn looks for work. An exception here does not spoil a panel -- it stops a pawn getting a job, every tick,
/// and buries the reason under an error per attempt. Falling back to vanilla's rules leaves the colony working.
/// </summary>
[HarmonyPatch(typeof (WorkGiver_GrowerSow), "JobOnCell")]
public static class PatchWorkGiverGrowerSowJobOnCell
{
    private static bool Prefix(Pawn pawn, IntVec3 c, bool forced, ref Job __result)
    {
        try
        {
            Map map = pawn?.Map;

            // Null for a pawn who is not on a map at all -- in a caravan, or in transit. Vanilla's own null
            // handling is the right answer there rather than a guess of ours.
            if (map?.zoneManager == null)
                return true;

            if (map.zoneManager.ZoneAt(c) is not Zone_GrowingPlus zoneGrowingPlus)
                return true;

            if (zoneGrowingPlus.RequireActiveBillToSow && !zoneGrowingPlus.AnyBillWantsSowing())
                return false;

            if (zoneGrowingPlus.SowOverSown)
                return true;

            Plant plant = c.GetPlant(map);

            // No plant here yet, so there is nothing to sow over.
            if (plant == null)
                return true;

            if (plant.def?.plant == null
                || plant.def.plant.harvestedThingDef == null
                || plant.def.plant.IsTree
                || plant.def.plant.isStump
                || plant.def == ThingDefOf.Plant_Grass)
                return true;

            __result = null;
            return false;
        }
        catch (Exception ex)
        {
            UIGuard.Report("GrowZones.SowJobOnCell", ex);

            // __result is untouched on every path that can throw, so handing the method back to vanilla gives
            // it the same starting state it would have had without this patch.
            return true;
        }
    }
}
