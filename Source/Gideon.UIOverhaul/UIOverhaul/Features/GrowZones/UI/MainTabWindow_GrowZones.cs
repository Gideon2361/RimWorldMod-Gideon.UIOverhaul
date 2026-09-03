using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
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
        /// <summary>
        /// Height of the header band.
        ///
        /// <b>Sixty-six, which is what every other restyled tab uses,</b> and it is a fix rather than a
        /// preference: at 42 the band was shorter than the two lines and three readouts laid out inside it.
        /// The inner rect is the band less ten a side, so 42 left 22 for a 24 pixel title, a subtitle
        /// starting below that, and a 28 pixel mark centred in less than its own height. The subtitle and the
        /// readout captions both spilled out of the bottom border and onto the rail heading under it.
        /// </summary>
        private const float HeaderHeight = 66f;
        private const float RailWidth = 190f;
        private const float MarkSize = 28f;
        private const float BlockGap = 10f;
        private const float Pad = 12f;
        private const float IconSize = 24f;

        private Vector2 railScroll;
        private bool railDragging;
        private float railDragOffset;

        /// <summary>Which zone the pane is showing. The rail keeps it; nothing else needs it.</summary>
        private int selected;


        private readonly List<Zone_Growing> zones = new List<Zone_Growing>();

        public override Vector2 RequestedTabSize => new Vector2(1020f, 580f);

        protected override float Margin => 0f;

        /// <summary>
        /// Guarded because window lifecycle methods are not: RimWorld wraps DoWindowContents and nothing else, so
        /// an exception here would escape into WindowStack.Add partway through opening the window -- leaving a
        /// window on the stack that never finished being set up.
        /// </summary>
        public override void PostOpen()
        {
            base.PostOpen();

            UIGuard.Try("GrowZones.TabOpen", () =>
            {
                // Fresh figures the moment the tab appears, and it releases zones deleted since the
                // last time it was open.
                GrowZoneStatusCache.Clear();
                railScroll = Vector2.zero;
                selected = 0;
            }, "The growing zones tab may show figures cached from the last time it was open.");
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("GrowZones.Tab", inRect, () => DrawContents(inRect),
                "The growing zones tab shows a failure notice. Zones and their bills are unaffected -- they can "
                + "still be reached through each zone's inspect pane.");
        }

        private void DrawContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, GzpPalette.BG);

            GatherZones();

            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            Rect body = inRect.ContractedBy(Pad);

            if (zones.Count == 0)
            {
                DrawEmptyState(body);

                return;
            }

            if (selected >= zones.Count)
                selected = zones.Count - 1;

            Rect head = new Rect(body.x, body.y, body.width, HeaderHeight);

            DrawHeader(head, palette);

            Rect rest = new Rect(body.x, head.yMax + 10f, body.width, body.yMax - head.yMax - 10f);
            Rect rail = new Rect(rest.x, rest.y, RailWidth, rest.height);

            DrawRail(rail, palette);
            DrawBlocks(new Rect(rail.xMax + 10f, rest.y, rest.xMax - rail.xMax - 10f, rest.height),
                palette);
        }

        /// <summary>
        /// Rebuilt each frame rather than cached: zones are created, deleted and renamed freely while the
        /// tab is open, and the list is at most a few dozen entries.
        /// </summary>
        private void GatherZones()
        {
            zones.Clear();

            Map map = Find.CurrentMap;

            if (map == null)
                return;

            foreach (Zone zone in map.zoneManager.AllZones)
            {
                // Zone_Growing rather than Zone_GrowingPlus: an unconverted vanilla zone still belongs in
                // this list, and it reads perfectly well without a bill stack.
                if (zone is Zone_Growing growing)
                    zones.Add(growing);
            }
        }

        /// <summary>
        /// This tab's own color, the fifth of the per-tab identities.
        ///
        /// <b>Wheat rather than green.</b> The obvious hue is spoken for twice: success green is what a
        /// healthy temperature reads as inside this very tab, and the animals tab next door already took the
        /// muted sage. Wheat says what the zone is for instead of what it is made of.
        /// </summary>
        private static Color Identity(UIColorPaletteDef palette)
        {
            return palette.TabGrowing;
        }

        /// <summary>
        /// The mark, the tab's name, which zone is being read, and the three figures worth having
        /// without opening anything.
        /// </summary>
        private void DrawHeader(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Color hue = Identity(palette);
            Rect inner = rect.ContractedBy(10f);
            float text = inner.x;

            if (GzpTex.Mark != null)
            {
                Rect mark = new Rect(inner.x, inner.y + (inner.height - MarkSize) * 0.5f, MarkSize,
                    MarkSize);

                Color previous = GUI.color;

                GUI.color = hue;

                GUI.DrawTexture(mark, GzpTex.Mark, ScaleMode.ScaleToFit);

                GUI.color = previous;

                text = mark.xMax + 10f;
            }

            Zone_Growing zone = zones[selected];

            TabParts.RowLabel(new Rect(text, inner.y + 1f, 340f, 24f), "Growing Zones", hue,
                GameFont.Medium, GrowFaces.Display, GrowFaces.Size.Title);

            TabParts.RowLabel(new Rect(text, inner.y + 25f, 340f, 18f),
                zone.label + "  -  " + zone.Cells.Count + " cells", palette.TextSecondary,
                GameFont.Tiny, GrowFaces.Condensed, GrowFaces.Size.Subtitle);

            int planted = 0;
            int wanting = 0;

            for (int i = 0; i < zones.Count; i++)
            {
                GrowZoneStatus each = GrowZoneStatusCache.For(zones[i]);

                planted += each.PlantCount;

                if (each.PlantCount < zones[i].Cells.Count)
                    wanting++;
            }

            float right = inner.xMax;

            right = Readout(inner, right, wanting.ToString(), "unplanted",
                wanting > 0 ? palette.Warning : palette.TextSecondary, palette);

            right = Readout(inner, right, planted.ToString("N0"), "planted", palette.TextPrimary, palette);

            Readout(inner, right, zones.Count.ToString(), "zones", palette.TextPrimary, palette);
        }

        /// <summary>
        /// One figure and its caption, laid out from the right edge inward.
        ///
        /// Right aligned by setting the anchor around the call rather than through it: TabParts.RowLabel
        /// takes a face and a size but not an alignment, and every other caller wants the left edge.
        /// </summary>
        private static float Readout(Rect inner, float right, string value, string caption, Color tint,
            UIColorPaletteDef palette)
        {
            float width = Mathf.Max(UITextControl.Width(value, GrowFaces.Mono, GrowFaces.Size.Readout),
                UITextControl.Width(caption.ToUpperInvariant(), GrowFaces.Mono, GrowFaces.Size.Caption))
                          + 4f;

            Rect box = new Rect(right - width, inner.y, width, inner.height);

            RightLabel(new Rect(box.x, box.y + 2f, box.width, 20f), value, tint, GrowFaces.Size.Readout);

            RightLabel(new Rect(box.x, box.y + 24f, box.width, 14f), caption.ToUpperInvariant(),
                palette.TextDisabled, GrowFaces.Size.Caption);

            return box.x - 22f;
        }

        /// <summary>
        /// A right aligned label in the mono face.
        ///
        /// <b>Not <c>TabParts.RowLabel</c>, which forces <c>MiddleLeft</c> itself.</b> Setting the anchor
        /// around that call does nothing, because it overwrites it on the way in and puts the caller's value
        /// back on the way out. Three labels here were written that way and all three drew left: a block's
        /// trailing note landed on top of the caption it was meant to sit opposite, which is what made the
        /// crop block read GROW over Forever and the growth block 22% over GROWTH.
        /// </summary>
        private static void RightLabel(Rect rect, string text, Color color, float points)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                Text.WordWrap = false;
                GUI.color = color;

                UITextControl.LabelEllipses(rect, text, GrowFaces.Mono, points);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        /// <summary>
        /// Every zone, with how far along it is.
        ///
        /// <b>The rail carries the answer, not just the name.</b> A zone's row shows its crop and its
        /// growth, so the list is the at-a-glance version of the table this replaced, and the pane never
        /// repeats what the rail already said.
        /// </summary>
        private void DrawRail(Rect rect, UIColorPaletteDef palette)
        {
            Color hue = Identity(palette);
            List<UIRailElement> elements = new List<UIRailElement>();

            elements.Add(new UIRailSectionHeaderControl("Zones")
            {
                Uppercase = true,
                Face = GrowFaces.Mono,
                Points = GrowFaces.Size.Caption,
                Color = palette.TextDisabled
            });

            for (int i = 0; i < zones.Count; i++)
            {
                Zone_Growing zone = zones[i];
                GrowZoneStatus status = GrowZoneStatusCache.For(zone);
                bool bare = status.Plant == null;

                elements.Add(new UIRailClickableEntry(i.ToString(),
                    bare ? zone.label : status.Plant.LabelCap)
                {
                    Rise = 30f,
                    Face = GrowFaces.Condensed,
                    Points = GrowFaces.Size.RailName,
                    TextColor = i == selected ? hue : (Color?) null,
                    Trailing = bare ? "--" : status.AverageGrowth.ToStringPercent(),
                    CountFace = GrowFaces.Mono,
                    CountPoints = GrowFaces.Size.RailCount,
                    Progress = bare ? 0f : Mathf.Clamp01(status.AverageGrowth),
                    ProgressColor = hue,
                    Tooltip = zone.label + " -- " + zone.Cells.Count + " cells"
                });
            }

            string picked = UIRailControl.Draw(rect, elements, selected.ToString(), ref railScroll,
                ref railDragging, ref railDragOffset, palette);

            int index;

            if (picked == null || !int.TryParse(picked, out index))
                return;

            // Clicking the row already open jumps the camera to it, which is the only reason this tab
            // ever needed a row to answer a second click.
            if (index == selected)
                JumpTo(zones[index]);

            selected = index;
        }

        // Column widths as fractions of the list width. They sum to 1.

        private void DrawEmptyState(Rect r)
        {
            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, "No growing zones on this map.");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previous;
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

        /// <summary>
        /// The chosen zone, in four small panels rather than five table columns.
        ///
        /// <b>The table gave a zone name and a temperature the same width.</b> Blocks size themselves to
        /// what they hold, so the crop gets room for its name and the temperature gets room for three
        /// digits, which is all it ever needed.
        /// </summary>
        private void DrawBlocks(Rect rect, UIColorPaletteDef palette)
        {
            Zone_Growing zone = zones[selected];
            GrowZoneStatus status = GrowZoneStatusCache.For(zone);

            float half = (rect.width - BlockGap) * 0.5f;
            float tall = 92f;

            Crop(new Rect(rect.x, rect.y, half, tall), zone, status, palette);
            Growth(new Rect(rect.x + half + BlockGap, rect.y, half, tall), zone, status, palette);

            float second = rect.y + tall + BlockGap;

            Yield(new Rect(rect.x, second, half, tall), status, palette);
            Conditions(new Rect(rect.x + half + BlockGap, second, half, tall), status, palette);
        }

        /// <summary>A block's chrome: a sunken caption strip over a raised body.</summary>
        private static Rect Block(Rect rect, string caption, string trailing, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect strip = new Rect(rect.x, rect.y, rect.width, 22f);

            Widgets.DrawBoxSolid(new Rect(strip.x + 1f, strip.yMax, strip.width - 2f, 1f), palette.Border);

            TabParts.RowLabel(new Rect(strip.x + 10f, strip.y, strip.width - 20f, strip.height),
                caption.ToUpperInvariant(), palette.TextDisabled, GameFont.Tiny, GrowFaces.Mono,
                GrowFaces.Size.Caption);

            if (!trailing.NullOrEmpty())
            {
                RightLabel(new Rect(strip.x + 10f, strip.y, strip.width - 20f, strip.height), trailing,
                    palette.TextDisabled, GrowFaces.Size.Small);
            }

            return new Rect(rect.x + 10f, strip.yMax + 8f, rect.width - 20f, rect.yMax - strip.yMax - 16f);
        }

        /// <summary>What is planted, and under which bill.</summary>
        private static void Crop(Rect rect, Zone_Growing zone, GrowZoneStatus status,
            UIColorPaletteDef palette)
        {
            Bill_Growing bill = ActiveBill(zone);

            Rect body = Block(rect, "Crop", bill == null ? null : bill.RepeatModeLabel, palette);

            if (status.Plant == null)
            {
                TabParts.RowLabel(body, "Nothing planted", palette.TextDisabled, GameFont.Small,
                    GrowFaces.Condensed, GrowFaces.Size.Body);

                return;
            }

            Rect icon = new Rect(body.x, body.y + (body.height - IconSize) * 0.5f, IconSize, IconSize);

            Widgets.ThingIcon(icon, status.Plant);

            float text = icon.xMax + 8f;

            TabParts.RowLabel(new Rect(text, body.y + 2f, body.xMax - text, 20f), status.Plant.LabelCap,
                palette.TextPrimary, GameFont.Small, GrowFaces.Condensed, GrowFaces.Size.Body);

            TabParts.RowLabel(new Rect(text, body.y + 22f, body.xMax - text, 16f),
                status.PlantCount + " of " + zone.Cells.Count + " cells planted", palette.TextSecondary,
                GameFont.Tiny, GrowFaces.Condensed, GrowFaces.Size.Small);
        }

        /// <summary>
        /// How far along, as a figure and a bar.
        ///
        /// <b>A Grow Forever bill has no target to count towards,</b> so the honest figure there is how
        /// far the planting has grown; a bill with a target counts towards it instead. The table said the
        /// same thing, and this keeps saying it.
        /// </summary>
        private static void Growth(Rect rect, Zone_Growing zone, GrowZoneStatus status,
            UIColorPaletteDef palette)
        {
            Bill_Growing bill = ActiveBill(zone);
            bool counting = bill != null && bill.repeatMode != BillRepeatModeDefOf.Forever
                                         && bill.targetCount > 0;

            float share = counting
                ? Mathf.Clamp01(status.HarvestablePlants / (float) bill.targetCount)
                : Mathf.Clamp01(status.AverageGrowth);

            Rect body = Block(rect, "Growth",
                counting ? null : status.AverageGrowth.ToStringPercent(), palette);

            string figure = counting
                ? status.HarvestablePlants + " / " + bill.targetCount
                : status.AverageGrowth.ToStringPercent();

            TabParts.RowLabel(new Rect(body.x, body.y, body.width, 20f), figure, palette.TextPrimary,
                GameFont.Small, GrowFaces.Mono, GrowFaces.Size.Figure);

            Rect bar = new Rect(body.x, body.y + 26f, body.width, 10f);

            UIElementPainter.OutlineRounded(bar, palette.Border, palette.SurfaceSunken);

            if (share > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(bar.x + 1f, bar.y + 1f,
                    Mathf.Max(1f, (bar.width - 2f) * share), bar.height - 2f), Identity(palette));
            }
        }

        /// <summary>What the zone is worth now, and what it will be worth left alone.</summary>
        private static void Yield(Rect rect, GrowZoneStatus status, UIColorPaletteDef palette)
        {
            Rect body = Block(rect, "Yield", null, palette);

            if (status.Plant == null)
            {
                TabParts.RowLabel(body, "--", palette.TextDisabled, GameFont.Small, GrowFaces.Mono,
                    GrowFaces.Size.Figure);

                return;
            }

            TabParts.RowLabel(new Rect(body.x, body.y, body.width, 20f),
                status.YieldNow.ToString("N0"),
                status.YieldNow > 0 ? Identity(palette) : palette.TextDisabled, GameFont.Small,
                GrowFaces.Mono, GrowFaces.Size.Figure);

            TabParts.RowLabel(new Rect(body.x, body.y + 24f, body.width, 16f),
                "now  -  " + status.YieldAtMaturity.ToString("N0") + " at maturity",
                palette.TextSecondary, GameFont.Tiny, GrowFaces.Condensed, GrowFaces.Size.Small);
        }

        /// <summary>
        /// Temperature against the range this crop will actually grow in.
        ///
        /// The reading is coloured by whether it is growing, which is the question -- a number on its own
        /// makes the reader remember two thresholds per crop.
        /// </summary>
        private static void Conditions(Rect rect, GrowZoneStatus status, UIColorPaletteDef palette)
        {
            Rect body = Block(rect, "Conditions", null, palette);

            if (!status.HasTemperature || status.Plant == null)
            {
                TabParts.RowLabel(body, "--", palette.TextDisabled, GameFont.Small, GrowFaces.Mono,
                    GrowFaces.Size.Figure);

                return;
            }

            PlantProperties props = status.Plant.plant;
            float temp = status.Temperature;

            bool cold = temp < props.minGrowthTemperature;
            bool hot = temp > props.maxGrowthTemperature;

            Color tint = cold || hot ? palette.Danger : palette.Success;
            string state = cold ? "too cold to grow" : hot ? "too hot to grow" : "growing";

            TabParts.RowLabel(new Rect(body.x, body.y, body.width, 20f),
                TemperatureText.Of(temp), tint, GameFont.Small, GrowFaces.Mono,
                GrowFaces.Size.Figure);

            TabParts.RowLabel(new Rect(body.x, body.y + 24f, body.width, 16f),
                state + "  -  " + TemperatureText.Of(props.minGrowthTemperature) + " to "
                + TemperatureText.Of(props.maxGrowthTemperature), palette.TextSecondary,
                GameFont.Tiny, GrowFaces.Condensed, GrowFaces.Size.Small);
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
