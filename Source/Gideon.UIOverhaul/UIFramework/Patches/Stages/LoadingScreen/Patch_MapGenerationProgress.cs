using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Tells the loading screen that a map is being generated, and how many steps that will take.
    ///
    /// This is the only hook map generation needs. Every generation step is already bracketed by
    /// DeepProfiler, so the labels are arriving at our existing stage hook; what was missing was any way
    /// to know that they were generation steps rather than unrecognized startup phases. With the count in
    /// hand the bar becomes a true fraction, exactly as def processing is during startup.
    ///
    /// GenerateContentsIntoMap rather than GenerateMap: it is the method that receives the step list, and
    /// it is also the one that runs for a map generated into an existing game -- a new settlement, a
    /// quest site -- not just for the first map of a new colony.
    ///
    /// Patching each step instead was not an option. GenStep.Generate is declared on the base class and
    /// overridden by every step, so a patch there would never run for any of them.
    /// </summary>
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    public static class Patch_MapGenerator_GenerateContentsIntoMap
    {
        public static void Prefix(IEnumerable<GenStepWithParams> genStepDefs)
        {
            // Counted rather than trusted to be a list: the parameter is an IEnumerable, and enumerating
            // it here is safe because the collection is already materialized by every caller.
            int count = genStepDefs?.Count() ?? 0;

            UIFramework.Stages.UILoadingScreen.BeginMapGeneration(count);
        }
    }
}
