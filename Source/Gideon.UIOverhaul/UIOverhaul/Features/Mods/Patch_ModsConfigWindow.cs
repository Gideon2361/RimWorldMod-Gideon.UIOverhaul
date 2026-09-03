using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Mods
{
    /// <summary>
    /// Draws our mods page instead of RimWorld's, inside RimWorld's own window.
    ///
    /// <b>A prefix on the drawing, not a replacement of the window.</b> The page holds the transaction that
    /// protects the player's <c>ModsConfig.xml</c>: the snapshot taken in <c>PreOpen</c>, the confirmation in
    /// <c>OnCloseRequest</c>, and the commit in <c>PostClose</c>. Leaving the instance in place keeps every one
    /// of those, and means the worst case here is a screen that looks wrong rather than a mod list that is.
    /// See <see cref="ModsReflection"/> for the fuller argument.
    ///
    /// <b>It stands down rather than half working.</b> If the two commit flags cannot be reached, vanilla draws
    /// its own page, which is a working mods screen. A prettier screen whose save button did nothing would be
    /// strictly worse than the plain one.
    /// </summary>
    [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoWindowContents))]
    internal static class Patch_Page_ModsConfig_DoWindowContents
    {
        public static bool Prefix(Page_ModsConfig __instance, Rect rect)
        {
            if (!ModsReflection.Available)
                return true;

            UIGuard.Try("Mods.Draw", () => ModsScreen.Draw(__instance, rect),
                "The mods screen did not draw.");

            // Always. Falling through to vanilla after a failed draw would stack two screens in the same frame,
            // and silently swapping to a different interface hides the defect that caused it. Same reasoning as
            // the developer palette opener.
            return false;
        }
    }

    /// <summary>
    /// Resets our own screen state when the page opens, so a second visit does not inherit the first visit's
    /// search text, scroll position and selection.
    ///
    /// Postfix rather than prefix: vanilla's <c>PreOpen</c> rebuilds the mod list, and reading the roster
    /// before that runs would cache whatever was true last time.
    /// </summary>
    [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.PreOpen))]
    internal static class Patch_Page_ModsConfig_PreOpen
    {
        public static void Postfix()
        {
            UIGuard.Try("Mods.Opened", ModsScreen.Opened);
        }
    }

    /// <summary>
    /// Tells our screen the page has gone, which only the probe currently cares about.
    ///
    /// <b>It has to be told rather than work it out.</b> Nothing else here runs when the page closes, so a
    /// probe watching a static font event would go on attributing every atlas rebuild in the game to a
    /// screen that is no longer on the stack.
    ///
    /// Vanilla's <c>PostClose</c> commits the mod list; a postfix runs after that and cannot disturb it.
    /// </summary>
    [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.PostClose))]
    internal static class Patch_Page_ModsConfig_PostClose
    {
        public static void Postfix()
        {
            UIGuard.Try("Mods.Closed", ModsScreen.Closed);
        }
    }
}
