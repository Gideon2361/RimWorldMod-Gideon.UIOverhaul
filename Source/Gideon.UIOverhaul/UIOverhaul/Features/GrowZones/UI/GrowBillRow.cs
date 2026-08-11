using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.GrowZones.UI
{
    /// <summary>
    /// Draws a <see cref="Bill_Growing"/> in the Modern UI style, replacing Bill.DoInterface.
    /// Vanilla's row is fixed chrome -- DoInterface is not virtual -- so the whole row is
    /// reimplemented here: icon, label, state stripe, repeat mode, target controls and progress.
    /// </summary>
    public static class GrowBillRow
    {
        public const float RowHeight = 78f;

        private const float IconSize = 40f;
        private const float ButtonSize = 20f;
        private const float Pad = 10f;
        private const float TargetFieldWidth = 58f;

        public static void Draw(Rect rect, Bill_Growing bill, Zone_GrowingPlus zone, int index, int total)
        {
            bool forever = bill.repeatMode == BillRepeatModeDefOf.Forever;
            bool hover = Mouse.IsOver(rect);

            GzpPalette.Card(rect, StateColor(bill), hover);

            Color previous = GUI.color;
            Text.Font = GameFont.Small;

            Rect iconRect = new Rect(rect.x + Pad, rect.y + Pad, IconSize, IconSize);
            if (bill.plantDef != null)
                Widgets.ThingIcon(iconRect, bill.plantDef);

            float contentX = iconRect.xMax + Pad;
            float buttonsWidth = ButtonSize * 4f + 6f;

            // Title line.
            GUI.color = bill.suspended || bill.paused ? GzpPalette.TextDim : GzpPalette.Stat;
            Widgets.Label(new Rect(contentX, rect.y + 6f, rect.width - contentX - buttonsWidth - Pad, 24f),
                bill.LabelCap);
            GUI.color = previous;

            DrawRowButtons(rect, bill, zone, index, total);
            DrawModeAndTarget(rect, bill, zone, contentX, forever);

            if (!forever)
                DrawProgress(rect, bill, zone, contentX);

            DrawStateBadge(rect, bill);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static Color StateColor(Bill_Growing bill)
        {
            if (bill.suspended)
                return GzpPalette.Bad;
            if (bill.paused)
                return GzpPalette.Warn;
            return GzpPalette.Good;
        }

        private static void DrawRowButtons(Rect rect, Bill_Growing bill, Zone_GrowingPlus zone, int index, int total)
        {
            float x = rect.xMax - Pad - ButtonSize;
            float y = rect.y + 6f;

            if (GzpPalette.IconButton(new Rect(x, y, ButtonSize, ButtonSize), TexButton.Delete, "Delete bill", GzpPalette.Bad))
            {
                zone.BillStack.Delete(bill);
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            x -= ButtonSize + 2f;
            string suspendTip = bill.suspended ? "Resume bill" : "Suspend bill";
            if (GzpPalette.IconButton(new Rect(x, y, ButtonSize, ButtonSize), TexButton.Suspend, suspendTip,
                    bill.suspended ? GzpPalette.Warn : GzpPalette.TextDim))
            {
                bill.suspended = !bill.suspended;
                zone.UpdatePlantDefToGrow();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            x -= ButtonSize + 2f;
            if (index < total - 1
                && GzpPalette.IconButton(new Rect(x, y, ButtonSize, ButtonSize), TexButton.ReorderDown, "Lower priority"))
            {
                zone.BillStack.Reorder(bill, 1);
                zone.UpdatePlantDefToGrow();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            x -= ButtonSize + 2f;
            if (index > 0
                && GzpPalette.IconButton(new Rect(x, y, ButtonSize, ButtonSize), TexButton.ReorderUp, "Raise priority"))
            {
                zone.BillStack.Reorder(bill, -1);
                zone.UpdatePlantDefToGrow();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        private static void DrawModeAndTarget(Rect rect, Bill_Growing bill, Zone_GrowingPlus zone, float contentX, bool forever)
        {
            float lineY = rect.y + 32f;
            Rect modeRect = new Rect(contentX, lineY, 120f, 24f);
            if (GzpPalette.GrayButton(modeRect, bill.RepeatModeLabel))
                Find.WindowStack.Add(new FloatMenu(bill.RepeatModeOptions()));

            Color previous = GUI.color;

            if (forever)
            {
                GUI.color = GzpPalette.TextDim;
                Widgets.Label(new Rect(modeRect.xMax + 8f, lineY + 2f, rect.width - modeRect.xMax - 20f, 22f),
                    "Always sowing");
                GUI.color = previous;
                return;
            }

            // Right-aligned group: [-] [typed target] [+], with the live count to its left.
            Rect plusRect = new Rect(rect.xMax - Pad - ButtonSize, lineY + 2f, ButtonSize, ButtonSize);
            Rect fieldRect = new Rect(plusRect.x - 4f - TargetFieldWidth, lineY + 2f, TargetFieldWidth, 22f);
            Rect minusRect = new Rect(fieldRect.x - 4f - ButtonSize, lineY + 2f, ButtonSize, ButtonSize);

            if (GzpPalette.IconButton(plusRect, TexButton.Plus, "Increase target"))
            {
                bill.targetCount += GenUI.CurrentAdjustmentMultiplier();
                bill.targetCountBuffer = null;
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            if (GzpPalette.IconButton(minusRect, TexButton.Minus, "Decrease target"))
            {
                bill.targetCount = Mathf.Max(0, bill.targetCount - GenUI.CurrentAdjustmentMultiplier());
                bill.targetCountBuffer = null;
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            Widgets.DrawBoxSolid(fieldRect, GzpPalette.BGD);
            GUI.color = GzpPalette.Stat;
            Widgets.TextFieldNumeric(fieldRect, ref bill.targetCount, ref bill.targetCountBuffer, 0f, 1000000f);
            GUI.color = previous;
            TooltipHandler.TipRegion(fieldRect, (TipSignal) "Target amount. Type a value, or use the arrows.");

            int current = bill.CurrentCountCached(zone);
            Rect countRect = new Rect(modeRect.xMax + 8f, lineY + 2f, minusRect.x - modeRect.xMax - 12f, 22f);
            GUI.color = current >= bill.targetCount ? GzpPalette.Warn : GzpPalette.Stat;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(countRect, $"{current}  /");
            Text.Anchor = anchor;
            GUI.color = previous;
        }

        private static void DrawProgress(Rect rect, Bill_Growing bill, Zone_GrowingPlus zone, float contentX)
        {
            int current = bill.CurrentCountCached(zone);
            float fill = bill.targetCount <= 0 ? 1f : current / (float) bill.targetCount;
            Rect bar = new Rect(contentX, rect.yMax - 14f, rect.width - contentX - Pad, 5f);
            GzpPalette.Bar(bar, fill, fill >= 1f ? GzpPalette.Warn : GzpPalette.Accent);
        }

        private static void DrawStateBadge(Rect rect, Bill_Growing bill)
        {
            if (!bill.suspended && !bill.paused)
                return;

            Widgets.DrawBoxSolid(rect, GzpPalette.DimScrim);

            string label = bill.suspended ? "SUSPENDED" : "TARGET MET";
            Color color = bill.suspended ? GzpPalette.Bad : GzpPalette.Warn;

            Vector2 size = Text.CalcSize(label);
            Rect badge = new Rect(rect.center.x - size.x / 2f - 10f, rect.center.y - 11f, size.x + 20f, 22f);
            Widgets.DrawBoxSolid(badge, GzpPalette.BGD);

            Color previous = GUI.color;
            GUI.color = color;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(badge, label);
            Text.Anchor = anchor;
            GUI.color = previous;
        }
    }
}
