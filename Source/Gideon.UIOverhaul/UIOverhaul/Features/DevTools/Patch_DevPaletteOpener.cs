using Gideon.UIFramework.Helpers;
using HarmonyLib;
using LudeonTK;
using Verse;

namespace Gideon.UIOverhaul.Features.DevTools
{
    /// <summary>
    /// Opens the palette instead of the full-screen developer menu.
    ///
    /// <b>Intercepted where the window is added, not where the button is drawn.</b> Vanilla's dev toolbar opens
    /// <c>Dialog_Debug</c> from more than one place, and a future RimWorld could add another; patching the window
    /// stack catches every route, including a mod that opens it directly.
    ///
    /// <b>The old menu is not removed, only redirected.</b> Anything that deliberately constructs a
    /// <c>Dialog_Debug</c> to navigate to a particular node still works, because
    /// <c>DebugActionNode.Enter</c> creates one itself when it needs somewhere to show children and this only
    /// replaces the case where it is being opened as the menu.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class Patch_WindowStack_Add_DevMenu
    {
        /// <summary>Guards against our own replacement being intercepted on the way in.</summary>
        private static bool opening;

        public static bool Prefix(Window window)
        {
            if (opening || !(window is Dialog_Debug))
                return true;

            UIGuard.Try("DevTools.Redirect", () =>
            {
                // Already showing, so this press closes it. Vanilla stays suppressed either way, which is what
                // makes the key behave as a toggle for our palette rather than opening the old menu on top.
                if (Find.WindowStack.WindowOfType<Dialog_DevPalette>() != null)
                {
                    Find.WindowStack.TryRemove(typeof(Dialog_DevPalette));

                    return;
                }

                opening = true;

                try
                {
                    Find.WindowStack.Add(new Dialog_DevPalette());
                }
                finally
                {
                    opening = false;
                }
            }, "The developer palette did not open.");

            // Always. Vanilla's menu is never substituted for ours, including when ours failed to open: a
            // silent swap to a different interface hides the defect that caused it, and a hidden defect is one
            // that ships. See the same reasoning on the save dialog opener.
            return false;
        }
    }
}
