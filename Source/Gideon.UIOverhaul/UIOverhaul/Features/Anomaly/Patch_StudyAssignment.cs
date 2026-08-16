using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Anomaly
{
    /// <summary>
    /// Shared pieces of the study assignment feature.
    ///
    /// <b>Everything here is switched off without Anomaly.</b> Each patch class carries
    /// <c>Prepare() =&gt; ModsConfig.AnomalyActive</c>, which Harmony reads before touching the target method, so
    /// with the DLC absent nothing is patched at all rather than patched and skipped. That matters more than it
    /// sounds: the types being patched are Anomaly types, and a patch attribute naming a type that does not
    /// exist throws while Harmony is resolving it.
    /// </summary>
    internal static class StudySubject
    {
        private static readonly CachedTexture Icon = new CachedTexture("UI/Icons/Study");

        /// <summary>
        /// The thing actually being studied, given whatever the work giver was handed.
        ///
        /// <b>This has to agree with the work giver exactly.</b> A holding platform is a valid study target, but
        /// the studied thing is the entity inside it, and <c>WorkGiver_DarkStudyInteract</c> resolves that itself
        /// before reading the comp. An assignment keyed on the platform while the gizmo keyed on the entity would
        /// simply never match, and the restriction would appear to be ignored.
        /// </summary>
        internal static Thing Resolve(Thing thing)
        {
            Building_HoldingPlatform platform = thing as Building_HoldingPlatform;

            return platform != null ? platform.HeldPawn : thing;
        }

        /// <summary>The colonists the player may choose between, or null when the map cannot be read.</summary>
        private static IEnumerable<Pawn> Candidates(Thing thing)
        {
            Map map = thing?.MapHeld;

            return map?.mapPawns?.FreeColonistsSpawned;
        }

        /// <summary>
        /// The command that opens the picker.
        ///
        /// Labelled with whoever is assigned, because a gizmo that reads the same whether or not a restriction is
        /// in force would have to be opened to answer the only question anybody asks of it.
        /// </summary>
        internal static Gizmo BuildCommand(CompStudiable comp)
        {
            if (comp?.parent == null || !comp.EverStudiable())
                return null;

            Thing thing = comp.parent;
            Pawn current = StudyAssignments.AssignedTo(thing);

            return new Command_Action
            {
                defaultLabel = current == null
                    ? "Studier: anyone"
                    : "Studier: " + current.LabelShortCap,
                defaultDesc = "Choose which colonist is allowed to study this.\n\n"
                              + "Assigning someone stops anybody else being offered the work. It does not order "
                              + "them to do it; they will pick it up through their own work priorities.",
                icon = Icon.Texture,
                action = () => Open(thing)
            };
        }

        private static void Open(Thing thing)
        {
            UIGuard.Try("Anomaly.StudyAssignment.Picker", () =>
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                Pawn current = StudyAssignments.AssignedTo(thing);

                options.Add(new FloatMenuOption("Anyone", () => StudyAssignments.Assign(thing, null))
                {
                    // Checked rather than removed, so the list always reads the same way and the current choice
                    // is visible in it.
                    Disabled = current == null
                });

                IEnumerable<Pawn> candidates = Candidates(thing);

                if (candidates != null)
                {
                    foreach (Pawn pawn in candidates)
                    {
                        if (pawn == null)
                            continue;

                        Pawn captured = pawn;

                        string label = pawn.LabelShortCap;
                        string blocked = Unable(pawn);

                        // Listed but disabled rather than left out, because "why is this colonist missing from
                        // the list" is a worse question than a greyed row saying what is wrong.
                        if (blocked != null)
                            label = label + " (" + blocked + ")";

                        options.Add(new FloatMenuOption(label,
                            () => StudyAssignments.Assign(thing, captured))
                        {
                            Disabled = blocked != null || captured == current
                        });
                    }
                }

                if (options.Count > 0)
                    Find.WindowStack.Add(new FloatMenu(options));
            }, "The studier could not be chosen. Study is unrestricted until one is set.");
        }

        /// <summary>Why this colonist could never take the job, or null if they could.</summary>
        private static string Unable(Pawn pawn)
        {
            WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail("DarkStudy");

            // Looked up by name rather than through a DefOf, because the work type ships with Anomaly. A DefOf
            // field for a def that is not loaded fails the whole static constructor it lives in.
            if (work == null)
                return null;

            if (pawn.WorkTypeIsDisabled(work))
                return "incapable";

            if (pawn.workSettings != null && pawn.workSettings.GetPriority(work) == 0)
                return "not assigned";

            return null;
        }
    }

    /// <summary>
    /// Hides the study job from everybody except the assigned colonist.
    ///
    /// <b>A postfix on the work giver rather than anything closer to the job.</b> This is the one place the game
    /// asks "may this pawn study this thing", and answering no here is indistinguishable from the dozen other
    /// reasons the answer is already no, so nothing downstream needs to know the feature exists. Patching the
    /// job driver instead would let the pawn walk over and then stop.
    ///
    /// Only ever turns an allowed job into a refused one, never the reverse, so no assignment can grant work that
    /// vanilla would have denied.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DarkStudyInteract), nameof(WorkGiver_DarkStudyInteract.HasJobOnThing))]
    internal static class Patch_StudyWorkGiver
    {
        public static bool Prepare() => ModsConfig.AnomalyActive;

        public static void Postfix(Pawn pawn, Thing t, ref bool __result)
        {
            if (!__result)
                return;

            bool refused = UIGuard.Try("Anomaly.StudyAssignment.Gate", () => Refused(pawn, t), false,
                "Study assignments are not being enforced; anybody may study any anomaly.");

            if (refused)
                __result = false;
        }

        private static bool Refused(Pawn pawn, Thing t)
        {
            Pawn owner = StudyAssignments.AssignedTo(StudySubject.Resolve(t));

            if (owner == null || owner == pawn)
                return false;

            // Said out loud rather than silently, because a colonist declining work with no reason given is the
            // single most confusing thing this feature could do.
            JobFailReason.Is("Assigned to " + owner.LabelShortCap);

            return true;
        }
    }

    /// <summary>
    /// Adds the assignment command to anything studiable.
    ///
    /// <b>On <c>CompStudiable</c> rather than on the holding platform,</b> so it appears wherever study does:
    /// entities on platforms, and the studiable items and structures that are never contained at all. The comp is
    /// what the work giver reads, so anything it appears on is something the restriction will actually govern.
    ///
    /// <b>Written as an iterator taking the original sequence,</b> which is how a postfix wraps a method that
    /// yields. Returning a new list instead would drop anything a later patch had added.
    /// </summary>
    [HarmonyPatch(typeof(CompStudiable), nameof(CompStudiable.CompGetGizmosExtra))]
    internal static class Patch_StudyGizmos
    {
        public static bool Prepare() => ModsConfig.AnomalyActive;

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, CompStudiable __instance)
        {
            foreach (Gizmo gizmo in values)
                yield return gizmo;

            Gizmo assign = UIGuard.Try("Anomaly.StudyAssignment.Gizmo",
                () => StudySubject.BuildCommand(__instance), null,
                "The studier cannot be chosen from this entity. Study is unrestricted.");

            if (assign != null)
                yield return assign;
        }
    }
}
