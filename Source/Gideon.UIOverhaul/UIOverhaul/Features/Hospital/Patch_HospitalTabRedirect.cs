using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// Sends Colony Hospital's own tab to ours, wherever it is opened from.
    ///
    /// <b>Patched where the tab is chosen, not where the button is drawn.</b> <c>MainTabsRoot.ToggleTab</c> is the
    /// one funnel: the button bar, a hotkey, <c>SetCurrentTab</c> and anything a mod calls all arrive here.
    /// Substituting the def rather than intercepting the window keeps the game's own bookkeeping intact, so the
    /// open tab, the highlighted button and the escape key all agree afterwards. The same shape as the animals
    /// redirect, which is where this pattern was worked out.
    ///
    /// <b>Matched on the def name rather than the window class,</b> because their class is one we cannot name at
    /// compile time without taking a hard dependency on a mod that is optional.
    ///
    /// <b>When our tab is not in the def database, nothing is redirected and theirs opens.</b> That is not the
    /// vanilla fallback this mod has a rule against: the rule is about our window failing at runtime and quietly
    /// handing off, which hides the defect. A missing MainButtonDef means our tab does not exist in this install,
    /// and taking away the player's only hospital screen to make a point would be the worse answer.
    /// </summary>
    [HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.ToggleTab))]
    internal static class Patch_MainTabsRoot_ToggleTab_Hospital
    {
        public static void Prefix(ref MainButtonDef newTab)
        {
            MainButtonDef replacement = Replacement(newTab);

            if (replacement != null)
                newTab = replacement;
        }

        /// <summary>
        /// Our tab when this one should be redirected to it, or null to leave the argument alone.
        ///
        /// Split out from the prefix because a ref parameter cannot be touched inside a lambda, and the guard is
        /// a lambda. Nothing may throw out of a prefix into RimWorld's tab handling.
        /// </summary>
        private static MainButtonDef Replacement(MainButtonDef newTab)
        {
            return UIGuard.Try<MainButtonDef>("Hospital.Redirect", () =>
            {
                if (newTab == null || newTab.defName != HospitalIntegrations.ColonyHospitalTabDefName)
                    return null;

                return HospitalTabs.Ours();
            }, null, null);
        }
    }

    /// <summary>
    /// Whether this mod's hospital tab exists, and its def.
    ///
    /// <b>Separate from the patch because two unrelated things ask.</b> The redirect asks so it knows whether it
    /// has anywhere to send a keypress, and the button bar asks so it knows whether suppressing Colony Hospital's
    /// button would leave the player with no hospital screen at all.
    /// </summary>
    internal static class HospitalTabs
    {
        internal const string OurDefName = "Gideon_Hospital";

        private static MainButtonDef ours;

        private static bool looked;

        internal static MainButtonDef Ours()
        {
            if (looked)
                return ours;

            looked = true;
            ours = DefDatabase<MainButtonDef>.GetNamedSilentFail(OurDefName);

            if (ours == null)
                UIGuard.Report("Hospital.NoDef", new System.InvalidOperationException(
                        "MainButtonDef " + OurDefName + " is missing from the def database."),
                    "The hospital tab's own button is missing. Reinstalling the mod restores it.");

            return ours;
        }

        internal static bool Available
        {
            get { return Ours() != null; }
        }
    }
}
