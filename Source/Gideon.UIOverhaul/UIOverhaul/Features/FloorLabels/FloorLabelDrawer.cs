using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// Draws every room and zone label on the current map, once per frame.
    ///
    /// <b>Placement is cached, because finding it is the expensive half.</b> Scanning a room's cells for its
    /// widest clear run is fine once and unaffordable sixty times a second across forty rooms. Rooms are
    /// recomputed from walls whenever the region grid updates, so the cache is rebuilt on an interval rather
    /// than tied to an event RimWorld does not raise, a wall going up shows its new label within a second,
    /// which is faster than anybody notices.
    ///
    /// <b>Drawing itself is a matrix and a mesh per label.</b> Both are cached, so the per-frame cost is a
    /// dictionary lookup and a <c>Graphics.DrawMesh</c> call each.
    ///
    /// <b>Everything here is gated on the setting</b>, checked once per frame at the top. See
    /// <c>GameComponent_FloorLabels.Enabled</c> for what that does and does not switch off.
    /// </summary>
    internal static class FloorLabelDrawer
    {
        /// <summary>
        /// How often placements are recomputed, in ticks.
        ///
        /// Sixty is a second of game time at normal speed. Short enough that building a wall appears to update
        /// the labels immediately, long enough that the scan is a rounding error.
        /// </summary>
        private const int RebuildInterval = 60;

        /// <summary>
        /// Smallest room worth labeling, in cells, from the Overlays group in the options.
        ///
        /// A setting because how small is too small depends on how far out somebody plays: the same closet is
        /// unreadable clutter zoomed out and perfectly legible zoomed in.
        /// </summary>
        internal static int MinimumRoomCells => UIOverhaulSettingsFile.Current.roomLabelMinimumCells;

        /// <summary>
        /// Narrowest run a label will squeeze into, in cells.
        ///
        /// Rule 3 in the mockup: text shrinks to fit, but below this it is too small to read and the room is
        /// treated as unlabelable instead. A label nobody can read is worse than none, because it still costs a
        /// draw call and still clutters the floor.
        /// </summary>
        private const float MinimumRunCells = 2.5f;

        /// <summary>How much of the run the text is allowed to fill, leaving air at both ends.</summary>
        private const float RunFill = 0.92f;

        /// <summary>
        /// Alpha the dark fill is drawn at, over whatever the label's color already carries.
        ///
        /// Faint on purpose. A label is a note about the floor rather than a thing on it, and at full strength it
        /// competes with the colony for attention every frame you are not reading it. A shade higher than the
        /// pale version needed, because dark strokes thin out faster as they fade.
        ///
        /// Lowered from 0.62 on 2026-08-18, because visible but not prominent was still landing on prominent.
        /// Moved together with <see cref="OutlineAlpha"/> rather than on its own: dropping only the fill leaves
        /// the white halo as the loudest thing on the floor, which is the pale, out of focus look the first
        /// attempt had.
        /// </summary>
        private const float FillAlpha = 0.42f;

        /// <summary>
        /// Alpha of each white outline copy.
        ///
        /// <b>Low because there are eight of them and they overlap.</b> Each copy blends over the last, so the
        /// halo's real opacity is far higher than this number, at 0.55 the eight of them accumulated into a
        /// solid white blob with the text lost inside it. This is per copy, not the outline's total.
        ///
        /// <b>Halved rather than trimmed, which is arithmetic and not taste.</b> Eight copies at alpha
        /// <c>a</c> leave the core of the halo at <c>1 - (1 - a)^8</c>, so 0.22 was really about 0.86. Trimming
        /// the per copy number by the same third the fill took would have left the halo near 0.72, only half as
        /// much reduction, and the outline would have come out relatively louder than it started. 0.11 puts the
        /// halo near 0.61, down by the same third as the fill, so the two layers fade together and the balance
        /// that made the text readable is preserved.
        /// </summary>
        private const float OutlineAlpha = 0.11f;

        /// <summary>
        /// How far above the outline the fill is drawn.
        ///
        /// <b>Not cosmetic: without it the outline covers the text.</b> The font shader writes no depth and tests
        /// none, so nine meshes submitted at one altitude have no defined order between them, and the eight
        /// white copies were landing on top of the dark fill they were supposed to sit behind. Transparent
        /// geometry is sorted by distance to the camera, so lifting the fill a hair is what puts it in front.
        ///
        /// Far below the gap between RimWorld's own altitude layers, so nothing else changes order.
        /// </summary>
        private const float FillLift = 0.02f;

        /// <summary>
        /// Outline thickness, in the mesh's own glyph units.
        ///
        /// Thin: a rim that separates the dark letters from the floor without becoming a shape of its own. Wider
        /// than this and the eight copies start reading as a glow rather than an edge, which is what made the
        /// first attempt look out of focus.
        /// </summary>
        private const float OutlineOffsetUnits = 1.4f;

        /// <summary>
        /// The eight directions the outline is offset in.
        ///
        /// Cardinals alone leave gaps on diagonal strokes and diagonals alone leave gaps on vertical ones, which
        /// on text is the difference between an outline and a dotted suggestion of one.
        /// </summary>
        private static readonly Vector2[] OutlineDirections =
        {
            new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f),
            new Vector2(0.7f, 0.7f), new Vector2(-0.7f, 0.7f), new Vector2(0.7f, -0.7f),
            new Vector2(-0.7f, -0.7f)
        };

        /// <summary>
        /// Where each label was drawn this frame, so a click can find it.
        ///
        /// <b>Rebuilt every frame rather than cached with the placements,</b> because it depends on the label's
        /// final scale, which depends on the text, which changes the moment somebody renames a room.
        /// </summary>
        internal static readonly List<FloorLabelHit> Hits = new List<FloorLabelHit>();

        private static readonly Dictionary<int, FloorLabelSpot> RoomSpots = new Dictionary<int, FloorLabelSpot>();
        private static readonly Dictionary<int, FloorLabelSpot> ZoneSpots = new Dictionary<int, FloorLabelSpot>();

        private static int lastRebuiltTick = -9999;
        private static int lastMapId = -1;

        /// <summary>
        /// Draws the labels for one map.
        ///
        /// Guarded as a whole rather than per label: this runs every frame, and a fault here is either in all of
        /// them or in none.
        /// </summary>
        internal static void Draw(Map map)
        {
            if (map == null || !GameComponent_FloorLabels.Enabled || !FloorLabelFont.Available)
                return;

            UIGuard.Try("FloorLabels.Draw", () => DrawAll(map),
                "Room names are not drawn on the floor. Nothing else is affected.");
        }

        private static void DrawAll(Map map)
        {
            // A different map means every cached placement belongs to somewhere else. Ids rather than
            // references, so nothing here keeps a disposed map alive.
            if (map.uniqueID != lastMapId)
            {
                lastMapId = map.uniqueID;
                RoomSpots.Clear();
                ZoneSpots.Clear();
                lastRebuiltTick = -9999;
            }

            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;

            if (tick - lastRebuiltTick >= RebuildInterval || tick < lastRebuiltTick)
            {
                lastRebuiltTick = tick;
                Rebuild(map);
            }

            // Cleared before drawing, since the click test reads whatever the last frame recorded.
            Hits.Clear();

            GameComponent_FloorLabels store = GameComponent_FloorLabels.Current;

            DrawRooms(map, store);
            DrawZones(map, store);
            DrawHighlight();
        }

        /// <summary>Recomputes where every label sits, and forgets rooms and zones that are gone.</summary>
        private static void Rebuild(Map map)
        {
            RoomSpots.Clear();
            ZoneSpots.Clear();

            // Cells already spoken for by a label placed earlier in this pass.
            //
            // Rooms go first and zones work around them, because a zone lives inside a room rather than next to
            // one: a stockpile filling a storeroom wants the very run the room's own name wants, and both drawn
            // there superimpose two words into an unreadable smear. Rooms win because a room is the larger and
            // longer lived thing, and because naming rooms is what this feature is for.
            HashSet<IntVec3> taken = new HashSet<IntVec3>();

            IReadOnlyList<Room> rooms = map.regionGrid == null ? null : map.regionGrid.AllRooms;

            int seen = 0;
            int notStructural = 0;
            int tooSmall = 0;
            int noRun = 0;

            if (rooms != null)
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    Room room = rooms[i];

                    seen++;

                    if (!Structural(room))
                    {
                        notStructural++;

                        continue;
                    }

                    if (room.CellCount < MinimumRoomCells)
                    {
                        tooSmall++;

                        continue;
                    }

                    FloorLabelSpot spot = FloorLabelPlacement.Find(room.Cells, map, taken);

                    // Second pass over furniture, only for a room that could not be given a clear run. See the
                    // note on FloorLabelPlacement: a four cell bedroom has nowhere legal to put a word, and
                    // silently drawing nothing was a worse answer than a watermark across the bed.
                    if (!Placeable(spot))
                        spot = FloorLabelPlacement.Find(room.Cells, map, taken, true);

                    if (!Placeable(spot))
                    {
                        noRun++;

                        continue;
                    }

                    RoomSpots[room.ID] = spot;
                    FloorLabelPlacement.Reserve(spot, taken);
                }
            }

            Census(seen, notStructural, tooSmall, noRun);

            if (map.zoneManager == null)
                return;

            List<Zone> zones = map.zoneManager.AllZones;

            for (int i = 0; i < zones.Count; i++)
            {
                Zone zone = zones[i];

                // Null entries are a real possibility rather than paranoia: a zone whose class is missing is
                // dropped during load and leaves a hole in this list. That is what took out our own grow zone
                // code on a converted save.
                if (zone == null || zone.label.NullOrEmpty() || zone.Cells == null)
                    continue;

                FloorLabelSpot spot = FloorLabelPlacement.Find(zone.Cells, map, taken);

                // Zones get the same fallback, for the same reason: a stockpile packed with shelves is exactly
                // the crowded case, and a zone that could not name itself only because it is well used would be
                // a strange rule to keep.
                if (!Placeable(spot))
                    spot = FloorLabelPlacement.Find(zone.Cells, map, taken, true);

                if (!Placeable(spot))
                    continue;

                ZoneSpots[zone.ID] = spot;
                FloorLabelPlacement.Reserve(spot, taken);
            }
        }

        /// <summary>
        /// Reports why rooms were passed over, when debug logging is on and the answer has changed.
        ///
        /// <b>Added on 2026-08-19 because a report of missing room labels could not be diagnosed by reading the
        /// code.</b> Four things drop a room, they are indistinguishable on screen, and every one of them looks
        /// exactly like the feature being broken. This turns "many rooms have no label" into a number against
        /// each reason, which is the difference between fixing it and guessing at it.
        ///
        /// <b>Only when the tally changes,</b> since this runs once a second forever. A wall going up moves a
        /// number and prints a line; a colony sitting still prints nothing. Costs four integers and a comparison
        /// per rebuild when logging is off.
        /// </summary>
        private static void Census(int seen, int notStructural, int tooSmall, int noRun)
        {
            if (!UIDebug.Enabled)
                return;

            if (seen == lastSeen && notStructural == lastNotStructural && tooSmall == lastTooSmall
                && noRun == lastNoRun)
                return;

            lastSeen = seen;
            lastNotStructural = notStructural;
            lastTooSmall = tooSmall;
            lastNoRun = noRun;

            UIDebug.Log("Floor labels: " + seen + " rooms on the map, " + RoomSpots.Count + " placed. Passed over "
                        + notStructural + " as not a proper indoor room, " + tooSmall + " as under the "
                        + MinimumRoomCells + " cell minimum, and " + noRun
                        + " with no run wide enough even over furniture.");
        }

        private static int lastSeen = -1;
        private static int lastNotStructural = -1;
        private static int lastTooSmall = -1;
        private static int lastNoRun = -1;

        /// <summary>
        /// Whether a spot is worth keeping, which is also whether it is worth reserving.
        ///
        /// <b>The run length test belongs here and not only in the drawing.</b> A spot too narrow to print is
        /// discarded by <see cref="DrawOne"/> anyway, so storing it changes nothing on screen, but reserving its
        /// cells would block a zone label that could have used them. That is a real case rather than a
        /// theoretical one: in a room cluttered enough that its only clear runs are a cell or two wide, the room
        /// name cannot be drawn, and the stockpile filling it should still get its chance at the floor.
        ///
        /// The drawer applies one further test this cannot, since it depends on the text: a name long enough to
        /// shrink below readable is dropped once its mesh exists. Such a label reserves cells it does not use,
        /// which is a smaller waste than measuring every string on every rebuild.
        /// </summary>
        private static bool Placeable(FloorLabelSpot spot)
        {
            return spot.Found && spot.Cells >= MinimumRunCells;
        }

        /// <summary>
        /// Whether a room is the sort of thing that gets a name on its floor.
        ///
        /// <b>The outdoors is a Room too,</b> and it is the whole map. Skipping it is not a nicety: a label
        /// scaled to the outdoors would be a word the size of the colony.
        /// </summary>
        internal static bool Structural(Room room)
        {
            if (room == null || room.Map == null)
                return false;

            if (room.Fogged || room.IsDoorway || !room.ProperRoom)
                return false;

            return !room.PsychologicallyOutdoors && !room.TouchesMapEdge;
        }

        private static void DrawRooms(Map map, GameComponent_FloorLabels store)
        {
            IReadOnlyList<Room> rooms = map.regionGrid == null ? null : map.regionGrid.AllRooms;

            if (rooms == null)
                return;

            for (int i = 0; i < rooms.Count; i++)
            {
                Room room = rooms[i];

                if (room == null)
                    continue;

                FloorLabelSpot spot;

                if (!RoomSpots.TryGetValue(room.ID, out spot))
                    continue;

                FloorLabel custom = store == null ? null : store.ForRoom(map, room);

                string text = custom != null && !custom.Label.NullOrEmpty()
                    ? custom.Label
                    : DefaultName(room);

                Color color = custom != null && custom.CustomColor.HasValue
                    ? custom.CustomColor.Value
                    : DefaultColor();

                DrawOne(text, color, spot, new FloorLabelHit { KeyCell = KeyCellOf(map, room) });
            }
        }

        private static void DrawZones(Map map, GameComponent_FloorLabels store)
        {
            if (map.zoneManager == null)
                return;

            List<Zone> zones = map.zoneManager.AllZones;

            for (int i = 0; i < zones.Count; i++)
            {
                Zone zone = zones[i];

                if (zone == null)
                    continue;

                FloorLabelSpot spot;

                if (!ZoneSpots.TryGetValue(zone.ID, out spot))
                    continue;

                FloorLabel custom = store == null ? null : store.ForZone(zone.ID);

                string text = custom != null && !custom.Label.NullOrEmpty() ? custom.Label : zone.label;

                // The zone's own color, lightened: zone colors are deliberately faint fills and text in the
                // same value disappears into them.
                Color color = custom != null && custom.CustomColor.HasValue
                    ? custom.CustomColor.Value
                    : Readable(zone.color);

                DrawOne(text, color, spot, new FloorLabelHit { IsZone = true, ZoneId = zone.ID });
            }
        }

        /// <summary>
        /// Puts one label on the ground, scaled to the run it was given.
        ///
        /// <b>Drawn as a watermark: a faint fill inside a white outline.</b> A single flat color cannot work,
        /// because the thing behind it is not one color, the same label crosses wood, stone, carpet, soil and
        /// snow, and any fill legible on one of those disappears into another. Two tones solve it between them:
        /// whatever the floor is, either the pale outline or the darker fill contrasts with it.
        ///
        /// <b>The outline is eight offset copies of the same mesh.</b> Nine draws per label rather than one, and
        /// worth it, the alternative is a second font atlas rendered with an outline baked in, which is far
        /// more machinery and fixes the outline thickness at build time.
        ///
        /// The scale is the whole of rule 3: the mesh is built once at atlas resolution and squeezed to fit
        /// here, so a long name shrinks rather than running through a wall.
        /// </summary>
        private static void DrawOne(string text, Color color, FloorLabelSpot spot, FloorLabelHit hit)
        {
            if (text.NullOrEmpty() || spot.Cells < MinimumRunCells)
                return;

            // Upper cased for the drawing only, never in the store: what somebody typed is what the labels
            // window shows them back, and forcing case into the save would make renaming lossy.
            //
            // Invariant rather than the current culture, because ToUpper under a Turkish locale turns i into a
            // dotted capital and would quietly change room names for those players alone.
            FloorLabelMesh mesh = FloorLabelMeshes.For(text.ToUpperInvariant());

            if (mesh == null || mesh.Mesh == null || mesh.Width <= 0f)
                return;

            float allowed = spot.Cells * RunFill;
            float scale = allowed / mesh.Width;

            // Never taller than one cell, however much room there is.
            //
            // Was a cell and a half, and that was too big: at that size the name competes with the room instead
            // of annotating it, which is the opposite of a watermark. One cell reads at any sane zoom and leaves
            // the floor visible through it.
            float capped = Mathf.Min(scale, 1f / Mathf.Max(0.01f, mesh.Height));

            if (mesh.Height * capped < 0.35f)
                return;

            Material outline = FloorLabelFont.MaterialFor(new Color(1f, 1f, 1f, OutlineAlpha));
            Material fill = FloorLabelFont.MaterialFor(new Color(color.r, color.g, color.b,
                color.a * FillAlpha));

            if (fill == null)
                return;

            Vector3 size = new Vector3(capped, 1f, capped);

            // Offset in mesh units before scaling, so the outline stays proportional to the text instead of
            // getting thicker on small labels and vanishing on large ones.
            float step = OutlineOffsetUnits * capped;

            if (outline != null)
            {
                for (int i = 0; i < OutlineDirections.Length; i++)
                {
                    Vector2 direction = OutlineDirections[i];

                    Graphics.DrawMesh(mesh.Mesh,
                        Matrix4x4.TRS(spot.Center + new Vector3(direction.x * step, 0f, direction.y * step),
                            Quaternion.identity, size), outline, 0);
                }
            }

            Graphics.DrawMesh(mesh.Mesh,
                Matrix4x4.TRS(spot.Center + new Vector3(0f, FillLift, 0f), Quaternion.identity, size), fill, 0);

            // Recorded so a click can find this label. Half a cell of slack, because somebody aiming at text
            // aims at the word rather than at its exact glyph bounds.
            float halfWidth = mesh.Width * capped * 0.5f + 0.5f;
            float halfHeight = mesh.Height * capped * 0.5f + 0.5f;

            hit.MinX = spot.Center.x - halfWidth;
            hit.MaxX = spot.Center.x + halfWidth;
            hit.MinZ = spot.Center.z - halfHeight;
            hit.MaxZ = spot.Center.z + halfHeight;

            Hits.Add(hit);

            if (!FloorLabelEditing.Active)
                return;

            // While editing, every label says it can be clicked. Without this the mode is invisible and the
            // only way to discover it is to click something and see what happens.
            GenDraw.DrawFieldEdges(CellsUnder(hit), FloorLabelPalette.Highlight);
        }

        /// <summary>
        /// A cell inside this room, for identifying its label later.
        ///
        /// Prefers the cell a stored label was already keyed on, so clicking a renamed room finds the entry that
        /// exists rather than creating a second one beside it. Otherwise the lowest cell, which is stable across
        /// rebuilds, an arbitrary pick would key the same room differently every time.
        /// </summary>
        private static IntVec3 KeyCellOf(Map map, Room room)
        {
            GameComponent_FloorLabels store = GameComponent_FloorLabels.Current;
            FloorLabel stored = store == null ? null : store.ForRoom(map, room);

            if (stored != null && stored.KeyCell.IsValid)
                return stored.KeyCell;

            IntVec3 best = IntVec3.Invalid;

            foreach (IntVec3 cell in room.Cells)
            {
                if (!best.IsValid || cell.z < best.z || (cell.z == best.z && cell.x < best.x))
                    best = cell;
            }

            return best;
        }

        /// <summary>The cells a label covers, for the outline drawn around it while editing.</summary>
        private static List<IntVec3> CellsUnder(FloorLabelHit hit)
        {
            List<IntVec3> cells = new List<IntVec3>();

            for (int x = Mathf.FloorToInt(hit.MinX); x <= Mathf.FloorToInt(hit.MaxX); x++)
            {
                for (int z = Mathf.FloorToInt(hit.MinZ); z <= Mathf.FloorToInt(hit.MaxZ); z++)
                    cells.Add(new IntVec3(x, 0, z));
            }

            return cells;
        }


        /// <summary>RimWorld's own name for the room, which is what a label says until somebody changes it.</summary>
        private static string DefaultName(Room room)
        {
            RoomRoleDef role = room.Role;

            return role == null ? null : role.LabelCap;
        }

        private static Color DefaultColor()
        {
            return FloorLabelPalette.Default;
        }

        /// <summary>
        /// A zone's own color, darkened enough to work as label text.
        ///
        /// <b>Darkened rather than lightened, which is the reverse of what this did.</b> Zone colors are faint
        /// pale fills by design, and text in the same value is invisible against them; the earlier version
        /// lightened them further, which made that worse. Pulling them down keeps the zone recognizable by hue
        /// while giving the white outline something to contrast with.
        /// </summary>
        private static Color Readable(Color zoneColor)
        {
            return new Color(zoneColor.r * 0.35f, zoneColor.g * 0.35f, zoneColor.b * 0.35f, 1f);
        }

        /// <summary>
        /// Cells to outline this frame, or null.
        ///
        /// <b>Set by the labels window and drawn here, because the window cannot draw it itself.</b> An
        /// outline on the map is world-space geometry, and geometry submitted from <c>OnGUI</c> belongs to a
        /// frame that has already been drawn, the same reason the labels are drawn from <c>MapUpdate</c>. So
        /// the window states what it wants highlighted and this pass obliges.
        /// </summary>
        internal static List<IntVec3> Highlight;

        private static void DrawHighlight()
        {
            List<IntVec3> cells = Highlight;

            if (cells == null || cells.Count == 0)
                return;

            GenDraw.DrawFieldEdges(cells, FloorLabelPalette.Highlight);
        }
    }

    /// <summary>
    /// Runs the drawing once per frame for the map being looked at.
    ///
    /// <b><c>Map.MapUpdate</c> is the seam because the labels are meshes, not interface.</b> Anything submitted
    /// with <c>Graphics.DrawMesh</c> belongs to the frame that submitted it, and the frame's map drawing happens
    /// here, from <c>OnGUI</c> it would be too late and the labels would never appear. It is also where
    /// RimWorld's own map overlays go, so they are drawn in the same pass and composite correctly.
    /// </summary>
    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    internal static class Patch_Map_MapUpdate_FloorLabels
    {
        public static void Postfix(Map __instance)
        {
            // Only the map on screen. MapUpdate runs for every loaded map, and drawing the others would be
            // meshes submitted for a camera that is not looking at them.
            if (__instance != null && __instance == Find.CurrentMap)
                FloorLabelDrawer.Draw(__instance);
        }
    }
}