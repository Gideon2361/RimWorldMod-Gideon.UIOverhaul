using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Integrations;
using HarmonyLib;
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
            flow.Give(column, Substructure(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Research(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Speed(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Bioferrite(column, y, thing, palette));

            column = flow.Take(out y);
            flow.Give(column, Containment(column, y, thing, palette));

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
            flow.Give(column, Maintenance(column, y, thing, palette));

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

        /// <summary>
        /// What this draws and what the grid has spare, which is the pair that decides whether to build.
        ///
        /// <b>What it needs is a different number from what it is drawing,</b> and the difference is the whole
        /// reason vanilla writes "Power needed" instead of "Power draw". A building that is off, unpowered or
        /// idling has a <c>PowerOutput</c> of zero or of its idle trickle, so a pane reporting only that says
        /// "Draw 0 W" about a machine that will take 4,200 W the moment it is switched on -- which is precisely
        /// the number somebody is looking for when they click a building that is not running. Reported on
        /// 2026-08-25 against the bioferrite harvester, whose rated draw was only in the raw inspect string at the
        /// bottom of the pane.
        ///
        /// So the rated figure is shown whenever it differs from the live one, and never when it does not: a
        /// running machine drawing exactly what it is rated for gets one line, because a second line repeating it
        /// would be noise on every powered building on the map.
        ///
        /// <c>Props.PowerConsumption</c> rather than the def's raw field, because that getter applies the
        /// research upgrades -- a colony that has finished the relevant project genuinely needs less, and quoting
        /// the unupgraded number would be wrong in the player's favour.
        /// </summary>
        private static float Power(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            CompPowerTrader trader = UIGuard.Try("Inspector.PowerComp",
                thing.TryGetComp<CompPowerTrader>, null, null);

            if (trader == null)
                return y;

            float watts = UIGuard.Try("Inspector.PowerOutput", () => trader.PowerOutput, 0f, null);
            bool on = UIGuard.Try("Inspector.PowerOn", () => trader.PowerOn, false, null);

            // Positive means consuming, which is the opposite sign to PowerOutput and the same sign a player
            // reads on the wall. Zero for a generator, whose rated figure is its output and is already the live
            // number above.
            float rated = UIGuard.Try("Inspector.PowerRated",
                () => trader.Props != null ? Mathf.Max(0f, trader.Props.PowerConsumption) : 0f, 0f, null);

            y = InspectPaneParts.Cap(view, y, "Power", on ? "on" : "off", palette);

            y = InspectPaneParts.Fact(view, y, watts < 0f ? "Draw" : "Output",
                Mathf.Abs(Mathf.RoundToInt(watts)) + " W",
                on ? palette.TextPrimary : palette.TextDisabled, palette);

            if (rated > 0f && Mathf.Abs(rated - Mathf.Abs(watts)) >= 1f)
            {
                y = InspectPaneParts.Fact(view, y, on ? "Needs running" : "Needs",
                    Mathf.RoundToInt(rated) + " W", palette.TextSecondary, palette);
            }

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

        /// <summary>
        /// Vanilla's own capacity for a bioferrite harvester.
        ///
        /// <b>Restated rather than read, because RimWorld keeps it as a private const.</b>
        /// <c>Building_BioferriteHarvester.MaxCapacity</c> is 60 and there is no property over it, so the choice
        /// is between this line and reflecting a compiler-visible field that a future version could rename with
        /// nothing to warn us. A wrong number here would draw a bar of the wrong length; a wrong reflection would
        /// throw on a draw path. The comparison is also written into the game's own inspect string as
        /// <c>containedBioferrite:F2 / 60</c>, so it is a number the player can already see us against.
        /// </summary>
        private const float BioferriteCapacity = 60f;

        /// <summary>
        /// How full a bioferrite harvester is, and how fast it is filling.
        ///
        /// <b>A tank, so it gets a tank's treatment.</b> Vanilla states it as "Bioferrite contained: 41.28 / 60
        /// (+8.40 per day)" in the raw inspect string, which is the same fact as fuel or charge written as prose
        /// while those two get bars two inches above it. Asked for on 2026-08-25.
        ///
        /// <b>Thirty is the mark on the track, because thirty is when something happens.</b> That is
        /// <c>ReadyForHauling</c> -- the floor of the contained amount reaching the mod's hauling threshold -- so
        /// below it the harvester is filling and above it a colonist will come and empty it. A bar without that
        /// mark would be a bar where every position looks alike.
        ///
        /// <b>The rate is not recomputed here.</b> Vanilla's <c>BioferritePerDay</c> is private, and duplicating
        /// it would mean copying its walk over the linked platforms and its per-entity rate -- a second
        /// implementation of a game rule, which is the thing this whole mod avoids. <c>IsWorking</c> is public and
        /// answers the question that actually matters: filling, or stalled.
        /// </summary>
        private static float Bioferrite(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            Building_BioferriteHarvester harvester = thing as Building_BioferriteHarvester;

            if (harvester == null)
                return y;

            float contained = UIGuard.Try("Inspector.Bioferrite",
                () => Mathf.Clamp(harvester.containedBioferrite, 0f, BioferriteCapacity), 0f, null);

            bool working = UIGuard.Try("Inspector.BioferriteWorking", harvester.IsWorking, false, null);
            bool ready = UIGuard.Try("Inspector.BioferriteReady", () => harvester.ReadyForHauling, false, null);

            float fraction = contained / BioferriteCapacity;

            y = InspectPaneParts.Cap(view, y, "Bioferrite", working ? "harvesting" : "idle", palette);

            y = InspectPaneParts.Need(view, y, "Contained",
                Mathf.FloorToInt(contained) + " / " + Mathf.RoundToInt(BioferriteCapacity),
                ready ? palette.Success : palette.TextSecondary, fraction,
                ready ? palette.Success : palette.Accent,
                new[] { 30f / BioferriteCapacity }, null, palette);

            // Said only when it is not, because "idle" on the cap already covers the ordinary case and a machine
            // that is running does not need to be told so twice.
            if (!working)
            {
                y = InspectPaneParts.Fact(view, y, "Filling", "nothing on the platforms", palette.TextDisabled,
                    palette);
            }
            else if (!harvester.unloadingEnabled)
            {
                y = InspectPaneParts.Fact(view, y, "Unloading", "switched off", palette.Warning, palette);
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// A holding platform's containment, and the state of whatever is chained to it.
        ///
        /// A one-line hand-off, because the reading belongs with the rest of the Anomaly code rather than in the
        /// middle of a file about plants and batteries -- and because the entity's own pane draws the same facts
        /// from the other side, and the two must not drift apart. See <see cref="Anomaly.EntityBlock.Platform"/>.
        /// </summary>
        private static float Containment(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            return Anomaly.EntityBlock.Platform(view, y, thing as Building_HoldingPlatform, palette);
        }

        /// <summary>
        /// Fluffy Breakdowns' maintenance level, when that mod is running.
        ///
        /// <b>Red because it is a countdown, not a gauge.</b> Aaron asked for a red bar on 2026-08-25 and the
        /// reading is right: unlike fuel or charge, nothing about this bar being full is an achievement, and every
        /// direction it moves on its own is downwards.
        ///
        /// <b>No threshold mark on it.</b> It carried one at the mod's maintenance threshold, on the reasoning
        /// that a full-looking bar with the mark just underneath reads as "about to become somebody's job". Aaron
        /// took it off the same day, and the bar is better for it: the mark was a second reading to take from a
        /// row that has one number in it, and the caption already says "due" the moment the threshold is crossed.
        /// A bar answers "how much is left" and nothing else.
        ///
        /// <b>Placed after Condition on purpose.</b> That block already carries health and breakdown state, and
        /// maintenance is the thing that decides the second of those -- so it belongs beside it rather than up
        /// with the tanks, even though it is drawn like one.
        ///
        /// <b>Without the mod it is a component count, not an empty space.</b> The block used to vanish entirely,
        /// on the reading that maintenance is that mod's idea and nothing of ours. Half right: the schedule is
        /// theirs, but the part a component plays is vanilla's, and a building that breaks down needs one fetched
        /// before anybody can fix it. Aaron asked for the row on 2026-08-25. See
        /// <see cref="FluffyBreakdownsIntegration"/>.
        /// </summary>
        private static float Maintenance(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            float? durability = FluffyBreakdownsIntegration.Durability(thing);

            return durability.HasValue
                ? Durability(view, y, durability.Value, palette)
                : Components(view, y, thing, palette);
        }

        /// <summary>How maintained the building is, on Fluffy Breakdowns' own reckoning.</summary>
        private static float Durability(Rect view, float y, float value, UIColorPaletteDef palette)
        {
            float level = Mathf.Clamp01(value);
            float threshold = Mathf.Clamp01(FluffyBreakdownsIntegration.Threshold);

            bool wanting = level < threshold;

            y = InspectPaneParts.Cap(view, y, "Maintenance", wanting ? "due" : null, palette);

            y = InspectPaneParts.Need(view, y, "Components", InspectPaneParts.Percent(level),
                wanting ? palette.Danger : palette.TextSecondary, level, palette.Danger,
                null, null, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// Whether the colony has a component to hand, for a building that can break down.
        ///
        /// <b>Stock, not state.</b> Condition already says whether this thing is broken, so repeating that here
        /// would be a second row saying one fact. What it does not say is whether the repair can actually happen:
        /// <c>WorkGiver_FixBrokenDownBuilding</c> sends a colonist to find a component first, and a colony with
        /// none has a broken machine that nobody is coming to fix. That is the thing worth a row.
        ///
        /// <b>Counted from the map's own resource readout,</b> which is the same number the resource bar at the
        /// top of the screen shows -- so it counts what is in a stockpile and ignores what is lying in the mud
        /// outside, exactly as the work giver's own search effectively does.
        /// </summary>
        private static float Components(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            CompBreakdownable breakdown = UIGuard.Try("Inspector.MaintenanceComp",
                thing.TryGetComp<CompBreakdownable>, null, null);

            if (breakdown == null || thing.Map == null)
                return y;

            bool broken = UIGuard.Try("Inspector.MaintenanceBroken", () => breakdown.BrokenDown, false, null);

            int held = UIGuard.Try("Inspector.ComponentStock",
                () => thing.Map.resourceCounter.GetCount(ThingDefOf.ComponentIndustrial), 0, null);

            y = InspectPaneParts.Cap(view, y, "Maintenance", broken ? "needs a part" : null, palette);

            // Danger only when the shortage is currently costing something. A colony with no spares and nothing
            // broken is a colony that is fine, and colouring that red would train the reader to ignore it.
            Color color = held > 0
                ? palette.TextSecondary
                : broken
                    ? palette.Danger
                    : palette.Warning;

            y = InspectPaneParts.Fact(view, y, "Components",
                held > 0 ? held + " in store" : "none in store", color, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// A gravship's substructure: how much of it a grav engine is holding, or how much a pylon adds.
        ///
        /// <b>A bar for the engine, because it is a budget being spent.</b> Vanilla writes "Connected
        /// substructure: 931 / 1250" into the inspect string, which is two numbers you have to divide in your head
        /// to answer the only question a player has: how much more can I build before the ship stops flying. A bar
        /// answers it without arithmetic, and it warns before the ceiling rather than at it.
        ///
        /// <b>A plain figure for everything else, because it is a contribution.</b> A grav field extender adds
        /// support and has no capacity of its own, so a bar would need a maximum that does not exist. Asked for on
        /// 2026-08-26, both halves.
        ///
        /// <b>Read from the stat and the engine's own set,</b> not recounted. <c>SubstructureSupport</c> is what
        /// the engine itself asks for its capacity, and <c>AllConnectedSubstructure</c> is the set it built while
        /// deciding what flies. Counting substructure tiles ourselves would be a second opinion about which of
        /// them are connected.
        /// </summary>
        private static float Substructure(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            Building_GravEngine engine = thing as Building_GravEngine;

            if (engine != null)
                return Engine(view, y, engine, palette);

            if (!(thing is Building) || thing.Map == null)
                return y;

            float support = UIGuard.Try("Inspector.SubstructureSupport", () => Support(thing.def), 0f, null);

            if (Mathf.Abs(support) < 0.5f)
                return y;

            y = InspectPaneParts.Cap(view, y, "Substructure", null, palette);

            // Signed, because a negative one is a thing a mod can define and a bare number would read as a
            // benefit either way.
            y = InspectPaneParts.Fact(view, y, "Support",
                (support > 0f ? "+" : string.Empty) + Mathf.RoundToInt(support),
                support > 0f ? palette.Success : palette.Danger, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// What a building contributes to a gravship's substructure support.
        ///
        /// <b>Not <c>GetStatValue</c>, which was the bug.</b> A grav field extender's +250 lives in its facility
        /// comp's <c>statOffsets</c>, because a facility's offsets apply to the building it is linked to rather
        /// than to itself -- that is what a facility is. Asking the extender for its own
        /// <c>SubstructureSupport</c> therefore returns the stat's default base value, which is 1 because the def
        /// declares no default. So the panel reported "+1" beside a game that said "+250", and it was our reading
        /// that was wrong rather than vanilla's. Reported and fixed 2026-08-26.
        ///
        /// <b>The facility offset first, then the def's own base.</b> The offset is the contributing case and the
        /// only one in the game today; the base is there for a thing that genuinely carries the stat itself, which
        /// a mod may well add. Neither route can be confused with the absent case, which is what
        /// <c>GetStatValue</c> could not manage.
        /// </summary>
        private static float Support(ThingDef def)
        {
            if (def == null)
                return 0f;

            CompProperties_Facility facility = def.GetCompProperties<CompProperties_Facility>();

            float offset = Modifier(facility?.statOffsets);

            return Mathf.Abs(offset) >= 0.5f ? offset : Modifier(def.statBases);
        }

        /// <summary>The substructure support entry in a list of stat modifiers, or zero when it has none.</summary>
        private static float Modifier(List<StatModifier> modifiers)
        {
            if (modifiers == null)
                return 0f;

            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier modifier = modifiers[i];

                if (modifier != null && modifier.stat == StatDefOf.SubstructureSupport)
                    return modifier.value;
            }

            return 0f;
        }

        /// <summary>How much of its capacity a grav engine's hull is using.</summary>
        private static float Engine(Rect view, float y, Building_GravEngine engine, UIColorPaletteDef palette)
        {
            int used = UIGuard.Try("Inspector.SubstructureUsed",
                () => engine.AllConnectedSubstructure?.Count ?? 0, 0, null);

            float capacity = UIGuard.Try("Inspector.SubstructureCap",
                () => engine.GetStatValue(StatDefOf.SubstructureSupport), 0f, null);

            if (capacity < 0.5f)
                return y;

            float fraction = Mathf.Clamp01(used / capacity);

            // Amber near the ceiling and red at it, because running out is what stops the ship flying rather than
            // something that degrades gracefully.
            Color tone = fraction >= 1f
                ? palette.Danger
                : fraction >= 0.9f
                    ? palette.Warning
                    : palette.Success;

            y = InspectPaneParts.Cap(view, y, "Substructure",
                fraction >= 1f ? "at capacity" : null, palette);

            y = InspectPaneParts.Need(view, y, "Connected",
                used + " / " + Mathf.RoundToInt(capacity), tone, fraction, tone, null,
                fraction >= 1f ? "no room for more hull" : null, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// A research bench: how fast it works, what it is working on, and how far along that is.
        ///
        /// <b>The progress bar is the reason this block exists.</b> Vanilla writes "1420 / 4000 (35.5%)" into the
        /// inspect string, which is the answer to "how far along" spelled out three times and legible none of
        /// them. It also puts the current project on the line below the grid readout, so the thing you selected
        /// the bench to check is the last thing you read.
        ///
        /// <b>The speed factor comes first because it is the actionable one.</b> 87% means this bench is missing
        /// something -- a laboratory room, a multi-analyzer -- and that is a thing a player can go and fix. The
        /// project and its progress are status; the factor is a prompt.
        ///
        /// <b>Progress is the game's own apparent figures.</b> <c>ProgressApparent</c> and <c>CostApparent</c>
        /// are scaled by the colony's tech level, which is what makes a low-tech colony's numbers larger than the
        /// raw ones; showing the real pair instead would disagree with the research screen.
        /// </summary>
        private static float Research(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            if (!(thing is Building_ResearchBench))
                return y;

            float factor = UIGuard.Try("Inspector.ResearchFactor",
                () => thing.GetStatValue(StatDefOf.ResearchSpeedFactor), 1f, null);

            ResearchProjectDef project = UIGuard.Try("Inspector.ResearchProject",
                () => Find.ResearchManager?.GetProject(), null, null);

            y = InspectPaneParts.Cap(view, y, "Research", project == null ? "idle" : null, palette);

            y = InspectPaneParts.Fact(view, y, "Speed", InspectPaneParts.Percent(factor),
                Factor(factor, palette), palette);

            if (project == null)
            {
                return InspectPaneParts.Fact(view, y, "Project", "None".Translate(), palette.TextDisabled,
                    palette) + InspectPaneParts.BlockGap;
            }

            y = InspectPaneParts.Fact(view, y, "Project", project.LabelCap, palette.TextPrimary, palette);

            float done = UIGuard.Try("Inspector.ResearchProgress", () => project.ProgressApparent, 0f, null);
            float cost = UIGuard.Try("Inspector.ResearchCost", () => project.CostApparent, 0f, null);

            float fraction = cost <= 0f ? 0f : Mathf.Clamp01(done / cost);

            y = InspectPaneParts.Need(view, y, "Progress",
                Mathf.RoundToInt(done) + " / " + Mathf.RoundToInt(cost), palette.TextSecondary, fraction,
                InspectPaneParts.Level(fraction, palette), null, null, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// How fast a workbench works, when something is making it faster or slower than it should be.
        ///
        /// <b>Said only when it is not 100%.</b> A bench at full speed has nothing to report, and a row saying so
        /// on every bench in the colony is a row players learn to skip -- which costs them the one time it says
        /// 62%. Vanilla prints it either way.
        ///
        /// <b>Research benches are excluded because they have their own block above,</b> which reports the same
        /// idea against the research stat rather than the worktable one.
        /// </summary>
        private static float Speed(Rect view, float y, Thing thing, UIColorPaletteDef palette)
        {
            if (!(thing is Building_WorkTable) || thing is Building_ResearchBench)
                return y;

            float factor = UIGuard.Try("Inspector.WorkFactor",
                () => thing.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor), 1f, null);

            if (Mathf.Abs(factor - 1f) < 0.005f)
                return y;

            y = InspectPaneParts.Cap(view, y, "Speed", null, palette);

            y = InspectPaneParts.Fact(view, y, "Work speed", InspectPaneParts.Percent(factor),
                Factor(factor, palette), palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The colour for a multiplier: green above par, amber below, plain at it.
        ///
        /// <b>Above 100% is coloured too,</b> because a bench a mod or a facility has made faster is as worth
        /// noticing as one something has slowed down, and leaving it plain would say the bonus did not count.
        /// </summary>
        private static Color Factor(float factor, UIColorPaletteDef palette)
        {
            if (factor > 1.005f)
                return palette.Success;

            return factor < 0.995f ? palette.Warning : palette.TextSecondary;
        }

        /// <summary>
        /// A battery's own charge, which the grid total above does not tell you about this one.
        ///
        /// <b>Efficiency and self-discharge are the two facts that explain the bar.</b> Asked for on 2026-08-26.
        /// A player watching a bank of batteries fail to fill is looking at a 50% efficiency they had forgotten
        /// about, and one watching them empty overnight with nothing switched on is looking at self-discharge.
        /// Neither is visible from the bar, and neither changes, which is exactly why they are easy to forget.
        ///
        /// <b>Self-discharge is said even at zero, as "none".</b> An empty battery is not losing anything, and
        /// printing 5 W beside an empty bar would be describing a drain that is not happening -- but dropping the
        /// row entirely, as vanilla does, makes the fact appear and disappear depending on charge and reads as a
        /// panel that cannot make up its mind.
        /// </summary>
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

            float efficiency = UIGuard.Try("Inspector.BatteryEfficiency",
                () => battery.Props != null ? battery.Props.efficiency : 1f, 1f, null);

            y = InspectPaneParts.Fact(view, y, "Efficiency", InspectPaneParts.Percent(efficiency),
                palette.TextSecondary, palette);

            y = InspectPaneParts.Fact(view, y, "Self-discharge",
                stored > 0f ? Mathf.RoundToInt(SelfDischargeWatts) + " W" : "none",
                stored > 0f ? palette.TextSecondary : palette.TextDisabled, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// What a battery loses to self-discharge, read out of RimWorld rather than copied from it.
        ///
        /// <b>It is a private const, so this reads the constant itself.</b> Vanilla writes the literal <c>5f</c>
        /// into its own inspect string beside the const it should have used, which is how a number ends up
        /// disagreeing with itself. Reading the field means our figure follows theirs if it ever changes, and the
        /// fallback is only reached if the field is renamed -- in which case 5 is still what the game shipped.
        /// </summary>
        private static float SelfDischargeWatts
        {
            get
            {
                if (selfDischarge.HasValue)
                    return selfDischarge.Value;

                selfDischarge = UIGuard.Try("Inspector.BatteryDischargeConst", () =>
                {
                    FieldInfo field = AccessTools.Field(typeof(CompPowerBattery), "SelfDischargingWatts");

                    object value = field?.GetRawConstantValue();

                    return value is float ? (float) value : 5f;
                }, 5f, null);

                return selfDischarge.Value;
            }
        }

        private static float? selfDischarge;

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
        /// What it is worth, how it looks and what it weighs, plus whether anybody is allowed to touch it.
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

            float beauty = UIGuard.Try("Inspector.Beauty",
                () => thing.GetStatValue(StatDefOf.Beauty), 0f, null);

            // Beauty is the one number here that can be meaningfully negative, so it is tested against zero from
            // both sides. Filth, corpses and slag all have it, and an ugly thing is exactly as worth reporting as
            // a beautiful one.
            bool hasBeauty = Mathf.Abs(beauty) >= 0.01f;

            if (value <= 0f && mass <= 0f && !forbidden && !hasBeauty)
                return y;

            y = InspectPaneParts.Cap(view, y, "Worth",
                thing.stackCount > 1 ? "x" + thing.stackCount : null, palette);

            if (value > 0f)
                y = InspectPaneParts.Fact(view, y, "Value",
                    (value * thing.stackCount).ToStringMoney(), palette.TextPrimary, palette);

            // Beside Value rather than under Condition, because that is where a player already looks for it:
            // vanilla files Beauty as BasicsNonPawn, the same family Market Value and Mass are in, so the stats
            // window lists all three together.
            //
            // Not multiplied by stackCount, and the line above it is. That reads like an oversight and is not:
            // market value is per item and a stack is worth the sum, while Beauty is a property of the thing as
            // it sits on the map. BeautyUtility reads it straight off the thing, stack and all, so multiplying
            // here would report a room contribution that never happens.
            if (hasBeauty)
                y = InspectPaneParts.Fact(view, y, "Beauty",
                    (beauty > 0f ? "+" : string.Empty) + beauty.ToString("0.##"),
                    beauty > 0f ? palette.Success : palette.Warning, palette);

            if (mass > 0f)
                y = InspectPaneParts.Fact(view, y, "Mass", mass.ToString("0.##") + " kg", palette.TextSecondary,
                    palette);

            if (forbidden)
                y = InspectPaneParts.Fact(view, y, "Forbidden", "nobody will touch it", palette.Warning, palette);

            return y + InspectPaneParts.BlockGap;
        }
    }
}
