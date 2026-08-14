using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using UnityEngine;

namespace Gideon.UIFramework.Caching
{
    /// <summary>
    /// One cached attribute of one kind of thing, rebuilt no more often than once per interval.
    ///
    /// <code>
    /// // Declared once, somewhere shared -- not privately inside a panel.
    /// public static readonly UICache&lt;Pawn, float&gt; Mood =
    ///     new UICache&lt;Pawn, float&gt;("Pawn.Mood", 1f, ReadMood, pawn =&gt; pawn.Spawned);
    ///
    /// float mood = PawnAttributes.Mood.Get(pawn);   // read at most once a second, by anyone
    /// </code>
    ///
    /// <b>One instance per attribute, not per panel.</b> That distinction is the point of the class. A panel-shaped
    /// cache holding a bundle of everything one panel needs cannot be shared: a second panel wanting the same
    /// pawn's mood builds its own bundle, and the same figure is computed twice on the same frame at two different
    /// staleness. An attribute-shaped cache is read by everyone who wants that value, so the work happens once and
    /// every consumer sees the same number.
    ///
    /// <b>The interval belongs to the data, not to the viewer.</b> How often a pawn's mood is worth recomputing is
    /// a fact about mood. If two panels want the same attribute at different rates, the attribute's interval is the
    /// answer for both -- the slower reader simply sees a value that is fresher than it needed. Letting a viewer
    /// name its own interval would mean a cache per viewer, which is the thing being avoided.
    ///
    /// <b>Rebuilt on read, not on a pump.</b> Nothing walks the caches refreshing them on a timer. A value is
    /// rebuilt the first time it is asked for after its interval has elapsed, which means a cache belonging to a
    /// tab nobody has open costs exactly nothing. A pump would do the opposite: pay for every cache in the mod on
    /// every frame regardless of what is on screen, which is the problem this class exists to solve rather than a
    /// way to solve it.
    ///
    /// So the interval is a promise about the <i>most</i> often a value will be rebuilt, not a guarantee about how
    /// often it will be. That is the useful direction: the cost of reading a panel is bounded, and a panel nobody
    /// looks at costs nothing.
    ///
    /// <b>Real seconds, not ticks -- but only while the game is running.</b> An interval is measured in seconds, so
    /// it means what a player would mean by it and keeps that meaning at every game speed; a tick-keyed cache would
    /// refresh six times as often on fast forward. Those seconds are counted only while the simulation is advancing,
    /// though, so a paused or force-paused game rebuilds <i>nothing</i>. See <c>freezeWhilePaused</c> below, which
    /// defaults to on.
    ///
    /// <b>"Nothing" means nothing is <i>re</i>built.</b> A value nobody has asked for yet is still built on its first
    /// read, whenever that happens -- there is no cached answer to show instead, and refusing to build one would mean
    /// a panel opened while paused drew blank. So opening a tab on a paused game does its work once and then holds
    /// still, which is the intended shape: the cost is paid where the player asked for something, never on a timer
    /// while they read it.
    ///
    /// <b>Edits do not wait for the interval.</b> Anything the player changes directly should call
    /// <see cref="Invalidate"/>, so a priority they just clicked is not still showing its old value for the rest
    /// of the second. The interval is for values that drift on their own -- mood, health, what a pawn is doing.
    ///
    /// <b>Asking about something that no longer exists throws.</b> <see cref="Get"/> raises
    /// <see cref="InvalidCacheRequest"/> rather than handing back a blank, because a dead key means the caller is
    /// working from a stale collection and a blank would hide that. <see cref="TryGet"/> is the non-throwing form for
    /// callers that genuinely cannot know in advance. Either way, handle it close to the read: panel drawing goes
    /// through <c>UIGuardedPanel</c>, which retires a panel for the session on its first failure, and one dead pawn
    /// must not cost a whole tab.
    ///
    /// <b>Main thread only, and deliberately unlocked.</b> Every one of these is read while drawing. Adding locks
    /// would cost something on every read to defend against a case that does not arise.
    /// </summary>
    public class UICache<TKey, TValue> : IUICache
    {
        private struct Entry
        {
            public TValue Value;
            public float BuiltAt;

            /// <summary>
            /// False when this entry holds nothing worth serving: it has never built, or its last build failed in a
            /// way that says the subject is gone.
            ///
            /// Needed because a struct's default is indistinguishable from a legitimately built default -- a cached
            /// zero, false or null can all be correct answers, so "is there a value here" cannot be inferred from
            /// the value.
            /// </summary>
            public bool HasValue;
        }

        private readonly Dictionary<TKey, Entry> entries;
        private readonly Func<TKey, TValue> build;
        private readonly Func<TKey, bool> stillValid;
        private readonly bool freezeWhilePaused;

