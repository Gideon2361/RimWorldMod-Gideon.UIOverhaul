using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Study
{
    /// <summary>
    /// Keeps everybody except the assigned colonist away from an assigned entity, for both of the jobs an entity
    /// gives out.
    ///
    /// <b>Study and suppression, because they are the same question.</b> Study was what Aaron asked for on
    /// 2026-08-22; suppression came from reading Assignable Entity, which he named as the reference, and it is
    /// right: an entity on a platform is suppressed by whoever wanders past, and suppression is the interaction
    /// that puts a pawn next to the thing most often. Naming a studier and then having somebody else stand at the
    /// platform every few hours would have missed most of the point.
    ///
    /// <b>Filtered at each giver's <c>PotentialWorkThingsGlobal</c>.</b> For study that method is declared on
    /// <c>WorkGiver_StudyBase</c> and inherited by both the ordinary and the dark study givers, so one patch
    /// covers both and anything else derived from it. Suppression is a warden giver with no shared base, so it
    /// gets its own patch pointing at the same filter.
    ///
    /// <b>It is also the seam that gets the behavior right.</b> These methods answer "what could this pawn go and
    /// do on their own", which is exactly what an assignment should govern. A direct order from the player, right
    /// clicking the entity and prioritizing it, goes through <c>JobOnThing</c> with <c>forced</c> set and never
    /// consults these lists, so it still works. That is also how the reference mod behaves, in its own words:
    /// unassigned colonists will not study or suppress "unless directly instructed by you".
    ///
    /// <b>Platforms resolve to what they hold.</b> Both lists contain platforms as well as entities, and the
    /// assignment lives on the entity, so a platform entry is followed to its occupant before the lookup. Vanilla
    /// does the same thing in both givers, one method along.
    /// </summary>
    internal static class StudyWorkFilter
    {
        /// <summary>
        /// Drops the entities somebody else has been assigned to.
        ///
        /// <b>The source is handed straight back when the colony has assigned nobody,</b> which is nearly always,
        /// and that one test is what keeps this off the cost of a work scan. Once there is an assignment the list
        /// is walked once and copied: the source may be lazily produced, so reading it twice to avoid an
        /// allocation would be the more expensive mistake.
        /// </summary>
        internal static IEnumerable<Thing> Filter(IEnumerable<Thing> things, Pawn pawn)
        {
            if (things == null || pawn == null || !StudyAssignments.AnyAssigned)
                return things;

            List<Thing> kept = new List<Thing>();

            foreach (Thing thing in things)
            {
                if (StudyAssignments.Allowed(Entity(thing), pawn))
                    kept.Add(thing);
            }

            return kept;
        }

        /// <summary>
        /// What an entry in the list is really about: a platform stands for whoever is on it.
        ///
        /// An empty platform answers as itself, which is harmless, since an assignment is only ever recorded
        /// against something that had a studiable comp to draw the button from.
        /// </summary>
        private static Thing Entity(Thing thing)
        {
            if (!ModsConfig.AnomalyActive)
                return thing;

            Building_HoldingPlatform platform = thing as Building_HoldingPlatform;

            if (platform == null)
                return thing;

            return platform.HeldPawn ?? thing;
        }
    }

    /// <summary>Study, for both the ordinary and the dark study givers, which share this method.</summary>
    [HarmonyPatch(typeof(WorkGiver_StudyBase), nameof(WorkGiver_StudyBase.PotentialWorkThingsGlobal))]
    internal static class Patch_StudyAssignment
    {
        public static void Postfix(Pawn pawn, ref IEnumerable<Thing> __result)
        {
            IEnumerable<Thing> found = __result;

            __result = UIGuard.Try("Study.FilterStudy", () => StudyWorkFilter.Filter(found, pawn), found,
                "Study assignments are not being honored, so anybody may study anything.");
        }
    }

    /// <summary>
    /// Suppression, which is a warden job rather than a study one and shares nothing with the above but the
    /// method name.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_Warden_SuppressActivity),
        nameof(WorkGiver_Warden_SuppressActivity.PotentialWorkThingsGlobal))]
    internal static class Patch_SuppressAssignment
    {
        public static void Postfix(Pawn pawn, ref IEnumerable<Thing> __result)
        {
            IEnumerable<Thing> found = __result;

            __result = UIGuard.Try("Study.FilterSuppress", () => StudyWorkFilter.Filter(found, pawn), found,
                "Suppression assignments are not being honored, so anybody may suppress anything.");
        }
    }
}
