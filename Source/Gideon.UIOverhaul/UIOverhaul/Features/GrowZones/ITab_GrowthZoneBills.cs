using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    /// <summary>
    /// Derives from ITab rather than ITab_Bills: ITab_Bills.SelTable casts SelThing to
    /// Building_WorkTable, which a Zone can never satisfy, and inheriting it also drags in
    /// third-party constructor patches aimed at real workbench tabs.
    /// </summary>
    public class ITab_GrowthZoneBills : ITab
    {
        /// <summary>
        /// The size this tab asks for. What it is <i>given</i> is <c>size</c>, which is not the same thing.
        ///
        /// The inspect pane hands a tab more room than it asked for when the pane is bigger, by writing that
        /// field for the length of one draw -- so laying out from this constant instead, as this tab did until
        /// 2026-08-23, pinned two bill rows to the top left of a pane four hundred pixels taller. Copied from
        /// vanilla's own tabs, several of which have the same habit.
        /// </summary>
        private static readonly Vector2 WinSize = new Vector2(420f, 480f);
        private const float RowGap = 6f;

        private Vector2 _scrollPosition;
        private bool _scrollDragging;
        private float _scrollDragOffset;

        private Zone_GrowingPlus SelGrowingZone => Find.Selector.SelectedZone as Zone_GrowingPlus;

        protected override bool StillValid => Find.Selector.SelectedZone is Zone_GrowingPlus;

        public ITab_GrowthZoneBills()
        {
            size = WinSize;
            labelKey = "TabBills";
            tutorTag = "Bills";
        }

        /// <summary>
        /// Guarded even though InspectTabBase already wraps FillTab in a <c>Log.ErrorOnce</c>. Vanilla's guard stops
        /// the flood but leaves the tab blank, with the one report scrolled far up the log by the time anyone looks;
        /// this one puts the reason on screen where the bills should be.
        /// </summary>
        protected override void FillTab()
        {
            Rect tab = new Rect(0f, 0f, size.x, size.y);

            UIGuardedPanel.Draw("GrowZones.BillsTab", tab, () => DrawContents(),
                "The bills tab for growing zones shows a failure notice. Existing bills keep running -- this is "
                + "the tab that edits them, not the zone itself.");
        }

        private void DrawContents()
        {
            Zone_GrowingPlus zone = SelGrowingZone;
            if (zone == null)
                return;

            Rect inner = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);

            Rect addRect = new Rect(inner.x, inner.y, 110f, 30f);
            bool full = zone.BillStack.Count >= BillStack.MaxCount;
            if (GzpPalette.GrayButton(addRect, "AddBill".Translate(), !full, true))
                Find.WindowStack.Add(new Dialog_AddGrowBill(zone, tutorTag));
            if (full)
                TooltipHandler.TipRegion(addRect, (TipSignal) $"This zone already has the maximum of {BillStack.MaxCount} bills.");

            // CheckboxRow rather than Widgets.CheckboxLabeled: the vanilla one draws its own textures
            // and was the last piece of stock chrome on this tab. It also owns the tooltip and the
            // toggle sound, so both come for free.
            Rect toggleRect = new Rect(inner.xMax - 190f, inner.y, 190f, 30f);
            bool autoUnsuspend = zone.AutoUnsuspendActive;
            if (GzpPalette.CheckboxRow(toggleRect, "Auto-Unsuspend", ref autoUnsuspend,
                    "Ensures that relevant bills are unsuspended if stocks run below the desired count."
                    + "\nEnabled by default. Disable if you do not want this to be the case."))
            {
                zone.AutoUnsuspendActive = autoUnsuspend;
            }

            Rect outRect = new Rect(inner.x, addRect.yMax + 8f, inner.width, inner.height - addRect.height - 8f);
            DrawBillList(zone, outRect);
        }

        private void DrawBillList(Zone_GrowingPlus zone, Rect outRect)
        {
            if (zone.BillStack.Count == 0)
            {
                Color previous = GUI.color;
                GUI.color = GzpPalette.TextDim;
                Widgets.Label(new Rect(outRect.x, outRect.y + 4f, outRect.width, 48f),
                    "No bills. Nothing will be sown here while Require Active Bill is on.");
                GUI.color = previous;
                return;
            }

            // Snapshot: a row's delete button mutates BillStack.Bills mid-draw.
            List<Bill> bills = new List<Bill>(zone.BillStack.Bills);
            float viewHeight = bills.Count * (GrowBillRow.RowHeight + RowGap);
            Rect view = new Rect(0f, 0f, GzpPalette.ContentWidth(outRect), viewHeight);

            Widgets.BeginScrollView(outRect, ref _scrollPosition, view, false);
            float y = 0f;
            for (int index = 0; index < bills.Count; index++)
            {
                if (bills[index] is not Bill_Growing growing)
                    continue;
                GrowBillRow.Draw(new Rect(0f, y, view.width, GrowBillRow.RowHeight), growing, zone, index, bills.Count);
                y += GrowBillRow.RowHeight + RowGap;
            }
            Widgets.EndScrollView();
            GzpPalette.FlatScrollbar(outRect, viewHeight, ref _scrollPosition, ref _scrollDragging, ref _scrollDragOffset);
        }
    }
}
