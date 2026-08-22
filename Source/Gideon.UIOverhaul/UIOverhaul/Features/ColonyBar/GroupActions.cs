using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.ColonyBar
{
    /// <summary>
    /// Everything the gear on a group header can do, applied to every pawn in that group at once.
    ///
    /// <b>Every row reads back before it offers to write.</b> A row says what the group currently shares, or
    /// <c>mixed</c> when they disagree. Showing the first pawn's value instead would quietly misreport everybody
    /// else, and a group control that misreports is worse than no group control: the whole reason to have one is
    /// to stop opening eight pawns to find out.
    ///
    /// <b>Writes go through the same properties the per-pawn controls use,</b> so a group assignment and eight
    /// individual ones are the same operation. Nothing here reimplements what a policy means.
    ///
    /// <b>Area is offered per map.</b> An area belongs to one map and a group can straddle two, so a group whose
    /// members are split gets one entry per map rather than one entry that silently only reaches some of them.
    ///
    /// <b>Six of these rows are internal because the animals tab composes its own menu from them.</b> A species
    /// group is a group of pawns, so allowed area, medical care, select all and the shared value reader are the
    /// same operations there. What that tab does not take is the rows that would be inert on an animal: apparel,
    /// drugs, reading and food are all humanlike only, and a menu entry that silently does nothing is worse than
    /// a missing one. See <see cref="Animals.AnimalGroupActions"/> for the rows that are animal specific.
    /// </summary>
    internal static class GroupActions
    {
        /// <summary>Opens the group menu. <paramref name="changed"/> lets the bar drop any cached readings.</summary>
        internal static void Open(PawnGroup group, List<Pawn> members, Action changed)
        {
            UIGuard.Try("Bar.GroupMenu", () =>
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                if (members != null && members.Count > 0)
                {
                    Areas(members, changed, options);

                    options.Add(Sub("Whole day", DayLabel(members), () => Day(members, changed)));
                    options.Add(Sub("Apparel", Label(members, p => p.outfits?.CurrentApparelPolicy),
                        () => Policies(members, changed, PolicyKind.Apparel)));
                    options.Add(Sub("Food", Label(members, p => p.foodRestriction?.CurrentFoodPolicy),
                        () => Policies(members, changed, PolicyKind.Food)));
                    options.Add(Sub("Drugs", Label(members, p => p.drugs?.CurrentPolicy),
                        () => Policies(members, changed, PolicyKind.Drug)));
                    options.Add(Sub("Reading", Label(members, p => p.reading?.CurrentPolicy),
                        () => Policies(members, changed, PolicyKind.Reading)));
                    options.Add(Sub("Medical care", CareLabel(members), () => Care(members, changed)));

                    options.Add(new FloatMenuOption("Draft all", UIGuard.Wrap("Bar.DraftAll",
                        () => Draft(members, true, changed))));
                    options.Add(new FloatMenuOption("Undraft all", UIGuard.Wrap("Bar.UndraftAll",
                        () => Draft(members, false, changed))));
                    options.Add(new FloatMenuOption("Select all", UIGuard.Wrap("Bar.SelectAll",
                        () => Select(members))));
                }

                // Only a real group can be renamed or disbanded. Unassigned is computed, so it has no object to
                // act on, and offering the rows greyed out would be four dead entries on the menu people use most.
                if (group != null)
                {
                    options.Add(new FloatMenuOption("Rename...", UIGuard.Wrap("Bar.Rename",
                        () => Find.WindowStack.Add(Dialog_NameGroup.For(group, changed)))));

                    options.Add(Sub("Recolour", string.Empty, () => Recolour(group, changed)));

                    options.Add(new FloatMenuOption("Move left", UIGuard.Wrap("Bar.MoveLeft",
                        () => Shift(group, -1, changed))));
                    options.Add(new FloatMenuOption("Move right", UIGuard.Wrap("Bar.MoveRight",
                        () => Shift(group, 1, changed))));

                    options.Add(new FloatMenuOption("Disband group", UIGuard.Wrap("Bar.Disband", () =>
                    {
                        // The pawns are deliberately not touched, so they fall back into Unassigned rather than
                        // vanishing from the bar with their group.
                        GameComponent_PawnGroups.Current?.Remove(group);

                        changed?.Invoke();
                    })));
                }

                if (options.Count > 0)
                    Find.WindowStack.Add(new FloatMenu(options));
            }, "The group menu could not be built, so nothing was changed.");
        }

        /// <summary>A row that reads back a value and opens another menu when pressed.</summary>
        internal static FloatMenuOption Sub(string caption, string value, Action open)
        {
            string label = value.NullOrEmpty() ? caption + "..." : caption + ": " + value;

            return new FloatMenuOption(label, UIGuard.Wrap("Bar.GroupSub", open));
        }

        // ------------------------------------------------------------------ area

        internal static void Areas(List<Pawn> members, Action changed, List<FloatMenuOption> options)
        {
            List<Map> maps = new List<Map>();

            foreach (Pawn pawn in members)
            {
                Map map = pawn?.MapHeld;

                if (map != null && !maps.Contains(map))
                    maps.Add(map);
            }

            foreach (Map map in maps)
            {
                Map captured = map;
                List<Pawn> here = On(members, captured);

                string caption = maps.Count > 1
                    ? "Allowed area on " + MapName(captured)
                    : "Allowed area";

                // Shared rather than Label: an area is not a Policy, so it reports its own name through the
                // string reader instead of the policy one.
                options.Add(Sub(caption,
                    Shared(here, p => p.playerSettings?.AreaRestrictionInPawnCurrentMap?.Label ?? "Unrestricted"),
                    () => AreaUtility.MakeAllowedAreaListFloatMenu(area =>
                    {
                        UIGuard.Try("Bar.SetArea", () =>
                        {
                            foreach (Pawn pawn in here)
                            {
                                // Asked per pawn rather than once for the group: a mech without an overseer or a
                                // penned animal cannot take an area, and vanilla's own column makes the same test.
                                if (Pawns.PawnAreas.Assignable(pawn))
                                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                            }

                            changed?.Invoke();
                        }, "The allowed area was not changed.");
                    }, true, true, captured)));
            }
        }

        private static List<Pawn> On(List<Pawn> members, Map map)
        {
            List<Pawn> here = new List<Pawn>();

            foreach (Pawn pawn in members)
            {
                if (pawn?.MapHeld == map)
                    here.Add(pawn);
            }

            return here;
        }

        private static string MapName(Map map)
        {
            return UIGuard.Try("Bar.MapName", () => map?.Parent?.LabelCap.ToString() ?? "this map", "this map", null);
        }

        // ------------------------------------------------------------------ schedule

        /// <summary>
        /// Sets every hour of the day to one assignment, for everybody in the group.
        ///
        /// <b>A whole day rather than an hour,</b> because that is the group-sized version of the question: an hour
        /// at a time is what the per-pawn strip on the pawns tab is for. "Everyone sleeps now" and "everyone works
        /// now" are the things a group is actually for.
        /// </summary>
        private static void Day(List<Pawn> members, Action changed)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (TimeAssignmentDef def in DefDatabase<TimeAssignmentDef>.AllDefsListForReading)
            {
                TimeAssignmentDef captured = def;

                options.Add(new FloatMenuOption(captured.LabelCap, UIGuard.Wrap("Bar.SetDay", () =>
                {
                    foreach (Pawn pawn in members)
                    {
                        if (pawn?.timetable == null)
                            continue;

                        for (int hour = 0; hour < 24; hour++)
                            pawn.timetable.SetAssignment(hour, captured);
                    }

                    changed?.Invoke();
                })));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string DayLabel(List<Pawn> members)
        {
            return Shared(members, p =>
            {
                if (p?.timetable == null)
                    return null;

                TimeAssignmentDef first = p.timetable.GetAssignment(0);

                // Only a day that is one assignment all the way through has a single answer to report; anything
                // else is a schedule rather than a setting, and says so.
                for (int hour = 1; hour < 24; hour++)
                {
                    if (p.timetable.GetAssignment(hour) != first)
                        return "varied";
                }

                return first?.LabelCap.ToString();
            });
        }

        // ------------------------------------------------------------------ policies

        private enum PolicyKind
        {
            Apparel,
            Food,
            Drug,
            Reading
        }

        private static void Policies(List<Pawn> members, Action changed, PolicyKind kind)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (Policy policy in All(kind))
            {
                Policy captured = policy;

                options.Add(new FloatMenuOption(captured.label.NullOrEmpty() ? "None" : captured.label,
                    UIGuard.Wrap("Bar.SetPolicy", () =>
                    {
                        foreach (Pawn pawn in members)
                            Apply(pawn, kind, captured);

                        changed?.Invoke();
                    })));
            }

            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<Policy> All(PolicyKind kind)
        {
            List<Policy> all = new List<Policy>();

            UIGuard.Try("Bar.PolicyList", () =>
            {
                switch (kind)
                {
                    case PolicyKind.Apparel:
                        Add(all, Current.Game?.outfitDatabase?.AllOutfits);
                        break;

                    case PolicyKind.Food:
                        Add(all, Current.Game?.foodRestrictionDatabase?.AllFoodRestrictions);
                        break;

                    case PolicyKind.Drug:
                        Add(all, Current.Game?.drugPolicyDatabase?.AllPolicies);
                        break;

                    default:
                        Add(all, Current.Game?.readingPolicyDatabase?.AllReadingPolicies);
                        break;
                }
            }, null);

            return all;
        }

        private static void Add<T>(List<Policy> into, List<T> from) where T : Policy
        {
            if (from == null)
                return;

            foreach (T policy in from)
            {
                if (policy != null)
                    into.Add(policy);
            }
        }

        private static void Apply(Pawn pawn, PolicyKind kind, Policy policy)
        {
            switch (kind)
            {
                case PolicyKind.Apparel:
                    if (pawn?.outfits != null && policy is ApparelPolicy apparel)
                        pawn.outfits.CurrentApparelPolicy = apparel;

                    return;

                case PolicyKind.Food:
                    if (pawn?.foodRestriction != null && policy is FoodPolicy food)
                        pawn.foodRestriction.CurrentFoodPolicy = food;

                    return;

                case PolicyKind.Drug:
                    if (pawn?.drugs != null && policy is DrugPolicy drug)
                        pawn.drugs.CurrentPolicy = drug;

                    return;

                default:
                    if (pawn?.reading != null && policy is ReadingPolicy reading)
                        pawn.reading.CurrentPolicy = reading;

                    return;
            }
        }

        // ------------------------------------------------------------------ medical care

        internal static void Care(List<Pawn> members, Action changed)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (MedicalCareCategory care in (MedicalCareCategory[]) Enum.GetValues(typeof(MedicalCareCategory)))
            {
                MedicalCareCategory captured = care;

                options.Add(new FloatMenuOption(captured.GetLabel(), UIGuard.Wrap("Bar.SetCare", () =>
                {
                    foreach (Pawn pawn in members)
                    {
                        if (pawn?.playerSettings != null)
                            pawn.playerSettings.medCare = captured;
                    }

                    changed?.Invoke();
                })));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        internal static string CareLabel(List<Pawn> members)
        {
            return Shared(members, p => p?.playerSettings == null ? null : p.playerSettings.medCare.GetLabel());
        }

        // ------------------------------------------------------------------ draft and select

        private static void Draft(List<Pawn> members, bool drafted, Action changed)
        {
            foreach (Pawn pawn in members)
            {
                // Guarded per pawn: a downed or non-drafting pawn throws rather than refusing, and one of those
                // must not stop the rest of the group being ordered.
                UIGuard.Try("Bar.DraftOne", () =>
                {
                    if (pawn?.drafter != null && pawn.drafter.Drafted != drafted)
                        pawn.drafter.Drafted = drafted;
                }, null);
            }

            changed?.Invoke();
        }

        internal static void Select(List<Pawn> members)
        {
            Find.Selector?.ClearSelection();

            foreach (Pawn pawn in members)
            {
                if (pawn != null && pawn.Spawned)
                    Find.Selector?.Select(pawn);
            }
        }

        // ------------------------------------------------------------------ group itself

        private static void Recolour(PawnGroup group, Action changed)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            for (int i = 0; i < GameComponent_PawnGroups.Palette.Length; i++)
            {
                UnityEngine.Color captured = GameComponent_PawnGroups.Palette[i];

                options.Add(new FloatMenuOption("Colour " + (i + 1), UIGuard.Wrap("Bar.Recolour", () =>
                {
                    group.Color = captured;

                    changed?.Invoke();
                })));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void Shift(PawnGroup group, int delta, Action changed)
        {
            GameComponent_PawnGroups.Current?.Shift(group, delta);

            changed?.Invoke();
        }

        // ------------------------------------------------------------------ shared readings

        /// <summary>What a policy row says: the shared name, "mixed", or nothing when none of them have one.</summary>
        private static string Label(List<Pawn> members, Func<Pawn, Policy> read)
        {
            return Shared(members, p =>
            {
                Policy policy = read(p);

                return policy == null ? null : policy.label.NullOrEmpty() ? "None" : policy.label;
            });
        }

        /// <summary>
        /// One reading for a whole group.
        ///
        /// Pawns with nothing to report are skipped rather than counted as a disagreement, so a group holding one
        /// animal does not make every policy row read "mixed" forever.
        /// </summary>
        internal static string Shared(List<Pawn> members, Func<Pawn, string> read)
        {
            return UIGuard.Try("Bar.SharedValue", () =>
            {
                string found = null;
                bool any = false;

                foreach (Pawn pawn in members)
                {
                    string value = read(pawn);

                    if (value == null)
                        continue;

                    if (!any)
                    {
                        found = value;
                        any = true;

                        continue;
                    }

                    if (found != value)
                        return "mixed";
                }

                return found;
            }, null, null);
        }
    }

}
