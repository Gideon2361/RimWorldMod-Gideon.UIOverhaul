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
        
        private void ProcessZonesAndThings()
        {
            foreach (Zone allZone in map.zoneManager.AllZones)
            {
                if (allZone is Zone_GrowingPlus zoneGrowingPlus)
                    zoneGrowingPlus.ZoneTick();
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