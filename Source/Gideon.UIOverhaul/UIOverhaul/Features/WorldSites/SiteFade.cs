using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Gideon.UIOverhaul.Features.WorldSites
{
    /// <summary>
    /// Puts a clock on the markers a colony leaves behind, so the planet does not fill up with them.
    ///
    /// <b>The clock is RimWorld's own, and so is the removal.</b> <c>TimeoutComp</c> already exists, already
    /// counts down, already prints its remaining time in the inspect pane, and already refuses to remove anything
    /// that has a map loaded. All this feature does is set the end tick on it. Nothing of ours runs in the world
    /// tick, nothing of ours calls <c>Destroy</c>, and the only new code in the removal path is the arithmetic
    /// that works out when.
    ///
    /// <b>Which meant the comp had to be on the def rather than made per object.</b> A world object reads its
    /// comps from <c>def.comps</c> in <c>ExposeData</c> while loading, so adding the properties to three defs at
    /// startup gives the comp to every marker in every existing save as well as to every new one. Doing it the
    /// other way -- our own component holding a dictionary of end ticks -- would have meant our own scribing, our
    /// own destroy call and our own inspect line, three times the code for the same behaviour. See
    /// <see cref="SiteFadeClocks"/>.
    ///
    /// <b>The lifespan is measured from when the marker appeared, not from when the setting was chosen.</b>
    /// Otherwise turning the feature on does nothing for a year and the markers already cluttering the map --
    /// the entire reason anybody would turn it on -- outlive everything created afterwards. The consequence is
    /// that choosing a lifespan shorter than the age of what is already out there removes it within the hour,
    /// which is why the options window counts those out before the choice is made rather than after.
    ///
    /// <b>And since thirty days is the default, the first load of a long game clears its old markers without
    /// anybody choosing anything.</b> That is deliberate -- asked for on 2026-08-23 -- and it is the one thing
    /// about this feature worth knowing before it runs, because a planet that has been collecting markers for
    /// four years loses nearly all of them in the first hour. Nothing of value goes with them: these markers
    /// hold no map, no items and no pawns, and the only information on one is where you were and when.
    ///
    /// <b>Nothing is announced.</b> A marker fading is not news, and vanilla's own thirty day camp timeout says
    /// nothing either. A letter per fade on a planet with a dozen of them would be the feature making more noise
    /// than the thing it exists to clean up.
    /// </summary>
    internal static class SiteFade
    {
        /// <summary>
        /// Whether any kind is set to something other than what RimWorld does on its own.
        ///
        /// <b>Compared against vanilla rather than against the default, and that stopped being the same test on
        /// 2026-08-23</b> when the three kinds RimWorld keeps forever were defaulted to thirty days. Against the
        /// default this would answer no on a fresh install and the sweep would return without setting a single
        /// clock -- a feature that is on, says it is on, and does nothing. Against vanilla it answers yes for
        /// those three and no for the abandoned camp while that row sits on the thirty days the game already set,
        /// which is exactly the work there is to do.
        /// </summary>
        internal static bool Asked
        {
            get
            {
                List<SiteFadeKind> kinds = SiteFadeKinds.All;

                for (int i = 0; i < kinds.Count; i++)
                {
                    if (SiteFadeKinds.Days(kinds[i]) != kinds[i].Vanilla)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Sets or clears the clock on every marker the settings speak for.
        ///
        /// Idempotent by construction: the end tick is computed from the marker's own creation tick, so running
        /// this every hour, or twice in a row, or right after a setting changes, all arrive at the same answer.
        /// That is also what makes it safe to recompute rather than remember whether we set a particular clock.
        /// </summary>
        internal static void ReconcileAll()
        {
            UIGuard.Try("WorldSites.ReconcileAll", () =>
            {
                if (Current.ProgramState != ProgramState.Playing || Find.World == null)
                    return;

                if (!Asked)
                    return;

                List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;

                for (int i = 0; i < objects.Count; i++)
                    Reconcile(objects[i]);
            }, null);
        }

        /// <summary>
        /// One marker's clock.
        ///
        /// Unguarded on purpose: the two callers are guarded, and a guard per object would report the same fault
        /// once per marker per hour.
        /// </summary>
        internal static void Reconcile(WorldObject site)
        {
            if (site == null || site.Destroyed)
                return;

            SiteFadeKind kind = SiteFadeKinds.For(site.def);

            if (kind == null)
                return;

            TimeoutComp clock = site.GetComponent<TimeoutComp>();

            if (clock == null)
                return;

            // A protected marker is left exactly as it is, rather than having its clock stopped. Stopping looks
            // like the cautious choice and is not: a quest that set a timeout of its own would have it silently
            // cancelled, and a quest waiting on the removal it asked for would wait forever. Declining to manage
            // quest objects at all is the rule, in both directions.
            if (Protected(site))
                return;

            int days = SiteFadeKinds.Days(kind);

            if (days <= 0)
            {
                if (clock.Active)
                    clock.StopTimeout();

                return;
            }

            int left = Created(site) + days * GenDate.TicksPerDay - Find.TickManager.TicksGame;

            // One tick rather than zero or a negative, so an already-overdue marker is removed by the comp's own
            // next tick. Handing it a past end tick would work, but it would also mean this method deciding that
            // something is gone, and the whole point is that the game's own comp decides that.
            clock.StartTimeout(left < 1 ? 1 : left);
        }

        /// <summary>
        /// Whether a marker must be left alone whatever the settings say.
        ///
        /// <b>Quest tags are the guard that matters.</b> A quest that points at a world object holds it by
        /// reference and signals off it, and removing one out from under a quest is how a mod breaks somebody's
        /// campaign. None of the four kinds is used by a vanilla quest today -- the gravcore quest that reads as
        /// an abandoned settlement generates its own site with an <c>AbandonedSettlement</c> site part, not one
        /// of these markers -- but another mod's quest may, and the test costs a null check.
        ///
        /// <b>A loaded map is the other,</b> and it is close to vacuous: all four of these defs say
        /// <c>canHaveMap</c> false, and <c>TimeoutComp</c> refuses to remove anything with a map in any case. It
        /// is here because a mod may make one of them enterable, and because the cost is a cast.
        ///
        /// <b>A caravan parked on the tile is deliberately not a reason.</b> The marker holds nothing and does
        /// nothing; it cannot be entered, it has no gizmos, and it does not block settling. Waiting for the
        /// caravan to move on would be guarding against a loss that does not exist.
        /// </summary>
        internal static bool Protected(WorldObject site)
        {
            if (site.questTags != null && site.questTags.Count > 0)
                return true;

            MapParent parent = site as MapParent;

            return parent != null && parent.HasMap;
        }

        /// <summary>
        /// The tick a marker was created on, which is the field RimWorld already keeps for this.
        ///
        /// <c>creationGameTicks</c> starts at -1 and is set when the object is made, so a marker from a save that
        /// somehow never got one is treated as new rather than as infinitely old. Fading something the moment the
        /// feature is switched on because of a missing field is the one outcome worth ruling out.
        /// </summary>
        private static int Created(WorldObject site)
        {
            return site.creationGameTicks > 0 ? site.creationGameTicks : Find.TickManager.TicksGame;
        }

        /// <summary>
        /// How many markers are on a clock, and how many of those have already run out.
        ///
        /// For the line under the settings rows. The second number is the one worth drawing: it is the count of
        /// markers that will be gone by the time the player has finished reading, and it exists because the
        /// alternative is a setting that quietly deletes six things on the way out of the window.
        /// </summary>
        internal static int Counting(out int immediate)
        {
            int counted = 0;
            int overdue = 0;

            UIGuard.Try("WorldSites.Counting", () =>
            {
                if (Current.ProgramState != ProgramState.Playing || Find.World == null)
                    return;

                List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;

                for (int i = 0; i < objects.Count; i++)
                {
                    WorldObject site = objects[i];

                    if (site == null || site.Destroyed || SiteFadeKinds.For(site.def) == null)
                        continue;

                    TimeoutComp clock = site.GetComponent<TimeoutComp>();

                    if (clock == null || !clock.Active)
                        continue;

                    counted++;

                    if (clock.TicksLeft <= GenDate.TicksPerHour)
                        overdue++;
                }
            }, null);

            immediate = overdue;

            return counted;
        }
    }
}
