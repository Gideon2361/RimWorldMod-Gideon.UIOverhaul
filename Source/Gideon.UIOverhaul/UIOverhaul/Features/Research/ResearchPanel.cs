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
    /// The research tab: one flow chart, a detail panel and a queue.
    ///
    /// <b>No category tabs.</b> A tab is not a fact about a project -- it is a fact about which file the project
    /// was declared in -- and cutting the tree along that line puts a prerequisite chain on two screens. Core, the
    /// DLCs and every mod are on one canvas.
    ///
    /// <b>Cut into blocks by mod, and inside a block into branches.</b> The first version laid the whole game out
    /// in one set of depth columns and it was worse than vanilla: hundreds of unrelated root projects stacked into
    /// column zero, and nothing to tell the necromancy chain from the accelerator chain. See
    /// <see cref="ResearchGraph"/> for why the mod is the clustering signal that works.
    ///
    /// <b>Filters do the job the tabs were pretending to do.</b> State on the left, tech level on the right, both
    /// multi-select, and both dim rather than remove: a filtered-out project keeps its place so the chains still
    /// read and so nothing moves under the pointer.
    ///
    /// <b>Two zoom levels rather than a slider.</b> IMGUI has three font sizes and no fourth, so a node scaled to
    /// seventy percent is a node whose text no longer fits it. The overview draws blocks instead of nodes, which
    /// is what a zoomed-out tech tree is for anyway: the shape of it, and where the work is.
    /// </summary>
    internal static class ResearchPanel
    {
        private const float DetailWidth = 250f;

        private const float QueueWidth = 236f;

        private const float ToolbarHeight = 40f;

        private const float ControlHeight = 24f;

        private const float Gap = 6f;

        private const float RowHeight = 26f;

        /// <summary>Node size in the overview, where nothing is written on them.</summary>
        private const float OverviewWidth = 54f;

        private const float OverviewHeight = 16f;

        /// <summary>How far the overview shrinks the layout. Everything in it is scaled by this.</summary>
        private const float OverviewScale = 0.42f;

        internal static float WindowWidth
        {
            get { return Mathf.Min(1240f, UI.screenWidth - 16f); }
        }

        internal static float WindowHeight
        {
            get { return Mathf.Min(780f, UI.screenHeight * 0.86f); }
        }

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private static ResearchNode selected;

        /// <summary>The selected project's whole ancestry and its direct children, for lighting their arrows.</summary>
        private static readonly HashSet<ResearchNode> highlighted = new HashSet<ResearchNode>();

        private static Vector2 canvasScroll;
        private static Vector2 detailScroll;
        private static Vector2 queueScroll;

        private static bool showAvailable = true;
        private static bool showLocked = true;
        private static bool showDone = true;
        private static bool showAnomaly = true;
        private static bool overview;

        private static readonly HashSet<TechLevel> hiddenLevels = new HashSet<TechLevel>();

        /// <summary>Which queue row is being dragged, or -1. Cleared on mouse up wherever that happens.</summary>
        private static int dragFrom = -1;

        /// <summary>The last refusal from a drag, and the frame it happened, so it fades rather than sticks.</summary>
        private static string refusal;

        private static int refusedAt = -1000;

        private static string query = string.Empty;

        /// <summary>The queue split into its two lanes. Reused, since it is rebuilt on every frame the rail draws.</summary>
        private static readonly List<ResearchProjectDef> mainLane = new List<ResearchProjectDef>();

        private static readonly List<ResearchProjectDef> anomalyLane = new List<ResearchProjectDef>();

        private static readonly Dictionary<ResearchProjectDef, bool> matches =
            new Dictionary<ResearchProjectDef, bool>();

        /// <summary>
        /// Called when the tab opens.
        ///
        /// The masks are built here rather than at load because whether a project is hidden is a fact about a game
        /// in progress. Once per open means no run is ever built while a node is being drawn.
        /// </summary>
        internal static void Notify_Opened()
        {
            ResearchMask.Prime();
            ResearchBenches.Invalidate();
            ResearchRate.Invalidate();
        }

        internal static void Draw(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // Re-resolved rather than held, because the graph is rebuilt whenever the def list or the fonts
            // change and every node object is replaced when it is. A held reference would keep drawing a detail
            // panel for a node that is no longer anywhere on the canvas.
            if (selected != null)
            {
                ResearchNode current = ResearchGraph.NodeFor(selected.Project);

                if (current != selected)
                {
                    selected = current;

                    // The highlight set holds node objects, every one of which was replaced by the rebuild.
                    Highlight(current);
                }
            }

            Rect content = inRect.ContractedBy(6f);

            Toolbar(new Rect(content.x, content.y, content.width, ToolbarHeight), palette);

            float top = content.y + ToolbarHeight + Gap;
            float height = content.yMax - top;

            // Both rails only exist when they have something in them. They held four hundred and eighty-six
            // pixels of "Nothing planned" on a freshly opened tab, which is a third of the window spent on
            // furniture; the canvas is what somebody opened this screen to look at.
            GameComponent_ResearchQueue queue = GameComponent_ResearchQueue.Current;
            bool showQueue = queue != null && queue.Count > 0;
            bool showDetail = selected != null;

            float right = content.xMax;

            Rect queueRect = new Rect(right - QueueWidth, top, QueueWidth, height);

            if (showQueue)
                right -= QueueWidth + Gap;

            Rect detailRect = new Rect(right - DetailWidth, top, DetailWidth, height);

            if (showDetail)
                right -= DetailWidth + Gap;

            Canvas(new Rect(content.x, top, right - content.x, height), palette);

            if (showDetail)
                Detail(detailRect, palette);

            if (showQueue)
                QueueRail(queueRect, palette);

            // Anywhere, because a drag that ends over the canvas or off the window still ends.
            if (dragFrom >= 0 && Event.current.type == EventType.MouseUp)
                dragFrom = -1;
        }

        // ---------------------------------------------------------------------------------------
        // Toolbar
        // ---------------------------------------------------------------------------------------

        private static void Toolbar(Rect bar, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(bar, palette.Border, palette.PanelBackground);

            float y = bar.y + (bar.height - ControlHeight) * 0.5f;
            float x = bar.x + 8f;

            Search.Draw(new Rect(x, y, 210f, ControlHeight), palette);

            if (Search.Text != query)
            {
                query = Search.Text ?? string.Empty;
                matches.Clear();
            }

            x += 210f + 10f;

            x = StateToggle(x, y, "All", showAvailable && showLocked && showDone, palette, () =>
            {
                showAvailable = true;
                showLocked = true;
                showDone = true;
            });

            x = StateToggle(x, y, "Available", showAvailable, palette, () => showAvailable = !showAvailable);
            x = StateToggle(x, y, "Locked", showLocked, palette, () => showLocked = !showLocked);
            x = StateToggle(x, y, "Done", showDone, palette, () => showDone = !showDone);

            // From the right: the overview switch, then the tech levels, so the levels grow leftwards into
            // whatever room is left rather than colliding with the state segments.
            float right = bar.xMax - 8f;
            float overviewWidth = TabParts.ButtonWidth("Overview");

            TabParts.IconToggle(new Rect(right - overviewWidth, y, overviewWidth, ControlHeight), null, overview,
                palette, () => overview = !overview, null);

            Overlay(new Rect(right - overviewWidth, y, overviewWidth, ControlHeight), "Overview", overview,
                palette);

            right -= overviewWidth + 12f;

            if (ModsConfig.AnomalyActive)
            {
                float width = TabParts.ButtonWidth("Anomaly", 12f);

                right -= width;

                TabParts.IconToggle(new Rect(right, y, width, ControlHeight), null, showAnomaly, palette,
                    () => showAnomaly = !showAnomaly, "Anomaly's knowledge projects.");

                Overlay(new Rect(right, y, width, ControlHeight), "Anomaly", showAnomaly, palette);

                right -= TabParts.SegmentGap;
            }

            List<TechLevel> levels = LevelsPresent();

            for (int i = levels.Count - 1; i >= 0; i--)
            {
                TechLevel level = levels[i];
                string label = level.ToStringHuman().CapitalizeFirst();
                float width = TabParts.ButtonWidth(label, 12f);

                right -= width;

                bool on = !hiddenLevels.Contains(level);

                TabParts.IconToggle(new Rect(right, y, width, ControlHeight), null, on, palette, () =>
                {
                    if (!hiddenLevels.Remove(level))
                        hiddenLevels.Add(level);
                }, null);

                Overlay(new Rect(right, y, width, ControlHeight), label, on, palette);

                right -= TabParts.SegmentGap;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(right - 66f, y, 62f, ControlHeight), "Tech level");

            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// One state toggle, and the x the next one starts at.
        ///
        /// A toggle rather than a segment: these are multi-select, and a segment's contract is that clicking the
        /// one already chosen does nothing -- which would make every filter here one-way. That fault shipped once
        /// already, on the music player's shuffle.
        /// </summary>
        private static float StateToggle(float x, float y, string label, bool on, UIColorPaletteDef palette,
            System.Action toggled)
        {
            float width = TabParts.ButtonWidth(label, 12f);
            Rect rect = new Rect(x, y, width, ControlHeight);

            TabParts.IconToggle(rect, null, on, palette, toggled, null);
            Overlay(rect, label, on, palette);

            return x + width + TabParts.SegmentGap;
        }

        /// <summary>
        /// The word inside a toggle.
        ///
        /// <b>Drawn over the control rather than through it,</b> because <c>TabParts.IconToggle</c> takes a
        /// picture and these are words. Giving that control a label parameter was the alternative and would have
        /// left one control with two mutually exclusive arguments; the frame and the click behaviour are what was
        /// worth sharing, and they are shared.
        /// </summary>
        private static void Overlay(Rect rect, string label, bool on, UIColorPaletteDef palette)
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            bool wrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = on ? palette.WindowBackground : palette.TextSecondary;

                UIRichText.Label(rect, label);
            }
            finally
            {
                Text.WordWrap = wrap;
                GUI.color = color;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }

        private static readonly List<TechLevel> levelsPresent = new List<TechLevel>();

        private static int levelsBuiltFor = -1;

        /// <summary>
        /// Every tech level any project in the graph carries, in the game's own order.
        ///
        /// Rebuilt only when the graph's node count changes. It was worked out from scratch on every frame, which
        /// is a list allocation, a walk of every node and a sort, sixty times a second, to produce the same eight
        /// entries -- for a toolbar that cannot change without the def list changing.
        /// </summary>
        private static List<TechLevel> LevelsPresent()
        {
            List<ResearchNode> nodes = ResearchGraph.Nodes;

            if (levelsBuiltFor == nodes.Count)
                return levelsPresent;

            levelsBuiltFor = nodes.Count;

            List<TechLevel> levels = levelsPresent;

            levels.Clear();

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Project.knowledgeCategory != null)
                    continue;

                TechLevel level = nodes[i].Project.techLevel;

                if (level != TechLevel.Undefined && !levels.Contains(level))
                    levels.Add(level);
            }

            levels.Sort((left, right) => ((int) left).CompareTo((int) right));

            return levels;
        }

        // ---------------------------------------------------------------------------------------
        // Canvas
        // ---------------------------------------------------------------------------------------

        private static void Canvas(Rect outRect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(outRect, palette.SurfaceSunken);

            float scale = overview ? OverviewScale : 1f;
            Vector2 size = ResearchGraph.Size * scale;

            Rect view = new Rect(0f, 0f, Mathf.Max(size.x, outRect.width - 20f),
                Mathf.Max(size.y, outRect.height - 20f));

            Pan(outRect, view);

            Widgets.BeginScrollView(outRect, ref canvasScroll, view);

            try
            {
                Rect visible = new Rect(canvasScroll.x - 40f, canvasScroll.y - 40f, outRect.width + 80f,
                    outRect.height + 80f);

                GroupCaptions(palette, scale, visible);
                Edges(palette, scale, visible);
                Nodes(palette, scale, visible);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// Dragging the canvas with the right or middle button moves it.
        ///
        /// <b>Not the left button,</b> which selects: a graph where dragging from a node moved the view would make
        /// every mis-aimed click a small earthquake. Right and middle are what every other node editor uses.
        /// </summary>
        private static void Pan(Rect outRect, Rect view)
        {
            Event current = Event.current;

            if (current.type != EventType.MouseDrag || !outRect.Contains(current.mousePosition))
                return;

            if (current.button != 1 && current.button != 2)
                return;

            canvasScroll.x = Mathf.Clamp(canvasScroll.x - current.delta.x, 0f,
                Mathf.Max(0f, view.width - outRect.width));
            canvasScroll.y = Mathf.Clamp(canvasScroll.y - current.delta.y, 0f,
                Mathf.Max(0f, view.height - outRect.height));

            current.Use();
        }

        private static void Edges(UIColorPaletteDef palette, float scale, Rect visible)
        {
            List<ResearchEdge> edges = ResearchGraph.Edges;
            Color plain = new Color(palette.Border.r, palette.Border.g, palette.Border.b, 0.85f);

            for (int i = 0; i < edges.Count; i++)
            {
                ResearchEdge edge = edges[i];

                bool lit = selected != null && highlighted.Contains(edge.From) && highlighted.Contains(edge.To);

                // A prerequisite in another mod's block is a line right across the canvas. There are a few
                // hundred of those -- nearly every mod project descends from a core one -- and drawing them all
                // is what turns a graph into a cobweb. They appear when one end is selected, which is when
                // somebody is actually asking where the chain goes.
                if (!edge.Local && !lit)
                    continue;

                Rect from = Scaled(edge.From.Rect, scale);
                Rect to = Scaled(edge.To.Rect, scale);

                if (!visible.Overlaps(Between(from, to)))
                    continue;

                Color color = lit
                    ? palette.Accent
                    : edge.To.Project.knowledgeCategory != null
                        ? new Color(palette.Mood.r, palette.Mood.g, palette.Mood.b, 0.7f)
                        : plain;

                if (edge.Local)
                    Elbow(from, to, color, lit ? 2f : 1f);
                else
                    // Straight, because it is crossing blocks and an elbow route between two arbitrary points
                    // on a canvas this size either doubles back on itself or runs through six other nodes.
                    Widgets.DrawLine(new Vector2(from.xMax, from.center.y), new Vector2(to.x, to.center.y),
                        color, 1.5f);
            }
        }

        /// <summary>The box an edge occupies, for culling it against the visible part of the canvas.</summary>
        private static Rect Between(Rect from, Rect to)
        {
            float x = Mathf.Min(from.xMax, to.x);
            float y = Mathf.Min(from.center.y, to.center.y);

            return new Rect(x, y, Mathf.Abs(to.x - from.xMax) + 1f,
                Mathf.Abs(to.center.y - from.center.y) + 1f);
        }

        /// <summary>
        /// One arrow, as three straight segments rather than a curve.
        ///
        /// IMGUI draws a line by stretching a one-pixel texture, so a curve is a run of a few dozen of those. On a
        /// canvas with several hundred edges that is the whole frame. Three segments read the same at this size
        /// and cost three draws.
        /// </summary>
        private static void Elbow(Rect from, Rect to, Color color, float width)
        {
            float startX = from.xMax;
            float startY = from.center.y;
            float endX = to.x;
            float endY = to.center.y;
            float middle = (startX + endX) * 0.5f;

            Widgets.DrawLine(new Vector2(startX, startY), new Vector2(middle, startY), color, width);

            if (Mathf.Abs(endY - startY) > 0.5f)
                Widgets.DrawLine(new Vector2(middle, startY), new Vector2(middle, endY), color, width);

            Widgets.DrawLine(new Vector2(middle, endY), new Vector2(endX, endY), color, width);
        }

        /// <summary>
        /// The caption over each block: Core, a DLC, or a mod's name.
        ///
        /// <b>This is the payoff of grouping by mod</b> and the thing the first version had no way to say. A
        /// player hunting for the necromancy chain is hunting for a mod, and now the canvas is labelled with the
        /// answer instead of being a uniform field of cards.
        ///
        /// Suppressed in the overview, where a block caption would be taller than the nodes it names.
        /// </summary>
        private static void GroupCaptions(UIColorPaletteDef palette, float scale, Rect visible)
        {
            if (overview)
                return;

            List<ResearchGroup> groups = ResearchGraph.Groups;

            GameFont font = Text.Font;
            Color color = GUI.color;

            try
            {
                Text.Font = GameFont.Small;

                for (int i = 0; i < groups.Count; i++)
                {
                    Rect band = Scaled(groups[i].Header, scale);

                    if (!visible.Overlaps(band))
                        continue;

                    GUI.color = palette.TextSecondary;

                    float width = UIRichText.WidthOf(groups[i].Label) + 6f;

                    Widgets.Label(new Rect(band.x, band.y, width, band.height), groups[i].Label);

                    GUI.color = new Color(palette.Border.r, palette.Border.g, palette.Border.b, 0.8f);

                    Widgets.DrawLineHorizontal(band.x + width + 4f, band.center.y + 1f,
                        Mathf.Max(0f, band.width - width - 8f));
                }
            }
            finally
            {
                GUI.color = color;
                Text.Font = font;
            }
        }

        private static void Nodes(UIColorPaletteDef palette, float scale, Rect visible)
        {
            List<ResearchNode> nodes = ResearchGraph.Nodes;
            GameComponent_ResearchQueue queue = GameComponent_ResearchQueue.Current;

            for (int i = 0; i < nodes.Count; i++)
            {
                ResearchNode node = nodes[i];
                Rect rect = Scaled(node.Rect, scale);

                if (!visible.Overlaps(rect))
                    continue;

                bool dimmed = !Passes(node);

                if (overview)
                {
                    Block(rect, node, palette, dimmed);

                    continue;
                }

                NodeClick click = ResearchNodeArt.Draw(rect, node, palette, node == selected,
                    queue == null ? 0 : queue.PlaceOf(node.Project), dimmed);

                if (click == NodeClick.Select)
                    Select(node);
                else if (click == NodeClick.ToggleQueue)
                    ToggleQueue(node, queue);
            }
        }

        /// <summary>
        /// A node in the overview: the stripe colour and nothing else.
        ///
        /// Nothing else fits. At this scale a node is fifty-four pixels wide and the smallest font the game has is
        /// taller than the block, so the honest thing is a coloured block that says what state it is in and hands
        /// over its name on hover.
        /// </summary>
        private static void Block(Rect rect, ResearchNode node, UIColorPaletteDef palette, bool dimmed)
        {
            ResearchState state = ResearchFacts.StateOf(node);
            Color color = ResearchFacts.ColorFor(state, palette, node.Project.knowledgeCategory);

            if (dimmed || state == ResearchState.Ghost)
                color = new Color(color.r, color.g, color.b, 0.3f);

            Rect block = new Rect(rect.x, rect.y, Mathf.Min(rect.width, OverviewWidth),
                Mathf.Min(rect.height, OverviewHeight));

            Widgets.DrawBoxSolid(block, color);

            if (node == selected)
                UIElementPainter.OutlineRounded(block.ExpandedBy(2f), palette.TextPrimary, Color.clear);

            if (!Mouse.IsOver(block))
                return;

            Widgets.DrawBoxSolid(block, palette.HoverOverlay);

            string tooltip = ResearchFacts.TooltipFor(node, state);

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(block, (TipSignal) tooltip);

            if (Widgets.ButtonInvisible(block))
                Select(node);
        }

        private static Rect Scaled(Rect rect, float scale)
        {
            return Mathf.Approximately(scale, 1f)
                ? rect
                : new Rect(rect.x * scale, rect.y * scale, rect.width * scale, rect.height * scale);
        }

        private static void Select(ResearchNode node)
        {
            selected = node;
            detailScroll = Vector2.zero;

            Highlight(node);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Everything the selected project is connected to: its whole ancestry, and what it leads to next.
        ///
        /// <b>The ancestry all the way back, not just the direct prerequisites.</b> The question a locked node
        /// raises is "how far back does this go", and lighting one arrow answers it one project at a time. The
        /// children are one deep because "what does this unlock" is a different question and the detail panel
        /// answers it in words.
        ///
        /// Built when the selection changes rather than per frame: this is a graph walk, and the edges are drawn
        /// sixty times a second.
        /// </summary>
        private static void Highlight(ResearchNode node)
        {
            highlighted.Clear();

            if (node == null)
                return;

            highlighted.Add(node);

            for (int i = 0; i < node.Children.Count; i++)
                highlighted.Add(node.Children[i]);

            Ancestors(node);
        }

        private static void Ancestors(ResearchNode node)
        {
            for (int i = 0; i < node.Parents.Count; i++)
            {
                if (highlighted.Add(node.Parents[i]))
                    Ancestors(node.Parents[i]);
            }
        }

        private static void ToggleQueue(ResearchNode node, GameComponent_ResearchQueue queue)
        {
            if (queue == null || node?.Project == null)
                return;

            if (queue.Contains(node.Project))
            {
                queue.Remove(node.Project);
                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            ResearchActions.Queue(node.Project);
        }

        // ---------------------------------------------------------------------------------------
        // Filtering
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Whether a node passes the filters and the search.
        ///
        /// <b>A node that fails is dimmed, not removed.</b> Removing one would leave a hole in a chain, and a hole
        /// with nothing to explain it reads as a missing mod -- the same reason the difficulty-hidden projects are
        /// drawn as ghosts. It also keeps the layout fixed while a filter is being fiddled with.
        /// </summary>
        private static bool Passes(ResearchNode node)
        {
            ResearchProjectDef project = node.Project;

            if (project.knowledgeCategory != null)
            {
                if (!showAnomaly)
                    return false;
            }
            else if (project.techLevel != TechLevel.Undefined && hiddenLevels.Contains(project.techLevel))
            {
                return false;
            }

            if (project.IsFinished)
            {
                if (!showDone)
                    return false;
            }
            else
            {
                ResearchState state = ResearchFacts.StateOf(node);
                bool available = state == ResearchState.Ready || state == ResearchState.Researching;

                if (available && !showAvailable)
                    return false;

                if (!available && !showLocked)
                    return false;
            }

            return query.NullOrEmpty() || Matches(project);
        }

        /// <summary>
        /// Whether the search text finds this project.
        ///
        /// <b>Matched against what it unlocks as well as its name,</b> which is most of the value: "turret" should
        /// find Gun turrets, and "component" should find Machining, which is where components come from and is not
        /// a word in its title.
        ///
        /// Cached per project and thrown away when the text changes. <c>UnlockedDefs</c> is a LINQ walk of four
        /// def databases the first time it is asked, and asking it three hundred times a frame while somebody
        /// types is the difference between a search box and a stutter.
        /// </summary>
        private static bool Matches(ResearchProjectDef project)
        {
            bool known;

            if (matches.TryGetValue(project, out known))
                return known;

            known = UIGuard.Try("Research.Search", () =>
            {
                string lower = query.ToLower();

                if (!ResearchMask.Masked(project) && project.label != null
                                                  && project.label.ToLower().Contains(lower))
                    return true;

                // An unknown project is not searchable: finding it by the name of the thing it unlocks would
                // hand over the secret the mask exists to keep.
                if (ResearchMask.Masked(project))
                    return false;

                List<Def> unlocked = project.UnlockedDefs;

                for (int i = 0; i < unlocked.Count; i++)
                {
                    if (unlocked[i].label != null && unlocked[i].label.ToLower().Contains(lower))
                        return true;
                }

                return false;
            }, false, null);

            matches[project] = known;

            return known;
        }

        // ---------------------------------------------------------------------------------------
        // Detail panel
        // ---------------------------------------------------------------------------------------

        private static void Detail(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(10f);

            if (selected == null)
            {
                TabParts.Note(inner, inner.y, "Pick a project to see what it unlocks, what it leads to and how "
                                              + "long it will take.", palette);

                return;
            }

            ResearchProjectDef project = selected.Project;
            ResearchState state = ResearchFacts.StateOf(selected);
            bool masked = state == ResearchState.Unknown;

            float buttons = ResearchActions.Actionable(selected) ? 36f : 0f;
            Rect view = new Rect(inner.x, inner.y, inner.width, inner.height - buttons);
            Rect body = new Rect(0f, 0f, view.width - 18f, DetailHeight(project, masked));

            Widgets.BeginScrollView(view, ref detailScroll, body);

            try
            {
                DetailBody(body, project, state, masked, palette);
            }
            finally
            {
                Widgets.EndScrollView();
            }

            if (buttons > 0f)
                Buttons(new Rect(inner.x, inner.yMax - 30f, inner.width, 30f), project, palette);
        }

        /// <summary>
        /// How tall the detail panel's contents come out.
        ///
        /// Counted rather than measured with <c>CalcHeight</c>, because every line in here is a fixed-height row
        /// and the only variable is how many there are.
        /// </summary>
        private static float DetailHeight(ResearchProjectDef project, bool masked)
        {
            float height = 30f + 18f + 26f;

            height += 22f + Mathf.Min(UnlockCount(project), 10) * 18f;
            height += 22f + Mathf.Min(ChildCount(), 10) * 18f;
            height += 22f + PrerequisiteCount(project) * 18f;

            if (!masked && !project.description.NullOrEmpty())
                height += 90f;

            return height;
        }

        private static void DetailBody(Rect body, ResearchProjectDef project, ResearchState state, bool masked,
            UIColorPaletteDef palette)
        {
            float y = body.y;

            if (masked)
            {
                ResearchMask.Draw(new Rect(body.x, y, body.width, 24f), ResearchMask.Key(project, "name"),
                    palette.Mood, GameFont.Small);

                y += 26f;

                ResearchMask.Draw(new Rect(body.x, y, body.width * 0.8f, 16f),
                    ResearchMask.Key(project, "meta"), palette.TextDisabled);

                y += 22f;
            }
            else
            {
                TabParts.RowLabel(new Rect(body.x, y, body.width, 24f), project.LabelCap.ToString(),
                    palette.TextPrimary);

                y += 26f;

                TabParts.RowLabel(new Rect(body.x, y, body.width, 16f), Meta(project), palette.TextDisabled,
                    GameFont.Tiny);

                y += 20f;

                Rect bar = new Rect(body.x, y, body.width, 4f);

                Widgets.DrawBoxSolid(bar, palette.SurfaceSunken);
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * project.ProgressPercent, bar.height),
                    ResearchFacts.ColorFor(state, palette, project.knowledgeCategory));

                y += 8f;

                TabParts.RowLabel(new Rect(body.x, y, body.width, 16f), Progress(project), palette.TextDisabled,
                    GameFont.Tiny);

                y += 20f;
            }

            if (!masked && !project.description.NullOrEmpty())
            {
                y = TabParts.Note(new Rect(body.x, y, body.width, 0f), y, project.description, palette,
                    GameFont.Tiny, palette.TextSecondary);

                y += 8f;
            }

            y = Section(body, y, "Unlocks", palette);
            y = Unlocks(body, y, project, masked, palette);

            y = Section(body, y, "Leads to", palette);
            y = LeadsTo(body, y, masked, palette);

            y = Section(body, y, "Requires", palette);

            Requires(body, y, project, masked, palette);
        }

        private static string Meta(ResearchProjectDef project)
        {
            if (project.knowledgeCategory != null)
                return project.knowledgeCategory.LabelCap.ToString() + " knowledge "
                       + project.knowledgeCost.ToString("F0");

            return project.techLevel.ToStringHuman().CapitalizeFirst() + "  "
                   + project.CostApparent.ToString("F0") + " points";
        }

        private static string Progress(ResearchProjectDef project)
        {
            if (project.IsFinished)
                return "Finished";

            string done = project.ProgressApparentString + " of " + project.CostApparent.ToString("F0");
            float days = ResearchRate.DaysFor(project);

            return days < 0f ? done : done + ", about " + ResearchRate.Days(days) + " at your current rate";
        }

        private static float Section(Rect body, float y, string title, UIColorPaletteDef palette)
        {
            GameFont font = Text.Font;
            Color color = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(body.x, y + 4f, body.width, 16f), title);
            }
            finally
            {
                GUI.color = color;
                Text.Font = font;
            }

            return y + 22f;
        }

        private static int UnlockCount(ResearchProjectDef project)
        {
            return UIGuard.Try("Research.UnlockCount", () => project.UnlockedDefs.Count, 0, null);
        }

        private static int ChildCount()
        {
            return selected == null ? 0 : selected.Children.Count;
        }

        private static int PrerequisiteCount(ResearchProjectDef project)
        {
            int count = project.prerequisites == null ? 0 : project.prerequisites.Count;

            return count + (project.hiddenPrerequisites == null ? 0 : project.hiddenPrerequisites.Count);
        }

        private static float Unlocks(Rect body, float y, ResearchProjectDef project, bool masked,
            UIColorPaletteDef palette)
        {
            List<Def> unlocked = UIGuard.Try("Research.Unlocks", () => project.UnlockedDefs, null, null);

            if (unlocked == null || unlocked.Count == 0)
                return Empty(body, y, "Nothing new to build", palette);

            int shown = Mathf.Min(unlocked.Count, 10);

            for (int i = 0; i < shown; i++)
            {
                Rect row = new Rect(body.x + 4f, y, body.width - 4f, 18f);

                if (masked)
                    ResearchMask.Draw(row, ResearchMask.Key(project, "unlock", i), palette.Mood);
                else
                    TabParts.RowLabel(row, unlocked[i].LabelCap.ToString(), palette.TextSecondary,
                        GameFont.Tiny);

                y += 18f;
            }

            if (unlocked.Count > shown && !masked)
            {
                TabParts.RowLabel(new Rect(body.x + 4f, y, body.width - 4f, 18f),
                    "and " + (unlocked.Count - shown) + " more", palette.TextDisabled, GameFont.Tiny);

                y += 18f;
            }

            return y + 4f;
        }

        private static float LeadsTo(Rect body, float y, bool masked, UIColorPaletteDef palette)
        {
            List<ResearchNode> children = selected.Children;

            if (children.Count == 0)
                return Empty(body, y, "The end of its branch", palette);

            int shown = Mathf.Min(children.Count, 10);

            for (int i = 0; i < shown; i++)
            {
                Rect row = new Rect(body.x + 4f, y, body.width - 4f, 18f);

                if (masked)
                    ResearchMask.Draw(row, ResearchMask.Key(selected.Project, "leads", i), palette.Mood);
                else
                    TabParts.RowLabel(row, ResearchFacts.Name(children[i].Project), palette.TextSecondary,
                        GameFont.Tiny);

                y += 18f;
            }

            return y + 4f;
        }

        private static void Requires(Rect body, float y, ResearchProjectDef project, bool masked,
            UIColorPaletteDef palette)
        {
            if (PrerequisiteCount(project) == 0)
            {
                Empty(body, y, "Nothing", palette);

                return;
            }

            y = Prerequisites(body, y, project.prerequisites, project, masked, palette, 0);

            Prerequisites(body, y, project.hiddenPrerequisites, project, masked, palette,
                project.prerequisites == null ? 0 : project.prerequisites.Count);
        }

        private static float Prerequisites(Rect body, float y, List<ResearchProjectDef> list,
            ResearchProjectDef project, bool masked, UIColorPaletteDef palette, int offset)
        {
            if (list == null)
                return y;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null)
                    continue;

                Rect row = new Rect(body.x + 4f, y, body.width - 4f, 18f);

                if (masked)
                {
                    ResearchMask.Draw(row, ResearchMask.Key(project, "needs", offset + i), palette.Mood);
                }
                else
                {
                    bool done = list[i].IsFinished;

                    if (done && ResearchGlyphs.Tick != null)
                    {
                        Color previous = GUI.color;

                        GUI.color = palette.Success;
                        GUI.DrawTexture(new Rect(row.x, row.y + 4f, 10f, 10f), ResearchGlyphs.Tick);
                        GUI.color = previous;
                    }

                    TabParts.RowLabel(new Rect(row.x + 14f, row.y, row.width - 14f, row.height),
                        ResearchFacts.Name(list[i]), done ? palette.TextSecondary : palette.TextPrimary,
                        GameFont.Tiny);
                }

                y += 18f;
            }

            return y;
        }

        private static float Empty(Rect body, float y, string text, UIColorPaletteDef palette)
        {
            TabParts.RowLabel(new Rect(body.x + 4f, y, body.width - 4f, 18f), text, palette.TextDisabled,
                GameFont.Tiny);

            return y + 22f;
        }

        private static void Buttons(Rect row, ResearchProjectDef project, UIColorPaletteDef palette)
        {
            GameComponent_ResearchQueue queue = GameComponent_ResearchQueue.Current;
            bool queued = queue != null && queue.Contains(project);

            string second = queued ? "Unqueue" : "Queue";
            float secondWidth = Mathf.Max(TabParts.ButtonWidth(second), 72f);

            Rect start = new Rect(row.x, row.y, row.width - secondWidth - Gap, row.height);
            Rect other = new Rect(start.xMax + Gap, row.y, secondWidth, row.height);

            bool canStart = UIGuard.Try("Research.CanStart", () => project.CanStartNow, false, null)
                            && (Find.ResearchManager == null || !Find.ResearchManager.IsCurrentProject(project));

            if (TabParts.Button(start, "Research now", palette, canStart, true,
                    canStart ? null : "Not something the colony can start right now."))
                ResearchActions.StartNow(project);

            if (!TabParts.Button(other, second, palette))
                return;

            if (queued)
                queue.Remove(project);
            else
                ResearchActions.Queue(project);
        }

        // ---------------------------------------------------------------------------------------
        // Queue rail
        // ---------------------------------------------------------------------------------------

        private static void QueueRail(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            GameComponent_ResearchQueue queue = GameComponent_ResearchQueue.Current;

            if (queue == null)
                return;

            Rect inner = rect.ContractedBy(8f);

            List<ResearchProjectDef> main = mainLane;
            List<ResearchProjectDef> anomaly = anomalyLane;

            main.Clear();
            anomaly.Clear();

            for (int i = 0; i < queue.Entries.Count; i++)
            {
                ResearchProjectDef project = queue.Entries[i];

                if (project == null)
                    continue;

                if (project.knowledgeCategory != null)
                    anomaly.Add(project);
                else
                    main.Add(project);
            }

            float footer = 46f;
            Rect view = new Rect(inner.x, inner.y, inner.width, inner.height - footer);
            float height = 22f + main.Count * RowHeight + BlockedRows(main) * 16f
                           + (anomaly.Count > 0 ? 24f + anomaly.Count * RowHeight : 0f)
                           + (main.Count == 0 ? 20f : 0f);

            Rect body = new Rect(0f, 0f, view.width - 18f, Mathf.Max(height, view.height));

            Widgets.BeginScrollView(view, ref queueScroll, body);

            try
            {
                float y = Header(body, 0f, "Queue", TotalDays(main), palette);

                if (main.Count == 0)
                    y = Empty(body, y, "Nothing planned", palette);

                for (int i = 0; i < main.Count; i++)
                    y = Row(body, y, main[i], i, queue, palette);

                if (anomaly.Count > 0)
                {
                    y = Header(body, y + 6f, "Anomaly", "parallel", palette);

                    for (int i = 0; i < anomaly.Count; i++)
                        y = Row(body, y, anomaly[i], -1, queue, palette);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            Footer(new Rect(inner.x, inner.yMax - footer + 6f, inner.width, footer - 6f), palette, anomaly.Count);
        }

        private static int BlockedRows(List<ResearchProjectDef> main)
        {
            int blocked = 0;

            for (int i = 0; i < main.Count; i++)
            {
                if (!main[i].CanStartNow)
                    blocked++;
            }

            return blocked;
        }

        private static string TotalDays(List<ResearchProjectDef> main)
        {
            float total = 0f;

            for (int i = 0; i < main.Count; i++)
            {
                float days = ResearchRate.DaysFor(main[i]);

                if (days < 0f)
                    return "-";

                total += days;
            }

            return ResearchRate.Days(total);
        }

        private static float Header(Rect body, float y, string title, string trailing,
            UIColorPaletteDef palette)
        {
            GameFont font = Text.Font;
            Color color = GUI.color;
            TextAnchor anchor = Text.Anchor;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(body.x, y, body.width * 0.6f, 18f), title);

                Text.Anchor = TextAnchor.UpperRight;

                Widgets.Label(new Rect(body.x + body.width * 0.6f, y, body.width * 0.4f, 18f), trailing);
            }
            finally
            {
                Text.Anchor = anchor;
                GUI.color = color;
                Text.Font = font;
            }

            return y + 20f;
        }

        /// <summary>
        /// One queue row, and the drag that reorders it.
        ///
        /// <b>The drag is started from the grip and finished anywhere.</b> A drag that had to end inside the rail
        /// would silently do nothing whenever somebody let go a few pixels wide of it, which reads as the list
        /// refusing to be reordered.
        /// </summary>
        private static float Row(Rect body, float y, ResearchProjectDef project, int index,
            GameComponent_ResearchQueue queue, UIColorPaletteDef palette)
        {
            Rect row = new Rect(body.x, y, body.width, RowHeight);
            bool current = Find.ResearchManager != null && Find.ResearchManager.IsCurrentProject(project);
            bool anomaly = project.knowledgeCategory != null;

            if (current)
                Widgets.DrawBoxSolid(row, palette.SelectionOverlay);
            else if (Mouse.IsOver(row))
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            if (index >= 0 && ResearchGlyphs.Grip != null)
            {
                Rect grip = new Rect(row.x + 2f, row.y + 5f, 12f, 16f);
                Color previous = GUI.color;

                GUI.color = palette.TextDisabled;
                GUI.DrawTexture(grip, ResearchGlyphs.Grip);
                GUI.color = previous;

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                                                              && Mouse.IsOver(grip))
                {
                    dragFrom = index;

                    Event.current.Use();
                }
            }

            Rect badge = new Rect(row.x + 16f, row.y + 5f, 16f, 16f);

            UIElementPainter.FillRounded(badge, current
                ? anomaly ? palette.Mood : palette.Accent
                : palette.ControlBackgroundFaded);

            DrawNumber(badge, index >= 0 ? index + 1 : 1,
                current ? palette.WindowBackground : palette.TextSecondary);

            Rect close = new Rect(row.xMax - 18f, row.y + 5f, 16f, 16f);
            Rect eta = new Rect(close.x - 48f, row.y, 46f, row.height);

            TabParts.RowLabel(new Rect(badge.xMax + 6f, row.y, eta.x - badge.xMax - 10f, row.height),
                ResearchFacts.Name(project), current ? palette.TextPrimary : palette.TextSecondary,
                GameFont.Tiny);

            TabParts.RowLabel(eta, anomaly
                ? project.knowledgeCost.ToString("F0") + " kn"
                : ResearchRate.Days(ResearchRate.DaysFor(project)), palette.TextDisabled, GameFont.Tiny);

            if (ResearchGlyphs.Cross != null)
            {
                Color previous = GUI.color;

                GUI.color = Mouse.IsOver(close) ? palette.Danger : palette.TextDisabled;
                GUI.DrawTexture(close.ContractedBy(3f), ResearchGlyphs.Cross);
                GUI.color = previous;
            }

            if (Widgets.ButtonInvisible(close))
            {
                queue.Remove(project);
                SoundDefOf.Click.PlayOneShotOnCamera();

                return y + RowHeight;
            }

            if (index >= 0 && dragFrom >= 0 && dragFrom != index && Mouse.IsOver(row)
                && Event.current.type == EventType.MouseUp)
            {
                string why;

                if (!queue.Move(dragFrom, index, out why) && why != null)
                {
                    refusal = why;
                    refusedAt = Time.frameCount;
                }

                dragFrom = -1;
            }

            y += RowHeight;

            if (!project.CanStartNow && !project.IsFinished)
            {
                ResearchNode node = ResearchGraph.NodeFor(project);
                string why = ResearchFacts.ChipFor(node, ResearchFacts.StateOf(node));

                if (why != null)
                {
                    TabParts.RowLabel(new Rect(body.x + 34f, y - 2f, body.width - 38f, 16f), why,
                        palette.Warning, GameFont.Tiny);

                    y += 16f;
                }
            }

            return y;
        }

        private static void DrawNumber(Rect rect, int number, Color color)
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color previous = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = color;

                Widgets.Label(rect, number.ToString());
            }
            finally
            {
                GUI.color = previous;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }

        /// <summary>
        /// The foot of the rail: why Anomaly is separate, or the reason a drag was refused.
        ///
        /// The refusal takes the space for a few seconds because it is the answer to the thing that just happened;
        /// after that the standing note comes back. A refusal that stayed forever would read as an error the
        /// player has to clear.
        /// </summary>
        private static void Footer(Rect rect, UIColorPaletteDef palette, int anomalyCount)
        {
            bool recent = Time.frameCount - refusedAt < 240;

            if (recent && refusal != null)
            {
                TabParts.Note(rect, rect.y, refusal, palette, GameFont.Tiny, palette.Warning);

                return;
            }

            if (anomalyCount > 0)
                TabParts.Note(rect, rect.y, "Anomaly runs alongside, paid for with knowledge from studying "
                                            + "entities rather than with researcher time.", palette);
        }
    }
}
