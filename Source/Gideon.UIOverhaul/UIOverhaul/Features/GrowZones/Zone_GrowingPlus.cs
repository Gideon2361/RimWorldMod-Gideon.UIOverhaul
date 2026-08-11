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

        MigrateLegacyBills();
        UpdatePlantDefToGrow();
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
            Log.Warning($"[Gideon.UIOverhaul] Zone '{label}' had {migrated} saved bills but only "
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

    public override void AddCell(IntVec3 c)
    {
        base.AddCell(c);

        if (SuppressCutWarnings)
            return;

        foreach (Thing t in Map.thingGrid.ThingsListAt(c))
            Designator_PlantsHarvestWood.PossiblyWarnPlayerImportantPlantDesignateCut(t);
    }

    public override IEnumerable<Gizmo> GetGizmos()
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

            yield return gizmo;
        }

        yield return new Command_Toggle
        {
            defaultLabel = "Sow Over Sown",
            defaultDesc = "When this zone's desired plant changes, sow over already sown plots.",
            icon = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/SowingIcon"),
            isActive = () => SowOverSown,
            toggleAction = () => SowOverSown = !SowOverSown
        };

        yield return new Command_Toggle
        {
            defaultLabel = "Require Active Bill",
            defaultDesc = "Only sow in this plot if there is an active bill.",
            icon = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/SetPlantToGrow"),
            isActive = () => RequireActiveBillToSow,
            toggleAction = () => RequireActiveBillToSow = !RequireActiveBillToSow
        };
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
