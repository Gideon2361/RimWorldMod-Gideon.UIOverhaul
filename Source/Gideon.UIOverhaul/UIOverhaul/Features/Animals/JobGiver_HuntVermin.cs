using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Sends a tame hunter after nearby wild game, the way a barn owl earns its keep around a granary.
    ///
    /// <b>Wild prey only, and that is the whole point.</b> The single test that matters is
    /// <c>prey.Faction == null</c>. Vanilla's <c>IsAcceptablePreyFor</c> does not ask, which is why a tamed
    /// predator will eat the colony's chickens and why the game ships an alert about predators in pens. A null
    /// faction means nobody owns it: rats, squirrels, hares. A visiting trader's pack animal and a neighboring
    /// settlement's herd both have factions and are both left alone, which also keeps this from starting an
    /// incident.
    ///
    /// <b>It uses vanilla's judgment about what is safe to attack.</b> <c>FoodUtility.IsAcceptablePreyFor</c>
    /// carries the combat power ratio, the flesh test and the difficulty setting for humanlikes, and all of that
    /// is still wanted here. What this adds is a second, smaller size cap and the faction rule; what it does not
    /// do is invent its own idea of a fair fight.
    ///
    /// <b>Inside the allowed area.</b> An owl assigned to the barn should hunt the barn. The area check uses the
    /// same effective restriction the animals tab writes, so restricting the animal restricts the hunting
    /// without any separate control to learn.
    /// </summary>
    public class JobGiver_HuntVermin : ThinkNode_JobGiver
    {
        /// <summary>
        /// When each pawn may be looked at again, keyed on thing ID rather than on the pawn.
        ///
        /// Keying on <c>Pawn</c> would hold a reference to every hunter that ever lived for as long as the game
        /// runs. Not saved, and it does not need to be: losing it costs one extra scan after a load.
        /// </summary>
        private static readonly Dictionary<int, int> Rested = new Dictionary<int, int>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Guarded because this runs from RimWorld's own think tree, on every tame animal, several times a
            // second. An exception here is not a panel that fails to draw: it is an animal with no job at all,
            // over and over, and a log that fills in seconds.
            return UIGuard.Try<Job>("Animals.HuntVermin", () => Consider(pawn), null, null);
        }

        private static Job Consider(Pawn pawn)
        {
            if (pawn == null || pawn.def == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.Drafted)
                return null;

            VerminHunterProperties props = pawn.def.GetModExtension<VerminHunterProperties>();

            if (props == null)
                return null;

            // Only the colony's own. A wild one already hunts through vanilla's hunger path, and a wild animal
            // that patrols for sport would be a change to how the game's ecology behaves rather than to how a
            // working animal behaves.
            if (pawn.Faction == null || pawn.Faction != Faction.OfPlayer)
                return null;

            if (pawn.needs != null && pawn.needs.food != null
                && pawn.needs.food.CurLevelPercentage > props.huntBelowFood)
                return null;

            // No verb, no hunt. Vanilla checks the same thing first, and without it the job would be issued and
            // then immediately fail.
            if (pawn.meleeVerbs == null || pawn.meleeVerbs.TryGetMeleeVerb(null) == null)
                return null;

            int next;

            if (Rested.TryGetValue(pawn.thingIDNumber, out next) && Find.TickManager.TicksGame < next)
                return null;

            Rested[pawn.thingIDNumber] = Find.TickManager.TicksGame + props.restTicks;

            Pawn prey = Best(pawn, props);

            if (prey == null)
                return null;

            Job job = JobMaker.MakeJob(JobDefOf.PredatorHunt, prey);

            job.killIncappedTarget = true;

            return job;
        }

        /// <summary>The best wild target within reach, or null.</summary>
        private static Pawn Best(Pawn hunter, VerminHunterProperties props)
        {
            if (hunter.Map == null || hunter.Map.mapPawns == null)
                return null;

            Area area = hunter.playerSettings != null
                ? hunter.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap
                : null;

            // AllPawnsSpawned hands back a read-only view rather than the backing list, and taking it as
            // one avoids a defensive copy of every pawn on the map on every scan.
            IReadOnlyList<Pawn> all = hunter.Map.mapPawns.AllPawnsSpawned;
            float reach = props.radius * props.radius;

            Pawn found = null;
            float best = 0f;

            for (int i = 0; all != null && i < all.Count; i++)
            {
                Pawn prey = all[i];

                if (prey == null || prey == hunter || !prey.Spawned || prey.Dead)
                    continue;

                // Nobody owns it. See the class note: this is the test the vanilla one is missing.
                if (prey.Faction != null)
                    continue;

                if (prey.RaceProps == null || prey.RaceProps.Humanlike)
                    continue;

                if (prey.BodySize > props.maxPreyBodySize)
                    continue;

                if ((prey.Position - hunter.Position).LengthHorizontalSquared > reach)
                    continue;

                if (area != null && area.TrueCount > 0 && !area[prey.Position])
                    continue;

                if (prey.IsForbidden(hunter) || !FoodUtility.IsAcceptablePreyFor(hunter, prey))
                    continue;

                float score = FoodUtility.GetPreyScoreFor(hunter, prey);

                if (found != null && score <= best)
                    continue;

                // Reachability last, because it is the expensive one and most candidates have already been
                // ruled out on something cheaper by the time anything gets this far.
                if (!hunter.CanReach(prey, PathEndMode.ClosestTouch, Danger.Deadly))
                    continue;

                best = score;
                found = prey;
            }

            return found;
        }
    }
}
