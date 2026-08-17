using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// One renamed room or zone: what it is called, and what colour it is drawn in.
    ///
    /// <b>The field names are not ours to choose.</b> They match the scribe keys written by Labels on Floor,
    /// so a save made with that mod is read directly rather than migrated by a separate pass. See
    /// <see cref="Compat_LabelsOnFloor"/> for why that is worth the constraint.
    /// </summary>
    public class FloorLabel : IExposable
    {
        /// <summary>What the player renamed it to. Empty means the room keeps its own name.</summary>
        public string Label = string.Empty;

        /// <summary>Null draws in the theme's colour, which is what an unrecoloured label wants.</summary>
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
            Color colour = CustomColor ?? Color.white;
            Scribe_Values.Look(ref colour, "customColor", Color.white);

            if (Scribe.mode == LoadSaveMode.LoadingVars && colour != Color.white)
                CustomColor = colour;

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
