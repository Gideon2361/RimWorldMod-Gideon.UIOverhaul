using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade.Caravan
{
    /// <summary>
    /// Draws our caravan screen in place of RimWorld's, inside RimWorld's own window.
    ///
    /// <b>A prefix on the body, not a substitution of the window.</b> Unlike the trade dialog -- a thin view over
    /// a public session -- this window holds the model: the route, the transferables, the error checks and the
    /// formation itself are all private members of this class. Replacing the object would mean reimplementing
    /// every one of them, which is exactly the thing this feature exists not to do. Taking over the drawing
    /// leaves all of it in place and untouched.
    ///
    /// <b>What that buys, besides not copying a thousand lines of game logic.</b> The instance in the stack is
    /// still a <c>Dialog_FormCaravan</c>, so <c>WorldRoutePlanner</c> finds it, <c>Notify_ChoseRoute</c> reaches
    /// it, anything asking the window stack for one gets one, and a mod patching any of its other methods still
    /// works. Only the pixels are ours.
    ///
    /// <b>It stands down rather than half-working.</b> If the setting is off, or if the two private members that
    /// matter cannot be found in this version of the game, the prefix returns true and vanilla draws its own
    /// window -- which is a working caravan screen, not a fallback that hides a defect. See
    /// <see cref="CaravanReflection"/>.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.DoWindowContents))]
    internal static class Patch_CaravanWindowContents
    {
        public static bool Prefix(Dialog_FormCaravan __instance, Rect inRect)
        {
            if (!Ours(__instance))
                return true;

            CaravanScreen.Draw(__instance, inRect);

            return false;
        }

        /// <summary>
        /// Whether this window is ours to draw.
        ///
        /// Exact type rather than assignability: a mod that subclasses the caravan dialog has done more than
        /// restyle it, and drawing over its body would throw that away silently.
        /// </summary>
        internal static bool Ours(Dialog_FormCaravan dialog)
        {
            return UIGuard.Try("Caravan.Ours", () =>
                    dialog != null
                    && dialog.GetType() == typeof(Dialog_FormCaravan)
                    && TradeWindowSettings.CustomCaravanWindow
                    && CaravanReflection.Available,
                false, null);
        }
    }

    /// <summary>
    /// Sizes the window for our layout.
    ///
    /// Vanilla asks for 1024 by the full screen height, which suits three stacked tabs and does not suit a rail,
    /// a table and a manifest side by side. Clamped to the display, because a window wider than the screen has
    /// controls that cannot be reached.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan), "get_InitialSize")]
    internal static class Patch_CaravanWindowSize
    {
        public static void Postfix(Dialog_FormCaravan __instance, ref Vector2 __result)
        {
            if (!Patch_CaravanWindowContents.Ours(__instance))
                return;

            __result = new Vector2(Mathf.Min(1360f, UI.screenWidth - 20f),
                Mathf.Min(940f, UI.screenHeight - 20f));
        }
    }

    /// <summary>
    /// Drops the screen state when a caravan window closes.
    ///
    /// <b>Keyed on the instance, so it has to be released with the instance.</b> Without this the dictionary
    /// would hold every caravan dialog of the session alive, and -- the part that would actually be noticed -- a
    /// second caravan formed later would inherit nothing, since it is a different key, while the first one's
    /// scroll position sat in memory forever.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.PostClose))]
    internal static class Patch_CaravanWindowClose
    {
        public static void Postfix(Dialog_FormCaravan __instance)
        {
            UIGuard.Try("Caravan.Forget", () => CaravanScreen.Forget(__instance), null);
        }
    }
}
