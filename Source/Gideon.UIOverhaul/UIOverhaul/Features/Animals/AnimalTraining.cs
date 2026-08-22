using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// What is trained on one animal, and how close it is to losing some of it.
    ///
    /// <b>The countdown is the reason this exists.</b> Trained levels are a state the player set and can see on
    /// the animal; the thing they cannot see anywhere in vanilla is that the clock is running, and that in a day
    /// and a half this husky drops a step and possibly forgets Obedience outright. So the species row counts what
    /// is at risk rather than what is trained.
    /// </summary>
    internal struct AnimalTrainingState
    {
        /// <summary>Whether this animal can be trained in anything at all.</summary>
        internal bool Trainable;

        /// <summary>How many kinds are fully learned, Tameness included.</summary>
        internal int Learned;

        /// <summary>How many kinds are part trained: some steps done, not learned yet.</summary>
        internal int PartTrained;

        /// <summary>How many kinds the player has asked for that are not learned yet.</summary>
        internal int Wanted;

        /// <summary>
        /// Days until the next training step is lost, or negative when nothing is decaying.
        ///
        /// Zero means it is due now, which happens while the game is paused or the animal is unspawned, since
        /// vanilla only advances the clock on a rare tick.
        /// </summary>
        internal float DecayDaysLeft;

        /// <summary>
        /// The kinds that would be forgotten outright if the next step went, rather than merely reduced.
        ///
        /// Vanilla picks one of the eligible kinds at random when the clock runs out, so this is what is at
        /// stake, not a prediction of which one goes. Naming a single kind would be a guess dressed as a fact.
        /// </summary>
        internal List<TrainableDef> AtRisk;

        internal bool Decaying => DecayDaysLeft >= 0f;

        internal bool AnythingAtRisk => AtRisk != null && AtRisk.Count > 0;
    }

    /// <summary>
    /// Reading and writing an animal's training, and working out when it decays.
    ///
    /// <b>The decay rule is reproduced from the game rather than approximated,</b> because a countdown that is
    /// wrong by a day is worse than no countdown: the player leaves the tab believing they have time. Vanilla's
    /// rare tick does four things, and all four are honoured here. The clock only runs while the animal is
    /// spawned and unsuspended. It only runs at all once Tameness has at least one step. When it expires, one
    /// kind is chosen from those with steps, excluding any kind that is a prerequisite of another trained kind,
    /// and that one loses a step. Tameness itself is exempt when the animal cannot lose tameness, which is the
    /// case in a fenced pen or for a nearly domestic species.
    ///
    /// <b>What is deliberately not claimed.</b> Which kind decays is a random choice made at the moment it
    /// happens, so nothing here names one. It reports how long is left and which kinds are one step from being
    /// forgotten, which is everything the player can act on.
    /// </summary>
    internal static class AnimalTraining
    {
        /// <summary>Scratch for the decay candidate walk. One list, since this runs on the UI thread only.</summary>
        private static readonly List<TrainableDef> Candidates = new List<TrainableDef>();

        private static readonly HashSet<TrainableDef> Prerequisites = new HashSet<TrainableDef>();

        /// <summary>
        /// The full training picture for one animal.
        ///
        /// Allocates a list only when something is actually at risk, which is the uncommon case: a herd of
        /// untrainable muffalo asks for this once a second per animal and gets no allocation at all.
        /// </summary>
        internal static AnimalTrainingState Of(Pawn animal)
        {
            AnimalTrainingState state = new AnimalTrainingState { DecayDaysLeft = -1f };

            Pawn_TrainingTracker training = animal?.training;

            if (training == null)
                return state;

            List<TrainableDef> kinds = TrainableUtility.TrainableDefsInListOrder;

            if (kinds == null)
                return state;

            Candidates.Clear();
            Prerequisites.Clear();

            int tamenessSteps = 0;

            for (int i = 0; i < kinds.Count; i++)
            {
                TrainableDef kind = kinds[i];

                if (!training.CanBeTrained(kind))
                    continue;

                state.Trainable = true;

                bool learned = training.HasLearned(kind);
                int steps = AnimalReflection.Steps(training, kind);

                if (kind == TrainableDefOf.Tameness)
                    tamenessSteps = steps;

                if (learned)
                    state.Learned++;
                else if (steps > 0)
                    state.PartTrained++;

                if (!learned && training.GetWanted(kind))
                    state.Wanted++;

                if (steps <= 0)
                    continue;

                Candidates.Add(kind);

                if (kind.prerequisites != null)
                {
                    for (int p = 0; p < kind.prerequisites.Count; p++)
                        Prerequisites.Add(kind.prerequisites[p]);
                }
            }

            // The same three gates vanilla's tick applies before it touches anything. An animal off the map has
            // its clock pushed forward instead of expiring, so there is nothing to count down to.
            if (!state.Trainable || tamenessSteps <= 0 || !animal.Spawned || animal.Suspended
                || animal.RaceProps.animalType == AnimalType.Dryad)
                return state;

            int from = AnimalReflection.DecayCountedFrom(training);

            if (from < 0)
                return state;

            int period = TrainableUtility.DegradationPeriodTicks(animal);

            if (period <= 0)
                return state;

            int ticksLeft = from + period - Find.TickManager.TicksGame;

            state.DecayDaysLeft = Mathf.Max(0f, ticksLeft / (float) GenDate.TicksPerDay);

            // Whatever loses a step is chosen from the kinds with steps, minus the ones another trained kind
            // depends on. Tameness drops out of the running while Obedience is trained, for instance, which is
            // why a trained animal does not quietly revert to wild.
            for (int i = 0; i < Candidates.Count; i++)
            {
                TrainableDef kind = Candidates[i];

                if (Prerequisites.Contains(kind))
                    continue;

                if (kind == TrainableDefOf.Tameness && !TrainableUtility.TamenessCanDecay(animal))
                    continue;

                // One step left means the next loss unlearns it. More than one only costs progress, which is not
                // worth alarming anybody about.
                if (AnimalReflection.Steps(training, kind) > 1)
                    continue;

                if (state.AtRisk == null)
                    state.AtRisk = new List<TrainableDef>(2);

                state.AtRisk.Add(kind);
            }

            return state;
        }

        /// <summary>Steps done of one kind, for the pips on an opened animal.</summary>
        internal static int StepsOf(Pawn animal, TrainableDef kind)
        {
            return animal?.training == null ? 0 : AnimalReflection.Steps(animal.training, kind);
        }

        /// <summary>Whether one kind is fully learned.</summary>
        internal static bool Learned(Pawn animal, TrainableDef kind)
        {
            return animal?.training != null && kind != null && animal.training.HasLearned(kind);
        }

        internal static bool Wanted(Pawn animal, TrainableDef kind)
        {
            return animal?.training != null && kind != null && animal.training.GetWanted(kind);
        }

        /// <summary>
        /// Whether this kind can be asked for on this animal, and why not when it cannot.
        ///
        /// Vanilla's own report, which covers the animal being too wild for the skill, the prerequisite not being
        /// learned, and the species being incapable. Reproducing those tests would drift from the tab that
        /// players compare this against.
        /// </summary>
        internal static AcceptanceReport CanAsk(Pawn animal, TrainableDef kind)
        {
            if (animal?.training == null || kind == null)
                return false;

            // A local rather than the guard's return value: AcceptanceReport converts from bool implicitly, so a
            // fallback of false makes the generic overload ambiguous with the void one and the error names
            // neither. A block bodied lambda can only be the void overload.
            AcceptanceReport report = false;

            UIGuard.Try("Animals.CanTrain", () => { report = animal.training.CanAssignToTrain(kind); }, null);

            return report;
        }

        /// <summary>
        /// Asks for a kind of training, or stops asking.
        ///
        /// <b>Recursive on purpose, through vanilla's own method.</b> Asking for Rescue means asking for
        /// Obedience and Tameness underneath it, and clearing Obedience has to clear what depends on it. Setting
        /// the flag directly would leave an animal wanted for a skill it can never start.
        /// </summary>
        internal static void SetWanted(Pawn animal, TrainableDef kind, bool wanted)
        {
            UIGuard.Try("Animals.SetTraining", () =>
            {
                if (animal?.training == null || kind == null)
                    return;

                if (!animal.training.CanAssignToTrain(kind).Accepted)
                    return;

                animal.training.SetWantedRecursive(kind, wanted);
            }, "The training request was not changed.");
        }
    }
}
