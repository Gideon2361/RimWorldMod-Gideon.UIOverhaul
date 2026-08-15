using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Tabs
{
    /// <summary>
    /// Marks every main tab resizable and restores whatever size it was last dragged to.
    ///
    /// <b>This method is the only place a tab's rect is decided,</b> which is what makes it the right seam.
    /// <c>Window.PreOpen</c> calls it on every open, <c>Notify_ResolutionChanged</c> calls it when the display
    /// changes, and the two tabs that re-size themselves during play -- the inspect pane on a new selection, the
    /// pawn table when its rows change -- call it again themselves. Nothing in the base game overrides it, so a
    /// postfix here runs for all of them.
    ///
    /// A postfix rather than a prefix: vanilla works out the anchor and the bottom edge first, and this only
    /// needs to change the size and slide the rect back against the same corner.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow), "SetInitialSizeAndPosition")]
    public static class Patch_MainTabWindow_SetInitialSizeAndPosition
    {
        public static void Postfix(MainTabWindow __instance)
        {
            UIGuard.Try("Tabs.ApplySize", () => TabResizer.Apply(__instance),
                "That tab opens at its own default size and cannot be resized.");
        }
    }

    /// <summary>
    /// Replaces vanilla's resize control while a main tab is the window being drawn.
    ///
    /// <b>Patching the resizer rather than the window is what keeps this small.</b> <c>Window</c> already has the
    /// whole mechanism -- it creates the resizer, calls it twice a frame at the right moments, and defers the new
    /// rect to the next frame so it is not changed inside the <c>GUI.Window</c> that is using it. All of that is
    /// wanted. The only part that is wrong for a tab is where the grip goes and which edges move, and that is
    /// exactly this one method. See <see cref="TabResizer"/> for why vanilla's corner is the wrong corner.
    ///
    /// <b>The owning window comes from <c>currentlyDrawnWindow</c>,</b> because the resizer is not told who it
    /// belongs to. <c>InnerWindowOnGUI</c> sets that field before it reaches the resize control, so by the time
    /// this runs it names the window whose rect is being passed in. Anything that is not a main tab we are
    /// handling falls through to vanilla untouched, so ordinary resizable dialogs keep their own grip.
    /// </summary>
    [HarmonyPatch(typeof(WindowResizer), nameof(WindowResizer.DoResizeControl))]
    public static class Patch_WindowResizer_DoResizeControl
    {
        public static bool Prefix(Rect winRect, ref Rect __result)
        {
            MainTabWindow tab = Find.WindowStack?.currentlyDrawnWindow as MainTabWindow;

            if (!TabResizer.Handles(tab))
                return true;

            // The fallback is the rect unchanged, which Window compares against its own and treats as "no
            // resize". A failure here therefore freezes the tab at its current size rather than resizing it to
            // something arbitrary while the mouse is held down.
            __result = UIGuard.Try("Tabs.Resize", () => TabResizer.Control(tab, winRect), winRect,
                "That tab cannot be resized for the rest of the session. Its current size is kept.");

            return false;
        }
    }
}
