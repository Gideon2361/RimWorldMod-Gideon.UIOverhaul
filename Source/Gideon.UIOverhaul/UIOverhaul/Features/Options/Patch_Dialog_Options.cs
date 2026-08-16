using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// Opens this mod's options wherever RimWorld would have opened its own.
    ///
    /// <b>Replacing rather than adding, now that Game Settings exists.</b> An earlier version drew an extra row
    /// inside vanilla's window; that made sense while ours held only this mod's settings. Now that the Game
    /// Settings category carries RimWorld's own pause menu and options, two windows offering the same controls
    /// is worse than either alone -- a player who changes the volume in one and looks for it in the other finds
    /// two answers to the same question.
    ///
    /// <b>Intercepted at <c>WindowStack.Add</c> rather than at the button.</b> The Options entry is built inside
    /// <c>MainMenuDrawer.DoMainMenuControls</c> as a delegate in a list, which cannot be patched without a
    /// transpiler -- and that method is shared by the main menu and the in-game pause menu, so a transpiler would
    /// have to be right about both. Swapping the window as it is added catches every route to it, including the
    /// keyboard shortcut and any mod that opens it directly, and needs no IL.
    ///
    /// <b>There is a way back to the real one, and there has to be.</b> Keybindings and the developer options are
    /// not reimplemented in Game Settings, so blocking vanilla's window outright would put them out of reach.
    /// <see cref="OpenVanilla"/> is the escape hatch, used by the button in that section.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    public static class Patch_WindowStack_Add_Options
    {
        /// <summary>
        /// Set while deliberately opening RimWorld's own options, so the swap below lets that one through.
        ///
        /// A field rather than a parameter because the thing being told is a Harmony prefix on somebody else's
        /// method, which takes no arguments of ours. Cleared in a finally, so an exception inside Add cannot
        /// leave the swap disabled for the rest of the session.
        /// </summary>
        private static bool bypass;

        /// <summary>Opens RimWorld's own options window, past the replacement.</summary>
        internal static void OpenVanilla()
        {
            try
            {
                bypass = true;

                Find.WindowStack.Add(new Dialog_Options());
            }
            finally
            {
                bypass = false;
            }
        }

        public static bool Prefix(Window window)
        {
            if (bypass || !(window is Dialog_Options))
                return true;

            return UIGuard.Replaced("Options.ReplaceVanillaOptions",
                () => Find.WindowStack.Add(new Dialog_UIOptions()),
                "RimWorld's own options window opens instead of this mod's.");
        }
    }
}
