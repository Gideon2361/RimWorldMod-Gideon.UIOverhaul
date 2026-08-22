using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The three things this tab needs that RimWorld keeps to itself.
    ///
    /// <b>Everything here is a read, and every one of them has a stated answer for being missing.</b> Reflection
    /// into another assembly is a dependency on a name, and a name can change in a patch. So each lookup is done
    /// once, cached, and reported once if it fails, and each caller has a defined behaviour when the answer is
    /// unavailable: the taming odds go from a percentage to "unknown", and the training decay clock goes from
    /// "forgets in 1.4 days" to nothing at all. Neither degrades into a wrong number, which is the only outcome
    /// that would matter.
    ///
    /// <b>Why these three and nothing else.</b> The taming chance curve and the decay clock are the two figures
    /// this tab exists to surface that vanilla computes and never shows; the training steps are what tells a
    /// half trained animal from a trained one. Everything else the tab draws has a public accessor, and anything
    /// that gains one should stop being read from here.
    /// </summary>
    internal static class AnimalReflection
    {
        private static bool resolved;

        private static FieldInfo curveField;
        private static FieldInfo decayField;
        private static FieldInfo stepsField;

        /// <summary>
        /// The curve turning an animal's wildness into a factor on the handler's taming chance.
        ///
        /// A private static on <c>InteractionWorker_RecruitAttempt</c>, which is where the game applies it. See
        /// <see cref="AnimalFacts.TameOdds"/> for why this is read rather than reimplemented.
        /// </summary>
        internal static SimpleCurve WildnessTameCurve
        {
            get
            {
                Resolve();

                if (curveField == null)
                    return null;

                return UIGuard.Try("Animals.TameCurve", () => curveField.GetValue(null) as SimpleCurve, null, null);
            }
        }

        /// <summary>
        /// The tick the animal's training decay timer is counted from, or a negative number when unknown.
        ///
        /// Vanilla forgets one trained skill once <c>countDecayFrom</c> plus the degradation period has passed,
        /// then restarts the clock. So this field plus <c>TrainableUtility.DegradationPeriodTicks</c> is the whole
        /// countdown, and it is the only way to say when an animal is next at risk.
        /// </summary>
        internal static int DecayCountedFrom(Pawn_TrainingTracker tracker)
        {
            Resolve();

            if (tracker == null || decayField == null)
                return -1;

            return UIGuard.Try("Animals.DecayClock", () => (int) decayField.GetValue(tracker), -1, null);
        }

        /// <summary>
        /// How many training steps of one kind the animal has done.
        ///
        /// <c>GetSteps</c> is internal to the game's own assembly, so the backing <c>DefMap</c> is read instead.
        /// Zero and the def's full step count are the two ends; anything between is a part trained animal, which
        /// is the state the species row counts and the pips show.
        /// </summary>
        internal static int Steps(Pawn_TrainingTracker tracker, TrainableDef trainable)
        {
            Resolve();

            if (tracker == null || trainable == null || stepsField == null)
                return 0;

            return UIGuard.Try("Animals.TrainingSteps", () =>
            {
                DefMap<TrainableDef, int> steps = stepsField.GetValue(tracker) as DefMap<TrainableDef, int>;

                return steps == null ? 0 : steps[trainable];
            }, 0, null);
        }

        /// <summary>Whether the decay clock could be read at all, so a caller can leave the column blank.</summary>
        internal static bool DecayClockAvailable
        {
            get
            {
                Resolve();

                return decayField != null;
            }
        }

        /// <summary>
        /// Looks the three members up once.
        ///
        /// <b>Reported at most once each, and not as a failure the player has to act on.</b> Every one of these
        /// is a nicety: the tab draws without them. So the consequence text says what is missing rather than
        /// asking for anything, and nothing here throws, because the caller is often a cell being drawn.
        /// </summary>
        private static void Resolve()
        {
            if (resolved)
                return;

            resolved = true;

            UIGuard.Try("Animals.Reflect", () =>
            {
                curveField = AccessTools.Field(typeof(InteractionWorker_RecruitAttempt),
                    "TameChanceFactorCurve_Wildness");

                decayField = AccessTools.Field(typeof(Pawn_TrainingTracker), "countDecayFrom");
                stepsField = AccessTools.Field(typeof(Pawn_TrainingTracker), "steps");
            }, "Part of the animals tab could not read the game's taming and training internals, so the taming "
               + "chance and the training decay countdown are left blank.");
        }
    }
}
