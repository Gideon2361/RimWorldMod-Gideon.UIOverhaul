using System;
using System.Collections.Generic;
using System.Text;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIFramework.Caching
{
    /// <summary>
    /// Every cache in the mod, in one list, so they can be cleared and pruned together.
    ///
    /// <b>What this is not.</b> It does not refresh anything on a timer. Each cache rebuilds a value when that value
    /// is next asked for and its interval has elapsed, so a cache belonging to a panel nobody has open costs
    /// nothing at all. A controller that pumped every cache each frame would pay for the whole mod on every frame
    /// regardless of what was on screen, which is the cost these caches exist to avoid rather than a way to avoid
    /// it. What this does own is the things that are genuinely global: dropping everything when the game changes
    /// underneath us, dropping entries whose subject has gone away, and being able to say what is registered.
    ///
    /// <b>Clear on anything that invalidates the world.</b> Loading a save, generating a map, reloading defs. A
    /// cached row describing a colonist from the previous save is worse than no row.
    ///
    /// <b>Pruning is what keeps this honest over a long colony.</b> A cache keyed by pawn accumulates an entry for
    /// every colonist who ever passed through, and none of them is ever read again. <see cref="Prune"/> asks each
    /// cache to drop keys whose subject no longer exists.
    /// </summary>
    public static class UICacheController
    {
        private static readonly List<IUICache> Caches = new List<IUICache>();

        /// <summary>
        /// How often pruning runs, in real seconds.
        ///
        /// Generous on purpose. A dead entry costs a dictionary slot and nothing else -- nothing reads it, so it
        /// cannot show stale data -- which makes this housekeeping rather than correctness. Running it often would
        /// walk every key in every cache for no benefit.
        /// </summary>
        public const float PruneIntervalSeconds = 60f;

        private static float nextPruneAt;

        private static float lastObservedRealtime = -1f;

        /// <summary>
        /// Seconds of <i>running</i> game time since launch: real seconds, but only counted while the game is not
        /// paused.
        ///
        /// <b>Why a second clock exists.</b> Most of what these caches hold describes a simulation that is not
        /// advancing while the game is paused: a colonist's mood, a zone's projected yield, a job report. Measuring
        /// their intervals against the wall clock means rebuilding them on a schedule while nothing they describe can
        /// possibly have changed, which is waste in the one situation where a player is most likely to be sitting
        /// still with a panel open.
        ///
        /// Anything the player can change while paused is handled by invalidation instead, not by the interval, so
        /// freezing costs no responsiveness.
        ///
        /// This is not the right clock for everything -- a readout of something that moves in real time wants the
        /// wall clock -- so it is opt in, per cache.
        /// </summary>
        public static float UnpausedSeconds { get; private set; }

        /// <summary>
        /// Called by every cache's constructor. Nothing else needs to call it.
        ///
        /// Caches are static fields, so this happens once per cache when its declaring type is first touched, and
        /// the list is complete by the time anything draws.
        /// </summary>
        internal static void Register(IUICache cache)
        {
            if (cache != null && !Caches.Contains(cache))
                Caches.Add(cache);
        }

        /// <summary>Drops every cached value in the mod.</summary>
        public static void ClearAll()
        {
            foreach (IUICache cache in Caches)
                UIGuard.Try("Cache.Clear." + cache.Name, cache.Clear);
        }

        /// <summary>
        /// Drops everything held about one subject, in every cache, at the moment it ceases to exist.
        ///
        /// <b>This is the difference between reacting and being told.</b> Pruning finds a dead subject eventually,
        /// by asking every key whether it is still valid on a timer; the cache's own exception handling catches one
        /// that dies between reads. Both are backstops. This is the direct route: a pawn is destroyed, every cache
        /// forgets them in the same frame, and nothing is ever in a position to ask about them.
        ///
        /// Broadcast to every cache rather than routed, because the controller does not know which caches are keyed
        /// by pawns. Each one type-tests the subject and ignores it if it is not theirs. That costs a type check per
        /// cache on an event that happens when something dies, which is nothing.
        /// </summary>
        public static void Forget(object subject)
        {
            if (subject == null)
                return;

            foreach (IUICache cache in Caches)
                UIGuard.Try("Cache.Forget." + cache.Name, () => cache.Forget(subject));
        }

        /// <summary>
        /// Drops entries whose subject has gone away, at most once per <see cref="PruneIntervalSeconds"/>.
        ///
        /// Safe and cheap to call every frame; it returns immediately until the interval has elapsed. Pumped from
        /// the same per-frame hook as the config watcher.
        /// </summary>
        public static void Tick(float realtimeNow)
        {
            // Nothing at all while a long event is running, which covers loading a save, generating a map and
            // switching maps. Three reasons, any one of which would be enough:
            //
            // The game is not in a steady state. ProgramState is already Playing partway through loading a save
            // while Current.Game is still being assembled, so anything reached through Find may not exist yet --
            // which is exactly how this method threw a NullReferenceException every frame of a load.
            //
            // Pruning would ask every key whether its subject still exists, against a world that is half built.
            // A pawn or zone whose map has not finished loading can answer "no" and be dropped for no reason.
            //
            // And there is nothing to gain. Nothing is simulating, nothing is drawing, and the clock below is
            // meant to measure running time, which a load is not.
            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                // The clock loses its place deliberately. Without this, the first call after a thirty second load
                // would find a thirty second gap since the last stamp and add all of it as running time, expiring
                // every cache at once. Forgetting the stamp costs one frame's worth of elapsed time and makes the
                // resumed clock continuous.
                lastObservedRealtime = -1f;
                return;
            }

            AdvanceUnpausedClock(realtimeNow);

            if (realtimeNow < nextPruneAt)
                return;

            nextPruneAt = realtimeNow + PruneIntervalSeconds;

            foreach (IUICache cache in Caches)
                UIGuard.Try("Cache.Prune." + cache.Name, cache.Prune);
        }

        /// <summary>
        /// Adds the time since the previous frame to <see cref="UnpausedSeconds"/>, unless the game is paused.
        ///
        /// The delta is derived from successive real timestamps rather than read from Unity's frame delta, so this
        /// needs nothing passed in beyond the clock the caller already has.
        /// </summary>
        private static void AdvanceUnpausedClock(float realtimeNow)
        {
            float previous = lastObservedRealtime;
            lastObservedRealtime = realtimeNow;

            // First call, or a clock that went backwards. Either way there is no trustworthy delta to add.
            if (previous < 0f || realtimeNow <= previous)
                return;

            if (Paused)
                return;

            UnpausedSeconds += realtimeNow - previous;
        }

        /// <summary>
        /// Whether the simulation is stopped. True outside a running game as well, since nothing is advancing at the
        /// main menu either.
        ///
        /// <b>Every uncertain answer is "paused", and that direction is chosen rather than incidental.</b> Reading
        /// this wrongly as paused costs a cache one skipped interval, which nobody can see; reading it wrongly as
        /// running spends work on values that cannot have changed. So anything that cannot be established -- no
        /// game, a game still being assembled, or a vanilla property that throws -- counts as stopped.
        ///
        /// <b>Why a try/catch around one property read.</b> <c>TickManager.Paused</c> is not the simple field it
        /// looks like: it falls through to <c>ForcePaused</c>, which reaches <c>Find.WindowStack</c>,
        /// <c>Find.TilePicker</c> and <c>Find.GravshipController</c> -- and the last of those goes through
        /// <c>Find.World</c>, which dereferences <c>Current.Game.World</c> with no guard of its own. Partway through
        /// loading a save, <c>ProgramState</c> is already <c>Playing</c> while <c>World</c> is still null, so the
        /// state test below is not enough on its own and was not: this threw once per frame for the length of a load.
        /// <c>Tick</c> now returns before reaching here during a long event, which is the real fix; this is the
        /// belt-and-braces version for any other moment vanilla decides a half-built game is playing.
        /// </summary>
        private static bool Paused
        {
            get
            {
                if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
                    return true;

                try
                {
                    TickManager ticks = Find.TickManager;

                    return ticks == null || ticks.Paused;
                }
                catch (Exception)
                {
                    // Not reported. This is a question about the game's state asked from a per-frame hook, and the
                    // only honest answer when the game cannot say is "nothing is running" -- which is what the
                    // caller wants anyway. Logging it would flood a load screen to say so.
                    return true;
                }
            }
        }

        /// <summary>
        /// What is registered, what interval each runs at, and how much each is holding.
        ///
        /// For the debug logging switch rather than for the player. The reason it is worth having: an interval is
        /// declared at the cache's construction, scattered across features, and this is the only place the whole
        /// set can be read at once when one of them turns out to be refreshing more often than intended.
        /// </summary>
        public static string Describe()
        {
            StringBuilder text = new StringBuilder();

            text.Append(UILogTag.Prefix).Append(Caches.Count).Append(" caches registered:");

            foreach (IUICache cache in Caches)
            {
                text.Append("\n  ").Append(cache.Name)
                    .Append("  every ").Append(cache.IntervalSeconds).Append("s")
                    .Append(", holding ").Append(cache.Count);
            }

            return text.ToString();
        }
    }
}
