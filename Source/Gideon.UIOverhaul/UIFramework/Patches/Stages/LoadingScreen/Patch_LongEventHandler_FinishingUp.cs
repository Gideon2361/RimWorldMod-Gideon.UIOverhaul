using System;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Stages;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Labels the last phase of a load, which otherwise wears the previous phase's name.
    ///
    /// <b>The symptom.</b> "Assigning hashes" appeared to take a very long time -- long enough to look like
    /// hash assignment was the slow part of loading. It is not: <c>ShortHashGiver.GiveAllShortHashes</c> is
    /// already parallel, partitioned by def type, and finishes quickly.
    ///
    /// <b>What actually happens.</b> The stage text comes from RimWorld's own DeepProfiler labels, mapped to
    /// display text by the milestone table in <see cref="UILoadingScreen"/>. <c>"Short hash giving."</c> is the
    /// <i>last</i> entry in that table, at 0.995. <c>DeepProfiler.End</c> pops the stack but does not clear the
    /// text -- deliberately, since a label flickering to empty between phases would be worse -- so the last
    /// milestone's text stands until another milestone replaces it.
    ///
    /// Nothing replaces it. <c>PlayDataLoader.DoPlayLoad</c> ends there, and what follows is four
    /// <c>LongEventHandler.ExecuteWhenFinished</c> callbacks running on the main thread with no DeepProfiler
    /// bracket of their own. That phase is real work -- it is where content loading actually happens, textures
    /// included -- and it is the part with no name, so it borrows the name of the phase before it.
    ///
    /// <b>The fix is a label, not threading.</b> The work is not misattributed because it is slow; it is
    /// misattributed because it is unlabeled. Adding a thread to hash assignment would have made a fast phase
    /// faster and left the long unlabeled one exactly as it was, still reading "Assigning hashes".
    ///
    /// <c>ExecuteToExecuteWhenFinished</c> is private, hence the string name. If it is ever renamed the patch
    /// fails to apply, Harmony says so at startup, and the label goes back to being wrong -- which is the
    /// behavior we started from rather than a new fault.
    /// </summary>
    [HarmonyPatch(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished")]
    public static class Patch_LongEventHandler_FinishingUp
    {
        /// <summary>
        /// Only while a long event is up, matching <see cref="Patch_DeepProfiler_Start"/>: this also runs during
        /// play, where relabeling a screen nobody is looking at would be pointless work.
        ///
        /// The bar is nudged rather than filled. 1.0 during work that is still running reads as stalled, which
        /// is the impression this patch exists to remove; a small advance says "moved on" without claiming done.
        ///
        /// The step line is cleared, because whatever it last said belonged to the previous phase.
        /// </summary>
        /// <summary>
        /// Guarded because of what this method is: the queue that finishes loading every mod's content. An escape
        /// from here stops the remaining callbacks running, and a load that stops there leaves textures unbuilt.
        /// A label is not worth that.
        /// </summary>
        public static void Prefix()
        {
            try
            {
                if (LongEventHandler.AnyEventNowOrWaiting)
                    UILoadingScreen.Report("Finishing up", string.Empty, 0.998f);
            }
            catch (Exception ex)
            {
                UIGuard.Report("LoadingScreen.FinishingUp", ex);
            }
        }
    }
}
