using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones.Patches;

[HarmonyPatch(typeof (Zone_Growing), "GetZoneAddGizmos")]
internal static class PatchZoneGrowingGetZoneAddGizmos
{
    private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result)
    {
        foreach (Gizmo gizmo in __result)
        {
            if (gizmo is Designator_ZoneAdd_Growing_Expand)
                yield return new DesignatorZoneAddGrowingPlusExpand();
            else
                yield return gizmo;
        }
    }
}