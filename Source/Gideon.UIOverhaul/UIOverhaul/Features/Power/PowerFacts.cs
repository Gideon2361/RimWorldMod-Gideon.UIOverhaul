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

    /// <summary>One battery, and how much of it is full.</summary>
    internal struct BatteryRow
    {
        internal string name;
        internal float stored;
        internal float capacity;

        /// <summary>Whether it is switched on. A battery flicked off still holds its charge and gives none.</summary>
        internal bool on;
    }

    /// <summary>One generator, and how long its own tank lasts.</summary>
    internal struct BurnerRow
    {
        internal string name;
        internal float held;
        internal float capacity;
        internal float perDay;

        /// <summary>Days until this one is dry, or a negative when it is not burning.</summary>
        internal float days;
    }

    /// <summary>One kind of fuel the grid burns, with every burner of it under it.</summary>
    internal struct FuelRow
    {
        internal ThingDef kind;
        internal string name;

        /// <summary>Held across every burner of this fuel on the grid.</summary>
        internal float held;

        internal float capacity;

        /// <summary>Units a day, counting only the burners actually consuming.</summary>
        internal float perDay;

        /// <summary>Days until the grid runs out of this, or a negative when nothing is burning it.</summary>
        internal float days;

        internal List<BurnerRow> burners;
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

        // -------------------------------------------------------------------------------------------
        // Fuel
        /// <summary>
        /// Every battery on the grid, emptiest first.
        ///
        /// <b>Emptiest first because that is the one that stops carrying.</b> A bank drains together but does
        /// not always fill together: a battery built later, or disconnected for a while, sits lower than the
        /// rest and is the first to leave the countdown short.
        /// </summary>
        internal static List<BatteryRow> Batteries(PowerNet net, List<BatteryRow> into)
        {
            into.Clear();

            List<CompPowerBattery> cells = UIGuard.Try("Power.Batteries", () => net?.batteryComps, null, null);

            for (int i = 0; cells != null && i < cells.Count; i++)
            {
                CompPowerBattery cell = cells[i];

                if (cell?.parent == null || cell.Props == null)
                    continue;

                into.Add(new BatteryRow
                {
                    name = UIGuard.Try("Power.BatteryName",
                        () => cell.parent.LabelCapNoCount.ToString(), "Battery", null),
                    stored = UIGuard.Try("Power.BatteryStored", () => cell.StoredEnergy, 0f, null),
                    capacity = cell.Props.storedEnergyMax,
                    on = UIGuard.Try("Power.BatteryOn", () =>
                    {
                        CompFlickable flick = cell.parent.TryGetComp<CompFlickable>();

                        return flick == null || flick.SwitchIsOn;
                    }, true, null)
                });
            }

            into.Sort((a, b) =>
            {
                float mine = a.capacity > 0f ? a.stored / a.capacity : 0f;
                float theirs = b.capacity > 0f ? b.stored / b.capacity : 0f;

                return mine.CompareTo(theirs);
            });

            return into;
        }

        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// What a grid is burning, and how long each burner has left.
        ///
        /// <b>Grouped by what goes in rather than by what it powers.</b> A colony stocks chemfuel and wood,
        /// not "the generator by the east wall", and the question behind opening this is always whether one of
        /// those two is about to run out. Generators are listed under their fuel so the answer and the things
        /// giving it are in the same place.
        ///
        /// <b>Only burners that are actually consuming are counted into the rate.</b> A generator switched off,
        /// or one whose props say it only burns while powered or in use, is holding its fuel rather than
        /// spending it, and folding it into the rate would give a countdown that never comes true.
        /// </summary>
        internal static List<FuelRow> Fuels(PowerNet net, List<FuelRow> into)
        {
            into.Clear();

            List<CompPowerTrader> traders = UIGuard.Try("Power.Traders", () => net?.powerComps, null, null);

            Dictionary<ThingDef, int> seen = new Dictionary<ThingDef, int>();

            for (int i = 0; traders != null && i < traders.Count; i++)
            {
                CompPowerTrader trader = traders[i];

                if (trader?.parent == null)
                    continue;

                CompRefuelable fuel = UIGuard.Try("Power.FuelComp",
                    trader.parent.TryGetComp<CompRefuelable>, null, null);

                if (fuel?.Props == null || fuel.Props.fuelCapacity <= 0f)
                    continue;

                ThingDef kind = Burns(fuel);

                if (kind == null)
                    continue;

                int at;

                if (!seen.TryGetValue(kind, out at))
                {
                    seen[kind] = into.Count;

                    into.Add(new FuelRow
                    {
                        kind = kind,
                        name = kind.LabelCap,
                        burners = new List<BurnerRow>()
                    });

                    at = into.Count - 1;
                }

                FuelRow row = into[at];

                float held = UIGuard.Try("Power.FuelHeld", () => fuel.Fuel, 0f, null);
                float rate = Rate(fuel, trader);

                row.held += held;
                row.capacity += fuel.Props.fuelCapacity;
                row.perDay += rate;

                row.burners.Add(new BurnerRow
                {
                    name = trader.parent.LabelCapNoCount,
                    held = held,
                    capacity = fuel.Props.fuelCapacity,
                    perDay = rate,
                    days = rate > 0f ? held / rate : -1f
                });

                into[at] = row;
            }

            for (int i = 0; i < into.Count; i++)
            {
                FuelRow row = into[i];

                row.days = row.perDay > 0f ? row.held / row.perDay : -1f;

                row.burners.Sort((a, b) =>
                {
                    // Whatever runs out first is what somebody wants to see, and a burner that is not
                    // consuming has no answer to that and belongs at the bottom rather than at the top.
                    if (a.days < 0f && b.days < 0f)
                        return 0;

                    if (a.days < 0f)
                        return 1;

                    if (b.days < 0f)
                        return -1;

                    return a.days.CompareTo(b.days);
                });

                into[i] = row;
            }

            into.Sort((a, b) => b.perDay.CompareTo(a.perDay));

            return into;
        }

        /// <summary>
        /// What this thing burns.
        ///
        /// <b>Read off the filter, which is where the game keeps it.</b> A generator's fuel is whatever its
        /// <c>fuelFilter</c> allows, so a modded burner naming a modded fuel is answered correctly without
        /// this knowing anything about either. A filter allowing several things reports the first, because a
        /// row headed by one of two interchangeable fuels is still a truer heading than none.
        /// </summary>
        private static ThingDef Burns(CompRefuelable fuel)
        {
            return UIGuard.Try("Power.FuelKind", () =>
            {
                ThingFilter filter = fuel.Props.fuelFilter;

                if (filter == null)
                    return null;

                foreach (ThingDef allowed in filter.AllowedThingDefs)
                    return allowed;

                return null;
            }, null, null);
        }

        /// <summary>
        /// How fast this one is burning, in units a day, or zero when it is not burning at all.
        ///
        /// <b>The conditions are the props' own.</b> <c>consumeFuelOnlyWhenPowered</c> and
        /// <c>consumeFuelOnlyWhenUsed</c> both exist, both are false on the ordinary generators and true on
        /// several modded ones, and a rate that ignored them would count fuel as spent that is not moving.
        /// </summary>
        private static float Rate(CompRefuelable fuel, CompPowerTrader trader)
        {
            return UIGuard.Try("Power.FuelRate", () =>
            {
                if (!fuel.HasFuel)
                    return 0f;

                CompFlickable flick = trader.parent.TryGetComp<CompFlickable>();

                if (flick != null && !flick.SwitchIsOn)
                    return 0f;

                if (fuel.Props.consumeFuelOnlyWhenPowered && !trader.PowerOn)
                    return 0f;

                return fuel.Props.fuelConsumptionRate;
            }, 0f, null);
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

        /// <summary>Days as something readable, or a dash when nothing is burning it.</summary>
        internal static string Days(float days)
        {
            if (days < 0f)
                return "not burning";

            if (days < 1f)
                return Mathf.RoundToInt(days * 24f) + "h left";

            return days.ToString("0.#") + "d left";
        }

        /// <summary>Watts with a sign and a separator, which is how the game writes them.</summary>
        internal static string Power(float watts)
        {
            return (watts > 0f ? "+" : string.Empty) + Mathf.RoundToInt(watts).ToString("N0") + " W";
        }
    }
}
