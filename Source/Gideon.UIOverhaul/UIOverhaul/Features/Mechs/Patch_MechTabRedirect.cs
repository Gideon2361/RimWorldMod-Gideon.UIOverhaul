using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// Sends RimWorld's own mech tab to ours, wherever it is opened from.
    ///
    /// <b>Patched where the tab is chosen, not where the button is drawn.</b> <c>MainTabsRoot.ToggleTab</c> is
    /// the one funnel: the button bar, a hotkey, <c>SetCurrentTab</c> and anything a mod calls all arrive here.
    /// Substituting the def rather than intercepting the window keeps the game's own bookkeeping intact, so the
    /// open tab, the highlighted button and the escape key all agree afterwards. The same shape as the animals,
    /// hospital and quests redirects, which is where this pattern was worked out.
    ///
    /// <b>When our tab is not available, nothing is redirected and RimWorld's opens.</b> That is not the vanilla
    /// fallback this mod has a rule against: the rule is about our window failing at runtime and quietly handing
    /// off, which hides the defect. A missing MainButtonDef means our tab does not exist in this install, and an
    /// install without Biotech has no mechanitors for either screen to show.
    /// </summary>
    [HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.ToggleTab))]
    internal static class Patch_MainTabsRoot_ToggleTab_Mechs
    {
        public static void Prefix(ref MainButtonDef newTab)
        {
            MainButtonDef replacement = Replacement(newTab);

            if (replacement != null)
                newTab = replacement;
        }

        /// <summary>
        /// Our tab when RimWorld's should be redirected to it, or null to leave the argument alone.
        ///
        /// Split out from the prefix because a ref parameter cannot be touched inside a lambda, and the guard is
        /// a lambda. Nothing may throw out of a prefix into RimWorld's tab handling.
        /// </summary>
        private static MainButtonDef Replacement(MainButtonDef newTab)
        {
            return UIGuard.Try<MainButtonDef>("Mechs.Redirect", () =>
            {
                if (newTab == null || newTab.defName != MechTabs.VanillaDefName)
                    return null;

                return MechTabs.Available ? MechTabs.Ours() : null;
            }, null, null);
        }
    }
}
