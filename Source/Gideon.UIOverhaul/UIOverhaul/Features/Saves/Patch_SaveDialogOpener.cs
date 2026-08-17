using System;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Opens our save window instead of vanilla's.
    ///
    /// <b>Intercepted where the window is added, not where the button is drawn.</b> The save dialog is opened
    /// from the pause menu, from this mod's own options window, and potentially from any other mod; patching
    /// the window stack catches every route at once, which is the same reasoning as
    /// <c>Patch_WindowStack_Add_DevMenu</c>.
    ///
    /// <b>Vanilla's dialog is never substituted for ours, even on a failure.</b> An earlier version fell back to
    /// it, on the reasoning that any save screen beats none. That was the wrong trade and it is gone: silently
    /// handing somebody a different interface hides the defect that caused it, so the bug ships and keeps
    /// shipping while the symptom looks like a cosmetic inconsistency. The window either works or its failure is
    /// visible and gets fixed.
    ///
    /// <b>The guard stays, and it is doing a different job now.</b> Nothing may throw out of a Harmony prefix
    /// into RimWorld's window stack, so construction is still contained -- but containment no longer changes the
    /// outcome. Vanilla stays suppressed either way.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class Patch_WindowStack_Add_SaveGame
    {
        /// <summary>Guards against our own replacements being intercepted on the way in.</summary>
        private static bool opening;

        public static bool Prefix(Window window)
        {
            if (opening)
                return true;

            if (window is Dialog_SaveFileList_Save)
            {
                Open(() => new Dialog_SaveGame());

                return false;
            }

            if (window is Dialog_SaveFileList_Load)
            {
                Open(() => new Dialog_LoadGame());

                return false;
            }

            return true;
        }

        /// <summary>Opens ours, containing any failure so it cannot escape into the window stack.</summary>
        private static void Open(Func<Window> build)
        {
            UIGuard.Try("Saves.RedirectDialog", () =>
            {
                // Already up. Dropping the request rather than stacking a second copy, which is what a
                // double click on the pause menu would otherwise do.
                if (Find.WindowStack.WindowOfType<Dialog_SaveGame>() != null
                    || Find.WindowStack.WindowOfType<Dialog_LoadGame>() != null)
                    return;

                opening = true;

                try
                {
                    Find.WindowStack.Add(build());
                }
                finally
                {
                    opening = false;
                }
            }, "The save window did not open.");
        }
    }
}
