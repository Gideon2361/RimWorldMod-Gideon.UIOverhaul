using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// Reads the room and zone labels a save made with Labels on Floor, so that save still loads once the
    /// mod is gone.
    ///
    /// <b>Why a save breaks without this, and badly.</b> Scribe stores components by class name. When the
    /// class is absent it falls back to the abstract base, <c>Verse.GameComponent</c> or
    /// <c>Verse.MapComponent</c>, which cannot be instantiated -- so the component is dropped and a null is
    /// left in the list the game iterates. One real load produced four such holes from this mod alone, and
    /// the visible symptom was other, fully loaded mods throwing NullReferenceExceptions while walking those
    /// lists. The absent mod is never the one that appears in the stack.
    ///
    /// <b>This is compatibility work, not appropriation.</b> Nothing here is copied from that mod: these are
    /// the scribe keys its saves contain, learned by reading the assembly, which is what any two programs
    /// that must exchange data have to agree on. The labels themselves are the player's own content. The
    /// feature that draws them is ours and is written from scratch, and the mod carries no licence, so
    /// nothing of its implementation is reused.
    ///
    /// <b>The absorbers are transitional and say so.</b> They exist to read old data once. They keep no
    /// behaviour of their own, and what they read is handed to <see cref="GameComponent_FloorLabels"/>,
    /// which is where labels live from then on.
    /// </summary>
    public static class Compat_LabelsOnFloor
    {
        /// <summary>
        /// Old class name to the type that should read it.
        ///
        /// Two of these carry data and two carry none: that mod's own <c>GameComponent_LabelsOnFloor</c> and
        /// <c>MapComponent_LabelsOnFloor</c> scribe nothing at all, so they only need something concrete to
        /// instantiate. They still have to be handled, because an empty component that cannot be created
        /// punches exactly the same hole as a full one.
        /// </summary>
        internal static readonly Dictionary<string, Type> Absorbed = new Dictionary<string, Type>
        {
            { "LabelsOnFloor.CustomRoomLabelManagerComponent", typeof(Absorb_RoomLabels) },
            { "LabelsOnFloor.CustomZoneLabelManagerComponent", typeof(Absorb_ZoneLabels) },
            { "LabelsOnFloor.GameComponent_LabelsOnFloor", typeof(Absorb_Inert) },
            { "LabelsOnFloor.MapComponent_LabelsOnFloor", typeof(Absorb_InertMap) }
        };

        /// <summary>
        /// Resolves those names when nothing else can.
        ///
        /// A postfix for the same reason as the Growing Zones Plus aliases: it only ever fires when the game
        /// has already failed to find the type, so a player who still has Labels on Floor installed is
        /// completely unaffected -- their type resolves, and none of this runs.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_AbsorbTypes
        {
            [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleType))]
            [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleTypeDirect))]
            [HarmonyPostfix]
            public static void Absorb(string providedClassName, ref Type __result)
            {
                if (__result != null || providedClassName.NullOrEmpty())
                    return;

                Type absorber;

                if (Absorbed.TryGetValue(providedClassName, out absorber))
                    __result = absorber;
            }
        }
    }

    /// <summary>Takes the room labels and hands them to the store.</summary>
    public class Absorb_RoomLabels : GameComponent
    {
        private List<FloorLabel> roomLabels = new List<FloorLabel>();

        public Absorb_RoomLabels(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // "roomLabels" is the key that mod wrote. Ours happens to match, which is not a coincidence:
            // FloorLabel was given its field names so this data reads without a translation step.
            Scribe_Collections.Look(ref roomLabels, "roomLabels", LookMode.Deep);
        }

        /// <summary>
        /// Moves what was read into the real store.
        ///
        /// <b>In FinalizeInit rather than PostLoadInit</b>, so every component is constructed and registered
        /// before one goes looking for another. Asking for a sibling component while the list is still being
        /// populated is how this would work in testing and fail on somebody else's mod order.
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();

            Compat.Hand(roomLabels, toRooms: true);
            roomLabels = null;
        }
    }

    /// <summary>Takes the zone labels and hands them to the store.</summary>
    public class Absorb_ZoneLabels : GameComponent
    {
        private List<FloorLabel> zoneLabels = new List<FloorLabel>();

        public Absorb_ZoneLabels(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref zoneLabels, "customZoneLabels", LookMode.Deep);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            Compat.Hand(zoneLabels, toRooms: false);
            zoneLabels = null;
        }
    }

    /// <summary>
    /// Stands in for the components of that mod which stored nothing.
    ///
    /// It needs no behaviour at all. Its entire job is to be a concrete class the save can name, so the
    /// component list loads without a hole in it.
    /// </summary>
    public class Absorb_Inert : GameComponent
    {
        public Absorb_Inert(Game game)
        {
        }
    }

    /// <summary>The map-level equivalent of <see cref="Absorb_Inert"/>.</summary>
    public class Absorb_InertMap : MapComponent
    {
        public Absorb_InertMap(Map map) : base(map)
        {
        }
    }

    internal static class Compat
    {
        /// <summary>
        /// Adds absorbed labels to the store, discarding anything that did not survive its own load.
        /// </summary>
        internal static void Hand(List<FloorLabel> absorbed, bool toRooms)
        {
            if (absorbed == null || absorbed.Count == 0)
                return;

            UIGuard.Try("FloorLabels.Absorb", () =>
            {
                GameComponent_FloorLabels store = GameComponent_FloorLabels.Current;

                if (store == null)
                    return;

                List<FloorLabel> into = toRooms ? store.Rooms : store.Zones;
                int taken = 0;

                foreach (FloorLabel label in absorbed)
                {
                    if (label == null || label.Label.NullOrEmpty())
                        continue;

                    into.Add(label);
                    taken++;
                }

                if (taken > 0)
                {
                    Log.Message("[UI Overhaul] Adopted " + taken + (toRooms ? " room" : " zone")
                                + " labels from a save made with Labels on Floor.");
                }
            }, "Custom labels from an older save were not carried over. Nothing else is affected.");
        }
    }
}
