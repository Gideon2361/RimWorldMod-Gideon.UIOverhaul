using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade.Caravan
{
    /// <summary>
    /// Draws our split screen in place of RimWorld's, inside RimWorld's own window.
    ///
    /// <b>Same arrangement as the form dialog and for a stronger version of the same reason.</b> That class at
    /// least made its transferables and its two mass figures public; this one exposes nothing but
    /// <c>InitialSize</c>. The split itself, the two colonist checks and the inventory hand-off between pawns are
    /// all private, and all of them are game rules. So the window stays RimWorld's and only the drawing is ours.
    /// See <see cref="SplitReflection"/>.
    ///
    /// <b>Governed by the same setting as the form window.</b> Both are the caravan screen as far as a player is
    /// concerned, and a mod that patches one of these dialogs is very likely patching the other -- so switching
    /// one off and leaving the other on would be an escape hatch that does not reach the thing it was opened for.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_SplitCaravan), nameof(Dialog_SplitCaravan.DoWindowContents))]
    internal static class Patch_SplitCaravanContents
    {
        public static bool Prefix(Dialog_SplitCaravan __instance, Rect inRect)
        {
            if (!Ours(__instance))
                return true;

            SplitScreen.Draw(__instance, inRect);

            return false;
        }

        /// <summary>
        /// Whether this window is ours to draw.
        ///
        /// Exact type rather than assignability: a mod subclassing the split dialog has done more than restyle
        /// it, and drawing over its body would throw that away silently.
        /// </summary>
        internal static bool Ours(Dialog_SplitCaravan dialog)
        {
            return UIGuard.Try("Split.Ours", () =>
                    dialog != null
                    && dialog.GetType() == typeof(Dialog_SplitCaravan)
                    && TradeWindowSettings.CustomCaravanWindow
                    && SplitReflection.Available,
                false, null);
        }
    }

    /// <summary>
    /// Sizes the window for our layout.
    ///
    /// Vanilla asks for 1024 by the full screen height, which suits a tab strip and does not suit a rail, a table
    /// and two manifests side by side. Shorter than the form window, because this one has no route to plan and
    /// so less to say.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_SplitCaravan), "get_InitialSize")]
    internal static class Patch_SplitCaravanSize
    {
        public static void Postfix(Dialog_SplitCaravan __instance, ref Vector2 __result)
        {
            if (!Patch_SplitCaravanContents.Ours(__instance))
                return;

            __result = new Vector2(Mathf.Min(1320f, UI.screenWidth - 20f),
                Mathf.Min(880f, UI.screenHeight - 20f));
        }
    }

    // <b>There is deliberately no close patch here, unlike the form window's.</b> Dialog_SplitCaravan does not
    // override PostClose -- it inherits Window's. So [HarmonyPatch(typeof(Dialog_SplitCaravan), "PostClose")]
    // does not mean "this window closing": it resolves to Window.PostClose itself and would run our postfix for
    // every window in the game, with an __instance parameter typed as a class most of them are not. Failing that,
    // it fails to resolve at all, and an unresolvable annotation takes down the whole PatchAll -- every patch in
    // this mod, not just this one.
    //
    // The state is pruned lazily instead, in SplitScreen, which needs no patch and cannot be wrong about which
    // window it is looking at.
}
