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
    /// <b>A failure falls through to vanilla.</b> Returning true hands the request back to the game, so the
    /// worst case is the old save dialog rather than a player who cannot save at all. For this particular
    /// window that is not a nicety: the alternative to a working save screen is losing a colony.
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
                return !Replace(() => new Dialog_SaveGame(), typeof(Dialog_SaveGame));

            if (window is Dialog_SaveFileList_Load)
                return !Replace(() => new Dialog_LoadGame(), typeof(Dialog_LoadGame));

            return true;
        }

        /// <summary>
        /// Opens ours instead, or reports that it could not.
        /// </summary>
        /// <returns>True when ours is showing, so the vanilla request should be dropped.</returns>
        private static bool Replace(Func<Window> build, Type ours)
        {
            return UIGuard.Try("Saves.RedirectDialog", () =>
            {
                // Already up. Dropping the request rather than stacking a second copy, which is what a
                // double click on the pause menu would otherwise do.
                if (Find.WindowStack.WindowOfType<Dialog_SaveGame>() != null
                    || Find.WindowStack.WindowOfType<Dialog_LoadGame>() != null)
                    return true;

                opening = true;

                try
                {
                    Find.WindowStack.Add(build());
                }
                finally
                {
                    opening = false;
                }

                return true;
            }, false, "The save window could not open, so the game's own was used instead.");
        }
    }
}
