using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>
    /// One row of the tab: a single body, or several that were not worth listing separately.
    ///
    /// Every field is something a column shows, read once per rebuild. A table of forty rows redrawn sixty times
    /// a second cannot afford to ask a corpse for its rot rate two and a half thousand times.
    /// </summary>
    internal sealed class CorpseEntry
    {
        /// <summary>The body, or the first of a folded group.</summary>
        internal Corpse Corpse;

        internal Pawn Pawn;

        internal Map Map;

        internal CorpseKind Kind;

        /// <summary>Every body this row stands for. One entry for an ordinary row.</summary>
        internal readonly List<Corpse> Members = new List<Corpse>();

        /// <summary>What makes these bodies interchangeable, or null when this one is its own row.</summary>
        internal string GroupKey;

        internal string Name;

        internal string Subline;

        // Condition -------------------------------------------------------------------------------

        internal RotStage Stage;

        internal float Progress;

        internal bool Frozen;

        internal string RotNote;

        internal float DaysRotted;

        /// <summary>"58 - 71%" when a folded group's bodies disagree, otherwise null.</summary>
        internal string Spread;

        // What was lost ---------------------------------------------------------------------------

        internal readonly List<CorpseSkill> Skills = new List<CorpseSkill>();

        internal readonly List<string> Traits = new List<string>();

        /// <summary>Traits across a folded group, which are listed as a count rather than named.</summary>
        internal int TraitTotal;

        // Gear ------------------------------------------------------------------------------------

        internal int GearCount;

        internal int GearValue;

        internal bool Strippable;

        internal bool StripQueued;

        // Yield -----------------------------------------------------------------------------------

        internal int Meat;

        internal int Leather;

        internal string LeatherLabel;

        // Where -----------------------------------------------------------------------------------

        internal Building_Grave Grave;

        internal string Where;

        internal string WhereNote;

        /// <summary>Negative once this body is costing the colony mood, <see cref="int.MaxValue"/> when it never will.</summary>
        internal int UnburiedIn;

        /// <summary>Where this row sorts inside its section. Higher is more urgent. See <see cref="CorpseRoster"/>.</summary>
        internal float Urgency;

        /// <summary>
        /// What the row actually sorts on, which for a group's members is their head's urgency.
        ///
        /// Members have to sit under the row that summarises them however urgent each of them is on its own, or
        /// an opened group would be scattered down the section with nothing tying it together.
        /// </summary>
        internal float SortUrgency;

        /// <summary>True on the one row that stands for a whole group.</summary>
        internal bool GroupHead;

        /// <summary>True on a row that is a member of an opened group, and so is drawn under its head.</summary>
        internal bool InGroup;

        // Whether each action can be taken, and what to say on hover when it cannot ---------------
        //
        // Answered at rebuild rather than in the button, because each of these walks something: every grave on
        // the map, or every colonist building on it. Thirty rows asking that sixty times a second is tens of
        // thousands of walks a second for an answer that changes about once a minute.

        internal bool CanBury;

        internal string BuryReason;

        internal bool CanCremate;

        internal string CremateReason;

        internal bool CanButcher;

        internal string ButcherReason;

        internal bool Buried
        {
            get { return Grave != null; }
        }

        internal bool Folded
        {
            get { return GroupHead; }
        }

        internal void Reset()
        {
            Corpse = null;
            Pawn = null;
            Map = null;
            Kind = CorpseKind.Guests;
            Members.Clear();
            GroupKey = null;
            Name = null;
            Subline = null;
            Stage = RotStage.Fresh;
            Progress = 0f;
            Frozen = false;
            RotNote = null;
            DaysRotted = 0f;
            Spread = null;
            Skills.Clear();
            Traits.Clear();
            TraitTotal = 0;
            GearCount = 0;
            GearValue = 0;
            Strippable = false;
            StripQueued = false;
            Meat = 0;
            Leather = 0;
            LeatherLabel = null;
            Grave = null;
            Where = null;
            WhereNote = null;
            UnburiedIn = int.MaxValue;
            Urgency = 0f;
            SortUrgency = 0f;
            GroupHead = false;
            InGroup = false;
            CanBury = false;
            BuryReason = null;
            CanCremate = false;
            CremateReason = null;
            CanButcher = false;
            ButcherReason = null;
        }
    }

    /// <summary>One section of the tab, and the rows in it across every loaded map.</summary>
    internal sealed class CorpseSection
    {
        internal CorpseKind Kind;

        internal string Label;

        internal readonly List<CorpseEntry> Entries = new List<CorpseEntry>();
    }

    /// <summary>
    /// Every body on every loaded map, sorted into what you may do with it.
    ///
    /// <b>RimWorld tells you somebody died and then loses them.</b> The body is on the map somewhere, on a clock
    /// nothing shows, still wearing the gear you paid for, and still carrying the fourteen levels of Medicine you
    /// spent four years growing. Nothing in the game lists the dead. This does.
    ///
    /// <b>Buried bodies are hidden until asked for.</b> A grave is a decision already made, and a list that opens
    /// with forty sarcophagi has buried the one body on the kitchen floor. Same reasoning for the animals toggle:
    /// a herd lost to toxic fallout would otherwise put sixty muffalo over the two colonists underneath them.
    ///
    /// <b>Interchangeable bodies fold into one row.</b> Three raiders from the same faction with nothing above
    /// Shooting 8 between them and no gear worth taking are one row with one Strip all button. Anything with a
    /// skill worth reading, gear worth taking, or a name of its own stays its own row -- and a folded row opens
    /// in place, so nothing is ever unreachable.
    ///
    /// <b>Rebuilt on the game's clock, not the frame's.</b> Reading a body walks its apparel, its inventory, its
    /// skills and its rot comp, none of which can change while the game is paused. Once a game second, plus an
    /// <see cref="Invalidate"/> after anything the player does through the tab.
    /// </summary>
    internal static class CorpseRoster
    {
        /// <summary>Ticks between rebuilds. Sixty is one game second and nothing at all while paused.</summary>
        private const int RebuildIntervalTicks = 60;

        /// <summary>At or above this a skill is worth a row of its own, whoever it belonged to.</summary>
        private const int SkillWorthReading = 9;

        /// <summary>At or above this the gear on a body is worth a row of its own.</summary>
        private const int GearWorthReading = 250;

        /// <summary>How many skills a row has room for.</summary>
        internal const int SkillsShown = 3;

        private static readonly List<CorpseSection> Built = new List<CorpseSection>();

        private static readonly List<CorpseEntry> Spare = new List<CorpseEntry>();

        /// <summary>Scratch for one map's bodies before they are folded. Never held past a rebuild.</summary>
        private static readonly List<CorpseEntry> Loose = new List<CorpseEntry>();

        private static readonly List<CorpseSkill> ScratchSkills = new List<CorpseSkill>();

        private static readonly List<string> ScratchTraits = new List<string>();

        private static int builtAt = -99999;

        private static bool dirty = true;

        /// <summary>Whether bodies already in a grave are listed. Off, because a grave is a settled decision.</summary>
        internal static bool ShowBuried;

        /// <summary>Whether animal bodies are listed. On, because a dead herd is a butchering deadline.</summary>
        internal static bool ShowAnimals = true;

        /// <summary>Which folded groups the player has opened, keyed the way the fold is.</summary>
        internal static readonly HashSet<string> Opened = new HashSet<string>();

        // Colony totals, computed on the same pass so the toolbar is not a second walk of every map.

        /// <summary>Silver still sitting on bodies nobody has stripped, across every map.</summary>
        internal static int GearOnTheDead;

        /// <summary>Meat a butcher would get from every fresh body that yields any.</summary>
        internal static int MeatIfButchered;

        /// <summary>Our own dead lying in the open, whether or not the mood hit has landed yet.</summary>
        internal static int UnburiedColonists;

        /// <summary>Our own dead who are already costing the whole colony mood.</summary>
        internal static int UnburiedCosting;

        // -------------------------------------------------------------------------------------------
        // Access
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The current sections, rebuilt first if they are stale.
        ///
        /// Hold the result for the length of a draw and no longer: a rebuild reuses the same entry objects, so a
        /// reference kept across frames would quietly start describing a different body.
        /// </summary>
        internal static List<CorpseSection> Sections
        {
            get
            {
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

                if (dirty || now - builtAt >= RebuildIntervalTicks || now < builtAt)
                {
                    builtAt = now;
                    dirty = false;

                    UIGuard.Try("Corpses.Gather", Rebuild,
                        "The corpses tab could not finish reading the map, so the list may be incomplete until "
                        + "it refreshes.");
                }

                return Built;
            }
        }

        /// <summary>Forces the next read to rebuild. Called after anything the player does through this tab.</summary>
        internal static void Invalidate()
        {
            dirty = true;
        }

        /// <summary>The row for one body, or null when it is not listed.</summary>
        internal static CorpseEntry EntryFor(Corpse corpse)
        {
            if (corpse == null)
                return null;

            for (int s = 0; s < Built.Count; s++)
            {
                List<CorpseEntry> entries = Built[s].Entries;

                for (int i = 0; i < entries.Count; i++)
                {
                    // A group's head shares its first member's corpse, and the pane is never about a group.
                    if (entries[i].Corpse == corpse && !entries[i].GroupHead)
                        return entries[i];
                }
            }

            return null;
        }

        // -------------------------------------------------------------------------------------------
        // Gathering
        // -------------------------------------------------------------------------------------------

        private static void Rebuild()
        {
            Recycle();

            GearOnTheDead = 0;
            MeatIfButchered = 0;
            UnburiedColonists = 0;
            UnburiedCosting = 0;

            List<Map> maps = Find.Maps;

            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                    GatherMap(maps[i]);
            }

            Fold();
            Ready();
            Sort();
        }

        /// <summary>
        /// Answers, once, whether each row's buttons can do anything.
        ///
        /// After folding rather than during the read, because a group's head owns bodies the individual reads
        /// knew nothing about and its Cremate all has to be judged on all of them.
        /// </summary>
        private static void Ready()
        {
            for (int s = 0; s < Built.Count; s++)
            {
                List<CorpseEntry> entries = Built[s].Entries;

                for (int i = 0; i < entries.Count; i++)
                {
                    CorpseEntry entry = entries[i];

                    string reason;

                    entry.CanBury = CorpseActions.CanBury(entry, out reason);
                    entry.BuryReason = reason;

                    if (entry.Kind == CorpseKind.Hostiles)
                    {
                        entry.CanCremate = CorpseActions.CanCremate(entry, out reason);
                        entry.CremateReason = reason;
                    }

                    if (entry.Kind != CorpseKind.Animals && entry.Kind != CorpseKind.Mechanoids)
                        continue;

                    entry.CanButcher = CorpseActions.CanButcher(entry, out reason);
                    entry.ButcherReason = reason;
                }
            }
        }

        private static void Recycle()
        {
            for (int s = 0; s < Built.Count; s++)
            {
                List<CorpseEntry> entries = Built[s].Entries;

                for (int i = 0; i < entries.Count; i++)
                {
                    entries[i].Reset();
                    Spare.Add(entries[i]);
                }

                entries.Clear();
            }

            if (Built.Count != 0)
                return;

            Add(CorpseKind.Ours);
            Add(CorpseKind.Guests);
            Add(CorpseKind.Hostiles);
            Add(CorpseKind.Animals);
            Add(CorpseKind.Mechanoids);
        }

        private static void Add(CorpseKind kind)
        {
            Built.Add(new CorpseSection { Kind = kind, Label = CorpseFacts.LabelOf(kind) });
        }

        private static CorpseEntry Take()
        {
            if (Spare.Count == 0)
                return new CorpseEntry();

            CorpseEntry entry = Spare[Spare.Count - 1];

            Spare.RemoveAt(Spare.Count - 1);

            return entry;
        }

        private static CorpseSection SectionFor(CorpseKind kind)
        {
            for (int i = 0; i < Built.Count; i++)
            {
                if (Built[i].Kind == kind)
                    return Built[i];
            }

            return Built[0];
        }

        /// <summary>
        /// One map's bodies: the ones lying about, and the ones in graves when they are wanted.
        ///
        /// <b>Two walks, because a buried body is not spawned.</b> A corpse in a grave lives inside the grave's
        /// container, so <c>ThingRequestGroup.Corpse</c> cannot see it -- which is also why burying one silences
        /// the unburied thought.
        /// </summary>
        private static void GatherMap(Map map)
        {
            if (map == null || map.listerThings == null)
                return;

            List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);

            for (int i = 0; corpses != null && i < corpses.Count; i++)
                Consider(corpses[i] as Corpse, map, null);

            List<Thing> graves = map.listerThings.ThingsInGroup(ThingRequestGroup.Grave);

            for (int i = 0; graves != null && i < graves.Count; i++)
            {
                Building_Grave grave = graves[i] as Building_Grave;

                if (grave == null || grave.Corpse == null)
                    continue;

                Consider(grave.Corpse, map, grave);
            }
        }

        private static void Consider(Corpse corpse, Map map, Building_Grave grave)
        {
            if (corpse == null || corpse.Destroyed)
                return;

            // A corpse whose pawn never finished generating. Vanilla's own guard, and reading one throws.
            if (UIGuard.Try("Corpses.Bugged", () => corpse.Bugged, true, null))
                return;

            Pawn pawn = UIGuard.Try("Corpses.Inner", () => corpse.InnerPawn, null, null);

            if (pawn == null)
                return;

            CorpseKind kind = CorpseFacts.KindOf(pawn);

            CorpseEntry entry = Take();

            Read(entry, corpse, pawn, map, grave, kind);

            Total(entry);

            // Counted before the filters, so the readouts still say what the colony owes even with the list
            // narrowed. A toggle changes what is shown, not what is true.
            if (grave != null && !ShowBuried)
            {
                entry.Reset();
                Spare.Add(entry);

                return;
            }

            if (kind == CorpseKind.Animals && !ShowAnimals)
            {
                entry.Reset();
                Spare.Add(entry);

                return;
            }

            // Last of the three, and before folding: a group must never contain a body the filter excluded, or
            // "Strip all" would act on one that is not on screen.
            if (!CorpseFilter.Matches(pawn))
            {
                entry.Reset();
                Spare.Add(entry);

                return;
            }

            Loose.Add(entry);
        }

        /// <summary>
        /// Adds one body to the colony figures in the toolbar.
        ///
        /// <b>Counted before the display filters, so a toggle changes what is shown and not what is true.</b>
        /// Turning animals off should hide sixty muffalo, not tell you the freezer has nothing waiting for it.
        ///
        /// <b>A buried body counts towards none of it.</b> Both figures are about work outstanding -- silver in
        /// the mud and meat going off -- and a body in a grave is a decision that has already been made. Its
        /// gear is still recoverable by emptying the grave, but nobody reading "gear on the dead" is asking
        /// which of their own colonists to exhume.
        /// </summary>
        private static void Total(CorpseEntry entry)
        {
            if (entry.Grave != null)
                return;

            GearOnTheDead += entry.GearValue;

            if (entry.Stage == RotStage.Fresh)
                MeatIfButchered += entry.Meat;

            if (entry.UnburiedIn == int.MaxValue)
                return;

            UnburiedColonists++;

            if (entry.UnburiedIn <= 0)
                UnburiedCosting++;
        }

        private static void Read(CorpseEntry entry, Corpse corpse, Pawn pawn, Map map, Building_Grave grave,
            CorpseKind kind)
        {
            entry.Corpse = corpse;
            entry.Pawn = pawn;
            entry.Map = map;
            entry.Kind = kind;
            entry.Grave = grave;
            entry.Members.Add(corpse);

            entry.Name = UIGuard.Try<string>("Corpses.Name",
                () => kind == CorpseKind.Animals || kind == CorpseKind.Mechanoids
                    ? pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString()
                    : pawn.LabelShortCap.ToString(), "?", null);

            entry.Subline = CorpseFacts.Subline(corpse, pawn, kind);

            entry.Stage = CorpseFacts.StageOf(corpse);
            entry.Progress = CorpseFacts.ProgressOf(corpse);
            entry.Frozen = CorpseFacts.Frozen(corpse);
            entry.RotNote = CorpseFacts.RotNote(corpse);
            entry.DaysRotted = CorpseFacts.DaysRotted(corpse);

            CorpseFacts.Skills(pawn, ScratchSkills, SkillsShown);

            entry.Skills.AddRange(ScratchSkills);

            CorpseFacts.Traits(pawn, ScratchTraits);

            entry.Traits.AddRange(ScratchTraits);
            entry.TraitTotal = ScratchTraits.Count;

            int count;
            int value;

            CorpseFacts.Gear(pawn, out count, out value);

            entry.GearCount = count;
            entry.GearValue = value;

            entry.Strippable = UIGuard.Try("Corpses.Strippable", corpse.AnythingToStrip, false, null);
            entry.StripQueued = CorpseActions.StripQueued(corpse);

            entry.Meat = CorpseFacts.Meat(pawn);

            string leather;

            entry.Leather = CorpseFacts.Leather(pawn, out leather);
            entry.LeatherLabel = leather;

            string where;
            string note;

            CorpseFacts.Where(corpse, out where, out note);

            entry.Where = where;
            entry.WhereNote = note;
            entry.UnburiedIn = grave != null ? int.MaxValue : CorpseFacts.UnburiedIn(corpse);

            entry.GroupKey = KeyFor(entry);
            entry.Urgency = UrgencyOf(entry);
        }

        // -------------------------------------------------------------------------------------------
        // Folding
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// What makes two bodies interchangeable, or null when this one has to stand alone.
        ///
        /// <b>The test is whether the row would say anything the other row does not.</b> A skill worth reading, a
        /// gear pile worth taking, or a name somebody gave it: any of the three and the body keeps its row. Our
        /// own dead and the guests we are answerable for are never folded, whatever they were carrying, because
        /// the point of those two sections is that these are people we knew.
        /// </summary>
        private static string KeyFor(CorpseEntry entry)
        {
            if (entry.Kind == CorpseKind.Ours || entry.Kind == CorpseKind.Guests)
                return null;

            if (entry.Grave != null)
                return null;

            if (CorpseFacts.TopSkill(entry.Pawn) >= SkillWorthReading)
                return null;

            if (entry.GearValue >= GearWorthReading)
                return null;

            if (entry.Pawn.Name != null && !entry.Pawn.Name.Numerical)
                return null;

            string faction = entry.Pawn.Faction != null ? entry.Pawn.Faction.GetUniqueLoadID() : "none";

            return entry.Map.uniqueID + "/" + entry.Pawn.def.defName + "/" + faction + "/" + entry.Stage;
        }

        /// <summary>Bodies of one group, gathered before it is decided whether the group survives.</summary>
        private static readonly Dictionary<string, List<CorpseEntry>> Groups =
            new Dictionary<string, List<CorpseEntry>>();

        /// <summary>Every key seen this rebuild, so opened groups that no longer exist stop being remembered.</summary>
        private static readonly HashSet<string> Seen = new HashSet<string>();

        /// <summary>
        /// Collapses interchangeable bodies into one summary row each.
        ///
        /// <b>An opened group keeps its summary row and gains its members underneath.</b> The alternative --
        /// replacing the summary with the members -- leaves nothing to press to close it again, which is how a
        /// list gets stuck open. So the head stays, says how many it stands for, and folds when pressed; and
        /// because the head owns every body in the group, its Strip all still means all of them while the
        /// individual rows below act on one each.
        ///
        /// <b>A group of one is not a group.</b> It draws as the ordinary row it always was.
        /// </summary>
        private static void Fold()
        {
            Groups.Clear();
            Seen.Clear();

            for (int i = 0; i < Loose.Count; i++)
            {
                CorpseEntry entry = Loose[i];

                if (entry.GroupKey == null)
                {
                    entry.SortUrgency = entry.Urgency;

                    SectionFor(entry.Kind).Entries.Add(entry);

                    continue;
                }

                Seen.Add(entry.GroupKey);

                List<CorpseEntry> members;

                if (!Groups.TryGetValue(entry.GroupKey, out members))
                {
                    members = new List<CorpseEntry>();

                    Groups[entry.GroupKey] = members;
                }

                members.Add(entry);
            }

            Loose.Clear();

            foreach (KeyValuePair<string, List<CorpseEntry>> pair in Groups)
            {
                List<CorpseEntry> members = pair.Value;

                if (members.Count == 1)
                {
                    members[0].GroupKey = null;
                    members[0].SortUrgency = members[0].Urgency;

                    SectionFor(members[0].Kind).Entries.Add(members[0]);

                    continue;
                }

                bool open = Opened.Contains(pair.Key);

                CorpseEntry head = Summarise(pair.Key, members, open);

                SectionFor(head.Kind).Entries.Add(head);

                for (int i = 0; i < members.Count; i++)
                {
                    if (!open)
                    {
                        members[i].Reset();
                        Spare.Add(members[i]);

                        continue;
                    }

                    members[i].InGroup = true;
                    members[i].SortUrgency = head.Urgency;

                    SectionFor(members[i].Kind).Entries.Add(members[i]);
                }
            }

            Opened.RemoveWhere(key => !Seen.Contains(key));

            Groups.Clear();
        }

        /// <summary>
        /// Builds the one row that stands for a group.
        ///
        /// A row of its own rather than the first member promoted: the head owns every body in the group, and a
        /// member that is also the head would be acted on twice by an opened group's Strip all followed by its
        /// own Strip.
        /// </summary>
        private static CorpseEntry Summarise(string key, List<CorpseEntry> members, bool open)
        {
            CorpseEntry first = members[0];

            CorpseEntry head = Take();

            head.Corpse = first.Corpse;
            head.Pawn = first.Pawn;
            head.Map = first.Map;
            head.Kind = first.Kind;
            head.GroupKey = key;
            head.GroupHead = true;
            head.Stage = first.Stage;
            head.Frozen = first.Frozen;
            head.RotNote = first.RotNote;
            head.Where = first.Where;
            head.WhereNote = first.WhereNote;
            head.UnburiedIn = int.MaxValue;
            head.Strippable = false;
            head.StripQueued = true;

            float low = 1f;
            float high = 0f;

            for (int i = 0; i < members.Count; i++)
            {
                CorpseEntry member = members[i];

                head.Members.Add(member.Corpse);

                head.GearCount += member.GearCount;
                head.GearValue += member.GearValue;
                head.Meat += member.Meat;
                head.Leather += member.Leather;
                head.TraitTotal += member.TraitTotal;

                head.Strippable = head.Strippable || member.Strippable;
                head.StripQueued = head.StripQueued && member.StripQueued;

                head.Progress = Mathf.Max(head.Progress, member.Progress);
                head.DaysRotted = Mathf.Max(head.DaysRotted, member.DaysRotted);
                head.Urgency = Mathf.Max(head.Urgency, member.Urgency);

                low = Mathf.Min(low, member.Progress);
                high = Mathf.Max(high, member.Progress);

                if (head.Where != member.Where)
                    head.Where = "Several places";

                if (head.WhereNote != member.WhereNote)
                    head.WhereNote = null;
            }

            head.LeatherLabel = first.LeatherLabel;
            head.SortUrgency = head.Urgency;

            head.Name = members.Count + " " + Plural(head);

            string faction = head.Pawn != null && head.Pawn.Faction != null ? head.Pawn.Faction.Name : null;

            head.Subline = (faction.NullOrEmpty() ? "Folded together" : faction.ToString())
                           + (open ? " - click to fold" : " - click to open");

            head.Spread = Mathf.RoundToInt(low * 100f) == Mathf.RoundToInt(high * 100f)
                ? null
                : Mathf.RoundToInt(low * 100f) + " - " + Mathf.RoundToInt(high * 100f) + "%";

            return head;
        }

        private static string Plural(CorpseEntry entry)
        {
            return UIGuard.Try<string>("Corpses.Plural", () =>
            {
                if (entry.Kind == CorpseKind.Hostiles)
                    return "raiders";

                string label = entry.Pawn.def.label;

                return label.NullOrEmpty() ? "bodies" : Find.ActiveLanguageWorker.Pluralize(label);
            }, "bodies", null);
        }

        // -------------------------------------------------------------------------------------------
        // Sorting
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Worst first inside every section, where worst means most in need of a decision today.
        ///
        /// <b>The mood clock outranks everything,</b> because it is the one row on this tab that is costing the
        /// colony something every hour it is left. Under that, decay: a body most of the way through a stage is
        /// about to become a different problem, and a desiccated one has finished becoming it and can wait. Gear
        /// breaks the remaining ties, which is what puts the armoured raider above the naked one.
        /// </summary>
        private static float UrgencyOf(CorpseEntry entry)
        {
            if (entry.Grave != null)
                return -1000f;

            float score = 0f;

            if (entry.UnburiedIn <= 0)
                score += 2000f;
            else if (entry.UnburiedIn != int.MaxValue)
                score += 1000f;

            switch (entry.Stage)
            {
                case RotStage.Fresh:
                    score += entry.Progress * 100f;

                    break;

                case RotStage.Rotting:
                    score += 100f + entry.Progress * 50f;

                    break;
            }

            return score + Mathf.Min(50f, entry.GearValue / 40f);
        }

        private static void Sort()
        {
            for (int i = 0; i < Built.Count; i++)
                Built[i].Entries.Sort(Compare);
        }

        /// <summary>
        /// Urgency first, then group, then the head before its members.
        ///
        /// The three tiebreaks exist so an opened group stays contiguous: without the group key two groups whose
        /// heads happen to score the same would interleave their members, and without the head test the summary
        /// row could land in the middle of the bodies it summarises.
        /// </summary>
        private static int Compare(CorpseEntry a, CorpseEntry b)
        {
            int byUrgency = b.SortUrgency.CompareTo(a.SortUrgency);

            if (byUrgency != 0)
                return byUrgency;

            int byGroup = string.Compare(a.GroupKey ?? string.Empty, b.GroupKey ?? string.Empty,
                System.StringComparison.Ordinal);

            if (byGroup != 0)
                return byGroup;

            if (a.GroupHead != b.GroupHead)
                return a.GroupHead ? -1 : 1;

            return string.Compare(a.Name, b.Name, System.StringComparison.Ordinal);
        }
    }
}
