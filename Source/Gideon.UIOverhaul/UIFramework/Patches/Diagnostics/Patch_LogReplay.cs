using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Diagnostics
{
    /// <summary>
    /// Diverts errors and warnings to whoever is replaying, instead of writing them.
    ///
    /// <b>Prefixes rather than postfixes, because the point is to stop the write.</b> The sibling patch
    /// <c>Patch_LogCapture</c> copies log output into the loading console and deliberately changes nothing;
    /// this one is the opposite case. See <see cref="UILogReplay"/> for why a tool that re-runs the definition
    /// parser must not let the second copy of every message reach the log.
    ///
    /// <b>Two methods, as with the capture patch.</b> <c>ErrorOnce</c> and <c>WarningOnce</c> both end in these,
    /// so covering the pair covers everything that reaches the log by that route.
    ///
    /// <b>Off unless a replay is running,</b> which is the overwhelmingly common case: the cost on the normal
    /// path is one thread-static read on a method that is already taking a lock and building a stack trace.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_LogReplay
    {
        /// <summary>
        /// First, so the line is claimed before another mod's prefix can act on it.
        ///
        /// A prefix returning false skips the remaining prefixes as well as the original, so running early is
        /// what makes the diversion complete rather than merely last.
        /// </summary>
        [HarmonyPatch(typeof(Log), nameof(Log.Error), typeof(string))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Error(string text)
        {
            return !UILogReplay.Take(true, text);
        }

        [HarmonyPatch(typeof(Log), nameof(Log.Warning), typeof(string))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Warning(string text)
        {
            return !UILogReplay.Take(false, text);
        }
    }
}
