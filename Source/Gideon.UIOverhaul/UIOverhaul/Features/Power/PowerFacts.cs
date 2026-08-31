using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Power
{
    /// <summary>One kind of building on a grid, summed: eighteen lamps as one row.</summary>
    internal struct DrawRow
    {
        internal ThingDef def;
        internal string name;

        /// <summary>How many of them are wired to this grid.</summary>
        internal int count;

        /// <summary>Watts, positive for a producer and negative for a consumer.</summary>
        internal float watts;

        /// <summary>How many of them are switched off or otherwise contributing nothing.</summary>
        internal int idle;
    }

    /// <summary>Something on the grid that wants attention rather than counting.</summary>
    internal struct FaultRow
    {
        internal string name;
        internal string state;
        internal string detail;

        /// <summary>Whether this is a fault rather than a warning, for the colour.</summary>
        internal bool severe;
    }

    /// <summary>One power grid, read as a whole.</summary>
    internal struct GridRow
    {
        internal PowerNet net;
        internal string name;

        /// <summary>Producers, consumers and batteries. Conduits are not counted; nobody thinks in conduits.</summary>
        internal int buildings;

        /// <summary>Net gain in watts. Negative is a deficit.</summary>
        internal float balance;

        internal float producing;
        internal float drawing;

        internal float stored;
        internal float capacity;

        /// <summary>Whether anything on it can generate at all.</summary>
        internal bool hasSource;

        /// <summary>Hours until the batteries are flat, or a negative when nothing is draining them.</summary>
        internal float hoursLeft;
    }

    /// <summary>
    /// The read side of the power tab.
    ///
    /// <b>All of it is on <c>PowerNet</c> already.</b> It holds every trader and battery wired to it and
    /// answers <c>CurrentEnergyGainRate</c> and <c>CurrentStoredEnergy</c> every tick because the simulation
    /// needs them; the map holds every net in <c>powerNetManager.AllNetsListForReading</c>. What nothing in
    /// the game does is put the two together and divide, and stored over deficit is the hours until the
    /// lights go out.
    ///
    /// <b>Everything is guarded.</b> A net carries comps from whatever mods are installed, and any one of
    /// them can throw on a property this screen reads.
    /// </summary>
    internal static class PowerFacts
    {
        /// <summary>Which grid the panel is showing, held across frames by its first transmitter.</summary>
        internal static PowerNet Selected;

        private static readonly List<GridRow> Grids = new List<GridRow>();

        /// <summary>
        /// Watts per unit of the game's own energy rate.
        ///
        /// <c>CurrentEnergyGainRate</c> answers in watt-days per tick, and this is the constant
        /// <c>CompPower</c> divides by to get watts back for its own inspect string. Reading the game's
        /// constant rather than writing 60000 keeps every figure on this tab equal to the ones in the pane.
        /// </summary>
        private static float Watts(float ratePerTick)
        {
            return ratePerTick / CompPower.WattsToWattDaysPerTick;
        }

        /// <summary>Every grid on the map, largest first.</summary>
        internal static List<GridRow> All(Map map)
        {
            Grids.Clear();

            if (map == null)
                return Grids;

            List<PowerNet> nets = UIGuard.Try("Power.Nets",
                () => map.powerNetManager?.AllNetsListForReading, null, null);

            for (int i = 0; nets != null && i < nets.Count; i++)
            {
                PowerNet net = nets[i];

                if (net == null)
                    continue;

                GridRow row = Read(net);

                // A net of nothing but conduit is a wire somebody ran and has not built on yet. It is not a
                // grid in any sense the reader cares about, and listing them buries the real ones.
                if (row.buildings > 0)
                    Grids.Add(row);
            }

            Grids.Sort((a, b) => b.buildings.CompareTo(a.buildings));

            for (int i = 0; i < Grids.Count; i++)
            {
                GridRow row = Grids[i];

                // Named by position rather than by content, because a PowerNet has no name and nothing on it
                // is stable enough to borrow one from: the biggest producer can be deconstructed, and then
                // every grid in the list renames itself. A number that follows size is at least predictable.
                row.name = "Grid " + (i + 1);

                Grids[i] = row;
            }

            return Grids;
        }

        /// <summary>One grid, read in full.</summary>
        internal static GridRow Read(PowerNet net)
        {
            GridRow row = new GridRow
            {
                net = net,
                name = "Grid",
                hasSource = UIGuard.Try("Power.HasSource", () => net.hasPowerSource, false, null),
                balance = UIGuard.Try("Power.Balance", () => Watts(net.CurrentEnergyGainRate()), 0f, null),
                stored = UIGuard.Try("Power.Stored", () => net.CurrentStoredEnergy(), 0f, null)
            };

            List<CompPowerTrader> traders = UIGuard.Try("Power.Traders", () => net.powerComps, null, null);

            for (int i = 0; traders != null && i < traders.Count; i++)
            {
                CompPowerTrader trader = traders[i];

                if (trader == null)
                    continue;

                row.buildings++;

                float watts = UIGuard.Try("Power.Output", () => trader.PowerOutput, 0f, null);

                if (watts > 0f)
                    row.producing += watts;
                else
                    row.drawing -= watts;
            }

            List<CompPowerBattery> batteries = UIGuard.Try("Power.Batteries", () => net.batteryComps, null, null);

            for (int i = 0; batteries != null && i < batteries.Count; i++)
            {
                CompPowerBattery battery = batteries[i];

                if (battery == null || battery.Props == null)
                    continue;

                row.buildings++;
                row.capacity += battery.Props.storedEnergyMax;
            }

            // Hours over the deficit, which is the whole point of the screen. Negative when nothing is being
            // taken out, because "how long until empty" has no answer on a grid that is filling.
            row.hoursLeft = row.balance < 0f && row.stored > 0f
                ? row.stored / -row.balance * 24f
                : -1f;

            return row;
        }

        /// <summary>
        /// What the grid is made of, grouped by kind and ranked by watts.
        ///
        /// <b>Grouped on the def rather than listed one by one.</b> A colony has eighteen of something, and a
        /// list where the interesting rows are buried under repeats of the boring ones is a list nobody reads.
        /// Eighteen lamps is one row saying what those lamps cost, which is the number a decision gets made
        /// against.
        /// </summary>
        internal static List<DrawRow> Traders(PowerNet net, bool producers, List<DrawRow> into)
        {
            into.Clear();

            List<CompPowerTrader> traders = UIGuard.Try("Power.Traders", () => net?.powerComps, null, null);

            Dictionary<ThingDef, int> seen = new Dictionary<ThingDef, int>();

            for (int i = 0; traders != null && i < traders.Count; i++)
            {
                CompPowerTrader trader = traders[i];

                if (trader?.parent?.def == null)
                    continue;

                float watts = UIGuard.Try("Power.Output", () => trader.PowerOutput, 0f, null);

                // A building is a producer or a consumer by what its def asks for, not by what it happens to
                // be doing this tick: a generator out of fuel is a producer making nothing, and filing it
                // under consumers because its output is zero would be a list that reshuffles itself.
                float rated = UIGuard.Try("Power.Rated",
                    () => trader.Props != null ? trader.Props.PowerConsumption : 0f, 0f, null);

                bool makes = rated < 0f || watts > 0f;

                if (makes != producers)
                    continue;

                ThingDef def = trader.parent.def;
                int at;

                if (!seen.TryGetValue(def, out at))
                {
                    seen[def] = into.Count;

                    into.Add(new DrawRow
                    {
                        def = def,
                        name = def.LabelCap,
                        count = 1,
                        watts = watts,
                        idle = Mathf.Approximately(watts, 0f) ? 1 : 0
                    });

                    continue;
                }

                DrawRow row = into[at];

                row.count++;
                row.watts += watts;

                if (Mathf.Approximately(watts, 0f))
                    row.idle++;

                into[at] = row;
            }

            into.Sort((a, b) => Mathf.Abs(b.watts).CompareTo(Mathf.Abs(a.watts)));

            return into;
        }

        /// <summary>
        /// The things on this grid worth doing something about.
        ///
        /// <b>A grid with no source is a gap rather than a shortage,</b> and it is said in those words. Six
        /// lights wired to each other and to nothing else is a conduit somebody never joined up, and reading
        /// it as a zero on a balance sheet is how it stays unnoticed for a season.
        /// </summary>
        internal static List<FaultRow> Faults(GridRow grid, List<FaultRow> into)
        {
            into.Clear();

            if (!grid.hasSource)
            {
                into.Add(new FaultRow
                {
                    name = grid.name,
                    state = "no source",
                    detail = grid.buildings + (grid.buildings == 1 ? " building" : " buildings") + " on it",
                    severe = true
                });
            }

            List<CompPowerTrader> traders = UIGuard.Try("Power.Traders", () => grid.net?.powerComps, null, null);

            Dictionary<ThingDef, int> counted = new Dictionary<ThingDef, int>();

            for (int i = 0; traders != null && i < traders.Count; i++)
            {
                CompPowerTrader trader = traders[i];

                if (trader?.parent?.def == null)
                    continue;

                string state = State(trader);

                if (state == null)
                    continue;

                ThingDef def = trader.parent.def;
                int at;

                if (counted.TryGetValue(def, out at))
                {
                    FaultRow seen = into[at];

                    seen.detail = Tally(seen.detail);

                    into[at] = seen;

                    continue;
                }

                counted[def] = into.Count;

                into.Add(new FaultRow
                {
                    name = def.LabelCap,
                    state = state,
                    detail = "1",
                    severe = state != "out of fuel"
                });
            }

            return into;
        }

        /// <summary>Why one building is contributing nothing, or null when it is fine.</summary>
        private static string State(CompPowerTrader trader)
        {
            return UIGuard.Try("Power.State", () =>
            {
                CompRefuelable fuel = trader.parent.TryGetComp<CompRefuelable>();

                if (fuel != null && fuel.Props != null && fuel.Props.fuelCapacity > 0f && !fuel.HasFuel)
                    return "out of fuel";

                CompBreakdownable broken = trader.parent.TryGetComp<CompBreakdownable>();

                if (broken != null && broken.BrokenDown)
                    return "broken down";

                CompFlickable flick = trader.parent.TryGetComp<CompFlickable>();

                if (flick != null && !flick.SwitchIsOn)
                    return null;

                // A consumer that wants power and is not getting it. Only reported for things that draw,
                // because a generator with PowerOn false is usually one that is simply off.
                if (!trader.PowerOn && trader.Props != null && trader.Props.PowerConsumption > 0f)
                    return "unpowered";

                return null;
            }, null, null);
        }

        private static string Tally(string detail)
        {
            int had;

            return int.TryParse(detail, out had) ? (had + 1).ToString() : detail;
        }

        /// <summary>Hours as something readable, or a dash when there is no countdown to give.</summary>
        internal static string Hours(float hours)
        {
            if (hours < 0f)
                return "--";

            if (hours < 1f)
                return Mathf.RoundToInt(hours * 60f) + "m";

            if (hours < 48f)
                return Mathf.RoundToInt(hours) + "h";

            return (hours / 24f).ToString("F1") + "d";
        }

        /// <summary>Watts with a sign and a separator, which is how the game writes them.</summary>
        internal static string Power(float watts)
        {
            return (watts > 0f ? "+" : string.Empty) + Mathf.RoundToInt(watts).ToString("N0") + " W";
        }
    }
}
