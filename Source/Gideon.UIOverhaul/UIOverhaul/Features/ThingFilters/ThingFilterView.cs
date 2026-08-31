using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.ThingFilters
{
    /// <summary>
    /// Which of the flattened rows are shown right now, and what each one's toggle should say.
    ///
    /// <b>The whole evaluation is three linear passes over an array.</b> One backwards, rolling every row's answer
    /// up into its parent; one forwards, pushing what a parent decided down into its children; one more forwards,
    /// collecting the rows that survive. No recursion and no subtree walks, because
    /// <see cref="ThingFilterRow.Parent"/> and <see cref="ThingFilterRow.SubtreeEnd"/> already encode the shape --
    /// see <see cref="ThingFilterTree"/> for why that is the expensive part of vanilla's version.
    ///
    /// The reverse pass is where the tri-state comes from, and it is the one worth understanding. Vanilla's
    /// <c>AllowanceStateOf</c> answers "is this category on, off or partial" by enumerating every descendant thing
    /// def of that category and testing each one, which it does once per drawn category per frame -- so a def is
    /// tested once for every open ancestor it has. Here each row contributes its own single answer to its parent's
    /// running totals as the pass goes by, and because a parent always sits before its children in display order,
    /// walking backwards means a category's totals are complete by the time the pass reaches it. Every category in
    /// the tree gets its exact counts, and no def is looked at twice.
    ///
    /// <b>Recomputed per frame rather than invalidated on change.</b> The tempting optimization is to keep the
    /// answers and rebuild only when something moves, but the inputs are the search text, the open bits, the
    /// player's discoveries, the parent filter's contents and the filter itself -- and the filter can be mutated
    /// by anything holding a reference, not only by this panel. A cache keyed on all of that would be wrong the
    /// first time something changed one of them without telling us, and wrong here means a checkbox that lies
    /// about a bill. One linear pass over a few thousand structs is measured in microseconds; being certainly
    /// right is worth more than being slightly faster.
    ///
    /// One instance lives per <c>ThingFilterUI.UIState</c>, so its arrays are allocated once per window rather
    /// than once per frame.
    /// </summary>
    internal sealed class ThingFilterView
    {
        /// <summary>
        /// Vanilla's private cached calculator for the special filters a subtree cannot ever match.
        ///
        /// Borrowed rather than reimplemented for two reasons. It is the answer to a question about
        /// <c>Worker.CanEverMatch</c> over every descendant def, which is genuinely expensive and which vanilla
        /// already keeps in a 500-entry LRU keyed by node and parent filter -- so calling it reuses that cache
        /// instead of building a second one beside it. And the rule for what counts as unmatchable is vanilla's to
        /// define; a copy here would be a second definition to keep in step.
        /// </summary>
        private static readonly Func<TreeNode_ThingCategory, ThingFilter, List<SpecialThingFilterDef>>
            CachedHiddenSpecialFilters = ResolveHiddenSpecialFilters();

        private static Func<TreeNode_ThingCategory, ThingFilter, List<SpecialThingFilterDef>>
            ResolveHiddenSpecialFilters()
        {
            try
            {
                return AccessTools.MethodDelegate<Func<TreeNode_ThingCategory, ThingFilter,
                    List<SpecialThingFilterDef>>>(
                    AccessTools.Method(typeof(Listing_TreeThingFilter), "GetCachedHiddenSpecialFilters"));
            }
            catch
            {
                // Deliberately silent and deliberately permissive. Losing this means showing a few special filter
                // rows that can never match anything in their category, which is untidy; the alternative of
                // treating everything as hidden would take working toggles away. Reported once by the caller
                // rather than here, since a static initializer has no useful context to report from.
                return null;
            }
        }

        // ---- Inputs held for the duration of one evaluation -------------------------------------------------

        private ThingFilter filter;
        private ThingFilter parentFilter;
        private QuickSearchFilter search;
        private HashSet<ThingDef> forceHidden;
        private List<SpecialThingFilterDef> hiddenSpecials;
        private int openMask;

        // ---- Per-row working state, reused between frames ---------------------------------------------------

        private ThingFilterRow[] rows = new ThingFilterRow[0];

        /// <summary>Whether the row's own subject passes the visibility rules, before search or collapse.</summary>
        private bool[] selfVisible = new bool[0];

        /// <summary>Whether the row's own label matches the search text.</summary>
        private bool[] selfMatch = new bool[0];

        /// <summary>Whether this row or anything under it is both visible and a match.</summary>
        private bool[] subtreeMatch = new bool[0];

        /// <summary>Whether an ancestor category matched, which makes a whole subtree exempt from the search.</summary>
        private bool[] inherited = new bool[0];

        /// <summary>Whether a thing row's def is one the player has not discovered.</summary>
        private bool[] undiscovered = new bool[0];

        private int[] visibleThings = new int[0];
        private int[] allowedThings = new int[0];
        private int[] visibleSpecials = new int[0];
        private int[] allowedSpecials = new int[0];

        /// <summary>
        /// How many of a category's <i>own</i> things are undiscovered, and the first of them.
        ///
        /// Indexed by parent row index plus one, so the top level's own things -- whose parent is -1 -- land in
        /// slot zero rather than needing a separate field. Direct children only, never descendants: the
        /// undiscovered row stands for the things of one category, as vanilla's does.
        /// </summary>
        private int[] undiscoveredHere = new int[0];

        private ThingDef[] firstUndiscovered = new ThingDef[0];

        /// <summary>Row indices that are actually shown, in display order.</summary>
        private int[] order = new int[0];

        internal int Count { get; private set; }

        /// <summary>How many shown rows match the search text, for the readout beside the search box.</summary>
        internal int MatchCount { get; private set; }

        /// <summary>Total shown rows, so the readout can say "12 of 340".</summary>
        internal int ShownCount => Count;

        /// <summary>Whether the borrowed vanilla helper could not be resolved, for a one-time report.</summary>
        internal static bool HiddenSpecialsUnavailable => CachedHiddenSpecialFilters == null;

        /// <summary>
        /// The special filters this tree treats as unmatchable, as <c>ThingFilter.SetAllow</c> wants them.
        ///
        /// Exposed because a category toggle has to be told which filters to leave alone, and vanilla passes
        /// exactly this list when it does the same thing. May be null, which that overload accepts.
        /// </summary>
        internal List<SpecialThingFilterDef> HiddenSpecials => hiddenSpecials;

        /// <summary>
        /// The defs the caller wants excluded, as a set rather than as whatever it handed us.
        ///
        /// <b>This exists to be passed to <c>ThingFilter.SetAllow</c>, and the difference is seconds.</b> That
        /// method tests <c>exceptedDefs.Contains</c> once per descendant of the category being toggled, and
        /// callers hand the exclusion list over as a lazy <c>IEnumerable</c>. Passing that through means every
        /// one of those tests re-runs the whole query, so toggling a category with a thousand descendants runs
        /// the caller's filter a thousand times over every def in the game.
        ///
        /// A <c>HashSet</c> is enumerated once here and answers each test in constant time, because LINQ's
        /// <c>Contains</c> defers to <c>ICollection.Contains</c> when it is handed one. Aaron reported a ten
        /// second freeze toggling Buildings on 2026-08-30.
        /// </summary>
        internal HashSet<ThingDef> ForceHidden => forceHidden;

        internal ThingFilterRow RowAt(int slot) => rows[order[slot]];

        /// <summary>Indent level of a shown row, for the renderer.</summary>
        internal int DepthAt(int slot) => rows[order[slot]].Depth;

        /// <summary>Whether a shown row matches the search, so the renderer can dim the ones that do not.</summary>
        internal bool MatchesAt(int slot) => !search.Active || selfMatch[order[slot]];

        /// <summary>Whether a shown category row is open. Collapsed categories still draw; their contents do not.</summary>
        internal bool IsOpenAt(int slot)
        {
            int index = order[slot];
            return IsOpen(index);
        }

        /// <summary>
        /// The tri-state a category's toggle should show, from the totals gathered in the reverse pass.
        ///
        /// The branches are vanilla's <c>AllowanceStateOf</c>, in vanilla's order, including the part that is easy
        /// to lose: outside <c>OnlySpecialFilters</c>, a category is fully On only when its special filters are all
        /// allowed as well as its things. A category whose every item is allowed but which has one special filter
        /// switched off is Partial, and it should be.
        /// </summary>
        internal MultiCheckboxState StateAt(int slot)
        {
            int index = order[slot];

            int things = visibleThings[index];
            int allowedT = allowedThings[index];
            int specials = visibleSpecials[index];
            int allowedS = allowedSpecials[index];

            if (filter.OnlySpecialFilters)
            {
                if (allowedS == 0)
                    return MultiCheckboxState.Off;

                return allowedS < specials ? MultiCheckboxState.Partial : MultiCheckboxState.On;
            }

            if (allowedT == 0)
                return MultiCheckboxState.Off;

            if (things == allowedT && specials == allowedS)
                return MultiCheckboxState.On;

            return MultiCheckboxState.Partial;
        }

        /// <summary>The undiscovered defs a shown undiscovered row stands for. Built on demand, for a click.</summary>
        internal List<ThingDef> UndiscoveredDefsAt(int slot)
        {
            ThingFilterRow row = rows[order[slot]];
            List<ThingDef> defs = new List<ThingDef>();

            foreach (ThingDef thing in row.Node.catDef.SortedChildThingDefs)
                if (Find.HiddenItemsManager.Hidden(thing))
                    defs.Add(thing);

            return defs;
        }

        /// <summary>The def whose allowance an undiscovered row's toggle reflects, as vanilla uses the first.</summary>
        internal ThingDef FirstUndiscoveredAt(int slot)
        {
            return firstUndiscovered[rows[order[slot]].Parent + 1];
        }

        /// <summary>
        /// Every def the player could currently toggle, each returned once.
        ///
        /// Drawn from the visibility mask rather than from the def database, so it honors the parent filter and the
        /// caller's hidden defs -- inverting into something the caller forbade would produce a filter the caller
        /// cannot represent.
        ///
        /// <b>Deduplicated, and that is not a tidiness measure.</b> A <c>ThingDef</c> may name several entries in
        /// its <c>thingCategories</c>, and <c>ThingCategoryNodeDatabase.FinalizeInit</c> adds it to the child list
        /// of every one of them -- so a def in two categories genuinely has two rows in this tree, and both are
        /// real rows the player can see and click. Handing the same def back twice made Invert flip it twice and
        /// leave it exactly as it started, which showed up as categories reading Partial with items still off
        /// after a Clear All followed by an Invert.
        ///
        /// The rows themselves are left duplicated on purpose: that is how the tree works, and a def really does
        /// appear under each of its categories. Only callers that <i>act</i> on defs need them collapsed, which is
        /// why this collapses rather than the tree.
        /// </summary>
        internal IEnumerable<ThingDef> VisibleThings()
        {
            HashSet<ThingDef> seen = new HashSet<ThingDef>();

            for (int i = 0; i < rows.Length; i++)
                if (rows[i].Kind == ThingFilterRowKind.Thing && selfVisible[i] && seen.Add(rows[i].Thing))
                    yield return rows[i].Thing;
        }

        /// <summary>
        /// Rebuilds the whole answer for this frame.
        /// </summary>
        internal void Refresh(TreeNode_ThingCategory root, ThingFilter filter, ThingFilter parentFilter,
            QuickSearchFilter search, IEnumerable<ThingDef> forceHiddenDefs,
            IEnumerable<SpecialThingFilterDef> forceHiddenFilters, int openMask)
        {
            this.filter = filter;
            this.parentFilter = parentFilter;
            this.search = search;
            this.openMask = openMask;

            rows = ThingFilterTree.Rows(root);

            forceHidden = null;
            if (forceHiddenDefs != null)
                forceHidden = new HashSet<ThingDef>(forceHiddenDefs);

            ResolveHiddenSpecials(root, forceHiddenFilters);
            Allocate(rows.Length);

            RollUp();
            PushDown();
            Collect();
        }

        /// <summary>
        /// The set of special filters treated as unmatchable, computed once for the whole tree.
        ///
        /// <b>Once, from the display root, because that is what vanilla actually does.</b> Its
        /// <c>Listing_TreeThingFilter</c> holds one <c>hiddenSpecialFilters</c> list per listing and fills it
        /// lazily on the first call to <c>Visible(sfDef, node)</c> -- which comes from <c>ListCategoryChildren</c>
        /// with the root node -- then reuses that same list for every category below. Computing it per node would
        /// be the more defensible reading of the method's signature, and it was the first thing written here; it
        /// was changed back because it is a behavior change to a control that edits bills and storage, and
        /// "slightly more precise about which unmatchable toggles to hide" is not worth a divergence from what
        /// every other filter window in the game does.
        /// </summary>
        private void ResolveHiddenSpecials(TreeNode_ThingCategory root,
            IEnumerable<SpecialThingFilterDef> forceHiddenFilters)
        {
            hiddenSpecials = null;

            if (CachedHiddenSpecialFilters != null && root != null)
                hiddenSpecials = CachedHiddenSpecialFilters(root, parentFilter);

            if (forceHiddenFilters == null)
                return;

            // Copied before appending, because the list returned above is the one inside vanilla's LRU cache and
            // adding a caller's temporary exclusions to it would poison every other window that shares the entry.
            hiddenSpecials = hiddenSpecials == null
                ? new List<SpecialThingFilterDef>()
                : new List<SpecialThingFilterDef>(hiddenSpecials);

            hiddenSpecials.AddRange(forceHiddenFilters);
        }

        private void Allocate(int length)
        {
            // Both tested, and the second is not redundant: the parent-indexed arrays are one longer, so a root
            // with no rows at all still needs a slot zero. Testing only the first would send a zero-length tree
            // into Array.Clear with a count of one.
            if (selfVisible.Length < length || undiscoveredHere.Length < length + 1)
            {
                selfVisible = new bool[length];
                selfMatch = new bool[length];
                subtreeMatch = new bool[length];
                inherited = new bool[length];
                undiscovered = new bool[length];
                visibleThings = new int[length];
                allowedThings = new int[length];
                visibleSpecials = new int[length];
                allowedSpecials = new int[length];
                order = new int[length];
                undiscoveredHere = new int[length + 1];
                firstUndiscovered = new ThingDef[length + 1];
                return;
            }

            // Cleared rather than reallocated. The int arrays in particular are accumulators, so a stale value
            // left in one would be added to this frame's totals and show as a wrong tri-state.
            Array.Clear(selfVisible, 0, length);
            Array.Clear(selfMatch, 0, length);
            Array.Clear(subtreeMatch, 0, length);
            Array.Clear(inherited, 0, length);
            Array.Clear(undiscovered, 0, length);
            Array.Clear(visibleThings, 0, length);
            Array.Clear(allowedThings, 0, length);
            Array.Clear(visibleSpecials, 0, length);
            Array.Clear(allowedSpecials, 0, length);
            Array.Clear(undiscoveredHere, 0, length + 1);
            Array.Clear(firstUndiscovered, 0, length + 1);
        }

        /// <summary>
        /// Backwards: every row's own answer, then that answer added into its parent's running totals.
        ///
        /// Backwards is what makes it work. A parent always precedes its children in display order, so by the time
        /// the pass arrives at a category every one of its descendants has already contributed, and its totals are
        /// final without ever having walked its subtree.
        /// </summary>
        private void RollUp()
        {
            bool active = search.Active;

            for (int i = rows.Length - 1; i >= 0; i--)
            {
                ThingFilterRow row = rows[i];

                int things = 0;
                int allowedT = 0;
                int specials = 0;
                int allowedS = 0;

                switch (row.Kind)
                {
                    case ThingFilterRowKind.Thing:
                        selfVisible[i] = VisibleThing(row.Thing);
                        selfMatch[i] = active && search.Matches(row.Thing);
                        undiscovered[i] = Find.HiddenItemsManager.Hidden(row.Thing);

                        if (selfVisible[i])
                        {
                            things = 1;

                            if (filter.Allows(row.Thing))
                                allowedT = 1;
                        }

                        // Direct children only, and the backwards walk means the last write is the first def in
                        // display order -- which is the one whose allowance vanilla's undiscovered row reflects.
                        if (undiscovered[i])
                        {
                            undiscoveredHere[row.Parent + 1]++;
                            firstUndiscovered[row.Parent + 1] = row.Thing;
                        }

                        break;

                    case ThingFilterRowKind.Special:
                        selfVisible[i] = VisibleSpecial(row.Special);
                        selfMatch[i] = active && search.Matches(row.Special);

                        // Counted whether or not it is configurable. Vanilla's tri-state arithmetic runs over
                        // every descendant special filter without asking, and a non-configurable one that is
                        // disallowed is exactly why a category can read Partial with no visible reason.
                        if (selfVisible[i])
                        {
                            specials = 1;

                            if (filter.Allows(row.Special))
                                allowedS = 1;
                        }

                        break;

                    case ThingFilterRowKind.Category:
                        things = visibleThings[i];
                        allowedT = allowedThings[i];
                        specials = visibleSpecials[i];
                        allowedS = allowedSpecials[i];

                        selfVisible[i] = filter.OnlySpecialFilters ? specials > 0 : things > 0;
                        selfMatch[i] = active && search.Matches(row.Node.catDef.label);

                        break;

                    case ThingFilterRowKind.Undiscovered:
                        // Stands for defs that already have rows of their own, so it contributes nothing to any
                        // total. Whether it appears is decided in Collect, from the counts gathered above.
                        selfVisible[i] = true;

                        break;
                }

                if (selfVisible[i] && selfMatch[i])
                    subtreeMatch[i] = true;

                int parent = row.Parent;

                if (parent < 0)
                    continue;

                visibleThings[parent] += things;
                allowedThings[parent] += allowedT;
                visibleSpecials[parent] += specials;
                allowedSpecials[parent] += allowedS;

                if (subtreeMatch[i])
                    subtreeMatch[parent] = true;
            }
        }

        /// <summary>
        /// Forwards: a category that matched the search exempts everything beneath it from being filtered out.
        ///
        /// This is vanilla's <c>subtreeMatchedSearch</c>, which it threads through its recursion as an argument.
        /// Searching for a category by name should show you its contents, not an empty category.
        /// </summary>
        private void PushDown()
        {
            for (int i = 0; i < rows.Length; i++)
            {
                int parent = rows[i].Parent;

                // Parents are always category rows and always come first, so the value being read here is final.
                inherited[i] = parent >= 0 && (inherited[parent] || selfMatch[parent]);
            }
        }

        /// <summary>
        /// Forwards again: the rows that survive, in order.
        ///
        /// The jump to <see cref="ThingFilterRow.SubtreeEnd"/> is doing two jobs at once. A closed category skips
        /// its contents, which is the obvious one. A category that is hidden -- because nothing in it is visible,
        /// or because the search excluded it -- skips its contents too, which is what keeps a row from appearing
        /// under a parent that is not there. Vanilla gets the second for free by never recursing; here it has to
        /// be the same jump, or a special filter under an invisible category would float loose.
        /// </summary>
        private void Collect()
        {
            Count = 0;
            MatchCount = 0;

            bool active = search.Active;
            int i = 0;

            while (i < rows.Length)
            {
                ThingFilterRow row = rows[i];
                bool category = row.Kind == ThingFilterRowKind.Category;

                if (!Shown(i, row, active))
                {
                    i = category ? row.SubtreeEnd : i + 1;
                    continue;
                }

                order[Count++] = i;

                if (active && selfMatch[i])
                    MatchCount++;

                i = category && !IsOpen(i) ? row.SubtreeEnd : i + 1;
            }
        }

        private bool Shown(int index, ThingFilterRow row, bool active)
        {
            switch (row.Kind)
            {
                case ThingFilterRowKind.Category:
                    if (!selfVisible[index])
                        return false;

                    // Kept when the row itself matches, when an ancestor matched, or when anything inside it
                    // matches -- the last being why searching for an item shows you where it lives.
                    return !active || inherited[index] || selfMatch[index] || subtreeMatch[index];

                case ThingFilterRowKind.Thing:
                    if (!selfVisible[index] || undiscovered[index])
                        return false;

                    return !active || inherited[index] || selfMatch[index];

                case ThingFilterRowKind.Special:
                    // Never hidden by the search, only dimmed, which is vanilla's behavior: these are constraints
                    // on what is allowed rather than entries in the list, and hiding them mid-search would let a
                    // player narrow a filter without seeing the constraint that still applies.
                    return row.Special.configurable && selfVisible[index];

                case ThingFilterRowKind.Undiscovered:
                    return !active && undiscoveredHere[row.Parent + 1] > 0;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a category is open, including vanilla's rule that a search opens whatever contains a match.
        /// </summary>
        private bool IsOpen(int index)
        {
            ThingFilterRow row = rows[index];

            if (row.Node.IsOpen(openMask))
                return true;

            return search.Active && subtreeMatch[index];
        }

        /// <summary>Vanilla's <c>Visible(ThingDef)</c>, unchanged in substance.</summary>
        private bool VisibleThing(ThingDef thing)
        {
            if (!thing.PlayerAcquirable)
                return false;

            // A virtual def is an alias of another def -- a style variant, say -- and toggling it separately from
            // its parent is meaningless. ThingFilter.SetAllow propagates to them.
            if (thing.virtualDefParent != null)
                return false;

            // A set rather than vanilla's list scan. Same answer, and this is asked once per def per frame.
            if (forceHidden != null && forceHidden.Contains(thing))
                return false;

            if (parentFilter == null)
                return true;

            if (!parentFilter.Allows(thing))
                return false;

            return !parentFilter.IsAlwaysDisallowedDueToSpecialFilters(thing);
        }

        /// <summary>Vanilla's <c>Visible(SpecialThingFilterDef, TreeNode_ThingCategory)</c>.</summary>
        private bool VisibleSpecial(SpecialThingFilterDef special)
        {
            if (parentFilter != null && !parentFilter.Allows(special))
                return false;

            // A filter that is only about special filters shows all of them; there is nothing else in it.
            if (filter.OnlySpecialFilters)
                return true;

            return hiddenSpecials == null || !hiddenSpecials.Contains(special);
        }

        /// <summary>
        /// Opens or closes a category, reporting the state it ended in so the caller can play the matching sound.
        ///
        /// Flipped from the <i>effective</i> open state rather than the stored bit, which matters during a search:
        /// a category forced open because something inside it matched shows a chevron that says "open", and
        /// clicking it has to close it. Reading the stored bit there would open an already-open category, and the
        /// click would appear to do nothing.
        /// </summary>
        internal bool Toggle(int slot, out bool nowOpen)
        {
            int index = order[slot];
            ThingFilterRow row = rows[index];

            if (row.Kind != ThingFilterRowKind.Category)
            {
                nowOpen = false;
                return false;
            }

            nowOpen = !IsOpen(index);
            row.Node.SetOpen(openMask, nowOpen);

            return true;
        }

        /// <summary>Whether a category row would show a chevron. Everything openable does.</summary>
        internal bool OpenableAt(int slot)
        {
            ThingFilterRow row = rows[order[slot]];
            return row.Kind == ThingFilterRowKind.Category && row.Node.Openable;
        }
    }
}
