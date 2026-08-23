using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The Needs body: every need in full, and what the mood is actually made of.
    ///
    /// <b>The mood breakdown is the reason this tab is worth rebuilding.</b> Vanilla has it behind a hover on the
    /// mood bar, which means it is invisible until you already suspect something and impossible to compare
    /// between two colonists. It is the single most useful thing in the tab and it belongs on the page, worst
    /// first, with the net total in the caption so the arithmetic can be checked.
    /// </summary>
    internal static class InspectNeedsBody
    {
        /// <summary>How many thought groups the breakdown lists before it stops.</summary>
        private const int ThoughtsShown = 10;

        /// <summary>Reused between frames so a draw does not allocate.</summary>
        private static readonly List<Thought> Thoughts = new List<Thought>();

        internal static float Draw(Rect view, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.needs == null)
                return 0f;

            Rect left;
            Rect right;

            InspectBodies.Columns(view, out left, out right);

            bool split = InspectBodies.Live(right);

            float leftY = Needs(left, view.y, pawn, palette);

            Rect second = split ? right : left;
            float secondY = split ? view.y : leftY;

            secondY = Mood(second, secondY, pawn, palette);

            return (split ? Mathf.Max(leftY, secondY) : secondY) - view.y;
        }

        /// <summary>
        /// Every need, in RimWorld's own order, each with the sentence that says what to do about it.
        ///
        /// The notes are only written where there is something to say: the mood bar names its own three break
        /// points, and everything else stays a bar and a number.
        /// </summary>
        private static float Needs(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            List<Need> all = pawn.needs.AllNeeds;

            if (all == null || all.Count == 0)
                return y;

            y = InspectPaneParts.Cap(view, y, "Needs", all.Count + " tracked", palette);

            // Mood first and drawn by hand, because RimWorld's own Mood def declares showOnNeedList false: it is
            // the headline of vanilla's needs tab rather than a row in its list, so the loop below will never
            // reach it however the list is ordered.
            Need_Mood mood = pawn.needs.mood;

            if (mood != null)
                y = InspectOverview.DrawNeed(view, y, mood, pawn, palette, NoteFor(mood, pawn));

            for (int i = 0; i < all.Count; i++)
            {
                Need need = all[i];

                if (need == null || !need.ShowOnNeedList)
                    continue;

                y = InspectOverview.DrawNeed(view, y, need, pawn, palette, NoteFor(need, pawn));
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The line under a need, where one is worth writing.
        ///
        /// Only the mood bar gets one, and it says where its own three ticks are. Naming the numbers matters more
        /// here than anywhere else in the pane, because the ticks are per pawn: two colonists with identical bars
        /// can be in completely different amounts of trouble and the marks are the only thing that says so.
        /// </summary>
        private static string NoteFor(Need need, Pawn pawn)
        {
            if (!(need is Need_Mood))
                return null;

            return UIGuard.Try("Inspector.MoodNote", () =>
            {
                MentalBreaker breaker = pawn.mindState != null ? pawn.mindState.mentalBreaker : null;

                if (breaker == null)
                    return null;

                return "Minor break at " + InspectPaneParts.Percent(breaker.BreakThresholdMinor)
                       + ", major at " + InspectPaneParts.Percent(breaker.BreakThresholdMajor)
                       + ", extreme at " + InspectPaneParts.Percent(breaker.BreakThresholdExtreme) + ".";
            }, null, null);
        }

        /// <summary>
        /// What the mood is made of, sorted by size with the worst first, so the top line is the thing to fix.
        ///
        /// <b>Grouped the way RimWorld groups them.</b> <c>GetDistinctMoodThoughtGroups</c> collapses four
        /// separate "ate raw food" memories into one line with one number, which is what makes the list short
        /// enough to read; listing the memories individually would put the same complaint on the page four times.
        /// </summary>
        private static float Mood(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            Need_Mood mood = pawn.needs.mood;

            if (mood == null || mood.thoughts == null)
                return y;

            Thoughts.Clear();

            float net = UIGuard.Try("Inspector.MoodTotal", () =>
            {
                mood.thoughts.GetDistinctMoodThoughtGroups(Thoughts);

                return mood.thoughts.TotalMoodOffset();
            }, 0f, "The inspect pane cannot break down this pawn's mood.");

            y = InspectPaneParts.Cap(view, y, "Mood is made of",
                "net " + (net >= 0f ? "+" : string.Empty) + Mathf.RoundToInt(net), palette);

            if (Thoughts.Count == 0)
                return InspectPaneParts.Note(view, y, "Nothing on their mind.", palette)
                       + InspectPaneParts.BlockGap;

            // Sorted in place: RimWorld filled our own list rather than handing us one of its own, so the order
            // is ours to change and nothing else reads it.
            List<Thought> sorted = Thoughts;

            sorted.SortBy(thought => Offset(mood, thought));

            int shown = Mathf.Min(sorted.Count, ThoughtsShown);

            for (int i = 0; i < shown; i++)
            {
                Thought thought = sorted[i];
                float offset = Offset(mood, thought);

                float before = y;

                y = InspectPaneParts.Entry(view, y,
                    UIGuard.Try("Inspector.ThoughtLabel", () => thought.LabelCap, "?", null),
                    (offset >= 0f ? "+" : string.Empty) + Mathf.RoundToInt(offset),
                    offset > 0f ? palette.Success : offset < 0f ? palette.Danger : palette.TextDisabled,
                    Duration(thought), palette);

                Rect row = new Rect(view.x, before, view.width, y - before);

                if (Mouse.IsOver(row))
                {
                    string tip = UIGuard.Try("Inspector.ThoughtTip", () => thought.Description, null, null);

                    if (!tip.NullOrEmpty())
                        TooltipHandler.TipRegion(row, (TipSignal) tip);
                }
            }

            if (sorted.Count > shown)
                y = InspectPaneParts.Note(view, y, (sorted.Count - shown) + " smaller ones.", palette)
                    + InspectPaneParts.RowGap;

            Thoughts.Clear();

            return y + InspectPaneParts.BlockGap;
        }

        private static float Offset(Need_Mood mood, Thought thought)
        {
            return UIGuard.Try("Inspector.ThoughtOffset", () => mood.thoughts.MoodOffsetOfGroup(thought), 0f,
                null);
        }

        /// <summary>
        /// How long a memory has left, for the ones that expire.
        ///
        /// Situational thoughts have no duration at all -- they last as long as the situation does -- so they get
        /// no line rather than a made up one.
        /// </summary>
        private static string Duration(Thought thought)
        {
            return UIGuard.Try("Inspector.ThoughtDuration", () =>
            {
                Thought_Memory memory = thought as Thought_Memory;

                if (memory == null || memory.def == null || memory.def.durationDays <= 0f)
                    return null;

                int left = memory.DurationTicks - memory.age;

                return left <= 0
                    ? null
                    : "Wears off in " + left.ToStringTicksToPeriod(false, false, true, true) + ".";
            }, null, null);
        }
    }
}
