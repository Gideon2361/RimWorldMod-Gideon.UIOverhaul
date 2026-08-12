using Gideon.UIFramework.Controls;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.GrowZones.UI
{
    /// <summary>
    /// Colony-wide list of growing zones, in the style of the Animals tab: one row per zone showing
    /// what it grows, how far along its bill is, whether the temperature suits the crop, and what
    /// harvesting it would yield. Clicking a row jumps the camera to the zone and selects it.
    /// </summary>
    public class MainTabWindow_GrowZones : MainTabWindow
    {
        private const float HeaderHeight = 42f;
        private const float ColumnHeaderHeight = 24f;
        private const float RowHeight = 52f;
        private const float RowGap = 3f;
        private const float Pad = 12f;
        private const float IconSize = 24f;

        private Vector2 scroll;

        /// <summary>Card chrome for the zone rows, reconfigured per row as the list is drawn.</summary>
        private readonly UICardControl rowCard = new UICardControl();
        private bool draggingScrollbar;
        private float scrollDragOffset;

        private readonly List<Zone_Growing> zones = new List<Zone_Growing>();

        public override Vector2 RequestedTabSize => new Vector2(1020f, 580f);

        protected override float Margin => 0f;

        public override void PostOpen()
        {
            base.PostOpen();
            // Fresh figures the moment the tab appears, and it releases zones deleted since the
            // last time it was open.
            GrowZoneStatusCache.Clear();
            scroll = Vector2.zero;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, GzpPalette.BG);

            GatherZones();

            Rect header = new Rect(inRect.x + Pad, inRect.y, inRect.width - Pad * 2f, HeaderHeight);
            DrawHeader(header);

            Rect columns = new Rect(header.x, header.yMax, header.width, ColumnHeaderHeight);
            DrawColumnHeaders(columns);

            Rect list = new Rect(header.x, columns.yMax + 4f, header.width,
                inRect.yMax - columns.yMax - 4f - Pad);

            if (zones.Count == 0)
            {
                DrawEmptyState(list);
                return;
            }

            DrawList(list);
        }

        /// <summary>
        /// Rebuilt each frame rather than cached: zones are created, deleted and renamed freely
        /// while the tab is open, and the list is at most a few dozen entries.
        /// </summary>
        private void GatherZones()
        {
            zones.Clear();

            Map map = Find.CurrentMap;
            if (map == null)
                return;

            foreach (Zone zone in map.zoneManager.AllZones)
            {
                // Zone_Growing rather than Zone_GrowingPlus: an unconverted vanilla zone still
                // belongs in this list, and it reads perfectly well without a bill stack.
                if (zone is Zone_Growing growing)
                    zones.Add(growing);
            }

            zones.SortBy(z => z.label);
        }

        private void DrawHeader(Rect r)
        {
            Text.Font = GameFont.Medium;
            Color previous = GUI.color;
            GUI.color = GzpPalette.Stat;
            Widgets.Label(new Rect(r.x, r.y + 4f, r.width * 0.6f, 32f), "Growing Zones");

            Text.Font = GameFont.Small;
            GUI.color = GzpPalette.TextDim;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(r.x + r.width * 0.6f, r.y, r.width * 0.4f, HeaderHeight),
                zones.Count == 1 ? "1 zone" : $"{zones.Count} zones");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previous;
        }

        // Column widths as fractions of the list width. They sum to 1.
        private const float ColName = 0.22f;
        private const float ColGrowing = 0.23f;
        private const float ColProgress = 0.22f;
        private const float ColTemp = 0.16f;
        private const float ColYield = 0.17f;

        private void DrawColumnHeaders(Rect r)
        {
            // The scrollbar sits inside the list, so the headers stop short of it to stay aligned.
            // Same measure the rows use, so the two cannot drift apart.
            float width = GzpPalette.ContentWidth(r);
            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;

            float x = r.x + Pad;
            Label(ref x, width * ColName, r.y, "Zone");
            Label(ref x, width * ColGrowing, r.y, "Growing");
            Label(ref x, width * ColProgress, r.y, "Progress");
            Label(ref x, width * ColTemp, r.y, "Temperature");
            Label(ref x, width * ColYield, r.y, "Yield");

            GUI.color = previous;
            Widgets.DrawLineHorizontal(r.x, r.yMax - 1f, r.width, GzpPalette.BGL);
        }

        private static void Label(ref float x, float width, float y, string text)
        {
            Widgets.Label(new Rect(x, y, width, ColumnHeaderHeight), text);
            x += width;
        }

        private void DrawEmptyState(Rect r)
        {
            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, "No growing zones on this map.");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previous;
        }

        private void DrawList(Rect outRect)
        {
            float viewHeight = zones.Count * (RowHeight + RowGap);
            Rect view = new Rect(0f, 0f, GzpPalette.ContentWidth(outRect), viewHeight);

            Widgets.BeginScrollView(outRect, ref scroll, view, false);

            float y = 0f;
            foreach (Zone_Growing zone in zones)
            {
                Rect row = new Rect(0f, y, view.width, RowHeight);

                // Skip rows scrolled out of sight. Each one costs a cached-status read and several
                // string builds, which is wasted work for a zone nobody can see.
                if (row.yMax >= scroll.y && row.y <= scroll.y + outRect.height)
                    DrawRow(row, zone);

                y += RowHeight + RowGap;
            }

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(outRect, viewHeight, ref scroll, ref draggingScrollbar,
                ref scrollDragOffset);
        }

        private void DrawRow(Rect r, Zone_Growing zone)
        {
            // Chrome from the shared card control. The stripe is the zone's own color, which is what ties
            // a row to the zone as drawn on the map.
            //
            // The interior stays hand-laid: the cells are proportional columns of the row's width, and a
            // column layout is clearer as arithmetic than as elements with fixed bounds.
            rowCard.Padding = 0f;
            rowCard.AccentColor = zone.color;
            rowCard.BackgroundColor = GzpPalette.PanelBG;
            rowCard.Draw(r);

            GrowZoneStatus status = GrowZoneStatusCache.For(zone);
            float width = r.width;
            float x = r.x + Pad;

            DrawNameCell(new Rect(x, r.y, width * ColName, r.height), zone, status);
            x += width * ColName;

            DrawGrowingCell(new Rect(x, r.y, width * ColGrowing, r.height), zone, status);
            x += width * ColGrowing;

            DrawProgressCell(new Rect(x, r.y, width * ColProgress, r.height), zone, status);
            x += width * ColProgress;

            DrawTemperatureCell(new Rect(x, r.y, width * ColTemp, r.height), status);
            x += width * ColTemp;

            DrawYieldCell(new Rect(x, r.y, width * ColYield, r.height), status);

            if (!Widgets.ButtonInvisible(r))
                return;

            JumpTo(zone);
        }

        /// <summary>
        /// Centers the view on the zone and selects it, the way clicking a pawn in the Animals tab
        /// does. A Zone is not a Thing, so CameraJumper cannot select it -- the jump and the
        /// selection are two separate calls.
        /// </summary>
        private static void JumpTo(Zone_Growing zone)
        {
            Map map = zone.Map;
            if (map == null)
                return;

            SoundDefOf.Click.PlayOneShotOnCamera();
            Find.MainTabsRoot.EscapeCurrentTab(false);
            CameraJumper.TryJump(zone.Position, map);
            Find.Selector.ClearSelection();
            Find.Selector.Select(zone, false, false);
        }

        private static void DrawNameCell(Rect r, Zone_Growing zone, GrowZoneStatus status)
        {
            GzpPalette.CardLabel(new Rect(r.x, r.y + 7f, r.width - 6f, 22f), zone.label,
                GzpPalette.Stat);

            string subtitle = status.Plant == null
                ? $"{zone.CellCount} cells"
                : $"{zone.CellCount} cells · {status.PlantCount} planted";
            SubLabel(new Rect(r.x, r.y + 28f, r.width - 6f, 20f), subtitle);
        }

        private static void DrawGrowingCell(Rect r, Zone_Growing zone, GrowZoneStatus status)
        {
            if (status.Plant == null)
            {
                SubLabel(new Rect(r.x, r.y + 16f, r.width - 6f, 22f), "Nothing");
                return;
            }

            Rect icon = new Rect(r.x, r.y + 7f, IconSize, IconSize);
            Widgets.ThingIcon(icon, status.Plant);

            float textX = icon.xMax + 6f;
            GzpPalette.CardLabel(new Rect(textX, r.y + 7f, r.xMax - textX - 6f, 22f),
                status.Plant.LabelCap, GzpPalette.Stat);

            Bill_Growing bill = ActiveBill(zone);
            string mode = bill == null ? "No active bill" : bill.RepeatModeLabel;
            SubLabel(new Rect(textX, r.y + 28f, r.xMax - textX - 6f, 20f), mode);
        }

        private static void DrawProgressCell(Rect r, Zone_Growing zone, GrowZoneStatus status)
        {
            Bill_Growing bill = ActiveBill(zone);
            Rect bar = new Rect(r.x, r.y + 18f, r.width - 12f, 16f);

            if (bill == null)
            {
                GzpPalette.Bar(bar, 0f, GzpPalette.BGL);
                CenteredBarLabel(bar, "—", GzpPalette.TextDim);
                return;
            }

            // Grow Forever has no target to count towards, so the honest progress figure is how
            // far the crop itself has come. Both are drawn as the same bar, distinguished by the
            // label and color.
            if (bill.repeatMode == BillRepeatModeDefOf.Forever)
            {
                float growth = status.AverageGrowth;
                GzpPalette.Bar(bar, growth, GzpPalette.Accent);
                CenteredBarLabel(bar, $"{growth.ToStringPercent()} grown", GzpPalette.Stat);
                TooltipHandler.TipRegion(bar, (TipSignal)
                    "Average growth of this zone's crop. A Grow Forever bill has no target to "
                    + "count towards, so this shows how far along the planting is.");
                return;
            }

            Zone_GrowingPlus plus = zone as Zone_GrowingPlus;
            if (plus == null)
            {
                GzpPalette.Bar(bar, 0f, GzpPalette.BGL);
                CenteredBarLabel(bar, "—", GzpPalette.TextDim);
                return;
            }

            int current = bill.CurrentCountCached(plus);
            int target = Mathf.Max(1, bill.targetCount);
            float fill = Mathf.Clamp01((float) current / target);
            bool met = current >= bill.targetCount;

            GzpPalette.Bar(bar, fill, met ? GzpPalette.Good : GzpPalette.Accent);
            CenteredBarLabel(bar, $"{current} / {bill.targetCount}",
                met ? GzpPalette.Good : GzpPalette.Stat);
            TooltipHandler.TipRegion(bar, (TipSignal)
                $"{bill.RepeatModeLabel}: {current} of {bill.targetCount}."
                + (met ? "\nTarget met -- this bill is paused." : string.Empty));
        }

        private static void DrawTemperatureCell(Rect r, GrowZoneStatus status)
        {
            if (!status.HasTemperature || status.Plant == null)
            {
                SubLabel(new Rect(r.x, r.y + 16f, r.width - 6f, 22f), "—");
                return;
            }

            PlantProperties props = status.Plant.plant;
            float temp = status.Temperature;

            Texture icon;
            Color color;
            string tip;

            if (temp < props.minGrowthTemperature)
            {
                icon = GzpTex.MinTemp;
                color = GzpPalette.Cold;
                tip = $"Too cold. {status.Plant.LabelCap} stops growing below "
                      + $"{props.minGrowthTemperature.ToStringTemperature("F0")}.";
            }
            else if (temp > props.maxGrowthTemperature)
            {
                icon = GzpTex.MaxTemp;
                color = GzpPalette.Bad;
                tip = $"Too hot. {status.Plant.LabelCap} stops growing above "
                      + $"{props.maxGrowthTemperature.ToStringTemperature("F0")}.";
            }
            else if (temp >= props.minOptimalGrowthTemperature && temp <= props.maxOptimalGrowthTemperature)
            {
                icon = GzpTex.IdealTemp;
                color = GzpPalette.Good;
                tip = "Ideal growing temperature.";
            }
            else
            {
                // Growing, but off its optimum, so the rate is reduced rather than stopped.
                icon = temp < props.minOptimalGrowthTemperature ? GzpTex.MinTemp : GzpTex.MaxTemp;
                color = GzpPalette.Warn;
                tip = "Growing, but outside the ideal range, so more slowly than it could.";
            }

            Rect iconRect = new Rect(r.x, r.y + 7f, IconSize, IconSize);
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(iconRect, icon);
            GUI.color = previous;

            float textX = iconRect.xMax + 6f;
            GzpPalette.CardLabel(new Rect(textX, r.y + 7f, r.xMax - textX - 6f, 22f),
                temp.ToStringTemperature("F0"), color);

            SubLabel(new Rect(textX, r.y + 28f, r.xMax - textX - 6f, 20f),
                $"{props.minGrowthTemperature.ToStringTemperature("F0")} – "
                + props.maxGrowthTemperature.ToStringTemperature("F0"));

            TooltipHandler.TipRegion(r, (TipSignal) tip);
        }

        private static void DrawYieldCell(Rect r, GrowZoneStatus status)
        {
            ThingDef product = status.Plant?.plant?.harvestedThingDef;
            if (product == null)
            {
                SubLabel(new Rect(r.x, r.y + 16f, r.width - 6f, 22f),
                    status.Plant == null ? "—" : "No harvest");
                return;
            }

            Rect icon = new Rect(r.x, r.y + 7f, IconSize, IconSize);
            Widgets.ThingIcon(icon, product);

            float textX = icon.xMax + 6f;
            bool ready = status.YieldNow > 0;
            GzpPalette.CardLabel(new Rect(textX, r.y + 7f, r.xMax - textX - 6f, 22f),
                $"{status.YieldNow} {product.label}",
                ready ? GzpPalette.Good : GzpPalette.TextDim);

            SubLabel(new Rect(textX, r.y + 28f, r.xMax - textX - 6f, 20f),
                $"{status.YieldAtMaturity} at maturity");

            TooltipHandler.TipRegion(r, (TipSignal)
                $"Harvesting now yields about {status.YieldNow} {product.label} from "
                + $"{status.HarvestablePlants} ready {(status.HarvestablePlants == 1 ? "plant" : "plants")}.\n"
                + $"Once the {status.PlantCount} planted have fully grown, about "
                + $"{status.YieldAtMaturity}.\n\nBoth figures include the difficulty's crop yield "
                + "factor but not the harvesting colonist's skill.");
        }

        private static void SubLabel(Rect r, string text)
        {
            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;
            Text.Font = GameFont.Tiny;
            Widgets.Label(r, text);
            Text.Font = GameFont.Small;
            GUI.color = previous;
        }

        private static void CenteredBarLabel(Rect bar, string text, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, text);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = previous;
        }

        /// <summary>
        /// The bill driving the zone, or null. Reads the flag cached by ZoneTick rather than
        /// calling FirstActiveBill, which re-evaluates ShouldDoNow and with it a map-wide resource
        /// count -- far too expensive for something drawn every frame for every zone.
        /// </summary>
        private static Bill_Growing ActiveBill(Zone_Growing zone)
        {
            return (zone as Zone_GrowingPlus)?.CurrentBill;
        }
    }
}
