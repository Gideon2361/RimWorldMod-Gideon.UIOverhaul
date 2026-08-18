using System.Collections.Generic;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// One renamed room or zone: what it is called, and what color it is drawn in.
    ///
    /// <b>The field names are not ours to choose.</b> They match the scribe keys written by Labels on Floor,
    /// so a save made with that mod is read directly rather than migrated by a separate pass. See
    /// <see cref="Compat_LabelsOnFloor"/> for why that is worth the constraint.
    /// </summary>
    public class FloorLabel : IExposable
    {
        /// <summary>What the player renamed it to. Empty means the room keeps its own name.</summary>
        public string Label = string.Empty;

        /// <summary>Null draws in the theme's color, which is what an unrecolored label wants.</summary>
        public Color? CustomColor;

        /// <summary>
        /// A cell inside the room, which is how a room is identified across a save.
        ///
        /// <b>Rooms have no persistent identity.</b> They are recomputed from walls whenever the region
        /// grid updates, so there is no id to store. A cell is the only durable handle: whatever room that
        /// cell is in after loading is the room the label belongs to. Unused for zones.
        /// </summary>
        public IntVec3 KeyCell;

        /// <summary>Zones do have a persistent id, so they use it. Unused for rooms.</summary>
        public int ZoneId;

        public Map Map;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Label, "label", string.Empty);

            // Written and read as a plain Color with white meaning "not set", which is the shape the older
            // data is already in. A nullable would not read that data back.
            Color color = CustomColor ?? Color.white;
            Scribe_Values.Look(ref color, "customColor", Color.white);

            if (Scribe.mode == LoadSaveMode.LoadingVars && color != Color.white)
                CustomColor = color;

            Scribe_References.Look(ref Map, "map");
            Scribe_Values.Look(ref KeyCell, "keyCell");
            Scribe_Values.Look(ref ZoneId, "zoneId", 0);
        }
    }

    /// <summary>
    /// Where renamed room and zone labels live.
    ///
    /// <b>This exists ahead of the feature that draws them, on purpose.</b> Backlog item 26 is the drawing:
    /// room names rendered onto the floor, defaulting to RimWorld's own name for the room, renameable. That
    /// is a subsystem of its own -- font atlas, meshes, edge finding, placement -- and is not built yet.
    ///
    /// What is built is the store, because a save that used Labels on Floor is being loaded *now* and its
    /// labels would otherwise be discarded. Holding them costs a list and means nothing is lost between now
    /// and the feature arriving.
    /// </summary>
    public class GameComponent_FloorLabels : GameComponent
    {
        private List<FloorLabel> rooms = new List<FloorLabel>();
        private List<FloorLabel> zones = new List<FloorLabel>();

        public GameComponent_FloorLabels(Game game)
        {
        }

        public List<FloorLabel> Rooms => rooms ?? (rooms = new List<FloorLabel>());

        public List<FloorLabel> Zones => zones ?? (zones = new List<FloorLabel>());

        internal static GameComponent_FloorLabels Current =>
            Verse.Current.Game == null ? null : Verse.Current.Game.GetComponent<GameComponent_FloorLabels>();

        /// <summary>
        /// Whether the feature is switched on. Everything that draws or edits a label must ask this first.
        ///
        /// <b>What it governs: drawing, and the labels window.</b> Nothing else, and the boundary is
        /// deliberate.
        ///
        /// <b>What it must never govern is <see cref="Compat_LabelsOnFloor"/>.</b> Those absorbers exist so a
        /// save written with Labels on Floor still loads once that mod is gone -- without them the save drops
        /// components and leaves nulls in lists the whole game walks, which took out unrelated mods when it
        /// happened for real. That is save integrity, not a feature, and no preference should be able to turn
        /// a cleared checkbox into a broken colony.
        ///
        /// <b>Nor does it govern this store.</b> Names stay in the save while the feature is off, so switching
        /// it off and on again gives them back rather than quietly discarding work.
        /// </summary>
        internal static bool Enabled => UIOverhaulSettingsFile.Current.roomNameLabels;

        /// <summary>
        /// The stored label for a room, found through the cell it was keyed on.
        ///
        /// <b>Matched by which room now contains the cell,</b> not by comparing cells. A wall moved inside a room
        /// changes nothing about the label, and a wall that splits a room in two hands the label to whichever
        /// half kept the cell -- which is the best answer available when rooms have no identity of their own.
        /// </summary>
        internal FloorLabel ForRoom(Map map, Room room)
        {
            if (map == null || room == null)
                return null;

            for (int i = 0; i < Rooms.Count; i++)
            {
                FloorLabel label = Rooms[i];

                if (label == null || label.Map != map || !label.KeyCell.IsValid
                    || !label.KeyCell.InBounds(map))
                    continue;

                Room at = label.KeyCell.GetRoom(map);

                if (at != null && at.ID == room.ID)
                    return label;
            }

            return null;
        }

        internal FloorLabel ForZone(int zoneId)
        {
            for (int i = 0; i < Zones.Count; i++)
            {
                FloorLabel label = Zones[i];

                if (label != null && label.ZoneId == zoneId)
                    return label;
            }

            return null;
        }

        /// <summary>
        /// Records a name and color for a room, or forgets it when both are absent.
        ///
        /// <b>An empty name with no color removes the entry rather than storing a blank one.</b> That is what
        /// makes Reset mean "go back to what RimWorld calls it" instead of "call it nothing", and it keeps the
        /// save from accumulating rows that say the default twice.
        /// </summary>
        internal void SetRoom(Map map, Room room, IntVec3 keyCell, string label, Color? color)
        {
            FloorLabel existing = ForRoom(map, room);
            bool empty = label.NullOrEmpty() && !color.HasValue;

            if (existing != null)
            {
                if (empty)
                {
                    Rooms.Remove(existing);

                    return;
                }

                existing.Label = label ?? string.Empty;
                existing.CustomColor = color;
                existing.KeyCell = keyCell;
                existing.Map = map;

                return;
            }

            if (empty)
                return;

            Rooms.Add(new FloorLabel
            {
                Label = label ?? string.Empty,
                CustomColor = color,
                KeyCell = keyCell,
                Map = map
            });
        }

        internal void SetZone(Map map, int zoneId, string label, Color? color)
        {
            FloorLabel existing = ForZone(zoneId);
            bool empty = label.NullOrEmpty() && !color.HasValue;

            if (existing != null)
            {
                if (empty)
                {
                    Zones.Remove(existing);

                    return;
                }

                existing.Label = label ?? string.Empty;
                existing.CustomColor = color;
                existing.Map = map;

                return;
            }

            if (empty)
                return;

            Zones.Add(new FloorLabel
            {
                Label = label ?? string.Empty,
                CustomColor = color,
                ZoneId = zoneId,
                Map = map
            });
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref rooms, "roomLabels", LookMode.Deep);
            Scribe_Collections.Look(ref zones, "zoneLabels", LookMode.Deep);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            if (rooms == null)
                rooms = new List<FloorLabel>();

            if (zones == null)
                zones = new List<FloorLabel>();

            // A label whose entry failed to load leaves a null behind, and everything downstream would then
            // have to defend against it. Dropped here, once, rather than at every reader.
            rooms.RemoveAll(label => label == null);
            zones.RemoveAll(label => label == null);
        }
    }
}
