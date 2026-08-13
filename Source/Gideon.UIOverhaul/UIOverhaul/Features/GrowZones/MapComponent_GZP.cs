using Gideon.UIFramework.Helpers;
using System.Collections.Generic;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    public class MapComponentGzp(Map map) : MapComponent(map)
    {
        private int _tickCounter = 0;
        private const int TickInterval = 180;
        
        public override void MapComponentTick()
        {
            ++_tickCounter;
            if (_tickCounter < TickInterval)
                return;
            ProcessZonesAndThings();
            _tickCounter = 0;
        }
        
        /// <summary>
        /// <b>Guarded per zone, not once around the loop.</b> Vanilla's MapComponentUtility already catches around
        /// this whole component, so an escape would not stop the map ticking -- but it would abandon the loop, so one
        /// bad zone would stop every zone after it in the list from ticking at all, and it would report it every 180
        /// ticks for the rest of the session.
        ///
        /// Guarding each zone keeps the others ticking and lets the flood control do its work.
        /// </summary>
        private void ProcessZonesAndThings()
        {
            List<Zone> zones = map?.zoneManager?.AllZones;

            if (zones == null)
                return;

            foreach (Zone allZone in zones)
            {
                if (allZone is Zone_GrowingPlus zoneGrowingPlus)
                    UIGuard.Try("GrowZones.ZoneTick", zoneGrowingPlus.ZoneTick,
                        "One growing zone's bills stop updating their progress and suspend state. Other zones "
                        + "are unaffected.");
            }

            // makes no sense -- this does nothing???
            /*foreach (Thing spawnedThing in map.spawnedThings)
            {
                if (!(spawnedThing is Building building) || !(building is Building_PlantGrowerPlus))
                    continue;
            }*/
        }
    }
}