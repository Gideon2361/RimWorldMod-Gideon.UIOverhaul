using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>
    /// Whether this mod's ideoligions tab exists and may be used, and its def.
    ///
    /// <b>Two unrelated callers ask, which is why this is not inside the patch.</b> The redirect asks so it knows
    /// whether it has anywhere to send a keypress, and the button bar asks so it knows whether suppressing
    /// vanilla's button would leave the player with no ideoligion screen at all.
    ///
    /// <b>Ideology is part of the question rather than a separate check.</b> A <c>MainButtonDef</c> cannot be
    /// conditionally undefined, so our own button exists even in an install without the expansion, where it would
    /// open a window with nothing in it. Answering false there is what makes "absent rather than empty" true for
    /// the tab, and it is the same arrangement the character editor's tab uses for its own switch.
    /// </summary>
    internal static class IdeoTabs
    {
        internal const string OurDefName = "Gideon_Ideoligions";

        /// <summary>Vanilla's, which this replaces.</summary>
        internal const string VanillaDefName = "Ideos";

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
                UIGuard.Report("Ideoligions.NoDef", new System.InvalidOperationException(
                        "MainButtonDef " + OurDefName + " is missing from the def database."),
                    "The ideoligions tab's own button is missing, so RimWorld's is left in place. Reinstalling "
                    + "the mod restores it.");

            return ours;
        }

        /// <summary>
        /// Whether vanilla's ideoligions tab has somewhere to be redirected to.
        ///
        /// Not cached, because <c>ModsConfig.IdeologyActive</c> can change between one launch and the next and a
        /// cached false would outlive the player installing the expansion.
        /// </summary>
        internal static bool Available
        {
            get { return ModsConfig.IdeologyActive && Ours() != null; }
        }
    }
}
