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

        /// <summary>The narrowest the open rail goes, whatever its labels measure.</summary>
        private const float MinRailWidth = 152f;

        /// <summary>
        /// The widest it goes. A mod name can be any length at all, and a rail that grew to fit "Vanilla Furniture
        /// Expanded - Props and Decor" would be half the window; past this the label ellipses and the tooltip has
        /// the rest.
        /// </summary>
        private const float MaxRailWidth = 268f;

        /// <summary>Folded: a column of colour ticks, and the arrow to unfold it.</summary>
        private const float FoldedRailWidth = 18f;

        /// <summary>Below this the canvas is not worth having, so the rail is dropped rather than the graph.</summary>
        private const float MinCanvasWidth = 420f;

        /// <summary>
        /// The text row itself, measured from the font rather than written down.
        ///
        /// The rail labels are Small from 2026-08-23, on Aaron's instruction -- they are names to read, not
        /// figures to scan. A literal 20 suited Tiny and would clip Small, and it is a setting anyway: a player
        /// who turns tiny text off gets taller lines than either.
        /// </summary>
        private static float RailRowHeight
        {
            get { return Mathf.Ceil(UIFonts.LineHeightOf(GameFont.Small) + 2f); }
        }

        /// <summary>
        /// Air below each rail row, on top of the row and its progress hairline.
        ///
        /// Ten rather than five from 2026-08-23: at five, twelve rows of coloured text with a coloured bar under
        /// each read as one striped block instead of twelve entries. The gap is what makes a row a row.
        /// </summary>
        private const float RailRowGap = 10f;

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

        /// <summary>
        /// The canvas viewport as it was last drawn, so a link can work out where to scroll to.
        ///
        /// Recorded rather than passed, because the thing that needs it -- following a link out of the detail
        /// panel -- happens in a different part of the frame from the thing that knows it. Zero until the canvas
        /// has drawn once, which is handled where it is read.
        /// </summary>
        private static Rect lastCanvas;
        private static Vector2 detailScroll;
        private static Vector2 queueScroll;

        private static bool showAvailable = true;
        private static bool showLocked = true;

        /// <summary>
        /// Off to start with, from 2026-08-23.
        ///
        /// A finished project is the one kind you never open this tab to look for: it is done, there is nothing to
        /// decide about it, and on a mature colony it is most of the canvas. Starting with them hidden means the
        /// tab opens showing what is left to do, and the toggle is right there for anybody who wants the history.
        /// </summary>
        private static bool showDone;
        private static bool showAnomaly = true;
        private static bool overview;

        private static readonly HashSet<TechLevel> hiddenLevels = new HashSet<TechLevel>();

        /// <summary>
        /// Whether the contents rail is folded to a strip of ticks.
        ///
        /// Static, and deliberately not in the settings file: it is a this-session working preference like the
        /// scroll position beside it, and a player who folds it to read one wide chain does not mean to fold it
        /// for every colony they ever load.
        /// </summary>
        private static bool railFolded;

        private static Vector2 railScroll;

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

            // The contents rail, on the left, and it folds. Three rails around one canvas is too many, so this
            // one collapses to a strip of coloured ticks that still says how many blocks there are and still
            // jumps to one -- the same "only take the room you have earned" rule the other two rails follow by
            // vanishing when empty. This one can never be empty, so it folds instead.
            float left = content.x;
            float railWidth = railFolded ? FoldedRailWidth : RailWidth(palette);

            // Not in the overview, which is a whole-canvas view: a table of contents for a picture you can
            // already see all of is furniture.
            bool showRail = !overview && railWidth < (right - content.x) - MinCanvasWidth;

            if (showRail)
                left += railWidth + Gap;

            Canvas(new Rect(left, top, right - left, height), palette);

            if (showRail)
                ContentsRail(new Rect(content.x, top, railWidth, height), palette);

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

            // Group by sits next to the state filters rather than out on the right, because it decides what the
            // canvas *is* and the tech levels only decide what is dimmed on it. A segmented control and not
            // toggles: the three are mutually exclusive, which is the one case a segment is right for.
            x += 12f;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            float captionWidth = UIRichText.WidthOf("Group by") + 4f;

            Widgets.Label(new Rect(x, y + 3f, captionWidth, ControlHeight), "Group by");

            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            x += captionWidth + 4f;

            ResearchGrouping grouping = ResearchGroupings.Current;

            for (int i = 0; i < ResearchGroupings.All.Length; i++)
            {
                ResearchGrouping which = ResearchGroupings.All[i];
                string label = ResearchGroupings.LabelOf(which);
                float width = TabParts.ButtonWidth(label, 12f);
                Rect segment = new Rect(x, y, width, ControlHeight);

                bool on = grouping == which;

                TabParts.IconToggle(segment, null, on, palette, () =>
                {
                    if (!on)
                        ResearchGroupings.Set(which);
                }, ResearchGroupings.TooltipOf(which));

                Overlay(segment, label, on, palette);

                x += width + TabParts.SegmentGap;
            }

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
            bool anyLevel = false;

            for (int i = levels.Count - 1; i >= 0; i--)
            {
                TechLevel level = levels[i];
                string label = level.ToStringHuman().CapitalizeFirst();
                float width = TabParts.ButtonWidth(label, 12f);

                // The levels grow leftwards and now have something to run into: Group by ends at x, and the
                // "Tech level" caption still needs its 66 pixels to the left of whatever is drawn last. Stopping
                // is right and clipping is not -- a toggle drawn under another control is a control that silently
                // does the wrong thing when clicked, which is the fault the pawns tab hit test taught.
                if (right - width - TabParts.SegmentGap < x + 70f)
                    break;

                right -= width;

                bool on = !hiddenLevels.Contains(level);

                TabParts.IconToggle(new Rect(right, y, width, ControlHeight), null, on, palette, () =>
                {
                    if (!hiddenLevels.Remove(level))
                        hiddenLevels.Add(level);
                }, null);

                Overlay(new Rect(right, y, width, ControlHeight), label, on, palette);

                right -= TabParts.SegmentGap;
                anyLevel = true;
            }

            // Only when there is something for it to name. On a narrow window the loop above stops early, and a
            // caption reading "Tech level" with no toggles beside it is worse than no caption: it says a control
            // is there and it is not.
            if (anyLevel)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(right - 66f, y, 62f, ControlHeight), "Tech level");

                GUI.color = palette.TextPrimary;
                Text.Font = GameFont.Small;
            }
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

            lastCanvas = outRect;

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

                // No arrows, on Aaron's instruction of 2026-08-23: "really messy", and he is right about what
                // they had become. A banded canvas puts most of a chain's links across band boundaries, and the
                // rule that a crossing arrow is only drawn when one end is selected meant the visible arrows were
                // the short local ones -- the links you least need drawn, since two cards side by side in
                // dependency order already say it.
                //
                // The edges are still built and still used: they decide depth, branches and the crossing-reducing
                // row order, they drive the highlight, and the detail panel's Requires and Leads to lists read
                // them. Only the lines are gone.
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
                    ResearchGroup group = groups[i];
                    Rect band = Scaled(group.Header, scale);

                    if (!visible.Overlaps(band))
                        continue;

                    // A theme band gets its own hue on the caption, the rule and a swatch; a mod name and a tech
                    // level get the ordinary secondary text. Eleven bands need a colour to be told apart at a
                    // glance and a mod name does not, so colouring both would be decoration pretending to be
                    // information.
                    Color tint = group.Band.HasValue
                        ? ResearchBands.ColorFor(group.Band.Value, palette)
                        : palette.TextSecondary;

                    float x = band.x;

                    if (group.Band.HasValue)
                    {
                        Widgets.DrawBoxSolid(new Rect(x, band.center.y - 4f, 8f, 8f), tint);

                        x += 13f;
                    }

                    GUI.color = tint;

                    // Weight as a face rather than as a tag. The bold markup was the only way to get weight out
                    // of RimWorld's one font; with sheets shipped it is the typeface's job, and dropping the tag
                    // is also what lets UITextControl draw this at all, since it refuses text carrying markup.
                    //
                    // Uppercase stays. IMGUI has no letter-spacing, so caps was doing most of the work of
                    // separating a heading from a node name -- and it still is, now that the two also differ in
                    // weight rather than only in case.
                    //
                    // Measured through the same control that draws it, so the rule beside it lands where the
                    // letters actually end. Measuring one face and drawing another is how a heading gets a rule
                    // through its last letter.
                    string caption = group.Label.ToUpperInvariant();
                    float width = UITextControl.Width(caption, UIFace.BarlowCondensed, GameFont.Small, FontStyle.Bold)
                                  + 6f;

                    UITextControl.Label(new Rect(x, band.y, width, band.height), caption,
                        UIFace.BarlowCondensed, GameFont.Small, FontStyle.Bold);

                    x += width;

                    // Done of total, in the quieter colour, so a band says how far through it you are without
                    // anybody having to count nodes. Off in the overview, which has no room for it.
                    string tally = Tally(group);

                    if (!tally.NullOrEmpty())
                    {
                        Text.Font = GameFont.Tiny;
                        GUI.color = palette.TextDisabled;

                        float tallyWidth = UIRichText.WidthOf(tally) + 6f;

                        Widgets.Label(new Rect(x, band.y + 3f, tallyWidth, band.height), tally);

                        x += tallyWidth;

                        // Back to the caption's font, not to the caller's: the outer finally restores that, and
                        // the next block's caption is drawn by the next turn of this loop.
                        Text.Font = GameFont.Small;
                    }

                    GUI.color = group.Band.HasValue
                        ? new Color(tint.r, tint.g, tint.b, 0.45f)
                        : new Color(palette.Border.r, palette.Border.g, palette.Border.b, 0.8f);

                    Widgets.DrawLineHorizontal(x + 4f, band.center.y + 1f,
                        Mathf.Max(0f, band.xMax - x - 8f));
                }
            }
            finally
            {
                GUI.color = color;
                Text.Font = font;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Contents rail
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// A table of contents for whatever the grouping is: one row per block, with its progress, jumping the
        /// canvas to it.
        ///
        /// <b>One control, three meanings.</b> Grouped by theme it lists bands; by source, mods; by tech level,
        /// tech levels. Nothing in here knows which -- it walks <c>ResearchGraph.Groups</c>, which is already
        /// whatever the grouping made it, and asks each group for its colour. That is why switching the grouping
        /// needs no second rail and no branch in this method.
        ///
        /// <b>It exists because the old canvas had no usable index.</b> Grouped by mod, a table of contents is a
        /// list of mod names, which says nothing about where anything is, and the block headings said the same
        /// thing and were already on screen. A list of subjects with "4 of 23" beside each is the first version of
        /// this that answers a question.
        /// </summary>
        private static float railMeasured = MinRailWidth;

        private static int railMeasuredFor = -1;

        private static string railMeasuredKey;

        /// <summary>
        /// How wide the open rail needs to be for the labels it is about to draw.
        ///
        /// <b>Measured rather than fixed, from 2026-08-23.</b> A fixed 152 was chosen for the theme names and was
        /// the right number for them. Grouped by source it produced nine consecutive rows reading "Vanilla
        /// Fur..." -- nine different mods, indistinguishable, which is a list that has stopped being a list. The
        /// two groupings want different widths and neither should lose so the other can win.
        ///
        /// The widest label plus what the row spends on furniture: the colour tick and its gap, the tally, and the
        /// scrollbar. Clamped both ends, so it never gets narrower than the themes want or wider than a window can
        /// spare.
        ///
        /// <b>Recomputed only when the block list changes,</b> keyed on the grouping and the block count.
        /// Measuring forty-eight labels with <c>CalcSize</c> sixty times a second to produce the same number is
        /// the fault this mod has fixed in four other panels.
        /// </summary>
        private static float RailWidth(UIColorPaletteDef palette)
        {
            List<ResearchGroup> groups = ResearchGraph.Groups;
            string key = ResearchGroupings.Store(ResearchGroupings.Current);

            if (railMeasuredFor == groups.Count && railMeasuredKey == key)
                return railMeasured;

            railMeasuredFor = groups.Count;
            railMeasuredKey = key;

            GameFont font = Text.Font;

            try
            {
                float widest = 0f;

                for (int i = 0; i < groups.Count; i++)
                {
                    ResearchGroup group = groups[i];

                    // Each half measured at the font it is drawn in -- the tally at Tiny, the label at Small.
                    // One measurement taken in the wrong font is exactly what truncated the node captions.
                    Text.Font = GameFont.Tiny;

                    float tally = UIRichText.WidthOf(Finished(group) + "/" + group.Nodes.Count);

                    // Measured bolded, because that is how it is drawn: bold is wider, and measuring the plain
                    // string would size the rail to text it never renders.
                    Text.Font = GameFont.Small;

                    widest = Mathf.Max(widest, UIRichText.WidthOf("<b>" + group.Label + "</b>") + tally);
                }

                // 10 for the tick and its gap, 6 between label and tally, 4 for the tally's own right margin,
                // 18 for the scrollbar, 2 for the rail's border.
                railMeasured = Mathf.Clamp(widest + 40f, MinRailWidth, MaxRailWidth);
            }
            finally
            {
                Text.Font = font;
            }

            return railMeasured;
        }

        /// <summary>What the rail is a list of, for its own heading.</summary>
        private static string RailHeading()
        {
            switch (ResearchGroupings.Current)
            {
                case ResearchGrouping.Source:
                    return "SOURCES";

                case ResearchGrouping.Tech:
                    return "TECH LEVELS";

                default:
                    return "THEMES";
            }
        }

        private static void ContentsRail(Rect rail, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rail, palette.Border, palette.PanelBackground);

            List<ResearchGroup> groups = ResearchGraph.Groups;

            Rect fold = new Rect(rail.x + 1f, rail.y + 1f, rail.width - 2f, 16f);

            if (Mouse.IsOver(fold))
                Widgets.DrawHighlight(fold);

            Text.Font = GameFont.Tiny;
            Text.Anchor = railFolded ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            GUI.color = palette.TextDisabled;

            // The caption goes with the rows under it. A heading in one typeface over a list in another reads as
            // two panels rather than one, which is the whole reason a face is chosen for a block and not a line.
            UITextControl.Label(railFolded ? fold : new Rect(fold.x + 5f, fold.y, fold.width - 5f, fold.height),
                railFolded ? ">>" : RailHeading(), UIFace.BarlowCondensed, GameFont.Tiny, FontStyle.Bold);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(fold,
                (TipSignal) (railFolded ? "Show the contents rail." : "Fold the contents rail."));

            if (Widgets.ButtonInvisible(fold))
            {
                railFolded = !railFolded;

                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            Rect body = new Rect(rail.x + 1f, fold.yMax + 2f, rail.width - 2f, rail.yMax - fold.yMax - 4f);

            // Rows are five pixels taller than their text when open, because each carries a progress hairline
            // underneath. Folded, a row is the colour tick and nothing else.
            float rowHeight = railFolded ? 14f : RailRowHeight + RailRowGap;
            float bar = groups.Count * rowHeight > body.height ? 18f : 0f;

            Rect view = new Rect(0f, 0f, body.width - bar, groups.Count * rowHeight);

            Widgets.BeginScrollView(body, ref railScroll, view);

            try
            {
                for (int i = 0; i < groups.Count; i++)
                    RailRow(new Rect(0f, i * rowHeight, view.width, rowHeight), groups[i], palette);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static void RailRow(Rect row, ResearchGroup group, UIColorPaletteDef palette)
        {
            Color tint = group.Band.HasValue
                ? ResearchBands.ColorFor(group.Band.Value, palette)
                : palette.TextSecondary;

            int done = Finished(group);

            bool here = selected != null && selected.Group == group;

            if (here)
                Widgets.DrawBoxSolid(row, palette.AccentMuted);
            else if (Mouse.IsOver(row))
                Widgets.DrawHighlight(row);

            Widgets.DrawBoxSolid(new Rect(row.x + 2f, row.y + 3f, 4f, Mathf.Max(4f, row.height - 8f)), tint);

            if (!railFolded)
            {
                Text.Font = GameFont.Tiny;

                string tally = done + "/" + group.Nodes.Count;
                float tallyWidth = UIRichText.WidthOf(tally) + 4f;

                // Measured at Tiny above, because that is what the tally draws at; the label below switches to
                // Small. Two fonts in one row means two measurements, and taking one number from the wrong font
                // is the fault that truncated the node captions.

                GUI.color = here ? palette.TextSecondary : palette.TextDisabled;
                Text.Anchor = TextAnchor.MiddleRight;

                UITextControl.Label(new Rect(row.xMax - tallyWidth - 3f, row.y, tallyWidth, RailRowHeight),
                    tally, UIFace.BarlowCondensed, GameFont.Tiny);

                // The label in the band's own colour, not grey. A four pixel tick is not enough of a hue to read
                // as "this is the medicine band" at a glance, and the canvas heading it jumps to is coloured, so
                // a grey rail was the one place a band had no colour. Grouped by source or tech level there is no
                // band, tint is the ordinary secondary text, and this is a no-op.
                GUI.color = here && !group.Band.HasValue ? palette.TextPrimary : tint;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;

                // Bold as a face rather than as a tag. The markup was the only way to get weight out of
                // RimWorld's one font; with the sheet shipped, the weight is the typeface and the string is just
                // the name -- which also means UITextControl can set it, since it refuses text carrying markup.
                // Widths are still measured against the game's font above, so the rail keeps the size it had and
                // the condensed face simply has more room in it than it needs.
                UITextControl.LabelEllipses(new Rect(row.x + 11f, row.y,
                        Mathf.Max(0f, row.width - tallyWidth - 17f), RailRowHeight),
                    group.Label, UIFace.BarlowCondensed, GameFont.Small, FontStyle.Bold);

                Text.Anchor = TextAnchor.UpperLeft;

                // The hairline, drawn even at zero: a row with no track under it reads as a row that does not
                // have progress rather than one that has none yet.
                // Pinned under the text rather than to the bottom of the cell, so the extra air added below goes
                // between one entry and the next instead of between a label and its own progress bar.
                Rect track = new Rect(row.x + 11f, row.y + RailRowHeight - 1f,
                    Mathf.Max(0f, row.width - 17f), 2f);

                Widgets.DrawBoxSolid(track, palette.SurfaceSunken);

                if (done > 0 && group.Nodes.Count > 0)
                    Widgets.DrawBoxSolid(
                        new Rect(track.x, track.y, track.width * done / group.Nodes.Count, track.height),
                        new Color(tint.r, tint.g, tint.b, 0.85f));
            }

            GUI.color = palette.TextPrimary;

            TooltipHandler.TipRegion(row, new TipSignal(
                () => group.Label + "\n" + done + " of " + group.Nodes.Count + " finished"
                      + (group.Band.HasValue
                          ? "\n\n" + ResearchBands.Info(group.Band.Value).Tooltip
                          : string.Empty),
                group.Label.GetHashCode()));

            if (Widgets.ButtonInvisible(row))
                JumpTo(group);
        }

        /// <summary>
        /// Scrolls the canvas to a block's heading.
        ///
        /// <b>Scrolls and does not select.</b> Clicking a band means "show me this", not "I have chosen a
        /// project", and selecting the block's first node would open the detail panel, narrow the canvas, and move
        /// the very thing the player was just told they were being taken to. The same rule as the colonist bar:
        /// click to select, then click to jump, and never both at once.
        ///
        /// The heading is put a little way down from the top rather than flush with it, so the block reads as
        /// having arrived rather than as being cut off above.
        /// </summary>
        private static void JumpTo(ResearchGroup group)
        {
            if (group == null)
                return;

            canvasScroll.y = Mathf.Max(0f, group.Header.y - 12f);
            canvasScroll.x = 0f;

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>How many of a block's projects are finished. Knowledge projects count; they finish too.</summary>
        private static int Finished(ResearchGroup group)
        {
            if (group == null)
                return 0;

            int done = 0;

            for (int i = 0; i < group.Nodes.Count; i++)
            {
                ResearchProjectDef project = group.Nodes[i].Project;

                if (project != null && project.IsFinished)
                    done++;
            }

            return done;
        }

        /// <summary>
        /// "6 of 15" for a block, or empty when it holds nothing finished and nothing to finish.
        ///
        /// Ghosts are counted in the total because they are drawn: a total that disagreed with the number of nodes
        /// somebody can see is worse than no total. Knowledge projects count too -- they finish, just not with
        /// research points.
        /// </summary>
        private static string Tally(ResearchGroup group)
        {
            if (group == null || group.Nodes.Count == 0)
                return string.Empty;

            return Finished(group) + " of " + group.Nodes.Count;
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

                // Excluded by a filter or the search: not drawn, and so not clickable either, which is the point.
                // A ghost that still answered a click was a card the player had asked not to see.
                if (!Passes(node))
                    continue;

                // Nothing dims any more. The flag stays on the drawing calls because a difficulty-hidden ghost
                // still wants the faded treatment, and that is decided from the node's own state rather than from
                // a filter -- see ResearchState.Ghost.
                const bool dimmed = false;

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

        /// <summary>
        /// Selects a project and, when asked, scrolls the canvas until it is on screen.
        /// </summary>
        /// <param name="reveal">
        /// <b>False for a click on the graph and true for a link out of the detail panel,</b> which is the whole
        /// distinction. A node the player has just clicked is by definition already in front of them, and moving
        /// the canvas under a click would be a small earthquake for no reason. A project reached by name from the
        /// Requires or Leads to list is very often nowhere near the view, and selecting it without going there
        /// would highlight something the player cannot see.
        /// </param>
        private static void Select(ResearchNode node, bool reveal = false)
        {
            selected = node;
            detailScroll = Vector2.zero;

            Highlight(node);

            if (reveal)
                Reveal(node);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Scrolls the canvas so a node sits in the middle of it.
        ///
        /// Clamped at zero because a node near the top left corner would otherwise ask for a negative scroll,
        /// which Unity accepts and then draws as blank space above the graph. Does nothing until the canvas has
        /// drawn at least once and so has a size worth centring against.
        /// </summary>
        private static void Reveal(ResearchNode node)
        {
            UIGuard.Try("Research.Reveal", () =>
            {
                if (node == null || lastCanvas.width <= 0f || lastCanvas.height <= 0f)
                    return;

                float scale = overview ? OverviewScale : 1f;

                Rect placed = Scaled(node.Rect, scale);

                canvasScroll = new Vector2(
                    Mathf.Max(0f, placed.center.x - lastCanvas.width * 0.5f),
                    Mathf.Max(0f, placed.center.y - lastCanvas.height * 0.5f));
            }, null);
        }

        /// <summary>
        /// A row in the detail panel that goes somewhere when clicked.
        ///
        /// <b>Reads as a row until the pointer is on it,</b> rather than being permanently underlined or
        /// coloured. These lists are read far more often than they are clicked -- the usual question is "what
        /// does this unlock", answered by reading -- so styling every line as a link all the time would put
        /// twenty pieces of emphasis on a panel to advertise an action nobody was looking for.
        /// </summary>
        private static bool Link(Rect row, string text, Color color, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(row);

            if (over)
                Widgets.DrawHighlight(row);

            TabParts.RowLabel(row, text, over ? palette.Accent : color, GameFont.Tiny);

            return over && Widgets.ButtonInvisible(row);
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
        /// <b>A node that fails is not drawn at all, from 2026-08-23.</b> It used to be dimmed, on the argument
        /// that removing one leaves a hole in a chain and a hole reads as a missing mod. Aaron asked for it gone
        /// and the argument no longer holds: the arrows that made a chain visible are gone too, so there is no
        /// chain left for a hole to interrupt -- and a filter that only fades what it excludes leaves a canvas of
        /// three hundred ghosts on a mature colony, which is the thing the filter was for.
        ///
        /// <b>The layout does not reflow,</b> so hiding leaves gaps where the excluded cards were. That is the
        /// deliberate half of the trade: relaying the graph on every toggle would move every card the player is
        /// looking at, and the signature that decides the layout is kept free of progress on purpose.
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
            // The filed row: the band and source line, plus the reason, which wraps. Reasons run to about a
            // hundred and forty characters at their longest ("It unlocks the surgery install bionic arm.") so
            // three lines of tiny text is the honest reserve; TabParts.Note returns where it actually ended, so
            // over-reserving costs a little scroll space and under-reserving clips nothing.
            // The filed block: the band inset (26), the reason, which wraps to about three lines of tiny text
            // (42), the Source heading (22) and its row (20). TabParts.Note returns where it actually ended, so
            // over-reserving costs a little scroll space while under-reserving would clip the sections below it.
            float height = 30f + 18f + 26f + (masked ? 0f : 26f + 42f + 6f + 22f + 20f);

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

            // Where it was filed, and why. Below the progress bar and above the description, because it answers
            // "why is this here and not where I looked for it" -- the question the banded canvas creates, and the
            // one thing that makes a placement you disagree with legible rather than arbitrary. Masked projects
            // are skipped: the reason names what a project unlocks, and not knowing that yet is what masked
            // means.
            if (!masked)
                y = Filed(body, y, project, palette);

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

        /// <summary>
        /// The band this project was filed in, one sentence saying why, and where it came from.
        ///
        /// <b>Built to the mockup's shape on 2026-08-23.</b> The first cut put the band and the mod on one
        /// eighteen-pixel row and it read as a caption rather than as a fact about the project. The mockup had the
        /// band in a bordered inset -- the same device the queue rail uses for a row -- with the reason beneath it
        /// and the source under its own small heading, and that reads as three answers instead of one crowded one.
        ///
        /// <b>The reason is why this exists at all.</b> A project sits in exactly one band and some genuinely
        /// belong to two: bionic replacements is a surgery and a crafting recipe both. So a placement will
        /// sometimes look wrong and be correct by the rule, and printing which unlock decided it turns that from an
        /// argument with the mod into a fact the player can read. See <see cref="ResearchTaxonomy"/>.
        ///
        /// Drawn whatever the grouping is: the band is what the project is <em>about</em>, and that stays
        /// interesting when the canvas happens to be cut by mod instead.
        /// </summary>
        private static float Filed(Rect body, float y, ResearchProjectDef project, UIColorPaletteDef palette)
        {
            ResearchBand band = ResearchTaxonomy.BandOf(project);
            Color tint = ResearchBands.ColorFor(band, palette);

            Rect box = new Rect(body.x, y, body.width, 22f);

            UIElementPainter.Outline(box, palette.Border, palette.SurfaceSunken);

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                Widgets.DrawBoxSolid(new Rect(box.x + 6f, box.center.y - 4f, 8f, 8f), tint);

                GUI.color = tint;

                UIRichText.Label(new Rect(box.x + 19f, box.y, box.width - 24f, box.height),
                    "<b>" + ResearchBands.LabelOf(band) + "</b>");

                y += 26f;

                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
                Text.Font = font;
            }

            y = TabParts.Note(new Rect(body.x, y, body.width, 0f), y, ResearchTaxonomy.ReasonFor(project),
                palette, GameFont.Tiny, palette.TextDisabled);

            y += 6f;

            y = Section(body, y, "Source", palette);

            Rect source = new Rect(body.x, y, body.width, 16f);

            Widgets.DrawBoxSolid(new Rect(source.x + 2f, source.y + 3f, 3f, 10f),
                ResearchSourceMarks.ColorFor(project, palette));

            TabParts.RowLabel(new Rect(source.x + 11f, source.y, source.width - 11f, source.height),
                ResearchSourceMarks.NameFor(project), palette.TextSecondary, GameFont.Tiny);

            return y + 20f;
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
                {
                    // Masked rows stay inert. A link out of a row whose text is deliberately scrambled would
                    // hand the player the answer the mask exists to withhold.
                    ResearchMask.Draw(row, ResearchMask.Key(project, "unlock", i), palette.Mood);
                }
                else
                {
                    Def unlock = unlocked[i];

                    if (Link(row, unlock.LabelCap.ToString(), palette.TextSecondary, palette))
                    {
                        // Dialog_InfoCard takes a bare Def, which is what UnlockedDefs holds: a research project
                        // unlocks buildings, terrain, recipes and plants alike, and the card knows what to make
                        // of each.
                        UIGuard.Try("Research.UnlockCard",
                            () => Find.WindowStack.Add(new Dialog_InfoCard(unlock)),
                            "That item's info card did not open.");
                    }
                }

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
                {
                    ResearchMask.Draw(row, ResearchMask.Key(selected.Project, "leads", i), palette.Mood);
                }
                else
                {
                    ResearchNode child = children[i];

                    if (Link(row, ResearchFacts.Name(child.Project), palette.TextSecondary, palette))
                        Select(child, true);
                }

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

                    ResearchProjectDef needed = list[i];

                    // The tick keeps its own space at the left, so the link starts where the label always did
                    // and a finished prerequisite and an unfinished one line up with each other.
                    if (Link(new Rect(row.x + 14f, row.y, row.width - 14f, row.height),
                            ResearchFacts.Name(needed), done ? palette.TextSecondary : palette.TextPrimary,
                            palette))
                    {
                        ResearchNode node = UIGuard.Try("Research.PrerequisiteNode",
                            () => ResearchGraph.NodeFor(needed), null, null);

                        // A prerequisite with no node is one the graph did not lay out -- a hidden prerequisite
                        // from a mod, most often. Nothing to go to, so the click does nothing rather than
                        // selecting null and blanking the panel.
                        if (node != null)
                            Select(node, true);
                    }
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
