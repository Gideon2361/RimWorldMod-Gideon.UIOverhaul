using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Factions
{
    /// <summary>
    /// Sends anything that opens vanilla's factions tab to ours instead.
    ///
    /// <b>Patched at the toggle rather than at the button,</b> the same shape as the quests redirect: the
    /// button on the bar is ours to draw and could simply point elsewhere, but the info card, the pawn table
    /// and any mod that opens the tab by def all go through <c>MainTabsRoot.ToggleTab</c>.
    ///
    /// <b>A redirect never closes the tab, and that is the whole reason this one is not a copy of the quests
    /// patch.</b> <c>SetCurrentTab</c> compares the def it was handed against the open tab before it calls
    /// through, so with our tab already up it sees vanilla's def, decides they differ, and calls the toggle --
    /// which then finds our def already open and closes it. Vanilla's two callers cast
    /// <c>OpenTab.TabWindow</c> on the very next line, so closing it there is a null reference in the info
    /// card. When the redirect lands on the tab that is already open, the toggle is skipped entirely and the
    /// tab stays where it is, which is what the caller meant by asking for it.
    ///
    /// <b>Matched on def name, not on the def object.</b> A missing or renamed vanilla def cannot throw here,
    /// and a null replacement leaves the argument alone, so the failure mode of this patch is RimWorld's own
    /// screen rather than no screen.
    /// </summary>
    [HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.ToggleTab))]
    internal static class Patch_MainTabsRoot_ToggleTab_Factions
    {
        public static bool Prefix(ref MainButtonDef newTab)
        {
            MainButtonDef replacement = Replacement(newTab);

            if (replacement == null)
                return true;

            if (AlreadyOpen(replacement))
                return false;

            newTab = replacement;

            return true;
        }

        /// <summary>
        /// Our tab when this one should be redirected to it, or null to leave the argument alone.
        ///
        /// Split out from the prefix because a ref parameter cannot be touched inside a lambda, and the guard
        /// is a lambda. Nothing may throw out of a prefix into RimWorld's tab handling.
        /// </summary>
        private static MainButtonDef Replacement(MainButtonDef newTab)
        {
            return UIGuard.Try("Factions.Redirect", () =>
            {
                if (newTab == null || newTab.defName != FactionTabs.VanillaDefName)
                    return null;

                return FactionTabs.Available ? FactionTabs.Ours() : null;
            }, null, null);
        }

        private static bool AlreadyOpen(MainButtonDef tab)
        {
            return UIGuard.Try("Factions.AlreadyOpen",
                () => Find.MainTabsRoot != null && Find.MainTabsRoot.OpenTab == tab, false, null);
        }
    }

    /// <summary>
    /// Whether this mod's factions tab exists, and its def.
    ///
    /// <b>Two unrelated callers ask, which is why this is not inside the patch.</b> The redirect asks so it
    /// knows whether it has anywhere to send a request, and the button bar asks so it knows whether
    /// suppressing vanilla's button would leave the player with no factions screen at all.
    /// </summary>
    internal static class FactionTabs
    {
        internal const string OurDefName = "Gideon_Factions";

        /// <summary>Vanilla's, which this replaces.</summary>
        internal const string VanillaDefName = "Factions";

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
                UIGuard.Report("Factions.NoDef", new System.InvalidOperationException(
                        "MainButtonDef " + OurDefName + " is missing from the def database."),
                    "The factions tab's own button is missing, so RimWorld's is left in place. Reinstalling "
                    + "the mod restores it.");

            return ours;
        }

        internal static bool Available
        {
            get { return Ours() != null; }
        }
    }
}
