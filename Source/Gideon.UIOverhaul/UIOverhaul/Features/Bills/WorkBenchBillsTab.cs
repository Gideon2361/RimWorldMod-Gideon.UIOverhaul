using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// The contents of a workbench's Bills tab: an Add bill button and this bench's bills as cards.
    ///
    /// <b>This is the growing zone bills tab with recipes in it.</b> Same size, same button in the same corner,
    /// same scrolling list of accent striped cards, same flat scrollbar. The two features add work to a thing and
    /// now look like they were built by the same people, which they were.
    ///
    /// <b>Static, because the tab it draws into is not ours.</b> <c>ITab_Bills</c> is instantiated by RimWorld
    /// once per workbench def and there is no hook for adding fields to it, so what little state this needs lives
    /// here and is keyed to the bench being looked at. That is safe because exactly one inspect tab is open at a
    /// time: there is only ever one bench on screen to be confused about.
    ///
    /// <b>The typing boxes are held by position in the list, not per bill.</b> A number you can type needs a
    /// control that survives between frames, and one per bill would leak one for every bill the player ever
    /// scrolled past. <see cref="BillNumberBox"/> notices when the bill under a given box changes and refills
    /// itself, which is what makes reuse by position correct rather than merely cheap.
    /// </summary>
    internal static class WorkBenchBillsTab
    {
        /// <summary>
        /// How wide the pane is, which is vanilla's 420 plus an inch.
        ///
        /// <b>Wider than vanilla because a production bill's row carries more than a growing bill's.</b> The
        /// growing row fits 420 because a plant needs a mode and one number; this one adds a fifth button and a
        /// live count of what the colony holds, and at 420 the count wrapped across the mode button. An inch at
        /// RimWorld's 96 pixel reference is 96, rounded to 100.
        ///
        /// <b>Read by <c>Patch_BillsTabSize</c>, which is what makes it take effect.</b> Changing this alone
        /// would widen the drawing and not the pane, and the difference would be clipped.
        /// </summary>
        internal const float Width = 520f;

        /// <summary>Vanilla's own height, which the growing zone tab also matches.</summary>
        internal const float Height = 480f;
        private const float RowGap = 6f;
        private const float Pad = 10f;

        private static Vector2 scroll;
        private static bool dragging;
        private static float dragOffset;

        /// <summary>Which bench the boxes below currently belong to, so switching benches starts them clean.</summary>
        private static int shownFor = -1;

        private static readonly List<BillNumberBox> Boxes = new List<BillNumberBox>();

        internal static void Draw(Building_WorkTable bench)
        {
            Rect tab = new Rect(0f, 0f, Width, Height);

            if (bench == null)
                return;

            if (bench.thingIDNumber != shownFor)
            {
                shownFor = bench.thingIDNumber;
                scroll = Vector2.zero;
            }

            Rect inner = tab.ContractedBy(Pad);
            BillStack stack = bench.billStack;
            bool full = stack != null && stack.Count >= BillCap.Current;

            Rect add = new Rect(inner.x, inner.y, 110f, 30f);

            if (GzpPalette.GrayButton(add, "Add Bill", !full, true))
                Find.WindowStack.Add(new Dialog_AddWorkBill(bench, null));

            if (full)
            {
                TooltipHandler.TipRegion(add,
                    (TipSignal)("This bench already has the maximum of " + BillCap.Current + " bills."));
            }

            Count(new Rect(add.xMax + 10f, inner.y, inner.width - add.width - 10f, 30f), stack);

            Rect list = new Rect(inner.x, add.yMax + 8f, inner.width, inner.height - add.height - 8f);

            List(list, bench, stack);
        }

        /// <summary>
        /// How many bills this bench holds, out of what it may.
        ///
        /// Worth the line because the cap is the player's own setting rather than vanilla's fifteen, and the only
        /// place they can see what they chose is here. Read from <see cref="BillCap"/> per frame so moving the
        /// slider shows up immediately.
        /// </summary>
        private static void Count(Rect rect, BillStack stack)
        {
            int count = stack?.Count ?? 0;

            Color previous = GUI.color;
            TextAnchor anchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = GzpPalette.TextDim;

            Widgets.Label(rect, count + " / " + BillCap.Current + " bills");

            Text.Font = GameFont.Small;
            Text.Anchor = anchor;
            GUI.color = previous;
        }

        private static void List(Rect rect, Building_WorkTable bench, BillStack stack)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // A snapshot, because a row's delete button mutates the stack while the list is still being drawn.
            List<Bill> bills = stack == null ? new List<Bill>() : new List<Bill>(stack.Bills);

            // Only production bills carry the settings these cards edit. Anything else a mod has put on a bench is
            // left out rather than half drawn, and counted here so the empty message does not claim an empty bench
            // when something is sitting on it that we simply cannot show.
            List<Bill_Production> shown = new List<Bill_Production>();

            foreach (Bill bill in bills)
            {
                if (bill is Bill_Production production)
                    shown.Add(production);
            }

            if (shown.Count == 0)
            {
                Color previous = GUI.color;
                GUI.color = GzpPalette.TextDim;

                Widgets.Label(new Rect(rect.x, rect.y + 4f, rect.width, 48f),
                    bills.Count == 0
                        ? "No bills. Nothing will be made here until one is added."
                        : "No bills this window can show. Use RimWorld's own tab for the rest.");

                GUI.color = previous;

                return;
            }

            float height = shown.Count * (WorkBillRow.RowHeight + RowGap);
            Rect view = new Rect(0f, 0f, GzpPalette.ContentWidth(rect), height);

            Widgets.BeginScrollView(rect, ref scroll, view, false);

            float y = 0f;

            for (int index = 0; index < shown.Count; index++)
            {
                WorkBillRow.Draw(new Rect(0f, y, view.width, WorkBillRow.RowHeight), shown[index], bench, index,
                    shown.Count, Box(index), null);

                y += WorkBillRow.RowHeight + RowGap;
            }

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(rect, height, ref scroll, ref dragging, ref dragOffset);
        }

        /// <summary>The typing box for the row at this position, made on first use and kept.</summary>
        private static BillNumberBox Box(int index)
        {
            while (Boxes.Count <= index)
                Boxes.Add(new BillNumberBox());

            return Boxes[index];
        }
    }
}
