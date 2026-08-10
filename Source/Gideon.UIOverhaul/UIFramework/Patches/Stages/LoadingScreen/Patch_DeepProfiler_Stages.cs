using Gideon.UIFramework.Stages;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Feeds RimWorld's own phase instrumentation into <see cref="UILoadingScreen"/>.
    ///
    /// DeepProfiler.Start/End brackets every load phase whether or not profiling is switched on, and
    /// the labels are exactly what a loading screen wants to say. Reading them beats maintaining our
    /// own list of phases, which would quietly describe the wrong sequence after a game update.
    ///
    /// These run on whichever thread is loading; UILoadingProgress takes a lock.
    /// </summary>
    [HarmonyPatch(typeof(DeepProfiler), nameof(DeepProfiler.Start))]
    public static class Patch_DeepProfiler_Start
    {
        /// <summary>
        /// Only while a long event is up. DeepProfiler is used during play as well, and tracking then
        /// would leave the stage text describing something no screen is showing -- besides adding
        /// work to calls that happen in gameplay hot paths.
        /// </summary>
        public static void Prefix(string label)
        {
            if (LongEventHandler.AnyEventNowOrWaiting)
                Gideon.UIFramework.Stages.UILoadingScreen.PushStage(label);
        }
    }

    [HarmonyPatch(typeof(DeepProfiler), nameof(DeepProfiler.End))]
    public static class Patch_DeepProfiler_End
    {
        public static void Prefix()
        {
            if (LongEventHandler.AnyEventNowOrWaiting)
                Gideon.UIFramework.Stages.UILoadingScreen.PopStage();
        }
    }
}
