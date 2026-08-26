using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Trade.Comms
{
    /// <summary>
    /// One thing you could call, and everything a card needs to describe it.
    ///
    /// <b>Every field here comes off the interface every target already implements.</b> <c>ICommunicable</c>
    /// requires <c>GetCallLabel()</c>, <c>GetInfoText()</c>, <c>GetFaction()</c>, <c>TryOpenComms(Pawn)</c> and
    /// <c>CommFloatMenuOption(...)</c> of everybody -- a mod that adds a comms target has already written all
    /// five, because it would not compile otherwise. So a modded target draws exactly the same card as a vanilla
    /// one, and there is no fallback to build for the case where it does not.
    ///
    /// <b>A null faction is not a gap.</b> <c>PassingShip.GetFaction()</c> returns null because an orbital trader
    /// genuinely has no faction, so showing it no goodwill is correct rather than missing. The card leaves the
    /// crest and the number off, and says what the ship is instead.
    /// </summary>
    internal class CommsTarget
    {
        internal ICommunicable Target;

        /// <summary>The name, from the target's own <c>GetCallLabel</c>.</summary>
        internal string Label;

        /// <summary>What they are: a trader kind, a faction type.</summary>
        internal string Kind;

        /// <summary>The one line under the name: what is on offer, how long they stay, when you last called.</summary>
        internal string Detail;

        internal Faction Faction;

        internal bool HasGoodwill;

        internal int Goodwill;

        /// <summary>The verb on the button, which is the target's own word where it has one.</summary>
        internal string Verb = "Call";

        /// <summary>Null when the target can be called. Otherwise the reason, taken from vanilla's own option.</summary>
        internal string Refusal;

        /// <summary>The target's own action, run unchanged. Null when refused.</summary>
        internal System.Action Call;

        /// <summary>Which rail group this belongs to.</summary>
        internal string Group;

        internal bool Callable
        {
            get { return Call != null; }
        }
    }

    /// <summary>
    /// Everybody a comms console can reach, described.
    ///
    /// <b>Read through vanilla's own float menu option, not around it.</b> Each target's
    /// <c>CommFloatMenuOption</c> is what decides whether the call can be placed at all and what to do about it:
    /// a faction with no leader available returns an option with no action, an orbital trader checks for a
    /// powered beacon before it will connect, and a mod's target does whatever it does. Taking the action out of
    /// that option means every one of those rules still applies, unchanged, without this file knowing any of
    /// them. What we add is the card around it.
    ///
    /// <b>Refused targets stay in the list, dimmed, with the reason.</b> Vanilla replaces the entire menu with
    /// one disabled line when the console cannot be used, so during a solar flare a player cannot even see who
    /// they would have been able to call. The console-level failure is drawn once at the top of the window and
    /// the directory stays readable underneath it.
    /// </summary>
    internal static class CommsTargets
    {
        internal const string GroupTraders = "traders";
        internal const string GroupAllies = "allies";
        internal const string GroupNeutral = "neutral";
        internal const string GroupHostile = "hostile";

        /// <summary>
        /// Why the console itself cannot be used, or null when it can.
        ///
        /// <b>These are console conditions, not target conditions,</b> which is why they are answered once for
        /// the window rather than per card. Each is asked through public API -- <c>CanReach</c>,
        /// <c>ElectricityDisabled</c>, the power comp, the pawn's talking capacity -- and each returns RimWorld's
        /// own translated sentence, so a player sees the words the game would have shown them.
        /// </summary>
        internal static string ConsoleProblem(Building_CommsConsole console, Pawn negotiator)
        {
            return UIGuard.Try<string>("Comms.ConsoleProblem", () =>
            {
                if (console == null || negotiator == null)
                    return null;

                if (!negotiator.CanReach(console, PathEndMode.InteractionCell, Danger.Some))
                    return "CannotUseNoPath".Translate();

                if (console.Spawned && console.Map.gameConditionManager.ElectricityDisabled(console.Map))
                    return "CannotUseSolarFlare".Translate();

                CompPowerTrader power = console.TryGetComp<CompPowerTrader>();

                if (power != null && !power.PowerOn)
                    return "CannotUseNoPower".Translate();

                if (!negotiator.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                {
                    return "CannotUseReason".Translate("IncapableOfCapacity".Translate(
                        PawnCapacityDefOf.Talking.label, negotiator.Named("PAWN")));
                }

                return null;
            }, null, null);
        }

        /// <summary>
        /// Every target, described and sorted.
        ///
        /// <b>Sorted by goodwill, best first, with the traders above all of them.</b> Orbital traders leave, so
        /// they are the time-limited half of the list and belong where they are seen; factions do not, and among
        /// them the one most likely to help is the one you are looking for.
        /// </summary>
        internal static List<CommsTarget> All(Building_CommsConsole console, Pawn negotiator,
            List<CommsTarget> into)
        {
            into.Clear();

            if (console == null || negotiator == null)
                return into;

            UIGuard.Try("Comms.Targets", () =>
            {
                foreach (ICommunicable target in console.GetCommTargets(negotiator))
                {
                    CommsTarget described = Describe(target, console, negotiator);

                    if (described != null)
                        into.Add(described);
                }

                into.Sort(Compare);
            }, "The comms directory could not be built. RimWorld's own menu is unaffected.");

            return into;
        }

        private static int Compare(CommsTarget left, CommsTarget right)
        {
            int rank = Rank(right).CompareTo(Rank(left));

            if (rank != 0)
                return rank;

            int goodwill = right.Goodwill.CompareTo(left.Goodwill);

            if (goodwill != 0)
                return goodwill;

            return string.Compare(left.Label ?? string.Empty, right.Label ?? string.Empty,
                System.StringComparison.CurrentCultureIgnoreCase);
        }

        private static int Rank(CommsTarget target)
        {
            if (target.Group == GroupTraders)
                return 3;

            // Refused targets sink within their group rather than out of the list. They are worth seeing and
            // never worth seeing first, which is the same rule the trade table's refusals follow.
            if (!target.Callable)
                return -1;

            return target.Group == GroupAllies ? 2 : target.Group == GroupNeutral ? 1 : 0;
        }

        private static CommsTarget Describe(ICommunicable target, Building_CommsConsole console, Pawn negotiator)
        {
            return UIGuard.Try<CommsTarget>("Comms.Describe", () =>
            {
                if (target == null)
                    return null;

                // Vanilla's own option, which is where the action and every refusal come from. A null option is
                // a target that has declined to appear at all -- the player's own faction does this -- and is
                // dropped rather than drawn as an empty card.
                FloatMenuOption option = target.CommFloatMenuOption(console, negotiator);

                if (option == null)
                    return null;

                CommsTarget described = new CommsTarget
                {
                    Target = target,
                    Label = target.GetCallLabel(),
                    Faction = target.GetFaction(),
                    Call = option.action,
                    Group = GroupNeutral
                };

                if (described.Label.NullOrEmpty())
                    described.Label = option.Label;

                if (option.action == null)
                {
                    // The option's own label carries the reason in a parenthesis -- "(their leader is
                    // unavailable)" -- because a float menu has nowhere else to put one. It is the sentence
                    // vanilla would have shown, so it is the sentence to show.
                    described.Refusal = Reason(option);
                }

                Detail(described, target);

                return described;
            }, null, null);
        }

        /// <summary>
        /// The refusal sentence, preferring the option's tooltip over its label.
        ///
        /// A label reads "Call Cortlyn's Rest (ally, +82) (their leader is unavailable)", which is the name and
        /// the reason run together because a menu row is one string. The tooltip, where there is one, is the
        /// reason on its own.
        /// </summary>
        private static string Reason(FloatMenuOption option)
        {
            if (option.tooltip.HasValue && !option.tooltip.Value.text.NullOrEmpty())
                return option.tooltip.Value.text;

            string label = option.Label ?? string.Empty;

            int open = label.LastIndexOf('(');

            return open > 0 && label.EndsWith(")")
                ? label.Substring(open + 1, label.Length - open - 2)
                : "Cannot call right now";
        }

        /// <summary>
        /// Fills in what kind of thing this is and the line under its name.
        ///
        /// <b>The trader branch is the only place a concrete type is named,</b> and it earns it: an orbital
        /// trader has a departure clock and a stock of silver, neither of which <c>ICommunicable</c> can express,
        /// and both of which decide whether to call now or later. Everything else falls back to
        /// <c>GetInfoText</c>, which every target supplies -- including a modded one, which is why no fallback
        /// card is needed.
        /// </summary>
        private static void Detail(CommsTarget described, ICommunicable target)
        {
            TradeShip ship = target as TradeShip;

            if (ship != null)
            {
                described.Group = GroupTraders;
                described.Kind = ship.TraderKind != null ? ship.TraderKind.LabelCap.ToString() : "Trader";
                described.Verb = "Hail";

                string leaves = ship.ticksUntilDeparture.ToStringTicksToPeriod();

                described.Detail = "leaves in " + leaves + " · " + ship.Silver.ToStringCached() + " silver";

                return;
            }

            Faction faction = described.Faction;

            if (faction != null)
            {
                described.HasGoodwill = true;
                described.Goodwill = faction.PlayerGoodwill;
                described.Kind = faction.def != null ? faction.def.LabelCap.ToString() : "Faction";

                described.Group = faction.PlayerRelationKind == FactionRelationKind.Ally
                    ? GroupAllies
                    : faction.HostileTo(Faction.OfPlayer)
                        ? GroupHostile
                        : GroupNeutral;

                described.Detail = faction.PlayerRelationKind.GetLabelCap();

                if (faction.leader != null)
                    described.Detail += " · " + faction.leader.LabelShortCap;

                return;
            }

            // Everything else, including anything a mod has added: its own info text, flattened to one line.
            // GetInfoText is allowed to be multi-line and a card gives it one, so the newlines become separators
            // rather than being cut off with whatever came after them.
            PassingShip passing = target as PassingShip;

            if (passing != null)
            {
                described.Group = GroupTraders;
                described.Verb = "Hail";
            }

            string info = target.GetInfoText();

            described.Detail = info.NullOrEmpty()
                ? null
                : info.Replace("\n", " · ").Replace("\r", string.Empty);
        }

        /// <summary>The colour a card's crest and goodwill are drawn in.</summary>
        internal static Color ToneFor(CommsTarget target, UIFramework.Defs.UIColorPaletteDef palette)
        {
            if (!target.HasGoodwill)
                return palette.Info;

            if (target.Group == GroupHostile)
                return palette.Danger;

            return target.Group == GroupAllies ? palette.Success : palette.TextSecondary;
        }
    }
}
