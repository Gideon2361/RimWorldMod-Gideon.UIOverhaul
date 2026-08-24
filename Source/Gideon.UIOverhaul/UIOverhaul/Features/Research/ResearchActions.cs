using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// The things a player can do to a project, and the one interesting question in the whole feature.
    ///
    /// <b>Queueing something unreachable offers the chain.</b> Ask for Fabrication with three prerequisites
    /// outstanding and the useful answer is not "no": it is the four projects in the order they have to happen,
    /// priced and timed together. Everything else here is plumbing around that.
    /// </summary>
    internal static class ResearchActions
    {
        /// <summary>
        /// Whether a project is something the player may act on at all.
        ///
        /// An unknown project is inert: it cannot be started, cannot be queued, and its detail panel offers no
        /// buttons. There is nothing to press on something you do not yet know exists. A ghost is inert for the
        /// opposite reason -- the difficulty settings have taken it out of this colony's game.
        /// </summary>
        internal static bool Actionable(ResearchNode node)
        {
            if (node?.Project == null || node.Ghost)
                return false;

            return !ResearchMask.Masked(node.Project) && !node.Project.IsFinished;
        }

        /// <summary>Starts a project now, putting it at the head of the queue so the plan agrees with the game.</summary>
        internal static void StartNow(ResearchProjectDef project)
        {
            GameComponent_ResearchQueue queue = GameComponent_ResearchQueue.Current;

            if (queue == null || project == null)
                return;

            queue.StartNow(project);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Queues a project, offering its unfinished chain when it has one.
        ///
        /// The offer is a window rather than a silent add-everything, because adding four projects when somebody
        /// asked for one is a surprise, and because the chain is worth reading: it is the answer to "why can I not
        /// research this yet".
        /// </summary>
        internal static void Queue(ResearchProjectDef project)
        {
            GameComponent_ResearchQueue queue = GameComponent_ResearchQueue.Current;

            if (queue == null || project == null)
                return;

            List<ResearchProjectDef> chain = Chain(project, queue);

            if (chain.Count <= 1)
            {
                queue.Add(project);
                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            Find.WindowStack.Add(new Dialog_ResearchChain(project, chain));
        }

        /// <summary>
        /// The unfinished projects that have to happen before this one, in dependency order, ending with it.
        ///
        /// <b>Depth first over the prerequisites, which gives the order for free.</b> A project is appended after
        /// everything it depends on, so walking the result top to bottom is a plan that never stalls.
        ///
        /// Anything already in the queue is left out: it is planned, and repeating it would make a chain of two
        /// look like a chain of five.
        /// </summary>
        internal static List<ResearchProjectDef> Chain(ResearchProjectDef project,
            GameComponent_ResearchQueue queue)
        {
            List<ResearchProjectDef> chain = new List<ResearchProjectDef>();
            HashSet<ResearchProjectDef> seen = new HashSet<ResearchProjectDef>();

            Collect(project, queue, chain, seen);

            return chain;
        }

        private static void Collect(ResearchProjectDef project, GameComponent_ResearchQueue queue,
            List<ResearchProjectDef> chain, HashSet<ResearchProjectDef> seen)
        {
            if (project == null || project.IsFinished || !seen.Add(project))
                return;

            Collect(project.prerequisites, queue, chain, seen);
            Collect(project.hiddenPrerequisites, queue, chain, seen);

            if (queue == null || !queue.Contains(project))
                chain.Add(project);
        }

        private static void Collect(List<ResearchProjectDef> list, GameComponent_ResearchQueue queue,
            List<ResearchProjectDef> chain, HashSet<ResearchProjectDef> seen)
        {
            if (list == null)
                return;

            for (int i = 0; i < list.Count; i++)
                Collect(list[i], queue, chain, seen);
        }

        /// <summary>The total days a run of projects will take, or a negative number when it cannot be said.</summary>
        internal static float DaysFor(List<ResearchProjectDef> chain)
        {
            float total = 0f;

            for (int i = 0; i < chain.Count; i++)
            {
                float days = ResearchRate.DaysFor(chain[i]);

                if (days < 0f)
                    return -1f;

                total += days;
            }

            return total;
        }
    }

    /// <summary>
    /// The offer made when a project needs other projects first.
    ///
    /// <b>Three answers, not two.</b> Add the whole chain, add only this one -- which is a legitimate thing to
    /// want, since a blocked entry sits in the queue and waits -- or change your mind. A yes/no dialog would have
    /// forced the second case into the first.
    /// </summary>
    public class Dialog_ResearchChain : Window
    {
        private const float RowHeight = 26f;

        private readonly ResearchProjectDef target;
        private readonly List<ResearchProjectDef> chain;

        private Vector2 scroll;

        internal Dialog_ResearchChain(ResearchProjectDef project, List<ResearchProjectDef> projects)
        {
            target = project;
            chain = projects;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            closeOnAccept = false;
            draggable = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                float rows = Mathf.Min(chain.Count, 10) * RowHeight;

                return new Vector2(460f, 148f + rows);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Research.ChainOffer", inRect, () => Contents(inRect),
                "This window failed to draw. Nothing has been added to the queue.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont font = Text.Font;
            Color color = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                    ResearchFacts.Name(target) + " needs " + (chain.Count - 1) + " more first");

                Text.Font = GameFont.Small;

                float y = inRect.y + 36f;
                float listHeight = inRect.height - 36f - 44f;

                Rect view = new Rect(inRect.x, y, inRect.width, listHeight);
                Rect inner = new Rect(0f, 0f, view.width - 18f, chain.Count * RowHeight);

                Widgets.BeginScrollView(view, ref scroll, inner);

                for (int i = 0; i < chain.Count; i++)
                {
                    Rect row = new Rect(0f, i * RowHeight, inner.width, RowHeight);

                    TabParts.RowLabel(new Rect(row.x + 4f, row.y, row.width - 74f, row.height),
                        (i + 1) + ".  " + ResearchFacts.Name(chain[i]),
                        chain[i] == target ? palette.TextPrimary : palette.TextSecondary);

                    TabParts.RowLabel(new Rect(row.xMax - 70f, row.y, 66f, row.height),
                        ResearchRate.Days(ResearchRate.DaysFor(chain[i])), palette.TextDisabled, GameFont.Tiny);
                }

                Widgets.EndScrollView();

                float total = ResearchActions.DaysFor(chain);

                GUI.color = palette.TextDisabled;
                Text.Font = GameFont.Tiny;

                Widgets.Label(new Rect(inRect.x, inRect.yMax - 40f, 200f, 20f),
                    total < 0f ? "" : "About " + ResearchRate.Days(total) + " altogether");

                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;

                Rect addAll = new Rect(inRect.xMax - 130f, inRect.yMax - 34f, 130f, 30f);
                Rect only = new Rect(addAll.x - 124f, addAll.y, 120f, 30f);

                if (TabParts.Button(addAll, "Add all " + chain.Count, palette, true, true))
                {
                    GameComponent_ResearchQueue queue = GameComponent_ResearchQueue.Current;

                    for (int i = 0; i < chain.Count; i++)
                        queue?.Add(chain[i]);

                    Close();
                }

                if (TabParts.Button(only, "Just this one", palette))
                {
                    GameComponent_ResearchQueue.Current?.Add(target);

                    Close();
                }
            }
            finally
            {
                Text.Font = font;
                GUI.color = color;
            }
        }
    }
}