        /// <param name="name">
        /// For diagnostics, and it is also the guard site name a failed rebuild is reported under, so make it the
        /// dotted feature name: "Pawns.Rows", "GrowZones.Status".
        /// </param>
        /// <param name="intervalSeconds">
        /// How long a built value is reused. Zero rebuilds on every read, which is only sensible for a cache whose
        /// point is deduplicating repeated reads within one frame.
        /// </param>
        /// <param name="build">Builds the value for a key. May throw; a failure is contained and reported.</param>
        /// <param name="stillValid">
        /// Optional. Answers whether a key is still worth holding, for <see cref="Prune"/> -- a pawn still on a
        /// map, a zone still on its zone manager. Without it, pruning does nothing and the cache relies on
        /// <see cref="Clear"/> alone, which is fine for a cache keyed by something that cannot go away.
        /// </param>
        /// <param name="freezeWhilePaused">
        /// Measures the interval in running game time rather than wall-clock time, so a paused or force-paused game
        /// rebuilds nothing. <b>On by default, and the default is the point.</b>
        ///
        /// Right for anything describing the simulation -- mood, health, a yield projection, a job report -- because
        /// none of it can change while the game is stopped, and rebuilding then is waste in exactly the situation
        /// where a player is most likely to be sitting still with a panel open. Anything the player can change while
        /// paused is handled by <see cref="Invalidate"/> rather than by the interval, so this costs no
        /// responsiveness. Every cache in the mod so far wants it.
        ///
        /// It defaulted to off when it was new, which was the wrong way round: a cache added later would silently get
        /// wall-clock refreshes, and the symptom -- work being done while nothing can change -- is invisible rather
        /// than a visible bug someone would go and fix. So the waste is what has to be asked for now.
        ///
        /// Pass false only for a reading that genuinely moves in real time regardless of the simulation: a wall-clock
        /// readout, a download, an animation's own state. If a value describes anything inside the colony, this is not
        /// that case.
        ///
        /// Note that either way this is about <i>re</i>building. A value nobody has read yet is built on first
        /// request, paused or not.
        /// </param>
        public UICache(string name, float intervalSeconds, Func<TKey, TValue> build,
            Func<TKey, bool> stillValid = null, bool freezeWhilePaused = true,
            IEqualityComparer<TKey> comparer = null)
        {
            Name = name;
            IntervalSeconds = Mathf.Max(0f, intervalSeconds);
            this.build = build;
            this.stillValid = stillValid;
            this.freezeWhilePaused = freezeWhilePaused;

            entries = comparer == null
                ? new Dictionary<TKey, Entry>()
                : new Dictionary<TKey, Entry>(comparer);

            UICacheController.Register(this);
        }

        public string Name { get; }

        public float IntervalSeconds { get; }

        public int Count => entries.Count;

        /// <summary>
        /// The cached value, rebuilt first if it is older than the interval.
        ///
        /// <b>A failed rebuild is handled two different ways, because there are two different failures.</b>
        ///
        /// <i>The computation went wrong.</i> Something inside the build threw for a reason that says nothing about
        /// whether the subject still exists: a mod's patched getter failed, an arithmetic edge case, a collection
        /// modified while being read. The last good value is kept and served. A readout showing figures from a
        /// second ago is worth far more than a panel that cannot draw.
        ///
        /// <i>The subject is gone.</i> A <see cref="NullReferenceException"/> from a build means something the value
        /// was derived from is no longer there -- a pawn destroyed, a tracker nulled on death, a map unloaded. Here
        /// the stale value is not a slightly old answer, it is a wrong one: serving it means showing a dead
        /// colonist's mood indefinitely. The value is discarded and <see cref="InvalidCacheRequest"/> is thrown,
        /// because the caller is holding a reference it should not be and returning a blank would hide that. Use
        /// <see cref="TryGet"/> where liveness genuinely cannot be known in advance.
        ///
        /// <b>Either way the attempt is stamped.</b> Without that, a build that throws every time would be retried
        /// on every frame, and a thrown exception is expensive enough that the cost would be the bug rather than the
        /// symptom. Stamping bounds it to one attempt per interval, which also leaves room to recover: a subject
        /// that becomes readable again is picked up on the next attempt.
        ///
        /// A poisoned entry is deliberately kept rather than removed, which is what makes repeated asking cheap:
        /// while it is fresh, a second call throws straight from the check above without attempting a build at all.
        /// Pruning is what eventually clears it.
        /// </summary>
        /// <exception cref="InvalidCacheRequest">The subject of <paramref name="key"/> no longer exists.</exception>
        public TValue Get(TKey key)
        {
            float now = Now;

            if (entries.TryGetValue(key, out Entry entry) && now - entry.BuiltAt < IntervalSeconds)
            {
                if (!entry.HasValue)
                    throw new InvalidCacheRequest(Name, Describe(key), null);

                return entry.Value;
            }

            try
            {
                entry.Value = build(key);
                entry.HasValue = true;
            }
            catch (Exception ex) when (IsSubjectGone(ex))
            {
                UIGuard.Report("Cache." + Name, ex,
                    "Whatever this described no longer exists, so the cached value has been discarded rather than "
                    + "shown. The code that asked for it is holding a stale reference.");

                // Discarded, not kept. This is the whole difference from the catch below.
                entry.Value = default;
                entry.HasValue = false;
                entry.BuiltAt = now;
                entries[key] = entry;

                throw new InvalidCacheRequest(Name, Describe(key), ex);
            }
            catch (Exception ex)
            {
                UIGuard.Report("Cache." + Name, ex,
                    entry.HasValue
                        ? "This readout keeps showing its previous values until the next successful refresh."
                        : "This readout has no values to show.");

                entry.BuiltAt = now;
                entries[key] = entry;

                return entry.Value;
            }

            entry.BuiltAt = now;
            entries[key] = entry;

            return entry.Value;
        }

