using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones.Patches;

[HarmonyPatch(typeof (Map), "FinalizeInit")]
public class PatchMapFinalizeInit
{
    private static void Postfix(Map __instance)
    {
        if (__instance.GetComponent<MapComponentGzp>() == null)
            __instance.components.Add(new MapComponentGzp(__instance));

        // Runs after Zone_GrowingPlus.ExposeData's PostLoadInit, so freshly converted zones are
        // never put through the legacy bill migration.
        GrowingZoneConverter.ConvertAll(__instance);
    }
}
