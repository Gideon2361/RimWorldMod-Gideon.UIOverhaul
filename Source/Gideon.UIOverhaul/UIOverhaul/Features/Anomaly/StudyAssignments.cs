using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Anomaly
{
    /// <summary>
    /// Who is allowed to study a given anomaly, when the player wants that decided rather than left to whoever
    /// is nearest.
    ///
    /// <b>Vanilla has no notion of this.</b> <c>WorkGiver_DarkStudyInteract</c> offers the job to any colonist
    /// whose work settings include Dark Study and who can reach the thing, so the pawn who happens to be closest
    /// takes it. That is usually what you want and occasionally exactly what you do not: a low skill colonist
    /// wandering into the containment room is a different proposition from the one you meant to send.
    ///
    /// <b>Assignment is a restriction, not an order.</b> Naming a pawn stops everybody else being offered the
    /// job; it does not make that pawn go and do it. They pick it up through their own work priorities like any
    /// other job, so the feature composes with the work tab rather than fighting it, and a pawn who cannot
    /// currently reach the thing simply means nobody studies it until they can.
    ///
    /// <b>Stored on the game rather than on the thing.</b> A <c>ThingComp</c> cannot be added to another mod's
    /// or the base game's def without a patch on every studiable def, and a comp added that way is lost from any
    /// save written before the patch existed. A <see cref="GameComponent"/> is instantiated automatically for
    /// every game, needs no def at all, and carries its own save data.
    ///
    /// <b>Both sides are saved by reference,</b> so the dictionary survives a reload as the same entity and the
    /// same colonist rather than as copies. Entries whose thing has been destroyed or whose colonist has died
    /// are dropped on load and whenever one is read, because an assignment to a corpse would silently prevent
    /// anybody at all from studying that entity, which looks exactly like a bug.
    /// </summary>
    public class StudyAssignments : GameComponent
    {
        private Dictionary<Thing, Pawn> assigned = new Dictionary<Thing, Pawn>();

        // Scribe_Collections needs somewhere to stage the keys and values while it works. These hold nothing
        // between saves and are not read anywhere else.
        private List<Thing> scribeThings;
        private List<Pawn> scribePawns;

        /// <summary>Required by RimWorld: every GameComponent is constructed with the game it belongs to.</summary>
        public StudyAssignments(Game game)
        {
        }

        private static StudyAssignments Component =>
            UIGuard.Try("Anomaly.StudyAssignments.Component",
                () => Current.Game?.GetComponent<StudyAssignments>(), null, null);

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref assigned, "gideonStudyAssignments", LookMode.Reference, LookMode.Reference,
                ref scribeThings, ref scribePawns);

            if (assigned == null)
                assigned = new Dictionary<Thing, Pawn>();

            // References are only resolved by the time PostLoadInit runs, so anything checked earlier would still
            // be null and every entry would look dead.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Prune();
        }

        /// <summary>The colonist allowed to study this, or null when anybody may.</summary>
        internal static Pawn AssignedTo(Thing thing)
        {
            if (thing == null)
                return null;

            StudyAssignments component = Component;

            if (component == null)
                return null;

            Pawn pawn;

            if (!component.assigned.TryGetValue(thing, out pawn))
                return null;

            if (Usable(pawn))
                return pawn;

            // Forgotten on the way past rather than left to rot. An assignment nobody can fulfil reads as the
            // entity simply never being studied again, with nothing on screen explaining why.
            component.assigned.Remove(thing);

            return null;
        }

        /// <summary>Restricts study of this thing to one colonist. A null pawn lifts the restriction.</summary>
        internal static void Assign(Thing thing, Pawn pawn)
        {
            if (thing == null)
                return;

            StudyAssignments component = Component;

            if (component == null)
                return;

            if (pawn == null)
                component.assigned.Remove(thing);
            else
                component.assigned[thing] = pawn;
        }

        private static bool Usable(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed;
        }

        private void Prune()
        {
            List<Thing> stale = null;

            foreach (KeyValuePair<Thing, Pawn> entry in assigned)
            {
                if (entry.Key != null && !entry.Key.Destroyed && Usable(entry.Value))
                    continue;

                if (stale == null)
                    stale = new List<Thing>();

                stale.Add(entry.Key);
            }

            if (stale == null)
                return;

            // Collected first and removed after, because a dictionary cannot be written to while it is being
            // walked.
            foreach (Thing thing in stale)
                assigned.Remove(thing);
        }
    }
}
