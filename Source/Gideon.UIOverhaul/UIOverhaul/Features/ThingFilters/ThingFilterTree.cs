using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.ThingFilters
{
    /// <summary>What one row of the filter tree stands for.</summary>
    internal enum ThingFilterRowKind
    {
        /// <summary>A <c>ThingCategoryDef</c>. Openable, and its toggle is tri-state.</summary>
        Category,

        /// <summary>A single <c>ThingDef</c>. The leaves, and the great majority of the rows.</summary>
        Thing,

        /// <summary>A <c>SpecialThingFilterDef</c> such as "allow rotten". A constraint, not an item.</summary>
        Special,

        /// <summary>
        /// The single "undiscovered items" row a category grows when some of its things are hidden.
        ///
        /// One row standing for several defs, so the player can allow things they have not met yet without being
        /// told what they are.
        /// </summary>
        Undiscovered
    }

    /// <summary>
    /// One row of the flattened tree. A struct, and deliberately: there is one of these per <c>ThingDef</c> in
    /// the game, and they are walked start to end several times a frame.
    /// </summary>
    internal struct ThingFilterRow
    {
        public ThingFilterRowKind Kind;

        /// <summary>Indent level, counted the way vanilla counts it, so the geometry is comparable.</summary>
        public int Depth;

        /// <summary>
        /// Index of the category row this one sits inside, or -1 for a row at the top level.
        ///
        /// This is what makes the whole evaluation linear. Because a parent always precedes its children in
        /// display order, one reverse pass can roll every child's answer up into its parent, and one forward pass
        /// can push every parent's answer down into its children -- no recursion, no per-node subtree walks.
        /// </summary>
        public int Parent;

        /// <summary>
        /// One past the last row belonging to this category's subtree. Only meaningful for
        /// <see cref="ThingFilterRowKind.Category"/>.
        ///
        /// A closed category is skipped by assigning this to the loop counter, so a collapsed branch costs one
        /// comparison however many thousand defs are inside it.
        /// </summary>
        public int SubtreeEnd;

        /// <summary>Set for <see cref="ThingFilterRowKind.Category"/>, and for the owning category of the other kinds.</summary>
        public TreeNode_ThingCategory Node;

        public ThingDef Thing;

        public SpecialThingFilterDef Special;
    }

    /// <summary>
    /// The filter tree flattened once into an array, and kept for the rest of the session.
    ///
    /// <b>What this is for.</b> Vanilla rebuilds its view of the tree by recursive descent on every event pass,
    /// and the expensive part is not the descent -- it is what the descent asks. <c>ThingCategoryDef</c> exposes
    /// <c>DescendantThingDefs</c>, <c>DescendantSpecialThingFilterDefs</c> and <c>ParentsSpecialThingFilterDefs</c>
    /// as nested iterator properties with no caching behind them, and <c>Listing_TreeThingFilter</c> calls the
    /// first of those once per drawn category, from <c>AllowanceStateOf</c>, to decide whether that category's
    /// checkbox is on, off or partial. So opening a category near the root walks its entire subtree, every frame,
    /// and a def deep in the tree is visited once for each open ancestor it has.
    ///
    /// <b>What is actually static.</b> The shape of the tree: which rows exist, in what order, at what depth,
    /// inside which parent. That is fixed once the defs are loaded and cannot change while the game runs. What is
    /// <i>not</i> static is which of those rows a given caller may see -- that depends on the <c>parentFilter</c>,
    /// on <c>forceHiddenDefs</c>, on the search text and on what the player has discovered -- so none of it is
    /// baked in here. This type answers "what rows are there"; <see cref="ThingFilterView"/> answers "which of
    /// them, right now".
    ///
    /// That split is the point. Because the array is in display order and each category row knows where its
    /// subtree ends, every per-frame question becomes a linear pass over an array of structs rather than a tree
    /// walk with a virtual call at each step -- and the aggregate a category needs for its tri-state falls out of
    /// that pass for free, instead of costing a subtree walk per category.
    ///
    /// <b>Rows are built as though everything were open,</b> including the subtrees of categories the player has
    /// collapsed. Collapse is a display state that changes several times a second; rebuilding the array for it
    /// would put the cost back where it was taken from. <see cref="ThingFilterRow.SubtreeEnd"/> is what makes
    /// skipping a closed branch free instead.
    ///
    /// <b>Built lazily, not at the end of loading.</b> The obvious place would be after def load, but the tree
    /// this walks is assembled by <c>ThingCategoryNodeDatabase.FinalizeInit</c> -- <c>childCategories</c> and
    /// <c>childThingDefs</c> are both filled in there -- and building ahead of it would silently produce an empty
    /// or partial array. Waiting for the first filter window instead costs one pass over the defs, once, at a
    /// moment when the player has just opened a window and a frame of work is invisible.
    /// </summary>
    internal static class ThingFilterTree
    {
        /// <summary>
        /// Keyed by display root, because different callers show different roots: a storage settings window is
        /// rooted at the whole tree, while a bill's ingredient filter is rooted wherever its parent filter's
        /// <c>DisplayRootCategory</c> lands. Each distinct root is flattened once.
        /// </summary>
        private static readonly Dictionary<TreeNode_ThingCategory, ThingFilterRow[]> flattened =
            new Dictionary<TreeNode_ThingCategory, ThingFilterRow[]>();

        /// <summary>
        /// The rows for one display root, in the order they would be drawn with every category open.
        ///
        /// Never null and never empty-checked by callers: a root with nothing under it yields a zero-length array,
        /// which every pass below handles without a special case.
        /// </summary>
        internal static ThingFilterRow[] Rows(TreeNode_ThingCategory root)
        {
            if (root == null)
                return new ThingFilterRow[0];

            ThingFilterRow[] rows;

            if (flattened.TryGetValue(root, out rows))
                return rows;

            List<ThingFilterRow> building = new List<ThingFilterRow>(1024);

            // The root's inherited special filters come first and belong to no category row, exactly as in
            // ListCategoryChildren. Their Parent is -1, which is also what keeps them out of every category's
            // tri-state arithmetic -- vanilla counts DescendantSpecialThingFilterDefs, which does not include
            // the ones inherited from above.
            foreach (SpecialThingFilterDef inherited in root.catDef.ParentsSpecialThingFilterDefs)
                building.Add(new ThingFilterRow
                {
                    Kind = ThingFilterRowKind.Special,
                    Depth = 0,
                    Parent = -1,
                    Special = inherited,
                    Node = root
                });

            Append(building, root, 0, -1);

            rows = building.ToArray();
            flattened[root] = rows;

            return rows;
        }

        /// <summary>
        /// Appends one category's children, in vanilla's order: its own special filters, then each child category
        /// followed immediately by that category's whole subtree, then its own things, then the undiscovered row.
        ///
        /// <b>Nothing is filtered out here, and that is deliberate.</b> Vanilla skips an invisible category and
        /// never descends into it, skips a def the parent filter disallows, and skips rows the search excludes.
        /// All three depend on state that changes while the window is open, so all three are decided per frame in
        /// <see cref="ThingFilterView"/>. The one exception is below.
        /// </summary>
        private static void Append(List<ThingFilterRow> rows, TreeNode_ThingCategory node, int depth, int parent)
        {
            foreach (SpecialThingFilterDef special in node.catDef.childSpecialFilters)
                rows.Add(new ThingFilterRow
                {
                    Kind = ThingFilterRowKind.Special,
                    Depth = depth,
                    Parent = parent,
                    Special = special,
                    Node = node
                });

            foreach (TreeNode_ThingCategory child in node.ChildCategoryNodes)
            {
                int index = rows.Count;

                rows.Add(new ThingFilterRow
                {
                    Kind = ThingFilterRowKind.Category,
                    Depth = depth,
                    Parent = parent,
                    Node = child
                });

                Append(rows, child, depth + 1, index);

                // Backfilled once the subtree is known, since a struct in a list cannot be edited in place.
                ThingFilterRow row = rows[index];
                row.SubtreeEnd = rows.Count;
                rows[index] = row;
            }

            foreach (ThingDef thing in node.catDef.SortedChildThingDefs)
                rows.Add(new ThingFilterRow
                {
                    Kind = ThingFilterRowKind.Thing,
                    Depth = depth,
                    Parent = parent,
                    Thing = thing,
                    Node = node
                });

            // <b>The one place a row is omitted for a static reason.</b> A category with no things of its own can
            // never have an undiscovered one, so the row would be permanently hidden rather than conditionally.
            // Whether it is *shown* still depends on what the player has discovered, which is per frame.
            if (node.catDef.childThingDefs.Count > 0)
                rows.Add(new ThingFilterRow
                {
                    Kind = ThingFilterRowKind.Undiscovered,
                    Depth = depth,
                    Parent = parent,
                    Node = node
                });
        }

        /// <summary>
        /// Drops everything. For a def reload, which rebuilds the very tree these indices describe.
        /// </summary>
        internal static void Clear()
        {
            flattened.Clear();
        }
    }

    /// <summary>
    /// Discards the flattened rows whenever RimWorld rebuilds the category tree.
    ///
    /// <c>ThingCategoryNodeDatabase.FinalizeInit</c> is where <c>childCategories</c> and <c>childThingDefs</c> are
    /// populated, so it runs on the initial load and again after any def reload. Every index in a flattened array
    /// refers to that structure, so an array built before a reload describes a tree that no longer exists -- it
    /// would draw defs from the previous mod list, and the indices could be out of range.
    ///
    /// A postfix rather than a prefix, so the new tree is in place before anything can ask for rows again.
    /// </summary>
    [HarmonyPatch(typeof(ThingCategoryNodeDatabase), nameof(ThingCategoryNodeDatabase.FinalizeInit))]
    public static class Patch_ThingCategoryNodeDatabase_FinalizeInit
    {
        public static void Postfix()
        {
            UIGuard.Try("ThingFilters.TreeReset", ThingFilterTree.Clear,
                "The thing filter panel may list defs from before the last def reload.");
        }
    }
}
