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
        /// Rebuild interval, in real seconds. Measured against the wall clock rather than game
        /// ticks for the same reason <see cref="Bill_Growing.CurrentCountCached"/> is: RimWorld
        /// speeds the game up by running more ticks per frame, so a tick budget would rebuild three
        /// times as often at 3x and never while paused.
        /// </summary>
        private const float RefreshSeconds = 2f;

        private class Entry
        {
            public readonly GrowZoneStatus Status = new GrowZoneStatus();
            public bool Primed;
            public float Elapsed;
            public float LastObserved;
        }

        private static readonly Dictionary<Zone_Growing, Entry> Entries =
            new Dictionary<Zone_Growing, Entry>();

        /// <summary>
        /// Drops every cached entry. Called when the tab opens, both to show current figures
        /// immediately and to release zones the player has since deleted -- nothing else prunes
        /// this dictionary, so it would otherwise hold deleted zones alive for the session.
        /// </summary>
        public static void Clear() => Entries.Clear();

        public static GrowZoneStatus For(Zone_Growing zone)
        {
            if (!Entries.TryGetValue(zone, out Entry entry))
            {
                entry = new Entry();
                Entries[zone] = entry;
            }

            float now = Time.realtimeSinceStartup;

            if (!entry.Primed)
            {
                Rebuild(zone, entry, now);
                return entry.Status;
            }

            float delta = now - entry.LastObserved;
            entry.LastObserved = now;

            // Accumulate only while the game is running: nothing these figures describe changes
            // while paused, so the interval freezes rather than draining. delta <= 0 guards a
            // clock that ran backwards.
            if (delta > 0f && !GamePaused)
                entry.Elapsed += delta;

            if (entry.Elapsed >= RefreshSeconds)
                Rebuild(zone, entry, now);

            return entry.Status;
        }

        private static bool GamePaused => Find.TickManager == null || Find.TickManager.Paused;

        private static void Rebuild(Zone_Growing zone, Entry entry, float now)
        {
            entry.Elapsed = 0f;
            entry.LastObserved = now;
            entry.Primed = true;

            GrowZoneStatus status = entry.Status;
            status.PlantCount = 0;
            status.HarvestablePlants = 0;
            status.YieldNow = 0;
            status.YieldAtMaturity = 0;
            status.AverageGrowth = 0f;
            status.HasTemperature = false;

            Map map = zone.Map;
            status.Plant = zone.GetPlantDefToGrow();
            if (map == null)
                return;

            status.HasTemperature =
                GenTemperature.TryGetTemperatureForCell(zone.Position, map, out status.Temperature);

            if (status.Plant == null)
                return;

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
        }

        /// <summary>
        /// The difficulty's crop multiplier, which <see cref="Plant.YieldNow"/> already applies.
        /// Included in the maturity estimate so the two figures are on the same scale.
        /// </summary>
        private static float CropYieldFactor =>
            Find.Storyteller?.difficulty?.cropYieldFactor ?? 1f;
    }
}
