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
        /// The text row itself, measured from the face rather than written down.
        ///
        /// The rail labels are names to read rather than figures to scan, which is why they take the larger of
        /// the two rail sizes. Measured through the control that draws them: a row sized against one face and
        /// filled with another is either clipped or padded, and which of the two depends on the words.
        /// </summary>
        private static float RailRowHeight
        {
            get
            {
                return Mathf.Ceil(
                    UITextControl.LineHeight(ResearchFaces.Condensed, ResearchFaces.Size.RailName) + 2f);
            }
        }

        /// <summary>
        /// Air below each rail row, on top of the row and its progress hairline.
        ///
        /// Ten rather than five from 2026-08-23: at five, twelve rows of coloured text with a coloured bar under
        /// each read as one striped block instead of twelve entries. The gap is what makes a row a row.
        ///
        /// Back down to four now that the labels are no longer tinted. The ten was buying separation from the
        /// striping, and with the colour on the swatch alone there is no striping left to separate from; a rail
        /// that spent ninety pixels on air was paying for a problem it no longer has.
        /// </summary>
        private const float RailRowGap = 4f;

        /// <summary>
        /// The block that names the tab, on <c>SurfaceSunken</c>, the same shape every restyled tab opens with.
        ///
        /// Sixty-two rather than the hospital tab's sixty-six: this header carries four figures and no controls,
        /// and the canvas under it is the thing the screen is for.
        /// </summary>
        private const float HeaderHeight = 62f;

        private const float GlyphSize = 30f;

        private const float GlyphGap = 10f;

        private const float ControlHeight = 26f;

        /// <summary>Between two chips in the filter strip.</summary>
        private const float ChipGap = 4f;

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
        private static bool queueDragging;
        private static float queueDragOffset;

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
        private static bool railDragging;
        private static float railDragOffset;

        /// <summary>Which queue row is being dragged, or -1. Cleared on mouse up wherever that happens.</summary>
        private static int dragFrom = -1;

        /// <summary>
        /// The project being dragged, held by identity rather than by its place.
        ///
        /// <b>The row number and the queue number are not the same number.</b> A row carries its place in the
        /// main lane, and the queue it reorders also holds the anomaly projects, so the two diverge the moment
        /// anything from Anomaly is queued -- a drag would then move whichever project happened to be sitting
        /// at the row number. The queue is also free to change under a drag, when a project finishes. Both are
        /// answered by carrying the project itself and asking the queue where it is on the drop.
        /// </summary>
        private static ResearchProjectDef dragProject;

        /// <summary>Where the drop would land, as a gap in the main lane, or -1 for nowhere.</summary>
        private static int dragTo = -1;

        /// <summary>What the dragged row says, so it can be drawn again under the cursor.</summary>
        private static string dragLabel;

        /// <summary>Width of the grip, shared by the glyph and the rect that grabs it.</summary>
        private const float GripWidth = 16f;

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

            // One walk of the graph, at the top of the frame, feeding the header's figures and the strip's
            // counts. Both used to be uncounted, and counting them twice in one frame would be the obvious way
            // to make a three hundred node canvas cost twice what it needs to.
            Count();

            Rect header = new Rect(content.x, content.y, content.width, HeaderHeight);

            Header(header, palette);

            Rect strip = new Rect(content.x, header.yMax + Gap, content.width, ControlHeight);

            Toolbar(strip, palette);

            float top = strip.yMax + Gap;
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

            // Anywhere, because a drag that ends over the canvas or off the window still ends. The queue
            // rail has already had its chance to land the drop by this point; this is the case where the
            // release happened somewhere the rail never saw.
            if (dragFrom >= 0 && Event.current.type == EventType.MouseUp)
                ClearDrag();
        }

        // ---------------------------------------------------------------------------------------
        // Header
        // ---------------------------------------------------------------------------------------

        /// <summary>The tab's own mark, the same texture its button on the bar uses.</summary>
        private static readonly Texture2D Glyph;

        static ResearchPanel()
        {
            // Through a local, because a readonly field can only be assigned in the constructor itself and the
            // guard does its work in a closure.
            Texture2D glyph = null;

            UIGuard.Try("Research.Glyph",
                () => glyph = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Research", false),
                "The header has no glyph this session. Everything on the tab still reads.");

            Glyph = glyph;
        }

        /// <summary>
        /// The block that names the screen, with the colony's research figures seated in it.
        ///
        /// <b>The same shape every restyled tab uses.</b> What was a framed toolbar of a search box and nine
        /// filled toggles is now a header saying where you are and how the work is going, and a strip below it
        /// holding nothing but controls.
        ///
        /// <b>The four figures are the ones somebody opens this tab to find out,</b> and every one of them was
        /// previously only reachable by looking for a single blue node among three hundred: how far through the
        /// game's projects the colony is, how fast it is going, when the current project lands, and how long
        /// everything planned will take.
        /// </summary>
        private static void Header(Rect rect, UIColorPaletteDef palette)
        {
            // SurfaceSunken, the same fill the two rails beside it use: header and rails are both chrome framing
            // the canvas, so they share a surface and the canvas sits above it.
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(10f);

            float text = inner.x;

            if (Glyph != null)
            {
                Rect mark = new Rect(inner.x, inner.y + (inner.height - GlyphSize) * 0.5f, GlyphSize, GlyphSize);

                Color previous = GUI.color;

                GUI.color = ResearchFaces.AccentOf(palette);
                GUI.DrawTexture(mark, Glyph);
                GUI.color = previous;

                text = mark.xMax + GlyphGap;
            }

            // The figures first, because they are the thing that cannot be shortened: they come out at whatever
            // width the colony's numbers happen to be, and the two lines of text beside them get what is left.
            // Sized the other way round, a long subtitle on a small screen runs underneath them.
            float wall = Readouts(inner, palette) - 12f;

            float titleWidth = Mathf.Max(0f, Mathf.Min(300f, wall - text));
            float subtitleWidth = Mathf.Max(0f, Mathf.Min(460f, wall - text));

            TabParts.RowLabel(new Rect(text, inner.y, titleWidth, 24f), "Research",
                ResearchFaces.AccentOf(palette), ResearchFaces.Display, ResearchFaces.Size.Title);

            TabParts.RowLabel(new Rect(text, inner.y + 23f, subtitleWidth, 18f), Subtitle(),
                palette.TextSecondary, ResearchFaces.Condensed, ResearchFaces.Size.Subtitle);
        }

        /// <summary>
        /// The line under the title: what the canvas is currently cut by, and what the colony is working on.
        ///
        /// <b>The current project belongs here and nowhere else.</b> It is one node somewhere on a canvas of
        /// three hundred, and finding it meant either scrolling for the blue one or knowing where it was.
        /// </summary>
        private static string Subtitle()
        {
            return UIGuard.Try("Research.Subtitle", () =>
            {
                string by = "By " + ResearchGroupings.LabelOf(ResearchGroupings.Current).ToLowerInvariant()
                            + "  -  " + ResearchGraph.Groups.Count + " blocks";

                ResearchProjectDef current = Find.ResearchManager != null
                    ? Find.ResearchManager.GetProject()
                    : null;

                return current == null
                    ? by + "  -  nothing being researched"
                    : by + "  -  working on " + current.LabelCap;
            }, "The whole tech tree, by theme", null);
        }

        /// <summary>
        /// The figures, right to left.
        ///
        /// <b>Rate reads in the danger color at nought,</b> whatever else is true. A colony with nobody assigned
        /// to research has a queue that will never move, and that is the whole story of the screen; it was not
        /// told anywhere at all before.
        /// </summary>
        /// <summary>Returns the left edge of the leftmost figure, which is the wall the title must stop at.</summary>
        private static float Readouts(Rect area, UIColorPaletteDef palette)
        {
            float x = area.xMax;
            float rate = ResearchRate.PointsPerDay;

            x = Readout(area, x, "queued", queuedDays, palette,
                "How long everything in the queue takes at the colony's current rate.");

            x = Readout(area, x, "current", CurrentDays(), palette,
                "How long the project being researched has left.");

            x = Readout(area, x, "rate", Mathf.RoundToInt(rate).ToString(), palette,
                "Research points a day, at the colony's current assignments and benches.",
                rate <= 0f ? palette.Danger : (Color?) null);

            return Readout(area, x, "projects", doneCount + " / " + totalCount, palette,
                "Projects finished, out of everything on the canvas.");
        }

        /// <summary>
        /// One right-aligned caption over a figure, in the mono, returning the x the next one ends at.
        ///
        /// <b>Not <c>TabParts.Readout</c>,</b> which measures and draws in the game font. Four figures side by
        /// side are a row of numbers to compare, and this is the tab that just moved every other number it draws
        /// onto the mono; a header that did not follow would be the one place on the screen where digits do not
        /// line up.
        /// </summary>
        private static float Readout(Rect bar, float right, string caption, string value,
            UIColorPaletteDef palette, string tip = null, Color? valueColor = null)
        {
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            bool wrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;

                float width = Mathf.Max(
                    UITextControl.Width(caption ?? string.Empty, ResearchFaces.Mono, ResearchFaces.Size.Caption),
                    UITextControl.Width(value ?? string.Empty, ResearchFaces.Mono, ResearchFaces.Size.Readout))
                    + 20f;

                Rect cell = new Rect(right - width, bar.y, width, bar.height);
                float valueHeight = UITextControl.LineHeight(ResearchFaces.Mono, ResearchFaces.Size.Readout);

                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = valueColor ?? palette.TextPrimary;

                UITextControl.Label(new Rect(cell.x, cell.y, cell.width - 6f, valueHeight + 2f), value,
                    ResearchFaces.Mono, ResearchFaces.Size.Readout);

                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = palette.TextDisabled;

                UITextControl.Label(new Rect(cell.x, cell.y + valueHeight + 3f, cell.width - 6f, 14f),
                    caption.ToUpperInvariant(), ResearchFaces.Mono, ResearchFaces.Size.Caption);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(cell, (TipSignal) tip);

                return cell.x;
            }
            finally
            {
                Text.WordWrap = wrap;
                GUI.color = color;
                Text.Anchor = anchor;
            }
        }

        /// <summary>How long the project being researched has left, or a dash when there is none.</summary>
        private static string CurrentDays()
        {
            ResearchProjectDef current = Find.ResearchManager != null
                ? Find.ResearchManager.GetProject()
                : null;

            if (current == null)
                return "-";

            float days = ResearchRate.DaysFor(current);

            return days < 0f ? "-" : ResearchRate.Days(days);
        }

        // ---------------------------------------------------------------------------------------
        // Counts
        //
        // Every figure the header and the strip show, from one walk of the graph at the top of the frame.
        // Nothing here is cached across frames: finishing a project changes six of these at once, and a cache
        // keyed on the node count -- which is what LevelsPresent can safely use -- would not notice.
        // ---------------------------------------------------------------------------------------

        private static int doneCount;

        private static int totalCount;

        private static int availableCount;

        private static int lockedCount;

        private static int anomalyCount;

        private static string queuedDays = "-";

        private static readonly Dictionary<TechLevel, int> levelCounts = new Dictionary<TechLevel, int>();

        private static void Count()
        {
            List<ResearchNode> nodes = ResearchGraph.Nodes;

            doneCount = 0;
            totalCount = nodes.Count;
            availableCount = 0;
            lockedCount = 0;
            anomalyCount = 0;

            levelCounts.Clear();

            for (int i = 0; i < nodes.Count; i++)
            {
                ResearchNode node = nodes[i];
                ResearchProjectDef project = node.Project;

                if (project == null)
                    continue;

                // Counted alongside its state rather than instead of it, because the Anomaly chip is a category
                // and the other three are states: a knowledge project is both anomalous and available, and a
                // count that pretended otherwise would not add up to the canvas.
                if (project.knowledgeCategory != null)
                {
                    anomalyCount++;
                }
                else if (project.techLevel != TechLevel.Undefined)
                {
                    int already;

                    levelCounts.TryGetValue(project.techLevel, out already);
                    levelCounts[project.techLevel] = already + 1;
                }

                if (project.IsFinished)
                {
                    doneCount++;

                    continue;
                }

                ResearchState state = ResearchFacts.StateOf(node);

                if (state == ResearchState.Ready || state == ResearchState.Researching)
                    availableCount++;
                else
                    lockedCount++;
            }

            GameComponent_ResearchQueue queue = GameComponent_ResearchQueue.Current;

            queuedDays = "-";

            if (queue == null || queue.Count == 0)
                return;

            List<ResearchProjectDef> planned = mainLane;

            planned.Clear();

            for (int i = 0; i < queue.Entries.Count; i++)
            {
                ResearchProjectDef project = queue.Entries[i];

                if (project != null && project.knowledgeCategory == null)
                    planned.Add(project);
            }

            queuedDays = TotalDays(planned);

            planned.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // Filter strip
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The controls, on the window rather than in a frame of their own.
        ///
        /// <b>Unfilled chips, from the pawns tab.</b> Nine toggles inverting to a solid accent put the nine
        /// brightest rectangles on the screen directly above a canvas that is itself twelve colors, and
        /// emphasising everything emphasises nothing. Each chip now carries a bar in the color the thing it
        /// filters is drawn in, and its count.
        ///
        /// <b>The states take the colors the nodes already use.</b> Available is accent, Done is success,
        /// Locked is disabled, Anomaly is mood, and none of those are new values: they are
        /// <c>ResearchFacts.ColorFor</c>, which paints every stripe on the canvas. So the bar on a chip and the
        /// stripe on the nodes it governs are the same color for the same reason, which is the argument for a
        /// legend rather than a row of buttons.
        ///
        /// <b>"All" is gone.</b> It was a fourth control that only ever meant "turn the other three on", and
        /// with counts present, three chips all reading as on say that more directly than a button that looks
        /// pressed whether or not it did anything.
        ///
        /// <b>Anomaly moved left, into the states.</b> It sat in the tech level run and is not a tech level: it
        /// is a category of project, exactly like the three it now stands beside.
        ///
        /// <b>Group by and the view switch became segments.</b> Both are one-of choices about what the canvas
        /// <i>is</i>, and a chip's contract is that you can hold several down at once.
        /// </summary>
        private static void Toolbar(Rect bar, UIColorPaletteDef palette)
        {
            float x = bar.x;

            Search.Draw(new Rect(x, bar.y, 178f, bar.height), palette);

            if (Search.Text != query)
            {
                query = Search.Text ?? string.Empty;
                matches.Clear();
            }

            x += 178f + 8f;

            x = StateChip(x, bar, "Available", availableCount, showAvailable, palette.Accent, palette,
                () => showAvailable = !showAvailable, "Projects that can be started now.");

            x = StateChip(x, bar, "Locked", lockedCount, showLocked, palette.TextDisabled, palette,
                () => showLocked = !showLocked, "Projects still waiting on something.");

            x = StateChip(x, bar, "Done", doneCount, showDone, palette.Success, palette,
                () => showDone = !showDone, "Projects already finished.");

            if (ModsConfig.AnomalyActive)
            {
                x = StateChip(x, bar, "Anomaly", anomalyCount, showAnomaly, palette.Mood, palette,
                    () => showAnomaly = !showAnomaly, "Anomaly's knowledge projects.");
            }

            x += 8f;
            x = Caption(x, bar, "Group by", palette);

            ResearchGrouping grouping = ResearchGroupings.Current;

            for (int i = 0; i < ResearchGroupings.All.Length; i++)
            {
                ResearchGrouping which = ResearchGroupings.All[i];
                ResearchGrouping chosen = which;

                x = SegmentTab(x, bar, ResearchGroupings.LabelOf(which), grouping == which, palette,
                    () => ResearchGroupings.Set(chosen), ResearchGroupings.TooltipOf(which));
            }

            // From the right: the view switch, then the tech levels, so the levels grow leftwards into whatever
            // room is left rather than colliding with the states.
            float right = bar.xMax;

            right = SegmentTab(right, bar, "Overview", overview, palette, () => overview = true,
                "Blocks instead of nodes, for the shape of the whole tree.", true);

            right = SegmentTab(right, bar, "Tree", !overview, palette, () => overview = false,
                "The full canvas, with every project named.", true);

            right -= 10f;

            List<TechLevel> levels = LevelsPresent();
            bool anyLevel = false;

            for (int i = levels.Count - 1; i >= 0; i--)
            {
                TechLevel level = levels[i];
                TechLevel chosen = level;
                string label = level.ToStringHuman().CapitalizeFirst();
                int count;

                levelCounts.TryGetValue(level, out count);

                string figure = count.ToString();
                float width = TabParts.FilterChipWidth(label, figure, ResearchFaces.Condensed,
                    ResearchFaces.Size.Chip, ResearchFaces.Mono, ResearchFaces.Size.RailCount);

                // Stops rather than clipping. A chip drawn under another chip is a control that silently does
                // the wrong thing when clicked, which is the fault the pawns tab hit test taught.
                if (right - width - ChipGap < x + 60f)
                    break;

                right -= width;

                if (TabParts.FilterChip(new Rect(right, bar.y, width, bar.height), label, figure,
                        !hiddenLevels.Contains(level), null, palette, ResearchFaces.Condensed,
                        ResearchFaces.Size.Chip, ResearchFaces.Mono, ResearchFaces.Size.RailCount))
                {
                    if (!hiddenLevels.Remove(chosen))
                        hiddenLevels.Add(chosen);

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                right -= ChipGap;
                anyLevel = true;
            }

            // Only when there is something for it to name. On a narrow window the loop above stops early, and a
            // caption reading "Tech" with no chips beside it is worse than no caption: it says a control is
            // there and it is not.
            if (anyLevel)
                Caption(right - CaptionWidth("Tech"), bar, "Tech", palette);
        }

        /// <summary>One state chip, and the x the next one starts at.</summary>
        private static float StateChip(float x, Rect bar, string label, int count, bool on, Color color,
            UIColorPaletteDef palette, System.Action toggled, string tip)
        {
            string figure = count.ToString();
            float width = TabParts.FilterChipWidth(label, figure, ResearchFaces.Condensed,
                ResearchFaces.Size.Chip, ResearchFaces.Mono, ResearchFaces.Size.RailCount);

            if (TabParts.FilterChip(new Rect(x, bar.y, width, bar.height), label, figure, on, color, palette,
                    ResearchFaces.Condensed, ResearchFaces.Size.Chip, ResearchFaces.Mono,
                    ResearchFaces.Size.RailCount, tip))
            {
                toggled();

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return x + width + ChipGap;
        }

        /// <summary>
        /// One segment of a one-of choice: a word, underlined in the tab's color while it is the chosen one.
        ///
        /// <b>An underline rather than a fill or a frame.</b> These sit in the same strip as the chips, and a
        /// filled segment beside an unfilled chip reads as the more important control rather than as a
        /// different kind of one. The underline is also where the tab's identity color gets a second job.
        ///
        /// <b>Clicking the chosen one does nothing,</b> which is a segment's contract and the reason the state
        /// filters are not segments.
        /// </summary>
        private static float SegmentTab(float x, Rect bar, string label, bool on, UIColorPaletteDef palette,
            System.Action chosen, string tip, bool rightToLeft = false)
        {
            float width = UITextControl.Width(label, ResearchFaces.Condensed, ResearchFaces.Size.Chip) + 18f;
            Rect rect = new Rect(rightToLeft ? x - width : x, bar.y, width, bar.height);

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            bool wrap = Text.WordWrap;

            try
            {
                bool over = Mouse.IsOver(rect);

                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = on ? palette.TextPrimary : over ? palette.TextSecondary : palette.TextDisabled;

                UITextControl.Label(new Rect(rect.x, rect.y - 2f, rect.width, rect.height), label,
                    ResearchFaces.Condensed, ResearchFaces.Size.Chip);

                if (on)
                {
                    Widgets.DrawBoxSolid(new Rect(rect.x + 3f, rect.yMax - 2f, rect.width - 6f, 2f),
                        ResearchFaces.AccentOf(palette));
                }
            }
            finally
            {
                Text.WordWrap = wrap;
                GUI.color = color;
                Text.Anchor = anchor;
            }

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tip);

            if (Widgets.ButtonInvisible(rect) && !on)
            {
                chosen();

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return rightToLeft ? rect.x : rect.xMax;
        }

        /// <summary>A small caps caption naming the run of controls beside it.</summary>
        private static float Caption(float x, Rect bar, string label, UIColorPaletteDef palette)
        {
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextDisabled;

                UITextControl.Label(new Rect(x, bar.y, CaptionWidth(label), bar.height),
                    label.ToUpperInvariant(), ResearchFaces.Mono, ResearchFaces.Size.Caption);
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
            }

            return x + CaptionWidth(label);
        }

        private static float CaptionWidth(string label)
        {
            return UITextControl.Width(label.ToUpperInvariant(), ResearchFaces.Mono,
                ResearchFaces.Size.Caption) + 10f;
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

            Color color = GUI.color;

            try
            {
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
                    float width = UITextControl.Width(caption, ResearchFaces.Condensed, ResearchFaces.Size.Band,
                                      FontStyle.Bold) + 6f;

                    UITextControl.Label(new Rect(x, band.y, width, band.height), caption,
                        ResearchFaces.Condensed, ResearchFaces.Size.Band, FontStyle.Bold);

                    x += width;

                    // Done of total, in the quieter colour, so a band says how far through it you are without
                    // anybody having to count nodes. Off in the overview, which has no room for it.
                    string tally = Tally(group);

                    if (!tally.NullOrEmpty())
                    {
                        GUI.color = palette.TextDisabled;

                        // In the mono, like every other figure on this tab. Eleven headings down a canvas put
                        // their tallies in a ragged column when they were set in the game's proportional font,
                        // and "3 of 68" beside "18 of 68" could not be compared at a glance.
                        float tallyWidth = UITextControl.Width(tally, ResearchFaces.Mono,
                            ResearchFaces.Size.Figure) + 8f;

                        UITextControl.Label(new Rect(x, band.y + 2f, tallyWidth, band.height), tally,
                            ResearchFaces.Mono, ResearchFaces.Size.Figure);

                        x += tallyWidth;
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

            float widest = 0f;

            for (int i = 0; i < groups.Count; i++)
            {
                ResearchGroup group = groups[i];

                // Each half measured in the face it is drawn in: the label in the condensed face at its own
                // point size, the tally in the mono at its. One measurement taken in the wrong font is exactly
                // what truncated the node captions, and both halves changed face when the rows did -- the
                // label is no longer bold and the tally is no longer the game's own font.
                float tally = UITextControl.Width(Finished(group) + "/" + group.Nodes.Count,
                    ResearchFaces.Mono, ResearchFaces.Size.RailCount);

                float label = UITextControl.Width(group.Label ?? string.Empty, ResearchFaces.Condensed,
                    ResearchFaces.Size.RailName);

                widest = Mathf.Max(widest, label + tally);
            }

            // 6 for the selection bar's reserved lane, 10 for the swatch and its gap, 6 between label and
            // tally, 4 for the tally's own right margin, 18 for the scrollbar, 2 for the rail's border.
            railMeasured = Mathf.Clamp(widest + 46f, MinRailWidth, MaxRailWidth);

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

        /// <summary>
        /// The bands or groups the graph is divided into, with how far each is finished.
        ///
        /// <b>The fold strip is drawn here, not as a rail element,</b> because it is a button that changes the
        /// rail rather than a row inside it. Folded, the rows keep only their colour tick, which is why the
        /// entries are built differently rather than the control being asked to render two ways.
        ///
        /// The colour is on the swatch alone. It was on the label as well, which said the same fact twice and
        /// left the selection with nowhere to go on a rail whose rows are all already coloured; the tab's own
        /// colour marks that now, as a bar down the leading edge. See <c>UIRailClickableEntry.SelectionBar</c>.
        /// </summary>
        private static void ContentsRail(Rect rail, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rail, palette.Border, palette.SurfaceSunken);

            List<ResearchGroup> groups = ResearchGraph.Groups;

            Rect fold = new Rect(rail.x + 1f, rail.y + 1f, rail.width - 2f, 18f);

            if (Mouse.IsOver(fold))
                Widgets.DrawHighlight(fold);

            Text.Anchor = railFolded ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            GUI.color = palette.TextDisabled;

            // Small caps in the mono, matching the queue's heading below it and the readout captions above.
            UITextControl.Label(railFolded ? fold : new Rect(fold.x + 6f, fold.y, fold.width - 30f, fold.height),
                railFolded ? ">>" : RailHeading().ToUpperInvariant(), ResearchFaces.Mono,
                ResearchFaces.Size.Caption);

            // How many blocks there are, on the right, the way the queue puts its total there. The rail could
            // always be counted by eye and never said the figure.
            if (!railFolded)
            {
                Text.Anchor = TextAnchor.MiddleRight;

                UITextControl.Label(new Rect(fold.xMax - 26f, fold.y, 22f, fold.height),
                    groups.Count.ToString(), ResearchFaces.Mono, ResearchFaces.Size.Caption);
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextPrimary;

            TooltipHandler.TipRegion(fold,
                (TipSignal) (railFolded ? "Show the contents rail." : "Fold the contents rail."));

            if (Widgets.ButtonInvisible(fold))
            {
                railFolded = !railFolded;

                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            Rect body = new Rect(rail.x + 1f, fold.yMax + 2f, rail.width - 2f, rail.yMax - fold.yMax - 4f);

            List<UIRailElement> elements = new List<UIRailElement>(groups.Count);

            for (int i = 0; i < groups.Count; i++)
            {
                ResearchGroup group = groups[i];

                Color tint = group.Band.HasValue
                    ? ResearchBands.ColorFor(group.Band.Value, palette)
                    : palette.TextSecondary;

                int done = Finished(group);
                bool here = selected != null && selected.Group == group;

                if (railFolded)
                {
                    elements.Add(new UIRailClickableEntry(i.ToString(), null)
                    {
                        Rise = 14f,
                        Swatch = tint,
                        SwatchWidth = 4f,
                        Silent = true
                    });

                    continue;
                }

                elements.Add(new UIRailClickableEntry(i.ToString(), group.Label)
                {
                    Rise = RailRowHeight + RailRowGap,
                    Swatch = tint,
                    SwatchWidth = 4f,

                    // The bar the selection is marked with, in the tab's colour, which no band can be. Set on
                    // every row so the swatch beside it does not shift as the selection moves.
                    SelectionBar = ResearchFaces.AccentOf(palette),

                    Face = ResearchFaces.Condensed,
                    Points = ResearchFaces.Size.RailName,

                    // <b>The label is no longer tinted, and the weight is no longer bold.</b> The swatch already
                    // carries the band's colour; saying it twice made twelve rows of coloured bold text with a
                    // coloured bar under each read as one striped block rather than as twelve entries, and left
                    // the selection nowhere to go -- being on Dark Knowledge and being on Mechanoids looked the
                    // same. Colour on the swatch, weight nowhere, and the tab's own colour free to mean
                    // "you are here".
                    TextColor = here ? palette.TextPrimary : palette.TextSecondary,

                    Trailing = done + "/" + group.Nodes.Count,
                    CountFace = ResearchFaces.Mono,
                    CountPoints = ResearchFaces.Size.RailCount,
                    CountColor = here ? palette.TextSecondary : palette.TextDisabled,
                    Progress = group.Nodes.Count == 0 ? 0f : done / (float) group.Nodes.Count,
                    ProgressColor = tint,
                    Silent = true
                });
            }

            string current = null;

            if (selected != null)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    if (selected.Group == groups[i])
                        current = i.ToString();
                }
            }

            string picked = UIRailControl.Draw(body, elements, current, ref railScroll, ref railDragging,
                ref railDragOffset, palette, false);

            if (picked == null)
                return;

            int index;

            if (int.TryParse(picked, out index) && index >= 0 && index < groups.Count)
                JumpTo(groups[index]);
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

            TabParts.RowLabel(row, text, over ? palette.Accent : color, ResearchFaces.Condensed,
                ResearchFaces.Size.Row);

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
                    palette.Mood, ResearchFaces.Size.Detail);

                y += 26f;

                ResearchMask.Draw(new Rect(body.x, y, body.width * 0.8f, 16f),
                    ResearchMask.Key(project, "meta"), palette.TextDisabled);

                y += 22f;
            }
            else
            {
                TabParts.RowLabel(new Rect(body.x, y, body.width, 24f), project.LabelCap.ToString(),
                    palette.TextPrimary, ResearchFaces.Condensed, ResearchFaces.Size.Detail);

                y += 26f;

                TabParts.RowLabel(new Rect(body.x, y, body.width, 16f), Meta(project), palette.TextDisabled,
                    ResearchFaces.Mono, ResearchFaces.Size.Meta);

                y += 20f;

                Rect bar = new Rect(body.x, y, body.width, 4f);

                Widgets.DrawBoxSolid(bar, palette.SurfaceSunken);
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * project.ProgressPercent, bar.height),
                    ResearchFacts.ColorFor(state, palette, project.knowledgeCategory));

                y += 8f;

                TabParts.RowLabel(new Rect(body.x, y, body.width, 16f), Progress(project), palette.TextDisabled,
                    ResearchFaces.Mono, ResearchFaces.Size.Meta);

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
                    ResearchFaces.Body, ResearchFaces.Size.Prose, palette.TextSecondary);

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

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleLeft;

                Widgets.DrawBoxSolid(new Rect(box.x + 6f, box.center.y - 4f, 8f, 8f), tint);

                GUI.color = tint;

                // The same caps the band's own heading on the canvas takes, rather than a bold tag: markup
                // cannot be drawn from a baked sheet, so the weight is the face's job now, and this is the
                // one place in the detail panel naming a band.
                UITextControl.Label(new Rect(box.x + 19f, box.y, box.width - 24f, box.height),
                    ResearchBands.LabelOf(band).ToUpperInvariant(), ResearchFaces.Condensed,
                    ResearchFaces.Size.Row, FontStyle.Bold);

                y += 26f;

                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
            }

            y = TabParts.Note(new Rect(body.x, y, body.width, 0f), y, ResearchTaxonomy.ReasonFor(project),
                palette, ResearchFaces.Body, ResearchFaces.Size.Prose, palette.TextDisabled);

            y += 6f;

            y = Section(body, y, "Source", palette);

            Rect source = new Rect(body.x, y, body.width, 16f);

            Widgets.DrawBoxSolid(new Rect(source.x + 2f, source.y + 3f, 3f, 10f),
                ResearchSourceMarks.ColorFor(project, palette));

            TabParts.RowLabel(new Rect(source.x + 11f, source.y, source.width - 11f, source.height),
                ResearchSourceMarks.NameFor(project), palette.TextSecondary, ResearchFaces.Condensed,
                ResearchFaces.Size.Row);

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
            Color color = GUI.color;

            try
            {
                GUI.color = palette.TextDisabled;

                // Small caps in the mono, the same heading the two rails and the readout captions take. It is
                // the one device this tab uses to say "a group of things starts here".
                UITextControl.Label(new Rect(body.x, y + 4f, body.width, 16f), title.ToUpperInvariant(),
                    ResearchFaces.Mono, ResearchFaces.Size.Caption);
            }
            finally
            {
                GUI.color = color;
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
                    "and " + (unlocked.Count - shown) + " more", palette.TextDisabled,
                    ResearchFaces.Condensed, ResearchFaces.Size.Row);

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
                ResearchFaces.Condensed, ResearchFaces.Size.Row);

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

        /// <summary>
        /// The queue: what is planned, in the order it will happen.
        ///
        /// <b>This is a list you edit, not one you pick from,</b> which is why it took the rail control longer
        /// to fit than the others. Each row carries a grip that starts a reorder and a cross that removes it,
        /// and both are drawn through the entry's glyph delegates -- those hit test and consume the click
        /// themselves, so removing a row does not also select it.
        ///
        /// A project that cannot start yet gets a note under it saying why, which is a
        /// <see cref="QueueBlockedNote"/> rather than part of the row: it is a second line of a different shape,
        /// and the element list is exactly the thing that lets a feature add one.
        /// </summary>
        private static void QueueRail(Rect rect, UIColorPaletteDef palette)
        {
            // SurfaceSunken, matching the header and the contents rail: all three are chrome around the canvas,
            // and the canvas is the one thing on the screen that sits above its ground.
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

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

            const float footer = 46f;

            Rect view = new Rect(inner.x, inner.y, inner.width, inner.height - footer);

            List<UIRailElement> elements = new List<UIRailElement>();

            // The row the game is actually working on, so the control can highlight it the way it highlights
            // a selection anywhere else.
            string currentKey = null;

            elements.Add(new UIRailSectionHeaderControl("Queue")
            {
                Trailing = TotalDays(main),
                Color = palette.TextDisabled,
                Uppercase = true,
                Face = ResearchFaces.Mono,
                Points = ResearchFaces.Size.Caption
            });

            if (main.Count == 0)
            {
                elements.Add(new UIRailClickableEntry(null, "Nothing planned")
                {
                    TextColor = palette.TextDisabled,
                    Rise = 20f
                });
            }

            for (int i = 0; i < main.Count; i++)
                AddQueueRow(elements, main[i], i, queue, palette, ref currentKey);

            if (anomaly.Count > 0)
            {
                elements.Add(new UIRailSectionHeaderControl("Anomaly")
                {
                    Trailing = "parallel",
                    Color = palette.TextDisabled,
                    Uppercase = true,
                    Face = ResearchFaces.Mono,
                    Points = ResearchFaces.Size.Caption
                });

                for (int i = 0; i < anomaly.Count; i++)
                    AddQueueRow(elements, anomaly[i], -1, queue, palette, ref currentKey);
            }

            UIRailControl.Draw(view, elements, currentKey, ref queueScroll, ref queueDragging,
                ref queueDragOffset, palette, false);

            if (dragFrom < 0)
                return;

            QueueGhost(view, palette);

            if (Event.current.type == EventType.MouseUp)
                QueueDrop(queue);
        }

        /// <summary>
        /// One queued project, plus the note under it when it cannot start yet.
        ///
        /// <paramref name="index"/> is the position in the reorderable lane, or -1 for the anomaly lane, which
        /// runs in parallel and therefore has no order to change.
        /// </summary>
        private static void AddQueueRow(List<UIRailElement> elements, ResearchProjectDef project, int index,
            GameComponent_ResearchQueue queue, UIColorPaletteDef palette, ref string currentKey)
        {
            bool current = Find.ResearchManager != null && Find.ResearchManager.IsCurrentProject(project);
            string key = (index >= 0 ? "q:" : "a:") + elements.Count;

            elements.Add(new UIRailClickableEntry(key, project.LabelCap)
            {
                Rise = RowHeight,
                Face = ResearchFaces.Condensed,
                Points = ResearchFaces.Size.RailName,
                Trailing = project.knowledgeCategory != null
                    ? "parallel"
                    : ResearchRate.Days(ResearchRate.DaysFor(project)),
                CountFace = ResearchFaces.Mono,
                CountPoints = ResearchFaces.Size.RailCount,
                CountColor = palette.TextDisabled,

                // The row the colony is actually working on, marked the way the contents rail marks its block.
                // The control was already being told which row that is and had nothing to draw it with.
                SelectionBar = ResearchFaces.AccentOf(palette),

                Silent = true,

                // The grip alone, tight against the edge. The badge that used to sit beside it said the same
                // thing as the row highlight, and a 236 pixel rail cannot afford to say anything twice.
                LeadPad = 2f,
                IconSize = 12f,
                Glyph = (slot, color) => QueueGrip(slot, index, palette),

                TrailingGlyphSize = 16f,
                TrailingGlyph = (slot, color) => QueueCross(slot, project, queue, palette),

                Decorate = row => QueueDrag(row, index, queue)
            });

            if (current)
                currentKey = key;

            if (project.CanStartNow || project.IsFinished)
                return;

            ResearchNode node = ResearchGraph.NodeFor(project);
            string why = ResearchFacts.ChipFor(node, ResearchFacts.StateOf(node));

            if (why != null)
                elements.Add(new QueueBlockedNote { Text = why });
        }

        /// <summary>The drag handle. Drawing only -- the drag is handled from the row, in <see cref="QueueDrag"/>.</summary>
        private static void QueueGrip(Rect slot, int index, UIColorPaletteDef palette)
        {
            if (index < 0 || ResearchGlyphs.Grip == null)
                return;

            Color previous = GUI.color;

            GUI.color = palette.TextDisabled;

            GUI.DrawTexture(new Rect(slot.x, slot.y + (slot.height - 16f) / 2f, 12f, 16f),
                ResearchGlyphs.Grip);

            GUI.color = previous;
        }

        /// <summary>
        /// Both halves of a reorder, from the row rather than from the grip's own slot.
        ///
        /// <b>The press and the release have to be measured against the same rect.</b> Reading the press off
        /// the grip's slot and the release off the row meant two coordinate spaces and a slot that did not
        /// quite sit where it looked, and the drag simply never landed. The row is the rect the control hands
        /// back, so both are taken from it.
        /// </summary>
        private static void QueueDrag(Rect row, int index, GameComponent_ResearchQueue queue)
        {
            if (index < 0)
                return;

            Rect grip = new Rect(row.x, row.y, GripWidth, row.height);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                                                          && Mouse.IsOver(grip))
            {
                dragFrom = index;
                dragProject = index < mainLane.Count ? mainLane[index] : null;
                dragLabel = dragProject == null ? null : dragProject.LabelCap.ToString();
                dragTo = -1;

                Event.current.Use();

                return;
            }

            if (dragFrom < 0 || !Mouse.IsOver(row))
                return;

            // Which half of the row the cursor is in, so a drop is a gap between two rows rather than a row:
            // the top half of the fourth row means before the fourth, not onto it.
            bool above = Event.current.mousePosition.y < row.center.y;

            dragTo = above ? index : index + 1;

            // Landing on either side of where it started is not a move, and a line drawn there would promise
            // one.
            if (dragTo == dragFrom || dragTo == dragFrom + 1)
                return;

            // Drawn here, inside the list own scroll view, so it is clipped with the rows it sits between
            // rather than floating over the panel.
            float y = above ? row.y : row.yMax - 2f;

            Widgets.DrawBoxSolid(new Rect(row.x, y, row.width, 2f),
                ResearchFaces.AccentOf(UIColorPaletteDef.Active));
        }

        /// <summary>
        /// The dragged row, drawn again under the cursor.
        ///
        /// <b>After the list rather than inside it,</b> so it rides over the rows it is passing instead of
        /// being clipped by them, and so it survives the cursor leaving the list.
        /// </summary>
        private static void QueueGhost(Rect view, UIColorPaletteDef palette)
        {
            if (dragLabel.NullOrEmpty())
                return;

            Rect ghost = new Rect(view.x + 4f, Event.current.mousePosition.y - RowHeight * 0.5f,
                view.width - 8f, RowHeight);

            UIElementPainter.OutlineRounded(ghost, ResearchFaces.AccentOf(palette), palette.SurfaceRaised);

            if (ResearchGlyphs.Grip != null)
            {
                Color previous = GUI.color;

                GUI.color = palette.TextSecondary;

                GUI.DrawTexture(new Rect(ghost.x + 4f, ghost.y + (ghost.height - 16f) / 2f, 12f, 16f),
                    ResearchGlyphs.Grip);

                GUI.color = previous;
            }

            TabParts.RowLabel(new Rect(ghost.x + GripWidth + 6f, ghost.y, ghost.width - GripWidth - 12f,
                ghost.height), dragLabel, palette.TextPrimary, GameFont.Small, ResearchFaces.Condensed,
                ResearchFaces.Size.RailName);
        }

        /// <summary>
        /// Ends the drag, wherever the button came up.
        ///
        /// <b>The release used to be acted on only by the row under the cursor.</b> A queue row can have a
        /// note under it saying what it is waiting for, and a note is its own element rather than part of the
        /// row, so letting go over one reached no row at all and the drag was simply dropped. The panel then
        /// cleared the grab on the way out and nothing had happened. That is the half of "sometimes it works"
        /// a player sees as the list ignoring them; the other half moved the wrong project, and is
        /// <see cref="dragProject"/>.
        ///
        /// Now the target is remembered from the last row the cursor crossed, so a release over a note, over a
        /// heading or off the end of the list still lands where the line was drawn.
        /// </summary>
        private static void QueueDrop(GameComponent_ResearchQueue queue)
        {
            if (dragProject != null && dragTo >= 0)
            {
                int from = queue.PlaceOf(dragProject);
                int to = QueuePlace(queue, from);

                string why;

                if (from >= 0 && to >= 0 && to != from && !queue.Move(from, to, out why) && why != null)
                {
                    // Recorded rather than thrown as a game message: the panel shows it in its own footer
                    // for a few seconds, which keeps the answer beside the thing that was refused.
                    refusal = why;
                    refusedAt = Time.frameCount;
                }
            }

            ClearDrag();
        }

        /// <summary>Forgets the drag. One place, so a release outside the list cannot half-clear it.</summary>
        private static void ClearDrag()
        {
            dragFrom = -1;
            dragTo = -1;
            dragProject = null;
            dragLabel = null;
        }

        /// <summary>
        /// The drop place in the queue, converted from its place in the main lane.
        ///
        /// The two are different lists: the queue carries the anomaly projects as well, so a gap three rows
        /// down the lane is not queue index three. Converted through whichever project the gap sits above.
        /// </summary>
        private static int QueuePlace(GameComponent_ResearchQueue queue, int from)
        {
            if (dragTo >= mainLane.Count)
                return queue.Entries.Count - 1;

            int to = queue.PlaceOf(mainLane[dragTo]);

            // Move takes the place after the row has been lifted out, so everything below it has come up one.
            return to > from ? to - 1 : to;
        }

        /// <summary>The remove cross. Consumes its own click so the row is not selected as well.</summary>
        private static void QueueCross(Rect slot, ResearchProjectDef project,
            GameComponent_ResearchQueue queue, UIColorPaletteDef palette)
        {
            if (ResearchGlyphs.Cross == null)
                return;

            Color previous = GUI.color;

            GUI.color = Mouse.IsOver(slot) ? palette.Danger : palette.TextDisabled;

            GUI.DrawTexture(slot.ContractedBy(3f), ResearchGlyphs.Cross);

            GUI.color = previous;

            if (!Widgets.ButtonInvisible(slot))
                return;

            queue.Remove(project);

            SoundDefOf.Click.PlayOneShotOnCamera();
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


        /// <summary>
        /// One queue row, and the drag that reorders it.
        ///
        /// <b>The drag is started from the grip and finished anywhere.</b> A drag that had to end inside the rail
        /// would silently do nothing whenever somebody let go a few pixels wide of it, which reads as the list
        /// refusing to be reordered.
        /// </summary>

        private static void DrawNumber(Rect rect, int number, Color color)
        {
            TextAnchor anchor = Text.Anchor;
            Color previous = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = color;

                UITextControl.Label(rect, number.ToString(), ResearchFaces.Mono,
                    ResearchFaces.Size.RailCount);
            }
            finally
            {
                GUI.color = previous;
                Text.Anchor = anchor;
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
                TabParts.Note(rect, rect.y, refusal, palette, ResearchFaces.Body, ResearchFaces.Size.Prose,
                    palette.Warning);

                return;
            }

            if (anomalyCount > 0)
                TabParts.Note(rect, rect.y, "Anomaly runs alongside, paid for with knowledge from studying "
                                            + "entities rather than with researcher time.", palette);
        }
    }
}
