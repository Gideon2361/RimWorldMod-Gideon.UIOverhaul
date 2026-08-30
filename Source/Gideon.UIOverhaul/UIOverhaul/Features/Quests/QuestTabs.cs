using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Quests
{
    /// <summary>
    /// Whether this mod's quests tab exists and may be used, and its def.
    ///
    /// <b>Two unrelated callers ask, which is why this is not inside the patch.</b> The redirect asks so it
    /// knows whether it has anywhere to send F7, and the button bar asks so it knows whether suppressing
    /// vanilla's button would leave the player with no quests screen at all.
    ///
    /// <b>No expansion gate, unlike the ideoligions tab.</b> Quests are Core: an install with no expansions
    /// still gets them, so the only question is whether our own def loaded.
    /// </summary>
    internal static class QuestTabs
    {
        internal const string OurDefName = "Gideon_Quests";

        /// <summary>Vanilla's, which this replaces.</summary>
        internal const string VanillaDefName = "Quests";

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
                UIGuard.Report("Quests.NoDef", new System.InvalidOperationException(
                        "MainButtonDef " + OurDefName + " is missing from the def database."),
                    "The quests tab's own button is missing, so RimWorld's is left in place. Reinstalling the "
                    + "mod restores it.");

            return ours;
        }

        internal static bool Available
        {
            get { return Ours() != null; }
        }
    }
}
