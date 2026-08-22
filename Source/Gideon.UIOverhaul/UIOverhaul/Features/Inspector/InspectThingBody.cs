using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The body for anything that is not a pawn: a plant, a building, an item, a corpse.
    ///
    /// <b>This is the half of the pane that was embarrassing.</b> Selecting a colonist gave a full panel and
    /// selecting a rice plant gave one lonely health bar in the right-hand column with an empty column beside it.
    /// The problem was not that things have nothing to say -- a plant has a growth clock, food has a rot clock, a
    /// battery has a charge, a workbench has a queue -- it was that only one reader had been written.
    ///
    /// <b>So it is a set of readers rather than a layout.</b> Each one answers "does this apply to the thing in
    /// front of me", and the ones that do are placed into whichever column is currently shorter. A rice plant
    /// gets growth, yield and condition; a battery gets charge and the grid; a hopper gets its storage priority
    /// and what is in it. Nothing is invented for a thing that has nothing to say, and where that happens the
    /// pane is vanilla's inspect string in our chrome, which is a fine outcome and an honest one.
    ///
    /// <b>Filled shortest-column-first rather than left-then-right.</b> With a variable number of blocks, a fixed
    /// split leaves one column empty whenever the thing happens to lack whatever was assigned to it -- which is
    /// exactly how the rice plant ended up with its only block on the right.
    /// </summary>
    internal static class InspectThingBody
    {
        /// <summary>
        /// Two columns and how far down each has been filled.
        ///
        /// A struct held as a local, so placing a block costs nothing and there is no per-frame allocation on a
        /// panel that redraws sixty times a second.
        /// </summary>
        private struct Flow
        {
            internal Rect Left;

            internal Rect Right;

            internal float LeftY;

            internal float RightY;

            internal bool Split;

            /// <summary>The column with the most room left, and where in it the next block starts.</summary>
            internal Rect Take(out float y)
            {
                if (!Split || LeftY <= RightY)
                {
                    y = LeftY;

                    return Left;
                }

                y = RightY;

                return Right;
            }

            /// <summary>Records where a block finished.</summary>
            internal void Give(Rect column, float y)
            {
                if (!Split || column.x == Left.x)
                    LeftY = y;
                else
                    RightY = y;
            }

            internal float Bottom
            {
                get { return Mathf.Max(LeftY, RightY); }
            }
        }

        internal static float Draw(Rect view, Thing thing, UIColorPaletteDef palette)
        {
            Flow flow = new Flow();

            InspectBodies.Columns(view, out flow.Left, out flow.Right);

            flow.Split = InspectBodies.Live(flow.Right);
            flow.LeftY = view.y;
            flow.RightY = view.y;

            Rect column;
            float y;

            column = flow.Take(out y);
            flow.Give(column, Growth(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Power(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Charge(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Fuel(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Climate(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Freshness(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Work(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Storage(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Sleeping(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Condition(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Worth(column, y, thing, palette));

            return flow.Bottom - view.y;
        }

        /// <summary>
        /// A plant's clock: how grown it is, what it will give, and what is holding it back.
        ///
        /// <b>The growth bar is the whole point.</b> Vanilla writes "Growth: 68%" into the inspect string, which
        /// is a number you have to compare against another number you do not have. A bar with the harvest point
        /// on it answers "is this field ready" from across the panel, and the yield says what "ready" is worth.
        /// </summary>
        private static float Growth(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            Plant plant = thing as Plant;

            if (plant == null)
                return y;

            float growth = UIGuard.Try("Inspector.PlantGrowth", () => plant.Growth, 0f, null);
            bool ripe = UIGuard.Try("Inspector.PlantRipe", () => plant.HarvestableNow, false, null);

            y = InspectPaneParts.Cap(view, y, "Growth", ripe ? "ready" : null, palette);

            y = InspectPaneParts.Need(view, y, plant.def.LabelCap, InspectPaneParts.Percent(growth),
                ripe ? palette.Success : palette.TextSecondary, growth,
                ripe ? palette.Success : palette.Accent, null, null, palette);

            int yield = UIGuard.Try("Inspector.PlantYield", () => plant.YieldNow(), 0, null);

            if (plant.def.plant != null && plant.def.plant.harvestedThingDef != null)
                y = InspectPaneParts.Fact(view, y, plant.def.plant.harvestedThingDef.LabelCap,
                    yield > 0 ? yield.ToString() : "nothing yet",
                    yield > 0 ? palette.TextPrimary : palette.TextDisabled, palette);

            // The reasons a plant is not growing, which is the question somebody clicking a stalled crop has.
            // Each is RimWorld's own factor rather than our guess at one, so a plant that says it is short of
            // light is short of light by the game's reckoning.
            y = Factor(view, y, "Light", plant, palette, p => p.GrowthRateFactor_Light);
            y = Factor(view, y, "Temperature", plant, palette, p => p.GrowthRateFactor_Temperature);
            y = Factor(view, y, "Fertility", plant, palette, p => p.GrowthRateFactor_Fertility);

            if (UIGuard.Try("Inspector.PlantBlighted", () => plant.Blighted, false, null))
                y = InspectPaneParts.Fact(view, y, "Blight", "will not yield", palette.Danger, palette);
            else if (UIGuard.Try("Inspector.PlantDying", () => plant.Dying, false, null))
                y = InspectPaneParts.Fact(view, y, "Dying", "losing health", palette.Danger, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>One growth factor, listed only when it is actually costing the plant something.</summary>
        private static float Factor(Rect view, float y, string name, Plant plant, UIColorPaletteDef palette,
            System.Func<Plant, float> read)
        {
            float factor = UIGuard.Try("Inspector.PlantFactor", () => read(plant), 1f, null);

            if (factor >= 0.999f)
                return y;

            return InspectPaneParts.Fact(view, y, name, InspectPaneParts.Percent(factor),
                InspectPaneParts.Level(factor, palette), palette);
        }

        /// <summary>What this draws and what the grid has spare, which is the pair that decides whether to build.</summary>
        private static float Power(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            CompPowerTrader trader = UIGuard.Try("Inspector.PowerComp",
                thing.TryGetComp<CompPowerTrader>, null, null);

            if (trader == null)
                return y;

            float watts = UIGuard.Try("Inspector.PowerOutput", () => trader.PowerOutput, 0f, null);
            bool on = UIGuard.Try("Inspector.PowerOn", () => trader.PowerOn, false, null);

            y = InspectPaneParts.Cap(view, y, "Power", on ? "on" : "off", palette);

            y = InspectPaneParts.Fact(view, y, watts < 0f ? "Draw" : "Output",
                Mathf.Abs(Mathf.RoundToInt(watts)) + " W",
                on ? palette.TextPrimary : palette.TextDisabled, palette);

            PowerNet net = UIGuard.Try("Inspector.PowerNetRead", () => trader.PowerNet, null, null);

            if (net != null)
            {
                float gain = UIGuard.Try("Inspector.PowerGain",
                    () => net.CurrentEnergyGainRate() / CompPower.WattsToWattDaysPerTick, 0f, null);

                float stored = UIGuard.Try("Inspector.PowerStored", net.CurrentStoredEnergy, 0f, null);

                y = InspectPaneParts.Fact(view, y, "Grid",
                    (gain >= 0f ? "+" : string.Empty) + Mathf.RoundToInt(gain) + " W",
                    gain >= 0f ? palette.Success : palette.Danger, palette);

                y = InspectPaneParts.Fact(view, y, "Stored", Mathf.RoundToInt(stored) + " Wd",
                    palette.TextSecondary, palette);
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>A battery's own charge, which the grid total above does not tell you about this one.</summary>
        private static float Charge(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            CompPowerBattery battery = UIGuard.Try("Inspector.BatteryComp",
                thing.TryGetComp<CompPowerBattery>, null, null);

            if (battery == null)
                return y;

            float level = UIGuard.Try("Inspector.BatteryLevel", () => battery.StoredEnergyPct, 0f, null);
            float stored = UIGuard.Try("Inspector.BatteryStored", () => battery.StoredEnergy, 0f, null);

            y = InspectPaneParts.Cap(view, y, "Charge", Mathf.RoundToInt(stored) + " Wd", palette);

            y = InspectPaneParts.Need(view, y, "Stored", InspectPaneParts.Percent(level),
                InspectPaneParts.Level(level, palette), level, InspectPaneParts.Level(level, palette), null, null,
                palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>Fuel, against the level it is being refilled to rather than against the tank.</summary>
        private static float Fuel(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            CompRefuelable fuel = UIGuard.Try("Inspector.FuelComp", thing.TryGetComp<CompRefuelable>, null, null);

            if (fuel == null || fuel.Props == null || fuel.Props.fuelCapacity <= 0f)
                return y;

            float level = UIGuard.Try("Inspector.FuelLevel", () => fuel.FuelPercentOfMax, 0f, null);

            y = InspectPaneParts.Cap(view, y, fuel.Props.FuelLabel.CapitalizeFirst(),
                UIGuard.Try("Inspector.FuelAmount", () => Mathf.Round(fuel.Fuel).ToString(), null, null), palette);

            y = InspectPaneParts.Need(view, y, "Remaining", InspectPaneParts.Percent(level),
                InspectPaneParts.Level(level, palette), level, InspectPaneParts.Level(level, palette), null, null,
                palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>A cooler or heater's target against what the room is actually doing.</summary>
        private static float Climate(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            CompTempControl control = UIGuard.Try("Inspector.TempComp",
                thing.TryGetComp<CompTempControl>, null, null);

            if (control == null)
                return y;

            float target = UIGuard.Try("Inspector.TempTarget", () => control.TargetTemperature, 0f, null);
            float here = UIGuard.Try("Inspector.TempHere", () => thing.AmbientTemperature, 0f, null);

            y = InspectPaneParts.Cap(view, y, "Temperature", null, palette);

            y = InspectPaneParts.Fact(view, y, "Target", target.ToStringTemperature("F0"), palette.TextPrimary,
                palette);

            y = InspectPaneParts.Fact(view, y, "Now", here.ToStringTemperature("F0"),
                Mathf.Abs(here - target) <= 3f ? palette.Success : palette.Warning, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// How long this has before it spoils, which is the one fact about food with a deadline on it.
        ///
        /// <c>TicksUntilRotAtCurrentTemp</c> is the reading worth showing rather than the raw progress: a meal in
        /// a freezer and the same meal on the ground are at the same percentage and are two completely different
        /// problems.
        /// </summary>
        private static float Freshness(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            CompRottable rot = UIGuard.Try("Inspector.RotComp", thing.TryGetComp<CompRottable>, null, null);

            if (rot == null || rot.PropsRot == null || rot.PropsRot.TicksToRotStart <= 0)
                return y;

            float progress = UIGuard.Try("Inspector.RotProgress", () => rot.RotProgressPct, 0f, null);
            RotStage stage = UIGuard.Try("Inspector.RotStage", () => rot.Stage, RotStage.Fresh, null);

            y = InspectPaneParts.Cap(view, y, "Freshness", stage.ToString().ToLower(), palette);

            y = InspectPaneParts.Need(view, y, "Spoiled", InspectPaneParts.Percent(progress),
                InspectPaneParts.Level(1f - progress, palette), progress,
                InspectPaneParts.Level(1f - progress, palette), null, null, palette);

            int left = UIGuard.Try("Inspector.RotLeft", () => rot.TicksUntilRotAtCurrentTemp, 0, null);

            if (stage == RotStage.Fresh && left > 0 && left < GenDate.TicksPerYear)
                y = InspectPaneParts.Fact(view, y, "Keeps for",
                    left.ToStringTicksToPeriod(false, false, false), palette.TextSecondary, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>A workbench's queue, and how far along the one being worked is.</summary>
        private static float Work(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            IBillGiver giver = thing as IBillGiver;

            if (giver == null)
                return y;

            BillStack bills = UIGuard.Try("Inspector.Bills", () => giver.BillStack, null, null);

            if (bills == null)
                return y;

            y = InspectPaneParts.Cap(view, y, "Bills",
                bills.Count == 0 ? "none" : bills.Count.ToString(), palette);

            if (bills.Count == 0)
                return InspectPaneParts.Note(view, y, "Nothing queued here.", palette)
                       + InspectPaneParts.BlockGap;

            int shown = Mathf.Min(bills.Count, 5);

            for (int i = 0; i < shown; i++)
            {
                Bill bill = bills[i];

                if (bill == null)
                    continue;

                Bill_Production production = bill as Bill_Production;

                y = InspectPaneParts.Entry(view, y, bill.LabelCap,
                    bill.suspended
                        ? "suspended"
                        : production != null ? production.RepeatInfoText : null,
                    bill.suspended ? palette.TextDisabled : palette.TextSecondary, null, palette);
            }

            if (bills.Count > shown)
                y = InspectPaneParts.Note(view, y, (bills.Count - shown) + " more.", palette)
                    + InspectPaneParts.RowGap;

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>What a shelf, hopper or stockpile building is set to and what is actually in it.</summary>
        private static float Storage(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            IStoreSettingsParent store = thing as IStoreSettingsParent;

            if (store == null || !UIGuard.Try("Inspector.StorageTab", () => store.StorageTabVisible, false, null))
                return y;

            StorageSettings settings = UIGuard.Try("Inspector.StorageSettings", store.GetStoreSettings, null,
                null);

            if (settings == null)
                return y;

            y = InspectPaneParts.Cap(view, y, "Storage",
                UIGuard.Try("Inspector.StoragePriority", () => settings.Priority.Label(), null, null), palette);

            ISlotGroupParent group = thing as ISlotGroupParent;

            if (group != null)
            {
                int held = UIGuard.Try("Inspector.StorageHeld", () =>
                {
                    SlotGroup slots = group.GetSlotGroup();

                    return slots != null ? slots.HeldThingsCount : 0;
                }, 0, null);

                y = InspectPaneParts.Fact(view, y, "Holding",
                    held == 0 ? "empty" : held.ToString(),
                    held == 0 ? palette.TextDisabled : palette.TextPrimary, palette);
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>Who a bed belongs to and what kind of bed it is.</summary>
        private static float Sleeping(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            Building_Bed bed = thing as Building_Bed;

            if (bed == null)
                return y;

            y = InspectPaneParts.Cap(view, y, "Bed",
                UIGuard.Try<string>("Inspector.BedKind", () =>
                {
                    if (bed.Medical)
                        return "medical";

                    return bed.ForPrisoners ? "prisoner" : null;
                }, null, null), palette);

            List<Pawn> owners = UIGuard.Try("Inspector.BedOwners", () => bed.OwnersForReading, null, null);

            if (owners == null || owners.Count == 0)
            {
                y = InspectPaneParts.Fact(view, y, "Assigned", "nobody", palette.TextDisabled, palette);
            }
            else
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    if (owners[i] != null)
                        y = InspectPaneParts.Fact(view, y, i == 0 ? "Assigned" : " ",
                            owners[i].LabelShortCap, palette.TextPrimary, palette);
                }
            }

            y = InspectPaneParts.Fact(view, y, "Comfort",
                UIGuard.Try("Inspector.BedComfort",
                    () => InspectPaneParts.Percent(bed.GetStatValue(StatDefOf.Comfort)), null, null),
                palette.TextSecondary, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>Hit points, quality, material and breakdown: whether this thing is about to stop working.</summary>
        private static float Condition(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            bool useHitPoints = thing.def != null && thing.def.useHitPoints && thing.MaxHitPoints > 0;

            CompBreakdownable breakdown = UIGuard.Try("Inspector.BreakdownComp",
                thing.TryGetComp<CompBreakdownable>, null, null);

            QualityCategory quality;
            bool hasQuality = UIGuard.Try("Inspector.Quality", () => thing.TryGetQuality(out quality), false,
                null);

            if (!useHitPoints && breakdown == null && !hasQuality && thing.Stuff == null)
                return y;

            y = InspectPaneParts.Cap(view, y, "Condition", null, palette);

            if (useHitPoints)
            {
                float condition = thing.HitPoints / (float) thing.MaxHitPoints;

                y = InspectPaneParts.Meter(view, y, "Health", condition,
                    InspectPaneParts.Level(condition, palette),
                    thing.HitPoints + " / " + thing.MaxHitPoints,
                    InspectPaneParts.Level(condition, palette), palette);
            }

            if (hasQuality)
            {
                QualityCategory read;

                thing.TryGetQuality(out read);

                y = InspectPaneParts.Fact(view, y, "Quality", read.GetLabel().CapitalizeFirst(),
                    palette.TextPrimary, palette);
            }

            if (thing.Stuff != null)
                y = InspectPaneParts.Fact(view, y, "Material", thing.Stuff.LabelCap, palette.TextSecondary,
                    palette);

            if (breakdown != null)
                y = InspectPaneParts.Fact(view, y, "Breakdown",
                    breakdown.BrokenDown ? "broken down" : "working",
                    breakdown.BrokenDown ? palette.Danger : palette.Success, palette);

            float deterioration = UIGuard.Try("Inspector.Deterioration",
                () => thing.GetStatValue(StatDefOf.DeteriorationRate), 0f, null);

            if (deterioration > 0.01f && thing.Spawned && !thing.Position.Roofed(thing.Map))
                y = InspectPaneParts.Fact(view, y, "Deteriorating",
                    deterioration.ToString("0.#") + " a day", palette.Warning, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// What it is worth and what it weighs, plus whether anybody is allowed to touch it.
        ///
        /// Last on purpose: it applies to nearly everything, so putting it first would push the block that is
        /// actually about this thing down the column.
        /// </summary>
        private static float Worth(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            float value = UIGuard.Try("Inspector.MarketValue", () => thing.MarketValue, 0f, null);
            float mass = UIGuard.Try("Inspector.ThingMass",
                () => thing.GetStatValue(StatDefOf.Mass) * thing.stackCount, 0f, null);

            bool forbidden = UIGuard.Try("Inspector.Forbidden",
                () => thing.Spawned && thing.IsForbidden(Faction.OfPlayer), false, null);

            if (value <= 0f && mass <= 0f && !forbidden)
                return y;

            y = InspectPaneParts.Cap(view, y, "Worth",
                thing.stackCount > 1 ? "x" + thing.stackCount : null, palette);

            if (value > 0f)
                y = InspectPaneParts.Fact(view, y, "Value",
                    (value * thing.stackCount).ToStringMoney(), palette.TextPrimary, palette);

            if (mass > 0f)
                y = InspectPaneParts.Fact(view, y, "Mass", mass.ToString("0.##") + " kg", palette.TextSecondary,
                    palette);

            if (forbidden)
                y = InspectPaneParts.Fact(view, y, "Forbidden", "nobody will touch it", palette.Warning, palette);

            return y + InspectPaneParts.BlockGap;
        }
    }
}
