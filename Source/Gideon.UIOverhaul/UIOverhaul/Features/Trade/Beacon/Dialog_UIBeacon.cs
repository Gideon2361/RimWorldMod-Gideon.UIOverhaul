using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using Gideon.UIOverhaul.Features.Options;
using Gideon.UIOverhaul.Features.Trade.Shell;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Trade.Beacon
{
    /// <summary>
    /// What a trade beacon reaches, what that reach is worth, and where it is about to stop working.
    ///
    /// <b>The one screen of the four that replaces nothing.</b> RimWorld draws a beacon's radius once, as a
    /// placement ghost, and never again -- so this is a window that did not exist rather than a substitute for one
    /// that did, and switching it off costs the readout and nothing else.
    ///
    /// <b>It is also the screen with code already behind it.</b> The radius slider ships today, defaulting to
    /// RimWorld's own 7.9 and adjustable to three times that, with the region cap scaled to match. What was
    /// missing was any way to see what the number bought.
    ///
    /// <b>The region meter is the part worth building this for.</b> The cell walk stops after a fixed number of
    /// regions, so past that point the ring is drawn at the size the player asked for and sells nothing extra --
    /// a beacon that has quietly started lying about its own reach. A meter reading 21 of 24 is the only form of
    /// that fact anybody can act on, and there is nowhere else in the game it appears.
    /// </summary>
    internal class Dialog_UIBeacon : Window
    {
        /// <summary>
        /// How often the scan is redone while the window is open, in real seconds.
        ///
        /// <b>Real time, not ticks,</b> because the readout has to keep up while the game is paused -- somebody
        /// reading this is very often moving stockpiles around with time stopped, which is exactly when the
        /// numbers change and exactly when a tick-driven refresh never fires. The mineable overlay is refreshed
        /// on the same footing for the same reason.
        /// </summary>
        private const float RescanSeconds = 0.6f;

        private readonly List<Building_OrbitalTradeBeacon> beacons = new List<Building_OrbitalTradeBeacon>();
        private readonly List<TradeRailEntry> rail = new List<TradeRailEntry>();
        private readonly List<Thing> rows = new List<Thing>();

        private BeaconScan scan = new BeaconScan();
        private float scanned = -999f;

        private Building_OrbitalTradeBeacon selected;

        private string showing = ShowSellable;

        private const string ShowSellable = "sellable";
        private const string ShowBlocked = "blocked";

        private Vector2 railScroll;
        private bool railDragging;
        private float railDragOffset;

        private Vector2 tableScroll;
        private bool tableDragging;
        private float tableDragOffset;

        private Vector2 spineScroll;
        private bool spineDragging;
        private float spineDragOffset;

        internal Dialog_UIBeacon(Building_OrbitalTradeBeacon beacon)
        {
            selected = beacon;

            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            closeOnCancel = true;
            draggable = true;
            doCloseX = true;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Min(1080f, UI.screenWidth - 20f), Mathf.Min(700f, UI.screenHeight - 20f));

        /// <summary>
        /// Not paused, unlike the trade window.
        ///
        /// Reading a beacon is something a player does while rearranging a stockpile, and pausing the colony to
        /// look at a readout would be the window deciding how the game is played. It is also why the scan is on a
        /// real-time timer: the numbers must keep up with hauling happening behind it.
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            TradeShell.Guarded("Trade.BeaconWindow", inRect, () => Contents(inRect),
                "The beacon readout failed to draw. Nothing about the beacon itself is affected; it goes on "
                + "selling exactly what it did.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Map map = selected != null ? selected.Map : Find.CurrentMap;

            BeaconReach.AllOn(map, beacons);

            if (selected == null || !selected.Spawned)
                selected = beacons.Count > 0 ? beacons[0] : null;

            if (selected == null)
            {
                Close();

                return;
            }

            Rescan();

            Rect headerRect;
            Rect railRect;
            Rect tableRect;
            Rect spineRect;
            Rect footerRect;

            TradeShell.Layout(inRect, true, true, out headerRect, out railRect, out tableRect, out spineRect,
                out footerRect);

            TradeShell.Header(headerRect, "Beacon reach", Detail(), palette);

            Rail(railRect, palette);
            Table(tableRect, palette);
            Spine(spineRect, palette);
            Footer(footerRect, palette);
        }

        private void Rescan()
        {
            if (Time.realtimeSinceStartup - scanned < RescanSeconds)
                return;

            scanned = Time.realtimeSinceStartup;

            scan = BeaconReach.Scan(selected, scan);
        }

        private string Detail()
        {
            string power = scan.Powered ? "powered" : "no power";

            string radius = "radius " + scan.Radius.ToString("0.#");

            if (!Mathf.Approximately(scan.Radius, TradeBeaconRadius.Default))
                radius += ", RimWorld's " + TradeBeaconRadius.Default.ToString("0.#");

            return power + " · " + radius + " · beacon " + (beacons.IndexOf(selected) + 1) + " of "
                   + beacons.Count;
        }

        // ---------------------------------------------------------------------------------------

        private void Rail(Rect rect, UIColorPaletteDef palette)
        {
            rail.Clear();

            rail.Add(TradeRailEntry.Group("Show"));
            rail.Add(TradeRailEntry.Of(ShowSellable, "Sellable", scan.Sellable.Count));

            // Flagged in the danger tone rather than left as a count, because everything on that list is
            // something the player believes is for sale and is not.
            TradeRailEntry blocked = TradeRailEntry.Of(ShowBlocked, "Walled off", scan.WalledOff.Count);

            if (scan.WalledOff.Count > 0)
                blocked.CountColor = palette.Danger;

            rail.Add(blocked);

            rail.Add(TradeRailEntry.Group("Beacons"));

            for (int i = 0; i < beacons.Count; i++)
            {
                Building_OrbitalTradeBeacon beacon = beacons[i];

                // Named by the room it stands in, which is how a player thinks about them -- "the one in the
                // north store" -- rather than by a position nobody reads off the map.
                rail.Add(TradeRailEntry.Of("beacon:" + i, RoomName(beacon), -1));
            }

            string picked = TradeRail.Draw(rect, rail, Key(), ref railScroll, ref railDragging, ref railDragOffset,
                palette);

            if (picked == null)
                return;

            if (picked.StartsWith("beacon:"))
            {
                int index;

                if (int.TryParse(picked.Substring(7), out index) && index >= 0 && index < beacons.Count)
                {
                    selected = beacons[index];

                    // Forced rather than waited for: the player has just changed what they are looking at, and a
                    // window showing the previous beacon's numbers for half a second is a window that has lied.
                    scanned = -999f;

                    CameraJumper.TryJumpAndSelect(selected);
                }

                return;
            }

            showing = picked;
            tableScroll = Vector2.zero;
        }

        private string Key()
        {
            return showing;
        }

        private static string RoomName(Building_OrbitalTradeBeacon beacon)
        {
            return UIGuard.Try("Trade.BeaconRoom", () =>
            {
                Room room = beacon.GetRoom();

                if (room == null || room.PsychologicallyOutdoors)
                    return "Outdoors".Translate().ToString();

                return room.GetRoomRoleLabel().CapitalizeFirst();
            }, "Beacon", null);
        }

        // ---------------------------------------------------------------------------------------

        private void Table(Rect rect, UIColorPaletteDef palette)
        {
            bool blocked = showing == ShowBlocked;

            rows.Clear();
            rows.AddRange(blocked ? scan.WalledOff : scan.Sellable);

            rows.Sort((left, right) => Value(right).CompareTo(Value(left)));

            float width = GzpPalette.ContentWidth(rect);

            Rect captions = new Rect(rect.x, rect.y, width, TradeShell.ColumnsHeight);

            TradeShell.Column(new Rect(captions.x + 34f, captions.y, width - 200f, captions.height), "Stack",
                palette);

            TradeShell.Column(new Rect(captions.xMax - 160f, captions.y, 70f, captions.height), "Count", palette,
                TextAnchor.MiddleRight);

            TradeShell.Column(new Rect(captions.xMax - 84f, captions.y, 84f, captions.height), "Worth", palette,
                TextAnchor.MiddleRight);

            GUI.color = palette.Border;
            Widgets.DrawLineHorizontal(rect.x, captions.yMax - 1f, width);
            GUI.color = Color.white;

            float top = captions.yMax;

            Rect list = new Rect(rect.x, top, rect.width, Mathf.Max(0f, rect.yMax - top));
            Rect view = new Rect(0f, 0f, width, rows.Count * 28f + 2f);

            Widgets.BeginScrollView(list, ref tableScroll, view, false);

            float first = tableScroll.y - 28f;
            float last = tableScroll.y + list.height;

            for (int i = 0; i < rows.Count; i++)
            {
                float y = i * 28f;

                if (y < first || y > last)
                    continue;

                Row(new Rect(0f, y, view.width, 28f), rows[i], i, blocked, palette);
            }

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(list, view.height, ref tableScroll, ref tableDragging, ref tableDragOffset);

            if (rows.Count == 0)
            {
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                Widgets.Label(list, blocked
                    ? "Nothing inside the ring is walled off from this beacon."
                    : "Nothing on this beacon can be sold.");

                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static float Value(Thing thing)
        {
            return UIGuard.Try("Trade.BeaconRowValue", () => thing.MarketValue * thing.stackCount, 0f, null);
        }

        private void Row(Rect rect, Thing thing, int index, bool blocked, UIColorPaletteDef palette)
        {
            TradeShell.RowBackground(rect, index, false, palette);

            UIGuard.Try("Trade.BeaconRowIcon",
                () => Widgets.ThingIcon(new Rect(rect.x + 3f, rect.y + 3f, 22f, 22f), thing), null);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = blocked ? palette.Danger : palette.TextPrimary;

                Widgets.LabelEllipses(
                    new Rect(rect.x + 30f, rect.y, Mathf.Max(40f, rect.width - 200f), rect.height),
                    thing.LabelNoCount);

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(rect.xMax - 160f, rect.y, 70f, rect.height),
                    thing.stackCount.ToStringCached());

                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(rect.xMax - 84f, rect.y, 84f, rect.height), Value(thing).ToStringMoney());
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!Mouse.IsOver(rect) || !Widgets.ButtonInvisible(rect))
                return;

            CameraJumper.TryJumpAndSelect(thing);
        }

        // ---------------------------------------------------------------------------------------

        private void Spine(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Rect inner = rect.ContractedBy(10f);

            float width = GzpPalette.ContentWidth(inner);

            Rect view = new Rect(0f, 0f, width, 460f);

            Widgets.BeginScrollView(inner, ref spineScroll, view, false);

            float y = 0f;

            y = TradeShell.Heading(view, y, "REACH", palette);

            if (!scan.Powered)
            {
                y = TradeShell.Readout(view, y, "Power", "off", palette, palette.Danger);

                y = TradeShell.Note(view, y,
                    "An unpowered beacon sells nothing. Everything below is what it would reach with power.",
                    palette);
            }

            y = TradeShell.Readout(view, y, "Cells covered", scan.Cells.Count.ToStringCached(), palette);

            y = TradeShell.Readout(view, y, "Sellable stacks", scan.Sellable.Count.ToStringCached(), palette);

            y = TradeShell.Readout(view, y, "Market value", scan.Value.ToStringMoney(), palette,
                palette.TextPrimary);

            y = TradeShell.Readout(view, y, "Walled off", scan.WalledOff.Count.ToStringCached(), palette,
                scan.WalledOff.Count > 0 ? palette.Danger : palette.TextDisabled);

            if (scan.WalledOff.Count > 0)
            {
                y = TradeShell.Readout(view, y, "Value out of reach", scan.WalledOffValue.ToStringMoney(),
                    palette, palette.Danger);

                y = TradeShell.Note(view, y,
                    "Inside the ring but behind a wall or a door. A beacon cannot sell through either, which is "
                    + "RimWorld's rule and not ours.", palette);
            }

            y += 6f;

            y = TradeShell.Heading(view, y, "REGION BUDGET", palette);

            y = Budget(view, y, palette);

            y += 6f;

            y = TradeShell.Heading(view, y, "BY CATEGORY", palette);

            y = Categories(view, y, palette);

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(inner, view.height, ref spineScroll, ref spineDragging, ref spineDragOffset);
        }

        /// <summary>
        /// How much of the cell walk's budget this beacon has spent.
        ///
        /// <b>The failure it describes is silent, which is the whole reason to draw it.</b> The walk crosses at
        /// most a fixed number of regions; past that it simply stops, and the ring goes on being drawn at the
        /// radius the player set. So a beacon at the cap covers less than it appears to and the only symptom is
        /// stock that will not sell.
        /// </summary>
        private float Budget(Rect view, float y, UIColorPaletteDef palette)
        {
            bool full = scan.Truncated;

            y = TradeShell.Readout(view, y, "Regions walked",
                scan.Regions + " / " + scan.RegionCap, palette, full ? palette.Danger : palette.TextPrimary);

            float fraction = scan.RegionCap <= 0 ? 0f : Mathf.Clamp01((float) scan.Regions / scan.RegionCap);

            GzpPalette.Bar(new Rect(view.x, y, view.width, 6f), fraction,
                full ? palette.Danger : fraction > 0.8f ? palette.Warning : palette.Accent);

            y += 12f;

            if (full)
            {
                return TradeShell.Note(view, y,
                    "At the limit. Past this the ring is drawn wider and sells nothing extra, so raising the "
                    + "radius further will make this beacon lie to you. A second beacon reaches further than a "
                    + "wider one.", palette);
            }

            return TradeShell.Note(view, y,
                "The walk crosses this many rooms before it stops. Small rooms spend it faster than a warehouse "
                + "does.", palette);
        }

        private float Categories(Rect view, float y, UIColorPaletteDef palette)
        {
            if (scan.ByCategory.Count == 0)
                return TradeShell.Readout(view, y, string.Empty, "nothing yet", palette, palette.TextDisabled);

            foreach (KeyValuePair<string, float> entry in scan.ByCategory)
                y = TradeShell.Readout(view, y, TradeCatalog.NameOf(entry.Key), entry.Value.ToStringMoney(),
                    palette, palette.TextSecondary, GameFont.Tiny);

            return y;
        }

        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The radius slider, on the screen where its effect is visible.
        ///
        /// <b>The same setting as the options page, not a second one.</b> It writes to
        /// <c>UIOverhaulSettingsFile</c> exactly as that page does, and both read it back through
        /// <see cref="TradeBeaconRadius.Radius"/>. Put here because this is the one place a player can see what
        /// moving it buys -- the covered cells, the sellable value and the region meter all move as it is
        /// dragged.
        /// </summary>
        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            if (settings == null)
                return;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(rect.x, rect.y, 130f, rect.height),
                    "Radius " + TradeBeaconRadius.Radius.ToString("0.#"));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            float width = Mathf.Max(80f, rect.width - 130f - 330f);

            float chosen = Widgets.HorizontalSlider(
                new Rect(rect.x + 130f, rect.y + (rect.height - 22f) * 0.5f, width, 22f),
                TradeBeaconRadius.Radius, TradeBeaconRadius.Minimum, TradeBeaconRadius.Maximum, false, null, null,
                null, 0.1f);

            if (!Mathf.Approximately(chosen, settings.tradeBeaconRadius))
            {
                settings.tradeBeaconRadius = chosen;

                // Rescanned at once. The whole point of putting the slider here is that the numbers move with it,
                // and waiting out the timer would make it feel disconnected from them.
                scanned = -999f;
            }

            float x = rect.xMax - 320f;

            if (UIActionButtonControl.Draw(new Rect(x, rect.y + 6f, 150f, 32f), "Match RimWorld's", palette,
                    false, !Mathf.Approximately(settings.tradeBeaconRadius, TradeBeaconRadius.Default)))
            {
                settings.tradeBeaconRadius = TradeBeaconRadius.Default;

                scanned = -999f;

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            if (UIActionButtonControl.Draw(new Rect(x + 158f, rect.y + 6f, 150f, 32f), "Close", palette))
                Close();
        }

        public override void PostClose()
        {
            base.PostClose();

            // Written on the way out rather than on every drag, which would rewrite the settings file sixty times
            // a second while somebody moves the slider.
            UIGuard.Try("Trade.BeaconSaveRadius", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings != null)
                    settings.Save();
            }, "The beacon radius was not written to the settings file, so it will be back to its previous value "
               + "next time the game starts.");
        }
    }
}
