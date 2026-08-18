using System.Reflection;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.ButtonBar;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// Makes Escape open this mod's settings window instead of RimWorld's menu tab.
    ///
    /// <b>Why this replaces rather than adds.</b> The Game Settings category already carries saving, loading,
    /// options and quitting, so vanilla's menu offers nothing our window does not. Leaving both reachable meant
    /// two doors into the same room, and the vanilla menu button is suppressed from the bar for the same reason
    /// (see <see cref="UIButtonBarConfig.Suppressed"/>). Escape is the other door, and this closes it.
    ///
    /// <b>The target is private, so it is named as a string.</b> <c>UIRoot_Play.OpenMainMenuShortcut</c> is where
    /// vanilla turns the key press into <c>SetCurrentTab(MainButtonDefOf.Menu)</c>. Patching it is narrower than
    /// the alternatives: intercepting the window as it is added would fight <c>MainTabsRoot</c>, which records
    /// which tab it believes is open and would be left disagreeing with the stack.
    ///
    /// <b>Toggling needs no code.</b> The method runs at the very end of <c>UIRootOnGUI</c>, after the window
    /// stack has had the event. An open window that handles the cancel key calls <c>Event.current.Use()</c>,
    /// which turns the event into <c>EventType.Used</c> and fails the test below, so Escape closes our window on
    /// the second press exactly as it closed vanilla's.
    ///
    /// <b>It never falls through to vanilla.</b> If our window cannot open, nothing opens and the failure is
    /// reported. Substituting RimWorld's menu would be handing the player a window this mod has spent its
    /// existence replacing, and it would arrive only in the moment something was already wrong.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_EscapeMenu
    {
        [HarmonyTargetMethod]
        internal static MethodBase Target()
        {
            return AccessTools.Method(typeof(UIRoot_Play), "OpenMainMenuShortcut");
        }

        internal static bool Prefix()
        {
            // The same test vanilla makes, and it has to be made here rather than left to the original: the
            // original is what we are replacing, and asking it would open the menu we are trying not to open.
            //
            // Both halves are kept because the game keeps both. The raw key covers Escape itself; the binding
            // covers a player who has moved Cancel somewhere else.
            bool pressed = UIGuard.Try("Menu.EscapePressed",
                () => (Event.current != null && Event.current.type == EventType.KeyDown
                                             && Event.current.keyCode == KeyCode.Escape)
                      || (KeyBindingDefOf.Cancel != null && KeyBindingDefOf.Cancel.KeyDownEvent),
                false, null);

            if (!pressed)
                return false;

            // Used before opening, not after. The window stack is walked again later in the frame, and an
            // unconsumed Escape reaching a window that closes on cancel would shut the window we just opened.
            Event.current.Use();

            // Paused, matching the menu this replaces. A lambda rather than the method itself: an optional
            // parameter is not filled in when a method group is converted to a delegate, so passing the method
            // by name would silently take the unpaused default.
            UIGuard.Try("Menu.OpenSettings", () => UIButtonBarRenderer.OpenUIOptions(true),
                "Escape opens nothing this once. The settings button on the bar still works.");

            return false;
        }
    }
}
