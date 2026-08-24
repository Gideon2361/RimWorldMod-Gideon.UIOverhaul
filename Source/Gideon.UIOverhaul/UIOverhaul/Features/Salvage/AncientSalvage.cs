using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Salvage
{
    /// <summary>
    /// The ancient wrecks a map is littered with, turned into something a colony can pull apart for steel.
    ///
    /// <b>What RimWorld does now.</b> A ruined tank, an APC, a warwalker leg and the rest of that family are
    /// buildings with <c>deconstructible</c> set to false and no cost list at all. They cannot be designated,
    /// so the only way to clear one is to shoot it until it dies, and dying leaves nothing behind -- several
    /// hundred hit points of work for an empty patch of ground. A map generated with a wreck across the spot
    /// you wanted is a map you build around.
    ///
    /// <b>Written onto the defs, because that is where the game reads them from.</b>
    /// <c>Building.DeconstructibleBy</c> asks <c>building.IsDeconstructible</c> and then, for a thing with no
    /// faction, <c>alwaysDeconstructible</c>; <c>GenLeaving.CanBuildingLeaveResources</c> asks
    /// <c>resourcesFractionWhenDeconstructed</c> and then hands <c>CostListAdjusted</c> to the drop loop. Three
    /// fields on two objects, read from a designator, a work giver, a job driver and the leavings code -- all of
    /// which agree the moment the fields say something different, and none of which is worth a patch.
    ///
    /// <b>A named list rather than a rule, and deliberately so.</b> Every one of these inherits from
    /// <c>NonDeconstructibleAncientBuildingBase</c> in the game's own XML, but an abstract parent leaves no trace
    /// on the def at runtime, and every structural test that comes close -- inert, unclaimable, impassable, no
    /// cost -- also catches a fleshmass heart, a void monolith and a pit gate. Those are quest machinery, and
    /// letting a colonist quietly deconstruct one breaks the quest that is counting on it. Twenty-six names
    /// spelled out cannot do that. <see cref="Extra"/> is there for wreckage a mod adds.
    ///
    /// <b>Off means given back.</b> Turning the switch off rewrites all three fields from the baselines rather
    /// than skipping the write, and clears any deconstruct order already standing on a wreck -- an order nobody
    /// can fill is the kind of thing that quietly repeats a job scan failure forever.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class AncientSalvage
    {
        /// <summary>
        /// Steel per cell of footprint.
        ///
        /// Scaled off the wreck's own size rather than tabled per def, so a warwalker claw and the dropship it
        /// fell off are worth what they look like they are worth, and a mod's wreck is priced without anyone
        /// having to price it. Five is the game's own steel-per-cell for a wall, which makes a ruined tank
        /// roughly a tank-sized building's worth of metal.
        /// </summary>
        internal const int SteelPerCell = 5;

        /// <summary>Cells of footprint per component, before the clamp.</summary>
        internal const float CellsPerComponent = 8f;

        /// <summary>
        /// The most components any one wreck gives.
        ///
        /// A cap rather than a straight ratio: components are the scarce half of this and the biggest wrecks are
        /// large enough that an honest ratio would make a map with three dropships on it a component mine.
        /// </summary>
        internal const int MaxComponents = 4;

        /// <summary>
        /// The wreckage, by defName.
        ///
        /// Core's outdoor family, the two indoor pieces that share its parent, and Biotech's exostrider. Not
        /// <c>AncientCryptosleepPod</c> or the mech gestators, which are also non-deconstructible and are not
        /// wreckage: a pod has somebody in it.
        /// </summary>
        private static readonly string[] Names =
        {
            "AncientLargeContainer",
            "AncientMegaCannonTripod",
            "AncientTank",
            "AncientTankTrap",
            "AncientRustedCar",
            "AncientRustedTruck",
            "AncientRustedJeep",
            "AncientRustedCarFrame",
            "AncientWarwalkerTorso",
            "AncientRustedDropship",
            "AncientWarwalkerClaw",
            "AncientWarwalkerLeg",
            "AncientMiniWarwalkerRemains",
            "AncientWarspiderRemains",
            "AncientWarwalkerFoot",
            "AncientAPC",
            "AncientWarwalkerShell",
            "AncientJetEngine",
            "AncientDropshipEngine",
            "AncientPodCar",
            "AncientMachine",
            "AncientEquipmentBlocks",
            "AncientExostriderRemains",
            "AncientExostriderHead",
            "AncientExostriderLeg",
            "AncientExostriderCannon"
        };

        /// <summary>
        /// Wreckage a mod adds, by defName, for anyone who wants their own covered.
        ///
        /// Add to it before the defs are read -- a mod's own static constructor is early enough. Nothing in this
        /// mod writes to it.
        /// </summary>
        internal static readonly HashSet<string> Extra = new HashSet<string>();

        private sealed class Wreck
        {
            internal ThingDef Def;

            internal bool BaseAlways;

            internal List<ThingDefCountClass> BaseCost;

            internal float BaseFraction;

            /// <summary>What deconstructing it yields once the switch is on. Built once, at capture.</summary>
            internal List<ThingDefCountClass> Salvage;
        }

        private static bool captured;

        private static readonly List<Wreck> Wrecks = new List<Wreck>();

        static AncientSalvage()
        {
            UIGuard.Try("Salvage.Startup", Apply,
                "Ancient wreckage is left exactly as the game shipped it this session.");
        }

        /// <summary>Whether there is any wreckage on this install to configure.</summary>
        internal static bool Available
        {
            get { return Wrecks.Count > 0; }
        }

        /// <summary>
        /// Writes the current setting onto the defs.
        ///
        /// Called at startup, when the setting changes, and when the config file is reloaded from disk. Cheap and
        /// idempotent: it writes the same three fields every time rather than tracking what it wrote last.
        /// </summary>
        internal static void Apply()
        {
            Capture();

            if (!Available)
                return;

            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            bool on = settings != null && settings.salvageAncientWrecks;

            for (int i = 0; i < Wrecks.Count; i++)
            {
                Wreck wreck = Wrecks[i];

                // One field, and it is the only one there is: BuildingProperties.deconstructible is private, and
                // alwaysDeconstructible is the only public thing that reaches IsDeconstructible -- which returns
                // true on it without consulting the private one at all. It also happens to be exactly what is
                // needed anyway, since a wreck has no faction and the faction test would refuse it otherwise.
                wreck.Def.building.alwaysDeconstructible = on || wreck.BaseAlways;

                wreck.Def.costList = on ? wreck.Salvage : wreck.BaseCost;

                // One, so the cost list above is the yield rather than half of it. There is no build cost here to
                // return a fraction of -- the list is the salvage, invented for this, and a fraction would only
                // make the number in the code differ from the number in the stockpile.
                wreck.Def.resourcesFractionWhenDeconstructed = on ? 1f : wreck.BaseFraction;
            }

            // The cost list is cached per def the first time anything asks, and a wreck on a generated map has
            // usually been asked about already. Without this the drop loop reads an empty list back.
            CostListCalculator.Reset();

            if (!on)
                Forget();
        }

        /// <summary>
        /// Reads the baselines, once, before this class has written anything.
        ///
        /// The one-shot flag is set before the reads rather than after, so a throw halfway through cannot leave
        /// the door open for a partial recapture that would read our own numbers back as the game's.
        /// </summary>
        private static void Capture()
        {
            if (captured)
                return;

            captured = true;

            ThingDef steel = ThingDefOf.Steel;
            ThingDef component = ThingDefOf.ComponentIndustrial;

            if (steel == null || component == null)
                return;

            for (int i = 0; i < Names.Length; i++)
                Add(Names[i], steel, component);

            foreach (string name in Extra)
                Add(name, steel, component);
        }

        private static void Add(string name, ThingDef steel, ThingDef component)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);

            // Silent, and on purpose. Half this list is Biotech and none of it is guaranteed: a def that is not
            // here is an expansion the player does not own, which is not a fault worth a line in anyone's log.
            if (def == null || def.building == null)
                return;

            for (int i = 0; i < Wrecks.Count; i++)
            {
                if (Wrecks[i].Def == def)
                    return;
            }

            int cells = Mathf.Max(1, def.Size.x * def.Size.z);

            Wrecks.Add(new Wreck
            {
                Def = def,
                BaseAlways = def.building.alwaysDeconstructible,
                BaseCost = def.costList,
                BaseFraction = def.resourcesFractionWhenDeconstructed,
                Salvage = new List<ThingDefCountClass>
                {
                    new ThingDefCountClass(steel, cells * SteelPerCell),
                    new ThingDefCountClass(component,
                        Mathf.Clamp(Mathf.RoundToInt(cells / CellsPerComponent), 1, MaxComponents))
                }
            });
        }

        /// <summary>
        /// Drops deconstruct orders standing on wreckage, for when the switch goes off.
        ///
        /// The designation outlives the ability to act on it: the job driver fails its <c>DeconstructibleBy</c>
        /// check and the work giver offers the job again on the next scan, forever. Clearing them is the same
        /// courtesy the game does when a building stops being a valid target.
        /// </summary>
        private static void Forget()
        {
            if (Current.ProgramState != ProgramState.Playing)
                return;

            List<Map> maps = Find.Maps;

            for (int m = 0; maps != null && m < maps.Count; m++)
            {
                Map map = maps[m];

                if (map == null || map.designationManager == null || map.listerThings == null)
                    continue;

                for (int i = 0; i < Wrecks.Count; i++)
                {
                    List<Thing> things = map.listerThings.ThingsOfDef(Wrecks[i].Def);

                    for (int t = 0; things != null && t < things.Count; t++)
                    {
                        Designation order =
                            map.designationManager.DesignationOn(things[t], DesignationDefOf.Deconstruct);

                        if (order != null)
                            map.designationManager.RemoveDesignation(order);
                    }
                }
            }
        }
    }
}
