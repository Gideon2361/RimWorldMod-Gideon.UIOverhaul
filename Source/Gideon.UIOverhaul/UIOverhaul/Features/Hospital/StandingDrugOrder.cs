using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>Who an order is pointed at.</summary>
    internal enum StandingOrderTarget
    {
        /// <summary>One named person, which is the case for luciferium and for a course of treatment.</summary>
        OnePatient,

        /// <summary>Anybody lying in a bed marked medical, which is what a ward-wide order means.</summary>
        MedicalBed,

        /// <summary>Every colonist on the map. Penoxycyline before the toxic fallout, and little else.</summary>
        Everyone
    }

    internal enum StandingOrderPeriod
    {
        Hours,

        Days
    }

    /// <summary>
    /// A medical bill that writes itself again on a clock.
    ///
    /// <b>The case that justifies the feature is medication on a schedule.</b> Painkillers twice a day for
    /// somebody who cannot get out of bed, penoxycyline across the ward every five days, luciferium for the
    /// colonist who dies without it. All three are things a player currently does by remembering, and forgetting
    /// the third one kills somebody.
    ///
    /// <b>Only the operations that can honestly repeat.</b> The test is the recipe's worker, not its name: the
    /// administer family consumes an item and changes nothing permanent. An implant or an amputation cannot
    /// repeat, because a person has two eyes, and those stay one-shot bills written from the picker. See
    /// <see cref="HospitalSurgery.IsDose"/>.
    ///
    /// <b>Every drug in the game is available and that is not a list anyone maintains.</b> RimWorld generates an
    /// <c>Administer_&lt;drug&gt;</c> recipe for every drug that exists, so a mod's painkiller gets a standing
    /// order for free.
    ///
    /// <b>It writes a real bill and then waits.</b> When a dose comes due it queues an ordinary
    /// <c>Bill_Medical</c> and does nothing else: the doctor, the walk, the ingredient and the job are all the
    /// game's. It will not queue a second while the first is outstanding, so a patient nobody has got to yet does
    /// not accumulate six doses.
    /// </summary>
    internal class StandingDrugOrder : IExposable
    {
        internal ThingDef drug;

        internal StandingOrderTarget target = StandingOrderTarget.OnePatient;

        /// <summary>The named patient, when the target is one person.</summary>
        internal Pawn patient;

        /// <summary>How many of <see cref="period"/> between doses.</summary>
        internal int every = 12;

        internal StandingOrderPeriod period = StandingOrderPeriod.Hours;

        internal HospitalConditionGate gate = new HospitalConditionGate();

        /// <summary>Who is to deliver it, or null for whoever is free. See <see cref="Nurse"/>.</summary>
        internal Pawn nurse;

        /// <summary>Skip a dose that would tip the patient into overdose. On by default.</summary>
        internal bool skipOnOverdose = true;

        /// <summary>Stop the order when the patient picks up an addiction. On by default.</summary>
        internal bool holdOnAddiction = true;

        internal bool suspended;

        /// <summary>When each patient was last dosed by this order, so the clock is per person.</summary>
        private Dictionary<Pawn, int> dosedAt = new Dictionary<Pawn, int>();

        internal string Label
        {
            get { return drug != null ? drug.LabelCap.ToString() : "Unset order"; }
        }

        /// <summary>Ticks between doses for one patient.</summary>
        internal int IntervalTicks
        {
            get
            {
                int unit = period == StandingOrderPeriod.Days ? GenDate.TicksPerDay : GenDate.TicksPerHour;

                return Mathf.Max(unit, every * unit);
            }
        }

        internal string FrequencyLabel
        {
            get { return every + (period == StandingOrderPeriod.Days ? "d" : "h"); }
        }

        internal string TargetLabel
        {
            get
            {
                switch (target)
                {
                    case StandingOrderTarget.MedicalBed:
                        return "anyone in a medical bed";

                    case StandingOrderTarget.Everyone:
                        return "everyone";

                    default:
                        return patient != null ? patient.LabelShortCap.ToString() : "nobody";
                }
            }
        }

        internal string NurseLabel
        {
            get { return nurse != null ? nurse.LabelShortCap.ToString() : "anyone"; }
        }

        /// <summary>The recipe that hands this drug over, or null when the game has none for it.</summary>
        internal RecipeDef Recipe
        {
            get
            {
                if (drug == null)
                    return null;

                return UIGuard.Try<RecipeDef>("Hospital.OrderRecipe",
                    () => DefDatabase<RecipeDef>.GetNamedSilentFail("Administer_" + drug.defName), null, null);
            }
        }

        // -------------------------------------------------------------------------------------------
        // Who it applies to
        // -------------------------------------------------------------------------------------------

        /// <summary>Whether this order is pointed at this person at all, before any condition is tested.</summary>
        internal bool Targets(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;

            return UIGuard.Try("Hospital.OrderTargets", () =>
            {
                switch (target)
                {
                    case StandingOrderTarget.OnePatient:
                        return pawn == patient;

                    case StandingOrderTarget.MedicalBed:
                        Building_Bed bed = pawn.CurrentBed();

                        return bed != null && bed.Medical && Ours(pawn);

                    default:
                        return Ours(pawn);
                }
            }, false, null);
        }

        /// <summary>A colony-wide order means the colony, not everybody standing on the map.</summary>
        private static bool Ours(Pawn pawn)
        {
            return pawn.IsColonist || pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony;
        }

        // -------------------------------------------------------------------------------------------
        // The clock and the safeguards
        // -------------------------------------------------------------------------------------------

        /// <summary>Ticks until the next dose for this patient, or negative when one is overdue.</summary>
        internal int NextDoseIn(Pawn pawn)
        {
            int last;

            if (pawn == null || !dosedAt.TryGetValue(pawn, out last))
                return 0;

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            return last + IntervalTicks - now;
        }

        /// <summary>
        /// Why this order is not acting on this patient right now, or null when it should.
        ///
        /// <b>A reason rather than a boolean, because the row says it out loud.</b> An order that quietly does
        /// nothing is worse than one that refuses in words: "held: addicted" is a fact the player can act on, and
        /// a blank row is a bug report.
        /// </summary>
        internal string BlockedBy(Pawn pawn)
        {
            return UIGuard.Try<string>("Hospital.OrderBlocked", () =>
            {
                if (suspended)
                    return "paused";

                if (drug == null || Recipe == null)
                    return "no recipe";

                if (!gate.Allows(pawn))
                    return "condition not met";

                if (holdOnAddiction && !AddictionIsThePoint && Addicted(pawn))
                    return "held: addicted";

                if (skipOnOverdose && Overdosing(pawn))
                    return "held: overdose";

                if (Outstanding(pawn))
                    return "dose already queued";

                return null;
            }, "unreadable", null);
        }

        /// <summary>
        /// Whether a dose is already on the patient's bill stack.
        ///
        /// <b>This is what stops a patient nobody has reached from accumulating six doses.</b> The order is a
        /// standing instruction, not a queue: one outstanding bill at a time means a doctor who finally gets
        /// there administers one dose rather than working through a backlog.
        /// </summary>
        internal bool Outstanding(Pawn pawn)
        {
            RecipeDef recipe = Recipe;

            if (recipe == null || pawn == null)
                return false;

            BillStack stack = pawn.BillStack;

            if (stack == null || stack.Bills == null)
                return false;

            for (int i = 0; i < stack.Bills.Count; i++)
            {
                Bill bill = stack.Bills[i];

                if (bill != null && !bill.deleted && bill.recipe == recipe)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether dosing now risks an overdose, using the game's own threshold.
        ///
        /// Vanilla's scheduled-drug logic refuses at a severity above half for anything that can overdose, and
        /// borrowing that number rather than inventing one means the safeguard agrees with the drug policy system
        /// a player already understands.
        /// </summary>
        private bool Overdosing(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
                return false;

            CompProperties_Drug props = drug.GetCompProperties<CompProperties_Drug>();

            if (props == null || !props.CanCauseOverdose)
                return false;

            Hediff overdose = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.DrugOverdose);

            return overdose != null && overdose.Severity > 0.5f;
        }

        private bool Addicted(Pawn pawn)
        {
            CompProperties_Drug props = drug.GetCompProperties<CompProperties_Drug>();

            if (props == null || props.chemical == null)
                return false;

            return AddictionUtility.IsAddicted(pawn, props.chemical);
        }

        /// <summary>
        /// Whether this drug's addiction is the whole point, in which case the hold must not apply.
        ///
        /// <b>Luciferium is the case that justifies the feature and the one the safeguards must not break:</b>
        /// withdrawal kills, the addiction is deliberate, and an order that stopped itself the moment the
        /// addiction took hold would stop on the first dose and kill the patient a fortnight later.
        ///
        /// <b>Tested by what the addiction does rather than by name.</b> An addiction hediff with a life
        /// threatening stage is one you cannot walk away from, which is exactly the distinction wanted -- and it
        /// catches a mod's own equivalent without a list to maintain.
        /// </summary>
        private bool AddictionIsThePoint
        {
            get
            {
                return UIGuard.Try("Hospital.LethalWithdrawal", () =>
                {
                    CompProperties_Drug props = drug.GetCompProperties<CompProperties_Drug>();

                    if (props == null || props.chemical == null || props.chemical.addictionHediff == null)
                        return false;

                    List<HediffStage> stages = props.chemical.addictionHediff.stages;

                    if (stages == null)
                        return false;

                    for (int i = 0; i < stages.Count; i++)
                    {
                        if (stages[i] != null && stages[i].lifeThreatening)
                            return true;
                    }

                    return false;
                }, false, null);
            }
        }

        // -------------------------------------------------------------------------------------------
        // Acting
        // -------------------------------------------------------------------------------------------

        /// <summary>Whether the clock says this patient is due, ignoring everything else.</summary>
        internal bool Due(Pawn pawn)
        {
            int last;

            if (!dosedAt.TryGetValue(pawn, out last))
                return true;

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            // A loaded save or a debug clock change can put the recorded tick in the future, which would otherwise
            // stall the order until the game caught up with it.
            if (last > now)
            {
                dosedAt[pawn] = now;

                return false;
            }

            return now - last >= IntervalTicks;
        }

        /// <summary>Queues the dose and restarts this patient's clock.</summary>
        internal void Fire(Pawn pawn)
        {
            RecipeDef recipe = Recipe;

            if (recipe == null || pawn == null)
                return;

            HospitalSurgery.Write(pawn, recipe, null, nurse);

            dosedAt[pawn] = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
        }

        /// <summary>Drops the clock for people this order no longer applies to, so the map does not accumulate.</summary>
        internal void Forget(Pawn pawn)
        {
            if (pawn != null)
                dosedAt.Remove(pawn);
        }

        internal StandingDrugOrder Clone()
        {
            return new StandingDrugOrder
            {
                drug = drug,
                target = target,
                patient = patient,
                every = every,
                period = period,
                gate = gate != null ? gate.Clone() : new HospitalConditionGate(),
                nurse = nurse,
                skipOnOverdose = skipOnOverdose,
                holdOnAddiction = holdOnAddiction,
                suspended = suspended
            };
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref drug, "drug");
            Scribe_Values.Look(ref target, "target");
            Scribe_References.Look(ref patient, "patient");
            Scribe_Values.Look(ref every, "every", 12);
            Scribe_Values.Look(ref period, "period");
            Scribe_Deep.Look(ref gate, "gate");
            Scribe_References.Look(ref nurse, "nurse");
            Scribe_Values.Look(ref skipOnOverdose, "skipOnOverdose", true);
            Scribe_Values.Look(ref holdOnAddiction, "holdOnAddiction", true);
            Scribe_Values.Look(ref suspended, "suspended");
            Scribe_Collections.Look(ref dosedAt, "dosedAt", LookMode.Reference, LookMode.Value);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            if (gate == null)
                gate = new HospitalConditionGate();

            // A reference-keyed dictionary comes back with a null key for anything that no longer exists, and a
            // dead patient's clock is meaningless anyway. Collected first and removed after, because a dictionary
            // cannot be written to while it is being walked.
            if (dosedAt == null)
            {
                dosedAt = new Dictionary<Pawn, int>();

                return;
            }

            List<Pawn> gone = null;

            foreach (KeyValuePair<Pawn, int> pair in dosedAt)
            {
                if (pair.Key != null)
                    continue;

                if (gone == null)
                    gone = new List<Pawn>();

                gone.Add(pair.Key);
            }

            if (gone == null)
                return;

            for (int i = 0; i < gone.Count; i++)
                dosedAt.Remove(gone[i]);
        }
    }
}
