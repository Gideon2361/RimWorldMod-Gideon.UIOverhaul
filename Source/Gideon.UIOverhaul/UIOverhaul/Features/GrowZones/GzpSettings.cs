using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    public class GzpSettings : ModSettings
    {
        /// <summary>
        /// Lifts the fertility requirement on growing zones. Cells with no ground at all -- open
        /// space on an orbital map -- are still refused, because a zone there has nothing to sit on.
        /// </summary>
        public bool allowZonesAnywhere;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref allowZonesAnywhere, "allowZonesAnywhere", false);
        }
    }

    public static class GzpTerrain
    {
        /// <summary>
        /// True when the cell has something to grow on. The only case that fails is genuinely empty
        /// space on an orbital map; substructure, platforms and every ordinary terrain all pass.
        /// </summary>
        public static bool HasGround(IntVec3 c, Map map)
        {
            if (map == null || !c.InBounds(map))
                return false;

            TerrainDef terrain = c.GetTerrain(map);
            return terrain != null && terrain != TerrainDefOf.Space;
        }
    }

    public static class GzpZonePlacement
    {
        private static AccessTools.FieldRef<Designator_ZoneAdd, Type> zoneTypeToPlaceRef;
        private static bool refResolved;

        /// <summary>
        /// The placement test used in place of the vanilla one while "draw anywhere" is on.
        ///
        /// What it drops is policy: the fertility minimum, and things that normally refuse to share
        /// a cell with a zone. What it keeps are the rules that are structurally or practically
        /// required -- the cell has to exist and be discovered, a cell cannot belong to two zones at
        /// once because the zone grid stores one zone per cell, the map-edge band stays reserved,
        /// and open space on an orbital map has nothing to grow on.
        ///
        /// Returns null when the designator's zone type could not be read, which tells the caller
        /// to fall back to vanilla behavior rather than guess.
        /// </summary>
        public static AcceptanceReport? CanDesignateAnywhere(Designator_ZoneAdd designator, IntVec3 c)
        {
            Type zoneType = ZoneTypeToPlace(designator);
            if (zoneType == null)
                return null;

            Map map = designator.Map;
            if (map == null || !c.InBounds(map))
                return false;

            // Undiscovered ground. Vanilla rejects it without a message, since the player is not
            // supposed to know what is there yet.
            if (c.Fogged(map))
                return false;

            // The reserved band around the map edge, same width vanilla uses. Zones drawn into it
            // interfere with map entry and the edge is not really usable ground.
            if (c.InNoZoneEdgeArea(map))
                return new AcceptanceReport("TooCloseToMapEdge".Translate());

            // IsInstanceOfType rather than an exact type match: our growing zones are a subclass of
            // Zone_Growing, and vanilla's exact comparison would refuse to draw over one.
            Zone existing = map.zoneManager.ZoneAt(c);
            if (existing != null && !zoneType.IsInstanceOfType(existing))
                return false;

            return GzpTerrain.HasGround(c, map)
                ? AcceptanceReport.WasAccepted
                : new AcceptanceReport("There is no ground here to grow on.");
        }

        private static Type ZoneTypeToPlace(Designator_ZoneAdd designator)
        {
            if (!refResolved)
            {
                refResolved = true;
                try
                {
                    zoneTypeToPlaceRef =
                        AccessTools.FieldRefAccess<Designator_ZoneAdd, Type>("zoneTypeToPlace");
                }
                catch (Exception ex)
                {
                    Log.Error("[Gideon.UIOverhaul] Could not read Designator_ZoneAdd.zoneTypeToPlace; "
                              + "the 'draw anywhere' setting will be ignored.\n" + ex);
                }
            }

            return zoneTypeToPlaceRef == null ? null : zoneTypeToPlaceRef(designator);
        }
    }
}
