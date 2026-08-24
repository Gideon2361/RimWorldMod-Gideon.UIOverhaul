using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>One project's place on the canvas.</summary>
    internal sealed class ResearchNode
    {
        internal ResearchProjectDef Project;

        /// <summary>Where it sits in canvas coordinates, which the panel offsets by its own scroll.</summary>
        internal Rect Rect;

        /// <summary>The longest prerequisite chain reaching it <em>inside its own branch</em>.</summary>
        internal int Depth;

        /// <summary>
        /// Which horizontal slot of its branch it is drawn in.
        ///
        /// <b>Not the same as <see cref="Depth"/>, and that is the whole point.</b> Depth is a fact about the
        /// prerequisite chain and must never be adjusted to suit the layout. A slot is where the drawing puts it,
        /// and one depth may span several slots when it holds more nodes than a column should be tall -- see
        /// <c>ResearchGraph.MaxRowsPerColumn</c>. Every node at a given depth still sits to the right of every
        /// node it depends on, so no arrow ever runs backwards.
        /// </summary>
        internal int Slot;

        /// <summary>Which branch of its group it belongs to, or -1 when it stands alone.</summary>
        internal int Branch = -1;

        /// <summary>The block this project sits in, under whatever grouping is in force.</summary>
        internal ResearchGroup Group;

        /// <summary>
        /// Hidden by the difficulty settings: not in vanilla's <c>VisibleResearchProjects</c>.
        ///
        /// Drawn as a ghost rather than left out, because a chain with a hole in it and nothing to explain the
        /// hole reads as a missing mod.
        /// </summary>
        internal bool Ghost;

        /// <summary>Order within its column, before it was turned into a y coordinate.</summary>
        internal int Row;

        /// <summary>
        /// The prerequisites and the projects this one is a prerequisite for, as nodes.
        ///
        /// <b>Kept here rather than walked out of the def lists at every ask.</b> Four unrelated things want them:
        /// the branch-finding, the ordering sweeps, the detail panel's "leads to" list -- which vanilla only has
        /// as <c>requiredByThis</c>, missing the hidden ones -- and the queue, which has to resolve a chain in
        /// dependency order.
        /// </summary>
        internal readonly List<ResearchNode> Parents = new List<ResearchNode>();

        internal readonly List<ResearchNode> Children = new List<ResearchNode>();
    }

    /// <summary>
    /// One block of the canvas: a theme band, a mod, or a tech level, depending on the grouping.
    ///
    /// <b>The mod used to be the only signal, and it was the wrong axis.</b> That version answered "what did this
    /// mod add", which is close to never the question somebody opens the research tab with -- and the scan of
    /// 2026-08-23 measured the cost across 354 projects: it produced 48 blocks, 26 of which held one or two
    /// projects, while the 23 medical and genetic projects were spread over four of them. Theme bands are the
    /// default now; grouping by mod is still offered and still exact. See <see cref="ResearchGroupings"/>.
    ///
    /// <b>Connected components on their own were never an option.</b> RimWorld's tree is very nearly one
    /// component and almost every mod project descends from a core one, so a component-first grouping pulls the
    /// whole game into a single block. Blocks come from the key; components only decide branches inside a block.
    /// </summary>
    internal sealed class ResearchGroup
    {
        internal string Label;

        /// <summary>Where the caption goes, in canvas coordinates.</summary>
        internal Rect Header;

        /// <summary>Sort key: load order, tech level, or the band's own priority. Lower comes first.</summary>
        internal int Order;

        /// <summary>
        /// The band, when the grouping is by theme. Null otherwise.
        ///
        /// Carried on the group rather than looked up from the label, because a label is a display string and two
        /// bands could be renamed into the same one; and because the header, the rail, the node stripes and the
        /// cross-band chips all want the same color from the same place.
        /// </summary>
        internal ResearchBand? Band;

        internal readonly List<ResearchNode> Nodes = new List<ResearchNode>();
    }

    /// <summary>
    /// One prerequisite arrow.
    ///
    /// <b><see cref="Local"/> is why this is a struct with a flag rather than a pair.</b> An arrow inside a
    /// branch is short and always drawn; one that crosses to another mod's block is long, and drawing every one
    /// of those would put a few hundred lines across the whole canvas. Those are drawn only when one end is
    /// selected, which is when somebody is actually asking where a chain goes.
    /// </summary>
    internal struct ResearchEdge
    {
        internal ResearchNode From;

        internal ResearchNode To;

        internal bool Local;
    }

    /// <summary>
    /// The whole tech tree as one canvas of clustered branches.
    ///
    /// <b>Why not vanilla's own coordinates.</b> <c>researchViewX</c> and <c>researchViewY</c> are authored per
    /// tab, so Main and Anomaly occupy the same space as each other and would sit on top of one another.
    /// Offsetting the tabs apart was considered and would keep the hand-placed clustering; it was turned down on
    /// 2026-08-23 in favour of a computed layout, so this computes one.
    ///
    /// <b>Three levels, and the middle one is the whole point.</b> Blocks are mods. Inside a block, branches are
    /// connected components of that mod's own prerequisite graph. Inside a branch, columns are longest-path depth
    /// <em>within the branch</em> and rows are ordered to reduce crossings.
    ///
    /// <b>Depth used to be global and that was the fault.</b> A global depth puts every root project in the game
    /// -- Berry cultivation, Fishing, Carvings, Recurve bow, and forty others with nothing to do with each other
    /// -- in one column forty rows tall, and leaves crossing-minimisation nothing to work with because almost
    /// every edge in RimWorld's tree is one column long. Depth inside a branch of eight projects means something;
    /// depth across the whole game does not.
    ///
    /// <b>Projects with no prerequisites and no dependants are gridded, not given a row each.</b> There are
    /// dozens of them and they are the other half of why the first version was a wall.
    ///
    /// <b>Built once and kept.</b> The signature covers everything the layout depends on and deliberately nothing
    /// it does not: progress, techprints and benches change constantly and none of them may move a node.
    /// </summary>
    internal static class ResearchGraph
    {
        /// <summary>
        /// How wide a node is.
        ///
        /// <b>Widened from 140 on 2026-08-23.</b> At 140 a name wrapped to two lines, so the card was three rows
        /// tall and every node in the game was as tall as the longest name in it. At 196 almost every name in
        /// vanilla and the expansions fits on one line, the card is a fixed two rows, and the canvas comes out
        /// shorter overall despite the wider cards -- which is the trade Aaron asked for.
        /// </summary>
        internal const float NodeWidth = 196f;

        /// <summary>
        /// The most rows one depth level may occupy before it is split into side-by-side sub-columns.
        ///
        /// <b>This is the fix for the pillars.</b> A column is a depth in the prerequisite chain, and a branch of
        /// forty projects four deep is four columns of ten -- a pillar, taller than the window, with the canvas
        /// three quarters empty to its right. Nothing can move a node to another depth without lying about what
        /// it needs, but a depth with ten nodes in it can be drawn as two columns of five side by side: the same
        /// depth, twice the width, half the height.
        ///
        /// Six because that is about a screen's worth of rows at the node height, so a level that fits stays one
        /// column and only the ones that would run off the bottom get split.
        /// </summary>
        private const int MaxRowsPerColumn = 6;

        /// <summary>Air between one column's right edge and the next column's left, where the arrows run.</summary>
        private const float ColumnGap = 38f;

        private const float RowGap = 7f;

        /// <summary>Air between two branches sitting side by side on a shelf.</summary>
        private const float BranchGapX = 30f;

        /// <summary>Air between one shelf of branches and the next.</summary>
        private const float BranchGapY = 18f;

        /// <summary>Air above a block's caption, and the caption's own height.</summary>
        private const float GroupGap = 26f;

        private const float HeaderHeight = 20f;

        /// <summary>Room left around the whole graph so a node's outline is never against the canvas edge.</summary>
        private const float Margin = 12f;

        /// <summary>How many times a branch's ordering is swept in each direction.</summary>
        private const int Sweeps = 4;

        private static readonly List<ResearchNode> nodes = new List<ResearchNode>();

        private static readonly List<ResearchEdge> edges = new List<ResearchEdge>();

        private static readonly List<ResearchGroup> groups = new List<ResearchGroup>();

        private static readonly Dictionary<ResearchProjectDef, ResearchNode> byProject =
            new Dictionary<ResearchProjectDef, ResearchNode>();

        private static readonly Dictionary<ResearchNode, int> depths = new Dictionary<ResearchNode, int>();

        /// <summary>Nodes whose depth is being computed, so a cycle in somebody's defs cannot recurse forever.</summary>
        private static readonly HashSet<ResearchNode> walking = new HashSet<ResearchNode>();

        private static string signature;

        private static Vector2 size;

        internal static List<ResearchNode> Nodes
        {
            get
            {
                Ensure();

                return nodes;
            }
        }

        internal static List<ResearchEdge> Edges
        {
            get
            {
                Ensure();

                return edges;
            }
        }

        internal static List<ResearchGroup> Groups
        {
            get
            {
                Ensure();

                return groups;
            }
        }

        /// <summary>The canvas the nodes are laid out in.</summary>
        internal static Vector2 Size
        {
            get
            {
                Ensure();

                return size;
            }
        }

        internal static ResearchNode NodeFor(ResearchProjectDef project)
        {
            if (project == null)
                return null;

            Ensure();

            ResearchNode node;

            return byProject.TryGetValue(project, out node) ? node : null;
        }

        /// <summary>
        /// Throws the layout away, so the next ask rebuilds it.
        ///
        /// The band and source caches go with it. Both are keyed on defs and so cannot go stale during a session
        /// on their own -- but this is also the call a grouping change makes, and it is the only moment in the
        /// session when anything is entitled to a fresh answer. Clearing them here rather than on every tab open
        /// keeps the reclassification of three hundred and fifty projects to the handful of times it is asked for.
        /// </summary>
        internal static void Invalidate()
        {
            signature = null;

            ResearchTaxonomy.Invalidate();
            ResearchSourceMarks.Invalidate();
        }

        /// <summary>
        /// How wide the layout packs to.
        ///
        /// <b>From the screen rather than from the canvas rect, on purpose.</b> The detail panel and the queue
        /// rail appear and disappear, so the canvas changes width while the tab is open -- and a target taken
        /// from it would relay the entire graph every time somebody selected a project. Rounded to two hundred
        /// pixels so a window drag does not rebuild on every frame of the drag.
        /// </summary>
        private static float TargetWidth
        {
            get
            {
                float available = UI.screenWidth - 560f;
                float rounded = Mathf.Round(available / 200f) * 200f;

                return Mathf.Max(4f * (NodeWidth + ColumnGap), rounded);
            }
        }

        /// <summary>How often the signature is recomputed, in frames. Building it walks every def.</summary>
        private const int SignatureFrames = 30;

        private static int checkedAt = -1000;

        private static void Ensure()
        {
            if (signature != null && Time.frameCount - checkedAt < SignatureFrames)
                return;

            checkedAt = Time.frameCount;

            string wanted = UIGuard.Try("Research.GraphSignature", Signature, "?", null);

            if (wanted == signature)
                return;

            signature = wanted;

            UIGuard.Try("Research.BuildGraph", Build,
                "The research tab shows no tech tree because its layout could not be built. Nothing about your "
                + "colony's research has changed.");
        }

        private static string Signature()
        {
            List<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            int ghosts = 0;

            for (int i = 0; i < all.Count; i++)
            {
                if (!Allowed(all[i]))
                    ghosts++;
            }

            // The grouping is in here because it decides the blocks, and nothing else in this string would move
            // if it changed -- so without it, switching from Theme to Source would leave the old canvas up until
            // something unrelated happened to relay it.
            return all.Count + "/" + ghosts + "/" + Mathf.RoundToInt(ResearchNodeArt.NodeHeight) + "/"
                   + Mathf.RoundToInt(TargetWidth) + "/" + ResearchGroupings.Store(ResearchGroupings.Current);
        }

        /// <summary>
        /// Whether the difficulty settings permit this project, which is vanilla's own test.
        ///
        /// The current project is always permitted, exactly as <c>VisibleResearchProjects</c> has it: a setting
        /// changed mid-colony must not hide the thing the colony is working on.
        /// </summary>
        private static bool Allowed(ResearchProjectDef project)
        {
            if (project == null)
                return false;

            if (Find.Storyteller == null || Find.Storyteller.difficulty == null)
                return true;

            if (Find.Storyteller.difficulty.AllowedBy(project.hideWhen))
                return true;

            return Find.ResearchManager != null && Find.ResearchManager.IsCurrentProject(project);
        }

        private static void Build()
        {
            nodes.Clear();
            edges.Clear();
            groups.Clear();
            byProject.Clear();
            depths.Clear();
            walking.Clear();

            List<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;

            for (int i = 0; i < all.Count; i++)
            {
                ResearchProjectDef project = all[i];

                if (project == null)
                    continue;

                ResearchNode node = new ResearchNode
                {
                    Project = project,
                    Ghost = !Allowed(project)
                };

                nodes.Add(node);
                byProject[project] = node;
            }

            BuildEdges();
            BuildGroups();
            BuildBranches();
            Place();
            MarkLocalEdges();
        }

        private static void BuildEdges()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                ResearchNode node = nodes[i];

                AddEdges(node, node.Project.prerequisites);
                AddEdges(node, node.Project.hiddenPrerequisites);
            }
        }

        private static void AddEdges(ResearchNode node, List<ResearchProjectDef> prerequisites)
        {
            if (prerequisites == null)
                return;

            for (int i = 0; i < prerequisites.Count; i++)
            {
                ResearchNode from;

                if (prerequisites[i] == null || !byProject.TryGetValue(prerequisites[i], out from)
                                            || from == node || node.Parents.Contains(from))
                    continue;

                edges.Add(new ResearchEdge { From = from, To = node });

                node.Parents.Add(from);
                from.Children.Add(node);
            }
        }

        /// <summary>
        /// Sorts every project into its block, under whatever grouping is in force.
        ///
        /// <b>One key function, three groupings, and no branch in here at all.</b> This method used to read
        /// <c>modContentPack</c> directly; it now asks <see cref="ResearchGroupings.KeyFor"/> for a label, a sort
        /// number and possibly a band, which is the only thing that differs between grouping by theme, by mod and
        /// by tech level. Everything downstream -- branches, columns, rows, arrows, the header rail -- was already
        /// written against an arbitrary key and did not need touching.
        ///
        /// <b>Order first, then label.</b> Order is load order for mods, the game's own enum for tech levels, and
        /// the classifier's priority order for bands. The label is the tie-break so two blocks that claim the same
        /// number still come out the same way round in every session.
        /// </summary>
        private static void BuildGroups()
        {
            Dictionary<string, ResearchGroup> byName = new Dictionary<string, ResearchGroup>();
            ResearchGrouping grouping = ResearchGroupings.Current;

            for (int i = 0; i < nodes.Count; i++)
            {
                ResearchNode node = nodes[i];

                string label;
                int order;
                ResearchBand? band;

                ResearchGroupings.KeyFor(node.Project, grouping, out label, out order, out band);

                if (label.NullOrEmpty())
                    label = "Other";

                ResearchGroup group;

                if (!byName.TryGetValue(label, out group))
                {
                    group = new ResearchGroup
                    {
                        Label = label,
                        Order = order,
                        Band = band
                    };

                    byName[label] = group;
                    groups.Add(group);
                }

                node.Group = group;
                group.Nodes.Add(node);
            }

            groups.Sort((left, right) =>
            {
                int byOrder = left.Order.CompareTo(right.Order);

                return byOrder != 0
                    ? byOrder
                    : string.Compare(left.Label, right.Label, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Finds the branches inside each block: connected components of that block's own edges.
        ///
        /// <b>Restricted to edges whose both ends are in the same block, which is what keeps a mod's chain its
        /// own.</b> Nearly every mod project descends from a core one, so following edges across blocks would
        /// merge every mod into Core. Those cross-block arrows still exist and are still drawn -- see
        /// <see cref="MarkLocalEdges"/> -- they simply do not decide where anything sits.
        /// </summary>
        private static void BuildBranches()
        {
            for (int g = 0; g < groups.Count; g++)
            {
                ResearchGroup group = groups[g];
                int next = 0;

                for (int i = 0; i < group.Nodes.Count; i++)
                {
                    ResearchNode node = group.Nodes[i];

                    if (node.Branch >= 0)
                        continue;

                    List<ResearchNode> found = new List<ResearchNode>();

                    Flood(node, group, next, found);

                    // A branch of one is not a branch. Those are gridded together at the foot of the block
                    // rather than each taking a column of its own.
                    if (found.Count == 1)
                        found[0].Branch = -1;
                    else
                        next++;
                }
            }
        }

        private static void Flood(ResearchNode start, ResearchGroup group, int branch,
            List<ResearchNode> found)
        {
            Stack<ResearchNode> pending = new Stack<ResearchNode>();

            pending.Push(start);
            start.Branch = branch;
            found.Add(start);

            while (pending.Count > 0)
            {
                ResearchNode node = pending.Pop();

                Spread(node.Parents, group, branch, found, pending);
                Spread(node.Children, group, branch, found, pending);
            }
        }

        private static void Spread(List<ResearchNode> neighbours, ResearchGroup group, int branch,
            List<ResearchNode> found, Stack<ResearchNode> pending)
        {
            for (int i = 0; i < neighbours.Count; i++)
            {
                ResearchNode neighbour = neighbours[i];

                if (neighbour.Group != group || neighbour.Branch >= 0)
                    continue;

                neighbour.Branch = branch;
                found.Add(neighbour);
                pending.Push(neighbour);
            }
        }

        /// <summary>
        /// The longest prerequisite chain reaching a node from inside its own branch.
        ///
        /// Memoized, and guarded against a cycle rather than assuming there is not one: prerequisites come from
        /// XML that any mod may write, and a cycle here would be a stack overflow rather than a red line in the
        /// log. A node caught mid-walk contributes nothing, which puts it at its branch's left edge instead.
        /// </summary>
        private static int DepthOf(ResearchNode node)
        {
            int known;

            if (depths.TryGetValue(node, out known))
                return known;

            if (!walking.Add(node))
                return 0;

            int deepest = 0;

            for (int i = 0; i < node.Parents.Count; i++)
            {
                ResearchNode parent = node.Parents[i];

                if (parent.Group == node.Group && parent.Branch == node.Branch)
                    deepest = Mathf.Max(deepest, DepthOf(parent) + 1);
            }

            walking.Remove(node);
            depths[node] = deepest;

            return deepest;
        }

        /// <summary>Tech level then name, which is the tie-break that makes the whole layout deterministic.</summary>
        private static int Compare(ResearchNode left, ResearchNode right)
        {
            int byLevel = ((int) left.Project.techLevel).CompareTo((int) right.Project.techLevel);

            if (byLevel != 0)
                return byLevel;

            return string.Compare(left.Project.label ?? left.Project.defName,
                right.Project.label ?? right.Project.defName, System.StringComparison.OrdinalIgnoreCase);
        }

        // ---------------------------------------------------------------------------------------
        // Placement
        // ---------------------------------------------------------------------------------------

        /// <summary>One laid-out branch, waiting to be packed into its block.</summary>
        private sealed class Shape
        {
            internal readonly List<ResearchNode> Members = new List<ResearchNode>();

            internal int Columns;

            internal int Rows;

            internal float Width;

            internal float Height;
        }

        private static void Place()
        {
            float nodeHeight = ResearchNodeArt.NodeHeight;
            float columnStride = NodeWidth + ColumnGap;
            float rowStride = nodeHeight + RowGap;
            float target = TargetWidth;

            float y = Margin;
            float widest = 0f;

            for (int g = 0; g < groups.Count; g++)
            {
                ResearchGroup group = groups[g];

                group.Header = new Rect(Margin, y, target, HeaderHeight);

                y += HeaderHeight + 4f;

                List<Shape> shapes = Shapes(group, columnStride, rowStride);
                List<ResearchNode> loners = Loners(group);

                y = PackShapes(shapes, y, target, columnStride, rowStride, ref widest);
                y = PackLoners(loners, y, target, columnStride, rowStride, ref widest);

                y += GroupGap;
            }

            size = new Vector2(Mathf.Max(widest + Margin, target + Margin * 2f), y + Margin);
        }

        /// <summary>
        /// Every branch of one block, laid out internally and measured.
        ///
        /// Ordered widest first, which is what makes the shelf packing below produce tidy rows rather than a
        /// staircase: the big branches claim their shelves and the small ones fill in beside them.
        /// </summary>
        private static List<Shape> Shapes(ResearchGroup group, float columnStride, float rowStride)
        {
            Dictionary<int, Shape> byBranch = new Dictionary<int, Shape>();

            for (int i = 0; i < group.Nodes.Count; i++)
            {
                ResearchNode node = group.Nodes[i];

                if (node.Branch < 0)
                    continue;

                Shape shape;

                if (!byBranch.TryGetValue(node.Branch, out shape))
                {
                    shape = new Shape();
                    byBranch[node.Branch] = shape;
                }

                shape.Members.Add(node);
            }

            List<Shape> shapes = new List<Shape>(byBranch.Values);

            for (int i = 0; i < shapes.Count; i++)
                Arrange(shapes[i], columnStride, rowStride);

            shapes.Sort((left, right) =>
            {
                int byWidth = right.Columns.CompareTo(left.Columns);

                if (byWidth != 0)
                    return byWidth;

                int byHeight = right.Rows.CompareTo(left.Rows);

                return byHeight != 0 ? byHeight : Compare(left.Members[0], right.Members[0]);
            });

            return shapes;
        }

        /// <summary>
        /// Columns and rows for one branch: depth across, crossings reduced down.
        ///
        /// Barycentre sweeps, four each way, started from a sort by tech level then name and broken by the same.
        /// Cheap, and plenty for a branch of a dozen projects whose edges are nearly all one column long.
        /// </summary>
        private static void Arrange(Shape shape, float columnStride, float rowStride)
        {
            Dictionary<int, List<ResearchNode>> columns = new Dictionary<int, List<ResearchNode>>();

            for (int i = 0; i < shape.Members.Count; i++)
            {
                ResearchNode node = shape.Members[i];

                node.Depth = DepthOf(node);

                List<ResearchNode> column;

                if (!columns.TryGetValue(node.Depth, out column))
                {
                    column = new List<ResearchNode>();
                    columns[node.Depth] = column;
                }

                column.Add(node);
            }

            List<int> order = new List<int>(columns.Keys);

            order.Sort();

            for (int i = 0; i < order.Count; i++)
            {
                List<ResearchNode> column = columns[order[i]];

                column.Sort(Compare);

                for (int r = 0; r < column.Count; r++)
                    column[r].Row = r;
            }

            for (int pass = 0; pass < Sweeps; pass++)
            {
                for (int i = 1; i < order.Count; i++)
                    Sweep(columns[order[i]], true);

                for (int i = order.Count - 2; i >= 0; i--)
                    Sweep(columns[order[i]], false);
            }

            // Slots, after the sweeps: a depth level taller than MaxRowsPerColumn becomes several columns side by
            // side. Done here rather than in the packer because only this method knows which nodes share a depth,
            // and done after the sweeps rather than before so the chunks inherit the ordering that reduced
            // crossings -- chopping an ordered list preserves the relative order inside each chunk.
            int slot = 0;
            int tallest = 0;

            for (int i = 0; i < order.Count; i++)
            {
                List<ResearchNode> column = columns[order[i]];

                int subs = Mathf.Max(1, Mathf.CeilToInt(column.Count / (float) MaxRowsPerColumn));
                int perSub = Mathf.Max(1, Mathf.CeilToInt(column.Count / (float) subs));

                for (int r = 0; r < column.Count; r++)
                {
                    column[r].Slot = slot + r / perSub;
                    column[r].Row = r % perSub;
                }

                slot += subs;
                tallest = Mathf.Max(tallest, Mathf.Min(column.Count, perSub));
            }

            shape.Columns = Mathf.Max(1, slot);
            shape.Rows = Mathf.Max(1, tallest);
            shape.Width = shape.Columns * columnStride - ColumnGap;
            shape.Height = shape.Rows * rowStride - RowGap;
        }

        /// <summary>
        /// One column of a branch, reordered by where its neighbours in that branch sit.
        ///
        /// A node with no neighbour in that direction keeps its own row as its barycentre, which holds it roughly
        /// where it was rather than collecting every childless node at the top.
        /// </summary>
        private static void Sweep(List<ResearchNode> column, bool fromLeft)
        {
            if (column == null || column.Count < 2)
                return;

            Dictionary<ResearchNode, float> centres = new Dictionary<ResearchNode, float>();

            for (int i = 0; i < column.Count; i++)
            {
                ResearchNode node = column[i];
                List<ResearchNode> neighbours = fromLeft ? node.Parents : node.Children;
                float total = 0f;
                int count = 0;

                for (int n = 0; n < neighbours.Count; n++)
                {
                    ResearchNode neighbour = neighbours[n];

                    if (neighbour.Group != node.Group || neighbour.Branch != node.Branch)
                        continue;

                    total += neighbour.Row;
                    count++;
                }

                centres[node] = count == 0 ? node.Row : total / count;
            }

            column.Sort((left, right) =>
            {
                int byCentre = centres[left].CompareTo(centres[right]);

                return byCentre != 0 ? byCentre : Compare(left, right);
            });

            for (int i = 0; i < column.Count; i++)
                column[i].Row = i;
        }

        private static List<ResearchNode> Loners(ResearchGroup group)
        {
            List<ResearchNode> loners = new List<ResearchNode>();

            for (int i = 0; i < group.Nodes.Count; i++)
            {
                if (group.Nodes[i].Branch < 0)
                    loners.Add(group.Nodes[i]);
            }

            loners.Sort(Compare);

            return loners;
        }

        /// <summary>
        /// Packs a block's branches into shelves, and returns the y the block ends at.
        ///
        /// Shelf packing rather than one branch per row: a mod with fifteen chains of three would otherwise be
        /// fifteen mostly-empty rows. A branch wider than the target gets a shelf to itself and overflows to the
        /// right, which is the honest outcome for a genuinely deep chain.
        /// </summary>
        private static float PackShapes(List<Shape> shapes, float y, float target, float columnStride,
            float rowStride, ref float widest)
        {
            float x = Margin;
            float shelfHeight = 0f;

            for (int i = 0; i < shapes.Count; i++)
            {
                Shape shape = shapes[i];

                if (x > Margin && x + shape.Width > Margin + target)
                {
                    y += shelfHeight + BranchGapY;
                    x = Margin;
                    shelfHeight = 0f;
                }

                for (int m = 0; m < shape.Members.Count; m++)
                {
                    ResearchNode node = shape.Members[m];

                    node.Rect = new Rect(x + node.Slot * columnStride, y + node.Row * rowStride, NodeWidth,
                        ResearchNodeArt.NodeHeight);
                }

                widest = Mathf.Max(widest, x + shape.Width);
                shelfHeight = Mathf.Max(shelfHeight, shape.Height);
                x += shape.Width + BranchGapX;
            }

            return shapes.Count == 0 ? y : y + shelfHeight + BranchGapY;
        }

        /// <summary>
        /// Packs a block's standalone projects into a dense grid, and returns the y it ends at.
        ///
        /// <b>This is the other half of the fix.</b> A project with no prerequisites and nothing depending on it
        /// has no chain to show, so a column and a row of its own says nothing -- and there are dozens of them.
        /// Gridded, forty of them are four rows instead of forty.
        /// </summary>
        private static float PackLoners(List<ResearchNode> loners, float y, float target, float columnStride,
            float rowStride, ref float widest)
        {
            if (loners.Count == 0)
                return y;

            int perRow = Mathf.Max(1, Mathf.FloorToInt((target + ColumnGap) / columnStride));

            for (int i = 0; i < loners.Count; i++)
            {
                float x = Margin + i % perRow * columnStride;

                loners[i].Rect = new Rect(x, y + i / perRow * rowStride, NodeWidth,
                    ResearchNodeArt.NodeHeight);

                widest = Mathf.Max(widest, x + NodeWidth);
            }

            int rows = (loners.Count + perRow - 1) / perRow;

            return y + rows * rowStride;
        }

        /// <summary>
        /// Marks which arrows are inside a branch, which is what decides whether they are always drawn.
        ///
        /// Done last, because it needs the branches and both endpoints' placement to exist.
        /// </summary>
        private static void MarkLocalEdges()
        {
            for (int i = 0; i < edges.Count; i++)
            {
                ResearchEdge edge = edges[i];

                edge.Local = edge.From.Group == edge.To.Group && edge.From.Branch >= 0
                             && edge.From.Branch == edge.To.Branch;

                edges[i] = edge;
            }
        }
    }
}
