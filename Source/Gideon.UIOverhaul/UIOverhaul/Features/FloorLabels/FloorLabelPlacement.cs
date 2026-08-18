using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>Where a label sits and how much room it has.</summary>
    internal struct FloorLabelSpot
    {
        /// <summary>Middle of the run, in world coordinates, at the overlay altitude.</summary>
        internal Vector3 Center;

        /// <summary>How many cells wide the clear run is.</summary>
        internal int Cells;

        /// <summary>
        /// Leftmost cell of the run, and the row it sits on.
        ///
        /// Carried next to the world space center so the run's cells can be handed back to
        /// <see cref="FloorLabelPlacement.Reserve"/> exactly, instead of being recovered from floating point.
        /// </summary>
        internal int StartX;

        internal int Z;

        internal bool Found;
    }

    /// <summary>
    /// A label as it was actually drawn, and what it belongs to.
    ///
    /// <b>Tested in map coordinates rather than screen ones.</b> Converting the label's bounds to a screen
    /// rectangle would mean redoing the camera's projection by hand and getting it wrong at some zoom; the mouse
    /// already has a map position, and the label's bounds are already in map units, so the comparison is a
    /// rectangle test with no maths in it.
    /// </summary>
    internal struct FloorLabelHit
    {
        internal float MinX;
        internal float MaxX;
        internal float MinZ;
        internal float MaxZ;

        internal bool IsZone;
        internal int ZoneId;

        /// <summary>A cell inside the room, which is how a room label is identified. Unused for zones.</summary>
        internal IntVec3 KeyCell;

        internal bool Contains(float x, float z)
        {
            return x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
        }
    }

    /// <summary>
    /// Finds the stretch of floor a label should sit on.
    ///
    /// <b>The widest clear horizontal run, not the room's center.</b> This is the difference between the feature
    /// looking finished and looking naive: a centered label lands on the dining table, across the beds, or over
    /// the workbench in the middle of a workshop. Rooms are built around their furniture, so the readable space
    /// is almost never the middle.
    ///
    /// <b>Horizontal only.</b> Text runs left to right, so a tall thin gap is no use to it, and rotating labels
    /// to fit was considered and rejected -- a colony of labels at different angles is harder to read than a few
    /// that are missing.
    ///
    /// <b>Two labels never share cells.</b> Zones sit inside rooms rather than beside them, so a stockpile
    /// filling a storeroom asks for the same widest run the room itself wants. Drawing both there does not give
    /// two labels, it gives one smear of superimposed glyphs. Callers place in priority order and reserve as
    /// they go, which pushes the second label onto the next best row and leaves it reading as a deliberate
    /// second line.
    ///
    /// <b>A building disqualifies a cell; a plant or an item does not.</b> Furniture is what a label must not
    /// cover, and it is also what stays put. Filth, corpses and dropped steel move constantly, and a label that
    /// jumped every time somebody dropped a rock would be worse than one sitting on a rock.
    ///
    /// <b>Unless there is nowhere else, in which case furniture is covered.</b> A small crowded room has no clear
    /// run at all: a double bed with an end table on each side fills a four cell width, and one torch splits
    /// whatever row is left into two pieces too short to hold a word. Under the absolute rule those rooms drew
    /// nothing, and drew nothing silently, which is a worse answer than a faint watermark lying across a bed.
    /// So placement runs twice and the second pass ignores buildings.
    ///
    /// It stays a fallback and never a preference: any room with somewhere clear to put its name still gets the
    /// clear spot, so the rule holds everywhere it can hold. What changed is only what happens when it cannot.
    /// </summary>
    internal static class FloorLabelPlacement
    {
        /// <summary>
        /// Finds the best run among these cells.
        ///
        /// Cells may be any set: a room's or a zone's. Nothing here knows the difference, which is why zones
        /// need no separate placement code.
        ///
        /// <paramref name="taken"/> is the cells earlier labels already occupy, treated exactly as though
        /// something were built on them. Pass null when placing in isolation, such as for a preview.
        ///
        /// <paramref name="overFurniture"/> drops the no-building rule, which is the fallback pass described on
        /// this class. Nothing else changes: fogged and out of bounds cells are still refused, and so are cells
        /// another label has taken.
        /// </summary>
        internal static FloorLabelSpot Find(IEnumerable<IntVec3> cells, Map map, HashSet<IntVec3> taken = null,
            bool overFurniture = false)
        {
            FloorLabelSpot spot = new FloorLabelSpot();

            if (cells == null || map == null)
                return spot;

            // Grouped by row, because a run is by definition cells sharing a z. Built once rather than probed
            // per row, since a room's cells arrive in no useful order.
            Dictionary<int, List<int>> rows = new Dictionary<int, List<int>>();
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;

            foreach (IntVec3 cell in cells)
            {
                if (!Clear(cell, map, overFurniture) || (taken != null && taken.Contains(cell)))
                    continue;

                List<int> row;

                if (!rows.TryGetValue(cell.z, out row))
                {
                    row = new List<int>();
                    rows[cell.z] = row;
                }

                row.Add(cell.x);

                if (cell.z < minZ)
                    minZ = cell.z;

                if (cell.z > maxZ)
                    maxZ = cell.z;
            }

            if (rows.Count == 0)
                return spot;

            float middleZ = (minZ + maxZ) * 0.5f;

            int bestLength = 0;
            int bestStart = 0;
            int bestZ = 0;
            float bestDistance = float.MaxValue;

            foreach (KeyValuePair<int, List<int>> row in rows)
            {
                List<int> xs = row.Value;
                xs.Sort();

                int runStart = xs[0];
                int runLength = 1;

                for (int i = 1; i <= xs.Count; i++)
                {
                    bool contiguous = i < xs.Count && xs[i] == xs[i - 1] + 1;

                    if (contiguous)
                    {
                        runLength++;

                        continue;
                    }

                    float distance = Mathf.Abs(row.Key - middleZ);

                    // Longer wins outright. Between equals, the row nearer the room's middle looks deliberate
                    // rather than shoved against a wall.
                    if (runLength > bestLength || (runLength == bestLength && distance < bestDistance))
                    {
                        bestLength = runLength;
                        bestStart = runStart;
                        bestZ = row.Key;
                        bestDistance = distance;
                    }

                    if (i < xs.Count)
                    {
                        runStart = xs[i];
                        runLength = 1;
                    }
                }
            }

            if (bestLength <= 0)
                return spot;

            spot.Found = true;
            spot.Cells = bestLength;
            spot.StartX = bestStart;
            spot.Z = bestZ;

            // Cell coordinates address a corner, so the half cell puts the label on the middle of the run
            // rather than a half tile left and down of it.
            spot.Center = new Vector3(bestStart + bestLength * 0.5f,
                AltitudeLayer.MetaOverlays.AltitudeFor(), bestZ + 0.5f);

            return spot;
        }

        /// <summary>
        /// Marks the cells a placed label covers, so whatever is placed next has to go elsewhere.
        ///
        /// <b>One row, because a label is never taller than one cell.</b> The drawer caps glyph height at a
        /// single cell and centers it on the row, so the text spans this row and no other. Reserving a margin
        /// above and below would refuse placements that were never going to collide, and two labels on
        /// neighboring rows read perfectly well.
        /// </summary>
        internal static void Reserve(FloorLabelSpot spot, HashSet<IntVec3> taken)
        {
            if (!spot.Found || taken == null)
                return;

            for (int x = spot.StartX; x < spot.StartX + spot.Cells; x++)
                taken.Add(new IntVec3(x, 0, spot.Z));
        }

        /// <summary>
        /// Whether a label may sit on this cell.
        ///
        /// Fogged cells are excluded as well as built ones: a label stretched across unexplored floor would be
        /// telling somebody the shape of a room they have not seen.
        /// </summary>
        private static bool Clear(IntVec3 cell, Map map, bool overFurniture)
        {
            if (!cell.InBounds(map) || cell.Fogged(map))
                return false;

            return overFurniture || cell.GetFirstBuilding(map) == null;
        }
    }
}
