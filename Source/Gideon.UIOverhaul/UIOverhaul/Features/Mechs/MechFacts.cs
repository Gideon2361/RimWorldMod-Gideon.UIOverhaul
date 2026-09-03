using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// Everything the mech tab reads off one mech, in one place.
    ///
    /// <b>Derived figures rather than raw ones.</b> The game has the charge and the fall rate; it has never
    /// divided one by the other and said how long the mech has. Same for the shutdown threshold, which is a
    /// constant with no representation anywhere in the interface, and for a mech's enabled work types, which
    /// exist on the def and are reachable only through the info card.
    ///
    /// <b>Nothing here decides anything.</b> Every value is read from state RimWorld already keeps, and the
    /// two setters that exist forward to the game's own methods. See <see cref="MechsPanel"/> for the rule.
    /// </summary>
    internal static class MechFacts
    {
        /// <summary>
        /// The charge a mech shuts itself down at, from <c>Need_MechEnergy.ShutdownUntil</c>.
        ///
        /// Copied rather than referenced because it is a <c>const</c> on a Biotech type: naming it directly
        /// would put a hard reference to that class in every build. It has been 15 since Biotech shipped.
        /// </summary>
        internal const float ShutdownAt = 15f;

        /// <summary>Below this fraction of a day's charge the header's figure turns warning colored.</summary>
        internal const float ShortOnCharge = 2f;

        // -------------------------------------------------------------------------------------------
        // What kind of mech this is
        // -------------------------------------------------------------------------------------------

        /// <summary>Whether this mech can be given work at all, which is what earns it a work section.</summary>
        internal static bool IsWorkMech(Pawn mech)
        {
            return mech != null && mech.RaceProps != null && mech.RaceProps.IsWorkMech;
        }

        /// <summary>Light, medium, heavy or ultra heavy, lowercased for a row's second line.</summary>
        internal static string WeightClass(Pawn mech)
        {
            if (mech == null || mech.RaceProps == null || mech.RaceProps.mechWeightClass == null)
                return string.Empty;

            return mech.RaceProps.mechWeightClass.LabelCap.ToString().ToLowerInvariant();
        }

        /// <summary>How much of its overseer's bandwidth this mech occupies.</summary>
        internal static int BandwidthCost(Pawn mech)
        {
            if (mech == null)
                return 0;

            return UIGuard.Try("Mechs.BandwidthCost",
                () => Mathf.RoundToInt(mech.GetStatValue(StatDefOf.BandwidthCost)), 0, null);
        }

        /// <summary>The group's own name for this mech, which nothing outside the group gizmo has ever shown.</summary>
        internal static string Tag(Pawn mech)
        {
            if (mech == null)
                return null;

            return UIGuard.Try<string>("Mechs.Tag", () =>
            {
                MechanitorControlGroup group = mech.GetMechControlGroup();

                return group == null ? null : group.GetTag(mech);
            }, null, null);
        }

        // -------------------------------------------------------------------------------------------
        // Energy
        // -------------------------------------------------------------------------------------------

        private static Need_MechEnergy Energy(Pawn mech)
        {
            if (mech == null || mech.needs == null)
                return null;

            return UIGuard.Try<Need_MechEnergy>("Mechs.EnergyNeed",
                () => mech.needs.TryGetNeed<Need_MechEnergy>(), null, null);
        }

        /// <summary>Charge as a fraction, or -1 when this mech has no energy need at all.</summary>
        internal static float Charge(Pawn mech)
        {
            Need_MechEnergy need = Energy(mech);

            if (need == null || need.MaxLevel <= 0f)
                return -1f;

            return Mathf.Clamp01(need.CurLevel / need.MaxLevel);
        }

        /// <summary>Charge as a whole percent, for the figure beside the bar.</summary>
        internal static string ChargeText(Pawn mech)
        {
            float charge = Charge(mech);

            return charge < 0f ? "-" : Mathf.RoundToInt(charge * 100f) + "%";
        }

        /// <summary>How fast charge is falling, per day. Negative while a mech is gaining.</summary>
        internal static float FallPerDay(Pawn mech)
        {
            Need_MechEnergy need = Energy(mech);

            return need == null ? 0f : UIGuard.Try("Mechs.FallPerDay", () => need.FallPerDay, 0f, null);
        }

        /// <summary>
        /// Which way the charge is going, in the same four states a battery uses.
        ///
        /// <b>Through <see cref="ChargePill"/>'s own vocabulary rather than a second one.</b> A mech is a
        /// battery that walks, and the power tab and the inspect pane already say charging, draining, full and
        /// empty about exactly this. Two vocabularies for one fact is how they drift.
        /// </summary>
        internal static ChargeFlow Flow(Pawn mech)
        {
            Need_MechEnergy need = Energy(mech);

            if (need == null)
                return ChargeFlow.Empty;

            // Negated: the need reports a fall rate, and the pill wants a gain. Passing FallPerDay straight
            // through would call a draining mech charged and a charging one drained.
            return ChargePill.Flow(need.CurLevel, need.MaxLevel, -FallPerDay(mech));
        }

        /// <summary>
        /// Days until this mech runs out of charge, or -1 when it is gaining or standing still.
        ///
        /// The arithmetic nobody should have to do: the game holds the level and the rate and has never
        /// divided one by the other. Measured to empty rather than to the shutdown line, because a mech that
        /// shuts down has not finished being a problem.
        /// </summary>
        internal static float DaysToEmpty(Pawn mech)
        {
            Need_MechEnergy need = Energy(mech);

            if (need == null)
                return -1f;

            float fall = FallPerDay(mech);

            if (fall <= 0.01f)
                return -1f;

            return need.CurLevel / fall;
        }

        /// <summary>Whether this mech is sitting in our own hibernation job right now.</summary>
        internal static bool Hibernating(Pawn mech)
        {
            if (mech == null || mech.CurJobDef == null)
                return false;

            return mech.CurJobDef == MechDefOf.Gideon_MechHibernate;
        }

        // -------------------------------------------------------------------------------------------
        // Condition
        // -------------------------------------------------------------------------------------------

        /// <summary>Overall integrity as a fraction, which is the health summary under another name.</summary>
        internal static float Integrity(Pawn mech)
        {
            if (mech == null || mech.health == null || mech.health.summaryHealth == null)
                return 1f;

            return UIGuard.Try("Mechs.Integrity",
                () => Mathf.Clamp01(mech.health.summaryHealth.SummaryHealthPercent), 1f, null);
        }

        /// <summary>
        /// The injuries this mech is carrying, worst first, as "part: what happened to it".
        ///
        /// <b>Listed rather than summed.</b> A mech at 78 percent with a cut torso and one with a destroyed
        /// leg are different problems and only one of them limps.
        /// </summary>
        internal static void DamagedParts(Pawn mech, List<string> into, int most = 4)
        {
            into.Clear();

            if (mech == null || mech.health == null || mech.health.hediffSet == null)
                return;

            UIGuard.Try("Mechs.DamagedParts", () =>
            {
                List<Hediff> hediffs = mech.health.hediffSet.hediffs;

                for (int i = 0; hediffs != null && i < hediffs.Count && into.Count < most; i++)
                {
                    Hediff hediff = hediffs[i];

                    if (hediff == null || !(hediff is Hediff_Injury))
                        continue;

                    string part = hediff.Part == null
                        ? mech.LabelShortCap
                        : hediff.Part.LabelCap.ToString();

                    into.Add(part + ": " + hediff.LabelCap);
                }
            }, null);
        }

        /// <summary>The repair comp, which owns the auto repair flag the tab's checkbox writes.</summary>
        internal static CompMechRepairable Repairable(Pawn mech)
        {
            if (mech == null)
                return null;

            return UIGuard.Try<CompMechRepairable>("Mechs.Repairable",
                () => mech.GetComp<CompMechRepairable>(), null, null);
        }

        // -------------------------------------------------------------------------------------------
        // Work
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The work types this mech may be given, in the order its def lists them.
        ///
        /// <b>Not the work tab's visible list.</b> <c>Pawn.GetDisabledWorkTypes</c> disables every type that
        /// is not in <c>mechEnabledWorkTypes</c>, and <c>Pawn_WorkSettings.SetPriority</c> logs an error if
        /// asked to set a non-zero priority on a disabled one. Building the grid from the def means a lifter
        /// shows one card instead of two dozen with all but one struck through, and means no click can ever
        /// reach a disabled type.
        /// </summary>
        internal static List<WorkTypeDef> WorkTypes(Pawn mech)
        {
            if (mech == null || mech.RaceProps == null || mech.RaceProps.mechEnabledWorkTypes == null)
                return null;

            return mech.RaceProps.mechEnabledWorkTypes;
        }

        /// <summary>
        /// A mech's skill at everything it does, which is one number on its def.
        ///
        /// <c>WorkPanel.SkillColor</c> answers with an empty label for any pawn whose <c>skills</c> is null,
        /// and that is every mech. This is what the card's subtitle says instead.
        /// </summary>
        internal static int FixedSkill(Pawn mech)
        {
            return mech == null || mech.RaceProps == null ? 0 : mech.RaceProps.mechFixedSkillLevel;
        }

        /// <summary>Whether this mech has work settings we can read and write.</summary>
        internal static bool HasWorkSettings(Pawn mech)
        {
            return mech != null && mech.workSettings != null && mech.workSettings.EverWork;
        }

        /// <summary>
        /// This mech's priority for one work type, or 0 when it cannot be read.
        ///
        /// <b>The stored number, always.</b> <c>Pawn_WorkSettings.GetPriority</c> flattens a non-zero priority
        /// to 3 when the player has manual priorities switched off, and the guard on that is
        /// <c>RaceProps.Humanlike</c>. Mech priorities are therefore permanently manual whatever the work
        /// tab's checkbox says, which is why this tab never draws the on/off box the pawns tab falls back to.
        /// </summary>
        internal static int PriorityOf(Pawn mech, WorkTypeDef work)
        {
            if (!HasWorkSettings(mech) || work == null)
                return 0;

            return UIGuard.Try("Mechs.Priority", () => mech.workSettings.GetPriority(work), 0, null);
        }

        /// <summary>Writes a priority, through the game's own setter so its bookkeeping still runs.</summary>
        internal static void SetPriority(Pawn mech, WorkTypeDef work, int priority)
        {
            if (!HasWorkSettings(mech) || work == null)
                return;

            // Refused rather than clamped. SetPriority logs an error for a disabled work type and we should
            // never be asking: the grid is built from mechEnabledWorkTypes, so reaching here means a bug
            // upstream and swallowing it would hide it.
            if (mech.WorkTypeIsDisabled(work))
                return;

            UIGuard.Try("Mechs.SetPriority", () => mech.workSettings.SetPriority(work, priority), null);
        }

        /// <summary>
        /// Whether this mech's work priorities are actually running.
        ///
        /// The mech think tree gates <c>JobGiver_Work</c> behind a <c>ThinkNode_ConditionalWorkMode</c> for
        /// Work mode, so an escorting or recharging group ignores its numbers entirely. The deck says so
        /// rather than showing a live looking setting that does nothing.
        /// </summary>
        internal static bool PrioritiesLive(Pawn mech)
        {
            if (!IsWorkMech(mech))
                return false;

            return UIGuard.Try("Mechs.PrioritiesLive", () =>
            {
                MechWorkModeDef mode = mech.GetMechWorkMode();

                return mode == MechWorkModeDefOf.Work;
            }, false, null);
        }

    }
}
