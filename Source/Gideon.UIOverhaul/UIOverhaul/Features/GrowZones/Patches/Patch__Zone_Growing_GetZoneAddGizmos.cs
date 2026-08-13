using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones.Patches;

/// <summary>
/// Swaps the vanilla expand-zone designator for ours in the growing zone's gizmo row.
///
/// <b>Built eagerly into a list, not written as an iterator.</b> The obvious form of this postfix is a
/// <c>foreach</c> with <c>yield return</c>, and it was written that way. The problem is where the body then runs:
/// an iterator's code does not execute when the postfix is called, it executes while vanilla walks the result,
/// deep inside the inspect pane's gizmo drawing. Nothing at this level can catch that, because there is no frame
/// of ours left on the stack -- and C# will not accept a try/catch around a <c>yield return</c> to put one there.
///
/// Taking the list up front puts the work back inside a stack we own, where it can be guarded. It also settles
/// what the row contains at the moment it is asked for, rather than while it is being drawn.
/// </summary>
[HarmonyPatch(typeof (Zone_Growing), "GetZoneAddGizmos")]
internal static class PatchZoneGrowingGetZoneAddGizmos
{
    private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result)
    {
        if (__result == null)
            return null;

        try
        {
            List<Gizmo> gizmos = new List<Gizmo>();

            foreach (Gizmo gizmo in __result)
            {
                if (gizmo is Designator_ZoneAdd_Growing_Expand)
                    gizmos.Add(new DesignatorZoneAddGrowingPlusExpand());
                else
                    gizmos.Add(gizmo);
            }

            return gizmos;
        }
        catch (Exception ex)
        {
            UIGuard.Report("GrowZones.ZoneAddGizmos", ex);

            // Vanilla's own row, unmodified. The player gets the standard expand designator instead of ours,
            // which is a smaller loss than an inspect pane that throws every frame a zone is selected.
            return __result;
        }
    }
}
