using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Sends both of vanilla's animal tabs to ours, wherever they are opened from.
    ///
    /// <b>Patched where the tab is chosen, not where the button is drawn.</b> <c>MainTabsRoot.ToggleTab</c> is the
    /// one funnel: the button bar, the F4 and F5 hotkeys, <c>SetCurrentTab</c> and anything a mod calls all arrive
    /// here. Substituting the def rather than intercepting the window keeps the game's own bookkeeping intact, so
    /// the open tab, the highlighted button and the escape key all agree afterwards.
    ///
    /// <b>Which scope opens depends on which button was pressed,</b> which is the whole reason to redirect rather
    /// than only hide the buttons. Somebody who has pressed F5 for wildlife for a thousand hours gets the wildlife
    /// list, in our tab, with no relearning.
    ///
    /// <b>When our tab is not in the def database, nothing is redirected and vanilla opens.</b> That looks like the
    /// fallback this mod has a rule against and is not: the rule is about our window failing at runtime and
    /// quietly handing off, which hides the defect. A missing MainButtonDef means our tab does not exist in this
    /// install, so there is nothing to redirect to, and taking away the player's only animals screen to make a
    /// point would be the worse answer. The suppression in the button bar is conditioned on the same thing for the
    /// same reason.
    ///
    /// <b>Assignability rather than equality on the window class,</b> so a mod that subclasses one of vanilla's
    /// animal tabs is caught too. That mod's own tab is left alone unless it inherits from one of these.
    /// </summary>
    [HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.ToggleTab))]
    internal static class Patch_MainTabsRoot_ToggleTab_Animals
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
        /// <b>Split out from the prefix because a ref parameter cannot be touched inside a lambda,</b> and the
        /// guard is a lambda. Deciding here and assigning there keeps the whole decision inside the guard, which
        /// is what matters: nothing may throw out of a prefix into RimWorld's tab handling.
        /// </summary>
        private static MainButtonDef Replacement(MainButtonDef newTab)
        {
            return UIGuard.Try("Animals.Redirect", () =>
            {
                if (newTab?.tabWindowClass == null)
                    return null;

                bool animals = typeof(MainTabWindow_Animals).IsAssignableFrom(newTab.tabWindowClass);
                bool wildlife = typeof(MainTabWindow_Wildlife).IsAssignableFrom(newTab.tabWindowClass);

                if (!animals && !wildlife)
                    return null;

                MainButtonDef ourTab = AnimalTabs.Ours();

                if (ourTab == null)
                    return null;

                AnimalsPanel.ShowScope(animals ? AnimalScope.Colony : AnimalScope.Wild);

                return ourTab;
            }, null, null);
        }

    }

    /// <summary>
    /// Whether this mod's animals tab exists, and its def.
    ///
    /// <b>Separate from the patch because two unrelated things ask.</b> The redirect above asks so it knows
    /// whether it has anywhere to send a keypress, and the button bar asks so it knows whether suppressing
    /// vanilla's two buttons would leave the player with no animals screen at all. Both need the same answer and
    /// neither should be reaching into a Harmony patch class for it.
    /// </summary>
    internal static class AnimalTabs
    {
        internal const string OurDefName = "Gideon_Animals";

        private static MainButtonDef ours;
        private static bool looked;

        /// <summary>Our tab's def, looked up once. Null when the mod's defs did not load.</summary>
        internal static MainButtonDef Ours()
        {
            if (looked)
                return ours;

            looked = true;
            ours = DefDatabase<MainButtonDef>.GetNamedSilentFail(OurDefName);

            if (ours == null)
                UIGuard.Report("Animals.NoDef", new System.InvalidOperationException(
                        "MainButtonDef " + OurDefName + " is missing from the def database."),
                    "The animals tab's own button is missing, so RimWorld's animal and wildlife tabs are left in "
                    + "place. Reinstalling the mod restores it.");

            return ours;
        }

        /// <summary>Whether vanilla's animal tabs have somewhere to be redirected to.</summary>
        internal static bool Available => Ours() != null;
    }
}
