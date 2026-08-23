using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>How a trait takes part in the filter.</summary>
    internal enum TraitFilterState
    {
        /// <summary>Not part of the filter at all.</summary>
        Ignored,

        /// <summary>Every body listed must have it.</summary>
        Required,

        /// <summary>No body listed may have it.</summary>
        Excluded
    }

    /// <summary>Whether a skill's passion takes part in the filter, and how much of one is wanted.</summary>
    internal enum PassionFilterState
    {
        /// <summary>Passion is not asked about.</summary>
        Ignored,

        /// <summary>Interested or burning will do.</summary>
        Any,

        /// <summary>Burning only.</summary>
        Major
    }

    /// <summary>
    /// One skill's conditions: how much passion is wanted, and what levels are acceptable.
    ///
    /// <b>A struct, so an unset skill costs nothing.</b> Only the skills somebody has actually asked about are in
    /// the dictionary; a skill absent from it is unfiltered, which is the same rule the trait list follows.
    /// </summary>
    internal struct SkillFilter
    {
        internal PassionFilterState Passion;

        internal IntRange Level;

        /// <summary>Whether the level range excludes anything at all.</summary>
        internal bool Bounded
        {
            get { return Level.min > SkillRecord.MinLevel || Level.max < SkillRecord.MaxLevel; }
        }

        internal bool Active
        {
            get { return Passion != PassionFilterState.Ignored || Bounded; }
        }

        /// <summary>The unfiltered state, which is what a skill starts at and what clearing one returns it to.</summary>
        internal static SkillFilter Open
        {
            get
            {
                return new SkillFilter
                {
                    Passion = PassionFilterState.Ignored,
                    Level = new IntRange(SkillRecord.MinLevel, SkillRecord.MaxLevel)
                };
            }
        }
    }

    /// <summary>
    /// What the corpses tab is showing, beyond the two toggles and the search box.
    ///
    /// <b>Asked for on 2026-08-23, and the combining rules are Aaron's rather than mine.</b> Xenotypes and factions
    /// are sets where any match will do, because "show me the archliches" and "show me anybody from these three
    /// factions" are questions about which group somebody belongs to. Traits are the opposite: each one named is a
    /// condition all of them must satisfy, because "tough and iron-willed" means both. Sex is one value or none.
    /// Age is a range.
    ///
    /// <b>Skills followed on the same day and follow the traits' rule.</b> Each skill named is a condition every
    /// body must satisfy -- a passion, a level range, or both -- because "eight construction and a burning passion
    /// for medicine" means both. Nothing here treats a skill as a group somebody belongs to.
    ///
    /// <b>Applied before folding and after counting.</b> Before folding, so a group never contains a body the
    /// filter excluded; after counting, so the toolbar's readouts keep saying what the colony actually owes. A
    /// filter changes what is shown, never what is true -- the same rule the buried and animals toggles follow.
    ///
    /// <b>Static and not saved.</b> A filter is a question being asked this minute, not a setting: coming back to
    /// the tab in an hour and finding half the dead hidden by a filter set before lunch would read as a bug.
    /// </summary>
    internal static class CorpseFilter
    {
        /// <summary>The widest age band, and so the one that means "do not filter on age".</summary>
        internal const int MinAge = 0;

        internal const int MaxAge = 120;

        internal static readonly HashSet<XenotypeDef> Xenotypes = new HashSet<XenotypeDef>();

        internal static readonly Dictionary<TraitDef, TraitFilterState> Traits =
            new Dictionary<TraitDef, TraitFilterState>();

        internal static readonly HashSet<Faction> Factions = new HashSet<Faction>();

        /// <summary>
        /// The skills somebody has asked about, and what they asked.
        ///
        /// <b>Added 2026-08-23, and every skill named is a condition all of them must satisfy,</b> the same rule
        /// the trait list follows: "eight construction and a burning passion for medicine" means both. The
        /// alternative reading -- any one of them -- would make adding a second skill widen the list, which is
        /// not what adding a condition to a filter does anywhere else in this window.
        /// </summary>
        internal static readonly Dictionary<SkillDef, SkillFilter> Skills =
            new Dictionary<SkillDef, SkillFilter>();

        /// <summary>Null for either sex, which is the default.</summary>
        internal static Gender? Sex;

        internal static IntRange Age = new IntRange(MinAge, MaxAge);

        /// <summary>How many of the six filters are doing anything. Zero means the tab is unfiltered.</summary>
        internal static int Count
        {
            get
            {
                int active = 0;

                if (Xenotypes.Count > 0)
                    active++;

                if (TraitCount > 0)
                    active++;

                if (Factions.Count > 0)
                    active++;

                if (Sex.HasValue)
                    active++;

                if (Age.min > MinAge || Age.max < MaxAge)
                    active++;

                if (SkillCount > 0)
                    active++;

                return active;
            }
        }

        internal static bool Any
        {
            get { return Count > 0; }
        }

        private static int TraitCount
        {
            get
            {
                int used = 0;

                foreach (KeyValuePair<TraitDef, TraitFilterState> pair in Traits)
                {
                    if (pair.Value != TraitFilterState.Ignored)
                        used++;
                }

                return used;
            }
        }

        /// <summary>How many skills are asking anything. Counted the same way the traits are.</summary>
        internal static int SkillCount
        {
            get
            {
                int used = 0;

                foreach (KeyValuePair<SkillDef, SkillFilter> pair in Skills)
                {
                    if (pair.Value.Active)
                        used++;
                }

                return used;
            }
        }

        /// <summary>This skill's conditions, or the open ones for a skill nobody has asked about.</summary>
        internal static SkillFilter StateOf(SkillDef def)
        {
            SkillFilter held;

            return def != null && Skills.TryGetValue(def, out held) ? held : SkillFilter.Open;
        }

        /// <summary>
        /// Writes a skill's conditions back, dropping the entry entirely once it asks for nothing.
        ///
        /// <b>Dropped rather than kept in its open state,</b> so <see cref="SkillCount"/> and the window's
        /// headings answer from the dictionary's size rather than having to walk it looking for entries that mean
        /// nothing. An open entry left behind is also what would make Clear all look like it had missed one.
        /// </summary>
        internal static void Set(SkillDef def, SkillFilter filter)
        {
            if (def == null)
                return;

            if (filter.Active)
                Skills[def] = filter;
            else
                Skills.Remove(def);

            CorpseRoster.Invalidate();
        }

        /// <summary>Steps a skill's passion requirement through ignored, any, and burning only.</summary>
        internal static void CyclePassion(SkillDef def)
        {
            if (def == null)
                return;

            SkillFilter filter = StateOf(def);

            switch (filter.Passion)
            {
                case PassionFilterState.Ignored:
                    filter.Passion = PassionFilterState.Any;

                    break;

                case PassionFilterState.Any:
                    filter.Passion = PassionFilterState.Major;

                    break;

                default:
                    filter.Passion = PassionFilterState.Ignored;

                    break;
            }

            Set(def, filter);
        }

        internal static TraitFilterState StateOf(TraitDef def)
        {
            TraitFilterState state;

            return def != null && Traits.TryGetValue(def, out state) ? state : TraitFilterState.Ignored;
        }

        /// <summary>Steps a trait through ignored, required and excluded.</summary>
        internal static void Cycle(TraitDef def)
        {
            if (def == null)
                return;

            switch (StateOf(def))
            {
                case TraitFilterState.Ignored:
                    Traits[def] = TraitFilterState.Required;

                    break;

                case TraitFilterState.Required:
                    Traits[def] = TraitFilterState.Excluded;

                    break;

                default:
                    Traits.Remove(def);

                    break;
            }

            CorpseRoster.Invalidate();
        }

        internal static void Toggle(XenotypeDef def)
        {
            if (def == null)
                return;

            if (!Xenotypes.Remove(def))
                Xenotypes.Add(def);

            CorpseRoster.Invalidate();
        }

        internal static void Toggle(Faction faction)
        {
            if (faction == null)
                return;

            if (!Factions.Remove(faction))
                Factions.Add(faction);

            CorpseRoster.Invalidate();
        }

        internal static void Clear()
        {
            Xenotypes.Clear();
            Traits.Clear();
            Factions.Clear();
            Skills.Clear();

            Sex = null;
            Age = new IntRange(MinAge, MaxAge);

            CorpseRoster.Invalidate();
        }

        /// <summary>
        /// Whether a body passes every filter that is set.
        ///
        /// <b>Each filter is skipped when it is empty rather than failing everything.</b> An unset filter is not a
        /// filter that nothing matches; it is a question nobody asked.
        ///
        /// <b>A body that cannot answer a question fails it.</b> A muffalo has no traits and no xenotype, so a
        /// trait requirement or a xenotype list excludes it -- which is what somebody who typed a trait into the
        /// filter meant. The alternative, letting the unanswerable through, would mean filtering for Tough and
        /// still getting sixty dead chickens.
        /// </summary>
        internal static bool Matches(Pawn pawn)
        {
            if (!Any)
                return true;

            return UIGuard.Try("Corpses.Filter", () =>
            {
                if (Sex.HasValue && pawn.gender != Sex.Value)
                    return false;

                if ((Age.min > MinAge || Age.max < MaxAge) && !Aged(pawn))
                    return false;

                if (Factions.Count > 0 && !Factions.Contains(pawn.Faction))
                    return false;

                if (Xenotypes.Count > 0 && !Xeno(pawn))
                    return false;

                if (TraitCount > 0 && !Trait(pawn))
                    return false;

                return SkillCount == 0 || Skill(pawn);
            }, false, null);
        }

        /// <summary>
        /// Every skill asked about satisfied, which is what an AND filter means.
        ///
        /// <b>Levels are read the way the tab reads them,</b> through <c>SkillRecord.Level</c> and skipping a
        /// skill the pawn is incapable of -- the same two rules <see cref="CorpseFacts.Skills"/> follows. A filter
        /// that counted a level the Skills column does not show would look like the column lying.
        ///
        /// <b>A skill somebody cannot do fails any condition set on it.</b> A body with Construction disabled is
        /// not a body with Construction 0: nobody filtering for a builder wants them, and nobody filtering for
        /// "at most 4" wants them either, because the question was about builders.
        /// </summary>
        private static bool Skill(Pawn pawn)
        {
            Pawn_SkillTracker tracker = pawn.skills;

            foreach (KeyValuePair<SkillDef, SkillFilter> pair in Skills)
            {
                if (!pair.Value.Active)
                    continue;

                if (tracker == null)
                    return false;

                SkillRecord record = tracker.GetSkill(pair.Key);

                if (record == null || record.TotallyDisabled)
                    return false;

                if (pair.Value.Bounded
                    && (record.Level < pair.Value.Level.min || record.Level > pair.Value.Level.max))
                    return false;

                if (pair.Value.Passion == PassionFilterState.Any && record.passion == Passion.None)
                    return false;

                if (pair.Value.Passion == PassionFilterState.Major && record.passion != Passion.Major)
                    return false;
            }

            return true;
        }

        private static bool Aged(Pawn pawn)
        {
            if (pawn.ageTracker == null)
                return false;

            int years = pawn.ageTracker.AgeBiologicalYears;

            return years >= Age.min && years <= Age.max;
        }

        private static bool Xeno(Pawn pawn)
        {
            return pawn.genes != null && Xenotypes.Contains(pawn.genes.Xenotype);
        }

        /// <summary>Every required trait present and no excluded one, which is what an AND filter means.</summary>
        private static bool Trait(Pawn pawn)
        {
            TraitSet traits = pawn.story != null ? pawn.story.traits : null;

            foreach (KeyValuePair<TraitDef, TraitFilterState> pair in Traits)
            {
                if (pair.Value == TraitFilterState.Ignored)
                    continue;

                bool has = traits != null && traits.HasTrait(pair.Key);

                if (pair.Value == TraitFilterState.Required && !has)
                    return false;

                if (pair.Value == TraitFilterState.Excluded && has)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The factions worth offering, which is every faction a body on the map could belong to.
        ///
        /// Read off the corpses rather than off the faction manager: a list of every faction in the world would be
        /// thirty entries of which two are represented among the dead, and the other twenty-eight would each
        /// filter the list down to nothing.
        /// </summary>
        internal static void FactionsPresent(List<Faction> into)
        {
            into.Clear();

            UIGuard.Try("Corpses.FilterFactions", () =>
            {
                List<Map> maps = Find.Maps;

                for (int m = 0; maps != null && m < maps.Count; m++)
                {
                    Map map = maps[m];

                    if (map == null || map.listerThings == null)
                        continue;

                    List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);

                    for (int i = 0; corpses != null && i < corpses.Count; i++)
                    {
                        Corpse corpse = corpses[i] as Corpse;

                        if (corpse == null || corpse.Bugged || corpse.InnerPawn == null)
                            continue;

                        Faction faction = corpse.InnerPawn.Faction;

                        if (faction != null && !into.Contains(faction))
                            into.Add(faction);
                    }
                }

                into.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
            }, null);
        }
    }
}