        /// <summary>
        /// Whether an exception says the thing being cached has ceased to exist, rather than that one computation
        /// over it failed.
        ///
        /// <b>Deliberately a short list.</b> Every type here means "something I dereferenced is not there any more",
        /// which for a cache keyed by a game object is as close as .NET gets to telling us the key is dead.
        ///
        /// Types kept off it, and why: <c>IndexOutOfRangeException</c> and <c>ArgumentOutOfRangeException</c> are
        /// usually a bug in the build function rather than a vanished subject; <c>InvalidOperationException</c> is
        /// most often a collection modified mid-read, which is transient; <c>KeyNotFoundException</c> is a lookup
        /// that should have been guarded. Treating any of those as context loss would throw away good cached data
        /// to work around our own mistakes.
        /// </summary>
        private static bool IsSubjectGone(Exception ex)
        {
            return ex is NullReferenceException
                   || ex is ObjectDisposedException
                   || ex is MissingReferenceException;
        }

        private bool IsStillValid(TKey key)
        {
            try
            {
                return stillValid(key);
            }
            catch
            {
                // A key that cannot be asked is not one worth holding.
                return false;
            }
        }

        /// <summary>
        /// The non-throwing form of <see cref="Get"/>: false when the subject no longer exists.
        ///
        /// For the case where a caller genuinely cannot know in advance whether a key is still alive and skipping it
        /// is the correct response. Anywhere the key is supposed to be alive, use <see cref="Get"/> instead and let
        /// it say so -- a silent skip there hides the stale reference rather than fixing it.
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            try
            {
                value = Get(key);
                return true;
            }
            catch (InvalidCacheRequest)
            {
                // Already reported by Get, through the guard, with flood control. Reporting again here would double
                // every line for a caller that is handling the situation correctly.
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Whether a key currently holds a usable value, without building one.
        ///
        /// False for a key that has never built and for one whose last build found its subject gone. Note the
        /// difference from <see cref="TryGet"/>: this never builds, so a false answer may only mean "not asked for
        /// yet" rather than "does not exist".
        /// </summary>
        public bool Has(TKey key) => entries.TryGetValue(key, out Entry entry) && entry.HasValue;

        /// <summary>
        /// A key as text for an exception message, defensively. A destroyed object's own ToString can throw, and an
        /// exception raised while describing an exception would replace a useful report with a useless one.
        /// </summary>
        private static string Describe(TKey key)
        {
            try
            {
                return key?.ToString() ?? "null";
            }
            catch
            {
                return typeof(TKey).Name + " (its ToString threw)";
            }
        }

        /// <summary>
        /// Forgets one key, so the next read rebuilds it.
        ///
        /// Call this from anything that changes what the value describes. This is the mechanism that keeps an
        /// interval from making the UI feel unresponsive to the player's own edits.
        /// </summary>
        public void Invalidate(TKey key)
        {
            entries.Remove(key);
        }

        public void Clear()
        {
            entries.Clear();
        }

        /// <summary>
        /// Drops this subject if it is one of ours, ignoring it otherwise.
        ///
        /// The type test is what lets the controller broadcast a destroyed pawn to every cache in the mod without
        /// knowing which of them are keyed by pawns.
        /// </summary>
        public void Forget(object subject)
        {
            if (subject is TKey key)
                entries.Remove(key);
        }

        public void Prune()
        {
            if (stillValid == null || entries.Count == 0)
                return;

            List<TKey> dead = null;

            foreach (KeyValuePair<TKey, Entry> pair in entries)
            {
                if (IsStillValid(pair.Key))
                    continue;

                if (dead == null)
                    dead = new List<TKey>();

                dead.Add(pair.Key);
            }

            if (dead == null)
                return;

            foreach (TKey key in dead)
                entries.Remove(key);
        }

        /// <summary>
        /// Real time since launch.
        ///
        /// Read through a property with a fallback because Unity throws if this is touched off the main thread.
        /// Nothing here should be, but a cache read from the wrong place should degrade to rebuilding every time
        /// rather than throwing into whatever was drawing.
        /// </summary>
        private float Now
        {
            get
            {
                try
                {
                    return freezeWhilePaused ? UICacheController.UnpausedSeconds : Time.realtimeSinceStartup;
                }
                catch
                {
                    return 0f;
                }
            }
        }
    }
}
