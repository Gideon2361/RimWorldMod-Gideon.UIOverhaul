using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.GrowZones.Patches;

[HarmonyPatch(typeof (WorkGiver_GrowerSow), "JobOnCell")]
public static class PatchWorkGiverGrowerSowJobOnCell
{
    private static bool Prefix(Pawn pawn, IntVec3 c, bool forced, ref Job __result)
    {
        Map map = pawn.Map;

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

        if (plant.def.plant.harvestedThingDef == null
            || plant.def.plant.IsTree
            || plant.def.plant.isStump
            || plant.def == ThingDefOf.Plant_Grass)
            return true;

        __result = null;
        return false;
    }
}
