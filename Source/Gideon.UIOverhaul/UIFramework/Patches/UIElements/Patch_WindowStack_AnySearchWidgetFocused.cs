using Gideon.UIFramework.Controls;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Stops key bindings from firing while one of our text boxes has focus.
    ///
    /// <c>WindowStack.AnySearchWidgetFocused</c> is the single gate every key binding in the game consults:
    /// <c>KeyBindingDef.IsDown</c>, <c>IsDownEvent</c>, <c>KeyDownEvent</c> and <c>JustPressed</c> all return
    /// false while it is true. That is the entire mechanism keeping W and A from panning the map as the player
    /// types, and camera dolly is only the most obvious of the bindings it covers.
    ///
    /// The gate walks the window stack asking each window for its <c>CommonSearchWidget</c>, so it can only
    /// ever see a <c>QuickSearchWidget</c> owned by a window. A <see cref="UITextBoxControl"/> is neither --
    /// it is a control a panel draws, and the panel is often not the window either -- so without this every one
    /// of our text boxes would let key bindings through as it was typed into.
    ///
    /// A postfix that only ever ORs in, so this can never report false for a vanilla widget that reported true.
    ///
    /// Patched on the getter rather than on each of the four <c>KeyBindingDef</c> members: they already funnel
    /// through here, and one patch cannot drift from the other three.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.AnySearchWidgetFocused), MethodType.Getter)]
    public static class Patch_WindowStack_AnySearchWidgetFocused
    {
        public static void Postfix(ref bool __result)
        {
            if (!__result && UITextBoxControl.AnyFocused)
                __result = true;
        }
    }
}
