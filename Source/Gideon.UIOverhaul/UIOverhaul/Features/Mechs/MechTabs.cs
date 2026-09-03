using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// Whether this mod's mech tab exists and may be used, and its def.
    ///
    /// <b>Two unrelated callers ask, which is why this is not inside the patch.</b> The redirect asks so it
    /// knows whether it has anywhere to send the mech button, and the button bar asks so it knows whether
    /// suppressing vanilla's button would leave the player with no mech screen at all.
    ///
    /// <b>Gated on Biotech, unlike the quests tab.</b> Mechanitors, control groups, bandwidth and work modes
    /// are all Biotech. Without it there are no mechs to command and vanilla's own tab draws nothing either,
    /// so ours should not be on the bar.
    /// </summary>
    internal static class MechTabs
    {
        internal const string OurDefName = "Gideon_Mechs";

        /// <summary>Vanilla's, which this replaces.</summary>
        internal const string VanillaDefName = "Mechs";

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
            {
                UIGuard.Report("Mechs.NoDef", new System.InvalidOperationException(
                        "MainButtonDef " + OurDefName + " is missing from the def database."),
                    "The mech tab's own button is missing, so RimWorld's is left in place. Reinstalling the "
                    + "mod restores it.");
            }

            return ours;
        }

        internal static bool Available
        {
            get { return ModsConfig.BiotechActive && Ours() != null; }
        }
    }
}
