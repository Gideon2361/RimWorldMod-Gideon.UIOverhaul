using System;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Integrations
{
    /// <summary>
    /// Rimbody's per-pawn workout goals, on our Bio panel.
    ///
    /// <b>Two settings, and they are the two a player changes.</b> <c>CompPhysique</c> carries a muscle goal and
    /// a fat goal, each a switch plus a number from 0 to 50. Rimbody's own words for what they do:
    /// "when gain goal exceeds their muscle level, pawns will eat more often and focus on strength exercises",
    /// and "when diet goal is below their fat level, pawns will only eat when hungry and will focus on cardio
    /// exercises". Everything else on that comp is state the simulation owns -- fatigue, exhaustion, the memory
    /// queue -- and none of it is a setting.
    ///
    /// <b>Running, not installed,</b> which Aaron asked for explicitly on 2026-08-23. <c>ModIntegrations.Loaded</c>
    /// reads <c>LoadedModManager.RunningModsListForReading</c>, so a copy sitting in the workshop folder unticked
    /// in the mod list is not detected. The type lookup after it is the second half of the same test: a package
    /// id can be present while the assembly failed to load, and a type that resolves is proof the code is really
    /// there.
    ///
    /// <b>Reflection throughout, and by name rather than by reference.</b> Adding a hard reference to Rimbody
    /// would make this mod refuse to load without it. Every member is resolved once behind
    /// <see cref="Ready"/>, and a rename in a future Rimbody costs the panel block rather than the panel: this
    /// reports once, answers "nothing to show", and the Bio panel draws as it does for anybody else.
    ///
    /// <b>Written back to the field directly, because that is where Rimbody reads it from.</b> Its own card does
    /// the same -- <c>Widgets.Checkbox(ref compPhysique.useMuscleGoal)</c> and a slider assigning
    /// <c>compPhysique.MuscleGoal</c> -- so there is no setter to call and no notification to send. The comp is
    /// scribed by the game, so a change lands in the save the ordinary way.
    /// </summary>
    internal static class RimbodyIntegration
    {
        internal const string PackageId = "Maux36.Rimbody";

        /// <summary>Rimbody's own slider bounds and step, taken from its card so ours cannot disagree.</summary>
        internal const float Ceiling = 50f;

        internal const float Step = 0.1f;

        private static bool resolved;

        private static bool usable;

        private static Type compType;

        private static FieldInfo useMuscleGoal;
        private static FieldInfo muscleGoal;
        private static FieldInfo useFatGoal;
        private static FieldInfo fatGoal;
        private static FieldInfo muscleMass;
        private static FieldInfo bodyFat;

        /// <summary>Whether Rimbody is running and every member this needs was found.</summary>
        internal static bool Available
        {
            get { return Ready(); }
        }

        /// <summary>
        /// This pawn's physique comp, or null when there is nothing to show.
        ///
        /// <b>Null for more reasons than a missing mod.</b> Rimbody gives the comp to humanlikes and fills it
        /// lazily -- <c>BodyFat</c> and <c>MuscleMass</c> both start at -1 and mean "not generated yet" -- so a
        /// pawn can carry the comp and still have no physique. Both are treated the same way here, because a goal
        /// set against a body the simulation has not measured yet is a number with nothing to compare it to.
        /// </summary>
        internal static ThingComp Physique(Pawn pawn)
        {
            if (pawn == null || !Ready() || pawn.AllComps == null)
                return null;

            return UIGuard.Try<ThingComp>("Integrations.RimbodyComp", () =>
            {
                // Walked rather than fetched with TryGetComp, which needs the comp's type as a generic argument
                // and so cannot be used for a type known only by name at run time.
                for (int i = 0; i < pawn.AllComps.Count; i++)
                {
                    ThingComp comp = pawn.AllComps[i];

                    if (comp == null || !compType.IsInstanceOfType(comp))
                        continue;

                    return Measured(comp) ? comp : null;
                }

                return null;
            }, null, null);
        }

        /// <summary>Whether the simulation has given this pawn a body yet. Both fields start at -1.</summary>
        private static bool Measured(ThingComp comp)
        {
            return Number(muscleMass, comp) >= 0f && Number(bodyFat, comp) >= 0f;
        }

        internal static bool UseMuscleGoal(ThingComp comp)
        {
            return Flag(useMuscleGoal, comp);
        }

        internal static bool UseFatGoal(ThingComp comp)
        {
            return Flag(useFatGoal, comp);
        }

        internal static float MuscleGoal(ThingComp comp)
        {
            return Number(muscleGoal, comp);
        }

        internal static float FatGoal(ThingComp comp)
        {
            return Number(fatGoal, comp);
        }

        internal static float MuscleMass(ThingComp comp)
        {
            return Number(muscleMass, comp);
        }

        internal static float BodyFat(ThingComp comp)
        {
            return Number(bodyFat, comp);
        }

        internal static void SetUseMuscleGoal(ThingComp comp, bool value)
        {
            Set(useMuscleGoal, comp, value);
        }

        internal static void SetUseFatGoal(ThingComp comp, bool value)
        {
            Set(useFatGoal, comp, value);
        }

        internal static void SetMuscleGoal(ThingComp comp, float value)
        {
            Set(muscleGoal, comp, Clamped(value));
        }

        internal static void SetFatGoal(ThingComp comp, float value)
        {
            Set(fatGoal, comp, Clamped(value));
        }

        /// <summary>
        /// Rounded to a tenth and held inside the range, exactly as Rimbody's own card does it.
        ///
        /// Its slider rounds to one decimal and its nudge buttons clamp at 0 and 50. A value written from here
        /// has to obey the same rules or the number on our panel and the number on theirs are different numbers.
        /// </summary>
        private static float Clamped(float value)
        {
            return UnityEngine.Mathf.Clamp(UnityEngine.Mathf.Round(value * 10f) / 10f, 0f, Ceiling);
        }

        private static bool Flag(FieldInfo field, ThingComp comp)
        {
            return UIGuard.Try("Integrations.RimbodyRead", () =>
            {
                if (field == null || comp == null)
                    return false;

                return (bool) field.GetValue(comp);
            }, false, null);
        }

        private static float Number(FieldInfo field, ThingComp comp)
        {
            return UIGuard.Try("Integrations.RimbodyRead", () =>
            {
                if (field == null || comp == null)
                    return -1f;

                return (float) field.GetValue(comp);
            }, -1f, null);
        }

        private static void Set(FieldInfo field, ThingComp comp, object value)
        {
            UIGuard.Try("Integrations.RimbodyWrite", () =>
            {
                if (field == null || comp == null)
                    return;

                field.SetValue(comp, value);
            }, "That workout goal could not be changed. Rimbody's own tab still sets it.");
        }

        private static bool Ready()
        {
            if (resolved)
                return usable;

            resolved = true;
            usable = false;

            if (!ModIntegrations.Loaded(PackageId))
                return false;

            usable = UIGuard.Try("Integrations.BindRimbody", () =>
            {
                compType = AccessTools.TypeByName("Maux36.Rimbody.CompPhysique");

                if (compType == null)
                    return false;

                useMuscleGoal = Bool("useMuscleGoal");
                useFatGoal = Bool("useFatgoal");

                muscleGoal = Float("MuscleGoal");
                fatGoal = Float("FatGoal");
                muscleMass = Float("MuscleMass");
                bodyFat = Float("BodyFat");

                return useMuscleGoal != null && useFatGoal != null && muscleGoal != null && fatGoal != null
                       && muscleMass != null && bodyFat != null;
            }, false, "Rimbody is running, but its physique fields could not be reached, so the Bio panel does "
                      + "not show its workout goals. Rimbody's own tab is unaffected.");

            return usable;
        }

        /// <summary>
        /// A field of the expected type, or null.
        ///
        /// <b>The type is checked as well as the name,</b> which matters more here than it looks: the fat goal is
        /// spelled <c>useFatgoal</c> with a lower case g while the muscle one is <c>useMuscleGoal</c>, so these
        /// names are copied from a decompile rather than guessed at, and a wrong guess that happened to hit
        /// another field would otherwise be written to.
        /// </summary>
        private static FieldInfo Bool(string name)
        {
            FieldInfo field = AccessTools.Field(compType, name);

            return field != null && field.FieldType == typeof(bool) ? field : null;
        }

        private static FieldInfo Float(string name)
        {
            FieldInfo field = AccessTools.Field(compType, name);

            return field != null && field.FieldType == typeof(float) ? field : null;
        }
    }
}
