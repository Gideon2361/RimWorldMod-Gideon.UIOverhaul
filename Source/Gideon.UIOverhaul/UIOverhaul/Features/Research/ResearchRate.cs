using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// How fast the colony researches, and therefore how long a project will take.
    ///
    /// <b>Days, not points, and that is the whole reason this exists.</b> "About two days" answers the question
    /// somebody actually has -- before or after the raid -- and "2,200 points" never has. Vanilla shows the points
    /// and nothing else, because it has nowhere to put a rate.
    ///
    /// <b>It is an estimate and it is wrong the moment anything changes.</b> A researcher takes a different job, a
    /// bench loses power, somebody is downed: all of those move the number. That is accepted rather than worked
    /// around -- an estimate that is roughly right now beats a number that is exactly right about nothing.
    ///
    /// <b>What it counts, said plainly.</b> Every colonist who is allowed to research, at their own research
    /// speed, for the share of the day their timetable gives to work, on the best bench the colony has. It is
    /// therefore optimistic: it does not know that a colonist with Research at priority four and a stack of
    /// hauling will not be at the bench. The alternative was counting only the pawns researching this instant,
    /// which reads as a rate that flickers between "one day" and "never".
    /// </summary>
    internal static class ResearchRate
    {
        /// <summary>Ticks in a day, which is the unit an estimate is turned into.</summary>
        private const float TicksPerDay = 60000f;

        /// <summary>How long an answer is trusted, in frames. The walk behind it is not free.</summary>
        private const int Frames = 60;

        private static float perTick;
        private static int stamped = -1;

        /// <summary>
        /// Raw research points a tick, across the whole colony.
        ///
        /// Raw rather than apparent: <c>ResearchPerformed</c> divides what a researcher produces by the project's
        /// cost factor, and <c>Cost</c> is the raw figure, so both sides of the division below are in the same
        /// unit. Mixing them is how an estimate comes out half or double for every project above the colony's
        /// tech level.
        /// </summary>
        internal static float PointsPerTick
        {
            get
            {
                if (Time.frameCount - stamped <= Frames)
                    return perTick;

                stamped = Time.frameCount;
                perTick = UIGuard.Try("Research.Rate", Measure, 0f, null);

                return perTick;
            }
        }

        /// <summary>Points a day, which is the figure worth showing on its own.</summary>
        internal static float PointsPerDay
        {
            get { return PointsPerTick * TicksPerDay; }
        }

        /// <summary>Drops the cached rate.</summary>
        internal static void Invalidate()
        {
            stamped = -1;
        }

        /// <summary>
        /// Days to finish a project at the current rate, or a negative number when it will never finish.
        ///
        /// Negative rather than zero or infinity: zero reads as "done" and infinity has to be special-cased at
        /// every call site anyway, so the callers test the sign once and print a dash.
        /// </summary>
        internal static float DaysFor(ResearchProjectDef project)
        {
            if (project == null)
                return -1f;

            return UIGuard.Try("Research.DaysFor", () =>
            {
                float remaining = project.Cost - project.ProgressReal;

                if (remaining <= 0f)
                    return 0f;

                // Knowledge is not bought with researcher time at all. There is no rate to divide by, so an
                // Anomaly project has no day figure and says so by refusing to give one.
                if (project.knowledgeCategory != null)
                    return -1f;

                float rate = PointsPerTick * project.CostFactor(Faction.OfPlayer.def.techLevel);

                if (rate <= 0f)
                    return -1f;

                return remaining / rate / TicksPerDay;
            }, -1f, null);
        }

        /// <summary>
        /// A day figure as text: one decimal under ten days, whole days above, a dash when there is no answer.
        ///
        /// One decimal matters at the bottom of the range, where the difference between half a day and a day and
        /// a half is a decision. At thirty days it is noise.
        /// </summary>
        internal static string Days(float days)
        {
            if (days < 0f)
                return "-";

            if (days <= 0f)
                return "done";

            if (days < 10f)
                return days.ToString("0.0") + "d";

            if (days < 400f)
                return Mathf.RoundToInt(days) + "d";

            return "years";
        }

        private static float Measure()
        {
            if (Find.Maps == null || Find.Storyteller == null)
                return 0f;

            float bench = BestBenchFactor();
            float total = 0f;

            List<Map> maps = Find.Maps;

            for (int i = 0; i < maps.Count; i++)
            {
                List<Pawn> pawns = maps[i].mapPawns?.FreeColonistsSpawned;

                if (pawns == null)
                    continue;

                for (int p = 0; p < pawns.Count; p++)
                    total += Contribution(pawns[p], bench);
            }

            return total * ResearchManager.ResearchPointsPerWorkTick
                   * Find.Storyteller.difficulty.researchSpeedFactor;
        }

        /// <summary>
        /// What one colonist adds, in speed units before the points-per-tick constant.
        ///
        /// Zero for anybody who cannot or will not research: the work type disabled by a backstory, the work
        /// switched off in the work tab, or a pawn who is downed. Those three are the ones a player has an opinion
        /// about, and counting them would make the rate a fiction rather than an estimate.
        /// </summary>
        private static float Contribution(Pawn pawn, float bench)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.workSettings == null)
                return 0f;

            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Research))
                return 0f;

            if (!pawn.workSettings.WorkIsActive(WorkTypeDefOf.Research))
                return 0f;

            return pawn.GetStatValue(StatDefOf.ResearchSpeed) * bench * WorkingFraction(pawn);
        }

        /// <summary>
        /// The share of the day this colonist is available for work.
        ///
        /// Read off their own timetable, since that is the one thing about a schedule the player has actually
        /// stated. Anything counts half, because an Anything hour is genuinely split between work and whatever
        /// need is lowest. No timetable at all counts as half a day, which is roughly what the default schedule
        /// comes to.
        /// </summary>
        private static float WorkingFraction(Pawn pawn)
        {
            if (pawn.timetable == null || pawn.timetable.times == null || pawn.timetable.times.Count == 0)
                return 0.5f;

            float hours = 0f;

            for (int i = 0; i < pawn.timetable.times.Count; i++)
            {
                TimeAssignmentDef assignment = pawn.timetable.times[i];

                if (assignment == TimeAssignmentDefOf.Work)
                    hours += 1f;
                else if (assignment == TimeAssignmentDefOf.Anything)
                    hours += 0.5f;
            }

            return Mathf.Clamp01(hours / pawn.timetable.times.Count);
        }

        /// <summary>
        /// The best research speed factor among the colony's benches, or one when it has none.
        ///
        /// One rather than zero for a colony with no bench: the rate is then a hypothetical, and every project on
        /// the tab is already saying it needs a bench. Zeroing it would replace eight useful estimates with eight
        /// dashes to make a point that is already made.
        /// </summary>
        private static float BestBenchFactor()
        {
            float best = 0f;

            List<Map> maps = Find.Maps;

            if (maps == null)
                return 1f;

            for (int i = 0; i < maps.Count; i++)
            {
                List<Building> buildings = maps[i].listerBuildings?.allBuildingsColonist;

                if (buildings == null)
                    continue;

                for (int b = 0; b < buildings.Count; b++)
                {
                    if (!(buildings[b] is Building_ResearchBench))
                        continue;

                    best = Mathf.Max(best, buildings[b].GetStatValue(StatDefOf.ResearchSpeedFactor));
                }
            }

            return best <= 0f ? 1f : best;
        }
    }
}
