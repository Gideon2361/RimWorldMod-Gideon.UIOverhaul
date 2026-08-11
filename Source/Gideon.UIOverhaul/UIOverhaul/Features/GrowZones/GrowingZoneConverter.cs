using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    /// <summary>
    /// Replaces plain vanilla growing zones with <see cref="Zone_GrowingPlus"/> on map load.
    ///
    /// Only zones created after the mod was installed went through the MakeNewZone patch, so a save
    /// that predates the mod keeps ordinary Zone_Growing zones. Those are invisible to the sow gate
    /// in PatchWorkGiverGrowerSowJobOnCell -- it tests `is Zone_GrowingPlus` and falls through to
    /// vanilla otherwise -- so they sow with no bill requirement at all.
    ///
    /// A zone's type cannot be changed in place, so each one is snapshotted, deleted and rebuilt.
    /// </summary>
    public static class GrowingZoneConverter
    {
        public static void ConvertAll(Map map)
        {
            List<Zone_Growing> pending = new List<Zone_Growing>();

            foreach (Zone zone in map.zoneManager.AllZones)
            {
                // Exact type only. Zone_GrowingPlus is itself a Zone_Growing, and other mods may
                // ship their own subclasses -- neither should be touched.
                if (zone.GetType() == typeof(Zone_Growing))
                    pending.Add((Zone_Growing) zone);
            }

            if (pending.Count == 0)
                return;

            int converted = 0;
            foreach (Zone_Growing old in pending)
            {
                if (Convert(map, old))
                    converted++;
            }

            Log.Message($"[Gideon.UIOverhaul] Converted {converted} vanilla growing zone(s) to "
                        + $"Growing Zones Plus zones on map {map.Index}.");
        }

        private static bool Convert(Map map, Zone_Growing old)
        {
            List<IntVec3> cells = new List<IntVec3>(old.cells);
            if (cells.Count == 0)
                return false;

            string label = old.label;
            Color color = old.color;
            bool hidden = old.Hidden;
            bool allowSow = old.allowSow;
            bool allowCut = old.allowCut;
            ThingDef plantDef = old.GetPlantDefToGrow();

            // Frees the cells in the zone grid before the replacement claims them.
            old.Delete(false);

            Zone_GrowingPlus converted = new Zone_GrowingPlus(map.zoneManager);
            map.zoneManager.RegisterZone(converted);

            // AddCell is overridden to raise the "important plant will be cut" warning; converting a
            // large zone would fire it once per thing per cell.
            Zone_GrowingPlus.SuppressCutWarnings = true;
            try
            {
                foreach (IntVec3 cell in cells)
                    converted.AddCell(cell);
            }
            finally
            {
                Zone_GrowingPlus.SuppressCutWarnings = false;
            }

            // The Zone constructor auto-generates a name, so identity is restored afterwards.
            converted.label = label;
            converted.color = color;
            converted.Hidden = hidden;
            converted.allowSow = allowSow;
            converted.allowCut = allowCut;

            // A vanilla growing zone should always have a crop, but fall back to potatoes if it
            // somehow does not -- that is what RimWorld defaults a new plot to anyway, and it keeps
            // every converted zone gated on a bill rather than carving out an exception.
            if (plantDef == null)
                plantDef = ThingDefOf.Plant_Potato;

            converted.SetPlantDefToGrow(plantDef);
            converted.BillStack.AddBill(new Bill_Growing(plantDef));

            return true;
        }
    }
}
