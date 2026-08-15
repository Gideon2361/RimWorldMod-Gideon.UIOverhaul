using System;
using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Calendar
{
    /// <summary>What a forecast mark says about an incident that has not happened yet.</summary>
    internal enum ForecastKind
    {
        /// <summary>A large threat: raids, mech clusters, infestations.</summary>
        MajorThreat,

        /// <summary>A small threat.</summary>
        MinorThreat,

        /// <summary>Disease.</summary>
        Disease,

        /// <summary>A quest being offered.</summary>
        Quest,

        /// <summary>Everything else the storyteller schedules, which is generally benign.</summary>
        Neutral
    }

    /// <summary>One incident the storyteller has already decided the timing of.</summary>
    internal struct ForecastMark
    {
        /// <summary>Game tick it fires on, to the nearest 1000-tick interval.</summary>
        public int FireTick;

        public ForecastKind Kind;

        /// <summary>
        /// The storyteller component that scheduled it, for the explicit readout. Null is tolerated.
        /// </summary>
        public StorytellerComp Comp;
    }

    /// <summary>
    /// When the storyteller will next act, worked out ahead of time.
    ///
    /// <b>This is possible, and the reason is worth stating because it is counter-intuitive.</b> The storyteller
    /// looks like it rolls dice every interval and decides nothing in advance. It does not.
    /// <c>StorytellerComp_OnOffCycle</c> asks <c>IncidentCycleUtility.IncidentCountThisInterval</c> whether this
    /// interval is a firing one, and that method builds a list of every firing interval in the whole cycle from a
    /// seed made of four values that are all knowable now:
    ///
    /// <code>
    /// Gen.HashCombineInt(Find.World.info.persistentRandomValue, target.ConstantRandSeed, compIndex, cycleIndex)
    /// </code>
    ///
    /// So the timing is already settled. The game simply recomputes the answer each interval instead of storing
    /// it, which means anything else can compute it too, including for cycles that have not arrived.
    ///
    /// <b>What is knowable and what is not.</b> The <i>when</i> is settled, and so is the <i>kind</i> -- the
    /// category is the component's own configuration rather than a roll. Which specific incident fires is not:
    /// <c>GenerateIncident</c> makes a weighted pick at the moment it fires, so a major threat three days out
    /// could still turn out to be a raid, an infestation or a mech cluster. Duration is unknowable for the same
    /// reason. That is why a forecast mark carries a kind and nothing more, and why the calendar only fills in
    /// what actually happened once it has happened.
    ///
    /// <b>Forecasts drift, and further ones drift more.</b> The hit list is built with an <c>acceptFraction</c>
    /// derived from days passed, threat points and progress score -- all of which move as the colony grows. A mark
    /// one day out is close to certain; one seven days out was computed against today's wealth and may not survive
    /// the week. Callers should present distance as confidence rather than treating every mark alike.
    ///
    /// <b>Only cycle-based components are predictable.</b> <c>StorytellerComp_RandomMain</c> and the other
    /// mean-time-between comps have no structure to read, and nothing here invents one for them. A quiet forecast
    /// means "nothing is scheduled", never "nothing will happen".
    ///
    /// <b>Vanilla's own hit list is borrowed rather than reimplemented.</b> The algorithm is a seeded random walk
    /// with a spacing constraint and a retry loop, and a copy of it would agree with the game right up until it
    /// did not. Reaching the private method costs a reflection call once; getting the arithmetic subtly wrong
    /// would cost a forecast that is confidently incorrect, which is worse than no forecast at all.
    /// </summary>
    internal static class CalendarForecast
    {
        /// <summary>Ticks in one storyteller interval. Vanilla's <c>QueueIntervalsPassed</c> divides by this.</summary>
        private const int TicksPerInterval = 1000;

        /// <summary>
        /// Vanilla's private hit list and the method that fills it.
        ///
        /// Both are needed together: <c>GenerateHitList</c> writes into the static list rather than returning
        /// anything, so reading the answer means holding a reference to that same field.
        /// </summary>
        private static readonly MethodInfo GenerateHitListMethod =
            AccessTools.Method(typeof(IncidentCycleUtility), "GenerateHitList");

        private static readonly AccessTools.FieldRef<List<int>> HitsField = ResolveHits();

        private static AccessTools.FieldRef<List<int>> ResolveHits()
        {
            try
            {
                return AccessTools.StaticFieldRefAccess<List<int>>(
                    AccessTools.Field(typeof(IncidentCycleUtility), "hits"));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Whether the forecast can run at all. False retires it quietly rather than reporting per frame.</summary>
        internal static bool Available => GenerateHitListMethod != null && HitsField != null;

        private static readonly List<ForecastMark> cached = new List<ForecastMark>();
        private static int cachedForDay = int.MinValue;
        private static int cachedForMapId = -1;

        /// <summary>
        /// Every scheduled incident between now and <paramref name="daysAhead"/> days from now.
        ///
        /// <b>Cached per day, and it has to be.</b> Rebuilding means replaying every cycle since the colony was
        /// settled, because each cycle's hit list is seeded from the last hit of the one before it -- there is no
        /// way to start in the middle. On a long-running colony that is hundreds of seeded random walks, which is
        /// fine once a day and ruinous once a frame.
        /// </summary>
        internal static List<ForecastMark> Upcoming(int daysAhead)
        {
            if (!Available)
                return cached;

            Map map = Find.CurrentMap;

            if (map == null)
                return cached;

            int today = GenLocalDate.DayOfYear(map);

            if (today == cachedForDay && map.uniqueID == cachedForMapId)
                return cached;

            cachedForDay = today;
            cachedForMapId = map.uniqueID;

            UIGuard.Try("Calendar.Forecast", () => Rebuild(map, daysAhead),
                "The calendar shows no upcoming storyteller marks.");

            return cached;
        }

        private static void Rebuild(Map map, int daysAhead)
        {
            cached.Clear();

            Storyteller storyteller = Find.Storyteller;

            if (storyteller?.storytellerComps == null)
                return;

            int nowInterval = Find.TickManager.TicksSinceSettle / TicksPerInterval;
            int horizon = nowInterval + daysAhead * GenDate.TicksPerDay / TicksPerInterval;

            for (int index = 0; index < storyteller.storytellerComps.Count; index++)
            {
                StorytellerComp comp = storyteller.storytellerComps[index];

                if (!(comp?.props is StorytellerCompProperties_OnOffCycle props))
                    continue;

                AppendComp(map, comp, props, index, nowInterval, horizon);
            }

            cached.Sort((a, b) => a.FireTick.CompareTo(b.FireTick));
        }

        /// <summary>
        /// Replays one component's cycles up to the horizon, collecting the firing intervals that are still ahead.
        ///
        /// The loop mirrors <c>IncidentCountThisInterval</c> exactly, including the part that is easy to miss: the
        /// last hit of each cycle is carried into the next as <c>fixedHit</c>, so the spacing rule holds across a
        /// cycle boundary. Starting anywhere but cycle zero would break that chain and produce a plausible,
        /// wrong answer.
        /// </summary>
        private static void AppendComp(Map map, StorytellerComp comp,
            StorytellerCompProperties_OnOffCycle props, int compIndex, int nowInterval, int horizon)
        {
            float onDays = props.onDays;
            float offDays = props.offDays;

            if (onDays <= 0f)
                return;

            int minInterval = DaysToIntervals(props.minDaysPassed);
            int onIntervals = DaysToIntervals(onDays);
            int offIntervals = DaysToIntervals(offDays);
            int cycleLength = onIntervals + offIntervals;

            if (cycleLength <= 0)
                return;

            // The same acceptFraction the component would compute right now. It is a snapshot: applying today's
            // wealth to a cycle a week away is exactly the drift documented on this class.
            float accept = AcceptFraction(props, map);

            int lastCycle = Math.Max(0, (horizon - minInterval) / cycleLength);
            int fixedHit = -9999999;

            List<int> hits = HitsField();
            ForecastKind kind = KindOf(props.IncidentCategory);

            for (int cycle = 0; cycle <= lastCycle; cycle++)
            {
                int seed = Gen.HashCombineInt(Find.World.info.persistentRandomValue, map.ConstantRandSeed,
                    compIndex, cycle);

                if (hits.Count > 0)
                    fixedHit = hits[hits.Count - 1];

                hits.Clear();

                GenerateHitListMethod.Invoke(null, new object[]
                {
                    seed, cycle * cycleLength, onIntervals, props.numIncidentsRange.min,
                    props.numIncidentsRange.max, DaysToIntervals(props.minSpacingDays), accept, fixedHit
                });

                for (int i = 0; i < hits.Count; i++)
                {
                    // Hits are counted from the first interval after minDaysPassed, which is what that offset
                    // puts back. Getting this wrong shifts the whole forecast by the storyteller's warmup.
                    int interval = hits[i] + minInterval;

                    if (interval <= nowInterval || interval > horizon)
                        continue;

                    cached.Add(new ForecastMark
                    {
                        FireTick = Find.TickManager.TicksGame
                                   + (interval - nowInterval) * TicksPerInterval,
                        Kind = kind,
                        Comp = comp
                    });
                }
            }

            // Left empty rather than holding this component's hits, since vanilla clears it before each use and
            // would otherwise read ours as the previous cycle's.
            hits.Clear();
        }

        /// <summary>
        /// The component's current acceptance multiplier, from the three curves it may carry.
        ///
        /// Copied in substance from <c>MakeIntervalIncidents</c>, which is the one piece of that method worth
        /// reproducing: it is three multiplications against public curves, with no randomness and nothing to get
        /// subtly wrong. The tree-connector branch is skipped deliberately -- it swaps the cycle lengths for an
        /// ideoligion meme, and a forecast that is a cycle out for one ideology is a smaller error than the
        /// reflection needed to read it would be worth.
        /// </summary>
        private static float AcceptFraction(StorytellerCompProperties_OnOffCycle props, Map map)
        {
            float accept = 1f;

            if (props.acceptFractionByDaysPassedCurve != null)
                accept *= props.acceptFractionByDaysPassedCurve.Evaluate(GenDate.DaysPassedSinceSettleFloat);

            if (props.acceptPercentFactorPerThreatPointsCurve != null)
                accept *= props.acceptPercentFactorPerThreatPointsCurve.Evaluate(
                    StorytellerUtility.DefaultThreatPointsNow(map));

            if (props.acceptPercentFactorPerProgressScoreCurve != null)
                accept *= props.acceptPercentFactorPerProgressScoreCurve.Evaluate(
                    StorytellerUtility.GetProgressScore(map));

            return accept;
        }

        /// <summary>Vanilla's own conversion: one interval is a thousand ticks.</summary>
        private static int DaysToIntervals(float days)
        {
            return (int) (days * GenDate.TicksPerDay / TicksPerInterval);
        }

        /// <summary>
        /// What a category means for the player, which is all a forecast is allowed to say.
        ///
        /// Deliberately coarse. The category is the only thing settled ahead of time, so mapping it to anything
        /// finer than this would be inventing detail the game has not decided yet.
        /// </summary>
        private static ForecastKind KindOf(IncidentCategoryDef category)
        {
            if (category == null)
                return ForecastKind.Neutral;

            if (category == IncidentCategoryDefOf.ThreatBig)
                return ForecastKind.MajorThreat;

            if (category == IncidentCategoryDefOf.ThreatSmall
                || category == IncidentCategoryDefOf.DeepDrillInfestation)
                return ForecastKind.MinorThreat;

            if (category == IncidentCategoryDefOf.DiseaseHuman)
                return ForecastKind.Disease;

            if (category == IncidentCategoryDefOf.GiveQuest)
                return ForecastKind.Quest;

            return ForecastKind.Neutral;
        }

        /// <summary>Drops the cache. For a load, where the colony and its seeds change underneath us.</summary>
        internal static void Clear()
        {
            cached.Clear();
            cachedForDay = int.MinValue;
            cachedForMapId = -1;
        }
    }
}
