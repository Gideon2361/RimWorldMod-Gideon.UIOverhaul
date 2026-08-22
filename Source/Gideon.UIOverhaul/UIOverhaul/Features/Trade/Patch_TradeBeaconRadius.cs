using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade
{
    /// <summary>
    /// Makes the trade beacon's radius the player's number instead of RimWorld's constant.
    ///
    /// <b>One method decides everything about a beacon's reach.</b>
    /// <c>Building_OrbitalTradeBeacon.TradeableCellsAround</c> is what the beacon's own <c>TradeableCells</c>
    /// reads, what <c>TradeUtility.AllLaunchableThingsForTrade</c> walks to decide what can be sold, what the
    /// Make matching stockpile button designates, and what <c>PlaceWorker_ShowTradeBeaconRadius</c> draws the
    /// outline from while a beacon is being placed. Replace it and the number, the sale and the picture move
    /// together; patch anything else and two of the three disagree.
    ///
    /// <b>A prefix that stands in for the method, rather than a transpiler.</b> The radius is a private const, so
    /// there is no field to set: the compiler has baked 7.9 into the code as a literal, and it is baked into the
    /// closure the region walk is handed rather than into the method itself. Reaching that literal would mean
    /// naming a compiler generated method like <c>&lt;TradeableCellsAround&gt;b__1</c>, which is a name the
    /// compiler is free to change without RimWorld changing at all. Twenty lines that use only public API are the
    /// steadier bet.
    ///
    /// <b>It is vanilla's algorithm, deliberately.</b> Breadth first from the beacon's own region, refusing to
    /// cross a door, taking every cell within the radius. Not a simple circle: the door rule is what stops a
    /// beacon selling through a wall into the next room, and reproducing the walk keeps that property rather than
    /// quietly widening what a beacon can reach.
    ///
    /// <b>The region cap moves with the radius, and forgetting it would have been the quiet bug.</b> Vanilla
    /// stops the walk after sixteen regions, which is generous at a radius of eight and not enough at
    /// twenty four: the ring would have been drawn at the size the player asked for while cells past the
    /// sixteenth region silently refused to sell. See <see cref="TradeBeaconRadius.MaxRegions"/>.
    ///
    /// <b>Anything unexpected falls back to RimWorld's own method.</b> The guard returns true on a throw, which
    /// runs the original: a beacon at vanilla's radius is a working beacon.
    /// </summary>
    [HarmonyPatch(typeof(Building_OrbitalTradeBeacon),
        nameof(Building_OrbitalTradeBeacon.TradeableCellsAround))]
    internal static class Patch_TradeBeaconRadius
    {
        /// <summary>
        /// Ours, cleared per call.
        ///
        /// A static scratch list because that is what vanilla returns too: callers read it and move on, and one
        /// of them asks per beacon per trade window. Nothing may hold onto it, which was already true.
        /// </summary>
        private static readonly List<IntVec3> Cells = new List<IntVec3>();

        /// <summary>
        /// <paramref name="__result"/> is written after the guard rather than inside it, because a ref parameter
        /// cannot be touched from a lambda and the guard's body is one. The same shape the animals tab's tab
        /// redirect uses, for the same compiler reason.
        /// </summary>
        public static bool Prefix(IntVec3 pos, Map map, ref List<IntVec3> __result)
        {
            List<IntVec3> found = null;

            bool handBack = UIGuard.Replaced("Trade.BeaconCells", () => found = Around(pos, map),
                "The trade beacon radius setting is not in force for this beacon, so it works at RimWorld's own "
                + "radius.");

            if (handBack || found == null)
                return true;

            __result = found;

            return false;
        }

        private static List<IntVec3> Around(IntVec3 pos, Map map)
        {
            Cells.Clear();

            if (map == null || !pos.InBounds(map))
                return Cells;

            Region region = pos.GetRegion(map);

            if (region == null)
                return Cells;

            float radius = TradeBeaconRadius.Radius;

            RegionTraverser.BreadthFirstTraverse(region, (from, r) => r.door == null, r =>
            {
                foreach (IntVec3 cell in r.Cells)
                {
                    if (cell.InHorDistOf(pos, radius))
                        Cells.Add(cell);
                }

                return false;
            }, TradeBeaconRadius.MaxRegions);

            return Cells;
        }
    }
}
