using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>How settled one believer is. Ordered weakest first, because that is the list you act on.</summary>
    internal struct ConvictionRow
    {
        internal Pawn pawn;
        internal float certainty;
        internal float drift;
    }

    /// <summary>One role, its holder, and what is wrong with the arrangement.</summary>
    internal struct RoleRow
    {
        internal Precept_Role role;
        internal Pawn holder;

        /// <summary>Null when the holder qualifies. Otherwise the first thing they fail.</summary>
        internal string fault;

        /// <summary>How many believers could take it, counted only when nobody holds it.</summary>
        internal int eligible;
    }

    /// <summary>One ritual and when it is next owed.</summary>
    internal struct ObligationRow
    {
        internal Precept_Ritual ritual;
        internal string when;
        internal bool owed;
        internal string note;
    }

    /// <summary>One building the faith demands, judged against the map you are looking at.</summary>
    internal struct DemandRow
    {
        internal Precept_Building precept;
        internal string state;
        internal bool met;

        /// <summary>Built, but not respected: blocked, in the wrong room, or short of a room requirement.</summary>
        internal bool disrespected;
    }

    /// <summary>One issue and where the faith stands on it.</summary>
    internal struct DoctrineRow
    {
        internal Precept precept;
        internal string issue;
        internal string stance;
        internal PreceptImpact impact;
    }

    /// <summary>
    /// Everything the ideoligions tab shows, read off the game and handed over as rows.
    ///
    /// <b>Separated from the drawing for the reason every other tab in this mod separates them:</b> a block that
    /// both reads and draws cannot be reasoned about when one of the two is wrong, and the reads here are the
    /// interesting half. Nothing in this file touches <c>GUI</c>.
    ///
    /// <b>Nothing here names a meme, a precept or an issue.</b> Everything is enumerated out of the database or
    /// off the ideoligion itself, which is what makes the screen correct for a player with one expansion and for
    /// a player with four -- and what makes a modded precept appear with no cooperation from its author. A def
    /// looked up by name is a def that is absent on somebody's install, and a hard reference is a crash rather
    /// than a gap.
    ///
    /// <b>Every method here is called from inside a guard by its caller,</b> so the bodies are written plainly
    /// rather than each wrapping itself. The exceptions are the two that reach into per-pawn state deep enough to
    /// be worth their own site.
    /// </summary>
    internal static class IdeoFacts
    {
        /// <summary>Below this, they are being pulled away from the faith rather than merely doubting.</summary>
        internal const float ConvertingBelow = 0.3f;

        /// <summary>Below this, they are unsettled enough to be worth naming.</summary>
        internal const float DoubtingBelow = 0.5f;

        /// <summary>At or above this, nothing short of a reform will move them.</summary>
        internal const float DevoutFrom = 0.9f;

        /// <summary>
        /// The faiths to list, colony first.
        ///
        /// <b>The colony's own are those with a believer in it,</b> which is the distinction the rail is built on:
        /// a faith somebody in this colony holds is one whose conviction, roles and demands are live, and a faith
        /// merely known to the world is reference. <c>ColonistBelieverCountCached</c> is the game's own count and
        /// is what the header shows too, so the two cannot disagree.
        /// </summary>
        internal static List<Ideo> Faiths(bool inColony)
        {
            List<Ideo> found = new List<Ideo>();

            if (Find.IdeoManager == null)
                return found;

            // Enumerated rather than indexed: IdeosInViewOrder hands back a sequence, and materializing it into
            // a list here only to walk it once would allocate on every frame this screen draws.
            foreach (Ideo ideo in Find.IdeoManager.IdeosInViewOrder)
            {
                if (ideo == null || ideo.hidden)
                    continue;

                if (ideo.ColonistBelieverCountCached > 0 == inColony)
                    found.Add(ideo);
            }

            return found;
        }

        /// <summary>
        /// The faith to open on: whatever vanilla last had selected, else the colony's primary, else anything.
        ///
        /// <b>Vanilla's own selection is borrowed rather than kept separately,</b> so opening our tab, opening a
        /// pawn's ideoligion from their bio, and anything else that calls <c>IdeoUIUtility.SetSelected</c> all
        /// agree about which faith is being looked at.
        /// </summary>
        internal static Ideo Selected()
        {
            Ideo selected = IdeoUIUtility.selected;

            if (selected != null && !selected.hidden)
                return selected;

            return IdeoUIUtility.FallbackSelectedIdeo;
        }

        // -------------------------------------------------------------------------------------------
        // Conviction
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Every believer in the colony, weakest first.
        ///
        /// <b>Caravans and transporters are included,</b> because a colonist halfway across the world can still
        /// lose their faith and the tab that would have told you is the one they are missing from.
        ///
        /// <b>Sorted ascending on certainty rather than on drift,</b> which is the choice the mockup argued for:
        /// the pawn about to leave the faith is the one you act on, and a devout pawn drifting downward is not
        /// yet a problem.
        /// </summary>
        internal static List<ConvictionRow> Conviction(Ideo ideo)
        {
            List<ConvictionRow> rows = new List<ConvictionRow>();

            if (ideo == null)
                return rows;

            List<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;

            for (int i = 0; pawns != null && i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];

                if (pawn?.ideo == null || pawn.ideo.Ideo != ideo)
                    continue;

                rows.Add(new ConvictionRow
                {
                    pawn = pawn,
                    certainty = pawn.ideo.Certainty,

                    // Read through a guard of its own: the drift is recomputed from the pawn's situational
                    // thoughts and their role, and it is the one read here that walks somebody else's data.
                    drift = UIGuard.Try("Ideoligions.Drift", () => pawn.ideo.CertaintyChangePerDay, 0f, null)
                });
            }

            rows.Sort((a, b) => a.certainty.CompareTo(b.certainty));

            return rows;
        }

        /// <summary>How many of them are losing certainty, which is the number the block's heading carries.</summary>
        internal static int Drifting(List<ConvictionRow> rows)
        {
            int count = 0;

            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                if (rows[i].drift < -0.001f)
                    count++;
            }

            return count;
        }

        // -------------------------------------------------------------------------------------------
        // Roles
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Every role the faith defines, whether or not anybody holds it.
        ///
        /// <b>An empty role is the interesting one and is listed first among equals,</b> so the order is: unheld
        /// roles, then held roles whose holder no longer qualifies, then the rest. That is rate of change again --
        /// a vacancy and a lapsed holder are both things to do something about today.
        /// </summary>
        internal static List<RoleRow> Roles(Ideo ideo)
        {
            List<RoleRow> rows = new List<RoleRow>();

            if (ideo == null)
                return rows;

            List<Precept_Role> roles = ideo.RolesListForReading;

            for (int i = 0; roles != null && i < roles.Count; i++)
            {
                Precept_Role role = roles[i];

                if (role == null)
                    continue;

                RoleRow row = new RoleRow { role = role };

                row.holder = UIGuard.Try("Ideoligions.RoleHolder", () => role.ChosenPawnSingle(), null, null);

                if (row.holder == null)
                    row.eligible = Eligible(ideo, role);
                else
                    row.fault = Fault(role, row.holder);

                rows.Add(row);
            }

            rows.Sort((a, b) => Rank(a).CompareTo(Rank(b)));

            return rows;
        }

        /// <summary>Unfilled first, then lapsed, then settled. Ties keep the ideoligion's own order.</summary>
        private static int Rank(RoleRow row)
        {
            if (row.holder == null)
                return 0;

            return row.fault != null ? 1 : 2;
        }

        /// <summary>
        /// How many believers could take an empty role.
        ///
        /// The game's own <c>RequirementsMet</c>, which covers the role's requirements but not its apparel: a
        /// pawn qualifies for a role they are not yet dressed for, and telling the player otherwise would hide
        /// the candidate they are looking for.
        /// </summary>
        private static int Eligible(Ideo ideo, Precept_Role role)
        {
            return UIGuard.Try("Ideoligions.RoleEligible", () =>
            {
                int count = 0;
                List<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;

                for (int i = 0; pawns != null && i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];

                    if (pawn?.ideo != null && pawn.ideo.Ideo == ideo && role.RequirementsMet(pawn))
                        count++;
                }

                return count;
            }, 0, null);
        }

        /// <summary>
        /// The first thing wrong with this holder, or null when there is nothing.
        ///
        /// <b>The role's own requirements are asked first and the apparel second,</b> in that order because a
        /// pawn who has stopped qualifying at all is a bigger problem than one who has taken the hat off. Both
        /// answers are the game's own: <c>GetFirstUnmetRequirement</c> hands back a requirement that can label
        /// itself, and <c>ApparelRequirement.IsMet</c> is the same test the role's own alert uses.
        /// </summary>
        private static string Fault(Precept_Role role, Pawn holder)
        {
            return UIGuard.Try("Ideoligions.RoleFault", () =>
            {
                RoleRequirement unmet = role.GetFirstUnmetRequirement(holder);

                if (unmet != null)
                    return unmet.GetLabelCap(role);

                List<PreceptApparelRequirement> apparel = role.ApparelRequirements;

                for (int i = 0; apparel != null && i < apparel.Count; i++)
                {
                    ApparelRequirement requirement = apparel[i]?.requirement;

                    if (requirement == null || !requirement.IsActive(holder) || requirement.IsMet(holder))
                        continue;

                    return Missing(requirement, holder);
                }

                return null;
            }, null, null);
        }

        /// <summary>
        /// What they are not wearing.
        ///
        /// The requirement's own group label when it has one, since that is the wording the game uses on the role
        /// itself; otherwise the first thing that would satisfy it, which is more use than "apparel required".
        /// </summary>
        private static string Missing(ApparelRequirement requirement, Pawn holder)
        {
            if (!requirement.groupLabel.NullOrEmpty())
                return requirement.groupLabel + " missing";

            foreach (ThingDef def in requirement.AllRequiredApparel(holder.gender))
            {
                if (def != null)
                    return def.label + " missing";
            }

            return "not dressed for it";
        }

        // -------------------------------------------------------------------------------------------
        // Obligations
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Every ritual the faith carries, owed ones first.
        ///
        /// <b>What is shown is what the game actually keeps.</b> The mockup wanted the quality of the last
        /// performance on each row; nothing on <c>Precept_Ritual</c> stores it -- only the tick it last finished
        /// and the repeat penalty that decays from it -- so the row says when instead, and the penalty when there
        /// is one. Inventing a remembered quality would have meant recording it ourselves from this point
        /// forward, which would read as history and be blank for every ritual held before the mod was installed.
        /// </summary>
        internal static List<ObligationRow> Obligations(Ideo ideo)
        {
            List<ObligationRow> rows = new List<ObligationRow>();

            if (ideo == null)
                return rows;

            List<Precept> precepts = ideo.PreceptsListForReading;

            for (int i = 0; precepts != null && i < precepts.Count; i++)
            {
                Precept_Ritual ritual = precepts[i] as Precept_Ritual;

                if (ritual == null)
                    continue;

                rows.Add(Row(ritual));
            }

            rows.Sort((a, b) => (a.owed ? 0 : 1).CompareTo(b.owed ? 0 : 1));

            return rows;
        }

        private static ObligationRow Row(Precept_Ritual ritual)
        {
            ObligationRow row = new ObligationRow { ritual = ritual };

            UIGuard.Try("Ideoligions.Obligation", () =>
            {
                int active = ritual.activeObligations?.Count ?? 0;

                if (active > 0)
                {
                    row.owed = true;

                    // The soonest to lapse, since that is the one the deadline belongs to.
                    int soonest = int.MaxValue;

                    for (int i = 0; i < ritual.activeObligations.Count; i++)
                    {
                        RitualObligation obligation = ritual.activeObligations[i];

                        if (obligation != null && obligation.expires)
                            soonest = Mathf.Min(soonest, obligation.TicksUntilExpiration);
                    }

                    row.when = soonest == int.MaxValue
                        ? (active > 1 ? active + " owed" : "owed")
                        : soonest <= 0
                            ? "lapsing now"
                            : "owed, " + soonest.ToStringTicksToPeriod() + " left";
                }
                else if (ritual.lastFinishedTick > 0)
                {
                    row.when = "last held " + ritual.TicksSinceLastPerformed.ToStringTicksToPeriod() + " ago";
                }
                else
                {
                    row.when = "never held";
                }

                if (ritual.RepeatPenaltyActive)
                    row.note = "repeat penalty " + ritual.RepeatQualityPenalty.ToStringPercent();
            }, null);

            return row;
        }

        // -------------------------------------------------------------------------------------------
        // Demands
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// What the faith demands be built, judged against one map.
        ///
        /// <b>One map rather than all of them,</b> because a demand is satisfied per map and a row that merged
        /// four colonies would be answering a question nobody asked. The map is named in the heading so the
        /// scope is never in doubt.
        ///
        /// <b>This is the data behind <c>Alert_IdeoBuildingMissing</c> and its disrespected twin,</b> read from
        /// the demand rather than from the alerts: an alert appears and clears, and the point of putting it here
        /// is to have a standing readout that says "met" as well as "not".
        /// </summary>
        internal static List<DemandRow> Demands(Ideo ideo, Map map)
        {
            List<DemandRow> rows = new List<DemandRow>();

            if (ideo == null || map == null)
                return rows;

            List<Precept> precepts = ideo.PreceptsListForReading;

            for (int i = 0; precepts != null && i < precepts.Count; i++)
            {
                Precept_Building building = precepts[i] as Precept_Building;
                IdeoBuildingPresenceDemand demand = building?.presenceDemand;

                if (demand == null)
                    continue;

                DemandRow row = new DemandRow { precept = building };

                bool listed = UIGuard.Try("Ideoligions.Demand", () =>
                {
                    if (!demand.AppliesTo(map))
                        return false;

                    if (!demand.BuildingPresent(map))
                    {
                        row.state = "none built";

                        return true;
                    }

                    if (!demand.RequirementsSatisfied(map))
                    {
                        row.disrespected = true;
                        row.state = "built, not respected";

                        return true;
                    }

                    row.met = true;
                    row.state = "met";

                    return true;
                }, false, null);

                if (listed)
                    rows.Add(row);
            }

            rows.Sort((a, b) => Rank(a).CompareTo(Rank(b)));

            return rows;
        }

        /// <summary>Missing first, then disrespected, then met. Worst news at the top of the block.</summary>
        private static int Rank(DemandRow row)
        {
            if (row.met)
                return 2;

            return row.disrespected ? 1 : 0;
        }

        // -------------------------------------------------------------------------------------------
        // Doctrine
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The faith's position on every issue it has one on.
        ///
        /// <b>Roles, rituals and building precepts are left out, and that is the layout decision rather than an
        /// oversight.</b> All three are precepts with issues and all three already have a block of their own
        /// further up the screen, where they say something that changes. Listing them again down here would put
        /// the Moral Guide on the screen twice and say less about them the second time.
        ///
        /// <b>Sorted by impact, high first.</b> Doctrine is the part that only changes at a reform, so it is
        /// reference rather than news; within reference, the precepts that actually move colonists come first.
        /// </summary>
        internal static List<DoctrineRow> Doctrine(Ideo ideo)
        {
            List<DoctrineRow> rows = new List<DoctrineRow>();

            if (ideo == null)
                return rows;

            List<Precept> precepts = ideo.PreceptsListForReading;

            for (int i = 0; precepts != null && i < precepts.Count; i++)
            {
                Precept precept = precepts[i];

                if (precept?.def == null || precept.def.issue == null || !precept.def.visible)
                    continue;

                if (precept is Precept_Role || precept is Precept_Ritual || precept is Precept_Building)
                    continue;

                rows.Add(new DoctrineRow
                {
                    precept = precept,
                    issue = precept.def.issue.LabelCap,
                    stance = precept.LabelCap,
                    impact = precept.def.impact
                });
            }

            rows.Sort((a, b) =>
            {
                int byImpact = ((int) b.impact).CompareTo((int) a.impact);

                return byImpact != 0 ? byImpact : string.CompareOrdinal(a.issue, b.issue);
            });

            return rows;
        }

        /// <summary>How many distinct issues the doctrine covers, for the block's heading.</summary>
        internal static int Issues(List<DoctrineRow> rows)
        {
            HashSet<string> seen = new HashSet<string>();

            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                if (rows[i].issue != null)
                    seen.Add(rows[i].issue);
            }

            return seen.Count;
        }
    }
}
