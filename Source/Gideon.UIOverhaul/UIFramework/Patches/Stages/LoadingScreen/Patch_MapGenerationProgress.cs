using System;
using System.Collections.Generic;
using System.Linq;
using Gideon.UIFramework.Helpers;
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
        /// <summary>
        /// <b>The sequence is materialized and handed back, rather than counted where it lies.</b>
        ///
        /// Counting it is the obvious implementation and it is a trap. What arrives is not a list: GenerateMap
        /// builds a deferred chain of Select, Where, Concat and Distinct across the biome's and each tile
        /// mutator's extra and prevented steps, and passes it lazily. Counting that walks the whole chain, and
        /// then vanilla walks it again -- harmless today only because every source at the bottom happens to be a
        /// def List, so the chain is re-enumerable and gives the same answer twice.
        ///
        /// That is a property of the current callers, not of the signature. A caller passing a single-pass
        /// sequence would have it consumed by the count and hand vanilla an empty one -- a map generated with no
        /// steps in it, caused by a progress bar. Taking the list once and passing that list on removes the
        /// question: one enumeration, an exact count, and vanilla receives the same steps in the same order
        /// whatever kind of sequence it started as.
        /// </summary>
        public static void Prefix(ref IEnumerable<GenStepWithParams> genStepDefs)
        {
            try
            {
                if (genStepDefs == null)
                {
                    UIFramework.Stages.UILoadingScreen.BeginMapGeneration(0);
                    return;
                }

                List<GenStepWithParams> steps = genStepDefs as List<GenStepWithParams>
                                                ?? genStepDefs.ToList();

                genStepDefs = steps;

                UIFramework.Stages.UILoadingScreen.BeginMapGeneration(steps.Count);
            }
            catch (Exception ex)
            {
                // genStepDefs is left exactly as it arrived on this path: the assignment above is the last thing
                // to happen after a successful ToList, so a throw cannot leave vanilla holding a half-built list.
                UIGuard.Report("LoadingScreen.BeginMapGeneration", ex);
            }
        }
    }
}
