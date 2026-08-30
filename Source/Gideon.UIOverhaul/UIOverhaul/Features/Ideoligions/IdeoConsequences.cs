using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>What a choice does, in four kinds. The tag is the word the row is filed under.</summary>
    internal enum ConsequenceKind
    {
        Gains,
        Requires,
        Forbids,
        RulesOut
    }

    internal struct ConsequenceRow
    {
        internal ConsequenceKind kind;
        internal string text;
    }

    /// <summary>One thing that happens to the colony you already have.</summary>
    internal struct DiffRow
    {
        internal bool good;
        internal string text;
    }

    /// <summary>
    /// The two columns beside the designer: what a meme set costs, and what it does to this colony.
    ///
    /// <b>This is the half of the design the mockup called the whole idea.</b> Vanilla's meme picker says what a
    /// meme <i>is</i>; it never says what it will <i>do</i>, and conflicts arrive as a refusal after the fact. All
    /// four consequence kinds are readable from <c>MemeDef</c> before anything is committed --
    /// <c>requiredRituals</c>, <c>requireAnyRitualSeat</c>, <c>exclusionTags</c> -- so there is no reason for the
    /// player to find out afterwards.
    ///
    /// <b>Nothing here is named in code.</b> Every row is built by enumerating the draft's own memes and the
    /// database, so a meme from a DLC nobody has installed simply is not in the list, and a modded meme produces
    /// its rows with no cooperation from its author.
    /// </summary>
    internal static class IdeoConsequences
    {
        /// <summary>
        /// What the draft's meme set demands and forbids.
        ///
        /// Deduplicated by the finished line rather than by what produced it: two memes can require the same
        /// ritual seat, and saying so twice reads as a longer bill than it is.
        /// </summary>
        internal static List<ConsequenceRow> Of(IdeoDraft draft)
        {
            List<ConsequenceRow> rows = new List<ConsequenceRow>();

            if (draft == null)
                return rows;

            UIGuard.Try("Ideoligions.Consequences", () =>
            {
                HashSet<string> seen = new HashSet<string>();

                for (int i = 0; i < draft.draft.memes.Count; i++)
                    Meme(draft.draft.memes[i], draft, rows, seen);
            }, null);

            return rows;
        }

        private static void Meme(MemeDef meme, IdeoDraft draft, List<ConsequenceRow> rows, HashSet<string> seen)
        {
            if (meme == null)
                return;

            // The rituals a meme brings with it, and the building each one needs a seat of.
            if (meme.requiredRituals != null)
            {
                for (int i = 0; i < meme.requiredRituals.Count; i++)
                {
                    RequiredRitualAndBuilding required = meme.requiredRituals[i];

                    // The ritual is named by its precept def, or by the pattern when the meme asks for a shape
                    // of ritual rather than a particular one.
                    if (required?.precept != null)
                        Add(rows, seen, ConsequenceKind.Gains, required.precept.LabelCap + " ritual");
                    else if (required?.pattern != null)
                        Add(rows, seen, ConsequenceKind.Gains, required.pattern.LabelCap + " ritual");

                    if (required?.building != null)
                        Add(rows, seen, ConsequenceKind.Requires, required.building.label);
                }
            }

            if (meme.requireAnyRitualSeat != null && meme.requireAnyRitualSeat.Count > 0)
            {
                List<string> names = new List<string>();

                for (int i = 0; i < meme.requireAnyRitualSeat.Count; i++)
                {
                    if (meme.requireAnyRitualSeat[i] != null)
                        names.Add(meme.requireAnyRitualSeat[i].label);
                }

                if (names.Count > 0)
                    Add(rows, seen, ConsequenceKind.Requires, "a place to sit: " + string.Join(" or ", names));
            }

            // What taking this meme costs you elsewhere in the list: every meme in the database it shares an
            // exclusion tag with, which is exactly the wall vanilla lets you walk into.
            List<string> ruled = RuledOut(meme, draft);

            if (ruled.Count > 0)
                Add(rows, seen, ConsequenceKind.RulesOut, string.Join(", ", ruled.ToArray()));

            // Precepts the meme forces, which is where the "forbids" half comes from: a precept at high impact
            // is the faith saying somebody may not do something.
            if (meme.requireOne != null)
            {
                for (int i = 0; i < meme.requireOne.Count; i++)
                {
                    List<PreceptDef> group = meme.requireOne[i];

                    if (group == null || group.Count == 0)
                        continue;

                    PreceptDef first = group[0];

                    if (first?.issue != null)
                        Add(rows, seen, ConsequenceKind.Forbids, "a ruling on " + first.issue.label);
                }
            }
        }

        /// <summary>Memes this one excludes, named, and capped so one greedy tag cannot fill the column.</summary>
        private static List<string> RuledOut(MemeDef meme, IdeoDraft draft)
        {
            List<string> names = new List<string>();

            if (meme.exclusionTags.NullOrEmpty())
                return names;

            List<MemeDef> all = DefDatabase<MemeDef>.AllDefsListForReading;

            for (int i = 0; i < all.Count && names.Count < 6; i++)
            {
                MemeDef other = all[i];

                if (other == null || other == meme || other.hiddenInChooseMemes
                    || other.exclusionTags.NullOrEmpty() || draft.draft.memes.Contains(other))
                    continue;

                for (int t = 0; t < meme.exclusionTags.Count; t++)
                {
                    if (other.exclusionTags.Contains(meme.exclusionTags[t]))
                    {
                        names.Add(other.LabelCap);

                        break;
                    }
                }
            }

            return names;
        }

        private static void Add(List<ConsequenceRow> rows, HashSet<string> seen, ConsequenceKind kind, string text)
        {
            if (text.NullOrEmpty() || !seen.Add(kind + "|" + text))
                return;

            rows.Add(new ConsequenceRow { kind = kind, text = text });
        }

        // -------------------------------------------------------------------------------------------
        // And then what happens to the colony I already have
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// What the reform does to the people and the buildings that already exist.
        ///
        /// <b>This is the question the mockup put beside every choice,</b> and it is the one vanilla never
        /// answers: the reform screen shows you the new ideoligion and never what your colonists lose. Each row
        /// here is a read against the live colony rather than against the def database.
        ///
        /// <b>The certainty hit is stated rather than computed.</b> Reforming costs every believer certainty, and
        /// the exact figure is <c>ApplyChangesToIdeo</c>'s to decide; quoting a number we worked out separately
        /// would be a second opinion that goes stale the first time Ludeon changes theirs.
        /// </summary>
        internal static List<DiffRow> Colony(IdeoDraft draft)
        {
            List<DiffRow> rows = new List<DiffRow>();

            if (draft == null)
                return rows;

            UIGuard.Try("Ideoligions.ColonyDiff", () =>
            {
                List<Precept> losing = draft.Losing();

                for (int i = 0; i < losing.Count && i < 6; i++)
                {
                    rows.Add(new DiffRow
                    {
                        good = false,
                        text = "The faith stops ruling on " + losing[i].def.issue.label
                    });
                }

                Roles(draft, rows);

                if (draft.Changed())
                {
                    rows.Add(new DiffRow
                    {
                        good = false,
                        text = "Every believer takes a hit to their certainty for the reform"
                    });
                }

                if (IdeoDraft.Preserve && (draft.NormalMemesChanged || draft.StructureChanged))
                {
                    rows.Add(new DiffRow
                    {
                        good = true,
                        text = "The doctrine is kept exactly as it is, precepts, roles and rituals included"
                    });
                }
            }, null);

            return rows;
        }

        /// <summary>
        /// Role holders who would stop qualifying, and roles that would appear.
        ///
        /// Compared by def between the two precept lists, because a role precept on the draft is a different
        /// object from the one on the live faith even when it is the same role.
        /// </summary>
        private static void Roles(IdeoDraft draft, List<DiffRow> rows)
        {
            List<Precept> before = draft.original.PreceptsListForReading;
            List<Precept> after = draft.draft.PreceptsListForReading;

            for (int i = 0; i < after.Count; i++)
            {
                Precept_Role role = after[i] as Precept_Role;

                if (role == null || Has(before, role.def))
                    continue;

                rows.Add(new DiffRow { good = true, text = role.LabelCap + " becomes a role you can fill" });
            }

            for (int i = 0; i < before.Count; i++)
            {
                Precept_Role role = before[i] as Precept_Role;

                if (role == null || Has(after, role.def))
                    continue;

                Pawn holder = UIGuard.Try("Ideoligions.DiffRoleHolder", () => role.ChosenPawnSingle(), null, null);

                rows.Add(new DiffRow
                {
                    good = false,
                    text = holder != null
                        ? holder.LabelShortCap + " stops being " + role.LabelCap
                        : role.LabelCap + " is no longer a role"
                });
            }
        }

        private static bool Has(List<Precept> precepts, PreceptDef def)
        {
            for (int i = 0; i < precepts.Count; i++)
            {
                if (precepts[i]?.def == def)
                    return true;
            }

            return false;
        }
    }
}
