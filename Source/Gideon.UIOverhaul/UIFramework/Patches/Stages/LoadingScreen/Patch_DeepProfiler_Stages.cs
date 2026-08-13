using System;
using Gideon.UIFramework.Helpers;
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
            // Written out rather than routed through UIGuard.Try for the same reason as the def counter: this is
            // called thousands of times per load and a closure per call would be waste. DeepProfiler brackets
            // essentially everything, so an escape from here would surface as a failure in whatever unrelated
            // system happened to be profiling at the time -- which is exactly the kind of report that gets filed
            // against the wrong mod.
            try
            {
                if (LongEventHandler.AnyEventNowOrWaiting)
                    Gideon.UIFramework.Stages.UILoadingScreen.PushStage(label);
            }
            catch (Exception ex)
            {
                UIGuard.Report("LoadingScreen.PushStage", ex);
            }
        }
    }

    [HarmonyPatch(typeof(DeepProfiler), nameof(DeepProfiler.End))]
    public static class Patch_DeepProfiler_End
    {
        public static void Prefix()
        {
            try
            {
                if (LongEventHandler.AnyEventNowOrWaiting)
                    Gideon.UIFramework.Stages.UILoadingScreen.PopStage();
            }
            catch (Exception ex)
            {
                UIGuard.Report("LoadingScreen.PopStage", ex);
            }
        }
    }
}
