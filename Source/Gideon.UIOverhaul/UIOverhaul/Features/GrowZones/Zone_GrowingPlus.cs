using Gideon.UIFramework.Helpers;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones;

public class Zone_GrowingPlus : Zone_Growing, IBillGiver
{
    private BillStack billStack;

    // Populated only when loading a save written before the BillStack refactor; converted in
    // PostLoadInit and then dropped. Never written back out.
    private List<Zone_GrowthBill> legacyBills;

    public Bill_Growing CurrentBill;
    public bool AutoUnsuspendActive = true;
    public bool SowOverSown = true;
    public bool RequireActiveBillToSow = true;

    private ITab_GrowthZoneBills _tabGrowthZoneBills;
    private int _tickDelta;
    private const int TickTarget = 60;
    public bool IsDueForTick => TickTarget - _tickDelta <= 0;

    public override bool IsMultiselectable => true;

    protected override Color NextZoneColor => ZoneColorUtility.NextGrowingZoneColor();

    public BillStack BillStack => billStack;
    public IEnumerable<IntVec3> IngredientStackCells => Enumerable.Empty<IntVec3>();
    public string LabelShort => label;
    public bool CurrentlyUsableForBills() => true;
    public bool UsableForBillsAfterFueling() => true;
    public void Notify_BillDeleted(Bill bill) => UpdatePlantDefToGrow();

    public Zone_GrowingPlus()
    {
        billStack = new BillStack(this);
    }

    public Zone_GrowingPlus(ZoneManager zoneManager)
        : base(zoneManager)
    {
        billStack = new BillStack(this);
    }

    public override IEnumerable<InspectTabBase> GetInspectTabs()
    {
        if (_tabGrowthZoneBills == null)
            _tabGrowthZoneBills = new ITab_GrowthZoneBills();
        yield return _tabGrowthZoneBills;
    }

    /// <summary>
    /// <b>The Scribe calls here are deliberately not wrapped in a try/catch, and that is not an oversight.</b>
    ///
    /// Scribe is a stateful writer: each Look enters and leaves a node, and the depth is tracked across the whole
    /// save. Catching an exception partway through and carrying on would leave that stack unbalanced and go on
    /// writing at the wrong depth -- turning a failure that vanilla knows how to abort cleanly into a save file that
    /// looks complete and is not. Letting it out is the safe behavior, and vanilla's own handler is where it
    /// belongs.
    ///
    /// What is guarded is our own work in the PostLoadInit pass below, which is logic rather than serialization and
    /// has no such constraint.
    /// </summary>
    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_Deep.Look(ref billStack, "billStack", this);
        Scribe_Values.Look(ref AutoUnsuspendActive, "autoUnsuspendActive", true);
        Scribe_Values.Look(ref SowOverSown, "sowOverSown", true);
        Scribe_Values.Look(ref RequireActiveBillToSow, "requireActiveBillToSow", true);

        // Read-only: pulls the pre-refactor bill list out of old saves without ever writing it back.
        if (Scribe.mode == LoadSaveMode.LoadingVars)
            Scribe_Collections.Look(ref legacyBills, "customBillStack", LookMode.Deep);

        if (Scribe.mode != LoadSaveMode.PostLoadInit)
            return;

        if (billStack == null)
            billStack = new BillStack(this);

        // Guarded separately so one of them failing does not cost the other. Vanilla's PostLoadIniter does catch
        // per item, so an escape here would not break the load -- but it would abandon this zone half set up, and
        // report it without saying which zone or what the mod was doing.
        UIGuard.Try("GrowZones.MigrateLegacyBills", MigrateLegacyBills,
            "Bills saved by an older version of this mod were not carried over on this zone. They are still in "
            + "the save file, so a fixed version can pick them up later.");

