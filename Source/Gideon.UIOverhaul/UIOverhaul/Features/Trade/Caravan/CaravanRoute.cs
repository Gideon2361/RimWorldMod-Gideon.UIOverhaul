using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade.Caravan
{
    /// <summary>
    /// What one row is worth on <i>this</i> journey.
    ///
    /// <b>The whole idea is comparison rather than reporting.</b> Vanilla has a days-until-rot column and, in a
    /// different part of the same window, a travel-time readout -- and never puts the two together. So berries
    /// that rot in three days and a trip that takes six are two numbers on one screen that nobody adds up.
    /// Here the row says <i>spoils before arrival</i>, which is the sentence those two numbers were always for.
    ///
    /// <b>Every input is the game's own.</b> Rot comes from <c>CompRottable</c> at the temperature it is actually
    /// at; the trip length is the dialog's own <c>TicksToArrive</c>; a pawn's state is read off the pawn. Nothing
    /// is modelled here, only compared.
    ///
    /// <b>It is a hint and never a gate.</b> Nothing on a row is refused because of what this says, in the same
    /// way the trade window's need lines never gate a control -- a player taking berries they know will spoil is
    /// allowed to, and may well be feeding them to something on the way.
    /// </summary>
    internal struct CaravanVerdict
    {
        internal string Text;

        /// <summary>How much attention it wants: 0 fine, 1 worth knowing, 2 a real problem.</summary>
        internal int Severity;

        internal Color Tone(UIColorPaletteDef palette)
        {
            return Severity >= 2 ? palette.Danger : Severity == 1 ? palette.Warning : palette.TextDisabled;
        }
    }

    internal static class CaravanRoute
    {
        /// <summary>
        /// Judges one row against a journey of <paramref name="tripDays"/>.
        /// </summary>
        /// <param name="tripDays">
        /// Zero when no destination has been chosen, which is a real state rather than a missing one: before a
        /// route exists there is nothing to judge against, so the rot lines fall back to stating the fact rather
        /// than drawing a conclusion from a trip length of nothing.
        /// </param>
        internal static CaravanVerdict For(TransferableOneWay transferable, float tripDays)
        {
            return UIGuard.Try("Caravan.Verdict", () => Judge(transferable, tripDays),
                new CaravanVerdict(), null);
        }

        private static CaravanVerdict Judge(TransferableOneWay transferable, float tripDays)
        {
            CaravanVerdict verdict = new CaravanVerdict();

            if (transferable == null || !transferable.HasAnyThing)
                return verdict;

            Thing thing = transferable.AnyThing;

            Pawn pawn = thing as Pawn;

            if (pawn != null)
                return Person(pawn);

            CompRottable rot = thing.TryGetComp<CompRottable>();

            if (rot != null && rot.Active)
            {
                float days = rot.TicksUntilRotAtCurrentTemp / 60000f;

                if (tripDays > 0f && days < tripDays)
                {
                    verdict.Text = "Spoils before arrival";
                    verdict.Severity = 2;

                    return verdict;
                }

                verdict.Text = "Rots in " + days.ToString("0.#") + " days";
                verdict.Severity = tripDays > 0f && days < tripDays * 1.5f ? 1 : 0;

                return verdict;
            }

            if (thing.def != null && thing.def.IsNutritionGivingIngestible)
            {
                verdict.Text = "Keeps indefinitely";

                return verdict;
            }

            if (thing.def != null && thing.def.IsMedicine)
            {
                verdict.Text = "Medicine";

                return verdict;
            }

            return verdict;
        }

        /// <summary>
        /// Whether a person is fit to be taken.
        ///
        /// <b>Ordered by how badly it goes wrong.</b> A downed colonist has to be carried and a caravan cannot
        /// carry one, which is the hard stop; an untreated infection is the thing that kills somebody two days
        /// out; the rest is worth saying and not worth alarm.
        /// </summary>
        private static CaravanVerdict Person(Pawn pawn)
        {
            CaravanVerdict verdict = new CaravanVerdict();

            if (pawn.Dead)
            {
                verdict.Text = "Dead";
                verdict.Severity = 2;

                return verdict;
            }

            if (pawn.Downed)
            {
                verdict.Text = "Downed";
                verdict.Severity = 2;

                return verdict;
            }

            if (pawn.InMentalState)
            {
                verdict.Text = "In a mental break";
                verdict.Severity = 2;

                return verdict;
            }

            // HealthAIUtility's own question, which is what a doctor in the colony would be asked about this pawn.
            // Asking it rather than reading hediffs means an infection our list has never heard of still counts.
            if (HealthAIUtility.ShouldBeTendedNowByPlayer(pawn))
            {
                verdict.Text = "Needs treatment on the way";
                verdict.Severity = 1;

                return verdict;
            }

            float moving = pawn.health != null && pawn.health.capacities != null
                ? pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving)
                : 1f;

            if (moving < 0.75f)
            {
                verdict.Text = "Slow: " + moving.ToStringPercent() + " mobile";
                verdict.Severity = 1;

                return verdict;
            }

            if (pawn.IsPrisoner)
            {
                verdict.Text = "Prisoner";
                verdict.Severity = 1;

                return verdict;
            }

            verdict.Text = "Fit to travel";

            return verdict;
        }

        /// <summary>
        /// How heavy a row's chosen count is.
        ///
        /// <c>GetStatValue</c> on the stack's own thing rather than on its def, so quality, stuff and a pawn's
        /// gear are all counted the way the caravan's own mass calculator counts them.
        /// </summary>
        internal static float MassOf(TransferableOneWay transferable)
        {
            return UIGuard.Try("Caravan.RowMass", () =>
            {
                if (transferable == null || !transferable.HasAnyThing)
                    return 0f;

                Thing thing = transferable.AnyThing;

                int count = Mathf.Max(0, transferable.CountToTransfer);

                if (thing is Pawn)
                    return count * thing.GetStatValue(StatDefOf.Mass);

                return count * thing.GetStatValue(StatDefOf.Mass);
            }, 0f, null);
        }

        internal static float ValueOf(TransferableOneWay transferable)
        {
            return UIGuard.Try("Caravan.RowValue", () =>
            {
                if (transferable == null || !transferable.HasAnyThing)
                    return 0f;

                return Mathf.Max(0, transferable.CountToTransfer) * transferable.AnyThing.MarketValue;
            }, 0f, null);
        }
    }
}
