using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Factions
{
    /// <summary>Which group of the list a faction belongs in, which is also what the rail filters on.</summary>
    internal enum FactionGroup
    {
        Allied,

        Neutral,

        Hostile,

        /// <summary>Beaten: no settlements left anywhere on the planet.</summary>
        Beaten
    }

    /// <summary>
    /// One line of a faction's goodwill ledger: a thing that is holding the number down, a thing that moved
    /// it, or a term of the resting value.
    /// </summary>
    internal struct GoodwillEntry
    {
        internal string label;

        /// <summary>How many times it happened. Zero or one draws no multiplier.</summary>
        internal int count;

        /// <summary>The goodwill it accounts for, signed.</summary>
        internal int amount;

        /// <summary>Whether <see cref="amount"/> is a ceiling rather than a change.</summary>
        internal bool ceiling;
    }

    /// <summary>
    /// One faction, read the way the tab draws it.
    ///
    /// <b>Everything here comes off <c>Faction</c>, <c>GoodwillSituationManager</c> and the world object list,
    /// which the game maintains whether or not anything is looking.</b> Nothing on this tab is stored by us.
    /// </summary>
    internal struct FactionRow
    {
        internal Faction faction;

        internal string name;

        /// <summary>The faction's type, which is the second line: "outlander union", "savage tribe".</summary>
        internal string kind;

        /// <summary>"Chairwoman: Marya Vance", or null when they have no leader.</summary>
        internal string leader;

        internal Color color;

        internal Texture2D icon;

        internal FactionRelationKind relation;

        internal FactionGroup group;

        /// <summary>Whether this faction has a standing that can move at all.</summary>
        internal bool hasGoodwill;

        /// <summary>Standing as the game reports it, which is already clipped by <see cref="ceiling"/>.</summary>
        internal int goodwill;

        /// <summary>
        /// Standing before the ceiling is applied, which is the value the drift is measured against.
        ///
        /// Worth carrying separately because the two disagree exactly when something is holding the faction
        /// down, and that disagreement is a fact the player has no way of seeing today.
        /// </summary>
        internal int stored;

        /// <summary>The value the standing rests around.</summary>
        internal int natural;

        /// <summary>The highest standing currently reachable. 100 when nothing is capping it.</summary>
        internal int ceiling;

        /// <summary>Bottom and top of the band the standing is left alone inside.</summary>
        internal int restingLow;

        internal int restingHigh;

        /// <summary>Whether the standing is outside its resting band and so is being pulled back.</summary>
        internal bool drifting;

        /// <summary>Which way it is being pulled: positive up, negative down, zero not at all.</summary>
        internal int driftDirection;

        internal bool defeated;

        /// <summary>Whether nothing can ever make them non-hostile.</summary>
        internal bool permanentEnemy;

        /// <summary>Their primary ideoligion, or null without Ideology or in classic mode.</summary>
        internal Ideo ideo;

        /// <summary>Other visible factions at war with this one. The colony is not among them.</summary>
        internal List<Faction> enemies;

        internal int settlements;
    }

    /// <summary>
    /// The read side of the factions tab.
    ///
    /// <b>The tab's whole subject is one number that is drawn without its context.</b> Vanilla puts the
    /// current standing and the natural standing side by side as two unlabeled signed figures, the second on
    /// a filled rectangle, and everything that explains either of them is built on hover and thrown away.
    ///
    /// <b>Natural goodwill is a band, not a target, and that is the correction this file exists to make.</b>
    /// <c>Faction.CheckReachNaturalGoodwill</c> leaves the standing entirely alone while it is inside
    /// <c>[natural - 50, natural + 50]</c>, and only outside that does it pull it back, by at most ten and
    /// only once every fifty days. So a faction at +88 with a natural of +65 is not sliding anywhere: it is
    /// resting. Reading the pair as "now, and where it is heading" is the mistake the numbers invite, and it
    /// is the mistake this tab is built to stop.
    ///
    /// <b>Everything is guarded.</b> A faction carries whatever a mod put on its def, and the situation and
    /// history managers are both reachable before a world is fully set up.
    /// </summary>
    internal static class FactionsFacts
    {
        /// <summary>
        /// Half the width of the band a standing rests inside, which is vanilla's own constant.
        ///
        /// Hardcoded there rather than exposed, so it is repeated here. If it ever moves, the band this tab
        /// draws is the thing that goes wrong, which is why it is named rather than written as a fifty.
        /// </summary>
        internal const int RestingHalfWidth = 50;

        /// <summary>How often the standing is pulled back toward the band, in ticks.</summary>
        internal const int DriftInterval = 3000000;

        /// <summary>The most it moves when it is pulled.</summary>
        internal const int DriftStep = 10;

        /// <summary>How far back the ledger of what moved a standing reaches.</summary>
        private const int LedgerWindow = GenDate.TicksPerYear;

        private static readonly List<int> Ticks = new List<int>();

        private static readonly List<int> Amounts = new List<int>();

        /// <summary>
        /// Every faction worth a row, in the game's own view order.
        ///
        /// <b>The player and hidden factions are dropped, which is vanilla's filter.</b> A hidden faction has
        /// relations the player is not supposed to be reading yet, and the colony has no standing with itself.
        /// </summary>
        internal static void All(List<FactionRow> into)
        {
            if (into == null)
                return;

            into.Clear();

            UIGuard.Try("Factions.All", () =>
            {
                FactionManager manager = Find.FactionManager;

                if (manager == null)
                    return;

                foreach (Faction faction in manager.AllFactionsInViewOrder)
                {
                    if (faction == null || faction.def == null || faction.IsPlayer || faction.Hidden)
                        continue;

                    into.Add(Read(faction));
                }
            }, "The factions tab could not read the faction list this frame.");
        }

        /// <summary>One faction, read into a row.</summary>
        private static FactionRow Read(Faction faction)
        {
            FactionRow row = new FactionRow();

            row.faction = faction;
            row.name = faction.Name.NullOrEmpty() ? faction.def.LabelCap.Resolve() : faction.Name.CapitalizeFirst();
            row.kind = faction.def.LabelCap.Resolve();
            row.color = faction.Color;
            row.defeated = faction.defeated;
            row.permanentEnemy = faction.def.permanentEnemy;
            row.relation = faction.PlayerRelationKind;

            row.leader = faction.leader != null
                ? faction.LeaderTitle.CapitalizeFirst() + ": " + faction.leader.Name.ToStringShort
                : null;

            UIGuard.Try("Factions.Icon", () => row.icon = faction.def.FactionIcon, null);

            row.hasGoodwill = faction.HasGoodwill && !faction.def.permanentEnemy && !faction.defeated;

            if (row.hasGoodwill)
            {
                row.goodwill = faction.PlayerGoodwill;
                row.stored = faction.BaseGoodwillWith(Faction.OfPlayer);
                row.natural = faction.NaturalGoodwill;
                row.ceiling = Ceiling(faction);

                row.restingLow = Mathf.Clamp(row.natural - RestingHalfWidth, -100, 100);
                row.restingHigh = Mathf.Clamp(row.natural + RestingHalfWidth, -100, 100);

                // Measured on the stored value rather than the reported one, because that is what the game
                // measures. A faction held at +40 by a quarrel whose stored standing is +58 is inside its band
                // and is not being pulled anywhere, and drawing the drift off the clipped figure would claim
                // otherwise.
                if (row.stored < row.restingLow)
                    row.driftDirection = 1;
                else if (row.stored > row.restingHigh)
                    row.driftDirection = -1;

                row.drifting = row.driftDirection != 0;
            }
            else
            {
                row.ceiling = 100;
            }

            row.group = faction.defeated
                ? FactionGroup.Beaten
                : faction.PlayerRelationKind == FactionRelationKind.Ally
                    ? FactionGroup.Allied
                    : faction.PlayerRelationKind == FactionRelationKind.Hostile
                        ? FactionGroup.Hostile
                        : FactionGroup.Neutral;

            row.ideo = Ideoligion(faction);
            row.enemies = Enemies(faction);
            row.settlements = SettlementCount(faction);

            return row;
        }

        /// <summary>
        /// The highest standing this faction can reach right now.
        ///
        /// A hundred when nothing is holding them down, which is what the manager returns for a faction with
        /// no capping situation.
        /// </summary>
        private static int Ceiling(Faction faction)
        {
            return UIGuard.Try("Factions.Ceiling", () =>
            {
                GoodwillSituationManager manager = Find.GoodwillSituationManager;

                return manager == null ? 100 : manager.GetMaxGoodwill(faction);
            }, 100, null);
        }

        /// <summary>
        /// Their primary ideoligion, or null when there is nothing to show.
        ///
        /// <b>Gated on the same test vanilla makes before it reserves the column at all:</b> Ideology active
        /// and classic mode off. In classic mode every faction shares one ideoligion, so naming it on each row
        /// would be twelve rows saying the same thing.
        /// </summary>
        private static Ideo Ideoligion(Faction faction)
        {
            return UIGuard.Try<Ideo>("Factions.Ideo", () =>
            {
                if (!ModsConfig.IdeologyActive || Find.IdeoManager == null || Find.IdeoManager.classicMode)
                    return null;

                return faction.ideos?.PrimaryIdeo;
            }, null, null);
        }

        /// <summary>
        /// The other visible factions at war with this one.
        ///
        /// <b>The colony is deliberately not among them,</b> which is vanilla's rule and the right one: the
        /// standing column already says where you stand, so listing yourself here would be the same fact twice
        /// on one row.
        /// </summary>
        private static List<Faction> Enemies(Faction faction)
        {
            return UIGuard.Try("Factions.Enemies", () =>
            {
                List<Faction> found = null;

                FactionManager manager = Find.FactionManager;

                if (manager == null)
                    return null;

                foreach (Faction other in manager.AllFactionsInViewOrder)
                {
                    if (other == null || other == faction || other.IsPlayer || other.Hidden)
                        continue;

                    if (!other.HostileTo(faction))
                        continue;

                    found = found ?? new List<Faction>();
                    found.Add(other);
                }

                return found;
            }, null, null);
        }

        /// <summary>How many settlements they still hold. Zero is what "beaten" means.</summary>
        private static int SettlementCount(Faction faction)
        {
            return UIGuard.Try("Factions.Settlements", () =>
            {
                WorldObjectsHolder objects = Find.WorldObjects;

                if (objects == null)
                    return 0;

                List<Settlement> settlements = objects.Settlements;
                int count = 0;

                for (int i = 0; i < settlements.Count; i++)
                {
                    if (settlements[i] != null && settlements[i].Faction == faction)
                        count++;
                }

                return count;
            }, 0, null);
        }

        /// <summary>Their nearest settlement, for the row that offers to show them on the map.</summary>
        internal static Settlement AnySettlement(Faction faction)
        {
            return UIGuard.Try<Settlement>("Factions.AnySettlement", () =>
            {
                WorldObjectsHolder objects = Find.WorldObjects;

                if (objects == null || faction == null)
                    return null;

                List<Settlement> settlements = objects.Settlements;

                for (int i = 0; i < settlements.Count; i++)
                {
                    if (settlements[i] != null && settlements[i].Faction == faction)
                        return settlements[i];
                }

                return null;
            }, null, null);
        }

        // -------------------------------------------------------------------------------------------
        // The three ledgers
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// What is holding this faction's standing below a hundred.
        ///
        /// <b>Vanilla builds exactly this and puts it in a tooltip.</b> It is the answer to the only question
        /// a capped faction provokes, which is why the number stopped climbing, and a hover is a poor place
        /// for an answer the player has to go looking for.
        /// </summary>
        internal static void Ceilings(Faction faction, List<GoodwillEntry> into)
        {
            if (into == null)
                return;

            into.Clear();

            UIGuard.Try("Factions.Ceilings", () =>
            {
                List<GoodwillSituationManager.CachedSituation> situations = Situations(faction);

                if (situations == null)
                    return;

                for (int i = 0; i < situations.Count; i++)
                {
                    if (situations[i].maxGoodwill >= 100)
                        continue;

                    into.Add(new GoodwillEntry
                    {
                        label = Label(situations[i].def, faction),
                        amount = situations[i].maxGoodwill,
                        ceiling = true
                    });
                }
            }, null);
        }

        /// <summary>
        /// What the resting value is made of.
        ///
        /// The terms sum to <see cref="FactionRow.natural"/>, which is the check worth having: a breakdown
        /// that does not add up to the figure it explains is worse than no breakdown.
        /// </summary>
        internal static void Resting(Faction faction, List<GoodwillEntry> into)
        {
            if (into == null)
                return;

            into.Clear();

            UIGuard.Try("Factions.Resting", () =>
            {
                List<GoodwillSituationManager.CachedSituation> situations = Situations(faction);

                if (situations == null)
                    return;

                for (int i = 0; i < situations.Count; i++)
                {
                    if (situations[i].naturalGoodwillOffset == 0)
                        continue;

                    into.Add(new GoodwillEntry
                    {
                        label = Label(situations[i].def, faction),
                        amount = situations[i].naturalGoodwillOffset
                    });
                }
            }, null);
        }

        /// <summary>
        /// What has actually moved the standing in the last year, largest first.
        ///
        /// <b>Only events that carried goodwill.</b> The history manager records a great deal that has no
        /// bearing on a faction's opinion, and a ledger listing those is a ledger nobody reads twice.
        /// </summary>
        internal static void Moved(Faction faction, List<GoodwillEntry> into)
        {
            if (into == null)
                return;

            into.Clear();

            UIGuard.Try("Factions.Moved", () =>
            {
                HistoryEventsManager history = Find.HistoryEventsManager;

                if (history == null || faction == null)
                    return;

                List<HistoryEventDef> defs = DefDatabase<HistoryEventDef>.AllDefsListForReading;

                for (int i = 0; i < defs.Count; i++)
                {
                    int times = history.GetRecentCountWithinTicks(defs[i], LedgerWindow, faction);

                    if (times <= 0)
                        continue;

                    history.GetRecent(defs[i], LedgerWindow, Ticks, Amounts, faction);

                    int total = 0;

                    for (int j = 0; j < Amounts.Count; j++)
                        total += Amounts[j];

                    if (total == 0)
                        continue;

                    into.Add(new GoodwillEntry
                    {
                        label = defs[i].LabelCap,
                        count = times,
                        amount = total
                    });
                }

                into.Sort((a, b) => Mathf.Abs(b.amount).CompareTo(Mathf.Abs(a.amount)));
            }, null);
        }

        /// <summary>
        /// The situations cached for a faction, or null when there is nothing to ask.
        ///
        /// <b>The player faction is checked for here rather than at every call site,</b> because the manager
        /// logs an error rather than returning null for it and this tab has no reason to ever ask.
        /// </summary>
        private static List<GoodwillSituationManager.CachedSituation> Situations(Faction faction)
        {
            GoodwillSituationManager manager = Find.GoodwillSituationManager;

            if (manager == null || faction == null || faction.IsPlayer)
                return null;

            return manager.GetSituations(faction);
        }

        /// <summary>
        /// A situation's name, put through its own worker the way vanilla's tooltips do.
        ///
        /// The worker is what turns "allied to an enemy" into the sentence naming which enemy, so taking the
        /// def's plain label instead would lose the only part of the line that identifies anything.
        /// </summary>
        private static string Label(GoodwillSituationDef def, Faction faction)
        {
            return UIGuard.Try("Factions.SituationLabel",
                () => def?.Worker == null ? def?.LabelCap.Resolve() : def.Worker.GetPostProcessedLabelCap(faction),
                def?.defName, null);
        }

        // -------------------------------------------------------------------------------------------
        // Formatting
        // -------------------------------------------------------------------------------------------

        /// <summary>A goodwill figure, always carrying its sign, which is how the game writes them.</summary>
        internal static string Signed(int value)
        {
            return value.ToStringWithSign();
        }

        /// <summary>
        /// Where a standing sits on a scale running from a hundred against to a hundred for.
        ///
        /// Returned as a fraction of the track rather than as a pixel, so the same arithmetic serves the
        /// row's scale and the card's larger one.
        /// </summary>
        internal static float Fraction(int goodwill)
        {
            return Mathf.Clamp01((goodwill + 100f) / 200f);
        }

        /// <summary>
        /// The one line under a faction's standing: what it is doing, in words.
        ///
        /// <b>"Resting" is the common answer and it is worth saying out loud.</b> The pairing of a current
        /// and a natural figure reads as a journey, and for most factions most of the time there is no
        /// journey: the number is where the game is content to leave it.
        /// </summary>
        internal static string Movement(FactionRow row)
        {
            if (!row.hasGoodwill)
                return null;

            if (row.drifting)
            {
                int edge = row.driftDirection > 0 ? row.restingLow : row.restingHigh;

                return (row.driftDirection > 0 ? "rising to " : "falling to ") + Signed(edge);
            }

            if (row.ceiling < 100 && row.stored > row.goodwill)
                return "held down";

            return "resting";
        }
    }
}
