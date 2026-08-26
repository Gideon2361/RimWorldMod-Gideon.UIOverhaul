using System.Collections.Generic;
using System.Text;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>
    /// Clears mutant trackers that have lost their def, once per load.
    ///
    /// <b>The fault this repairs.</b> <c>Pawn.IsMutant</c> is only <c>mutant != null</c>, but at least twenty
    /// places in <c>Pawn</c> gate on it and then dereference <c>mutant.Def</c> -- drafting, opening doors,
    /// breathing, aging, work tags, the name prefix, the inspect string, death handling -- as does
    /// <c>Pawn_FlightTracker.CanEverFly</c>. So a tracker whose def is null is a pawn that throws a
    /// NullReferenceException on the next thing that asks any of those questions, and keeps throwing, once per
    /// job or once per tick, for the rest of the session.
    ///
    /// <b>Seen in the wild on 2026-08-25</b> as "Exception ticking Chicken383159" out of
    /// <c>Pawn_FlightTracker.Notify_JobStarted</c>, on a save running Mycohazard, which turns animals into
    /// fungal ghouls by assigning <c>pawn.mutant = new Pawn_MutantTracker(pawn, DefsOf.DE_FungalGhoul, ...)</c>.
    /// Both of that mod's mutant defs still exist, so this is not a def that was deleted; the tracker is scribed
    /// with <c>Scribe_Defs.Look(ref def, "shamblerType")</c>, which assigns null for any name it cannot resolve,
    /// and a rename, a load order that leaves the def out, or Anomaly being switched off all reach the same
    /// state.
    ///
    /// <b>Repairing the state rather than guarding the reads.</b> The alternative was a postfix on
    /// <c>Pawn.IsMutant</c> ANDing in a def check, which would cover every one of those twenty sites at once. It
    /// was rejected on cost and honesty: <c>IsMutant</c> is a one field null test the JIT inlines everywhere and
    /// is read from tick and render paths through <c>IsSubhuman</c>, <c>IsShambler</c> and <c>IsGhoul</c>, so
    /// patching it would slow every game to protect the few that need it, and it would hide the fault from the
    /// mod author who can fix it properly. This runs once, costs nothing afterwards, and leaves healthy mutants
    /// completely alone.
    ///
    /// <b>What is lost.</b> A tracker with no def cannot be repaired, only removed: nothing records which mutant
    /// it was. The pawn goes back to being an ordinary member of its race, and hediffs the mutation granted stay
    /// on it. That is untidy, and it is a great deal better than an exception every time the pawn takes a job.
    ///
    /// <b>Reported rather than fixed quietly,</b> and deliberately as a warning. A player seeing this wants to
    /// know their save was altered, and the mod author wants to know the state exists at all.
    ///
    /// <b>Load only.</b> Nothing here watches for the state appearing mid-game. It is created by whatever mod
    /// owns the mutation, and a pawn that reaches this state while playing is repaired at the next load rather
    /// than by a scan we would have to keep running forever.
    /// </summary>
    public class MutantTrackerRepair : GameComponent
    {
        /// <summary>How many names go in the report before it stops listing them.</summary>
        private const int NamesShown = 12;

        /// <summary>Required by RimWorld: every GameComponent is constructed with the game it belongs to.</summary>
        public MutantTrackerRepair(Game game)
        {
        }

        /// <summary>
        /// After a load, and not after a new game.
        ///
        /// A game that has just been generated has no scribed defs to have failed to resolve, so there is
        /// nothing for this to find. <c>LoadedGame</c> also runs after PostLoadInit, which matters: earlier than
        /// that, a def reference that will resolve perfectly well is still null and every mutant in the save
        /// would look broken.
        /// </summary>
        public override void LoadedGame()
        {
            UIGuard.Try("Diagnostics.MutantRepair", Repair,
                "Mutant trackers were not checked for missing defs after loading.");
        }

        /// <summary>
        /// Deliberately not gated on <c>ModsConfig.AnomalyActive</c>.
        ///
        /// A save written with Anomaly on and loaded with it off is one of the ways this state happens, and it
        /// is the way a player is least likely to connect to the errors that follow. Skipping the check exactly
        /// when Anomaly is absent would skip the case most in need of it.
        /// </summary>
        private static void Repair()
        {
            List<Pawn> pawns = PawnsFinder.All_AliveOrDead;

            if (pawns == null)
                return;

            List<string> cleared = null;
            int total = 0;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];

                if (pawn == null || pawn.mutant == null || pawn.mutant.Def != null)
                    continue;

                // Cleared before the name is read, not after. LabelShortCap goes through Pawn.LabelNoCount,
                // which asks mutant.Def.overrideLabel, so naming the pawn first would throw the very exception
                // this exists to stop.
                pawn.mutant = null;

                total++;

                if (cleared == null)
                    cleared = new List<string>();

                if (cleared.Count < NamesShown)
                    cleared.Add(pawn.ThingID ?? "unknown");
            }

            if (cleared == null)
                return;

            Report(cleared, total);
        }

        /// <summary>
        /// Says what was done and why, naming pawns by <c>ThingID</c>.
        ///
        /// The id rather than the label because that is what the exception reported: somebody who saw
        /// "Exception ticking Chicken383159" can match this line to it directly, and a label could not be read
        /// safely before the tracker was cleared anyway.
        /// </summary>
        private static void Report(List<string> cleared, int total)
        {
            StringBuilder built = new StringBuilder();

            built.Append("Gideon's UI Overhaul cleared ").Append(total)
                .Append(total == 1 ? " mutant tracker" : " mutant trackers")
                .Append(" with no def, which would otherwise have thrown a NullReferenceException on every job "
                        + "or tick for the affected pawns. Anything the mutation granted, such as hediffs, has "
                        + "been left in place.");

            built.Append("\n\nAffected: ");

            for (int i = 0; i < cleared.Count; i++)
            {
                if (i > 0)
                    built.Append(", ");

                built.Append(cleared[i]);
            }

            if (total > cleared.Count)
                built.Append(" and ").Append(total - cleared.Count).Append(" more");

            built.Append("\n\nThis is a def that failed to resolve rather than anything this mod did. It is "
                         + "worth reporting to whichever mod turns pawns into mutants, along with the pawn ids "
                         + "above.");

            Log.Warning(built.ToString());
        }
    }
}
