using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Plants
{
    /// <summary>
    /// Marks a blighted crop for cutting the moment the blight appears.
    ///
    /// <b>Blight is the one crop problem with exactly one answer.</b> A blighted plant yields nothing at all,
    /// which is not a judgement call: <c>Plant.CanYieldNow</c> returns false outright when <c>Blighted</c> is set,
    /// so the plant will never produce again however long it is left. It also spreads to neighbours on a timer.
    /// The player's only move is to cut it, and vanilla makes them do that by hand, one drag at a time, in a field
    /// where the blighted plants are scattered among healthy ones and hard to pick out.
    ///
    /// <b>Patched at <c>Blight.SpawnSetup</c>, which is the one funnel.</b> Blight arrives three ways: the
    /// incident, <c>Plant.CropBlighted</c>, and an existing blight reproducing onto a neighbour within four cells.
    /// All three spawn a <c>Blight</c> thing, so one hook covers them and cannot fall out of step with a fourth.
    ///
    /// <b>Not on load, deliberately.</b> <c>SpawnSetup</c> runs again for every blight in a save when it is
    /// loaded, and marking there would undo a player's own decision: somebody who cancelled the designation to
    /// leave a plant standing would find it marked again on their next load, with nothing to explain it. Blight
    /// that is already on the map when this is switched on is theirs to handle; every blight that appears
    /// afterwards is marked.
    ///
    /// <b>A pending harvest is replaced rather than left beside it.</b> The plant cannot yield, so a harvest
    /// order on it is a colonist walking out to collect nothing, and two designations on one plant is a fight
    /// between two work givers.
    ///
    /// <b>Prevent cutting is honored,</b> which is the one case where the player has already said what they want
    /// done with this plant and it is not this. Vanilla's own cut designator refuses the same plants.
    /// </summary>
    [HarmonyPatch(typeof(Blight), nameof(Blight.SpawnSetup))]
    internal static class Patch_CutBlightedPlants
    {
        public static void Postfix(Blight __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad)
                return;

            UIGuard.Try("Plants.CutBlighted", () => Mark(__instance, map),
                "That blighted plant was not marked for cutting. It can still be cut by hand.");
        }

        private static void Mark(Blight blight, Map map)
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            if (settings == null || !settings.autoCutBlightedPlants)
                return;

            // Somebody else's map is not ours to give orders on, and a designation there would be invisible to
            // the player anyway.
            if (blight == null || map?.designationManager == null || !map.IsPlayerHome)
                return;

            // The blight's own way of finding what it sits on, rather than a cell search of our own: it lands on
            // a plant, and this is the method its Plant property uses.
            Plant plant = BlightUtility.GetFirstBlightableEverPlant(blight.Position, map);

            if (plant == null || plant.def?.plant == null || plant.Destroyed)
                return;

            CompPlantPreventCutting prevent;

            if (plant.TryGetComp(out prevent) && prevent.PreventCutting)
                return;

            if (map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null)
                return;

            Designation harvest = map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant);

            if (harvest != null)
                map.designationManager.RemoveDesignation(harvest);

            map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.CutPlant));
        }
    }
}
