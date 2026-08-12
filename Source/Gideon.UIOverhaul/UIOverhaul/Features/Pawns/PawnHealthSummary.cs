using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The one-line answer to "how is this colonist doing", in severity order.
    ///
    /// Ordered rather than combined, and the order is the whole design. A bleeding, downed, freezing pawn is
    /// three problems, but a column that says all three says nothing at a glance -- and only one of them is
    /// what you would act on first. So the most urgent wins the line, and the tooltip carries the rest.
    ///
    /// The order is by how soon it kills: bleeding out has a clock on it, vacuum burns through a pawn in
    /// seconds, being downed is dangerous but stable, needing treatment is a job to queue, temperature is a
    /// warning, and healthy is everything else.
    /// </summary>
    internal enum PawnHealthState
    {
        Healthy,
        Temperature,

        /// <summary>Something is tendable, and nothing about it is racing a clock. Amber.</summary>
        NeedsTending,

        /// <summary>An infection is present. Red: this one gets worse on its own if left.</summary>
        UrgentTending,

        Downed,
        Vacuum,

        /// <summary>The game says this pawn dies of blood loss soon. Red, and stated as an emergency.</summary>
        BleedingOut
    }

    /// <summary>
    /// Reads a pawn's condition into something a cell can draw: a state, a short label, and a color.
    ///
    /// Every reading here is a live property, so nothing needs invalidating. All of it is cheap except
    /// <c>TicksUntilDeathDueToBloodLoss</c>, which is only asked for once bleeding is already established.
    /// </summary>
    internal readonly struct PawnHealthSummary
    {
        public readonly PawnHealthState State;

        /// <summary>What the cell shows. Carries the countdown when there is one.</summary>
        public readonly string Label;

        /// <summary>Everything true about the pawn, not only the winning line.</summary>
        public readonly string Detail;

        private PawnHealthSummary(PawnHealthState state, string label, string detail)
        {
            State = state;
            Label = label;
            Detail = detail;
        }

        public static PawnHealthSummary For(Pawn pawn)
        {
            bool downed = pawn.Downed;
            bool needsTending = pawn.health?.hediffSet?.HasTendableHediff(false) ?? false;
            float bleedRate = pawn.health?.hediffSet?.BleedRateTotal ?? 0f;
            bool infected = HasInfection(pawn);

            // HarmedByVacuum is the exact question -- exposed *and* unprotected -- rather than something to
            // assemble from cell vacuum plus apparel plus a resistance stat. ConcernedByVacuum is the weaker
            // "would care about it", which is not the same thing and is not what this column is for.
            bool vacuum = pawn.HarmedByVacuum;

            TemperatureTrouble temperature = ReadTemperature(pawn);

            string detail = BuildDetail(pawn, downed, needsTending, bleedRate, infected, vacuum, temperature);

            // Tier three: the game itself says this pawn dies of blood loss soon. Asked only once bleeding is
            // established, because the call walks hediffs; and gated on a day, because a scratch bleeds too and
            // a column that cries emergency over a scratch is one players learn to ignore.
            if (bleedRate > 0.0001f)
            {
                int ticks = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);

                if (ticks < GenDate.TicksPerDay)
                {
                    return new PawnHealthSummary(PawnHealthState.BleedingOut,
                        "Emergency: bleeding out, " + ticks.ToStringTicksToPeriod(true, false, true, true),
                        detail);
                }
            }

            if (vacuum)
                return new PawnHealthSummary(PawnHealthState.Vacuum, "In vacuum, unprotected", detail);

            if (downed)
                return new PawnHealthSummary(PawnHealthState.Downed, "Downed", detail);

            // Tier two: an infection. Above plain tending because this is the one that gets worse while you
            // decide -- an untended cut waits, an infection races the pawn's immunity.
            if (infected)
                return new PawnHealthSummary(PawnHealthState.UrgentTending, "Urgent tending needed", detail);

            // Tier one: something is tendable and nothing about it is on a clock. Bleeding that is not fatal
            // within a day lands here too -- it is a wound to tend, not an emergency, and saying so twice in
            // two different colors would be worse than saying it once.
            if (needsTending || bleedRate > 0.0001f)
                return new PawnHealthSummary(PawnHealthState.NeedsTending, "Needs tending", detail);

            if (temperature != TemperatureTrouble.None)
            {
                return new PawnHealthSummary(PawnHealthState.Temperature,
                    temperature == TemperatureTrouble.Cold ? "Freezing" : "Overheating", detail);
            }

            return new PawnHealthSummary(PawnHealthState.Healthy, "Healthy", detail);
        }

        /// <summary>
        /// The color for the winning state, from the palette's meaning roles rather than from literals, so a
        /// theme can restate what "danger" looks like and this follows.
        /// </summary>
        public Color Color(UIColorPaletteDef palette)
        {
            switch (State)
            {
                case PawnHealthState.BleedingOut:
                case PawnHealthState.Vacuum:
                case PawnHealthState.Downed:
                case PawnHealthState.UrgentTending:
                    return palette.Danger;

                case PawnHealthState.NeedsTending:
                case PawnHealthState.Temperature:
                    return palette.Warning;

                default:
                    return palette.Success;
            }
        }

        /// <summary>
        /// Whether the pawn has an infection.
        ///
        /// Keyed on <c>HediffDef.isInfection</c>, which the game sets on the defs that are infections. Two
        /// things recommend it over naming WoundInfection: that def is not in <c>HediffDefOf</c>, so reaching
        /// it would mean a database lookup by string; and a mod's own infection sets the same flag, so this
        /// covers those without knowing about them.
        ///
        /// The list is walked directly rather than through a helper because HediffSet has none for this. It is
        /// short -- a pawn's hediffs number in the tens at worst -- and this is read once per row per frame.
        /// </summary>
        private static bool HasInfection(Pawn pawn)
        {
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;

            if (hediffs == null)
                return false;

            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i]?.def != null && hediffs[i].def.isInfection)
                    return true;
            }

            return false;
        }

        private enum TemperatureTrouble
        {
            None,
            Cold,
            Hot
        }

        /// <summary>
        /// Whether the pawn is actually in temperature trouble.
        ///
        /// Read from the hediffs rather than by comparing ambient temperature against the comfortable range.
        /// The comparison answers "is this uncomfortable", which is true for a pawn walking briskly across a
        /// cold map and in no danger at all; the hediff answers "is this doing damage", which is what a status
        /// column should be reporting. Hypothermia and Heatstroke are the two the game itself gives out.
        /// </summary>
        private static TemperatureTrouble ReadTemperature(Pawn pawn)
        {
            HediffSet set = pawn.health?.hediffSet;
            if (set == null)
                return TemperatureTrouble.None;

            if (set.HasHediff(HediffDefOf.Hypothermia))
                return TemperatureTrouble.Cold;

            if (set.HasHediff(HediffDefOf.Heatstroke))
                return TemperatureTrouble.Hot;

            return TemperatureTrouble.None;
        }

        /// <summary>
        /// Everything true at once, for the tooltip: the line in the cell is only the most urgent of these.
        /// </summary>
        private static string BuildDetail(Pawn pawn, bool downed, bool needsTending, float bleedRate,
            bool infected, bool vacuum, TemperatureTrouble temperature)
        {
            string detail = "Health: "
                            + (pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f).ToStringPercent();

            if (bleedRate > 0.0001f)
                detail += "\nBleeding";

            if (infected)
                detail += "\nInfected";

            if (vacuum)
                detail += "\nExposed to vacuum with no protection";

            if (downed)
                detail += "\nDowned";

            if (needsTending)
                detail += "\nHas untended injuries or conditions";

            if (temperature == TemperatureTrouble.Cold)
                detail += "\nHypothermia";
            else if (temperature == TemperatureTrouble.Hot)
                detail += "\nHeatstroke";

            return detail;
        }
    }
}
