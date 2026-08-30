using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Quests
{
    /// <summary>
    /// Sends anything that opens vanilla's quests tab to ours instead.
    ///
    /// <b>Patched at the toggle rather than at the button.</b> The button on the bar is ours to draw and could
    /// simply point elsewhere, but F7, the letter that says a quest has arrived, and any mod that opens the tab
    /// by def all go through <c>MainTabsRoot.ToggleTab</c>. Catching them there is one patch instead of a list
    /// of call sites that is never finished.
    ///
    /// <b>Matched on def name, not on the def object.</b> The def is looked up by name so a missing or renamed
    /// vanilla def cannot throw here, and a null replacement leaves the argument alone, which means the failure
    /// mode of this patch is RimWorld's own screen rather than no screen.
    /// </summary>
    [HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.ToggleTab))]
    internal static class Patch_MainTabsRoot_ToggleTab_Quests
    {
        public static void Prefix(ref MainButtonDef newTab)
        {
            MainButtonDef replacement = Replacement(newTab);

            if (replacement != null)
                newTab = replacement;
        }

        private static MainButtonDef Replacement(MainButtonDef newTab)
        {
            return UIGuard.Try("Quests.Redirect", () =>
            {
                if (newTab == null || newTab.defName != QuestTabs.VanillaDefName)
                    return null;

                return QuestTabs.Available ? QuestTabs.Ours() : null;
            }, null, null);
        }
    }
}
