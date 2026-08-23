using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// One patient in full: their health card, the operations queued on them, and the standing orders they are
    /// under.
    ///
    /// <b>The health half is the inspect pane's own Health body, the same code.</b> A condition therefore reads
    /// identically whether you found this person here or clicked them on the map, which is the whole reason it is
    /// worth reusing rather than writing a second reading that could disagree with the first.
    ///
    /// <b>What this adds is everything with a button on it.</b> The inspect pane lists queued operations and can
    /// do nothing about them; here they can be suspended, cancelled and added to, and the standing orders that
    /// point at this patient are listed underneath with the same treatment. That is the difference between a
    /// panel you read and a screen you work from.
    /// </summary>
    internal static class HospitalPatientPane
    {
        internal const float PaneWidth = 340f;

        private const float HeaderHeight = 52f;

        private const float PortraitSize = 44f;

        private const float ButtonHeight = 26f;

        /// <summary>Scratch for the standing orders pointed at this patient. Never held past a draw.</summary>
        private static readonly List<StandingDrugOrder> Orders = new List<StandingDrugOrder>();

        private static Vector2 scroll;

        /// <summary>Height the column came to last frame. Remembered rather than predicted.</summary>
        private static float measured;

        /// <summary>Which patient the measurement belongs to, so a switch does not inherit a stale height.</summary>
        private static Pawn measuredFor;

        /// <summary>
        /// Draws the pane, and answers whether the patient is still worth drawing.
        ///
        /// A false return means the pane should close: the patient died, left the map, or was never really there.
        /// The caller is the tab, which falls back to no pane at all rather than to some other patient.
        /// </summary>
        internal static bool Draw(Rect rect, HospitalPatient patient, UIColorPaletteDef palette, Action changed,
            Action close)
        {
            if (patient == null || patient.Pawn == null || patient.Pawn.Destroyed)
                return false;

            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Rect inner = rect.ContractedBy(10f);

            Header(new Rect(inner.x, inner.y, inner.width, HeaderHeight), patient, palette, close);

            Rect body = new Rect(inner.x, inner.y + HeaderHeight + 4f, inner.width,
                Mathf.Max(0f, inner.height - HeaderHeight - 4f));

            if (measuredFor != patient.Pawn)
            {
                measuredFor = patient.Pawn;
                measured = 0f;
                scroll = Vector2.zero;
            }

            Rect view = new Rect(0f, 0f, body.width - 18f, measured > 0f ? measured : body.height);

            Widgets.BeginScrollView(body, ref scroll, view);

            Rect column = new Rect(0f, 0f, view.width, view.height);

            float y = InspectHealthBody.Draw(column, patient.Pawn, palette, false);

            y = Operations(column, y, patient, palette, changed);
            y = Standing(column, y, patient, palette, changed);

            measured = y + 8f;

            Widgets.EndScrollView();

            return true;
        }

        private static void Header(Rect rect, HospitalPatient patient, UIColorPaletteDef palette, Action close)
        {
            Pawn pawn = patient.Pawn;

            PawnPortraitCell.Draw(new Rect(rect.x, rect.y, PortraitSize, PortraitSize), pawn, palette,
                palette.SurfaceSunken);

            Rect text = new Rect(rect.x + PortraitSize + 8f, rect.y, rect.width - PortraitSize - 34f, rect.height);

            float y = TabParts.Line(text, text.y, pawn.LabelShortCap, palette.TextPrimary);

            string badge = patient.Summary.Tag;
            float x = text.x;

            if (!badge.NullOrEmpty())
            {
                Rect pill = TabParts.Pill(text, x, y + 1f, badge, patient.Summary.TagColor(palette), palette);

                x = pill.xMax + 4f;
            }

            TabParts.Line(new Rect(x, y, Mathf.Max(20f, text.xMax - x), 0f), y + 1f,
                patient.Summary.Label, patient.Summary.Color(palette), GameFont.Tiny);

            Rect closeRect = new Rect(rect.xMax - 24f, rect.y, 24f, 24f);

            if (Widgets.ButtonImage(closeRect, TexButton.CloseXSmall))
                close();

            // The portrait jumps the camera on click, the way it does everywhere else in the mod.
            if (PawnPortraitCell.IsOver(new Rect(rect.x, rect.y, PortraitSize, PortraitSize))
                && Widgets.ButtonInvisible(new Rect(rect.x, rect.y, PortraitSize, PortraitSize)))
                PawnCameraJump.Request(pawn);
        }

        // ---------------------------------------------------------------------------------------
        // Operations
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The queued operations, with the two controls a bill actually needs.
        ///
        /// <b>Suspend rather than only cancel,</b> because a bill you are waiting on a part for is not a bill you
        /// want to rewrite later: suspending keeps the decision and stops the doctor walking over.
        /// </summary>
        private static float Operations(Rect column, float y, HospitalPatient patient, UIColorPaletteDef palette,
            Action changed)
        {
            Pawn pawn = patient.Pawn;

            BillStack bills = UIGuard.Try("Hospital.PaneBills", () => pawn.health.surgeryBills, null, null);

            int count = bills != null ? bills.Count : 0;

            y = InspectPaneParts.Cap(column, y, "Operations", count == 0 ? "none queued" : count + " queued",
                palette);

            if (count == 0)
            {
                y = InspectPaneParts.Note(column, y, "Nothing is scheduled for " + pawn.LabelShortCap + ".",
                    palette);
            }
            else
            {
                for (int i = 0; i < bills.Count; i++)
                {
                    Bill bill = bills[i];

                    if (bill == null)
                        continue;

                    y = Bill(column, y, bill, palette, changed);
                }
            }

            y += TabParts.RowGap;

            if (TabParts.Button(new Rect(column.x, y, column.width, ButtonHeight), "Add an operation",
                    palette))
                Find.WindowStack.Add(new Dialog_AddOperation(pawn));

            return y + ButtonHeight + TabParts.BlockGap;
        }

        private static float Bill(Rect column, float y, Bill bill, UIColorPaletteDef palette, Action changed)
        {
            float line = UIFonts.LineHeightOf(GameFont.Tiny);
            float height = Mathf.Max(line + 4f, 20f);

            Rect row = new Rect(column.x, y, column.width, height);

            float buttons = 46f;

            TabParts.Line(new Rect(row.x, row.y, Mathf.Max(20f, row.width - buttons - 4f), 0f), row.y + 2f,
                bill.LabelCap, bill.suspended ? palette.TextDisabled : palette.TextPrimary, GameFont.Tiny);

            if (Widgets.ButtonImage(new Rect(row.xMax - buttons, row.y + 1f, 18f, 18f),
                    bill.suspended ? TexButton.Play : TexButton.Suspend))
            {
                bill.suspended = !bill.suspended;

                changed();
            }

            if (Widgets.ButtonImage(new Rect(row.xMax - 20f, row.y + 1f, 18f, 18f), TexButton.Delete))
            {
                UIGuard.Try("Hospital.CancelBill", () => bill.billStack.Delete(bill),
                    "The operation could not be cancelled here. It can still be removed from the pawn's own "
                    + "health tab.");

                changed();
            }

            return row.yMax + TabParts.RowGap;
        }

        // ---------------------------------------------------------------------------------------
        // Standing orders
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The standing orders pointed at this patient, whether by name or by a colony-wide target.
        ///
        /// <b>An order held by a safeguard says so on its row.</b> "Held: addicted" is a fact the player can act
        /// on; an order that has quietly stopped is a bug report waiting to be filed.
        /// </summary>
        private static float Standing(Rect column, float y, HospitalPatient patient, UIColorPaletteDef palette,
            Action changed)
        {
            Pawn pawn = patient.Pawn;

            MapComponent_StandingOrders.For(pawn, Orders);

            y = InspectPaneParts.Cap(column, y, "Standing orders",
                Orders.Count == 0 ? "none" : Orders.Count.ToString(), palette);

            if (Orders.Count == 0)
            {
                y = InspectPaneParts.Note(column, y,
                    "Nothing is given to " + pawn.LabelShortCap + " on a schedule.", palette);
            }
            else
            {
                for (int i = 0; i < Orders.Count; i++)
                    y = Order(column, y, Orders[i], pawn, palette);
            }

            y += TabParts.RowGap;

            if (TabParts.Button(new Rect(column.x, y, column.width, ButtonHeight), "Add a standing order",
                    palette))
                New(pawn, changed);

            Orders.Clear();

            return y + ButtonHeight + TabParts.BlockGap;
        }

        private static float Order(Rect column, float y, StandingDrugOrder order, Pawn pawn,
            UIColorPaletteDef palette)
        {
            string blocked = order.BlockedBy(pawn);

            string right = blocked ?? Countdown(order, pawn);

            Color color = blocked == null
                ? palette.Success
                : blocked == "paused" || blocked == "condition not met"
                    ? palette.TextDisabled
                    : palette.Warning;

            float before = y;

            y = InspectPaneParts.Entry(column, y, order.Label + ", every " + order.FrequencyLabel, right, color,
                order.nurse != null ? "Nurse: " + order.NurseLabel + "." : null, palette);

            Rect row = new Rect(column.x, before, column.width, y - before);

            if (!Widgets.ButtonInvisible(row))
                return y;

            Find.WindowStack.Add(new Dialog_StandingOrder(order, pawn.MapHeld));

            return y;
        }

        private static string Countdown(StandingDrugOrder order, Pawn pawn)
        {
            int left = order.NextDoseIn(pawn);

            if (left <= 0)
                return left < -GenDate.TicksPerHour
                    ? "overdue " + (-left).ToStringTicksToPeriod(false, false, false)
                    : "due now";

            return "in " + left.ToStringTicksToPeriod(false, false, false);
        }

        /// <summary>
        /// Creates an order already pointed at this patient and opens it for editing.
        ///
        /// Pre-pointed because it was reached from their pane: asking who it is for immediately after clicking
        /// the button on somebody's card is the sort of question a screen should already know the answer to.
        /// </summary>
        private static void New(Pawn pawn, Action changed)
        {
            UIGuard.Try("Hospital.NewOrder", () =>
            {
                MapComponent_StandingOrders component = MapComponent_StandingOrders.For(pawn.MapHeld);

                if (component == null)
                    return;

                StandingDrugOrder order = new StandingDrugOrder
                {
                    target = StandingOrderTarget.OnePatient,
                    patient = pawn
                };

                component.Add(order);

                changed();

                Find.WindowStack.Add(new Dialog_StandingOrder(order, pawn.MapHeld));
            }, "A standing order could not be created.");
        }
    }
}
