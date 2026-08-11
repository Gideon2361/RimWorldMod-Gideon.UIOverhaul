using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones.Patches
{
    [HarmonyPatch(typeof (Designator_ZoneAdd_Growing), "MakeNewZone")]
    public static class PatchDesignatorZoneAddGrowing
    {
        private static bool Prefix(ref Zone __result)
        {
            PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.GrowingFood, KnowledgeAmount.Total);
            __result = new Zone_GrowingPlus(Find.CurrentMap.zoneManager);
            return false;
        }
    }
}