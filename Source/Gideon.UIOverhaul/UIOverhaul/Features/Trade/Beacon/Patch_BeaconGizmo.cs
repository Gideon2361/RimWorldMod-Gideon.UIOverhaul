using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade.Beacon
{
    /// <summary>
    /// Puts a button on a selected trade beacon that opens the reach readout.
    ///
    /// <b>A gizmo rather than an inspect-pane block,</b> because what it opens is a window with a table and a
    /// slider in it. The pane has room for a few facts about the selected thing; this is a screen.
    ///
    /// <b>Appended rather than substituted.</b> Vanilla's own beacon gizmo -- the one that lays a matching
    /// stockpile over the reach -- is left exactly where it was, and so is anything another mod has added. This
    /// is the only one of the four trade screens that takes nothing away.
    ///
    /// <b>Everything a beacon is doing is already true whether this is on or off.</b> The window reads; it does
    /// not change what a beacon reaches. The radius slider on its footer is the same setting as the one on the
    /// options page, put there because that is the one place its effect is visible.
    /// </summary>
    [HarmonyPatch(typeof(Building_OrbitalTradeBeacon), nameof(Building_OrbitalTradeBeacon.GetGizmos))]
    internal static class Patch_BeaconGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Building_OrbitalTradeBeacon __instance)
        {
            foreach (Gizmo gizmo in gizmos)
                yield return gizmo;

            // Built outside the guard's lambda and tested afterwards, because an iterator method cannot yield
            // from inside one. A failure here costs the button and leaves every vanilla gizmo above it intact.
            Gizmo ours = Build(__instance);

            if (ours != null)
                yield return ours;
        }

        private static Gizmo Build(Building_OrbitalTradeBeacon beacon)
        {
            return UIGuard.Try<Gizmo>("Trade.BeaconGizmo", () =>
            {
                if (!TradeWindowSettings.BeaconReadout || beacon == null || !beacon.Spawned)
                    return null;

                Command_Action command = new Command_Action
                {
                    defaultLabel = "Reach",
                    defaultDesc = "Show what this beacon covers, what it can sell and what that is worth, what "
                                  + "is inside the ring but walled off from it, and how close its cell walk is "
                                  + "to the region limit it stops at.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/SellableItems", false),
                    action = () => Find.WindowStack.Add(new Dialog_UIBeacon(beacon))
                };

                return command;
            }, null, null);
        }
    }
}
