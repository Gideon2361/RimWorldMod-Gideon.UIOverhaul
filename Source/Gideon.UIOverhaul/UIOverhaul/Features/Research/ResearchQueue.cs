using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// The colony's research plan: what to work on after this.
    ///
    /// <b>RimWorld has no queue at all.</b> <c>ResearchManager</c> holds one private <c>currentProj</c> field, so
    /// a finished project is a prompt to stop what you were doing and go back to the research screen. This is a
    /// list of ours, and everything else in the feature is bookkeeping around it.
    ///
    /// <b>It lives in the save, not in the settings.</b> A research plan is about this colony and means nothing in
    /// another one, which is the same test the pawn groups pass and the music library fails.
    ///
    /// <b>An empty queue changes nothing.</b> Nothing is patched, nothing is set, and RimWorld behaves exactly as
    /// it does today. The queue only ever acts when the game has no current project and the queue has one that can
    /// start.
    ///
    /// <b>A blocked entry stays, and is skipped rather than dropped.</b> Fabrication waiting on a techprint you
    /// are trying to buy is precisely the thing worth remembering; dropping it would punish the player for
    /// planning ahead. So the advance takes the first entry that can start, not the head.
    ///
    /// <b>Anomaly runs alongside.</b> <c>CurrentAnomalyKnowledgeProjects</c> holds one project per knowledge
    /// category, concurrent with the main one and paid for in knowledge rather than researcher time, so those
    /// entries never compete with the rest of the queue and are advanced separately.
    /// </summary>
    public class GameComponent_ResearchQueue : GameComponent
    {
        /// <summary>How often the queue looks at the game. Once a second is far more often than it can matter.</summary>
        private const int IntervalTicks = 60;

        private List<ResearchProjectDef> queue = new List<ResearchProjectDef>();

        private int sinceCheck;

        /// <summary>Required by RimWorld: every GameComponent is constructed with the game it belongs to.</summary>
        public GameComponent_ResearchQueue(Game game)
        {
        }

        internal static GameComponent_ResearchQueue Current
        {
            get { return Verse.Current.Game?.GetComponent<GameComponent_ResearchQueue>(); }
        }

        internal List<ResearchProjectDef> Entries
        {
            get { return queue ?? (queue = new List<ResearchProjectDef>()); }
        }

        internal int Count
        {
            get { return Entries.Count; }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref queue, "gideonResearchQueue", LookMode.Def);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            if (queue == null)
                queue = new List<ResearchProjectDef>();
            else
                queue.RemoveAll(project => project == null);
        }

        /// <summary>
        /// Guarded whole, because a tick is a boundary RimWorld does not wrap.
        ///
        /// The counter is ours rather than a modulo of <c>TicksGame</c>, so the interval survives a save loaded
        /// mid-second and does not fire on the same tick as every other component doing the same arithmetic.
        /// </summary>
        public override void GameComponentTick()
        {
            if (++sinceCheck < IntervalTicks)
                return;

            sinceCheck = 0;

            UIGuard.Try("Research.QueueTick", Advance,
                "The research queue stopped advancing. Whatever your colony is researching now carries on, and "
                + "the next project can be set by hand from the research tab.");
        }

        /// <summary>Where this project sits in the queue, counting from one, or zero when it is not in it.</summary>
        internal int PlaceOf(ResearchProjectDef project)
        {
            if (project == null)
                return 0;

            return Entries.IndexOf(project) + 1;
        }

        internal bool Contains(ResearchProjectDef project)
        {
            return project != null && Entries.Contains(project);
        }

        /// <summary>Adds one project to the end, if it is not already somewhere in the queue.</summary>
        internal void Add(ResearchProjectDef project)
        {
            if (project == null || project.IsFinished || Contains(project))
                return;

            Entries.Add(project);
        }

        internal void Remove(ResearchProjectDef project)
        {
            if (project == null)
                return;

            Entries.Remove(project);

            // Taking the current project out of the queue stops it as well. The queue is the plan, and an entry
            // removed from the plan that carried on being worked on would be a lie about what the colony is
            // doing -- and the player has no other control that stops a project.
            if (Find.ResearchManager != null && Find.ResearchManager.IsCurrentProject(project))
                Find.ResearchManager.StopProject(project);
        }

        internal void Clear()
        {
            Entries.Clear();
        }

        /// <summary>
        /// Puts a project at the head and starts it now.
        ///
        /// Always dependency-safe: a project that can start has every prerequisite finished, and a finished
        /// project is pruned from the queue, so nothing that was ahead of it can depend on it.
        /// </summary>
        internal void StartNow(ResearchProjectDef project)
        {
            if (project == null || Find.ResearchManager == null)
                return;

            Entries.Remove(project);
            Entries.Insert(0, project);

            Find.ResearchManager.SetCurrentProject(project);
        }

        /// <summary>
        /// Moves an entry, refusing an order that would break a dependency.
        ///
        /// <b>Refused with a reason rather than silently corrected.</b> Quietly reshuffling a drag would leave
        /// somebody dragging the same row twice, wondering why it will not stay -- and the reason it will not is
        /// the one useful thing in the interaction.
        /// </summary>
        internal bool Move(int from, int to, out string refusal)
        {
            refusal = null;

            List<ResearchProjectDef> list = Entries;

            if (from < 0 || from >= list.Count)
                return false;

            to = Mathf.Clamp(to, 0, list.Count - 1);

            if (to == from)
                return false;

            ResearchProjectDef moving = list[from];

            list.RemoveAt(from);
            list.Insert(to, moving);

            ResearchProjectDef before;
            ResearchProjectDef after;

            if (Ordered(list, out before, out after))
                return true;

            // Put back exactly as it was. Repairing the order here instead would be the silent correction the
            // whole method exists to avoid.
            list.RemoveAt(to);
            list.Insert(from, moving);

            refusal = ResearchFacts.Name(after) + " needs " + ResearchFacts.Name(before) + " first.";

            return false;
        }

        /// <summary>
        /// Whether every entry sits after the entries it depends on.
        ///
        /// Reports the offending pair rather than a bool alone, since the refusal message is the point.
        /// </summary>
        private static bool Ordered(List<ResearchProjectDef> list, out ResearchProjectDef before,
            out ResearchProjectDef after)
        {
            before = null;
            after = null;

            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (!DependsOn(list[i], list[j]))
                        continue;

                    before = list[j];
                    after = list[i];

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether <paramref name="project"/> needs <paramref name="other"/> finished first, however far back.
        ///
        /// Walked rather than read off <c>prerequisites</c> directly, because a chain of three is exactly the case
        /// a queue gets wrong: A needs B needs C, and C dragged below A is a queue that can never advance.
        /// </summary>
        internal static bool DependsOn(ResearchProjectDef project, ResearchProjectDef other)
        {
            if (project == null || other == null || project == other)
                return false;

            HashSet<ResearchProjectDef> seen = new HashSet<ResearchProjectDef>();

            return Walk(project, other, seen);
        }

        private static bool Walk(ResearchProjectDef project, ResearchProjectDef wanted,
            HashSet<ResearchProjectDef> seen)
        {
            if (project == null || !seen.Add(project))
                return false;

            if (Holds(project.prerequisites, wanted, seen))
                return true;

            return Holds(project.hiddenPrerequisites, wanted, seen);
        }

        private static bool Holds(List<ResearchProjectDef> list, ResearchProjectDef wanted,
            HashSet<ResearchProjectDef> seen)
        {
            if (list == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == wanted || Walk(list[i], wanted, seen))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Prunes what is done and sets what is next.
        ///
        /// The main lane and each knowledge category are advanced separately, because RimWorld runs them at the
        /// same time: setting an Anomaly project does not stop the industrial one and never should.
        /// </summary>
        private void Advance()
        {
            ResearchManager manager = Find.ResearchManager;

            if (manager == null)
                return;

            Entries.RemoveAll(project => project == null || project.IsFinished);

            if (Entries.Count == 0)
                return;

            if (manager.GetProject() == null)
            {
                ResearchProjectDef next = NextFor(null);

                if (next != null)
                    manager.SetCurrentProject(next);
            }

            if (!ModsConfig.AnomalyActive)
                return;

            List<ResearchManager.KnowledgeCategoryProject> lanes = manager.CurrentAnomalyKnowledgeProjects;

            if (lanes == null)
                return;

            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] == null || lanes[i].project != null)
                    continue;

                ResearchProjectDef next = NextFor(lanes[i].category);

                if (next != null)
                    manager.SetCurrentProject(next);
            }
        }

        /// <summary>
        /// The first entry that can start in one lane, or null.
        ///
        /// A null category means the main lane, which is what <c>GetProject</c> and <c>SetCurrentProject</c> both
        /// use to mean the same thing.
        /// </summary>
        private ResearchProjectDef NextFor(KnowledgeCategoryDef category)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                ResearchProjectDef project = Entries[i];

                if (project == null || project.knowledgeCategory != category || !project.CanStartNow)
                    continue;

                // The manager refuses a main-lane project with no base cost, so a def that is neither one thing
                // nor the other would sit at the head of the queue being silently ignored forever.
                if (category == null && !(project.baseCost > 0f))
                    continue;

                return project;
            }

            return null;
        }
    }
}