        UIGuard.Try("GrowZones.UpdatePlantToGrow", UpdatePlantDefToGrow,
            "This zone may show the wrong plant as the one it is growing until a bill is changed.");
    }

    /// <summary>
    /// Converts pre-refactor <see cref="Zone_GrowthBill"/> entries into real Bills. The old class
    /// stored its owning zone as a GUID string and re-resolved it through Find.CurrentMap; a Bill
    /// gets its owner from billStack.billGiver, so that whole mechanism goes away.
    /// </summary>
    private void MigrateLegacyBills()
    {
        if (legacyBills == null)
            return;

        List<Zone_GrowthBill> ordered = legacyBills.Where(b => b?.plantDefBill != null)
            .OrderBy(b => b.index)
            .ToList();

        int migrated = 0;
        foreach (Zone_GrowthBill old in ordered)
        {
            Bill_Growing bill = new Bill_Growing(old.plantDefBill)
            {
                repeatMode = RepeatModeFor(old.billType),
                targetCount = old.targetQuantity,
                suspended = old.manualSuspend,
                paused = old.suspended && !old.manualSuspend
            };

            // The old UI auto-filled billName with "Grow <plant>"; only carry over real renames.
            if (!old.billName.NullOrEmpty() && old.billName != $"Grow {old.plantDefBill.LabelCap}")
                bill.RenamableLabel = old.billName;

            billStack.AddBill(bill);
            migrated++;
        }

        if (migrated > billStack.Count)
        {
            Log.Warning(UILogTag.Prefix + $"Zone '{label}' had {migrated} saved bills but only "
                        + $"{billStack.Count} fit; BillStack.MaxCount is {BillStack.MaxCount}.");
        }

        legacyBills = null;
    }

    private static BillRepeatModeDef RepeatModeFor(Zone_GrowthBill.BillType billType)
    {
        switch (billType)
        {
            case Zone_GrowthBill.BillType.GrowUntilX:
                return BillRepeatModeDefOf.TargetCount;
            case Zone_GrowthBill.BillType.IfTotalNutritionBelow:
                return GZPDefOf.GZP_NutritionBelow;
            case Zone_GrowthBill.BillType.IfTotalNutritionFromPlantsBelow:
                return GZPDefOf.GZP_PlantNutritionBelow;
            default:
                return BillRepeatModeDefOf.Forever;
        }
    }

    /// <summary>
    /// Set while <see cref="GrowingZoneConverter"/> rebuilds a zone's cells in bulk, so converting
    /// an existing zone does not replay the cut warning for every thing it already contained.
    /// </summary>
    public static bool SuppressCutWarnings;

    /// <summary>
    /// The base call is left unguarded on purpose: it is what actually adds the cell to the zone, and a zone that
    /// silently failed to grow while reporting success would be a worse outcome than the exception. Only the warning
    /// pass after it is guarded, which is ours and is advisory.
    /// </summary>
    public override void AddCell(IntVec3 c)
    {
        base.AddCell(c);

        if (SuppressCutWarnings)
            return;

        UIGuard.Try("GrowZones.WarnOnCut", () =>
        {
            foreach (Thing t in Map.thingGrid.ThingsListAt(c))
                Designator_PlantsHarvestWood.PossiblyWarnPlayerImportantPlantDesignateCut(t);
        }, "No warning is shown when a growing zone is drawn over an important plant.");
    }

    /// <summary>
    /// <b>Built into a list rather than yielded.</b> An iterator's body runs while vanilla walks the result, inside
    /// the inspect pane -- a stack with no frame of ours on it to catch anything, and C# will not allow a try/catch
    /// around a <c>yield return</c> to put one there. Taking the list up front is what makes this guardable at all.
    ///
    /// The toggles' own delegates are wrapped separately, because they are called later still: <c>isActive</c> on
    /// every frame the gizmo is drawn, and <c>toggleAction</c> whenever it is clicked, both long after this method
    /// has returned.
    /// </summary>
    public override IEnumerable<Gizmo> GetGizmos()
    {
        List<Gizmo> gizmos = new List<Gizmo>();

        // base.GetGizmos is vanilla's and any other mod's postfixes, so it is guarded in its own right: a failure
        // there must cost us our two toggles, not the whole inspect pane.
        UIGuard.Try("GrowZones.BaseGizmos", () =>
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                // Drop vanilla's "set plant to grow". It opens a FloatMenu that sets one plant for the
                // whole zone, which is the thing the bill system replaces: leaving both in offers two
                // routes to the same setting, one of which silently ignores every bill on the zone.
                //
                // Filtered here rather than patched. Zone_Growing.GetGizmos yields the command for every
                // growing zone, and a patch on it could not tell ours apart from a plain one -- a mod
                // adding its own Zone_Growing would lose the command too.
                if (gizmo is Command_SetPlantToGrow)
                    continue;

                gizmos.Add(gizmo);
            }
        }, "A selected growing zone shows only this mod's two toggles, without vanilla's zone commands.");

        UIGuard.Try("GrowZones.ZoneToggles", () =>
        {
            gizmos.Add(new Command_Toggle
            {
                defaultLabel = "Sow Over Sown",
                defaultDesc = "When this zone's desired plant changes, sow over already sown plots.",
                icon = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/SowingIcon"),
                isActive = UIGuard.Wrap("GrowZones.ReadSowOverSown", () => SowOverSown, false),
                toggleAction = UIGuard.Wrap("GrowZones.ToggleSowOverSown", () => SowOverSown = !SowOverSown)
            });

            gizmos.Add(new Command_Toggle
            {
                defaultLabel = "Require Active Bill",
                defaultDesc = "Only sow in this plot if there is an active bill.",
                icon = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/SetPlantToGrow"),
                isActive = UIGuard.Wrap("GrowZones.ReadRequireActiveBill", () => RequireActiveBillToSow, false),
                toggleAction = UIGuard.Wrap("GrowZones.ToggleRequireActiveBill",
                    () => RequireActiveBillToSow = !RequireActiveBillToSow)
            });
        }, "The sow-over-sown and require-active-bill toggles are missing from the zone's commands. Both settings "
           + "keep whatever value they already had.");

        return gizmos;
    }

    /// <summary>
    /// Cheap sow-gate predicate. Reads the suspend/pause flags cached by the last ZoneTick instead
    /// of re-evaluating ShouldDoNow, which would re-count map resources. This runs once per cell
    /// per work scan, so it must not touch the resource counter.
    /// </summary>
    public bool AnyBillWantsSowing()
    {
        foreach (Bill bill in billStack.Bills)
        {
            if (!bill.suspended && bill is Bill_Growing growing && !growing.paused)
                return true;
        }
        return false;
    }

    /// <summary>The highest-priority bill that currently wants to run, or null.</summary>
    public Bill_Growing FirstActiveBill()
    {
        foreach (Bill bill in billStack.Bills)
        {
            if (bill is Bill_Growing growing && growing.ShouldDoNow())
                return growing;
        }
        return null;
    }

    public void UpdatePlantDefToGrow()
    {
        Bill_Growing active = FirstActiveBill();
        CurrentBill = active;
        if (active?.plantDef != null)
            SetPlantDefToGrow(active.plantDef);
    }

    public void ZoneTick()
    {
        _tickDelta++;
        if (!IsDueForTick)
            return;

        _tickDelta = 0;
        UpdatePlantDefToGrow();
    }

    public int CountPlayerStockOf(ThingDef thingDef, Map map) => map.resourceCounter.GetCount(thingDef);

    public int CountTotalNutrition(Map map) => (int) map.resourceCounter.TotalHumanEdibleNutrition;

    public int CountTotalNutritionFromPlants(Map map)
    {
        float totalNutrition = 0.0f;
        foreach (KeyValuePair<ThingDef, int> allCountedAmount in map.resourceCounter.AllCountedAmounts)
        {
            if (allCountedAmount.Key.IsNutritionGivingIngestible && allCountedAmount.Key.IsPlant)
                totalNutrition += allCountedAmount.Key.GetStatValueAbstract(StatDefOf.Nutrition) * allCountedAmount.Value;
        }
        return (int) totalNutrition;
    }
}
