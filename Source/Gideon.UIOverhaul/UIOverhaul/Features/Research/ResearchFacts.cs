using System.Collections.Generic;
using System.Text;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// Why a project cannot be started, or that it can.
    ///
    /// <b>Eight states, and vanilla draws six of them as the same grey box.</b> Each has a different answer -- go
    /// build something, go study something, go buy something, go finish something else -- and the node says which
    /// without being opened.
    /// </summary>
    internal enum ResearchState
    {
        Ready,

        Researching,

        Finished,

        /// <summary>A prerequisite is unfinished. The commonest one, and the only one that is about the graph.</summary>
        Prerequisite,

        /// <summary>No bench of the right kind, or one without the facilities this project wants.</summary>
        Bench,

        /// <summary>Techprints bought so far, out of the number needed. A count, not a flag.</summary>
        Techprint,

        /// <summary>Biotech's mechanitor requirement. Nothing done at a bench will help.</summary>
        Mechanitor,

        /// <summary>Biotech's analysis gate: something has to be studied first.</summary>
        Analyze,

        /// <summary>Odyssey's grav engine inspection.</summary>
        Inspect,

        /// <summary>Anomaly's undiscovered research. The one state whose whole node is masked.</summary>
        Unknown,

        /// <summary>Hidden by the difficulty settings. Drawn as a ghost rather than left out.</summary>
        Ghost
    }

    /// <summary>
    /// What the tab needs to know about one project, worked out from the game rather than stored.
    ///
    /// <b>Read fresh every frame and deliberately not cached.</b> Every field here can change while the tab is
    /// open -- a techprint is applied, a bench is built, a researcher finishes -- and a cache would need
    /// invalidating from all of those. None of it is expensive except the bench test, which walks the colony's
    /// buildings, and that one is cached for a few frames in <see cref="ResearchBenches"/>.
    ///
    /// <b>Nothing here may move a node.</b> The layout's signature is built from the def list and the fonts alone,
    /// so a project that becomes available does not reflow the graph.
    /// </summary>
    internal static class ResearchFacts
    {
        /// <summary>
        /// Which of the eight states a project is in, in the order that answers "what do I do about it".
        ///
        /// Finished and researching come first because they are facts about the colony rather than obstacles.
        /// Unknown comes before every lock, since a project whose name is a secret cannot be told to go and do
        /// something. After that the order is roughly cheapest-to-fix first.
        /// </summary>
        private static readonly Dictionary<ResearchProjectDef, ResearchState> states =
            new Dictionary<ResearchProjectDef, ResearchState>();

        private static int statesFrame = -1;

        /// <summary>
        /// Memoized for the frame, which is not an optimization so much as a repair.
        ///
        /// Three of the eight tests are expensive and two of them are startling: <c>IsHidden</c> walks every
        /// <c>EntityCodexEntryDef</c> in the game looking for one that mentions this project, and
        /// <c>PlayerMechanitorRequirementMet</c> walks the colony's pawns. The panel asks for a node's state four
        /// times in a frame -- to draw it, to filter it, for its chip and for its tooltip -- so without this the
        /// same two walks happen four times per visible node per frame.
        ///
        /// One frame and no longer. Everything here can change between frames and a stale state would be a node
        /// that stays grey after the techprint arrives.
        /// </summary>
        internal static ResearchState StateOf(ResearchNode node)
        {
            if (node == null || node.Project == null)
                return ResearchState.Ghost;

            if (statesFrame != Time.frameCount)
            {
                states.Clear();
                statesFrame = Time.frameCount;
            }

            ResearchState known;

            if (states.TryGetValue(node.Project, out known))
                return known;

            known = Measure(node);
            states[node.Project] = known;

            return known;
        }

        private static ResearchState Measure(ResearchNode node)
        {
            ResearchProjectDef project = node.Project;

            if (project.IsFinished)
                return ResearchState.Finished;

            if (Find.ResearchManager != null && Find.ResearchManager.IsCurrentProject(project))
                return ResearchState.Researching;

            if (ResearchMask.Masked(project))
                return ResearchState.Unknown;

            if (node.Ghost)
                return ResearchState.Ghost;

            if (!project.PrerequisitesCompleted)
                return ResearchState.Prerequisite;

            if (!project.TechprintRequirementMet)
                return ResearchState.Techprint;

            if (!ResearchBenches.Satisfied(project))
                return ResearchState.Bench;

            if (!project.PlayerMechanitorRequirementMet)
                return ResearchState.Mechanitor;

            if (!project.AnalyzedThingsRequirementsMet)
                return ResearchState.Analyze;

            if (!project.InspectionRequirementsMet)
                return ResearchState.Inspect;

            return ResearchState.Ready;
        }

        /// <summary>
        /// The chip on the node: the one thing to go and do, in as few words as it takes.
        ///
        /// Null for the states a node already says by how it is drawn. A finished project has a tick and a
        /// researching one has a lit border and a progress bar; a chip repeating either would be the only text on
        /// the node that told it nothing.
        /// </summary>
        internal static string ChipFor(ResearchNode node, ResearchState state)
        {
            ResearchProjectDef project = node?.Project;

            if (project == null)
                return null;

            switch (state)
            {
                case ResearchState.Ready:
                case ResearchState.Finished:
                case ResearchState.Researching:
                    return null;

                case ResearchState.Unknown:
                    // The one readable thing on a masked node. Left in as the mockup had it; whether it survives
                    // is the question that was raised there and has not been answered.
                    return "Not yet understood";

                case ResearchState.Ghost:
                    return "Hidden by your difficulty settings";

                case ResearchState.Prerequisite:
                    return Missing(project);

                case ResearchState.Techprint:
                    return project.TechprintsApplied + " of " + project.TechprintCount + " techprints";

                case ResearchState.Bench:
                    return "Needs " + BenchName(project);

                case ResearchState.Mechanitor:
                    return "Needs a mechanitor";

                case ResearchState.Analyze:
                    return "Analyze " + Analyzable(project);

                case ResearchState.Inspect:
                    return "Inspect a grav engine";

                default:
                    return null;
            }
        }

        /// <summary>
        /// The picture that goes on a node for one state, or null when the node should say nothing.
        ///
        /// <b>Null for a missing prerequisite, which is the important one.</b> That state is what the arrows into
        /// a node already say, and it is far and away the commonest: putting a chip on it meant almost every node
        /// on the canvas carried a truncated "Needs Intermedi..." restating its own incoming arrow. Removing it is
        /// what lets the five rare states -- the ones a player genuinely cannot work out from the graph -- read at
        /// a glance instead of drowning.
        ///
        /// Null for Ready, Researching and Finished too: how the node is drawn already says all three.
        /// </summary>
        internal static Texture2D IconFor(ResearchState state)
        {
            switch (state)
            {
                case ResearchState.Bench:
                    return ResearchGlyphs.Bench;

                case ResearchState.Analyze:
                case ResearchState.Inspect:
                    return ResearchGlyphs.Eye;

                case ResearchState.Mechanitor:
                    return ResearchGlyphs.Cross;

                case ResearchState.Techprint:
                    return ResearchGlyphs.Plus;

                default:
                    return null;
            }
        }

        /// <summary>
        /// The two or three characters that go beside the icon, or null.
        ///
        /// Only the techprint state has one, and it is the reason that state gets numbers rather than a flag:
        /// <b>2/3</b> tells you whether to keep buying and a warning triangle does not.
        /// </summary>
        internal static string MarkFor(ResearchState state, ResearchProjectDef project)
        {
            if (state != ResearchState.Techprint || project == null)
                return null;

            return project.TechprintsApplied + "/" + project.TechprintCount;
        }

        /// <summary>The colour a chip and a node's stripe take for one state.</summary>
        internal static Color ColorFor(ResearchState state, UIColorPaletteDef palette,
            KnowledgeCategoryDef knowledge)
        {
            switch (state)
            {
                case ResearchState.Finished:
                    return palette.Success;

                case ResearchState.Researching:
                    return knowledge != null ? palette.Mood : palette.Accent;

                case ResearchState.Ready:
                    return palette.Success;

                case ResearchState.Unknown:
                    return palette.Mood;

                case ResearchState.Bench:
                    return palette.Warning;

                case ResearchState.Mechanitor:
                    return palette.Danger;

                case ResearchState.Ghost:
                    return palette.TextDisabled;

                default:
                    return palette.TextSecondary;
            }
        }

        /// <summary>
        /// The first unfinished prerequisite, or a count when there is more than one.
        ///
        /// Named when there is one because a name is something to go and find on the canvas; counted when there
        /// are several because a list of three names does not fit on a node and picking one of them arbitrarily
        /// would send somebody after the wrong project.
        /// </summary>
        private static string Missing(ResearchProjectDef project)
        {
            ResearchProjectDef only = null;
            int count = 0;

            count += Count(project.prerequisites, ref only);
            count += Count(project.hiddenPrerequisites, ref only);

            if (count == 0)
                return "Needs an earlier project";

            if (count == 1)
                return "Needs " + Name(only);

            return "Needs " + count + " earlier projects";
        }

        private static int Count(List<ResearchProjectDef> list, ref ResearchProjectDef only)
        {
            if (list == null)
                return 0;

            int found = 0;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || list[i].IsFinished)
                    continue;

                found++;

                if (only == null)
                    only = list[i];
            }

            return found;
        }

        /// <summary>
        /// A project's name, masked if that project is one of the unknown ones.
        ///
        /// <b>This is why the mask is not only about the node it belongs to.</b> A readable project whose
        /// prerequisite is an unknown one would otherwise print the secret name in its own chip.
        /// </summary>
        internal static string Name(ResearchProjectDef project)
        {
            if (project == null)
                return "something";

            return ResearchMask.Masked(project) ? "an unknown project" : project.LabelCap.ToString();
        }

        private static string BenchName(ResearchProjectDef project)
        {
            if (project.requiredResearchBuilding != null)
                return project.requiredResearchBuilding.label;

            if (!project.requiredResearchFacilities.NullOrEmpty())
                return project.requiredResearchFacilities[0].label;

            return "a research bench";
        }

        private static string Analyzable(ResearchProjectDef project)
        {
            return project.requiredAnalyzed.NullOrEmpty() ? "something" : project.requiredAnalyzed[0].label;
        }

        /// <summary>
        /// The whole story for the hover tooltip: what it is, what it needs and what it costs.
        ///
        /// <b>Ours rather than <c>GetTip</c>.</b> Vanilla's tip is the description plus a techprint line, and it
        /// caches the description with the mod's name appended -- which is worth having, so it is included. What
        /// it does not say is which of the eight things is missing, and that is the whole point of this tab.
        /// </summary>
        internal static string TooltipFor(ResearchNode node, ResearchState state)
        {
            ResearchProjectDef project = node?.Project;

            if (project == null)
                return null;

            if (state == ResearchState.Unknown)
                return "Unknown research.\n\nSomething is here, and what it is will not be legible until the "
                       + "entity that explains it has been discovered and studied.";

            StringBuilder text = new StringBuilder();

            text.Append(project.LabelCap.ToString());

            if (project.knowledgeCategory != null)
                text.Append("\n").Append(project.knowledgeCategory.LabelCap.ToString()).Append(" knowledge ")
                    .Append(project.knowledgeCost.ToString("F0"));
            else
                text.Append("\n").Append(project.techLevel.ToStringHuman()).Append(" - ")
                    .Append(project.CostApparent.ToString("F0")).Append(" points");

            if (!project.description.NullOrEmpty())
                text.Append("\n\n").Append(project.description);

            string chip = ChipFor(node, state);

            if (chip != null)
                text.Append("\n\n").Append(chip);

            if (state == ResearchState.Bench && !project.requiredResearchFacilities.NullOrEmpty())
            {
                text.Append("\n").Append("With: ");

                for (int i = 0; i < project.requiredResearchFacilities.Count; i++)
                {
                    if (i > 0)
                        text.Append(", ");

                    text.Append(project.requiredResearchFacilities[i].label);
                }
            }

            if (project.modContentPack != null && !project.modContentPack.IsCoreMod)
                text.Append("\n\n").Append(project.modContentPack.Name);

            return text.ToString();
        }
    }

    /// <summary>
    /// Whether the colony has a bench that can run a project, answered without walking the map every frame.
    ///
    /// <b>Cached for a second, which is the one measurement on this tab worth caching.</b>
    /// <c>PlayerHasAnyAppropriateResearchBench</c> walks every colonist building on every loaded map and, for each
    /// bench, every facility linked to it. The research tab asks it once per node, so a few hundred times a frame:
    /// on a large colony with a gravship that is the difference between a tab that opens and one that stutters.
    ///
    /// <b>Keyed by project, because the answer is per project.</b> A hi-tech bench satisfies one project and not
    /// another; the same walk with a different question gives a different answer.
    /// </summary>
    internal static class ResearchBenches
    {
        /// <summary>How long an answer is trusted. Sixty frames, not sixty ticks: this is a drawing cache.</summary>
        private const int Frames = 60;

        private static readonly Dictionary<ResearchProjectDef, bool> answers =
            new Dictionary<ResearchProjectDef, bool>();

        private static int stamped = -1;

        internal static bool Satisfied(ResearchProjectDef project)
        {
            if (project == null || project.requiredResearchBuilding == null)
                return true;

            if (Time.frameCount - stamped > Frames)
            {
                answers.Clear();
                stamped = Time.frameCount;
            }

            bool known;

            if (answers.TryGetValue(project, out known))
                return known;

            known = UIGuard.Try("Research.BenchTest", () => project.PlayerHasAnyAppropriateResearchBench, true,
                null);

            answers[project] = known;

            return known;
        }

        /// <summary>Drops every answer, for when something has certainly changed.</summary>
        internal static void Invalidate()
        {
            answers.Clear();
            stamped = -1;
        }
    }
}
