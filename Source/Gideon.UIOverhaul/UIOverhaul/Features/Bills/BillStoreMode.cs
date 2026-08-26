using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Where a bill's products go: dropped where they are made, hauled to the best stockpile that will take them,
    /// or hauled to one the player names.
    ///
    /// <b>Missing until 2026-08-22, and it showed.</b> Our bill settings covered ingredients, reach, worker and
    /// skill; RimWorld's own dialog also has this, and a player moving to our window lost the only control that
    /// decides whether a hundred meals pile up on the kitchen floor. The bill templates have been carrying the
    /// store mode across colonies the whole time, so the setting could be applied and not seen, which is the worse
    /// half of missing.
    ///
    /// <b>Three segments rather than vanilla's dropdown,</b> per Aaron's standing preference for modern controls
    /// over float menus. The choice is three states, two of which need no further input, so a menu was hiding a
    /// two thirds of a decision behind a click. The stockpile list only appears when a specific one is being
    /// chosen, which is the only time it means anything.
    ///
    /// <b>The list is built the way vanilla builds its menu,</b> including the two rules that are easy to miss: a
    /// slot group belonging to a storage group is represented by the group rather than by each of its members, and
    /// a storage building that cannot be renamed is left out entirely. Reproducing the enumeration rather than the
    /// menu means the same stockpiles appear in both windows.
    /// </summary>
    internal static class BillStoreMode
    {
        private const float SegmentHeight = 26f;
        private const float RowHeight = 24f;
        private const float NoteGap = 6f;

        /// <summary>How many stockpile rows are shown before the list scrolls.</summary>
        private const int VisibleRows = 4;

        private static Vector2 scroll;

        /// <summary>Scratch for the stockpile walk, rebuilt per call. The UI is single threaded.</summary>
        private static readonly List<ISlotGroup> Groups = new List<ISlotGroup>();

        /// <summary>
        /// How tall the whole control is for this bill, so the pane can lay out around it.
        ///
        /// The specific case is taller because it carries a list, and a fixed height for both would either
        /// waste a band of nothing in the common case or clip the list in the uncommon one.
        /// </summary>
        internal static float HeightFor(Bill_Production bill)
        {
            float height = SegmentHeight + NoteGap;

            if (bill != null && bill.GetStoreMode() == BillStoreModeDefOf.SpecificStockpile)
                return height + VisibleRows * RowHeight;

            // The segments and nothing else. This was 34 pixels taller for a sentence restating whichever
            // segment was lit, and a height left behind after the thing it measured is gone is a gap the next
            // person has to work out the reason for.
            return height;
        }

        internal static void Draw(Rect rect, Bill_Production bill, UIColorPaletteDef palette)
        {
            if (bill == null)
                return;

            UIGuard.Try("Bills.StoreMode", () => Contents(rect, bill, palette),
                "The output setting could not be drawn. The bill still stores its products the way it did.");
        }

        private static void Contents(Rect rect, Bill_Production bill, UIColorPaletteDef palette)
        {
            BillStoreModeDef mode = bill.GetStoreMode();
            List<ISlotGroup> groups = GroupsFor(bill);

            float third = Mathf.Floor((rect.width - 8f) / 3f);
            Rect row = new Rect(rect.x, rect.y, third, SegmentHeight);

            if (UIActionButtonControl.Draw(row, "Drop here", true, mode == BillStoreModeDefOf.DropOnFloor)
                && mode != BillStoreModeDefOf.DropOnFloor)
                bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor);

            row.x += third + 4f;

            if (UIActionButtonControl.Draw(row, "Best stockpile", true, mode == BillStoreModeDefOf.BestStockpile)
                && mode != BillStoreModeDefOf.BestStockpile)
                bill.SetStoreMode(BillStoreModeDefOf.BestStockpile);

            row.x += third + 4f;
            row.width = rect.xMax - row.x;

            // Disabled rather than hidden when there is nowhere to send anything. A control that appears once you
            // have built a stockpile is a control nobody finds; one that is visibly unavailable explains itself.
            bool any = groups.Count > 0;
            bool specific = mode == BillStoreModeDefOf.SpecificStockpile;

            if (UIActionButtonControl.Draw(row, "Take to...", any, specific) && !specific)
                bill.SetStoreMode(BillStoreModeDefOf.SpecificStockpile, Chosen(bill, groups));

            if (!any)
            {
                TooltipHandler.TipRegion(row,
                    (TipSignal) "There are no stockpiles or storage buildings on this map to send products to.");
            }

            float y = rect.y + SegmentHeight + NoteGap;

            if (specific)
            {
                List(new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y)), bill, groups, palette);

                return;
            }

            // The chosen segment is the answer, so nothing is written under it. "Products are hauled to the best
            // stockpile that accepts them" is the segment labelled Best stockpile, in a sentence. Removed
            // 2026-08-23 on Aaron's instruction; see HeightFor, which no longer reserves the room.
        }

        /// <summary>
        /// The group this bill should point at when Take to is first chosen.
        ///
        /// <b>Never null, because the game logs an error for a specific store mode without one.</b> The bill's own
        /// group is kept when it still exists, so switching away to Drop and back returns to the same stockpile
        /// rather than to whichever one happens to be first.
        /// </summary>
        private static ISlotGroup Chosen(Bill_Production bill, List<ISlotGroup> groups)
        {
            ISlotGroup current = bill.GetSlotGroup();

            if (current != null && groups.Contains(current))
                return current;

            return groups.Count > 0 ? groups[0] : null;
        }

        /// <summary>
        /// The stockpiles, as rows that read back which one is chosen.
        ///
        /// <b>Incompatible destinations are marked and still offered,</b> which is vanilla's behaviour: a
        /// stockpile that will not accept this product today may accept it once its filter changes, and refusing
        /// the choice would hide the reason it is wrong.
        /// </summary>
        private static void List(Rect rect, Bill_Production bill, List<ISlotGroup> groups,
            UIColorPaletteDef palette)
        {
            ISlotGroup current = bill.GetSlotGroup();

            Rect view = new Rect(0f, 0f, rect.width - 18f, groups.Count * RowHeight);
            bool scrolls = view.height > rect.height;

            if (!scrolls)
                view.width = rect.width;

            Widgets.BeginScrollView(rect, ref scroll, view);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;

                for (int i = 0; i < groups.Count; i++)
                {
                    ISlotGroup group = groups[i];
                    Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight - 2f);
                    bool chosen = group == current;

                    if (chosen)
                        Widgets.DrawBoxSolid(row, palette.SelectionOverlay);
                    else if (Mouse.IsOver(row))
                        Widgets.DrawBoxSolid(row, palette.HoverOverlay);

                    bool fits = bill.recipe == null || bill.recipe.WorkerCounter == null
                                || bill.recipe.WorkerCounter.CanPossiblyStore(bill, group);

                    GUI.color = chosen ? palette.Accent : fits ? palette.TextPrimary : palette.TextDisabled;

                    string label = SlotGroup.GetGroupLabel(group);

                    if (!fits)
                        label += "  (" + "IncompatibleLower".Translate() + ")";

                    Widgets.LabelEllipses(new Rect(row.x + 8f, row.y, row.width - 12f, row.height), label);

                    if (Widgets.ButtonInvisible(row) && !chosen)
                        bill.SetStoreMode(BillStoreModeDefOf.SpecificStockpile, group);
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Every place this bill's products could be sent, in the game's own priority order.
        ///
        /// The two rules copied from <c>Dialog_BillConfig</c>: a slot group that belongs to a storage group is
        /// represented once by that group rather than by each shelf in it, and a storage building that cannot be
        /// renamed is skipped, since it has no name to offer and is reached through its group instead.
        ///
        /// The returned list is scratch and is valid until the next call.
        /// </summary>
        internal static List<ISlotGroup> GroupsFor(Bill_Production bill)
        {
            Groups.Clear();

            Map map = bill?.Map;

            if (map?.haulDestinationManager == null)
                return Groups;

            List<SlotGroup> all = map.haulDestinationManager.AllGroupsListInPriorityOrder;

            if (all == null)
                return Groups;

            for (int i = 0; i < all.Count; i++)
            {
                SlotGroup group = all[i];

                if (group == null)
                    continue;

                if (group.StorageGroup != null)
                {
                    if (!Groups.Contains(group.StorageGroup))
                        Groups.Add(group.StorageGroup);

                    continue;
                }

                Building_Storage building = group.parent as Building_Storage;

                if (building != null && !(building is IRenameable))
                    continue;

                Groups.Add(group);
            }

            return Groups;
        }
    }
}
