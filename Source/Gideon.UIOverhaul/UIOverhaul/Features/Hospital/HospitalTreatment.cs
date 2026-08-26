using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>How a treatment line should read: the four weights the column uses.</summary>
    internal enum HospitalTone
    {
        /// <summary>Nothing is happening and nothing needs to.</summary>
        Idle,

        /// <summary>Under way and going well.</summary>
        Good,

        /// <summary>Under way, but something is holding it up or running out.</summary>
        Waiting,

        /// <summary>Somebody has to move now.</summary>
        Urgent
    }

    /// <summary>
    /// What is being done about a patient, and what is holding it up.
    ///
    /// <b>This is the column that earns the tab.</b> The condition column already says what is wrong, and every
    /// other screen in the game stops there. This one says how it is going: an immunity race with the crossing
    /// point named, a tend that expires in nine hours, an operation waiting on a surgeon of skill six when your
    /// best is an eleven who is asleep. That is the difference between a list of the sick and a list of decisions.
    ///
    /// <b>One line and one note, chosen by urgency rather than by category.</b> A patient can be several of these
    /// at once -- bleeding, infected, and booked for surgery -- and the row has room for the worst of them. The
    /// order the tests run in is therefore the whole design, and it runs from "somebody has to stop what they are
    /// doing" down to "they are asleep and getting better".
    /// </summary>
    internal struct HospitalTreatment
    {
        internal string Label;

        /// <summary>The second line, which is nearly always where the useful half is.</summary>
        internal string Note;

        internal HospitalTone Tone;

        /// <summary>
        /// Whether something is actively being fought, which is what puts a patient in the treatment section.
        ///
        /// <b>Separate from the tone on purpose.</b> A tend running out is good news with a deadline, so it is not
        /// urgent and is certainly not idle -- but it is a reason to be in the treatment list rather than in
        /// recovering, and no combination of tones says that on its own.
        /// </summary>
        internal bool Active;

        internal Color Color(UIColorPaletteDef palette)
        {
            switch (Tone)
            {
                case HospitalTone.Urgent:
                    return palette.Danger;

                case HospitalTone.Waiting:
                    return palette.Warning;

                case HospitalTone.Good:
                    return palette.Success;

                default:
                    return palette.TextDisabled;
            }
        }

        /// <summary>
        /// Reads a patient into the one line worth showing.
        ///
        /// Guarded as a whole rather than test by test: this walks hediff comps, the bill stack and the colony's
        /// doctors, and a failure anywhere in that should cost the column rather than the row.
        /// </summary>
        internal static HospitalTreatment For(HospitalPatient patient)
        {
            HospitalTreatment fallback = new HospitalTreatment
            {
                Label = "Unreadable", Tone = HospitalTone.Idle
            };

            return UIGuard.Try("Hospital.Treatment", () => Read(patient), fallback,
                "The hospital tab cannot say what is being done for one patient. Everything else on the row is "
                + "unaffected.");
        }

        private static HospitalTreatment Read(HospitalPatient patient)
        {
            Pawn pawn = patient.Pawn;

            if (pawn == null)
                return new HospitalTreatment { Label = "-", Tone = HospitalTone.Idle };

            // Somebody has to stop what they are doing. Nothing below this matters until they have.
            if (patient.Bleeding > 0f)
            {
                int ticks = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);

                if (ticks < GenDate.TicksPerDay)
                    return Urgent("Bleeding out",
                        "Dies in " + ticks.ToStringTicksToPeriod(false, false, false) + " without tending.");
            }

            if (pawn.Downed && !RestUtility.InBed(pawn))
                return Urgent("Needs rescuing", "Nobody has picked them up yet.");

            if (pawn.InMentalState)
                return new HospitalTreatment
                {
                    Label = "Not a medical problem",
                    Note = "In a mental state. A doctor is no use to this one.",
                    Tone = HospitalTone.Urgent,
                    Active = false
                };

            // Something is being fought.
            HospitalTreatment race = Race(pawn);

            if (race.Label != null)
                return race;

            int untended = Untended(pawn);

            if (untended > 0)
                return new HospitalTreatment
                {
                    Label = "Waiting for tending",
                    Note = untended == 1 ? "One wound is untended." : untended + " wounds are untended.",
                    Tone = HospitalTone.Waiting,
                    Active = true
                };

            HospitalTreatment tend = Tended(pawn);

            if (tend.Label != null)
                return tend;

            // Nothing is wrong that a doctor is treating; something is queued.
            if (patient.Operations > 0)
                return Surgery(patient);

            if (patient.Doses > 0)
                return new HospitalTreatment
                {
                    Label = patient.Doses == 1 ? "A dose is due" : patient.Doses + " doses are due",
                    Note = "Waiting for whoever is on doctoring.",
                    Tone = HospitalTone.Waiting,
                    Active = false
                };

            // Nothing queued and nothing being fought: they are getting better or they are fine.
            if (patient.Health < 0.999f)
                return Healing(patient);

            if (patient.InMedicalBed)
                return new HospitalTreatment
                {
                    Label = "In a medical bed",
                    Note = "Nothing left to treat. The bed is free when they leave it.",
                    Tone = HospitalTone.Good
                };

            return new HospitalTreatment { Label = "-", Tone = HospitalTone.Idle };
        }

        private static HospitalTreatment Urgent(string label, string note)
        {
            return new HospitalTreatment
            {
                Label = label, Note = note, Tone = HospitalTone.Urgent, Active = true
            };
        }

        /// <summary>
        /// The immunity race, which is the one health fact on this screen with two numbers and a winner.
        ///
        /// <b>Both numbers and the rate, because one of them alone means nothing.</b> Sixty one percent immunity
        /// is excellent against a severity of forty four and a death sentence against a severity of ninety, and
        /// the rate is what says whether the gap is opening or closing. The worst race wins the line when a pawn
        /// has caught two things at once.
        /// </summary>
        private static HospitalTreatment Race(Pawn pawn)
        {
            if (pawn.health == null || pawn.health.hediffSet == null)
                return default(HospitalTreatment);

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;

            Hediff worst = null;
            HediffComp_Immunizable worstComp = null;
            float worstGap = float.MaxValue;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];

                if (hediff == null || !hediff.Visible)
                    continue;

                HediffComp_Immunizable comp = hediff.TryGetComp<HediffComp_Immunizable>();

                if (comp == null || comp.FullyImmune)
                    continue;

                float gap = comp.Immunity - hediff.Severity;

                if (worst != null && gap >= worstGap)
                    continue;

                worst = hediff;
                worstComp = comp;
                worstGap = gap;
            }

            if (worst == null)
                return default(HospitalTreatment);

            float immunity = worstComp.Immunity;
            float severity = worst.Severity;
            bool winning = immunity > severity;

            string note = "Immunity " + Percent(immunity) + " against severity " + Percent(severity) + ".";

            float perDay = ImmunityPerDay(pawn, worst);

            if (perDay > 0f)
                note += " Gaining " + Percent(perDay) + " a day.";

            return new HospitalTreatment
            {
                Label = (winning ? "Winning " : "Losing ") + Percent(immunity) + " / " + Percent(severity),
                Note = note,
                Tone = winning ? HospitalTone.Good : HospitalTone.Urgent,
                Active = true
            };
        }

        /// <summary>
        /// How fast immunity is climbing, from the game's own per-tick figure.
        ///
        /// Asked of the record rather than worked out from bed rest and food, because the record is what the tick
        /// loop actually reads: reproducing the calculation would mean a number that drifts from the bar beside it
        /// the first time Ludeon changes a factor.
        /// </summary>
        private static float ImmunityPerDay(Pawn pawn, Hediff hediff)
        {
            return UIGuard.Try("Hospital.ImmunityRate", () =>
            {
                if (pawn.health == null || pawn.health.immunity == null)
                    return 0f;

                ImmunityRecord record = pawn.health.immunity.GetImmunityRecord(hediff.def);

                if (record == null)
                    return 0f;

                return record.ImmunityChangePerTick(pawn, true, hediff) * GenDate.TicksPerDay;
            }, 0f, null);
        }

        private static int Untended(Pawn pawn)
        {
            if (pawn.health == null || pawn.health.hediffSet == null)
                return 0;

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            int count = 0;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];

                if (hediff != null && hediff.Visible && hediff.TendableNow())
                    count++;
            }

            return count;
        }

        /// <summary>
        /// A tend that is holding, and when it stops holding.
        ///
        /// <b>The soonest expiry, not the worst quality.</b> A pawn with four tended wounds is fine until the
        /// first of them runs out, and that is the moment the row is warning about.
        /// </summary>
        private static HospitalTreatment Tended(Pawn pawn)
        {
            if (pawn.health == null || pawn.health.hediffSet == null)
                return default(HospitalTreatment);

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;

            int soonest = int.MaxValue;
            float quality = 0f;
            int tended = 0;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];

                if (hediff == null || !hediff.Visible)
                    continue;

                // <b>A tend that can never lapse into needing another one is not a treatment.</b> A torn off
                // tail keeps a live HediffComp_TendDuration for as long as its timer runs, so IsTended stays
                // true and the pawn sat in this tab reading "Tended 68%" with nothing anybody could do about it
                // and nothing that would ever change. Reported on a crow, 2026-08-25.
                //
                // Hediff_MissingPart overrides TendableNow to IsFreshNonSolidExtremity and its Tended() clears
                // IsFresh, so a dressed stump answers false here and drops out. It also stops bleeding at the
                // same moment, which is the whole reason it needed tending in the first place.
                //
                // <b>ignoreTimer, and that is load bearing.</b> The question is whether this is the kind of
                // thing that gets tended at all, not whether it is due right now. Every currently tended wound
                // answers false to the timed form, so asking that would empty this tab of exactly the pawns it
                // exists to list.
                if (!hediff.TendableNow(true))
                    continue;

                HediffComp_TendDuration comp = hediff.TryGetComp<HediffComp_TendDuration>();

                if (comp == null || !comp.IsTended)
                    continue;

                tended++;

                if (comp.tendTicksLeft <= 0 || comp.tendTicksLeft >= soonest)
                    continue;

                soonest = comp.tendTicksLeft;
                quality = comp.tendQuality;
            }

            if (tended == 0)
                return default(HospitalTreatment);

            HospitalTreatment result = new HospitalTreatment
            {
                Label = "Tended " + Percent(quality),
                Tone = HospitalTone.Good,
                Active = true
            };

            // A permanent tend has no clock at all, which is the case for the ones that never need doing again.
            result.Note = soonest == int.MaxValue
                ? "The tend holds."
                : "Wears off in " + soonest.ToStringTicksToPeriod(false, false, false) + ".";

            return result;
        }

        /// <summary>
        /// What a queued operation is waiting on, which is nearly always a person rather than a thing.
        ///
        /// <b>The skill and the best surgeon by name, because that is the decision.</b> An operation needing skill
        /// six when the colony's best is an eleven is waiting for them to wake up; the same operation when the
        /// best is a four is waiting forever, and the row should say so rather than sitting there looking patient.
        /// </summary>
        private static HospitalTreatment Surgery(HospitalPatient patient)
        {
            Pawn pawn = patient.Pawn;
            Bill_Medical bill = FirstOperation(pawn);

            if (bill == null)
                return new HospitalTreatment
                {
                    Label = "Operation queued", Tone = HospitalTone.Waiting, Active = false
                };

            int required = HospitalSurgery.RequiredSkill(bill.recipe);

            Pawn best = HospitalSurgery.BestSurgeon(patient.Map, bill.recipe, pawn);

            string note;
            HospitalTone tone = HospitalTone.Waiting;

            if (best == null)
                note = required > 0
                    ? "Nobody here has Medicine " + required + "."
                    : "Nobody here can operate.";
            else
                note = (required > 0 ? "Needs Medicine " + required + "; best is " : "Best is ")
                       + best.LabelShortCap + ", " + HospitalSurgery.SkillOf(best) + ".";

            if (best == null)
                tone = HospitalTone.Urgent;

            string label = patient.Operations == 1
                ? bill.LabelCap.ToString()
                : patient.Operations + " operations queued";

            return new HospitalTreatment { Label = label, Note = note, Tone = tone, Active = false };
        }

        private static Bill_Medical FirstOperation(Pawn pawn)
        {
            BillStack stack = pawn.BillStack;

            if (stack == null || stack.Bills == null)
                return null;

            for (int i = 0; i < stack.Bills.Count; i++)
            {
                Bill_Medical bill = stack.Bills[i] as Bill_Medical;

                if (bill != null && !bill.deleted && !HospitalSurgery.IsDose(bill.recipe))
                    return bill;
            }

            return null;
        }

        /// <summary>
        /// How long until they are whole again, worked out the way the game actually heals them.
        ///
        /// <b>The arithmetic is the game's own.</b> Every six hundred ticks a flesh pawn who is not starving heals
        /// one injury by eight percent of their health scale, and a second one on top of that if any injury is
        /// tended, scaled by the tend quality. So the daily rate is that figure a hundred times over, and the
        /// forecast is what is left divided by it.
        ///
        /// <b>It says healed rather than walking again, and the difference is deliberate.</b> A downed pawn stands
        /// up when their capacities recover, which happens well before the last scar closes, and this arithmetic
        /// cannot see that moment. Promising it would be a confident wrong number where an honest partial one
        /// will do.
        /// </summary>
        private static HospitalTreatment Healing(HospitalPatient patient)
        {
            Pawn pawn = patient.Pawn;

            float days = HealingDays(pawn);

            string note = days > 0f
                ? "Healed in about " + Mathf.RoundToInt(days * GenDate.TicksPerDay)
                    .ToStringTicksToPeriod(false, false, false) + " at this rate."
                : "Not healing on their own.";

            return new HospitalTreatment
            {
                Label = patient.Bed != null ? "Resting" : "Up and about",
                Note = note,
                Tone = days > 0f ? HospitalTone.Good : HospitalTone.Waiting
            };
        }

        /// <summary>Days to close every injury that can close, or zero when none of them can.</summary>
        private static float HealingDays(Pawn pawn)
        {
            return UIGuard.Try("Hospital.HealingRate", () =>
            {
                if (pawn.health == null || pawn.health.hediffSet == null || pawn.RaceProps == null
                    || !pawn.RaceProps.IsFlesh)
                    return 0f;

                if (pawn.needs != null && pawn.needs.food != null && pawn.needs.food.Starving)
                    return 0f;

                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;

                float remaining = 0f;
                bool natural = false;
                bool tended = false;
                float tendQuality = 0f;

                for (int i = 0; i < hediffs.Count; i++)
                {
                    Hediff_Injury injury = hediffs[i] as Hediff_Injury;

                    if (injury == null)
                        continue;

                    bool healsNaturally = injury.CanHealNaturally();
                    bool healsFromTending = injury.CanHealFromTending();

                    if (!healsNaturally && !healsFromTending)
                        continue;

                    remaining += injury.Severity;

                    natural |= healsNaturally;

                    if (!healsFromTending)
                        continue;

                    HediffComp_TendDuration comp = injury.TryGetComp<HediffComp_TendDuration>();

                    tended = true;
                    tendQuality = Mathf.Max(tendQuality, comp != null ? comp.tendQuality : 0f);
                }

                if (remaining <= 0f)
                    return 0f;

                // Eight percent of the health scale per heal event, and a hundred events a day: sixty thousand
                // ticks divided by the six hundred the game heals on.
                float factor = pawn.HealthScale * 0.01f * pawn.GetStatValue(StatDefOf.InjuryHealingFactor);

                float perEvent = 0f;

                if (natural)
                    perEvent += 8f * factor;

                if (tended)
                    perEvent += 8f * GenMath.LerpDouble(0f, 1f, 0.5f, 1.5f, Mathf.Clamp01(tendQuality)) * factor;

                float perDay = perEvent * (GenDate.TicksPerDay / 600f);

                return perDay <= 0f ? 0f : remaining / perDay;
            }, 0f, null);
        }

        private static string Percent(float fraction)
        {
            return Mathf.RoundToInt(fraction * 100f) + "%";
        }
    }
}
