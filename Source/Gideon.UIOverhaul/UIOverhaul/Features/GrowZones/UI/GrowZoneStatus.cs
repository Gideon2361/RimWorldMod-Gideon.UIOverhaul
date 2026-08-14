using Gideon.UIFramework.Caching;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones.UI
{
    /// <summary>
    /// The per-zone figures shown by <see cref="MainTabWindow_GrowZones"/>: a plant census, what
    /// harvesting right now would yield, average growth, and the zone's temperature.
    /// </summary>
    public class GrowZoneStatus
    {
        /// <summary>What the zone is set to grow, as of the last rebuild.</summary>
        public ThingDef Plant;

        /// <summary>Cells holding a plant of <see cref="Plant"/>. Weeds and leftovers are ignored.</summary>
        public int PlantCount;

        public int HarvestablePlants;

        /// <summary>What harvesting the zone right now would produce.</summary>
        public int YieldNow;

        /// <summary>What the plants already in the ground will produce once fully grown.</summary>
        public int YieldAtMaturity;

        /// <summary>Mean growth of the counted plants, 0..1.</summary>
        public float AverageGrowth;

        public float Temperature;
        public bool HasTemperature;
    }

    /// <summary>
    /// Caches a <see cref="GrowZoneStatus"/> per zone.
    ///
    /// Building one walks every cell of the zone and calls <see cref="Plant.YieldNow"/> on each
    /// plant. The tab reads these figures for every zone on every frame, so an uncached rebuild
    /// would be a full sweep of every growing zone on the map sixty times a second.
    /// </summary>
    public static class GrowZoneStatusCache
    {
        /// <summary>
        /// Rebuild interval, in running game seconds.
        ///
        /// Five, rather than the two this used before: the figures describe crops, which change over in-game hours,
        /// and a zone list is read rather than watched. Nothing here justified refreshing more often.
        ///
        /// The interval freezes while the game is paused, which is what <c>freezeWhilePaused</c> below asks for.
        /// That behaviour came from this class originally and is now the framework's, so every cache can have it:
        /// none of these numbers can change while the simulation is stopped, and rebuilding then is waste in the one
        /// situation where a player is most likely to be sitting still with the tab open.
        /// </summary>
        private const float RefreshSeconds = 5f;

        /// <summary>
        /// The cache itself, registered with <see cref="UICacheController"/> so it is cleared on a def reload,
        /// pruned of deleted zones, and forgotten about when a zone goes away.
        ///
        /// <c>stillValid</c> is a Map test rather than a search of the zone manager, which would be a linear scan per
        /// key. A zone that has been deleted either reports no map or throws reaching for one, and the cache treats
        /// both as gone.
        /// </summary>
        private static readonly UICache<Zone_Growing, GrowZoneStatus> Cache =
            new UICache<Zone_Growing, GrowZoneStatus>("GrowZones.Status", RefreshSeconds, Build,
                zone => zone != null && zone.Map != null, true);

        /// <summary>
        /// Drops every cached entry, so the next read takes fresh figures. Called when the tab opens.
        ///
        /// Releasing deleted zones is no longer this method's job: the cache prunes them, and
        /// <c>UICacheController.Forget</c> drops one the moment it is destroyed.
        /// </summary>
        public static void Clear() => Cache.Clear();

        /// <summary>
        /// This zone's figures, rebuilt if they have gone stale.
        ///
        /// Throws <see cref="InvalidCacheRequest"/> if the zone no longer exists, which for a caller iterating a
        /// zone list it built this frame means the list is wrong. Use <c>Cache.TryGet</c> where a zone may legitimately
        /// have been deleted since.
        /// </summary>
        public static GrowZoneStatus For(Zone_Growing zone) => Cache.Get(zone);

        /// <summary>Whether this zone's figures can be read at all, without building them.</summary>
        public static bool TryGet(Zone_Growing zone, out GrowZoneStatus status) => Cache.TryGet(zone, out status);

        /// <summary>
        /// Takes every figure for one zone: walks its cells and asks each plant what it would yield.
        ///
        /// Returns a fresh object rather than filling one in place, which is what the cache wants. That costs one
        /// small allocation per zone per interval, against the previous version's zero -- a trade worth making for
        /// the shared clearing and pruning, at five seconds apart.
        /// </summary>
        private static GrowZoneStatus Build(Zone_Growing zone)
        {
            GrowZoneStatus status = new GrowZoneStatus();

            Map map = zone.Map;
            status.Plant = zone.GetPlantDefToGrow();
            if (map == null)
                return status;

            status.HasTemperature =
                GenTemperature.TryGetTemperatureForCell(zone.Position, map, out status.Temperature);

            if (status.Plant == null)
                return status;

            float growthSum = 0f;
            List<IntVec3> cells = zone.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                Plant plant = cells[i].GetPlant(map);

                // Only the crop counts. A zone commonly also holds weeds, or the previous crop
                // waiting to be cut, and neither belongs in this zone's harvest estimate.
                if (plant == null || plant.def != status.Plant)
                    continue;

                status.PlantCount++;
                growthSum += plant.Growth;
                if (plant.HarvestableNow)
                    status.HarvestablePlants++;
                status.YieldNow += plant.YieldNow();
            }

            if (status.PlantCount > 0)
                status.AverageGrowth = growthSum / status.PlantCount;

            status.YieldAtMaturity = Mathf.RoundToInt(
                status.PlantCount * status.Plant.plant.harvestYield * CropYieldFactor);

            return status;
        }

        /// <summary>
        /// The difficulty's crop multiplier, which <see cref="Plant.YieldNow"/> already applies.
        /// Included in the maturity estimate so the two figures are on the same scale.
        /// </summary>
        private static float CropYieldFactor =>
            Find.Storyteller?.difficulty?.cropYieldFactor ?? 1f;
    }
}
