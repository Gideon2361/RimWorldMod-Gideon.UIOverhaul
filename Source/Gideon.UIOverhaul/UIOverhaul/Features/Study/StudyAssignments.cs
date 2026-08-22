using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Study
{
    /// <summary>
    /// Who is allowed to study which entity, when the player has named somebody.
    ///
    /// <b>Asked for on 2026-08-22:</b> a button that assigns one colonist to an entity so nobody else studies it.
    /// The reason is that studying is not interchangeable work. Anomaly research goes faster in the hands of the
    /// pawn with the Intellectual skill, every study interaction on a live entity is a chance for it to do
    /// something to whoever is standing there, and some of them convert the studier. Vanilla offers one switch,
    /// study on or off, so the choice available today is between everybody and nobody.
    ///
    /// <b>A GameComponent rather than a comp field.</b> <c>CompStudiable</c> is vanilla and cannot grow a field,
    /// and the alternative of a parallel table keyed on thing id would have to be told when things are destroyed.
    /// A component scribes by reference, which means the game's own cross-reference pass resolves both ends and
    /// silently drops an entry whose entity or colonist did not survive the load. That is the behavior wanted:
    /// an assignment to a dead pawn is not an assignment.
    ///
    /// <b>Held on the game rather than the map,</b> because an entity travels. A creature carried to another map
    /// on a gravship keeps the colonist it was assigned to, and a map component would have lost that.
    ///
    /// <b>The assignment is on the entity, not on the platform it is held by.</b> Vanilla's studiable comp lives
    /// on the entity, the gizmo comes from that comp, and platforms are furniture the entity can be moved between.
    /// Keying it to the platform would mean an entity losing its studier by being carried across the room.
    /// </summary>
    internal class StudyAssignments : GameComponent
    {
        private static StudyAssignments current;

        private Dictionary<Thing, Pawn> assigned = new Dictionary<Thing, Pawn>();

        private List<Thing> keys;
        private List<Pawn> values;

        public StudyAssignments(Game game)
        {
            current = this;
        }

        /// <summary>
        /// The live component, or null when no game is running.
        ///
        /// <b>Taken from the constructor rather than looked up per call.</b> RimWorld builds one of these per
        /// game and builds it before anything can ask, so the newest one is always the running game's; the only
        /// stale case is having quit to the menu, which the game check covers. A <c>GetComponent</c> walk per
        /// read would be cheap but pointless.
        /// </summary>
        private static StudyAssignments Live => Verse.Current.Game == null ? null : current;

        /// <summary>
        /// Whether anything at all has been assigned.
        ///
        /// The cheap test the work giver filter opens with: a colony that has never used the button pays one field
        /// read per scan rather than a lookup per candidate entity.
        /// </summary>
        internal static bool AnyAssigned
        {
            get
            {
                StudyAssignments component = Live;

                return component != null && component.assigned != null && component.assigned.Count > 0;
            }
        }

        /// <summary>
        /// Who may study this entity, or null when anybody may.
        ///
        /// Null is the default and the common case, so every caller treats it as "no restriction" rather than
        /// having to ask whether an assignment exists first.
        /// </summary>
        internal static Pawn AssignedTo(Thing entity)
        {
            return UIGuard.Try("Study.Assigned", () =>
            {
                StudyAssignments component = Live;

                if (component == null || entity == null)
                    return null;

                Pawn studier;

                if (!component.assigned.TryGetValue(entity, out studier))
                    return null;

                // A studier who has died, left, or been captured is no assignment at all. Cleared on the way out
                // rather than by a tick that walks the table, since this is the only place the answer is read.
                if (studier == null || studier.Dead || studier.Destroyed
                    || studier.Faction != Faction.OfPlayer)
                {
                    component.assigned.Remove(entity);

                    return null;
                }

                return studier;
            }, null, null);
        }

        /// <summary>Names the only colonist allowed to study this entity, or null to let anybody.</summary>
        internal static void Assign(Thing entity, Pawn studier)
        {
            UIGuard.Try("Study.Assign", () =>
            {
                StudyAssignments component = Live;

                if (component == null || entity == null)
                    return;

                if (studier == null)
                    component.assigned.Remove(entity);
                else
                    component.assigned.SetOrAdd(entity, studier);
            }, "The studier was not assigned.");
        }

        /// <summary>
        /// Whether this pawn is allowed to study this entity by their own choice.
        ///
        /// <b>The question the work givers ask,</b> and the reason it is phrased about autonomous work: a direct
        /// order from the player is not covered by this and is not meant to be. See
        /// <see cref="Patch_StudyAssignment"/>.
        /// </summary>
        internal static bool Allowed(Thing entity, Pawn pawn)
        {
            Pawn studier = AssignedTo(entity);

            return studier == null || studier == pawn;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref assigned, "gideonStudyAssignments", LookMode.Reference, LookMode.Reference,
                ref keys, ref values);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Clean();
        }

        /// <summary>
        /// Drops entries the load could not resolve.
        ///
        /// <c>Scribe_Collections</c> with reference look modes leaves a null on either side when the thing it
        /// pointed at is gone, which is what happens when a mod that added an entity is removed. A dictionary with
        /// a null key throws on the next lookup, so it is emptied of those here rather than defended against
        /// everywhere.
        /// </summary>
        private void Clean()
        {
            if (assigned == null)
            {
                assigned = new Dictionary<Thing, Pawn>();

                return;
            }

            assigned.RemoveAll(pair => pair.Key == null || pair.Value == null);
        }
    }
}
