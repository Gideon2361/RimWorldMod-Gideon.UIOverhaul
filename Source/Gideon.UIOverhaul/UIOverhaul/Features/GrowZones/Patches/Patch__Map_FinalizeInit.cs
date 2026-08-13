using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones.Patches;

/// <summary>
/// Attaches this feature's map component and migrates any legacy growing zones on the map.
///
/// <b>Both halves are guarded, and separately.</b> FinalizeInit is part of loading a save and generating a map, so
/// an escape from here is not a broken feature but a save that will not open. Separately, because they are
/// independent: a migration that fails must still leave the map with its component, and a component that fails to
/// attach must not stop zones already in the save from being converted.
/// </summary>
[HarmonyPatch(typeof (Map), "FinalizeInit")]
public class PatchMapFinalizeInit
{
    private static void Postfix(Map __instance)
    {
        if (__instance == null)
            return;

        UIGuard.Try("GrowZones.AttachMapComponent", () =>
        {
            if (__instance.GetComponent<MapComponentGzp>() == null)
                __instance.components.Add(new MapComponentGzp(__instance));
        });

        // Runs after Zone_GrowingPlus.ExposeData's PostLoadInit, so freshly converted zones are
        // never put through the legacy bill migration.
        UIGuard.Try("GrowZones.ConvertZones", () => GrowingZoneConverter.ConvertAll(__instance));
    }
}
