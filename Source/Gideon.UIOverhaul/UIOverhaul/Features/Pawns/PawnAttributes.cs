using Gideon.UIFramework.Caching;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The per-pawn readings that cost something to produce, cached one attribute at a time so any feature can read
    /// them and the work happens once.
    ///
    /// <b>Only computed values are here, and that is the whole selection rule.</b> A cache earns its place when the
    /// value costs CPU to derive or allocates while deriving it: iterating hediffs, sorting them, asking a JobDriver
    /// for a translated report, formatting a percentage into a string. Reading it back later is then a dictionary
    /// lookup instead of all of that.
    ///
    /// <b>Stored values are deliberately absent.</b> <c>SummaryHealthPercent</c> is already cached by vanilla behind
    /// a dirty flag; <c>CurLevelPercentage</c> is effectively a field read; <c>CurrentAssignment</c> is a list index.
    /// Caching any of those would cost a dictionary lookup and a timestamp compare to avoid work cheaper than the
    /// lookup, and would add a staleness bug surface for nothing. Those are read directly at the call site.
    ///
    /// <b>The real cost of an entry here is not CPU, it is invalidation discipline.</b> Every attribute is something
    /// somebody has to remember to invalidate when the player changes what it describes, and a readout that ignores
    /// its own click is a worse bug than a slow read. That is why this list is short and why adding to it should be
    /// justified by cost rather than by tidiness.
    ///
    /// <b>Why the strings are separate attributes and not one bundle.</b> A bundle cannot be shared: a second panel
    /// wanting a pawn's activity would build the whole bundle again, computing the health summary it did not ask
    /// for. One cache per attribute means the work-priorities pane can read the condition summary that the pawns
    /// tab already paid for.
    /// </summary>
    public static class PawnAttributes
    {
        /// <summary>
        /// How long a reading is reused. One second, which is the rate the pawns tab is specified to refresh at.
        ///
        /// It sits on the data rather than on any panel because it is a fact about the reading: a colonist's
        /// condition is not worth recomputing more often than this no matter who is looking.
        /// </summary>
        public const float IntervalSeconds = 1f;

        /// <summary>
        /// Still worth holding while the pawn exists and has not been destroyed.
        ///
        /// Not <c>Spawned</c>: a colonist in a caravan or a transport pod is off the map and still perfectly
        /// readable, and dropping them would mean rebuilding on every read for anyone away from home.
        /// </summary>
        private static bool Alive(Pawn pawn) => pawn != null && !pawn.Destroyed;

        /// <summary>
        /// The condition summary: iterates the hediff set, picks what matters and orders it by severity.
        ///
        /// The most expensive reading on a row, and the clearest case for caching.
        /// </summary>
        /// <remarks>
        /// Internal rather than public because <see cref="PawnHealthSummary"/> is: it is this mod's own shape, not
        /// part of the framework's API, and widening a type just to widen a field would be the wrong way round.
        ///
        /// It is also a struct, which is the case that made <c>HasValue</c> necessary on the cache entry: a default
        /// PawnHealthSummary looks like a real reading, so "nothing here" cannot be inferred from the value.
        /// </remarks>
        internal static readonly UICache<Pawn, PawnHealthSummary> Condition =
            new UICache<Pawn, PawnHealthSummary>("Pawn.Condition", IntervalSeconds,
                PawnHealthSummary.For, Alive, true);

        /// <summary>
        /// What the pawn is doing, as a sentence.
        ///
        /// Asks the current job for its report, which goes through translation and string formatting and allocates
        /// every time. Read several times a frame while a tab is open, so this is the reading that most rewards
        /// being taken once a second.
        /// </summary>
        public static readonly UICache<Pawn, string> Activity =
            new UICache<Pawn, string>("Pawn.Activity", IntervalSeconds, ReadActivity, Alive, true);

        /// <summary>Health as a percentage, and the tooltip built from it. Two allocations per refresh.</summary>
        public static readonly UICache<Pawn, string> HealthReading =
            new UICache<Pawn, string>("Pawn.HealthReading", IntervalSeconds,
                pawn => HealthFractionOf(pawn).ToStringPercent(), Alive, true);

        public static readonly UICache<Pawn, string> HealthTooltip =
            new UICache<Pawn, string>("Pawn.HealthTooltip", IntervalSeconds,
                pawn => "Overall health: " + HealthFractionOf(pawn).ToStringPercent(), Alive, true);

        /// <summary>
        /// Mood as vanilla words it, and the tooltip.
        ///
        /// The tooltip reads the mental break threshold as well, which is a second lookup through mindState, so it
        /// is worth taking once rather than on every hover test.
        /// </summary>
        public static readonly UICache<Pawn, string> MoodReading =
            new UICache<Pawn, string>("Pawn.MoodReading", IntervalSeconds,
                pawn => pawn.needs?.mood?.MoodString ?? string.Empty, Alive, true);

        public static readonly UICache<Pawn, string> MoodTooltip =
            new UICache<Pawn, string>("Pawn.MoodTooltip", IntervalSeconds, BuildMoodTooltip, Alive, true);

        // ---------------------------------------------------------------------------------------
        // Cheap readings, offered as plain helpers rather than as caches
        //
        // Here so a caller has one place to read a pawn's figures from, without implying that everything in that
        // place is cached. Each of these is a field read or close to it, and cheaper than the dictionary lookup a
        // cache would need.
        // ---------------------------------------------------------------------------------------

        /// <summary>Overall health, 0 to 1. Vanilla caches this behind a dirty flag, so reading it is cheap.</summary>
        public static float HealthFractionOf(Pawn pawn)
        {
            return Mathf.Clamp01(pawn?.health?.summaryHealth?.SummaryHealthPercent ?? 1f);
        }

        /// <summary>Mood, 0 to 1, or a negative number when this pawn has no mood need at all.</summary>
        public static float MoodFractionOf(Pawn pawn)
        {
            Need_Mood mood = pawn?.needs?.mood;

            return mood == null ? -1f : Mathf.Clamp01(mood.CurLevelPercentage);
        }

        public static bool HasMood(Pawn pawn) => pawn?.needs?.mood != null;

        /// <summary>The assignment for the current hour. A list index behind a property.</summary>
        public static TimeAssignmentDef AssignmentOf(Pawn pawn) => pawn?.timetable?.CurrentAssignment;

        // ---------------------------------------------------------------------------------------
        // Invalidation
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Drops every cached reading for one pawn, so the next read takes fresh ones.
        ///
        /// For a change the player just made. Waiting up to a second to see the result of their own click is the one
        /// case where an interval is felt as lag rather than not noticed at all.
        /// </summary>
        public static void Invalidate(Pawn pawn)
        {
            if (pawn == null)
                return;

            Condition.Invalidate(pawn);
            Activity.Invalidate(pawn);
            HealthReading.Invalidate(pawn);
            HealthTooltip.Invalidate(pawn);
            MoodReading.Invalidate(pawn);
            MoodTooltip.Invalidate(pawn);
        }

        private static string BuildMoodTooltip(Pawn pawn)
        {
            Need_Mood mood = pawn?.needs?.mood;

            if (mood == null)
                return string.Empty;

            return "Mood: " + Mathf.Clamp01(mood.CurLevelPercentage).ToStringPercent()
                   + " (" + mood.MoodString + ")"
                   + "\n\nMental break at "
                   + pawn.mindState?.mentalBreaker?.BreakThresholdMinor.ToStringPercent();
        }

        /// <summary>
        /// What the pawn is doing.
        ///
        /// Moved here from the panel so anything can read it. The job report is asked for defensively, because a
        /// modded JobDriver that throws from GetReport would otherwise take the row with it.
        /// </summary>
        private static string ReadActivity(Pawn pawn)
        {
            JobDriver driver = pawn?.jobs?.curDriver;

            if (driver == null)
                return "Idle";

            try
            {
                string report = driver.GetReport();
                return report.NullOrEmpty() ? "Idle" : report.CapitalizeFirst();
            }
            catch
            {
                // A mod's JobDriver, not ours to fix. Saying so beats an empty cell and beats a broken tab.
                return "(unavailable)";
            }
        }
    }
}
