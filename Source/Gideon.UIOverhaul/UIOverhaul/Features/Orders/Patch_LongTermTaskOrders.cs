using System;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Orders
{
    /// <summary>
    /// Lets a right click prioritize research, which vanilla refuses on the grounds that it is a long-term task.
    ///
    /// <b>What vanilla does.</b> <c>FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption</c> builds every work
    /// order in the menu through one long chain of tests, and one link of that chain is
    /// <c>job.def == JobDefOf.Research &amp;&amp; target.Thing is Building_ResearchBench</c>, which turns the
    /// option into a dead grey line reading "Research is a long-term task". The reasoning is sound in the general
    /// case: a prioritized job holds the pawn until it finishes or is interrupted, and research finishes when the
    /// project does, which could be days. It is still the player's call, and Aaron asked for it on 2026-08-22.
    ///
    /// <b>A postfix that rebuilds the option rather than a transpiler that removes the test.</b> The test is one
    /// <c>else if</c> in the middle of a chain of eight, and an IL edit there would be reading branch targets that
    /// shift with every hotfix. The postfix sees only what the method returned, works out whether the refusal it is
    /// holding is the research one, and if so builds the option vanilla would have built had that link not been
    /// there. Nothing else in the chain is touched, so a bench that is forbidden, unreachable, or in a work type
    /// the pawn will never do keeps saying exactly that.
    ///
    /// <b>The reason is identified by recomputing it, not by reading the label.</b> The label is translated, so
    /// matching on "long-term" would work in English and quietly stop working in every other language. Instead
    /// every test that sits <i>before</i> the research link is asked again, and if all of them pass while the
    /// option is still disabled, the research link is the only thing that can have disabled it.
    ///
    /// <b>The tests that sit after it are asked too, and honored.</b> Forbidden, outside the allowed area and no
    /// path all come later in vanilla's chain, so with the research link gone they are what would have refused
    /// instead. Enabling the option without checking them would order a pawn to walk to a bench they cannot
    /// legally reach, and the job would be dropped on arrival with no explanation.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), "GetWorkGiverOption")]
    internal static class Patch_LongTermTaskOrders
    {
        public static void Postfix(Pawn pawn, WorkGiverDef workGiver, LocalTargetInfo target,
            FloatMenuContext context, ref FloatMenuOption __result)
        {
            FloatMenuOption result = __result;

            FloatMenuOption enabled = UIGuard.Try("Orders.PrioritizeResearch",
                () => Enable(pawn, workGiver, target, context, result), null,
                "Research cannot be prioritized from the right click menu, which is RimWorld's own behavior.");

            if (enabled != null)
                __result = enabled;
        }

        /// <summary>
        /// The working option, or null to leave vanilla's answer alone.
        ///
        /// Null is returned for every case that is not the one refusal being overridden, which is nearly all of
        /// them: this method runs for every work giver on every right click.
        /// </summary>
        private static FloatMenuOption Enable(Pawn pawn, WorkGiverDef workGiver, LocalTargetInfo target,
            FloatMenuContext context, FloatMenuOption result)
        {
            // Disabled is the only interesting state. An option with an action is one vanilla is already happy
            // with, and a null result means the giver was not offered at all.
            if (result == null || !result.Disabled || pawn == null || context == null)
                return null;

            if (!target.HasThing || !(target.Thing is Building_ResearchBench))
                return null;

            WorkGiver_Scanner scanner = workGiver?.Worker as WorkGiver_Scanner;

            if (scanner == null)
                return null;

            Job job = scanner.HasJobOnThing(pawn, target.Thing, true)
                ? scanner.JobOnThing(pawn, target.Thing, true)
                : null;

            // No job means the refusal came from JobFailReason rather than from the chain, and that reason is a
            // real one: no project selected, the bench is being used, or the pawn cannot do intellectual work.
            if (job == null || job.def != JobDefOf.Research)
                return null;

            if (!EarlierTestsPass(pawn, scanner, job))
                return null;

            if (!LaterTestsPass(pawn, target, scanner))
                return null;

            return Option(pawn, workGiver, target, context, scanner, job);
        }

        /// <summary>
        /// The links of vanilla's chain that come before the research one.
        ///
        /// If any of these fails, the message on the option is that failure rather than the long-term one, and it
        /// must be left where it is. They are in vanilla's own order, which is also cheapest first.
        /// </summary>
        private static bool EarlierTestsPass(Pawn pawn, WorkGiver_Scanner scanner, Job job)
        {
            if (scanner.MissingRequiredCapacity(pawn) != null)
                return false;

            if (pawn.WorkTagIsDisabled(scanner.def.workTags))
                return false;

            // Already doing this exact job. Vanilla says "already researching at", which is worth keeping.
            if (pawn.jobs?.curJob != null && pawn.jobs.curJob.JobIsSameAs(pawn, job))
                return false;

            WorkTypeDef workType = scanner.def.workType;

            if (workType == null || pawn.workSettings == null)
                return false;

            // Priority zero covers both "never does this" and "not assigned to it", which vanilla reports as two
            // different messages. Either way the pawn is not going to research and the refusal stands.
            return pawn.workSettings.GetPriority(workType) != 0;
        }

        /// <summary>
        /// The links that come after the research one, which would have refused in its place.
        ///
        /// Left disabled rather than reworded when one of these fails: the message will say the wrong thing, since
        /// it is still vanilla's long-term one, but the option is correctly dead. Rewriting it would mean
        /// reproducing four more translated strings for a case a player reaches by right clicking a bench they
        /// have forbidden.
        /// </summary>
        private static bool LaterTestsPass(Pawn pawn, LocalTargetInfo target, WorkGiver_Scanner scanner)
        {
            if (target.Thing.IsForbidden(pawn))
                return false;

            return pawn.CanReach(target.Thing, scanner.PathEndMode, Danger.Deadly);
        }

        /// <summary>
        /// The option vanilla builds for any other prioritizable job, built here for this one.
        ///
        /// <b>Through <c>TryTakeOrderedJobPrioritizedWork</c>, which is the point.</b> That is what records the
        /// work giver and the cell on the pawn's <c>priorityWork</c>, so the order survives the pawn finishing one
        /// bench interaction and looking for the next thing to do. A plain ordered job would be dropped the moment
        /// the think tree ran again.
        ///
        /// The mote and fleck are carried over because the work giver def may name them and a player who has one
        /// configured is entitled to see it here too.
        /// </summary>
        private static FloatMenuOption Option(Pawn pawn, WorkGiverDef workGiver, LocalTargetInfo target,
            FloatMenuContext context, WorkGiver_Scanner scanner, Job job)
        {
            string label = "PrioritizeGeneric"
                .Translate(scanner.PostProcessedGerund(job), target.Thing.Label).CapitalizeFirst();

            job.workGiverDef = scanner.def;

            IntVec3 cell = context.ClickedCell;

            Action action = UIGuard.Wrap("Orders.TakeResearchJob", () =>
            {
                if (!pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, scanner, cell))
                    return;

                if (workGiver.forceMote != null)
                    MoteMaker.MakeStaticMote(cell, pawn.Map, workGiver.forceMote);

                if (workGiver.forceFleck != null)
                    FleckMaker.Static(cell, pawn.Map, workGiver.forceFleck);
            });

            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, action), pawn, target);
        }
    }
}
