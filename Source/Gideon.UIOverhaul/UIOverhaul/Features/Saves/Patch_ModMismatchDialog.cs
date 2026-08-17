using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Stops vanilla's mod mismatch dialog interrupting a load started from this mod's load window.
    ///
    /// <b>The information is not being removed, it is being moved earlier.</b> <c>Dialog_ModMismatch</c>
    /// appears after the save has been chosen and the Load button pressed, which is the wrong moment: by then
    /// the decision has been made, and the dialog's job is to talk somebody out of it. The load window now
    /// lists what is missing and what is new beside the save itself, while it is being chosen, so the answer
    /// arrives before the question.
    ///
    /// <b>Only the mod branch is suppressed, and only for our own load.</b> The way it is done matters, so it
    /// is worth being exact about:
    ///
    /// <list type="bullet">
    /// <item><c>LoadedModsMatchesActiveMods</c> -- the overload with the summary out-parameters -- is used
    /// solely to decide whether to raise a warning dialog. Answering "they match" while our flag is armed
    /// skips the mod dialog and lets the method fall through to its version check, so a save from an
    /// incompatible build still stops and asks. Vanilla actually returns before that check when mods differ,
    /// so this shows a warning in a case where the base game shows none.</item>
    /// <item><c>LoadedModsMatchesActiveModsNoInfo</c> is deliberately left alone. It looks like the same
    /// question and is not: <c>LoadGameDataHeader</c> uses it to set <c>modListChanged</c>, which feeds
    /// <c>BackCompatibility.CheckSaveIdenticalToCurrentEnvironment</c> and decides whether the back
    /// compatibility converters run at all. Patching that would quietly change how old saves are read.</item>
    /// <item>The flag is armed around one call and cleared in a finally, so nothing else that loads a file --
    /// a scenario, a world, another mod's loader -- is affected.</item>
    /// </list>
    /// </summary>
    [HarmonyPatch(typeof(ScribeMetaHeaderUtility),
        nameof(ScribeMetaHeaderUtility.LoadedModsMatchesActiveMods))]
    internal static class Patch_ModMismatchDialog
    {
        /// <summary>
        /// Set while this mod's load window is handing a save to vanilla's loader.
        ///
        /// Armed and cleared exactly like <c>SaveFolders.Redirect</c>, and for the same reason: the dialogs
        /// are decided synchronously inside that one call, so the flag never needs to outlive it and must
        /// not.
        /// </summary>
        internal static bool Suppress;

        public static bool Prefix(ref string loadedModsSummary, ref string runningModsSummary,
            ref bool __result)
        {
            if (!Suppress)
                return true;

            // The out parameters are only read to build the dialog's text, and there is no dialog. They still
            // have to be assigned, because C# requires it of the method being replaced.
            loadedModsSummary = null;
            runningModsSummary = null;
            __result = true;

            return false;
        }
    }
}
