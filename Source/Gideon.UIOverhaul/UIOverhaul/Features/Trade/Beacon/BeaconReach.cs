using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade.Beacon
{
    /// <summary>
    /// What one trade beacon actually reaches, and what that reach is worth.
    ///
    /// <b>Vanilla draws nothing at all for a built beacon.</b> The radius is shown once, as a placement ghost,
    /// and then never again -- so "why is this not for sale" is answered by counting tiles by hand, and "was
    /// widening the radius worth it" is not answered at all. Every number below is read from the same walk the
    /// game itself sells through, so a stack this calls sellable is a stack the trade window will list.
    ///
    /// <b>Three facts that only a scan can produce.</b> How many stacks are inside the ring but behind a wall,
    /// because the beacon's walk refuses to cross a door and that rule is invisible; how close the walk is to the
    /// region limit it stops at, which is a real failure mode -- past it the ring is drawn at the size you asked
    /// for and sells nothing extra; and what the covered stacks are worth, which is the number that says whether
    /// the radius setting bought anything.
    /// </summary>
    internal class BeaconScan
    {
        /// <summary>Cells the beacon can sell from.</summary>
        internal readonly HashSet<IntVec3> Cells = new HashSet<IntVec3>();

        /// <summary>Cells inside the radius that the walk could not reach, which is the door rule made visible.</summary>
        internal readonly HashSet<IntVec3> Blocked = new HashSet<IntVec3>();

        /// <summary>Stacks the beacon could sell right now.</summary>
        internal readonly List<Thing> Sellable = new List<Thing>();

        /// <summary>Stacks inside the ring that the beacon cannot reach.</summary>
        internal readonly List<Thing> WalledOff = new List<Thing>();

        /// <summary>What the reachable stacks are worth, by our own trade category.</summary>
        internal readonly Dictionary<string, float> ByCategory = new Dictionary<string, float>();

        internal float Value;

        internal float WalledOffValue;

        /// <summary>How many regions the walk crossed, against the cap it would have stopped at.</summary>
        internal int Regions;

        internal int RegionCap;

        internal float Radius;

        /// <summary>Whether the walk stopped because it ran out of budget rather than out of ground.</summary>
        internal bool Truncated
        {
            get { return Regions >= RegionCap; }
        }

        /// <summary>Whether the beacon has power, and so whether any of this is currently true.</summary>
        internal bool Powered;
    }

    /// <summary>
    /// Scans a beacon.
    ///
    /// <b>The reachable walk is vanilla's, and the blocked set is the difference between it and a plain circle.</b>
    /// That subtraction is the whole trick: the game gives no list of what a beacon cannot reach, but a circle is
    /// trivial and the reachable set is already computed, so what is inside one and not the other is exactly the
    /// set the door rule excluded. Nothing about the rule is reproduced here -- it is measured.
    ///
    /// <b>Region counting needs its own traverse,</b> because the one that produces the cells returns only cells.
    /// It is the same traverse with the same door predicate and the same cap, counting instead of collecting, and
    /// it is what lets the meter say 21 of 24 rather than leaving the cap as a fact nobody can act on.
    /// </summary>
    internal static class BeaconReach
    {
        /// <summary>
        /// Scans, reusing <paramref name="into"/> so a window redrawing on a timer allocates nothing.
        /// </summary>
        internal static BeaconScan Scan(Building_OrbitalTradeBeacon beacon, BeaconScan into)
        {
            if (into == null)
                into = new BeaconScan();

            Clear(into);

            if (beacon == null || beacon.Map == null || !beacon.Spawned)
                return into;

            UIGuard.Try("Trade.BeaconScan", () => Walk(beacon, into),
                "This beacon's reach could not be scanned. The beacon itself works normally.");

            return into;
        }

        private static void Clear(BeaconScan scan)
        {
            scan.Cells.Clear();
            scan.Blocked.Clear();
            scan.Sellable.Clear();
            scan.WalledOff.Clear();
            scan.ByCategory.Clear();

            scan.Value = 0f;
            scan.WalledOffValue = 0f;
            scan.Regions = 0;
            scan.RegionCap = 0;
            scan.Radius = 0f;
            scan.Powered = false;
        }

        private static void Walk(Building_OrbitalTradeBeacon beacon, BeaconScan scan)
        {
            Map map = beacon.Map;

            scan.Radius = TradeBeaconRadius.Radius;
            scan.RegionCap = TradeBeaconRadius.MaxRegions;

            CompPowerTrader power = beacon.TryGetComp<CompPowerTrader>();

            scan.Powered = power == null || power.PowerOn;

            // The beacon's own property, so this reads exactly what the game sells through -- including our own
            // radius prefix, which is the point: a readout computed a second way would eventually disagree with
            // the thing it is describing.
            foreach (IntVec3 cell in beacon.TradeableCells)
                scan.Cells.Add(cell);

            scan.Regions = CountRegions(beacon.Position, map, scan.Radius, scan.RegionCap);

            int radialCells = GenRadial.NumCellsInRadius(scan.Radius);

            for (int i = 0; i < radialCells; i++)
            {
                IntVec3 cell = beacon.Position + GenRadial.RadialPattern[i];

                if (!cell.InBounds(map) || scan.Cells.Contains(cell))
                    continue;

                scan.Blocked.Add(cell);
            }

            Collect(map, scan.Cells, scan.Sellable, scan);
            Collect(map, scan.Blocked, scan.WalledOff, null);

            for (int i = 0; i < scan.WalledOff.Count; i++)
                scan.WalledOffValue += Worth(scan.WalledOff[i]);
        }

        /// <summary>
        /// Vanilla's own traverse, counting regions instead of collecting cells.
        ///
        /// Same seed region, same door predicate and same cap as
        /// <c>Building_OrbitalTradeBeacon.TradeableCellsAround</c> and our replacement of it, so the number it
        /// returns is the number the real walk spent. A region entirely outside the radius still counts, because
        /// the walk still visited it -- which is exactly what the cap is spent on and why a beacon in a warren of
        /// small rooms runs out sooner than one in a warehouse.
        /// </summary>
        private static int CountRegions(IntVec3 position, Map map, float radius, int cap)
        {
            Region start = position.GetRegion(map);

            if (start == null)
                return 0;

            int visited = 0;

            RegionTraverser.BreadthFirstTraverse(start, (from, region) => region.door == null, region =>
            {
                visited++;

                return false;
            }, cap);

            return visited;
        }

        /// <summary>
        /// Everything in a set of cells that a trader would take.
        /// </summary>
        /// <param name="tally">Where to add the value and the per-category split, or null to skip the tally.</param>
        /// <remarks>
        /// <b>Items only, and pawns deliberately skipped.</b> <c>TradeUtility.PlayerSellableNow</c> takes a trader
        /// and dereferences it for a pawn -- checking host and home factions -- so asking it about a pawn with no
        /// trader would throw. Outside a trade session there is no trader to name, and a beacon's worth is about
        /// goods anyway: an animal standing on a beacon is not stock.
        ///
        /// This walks the same categories vanilla's <c>AllLaunchableThingsForTrade</c> does for a plain stack, and
        /// stops there. Gene banks, bookcases and outfit stands hold sellable things inside themselves, and each
        /// would need its own unwrapping; leaving them out undercounts a beacon rather than overcounting it,
        /// which is the safer way to be wrong about a number a player is deciding on.
        /// </remarks>
        private static void Collect(Map map, HashSet<IntVec3> cells, List<Thing> into, BeaconScan tally)
        {
            foreach (IntVec3 cell in cells)
            {
                List<Thing> things = cell.GetThingList(map);

                for (int i = 0; things != null && i < things.Count; i++)
                {
                    Thing thing = things[i];

                    if (thing == null || thing is Pawn || thing.def == null
                        || thing.def.category != ThingCategory.Item)
                        continue;

                    if (!TradeUtility.PlayerSellableNow(thing, null))
                        continue;

                    into.Add(thing);

                    if (tally == null)
                        continue;

                    float worth = Worth(thing);

                    tally.Value += worth;

                    string category = CategoryOf(thing);

                    float held;

                    tally.ByCategory[category] = tally.ByCategory.TryGetValue(category, out held)
                        ? held + worth
                        : worth;
                }
            }
        }

        private static float Worth(Thing thing)
        {
            return UIGuard.Try("Trade.BeaconWorth", () => thing.MarketValue * thing.stackCount, 0f, null);
        }

        /// <summary>
        /// The same categories the trade window's rail uses, so the two screens file a thing the same way.
        ///
        /// Built through a throwaway is not possible -- <c>TradeCatalog.CategoryOf</c> takes a <c>Tradeable</c>,
        /// which only exists inside a session -- so the def tests are asked directly here. Kept deliberately
        /// short and in the same order as that method, and it is the one thing in this feature that is written
        /// twice; the alternative was a <c>Tradeable</c> fabricated per stack per scan.
        /// </summary>
        private static string CategoryOf(Thing thing)
        {
            ThingDef def = thing.def;

            if (def.IsMedicine)
                return "medicine";

            if (def.IsDrug)
                return "drugs";

            if (def.IsNutritionGivingIngestible)
                return "food";

            if (def.IsWeapon)
                return "weapons";

            if (def.IsApparel)
                return "apparel";

            return def.CountAsResource || def.IsStuff ? "resources" : "other";
        }

        /// <summary>Every powered beacon on a map, which is the set the trade window actually sells through.</summary>
        internal static List<Building_OrbitalTradeBeacon> AllOn(Map map, List<Building_OrbitalTradeBeacon> into)
        {
            into.Clear();

            if (map == null)
                return into;

            UIGuard.Try("Trade.BeaconList", () =>
            {
                // Every beacon, not only the powered ones: an unpowered beacon is exactly the thing somebody
                // opens this readout to find, and AllPowered would hide it.
                foreach (Building_OrbitalTradeBeacon beacon in
                         map.listerBuildings.AllBuildingsColonistOfClass<Building_OrbitalTradeBeacon>())
                    into.Add(beacon);
            }, null);

            return into;
        }
    }
}
