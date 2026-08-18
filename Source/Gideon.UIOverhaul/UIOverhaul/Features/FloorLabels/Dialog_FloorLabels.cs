using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// Naming and coloring the labels on the floor.
    ///
    /// <b>A window rather than clicking the label, and that was a correction.</b> Clicking the text on the ground
    /// is the obvious gesture and the wrong one: the label sits on open floor, which is exactly where a click
    /// places a building, starts a box selection, or picks something up. Any interception is a hit box fighting
    /// three existing ones, and the failure mode is a blueprint that silently refuses to go down. Qualifying it
    /// -- only when no designator is active, only without a drag -- makes it less predictable rather than more.
    ///
    /// <b>The window also does something clicking never could.</b> A room too small to draw a label has no label
    /// to click, so under that model it could never be named at all. Here it is a row marked too small, nameable
    /// now, and the name appears if the room is ever enlarged.
    ///
    /// <b>Hovering a row outlines that room on the map,</b> which is how anybody tells two bedrooms apart. The
    /// outline itself is drawn by <see cref="FloorLabelDrawer"/>, because world-space geometry submitted from
    /// <c>OnGUI</c> would belong to a frame already drawn.
    /// </summary>
    public class Dialog_FloorLabels : Window
    {
        private const float TitleHeight = 32f;
        private const float RailWidth = 300f;
        private const float Gap = 10f;
        private const float RowHeight = 26f;
        private const float FooterHeight = 44f;
        private const float SwatchSize = 26f;

        private static readonly UITextBoxControl Name = new UITextBoxControl
        {
            Placeholder = "Leave empty for the room's own name",
            MaxLength = 48
        };

        /// <summary>One nameable thing on the map: a room, or a zone.</summary>
        private sealed class Entry
        {
            internal bool IsZone;
            internal int ZoneId;

            /// <summary>The cell a room label is keyed on. See <see cref="FloorLabel.KeyCell"/>.</summary>
            internal IntVec3 KeyCell;

            internal int RoomId;
            internal string DefaultName;
            internal string CustomName;
            internal Color? Color;
            internal bool TooSmall;
            internal string Source;
            internal List<IntVec3> Cells;

            internal string Shown => CustomName.NullOrEmpty() ? DefaultName : CustomName;
        }

        private List<Entry> entries = new List<Entry>();
        private Entry selected;
        private Vector2 scroll;
        private string problem;

        /// <summary>What to select once the list has been built, when opened by clicking a label.</summary>
        private FloorLabelHit? focus;

        /// <summary>
        /// Opens focused on one label, which is what clicking one on the map does.
        ///
        /// The hit is kept rather than resolved here, because the list does not exist until
        /// <see cref="PostOpen"/> has run.
        /// </summary>
        internal Dialog_FloorLabels(FloorLabelHit hit) : this()
        {
            focus = hit;
        }

        public Dialog_FloorLabels()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Min(760f, UI.screenWidth - 80f), Mathf.Min(540f, UI.screenHeight - 80f));

        public override void PostOpen()
        {
            base.PostOpen();

            Refresh();
        }

        public override void PostClose()
        {
            base.PostClose();

            // Or the last row hovered stays outlined on the map for the rest of the session.
            FloorLabelDrawer.Highlight = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("FloorLabels.Window", inRect, () => Contents(inRect),
                "The floor labels window could not finish drawing. Your labels are unchanged.");
        }

        /// <summary>
        /// Rebuilds the list from the map being looked at.
        ///
        /// <b>Rooms are enumerated fresh every time</b> rather than held: they are recomputed from walls whenever
        /// the region grid updates, so a kept reference goes stale as soon as somebody builds a wall while this
        /// window is open.
        /// </summary>
        private void Refresh()
        {
            entries = UIGuard.Try("FloorLabels.List", Build, new List<Entry>(),
                "The rooms on this map could not be listed.");

            // Consumed once. A click chose this label, and rebuilding the list later must not drag the selection
            // back to it after the player has moved on.
            if (focus.HasValue)
            {
                FloorLabelHit hit = focus.Value;

                focus = null;

                Select(Matching(hit));

                return;
            }

            // The selection is matched again by identity rather than kept, for the same reason the rooms are.
            if (selected == null)
                return;

            Entry again = null;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];

                if (selected.IsZone ? entry.IsZone && entry.ZoneId == selected.ZoneId
                        : !entry.IsZone && entry.KeyCell == selected.KeyCell)
                {
                    again = entry;

                    break;
                }
            }

            Select(again);
        }

        /// <summary>
        /// The row a clicked label belongs to.
        ///
        /// <b>Rooms are matched by which room holds the key cell,</b> not by comparing cells: the click recorded
        /// whichever cell the label was keyed on, and the row was built from the room it is in. Comparing cells
        /// directly would fail whenever the two picked different ones.
        /// </summary>
        private Entry Matching(FloorLabelHit hit)
        {
            Map map = Find.CurrentMap;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];

                if (hit.IsZone)
                {
                    if (entry.IsZone && entry.ZoneId == hit.ZoneId)
                        return entry;

                    continue;
                }

                if (entry.IsZone || map == null || !hit.KeyCell.IsValid || !hit.KeyCell.InBounds(map))
                    continue;

                Room room = hit.KeyCell.GetRoom(map);

                if (room != null && room.ID == entry.RoomId)
                    return entry;
            }

            return null;
        }

        private List<Entry> Build()
        {
            List<Entry> found = new List<Entry>();
            Map map = Find.CurrentMap;

            if (map == null)
                return found;

            GameComponent_FloorLabels store = GameComponent_FloorLabels.Current;
            IReadOnlyList<Room> rooms = map.regionGrid == null ? null : map.regionGrid.AllRooms;

            if (rooms != null)
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    Room room = rooms[i];

                    // Structural rather than labelable, so a room that is merely too small still gets a row.
                    if (!FloorLabelDrawer.Structural(room))
                        continue;

                    FloorLabel stored = store == null ? null : store.ForRoom(map, room);
                    List<IntVec3> cells = new List<IntVec3>(room.Cells);

                    found.Add(new Entry
                    {
                        RoomId = room.ID,
                        KeyCell = stored != null && stored.KeyCell.IsValid ? stored.KeyCell : Anchor(cells),
                        DefaultName = room.Role == null ? "Room" : room.Role.LabelCap,
                        CustomName = stored == null ? null : stored.Label,
                        Color = stored == null ? null : stored.CustomColor,
                        TooSmall = room.CellCount < FloorLabelDrawer.MinimumRoomCells,
                        Source = room.Role == null ? "room" : room.Role.LabelCap.ToLower(),
                        Cells = cells
                    });
                }
            }

            if (map.zoneManager != null)
            {
                List<Zone> zones = map.zoneManager.AllZones;

                for (int i = 0; i < zones.Count; i++)
                {
                    Zone zone = zones[i];

                    // A null is a real possibility: a zone whose class went missing is dropped during load and
                    // leaves a hole in this list.
                    if (zone == null || zone.Cells == null || zone.Cells.Count == 0)
                        continue;

                    FloorLabel stored = store == null ? null : store.ForZone(zone.ID);

                    found.Add(new Entry
                    {
                        IsZone = true,
                        ZoneId = zone.ID,
                        DefaultName = zone.label.NullOrEmpty() ? "Zone" : zone.label,
                        CustomName = stored == null ? null : stored.Label,
                        Color = stored == null ? null : stored.CustomColor,
                        Source = zone is Zone_Growing ? "growing" : zone is Zone_Stockpile ? "stockpile" : "zone",
                        Cells = new List<IntVec3>(zone.Cells)
                    });
                }
            }

            return found;
        }

        /// <summary>
        /// The cell a room's label is remembered by.
        ///
        /// <b>The lowest cell rather than any cell,</b> so the same room produces the same key every time this
        /// list is built. An arbitrary pick would mean a label attaching itself to a different cell on each
        /// rebuild, and any future code comparing keys would find them all different.
        /// </summary>
        private static IntVec3 Anchor(List<IntVec3> cells)
        {
            IntVec3 best = IntVec3.Invalid;

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];

                if (!best.IsValid || cell.z < best.z || (cell.z == best.z && cell.x < best.x))
                    best = cell;
            }

            return best;
        }

        private void Select(Entry entry)
        {
            selected = entry;
            problem = null;
            Name.Text = entry == null ? string.Empty : entry.CustomName ?? string.Empty;
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            // Cleared each frame and set again by whichever row is hovered, so moving off every row stops the
            // outline rather than leaving the last one lit.
            FloorLabelDrawer.Highlight = null;

            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, TitleHeight), "Floor labels");

                Text.Font = GameFont.Small;

                float top = inRect.y + TitleHeight + 6f;
                float height = Mathf.Max(0f, inRect.yMax - top - FooterHeight);

                DrawList(new Rect(inRect.x, top, RailWidth, height), palette);
                DrawDetail(new Rect(inRect.x + RailWidth + Gap, top,
                    Mathf.Max(0f, inRect.width - RailWidth - Gap), height), palette);

                DrawFooter(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private void DrawList(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(5f);

            if (entries.Count == 0)
            {
                GUI.color = palette.TextDisabled;
                Widgets.Label(inner.ContractedBy(6f),
                    Find.CurrentMap == null ? "No map is loaded." : "No enclosed rooms yet.");
                GUI.color = palette.TextPrimary;

                return;
            }

            Rect view = new Rect(0f, 0f, inner.width - 18f, entries.Count * (RowHeight + 2f));

            Widgets.BeginScrollView(inner, ref scroll, view);

            try
            {
                for (int i = 0; i < entries.Count; i++)
                    DrawRow(new Rect(0f, i * (RowHeight + 2f), view.width, RowHeight), entries[i], palette);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawRow(Rect row, Entry entry, UIColorPaletteDef palette)
        {
            bool chosen = selected == entry;
            bool over = Mouse.IsOver(row);

            if (chosen)
                Widgets.DrawBoxSolid(row, palette.SelectionOverlay);
            else if (over)
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            if (over)
                FloorLabelDrawer.Highlight = entry.Cells;

            // A stripe in the label's own color, so the list shows at a glance which ones have been recolored.
            if (entry.Color.HasValue)
                Widgets.DrawBoxSolid(new Rect(row.x, row.y + 2f, 3f, row.height - 4f), entry.Color.Value);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = entry.TooSmall ? palette.TextDisabled : palette.TextPrimary;

            Rect name = new Rect(row.x + 10f, row.y, Mathf.Max(0f, row.width * 0.58f), row.height);

            if (name.width >= 24f)
                Widgets.LabelEllipses(name, entry.Shown);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = entry.TooSmall ? palette.Warning : palette.TextDisabled;

            Rect tag = new Rect(row.x, row.y, Mathf.Max(0f, row.width - 8f), row.height);

            if (tag.width >= 24f)
            {
                Widgets.LabelEllipses(tag, entry.TooSmall
                    ? "too small"
                    : entry.CustomName.NullOrEmpty() ? "default" : entry.Source);
            }

            GUI.color = palette.TextPrimary;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (!Widgets.ButtonInvisible(row))
                return;

            Select(entry);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DrawDetail(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(12f);

            if (selected == null)
            {
                GUI.color = palette.TextDisabled;
                Widgets.Label(inner, "Choose a room or zone on the left.");
                GUI.color = palette.TextPrimary;

                return;
            }

            float y = inner.y;

            GameFont previousFont = Text.Font;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;
            Widgets.Label(new Rect(inner.x, y, inner.width, 16f), "NAME");
            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            y += 18f;

            Name.Draw(new Rect(inner.x, y, inner.width, 30f), palette);

            y += 38f;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;
            Widgets.Label(new Rect(inner.x, y, inner.width, 16f), "COLOR");
            y += 18f;

            List<Color> swatches = FloorLabelPalette.Swatches();
            Color current = selected.Color ?? FloorLabelPalette.Default;

            for (int i = 0; i < swatches.Count; i++)
            {
                Rect swatch = new Rect(inner.x + i * (SwatchSize + 6f), y, SwatchSize, SwatchSize - 4f);
                bool picked = FloorLabelPalette.Same(current, swatches[i]);

                Widgets.DrawBoxSolid(swatch, swatches[i]);

                if (picked)
                {
                    GUI.color = palette.TextPrimary;
                    Widgets.DrawBox(swatch.ExpandedBy(2f), 2);
                    GUI.color = palette.TextPrimary;
                }

                if (!Widgets.ButtonInvisible(swatch))
                    continue;

                // The first swatch is the default, and choosing it clears the override rather than storing the
                // default color as if it had been picked. That keeps Reset and "pick the pale one" the same act.
                selected.Color = i == 0 ? null : (Color?) swatches[i];
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += SwatchSize + 10f;

            GUI.color = palette.TextDisabled;

            // Captured and put back, not assumed. This set word wrap on and walked away, which leaves a caller
            // that wanted it off drawing wrapped text for the rest of the frame -- the mirror image of the leak
            // Text.StartOfOnGUI complains about, and just as hard to trace back here.
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = true;

                Widgets.Label(new Rect(inner.x, y, inner.width, 40f),
                    selected.TooSmall
                        ? "This room is below the minimum size, so no label is drawn. A name set now appears if "
                          + "it grows, or if you lower the minimum."
                        : "Empty name uses " + selected.DefaultName + ".");
            }
            finally
            {
                Text.WordWrap = previousWrap;
            }

            GUI.color = palette.TextPrimary;
            Text.Font = previousFont;
        }

        private void DrawFooter(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Color edge = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);
            GUI.color = edge;

            Rect apply = new Rect(rect.xMax - 110f, rect.y + 8f, 110f, 28f);
            Rect reset = new Rect(apply.x - 96f, rect.y + 8f, 90f, 28f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = problem.NullOrEmpty() ? palette.TextDisabled : palette.Danger;

            Rect said = new Rect(rect.x + 4f, rect.y + 8f, Mathf.Max(0f, reset.x - rect.x - 12f), 28f);

            if (said.width >= 24f)
            {
                Widgets.LabelEllipses(said, problem
                                            ?? "Hover a row to outline it on the map. "
                                            + entries.Count + " rooms and zones.");
            }

            GUI.color = palette.TextPrimary;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (selected != null && Button(reset, "Reset", palette, false))
            {
                Name.Text = string.Empty;
                selected.Color = null;
                Commit();
            }

            if (selected != null && Button(apply, "Apply", palette, true))
                Commit();
        }

        private static bool Button(Rect rect, string label, UIColorPaletteDef palette, bool primary)
        {
            bool over = Mouse.IsOver(rect);

            if (primary)
            {
                UIElementPainter.FillRounded(rect, palette.Accent);

                if (over)
                    UIElementPainter.FillRounded(rect, palette.HoverOverlay);
            }
            else
            {
                UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = primary ? palette.WindowBackground : palette.TextPrimary;

            Widgets.Label(rect, label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>Writes the name and color into the store, then rereads the list so the row agrees.</summary>
        private void Commit()
        {
            Entry entry = selected;

            if (entry == null)
                return;

            bool done = UIGuard.Try("FloorLabels.Apply", () =>
            {
                Map map = Find.CurrentMap;
                GameComponent_FloorLabels store = GameComponent_FloorLabels.Current;

                if (map == null || store == null)
                    return false;

                string wanted = (Name.Text ?? string.Empty).Trim();

                if (entry.IsZone)
                {
                    store.SetZone(map, entry.ZoneId, wanted, entry.Color);
                }
                else
                {
                    Room room = entry.KeyCell.IsValid && entry.KeyCell.InBounds(map)
                        ? entry.KeyCell.GetRoom(map)
                        : null;

                    if (room == null)
                        return false;

                    store.SetRoom(map, room, entry.KeyCell, wanted, entry.Color);
                }

                return true;
            }, false, null);

            problem = done ? null : "That room is no longer there. The list has been refreshed.";

            SoundDefOf.Click.PlayOneShotOnCamera();
            Refresh();
        }
    }
}
