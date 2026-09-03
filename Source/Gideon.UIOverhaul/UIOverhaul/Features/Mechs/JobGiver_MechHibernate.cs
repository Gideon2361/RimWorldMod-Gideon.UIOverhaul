using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// Parks an idle work mech for a while instead of letting it wander and re-scan for work.
    ///
    /// <b>What it replaces.</b> RimWorld's mech think tree ends a player controlled mech with an idle branch
    /// of <c>JobGiver_WanderColony</c> at <c>maxDanger None</c>. The mech takes a short walk, the walk ends,
    /// and the whole tree runs from the top again: seek allowed area, look for a recharger, then
    /// <c>JobGiver_Work</c> twice, once with <c>emergency</c> set and once without. Each of those two is a
    /// full pass over every work giver the mech has against everything on the map, and it happens for every
    /// mech that has run out of work.
    ///
    /// <b>A nap, not a coma.</b> This issues an ordinary job, so everything that ends an ordinary job ends
    /// it: the constant think tree at its thirty tick interval, <c>CheckForJobOverride</c>, being drafted,
    /// danger, and the job's own expiry. <c>JobDriver_Wait</c> also runs <c>CheckForAutoAttack</c> every
    /// tick, so a hibernating mech still shoots at whatever walks up to it. The interval is a ceiling on how
    /// long the mech goes without asking, not a promise to sleep through anything.
    ///
    /// <b>The zone is optional and its absence is not an error.</b> <c>MechHibernateZone</c> is matched by
    /// <c>AreaManager.GetLabeled</c>, which compares labels exactly, so a player who typed it differently
    /// gets no zone. That is why the mech tab's settings dialog reports what it actually found rather than
    /// leaving a typo to look like a bug here.
    /// </summary>
    public class JobGiver_MechHibernate : ThinkNode_JobGiver
    {
        /// <summary>
        /// The area a player draws to say where mechs should idle. Matched exactly, case included.
        /// </summary>
        public const string ZoneLabel = "MechHibernateZone";

        /// <summary>
        /// How long a mech waits before asking for work again.
        ///
        /// 1200 ticks is twenty seconds at normal speed, or a little under half an in-game hour. Long enough
        /// that the scan rate falls by an order of magnitude, short enough that a mech notices a new job
        /// before a player watching it would.
        /// </summary>
        public const int Ticks = 1200;

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Guarded because this runs from RimWorld's own think tree, on every player mech, several times a
            // second. An exception here is not a panel that fails to draw: it is a mech with no job at all,
            // over and over, and a log that fills in seconds.
            return UIGuard.Try<Job>("Mechs.Hibernate", () => Consider(pawn), null, null);
        }

        private static Job Consider(Pawn pawn)
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            if (settings == null || !settings.mechHibernation)
                return null;

            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.Drafted)
                return null;

            if (pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid)
                return null;

            // Only mechs that answer to somebody. A feral mech reaching the idle branch is not ours to park,
            // and a hostile one certainly is not.
            if (pawn.Faction != Faction.OfPlayer || pawn.GetOverseer() == null)
                return null;

            // Gestating mechs are not on the map in any meaningful sense and must not be given jobs.
            if (pawn.IsGestating())
                return null;

            // Work mode only. A group set to escort is following somebody, one set to recharge is walking to
            // a charger, and one set to dormant self charge is already asleep in the game's own way. Parking
            // a mech in any of those would be overriding an order the player gave.
            if (pawn.GetMechWorkMode() != MechWorkModeDefOf.Work)
                return null;

            // Combat mechs patrol, which is a behaviour rather than an accident of the tree, and this node
            // sits below that branch anyway. Being explicit means a tree that reordered would not silently
            // stop them patrolling.
            if (!pawn.RaceProps.IsWorkMech)
                return null;

            IntVec3 cell = Destination(pawn);

            if (!cell.IsValid)
                return null;

            Job job = JobMaker.MakeJob(MechDefOf.Gideon_MechHibernate, cell);

            job.expiryInterval = Ticks;

            // Checked rather than ended outright when it expires, so a mech whose work arrived while it was
            // waiting takes that work rather than standing through another interval first.
            job.checkOverrideOnExpire = true;

            return job;
        }

        /// <summary>
        /// Where to hibernate: the zone if there is one it can reach, otherwise where it stands.
        ///
        /// <b>The allowed area still wins.</b> <c>JobGiver_SeekAllowedArea</c> sits above this in the tree
        /// and the control group's own area restriction still applies, so a hibernate zone drawn outside it
        /// is not somewhere this mech may go. Standing still is the honest answer then, and the settings
        /// dialog says which of the two is happening.
        /// </summary>
        private static IntVec3 Destination(Pawn pawn)
        {
            IntVec3 here = pawn.Position;

            if (pawn.Map == null || pawn.Map.areaManager == null)
                return here;

            Area zone = pawn.Map.areaManager.GetLabeled(ZoneLabel);

            if (zone == null)
                return here;

            // Already inside it. Walking to another cell of the same zone would be motion for its own sake,
            // which is the thing this exists to stop.
            if (zone[here])
                return here;

            Area allowed = pawn.playerSettings == null
                ? null
                : pawn.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap;

            IntVec3 best = IntVec3.Invalid;
            float nearest = float.MaxValue;

            // The nearest standable cell of the zone this mech may actually reach. Walked rather than picked
            // at random: a mech that crosses the whole colony to idle has spent more on the walk than the
            // setting saves, and the nearest cell is also the one a player would expect it to take.
            foreach (IntVec3 cell in zone.ActiveCells)
            {
                if (allowed != null && !allowed[cell])
                    continue;

                float distance = (cell - here).LengthHorizontalSquared;

                if (distance >= nearest)
                    continue;

                if (!cell.Standable(pawn.Map) || cell.IsForbidden(pawn))
                    continue;

                if (!pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None))
                    continue;

                best = cell;
                nearest = distance;
            }

            return best.IsValid ? best : here;
        }
    }
}
