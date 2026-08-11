using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones;

/// <summary>
/// LEGACY -- data-only. Saves written before the BillStack refactor contain
/// <c>&lt;li Class="Gideon.UIOverhaul.Features.GrowZones.Zone_GrowthBill"&gt;</c> nodes under <c>customBillStack</c>,
/// so this class must keep its exact name and namespace to stay loadable. It is read once by
/// <see cref="Zone_GrowingPlus.ExposeData"/>, converted to <see cref="Bill_Growing"/>, then dropped.
/// It is never saved again. Do not add behavior here.
/// </summary>
public class Zone_GrowthBill : IExposable
{
    public BillType billType;
    public ThingDef plantDefBill;
    public int index;
    public int targetQuantity = 500;
    public string billName;
    public bool suspended;
    public bool manualSuspend;

    public void ExposeData()
    {
        // Load-only. The old implementation also resolved an owning-zone GUID through
        // Find.CurrentMap here, which is what produced the NullReferenceExceptions it swallowed;
        // ownership now comes from Bill.billStack.billGiver, so that lookup is gone.
        if (Scribe.mode != LoadSaveMode.LoadingVars)
            return;

        int billTypeValue = (int) billType;
        Scribe_Defs.Look(ref plantDefBill, "plantDefBill");
        Scribe_Values.Look(ref index, "index");
        Scribe_Values.Look(ref targetQuantity, "targetQuantity", 100);
        Scribe_Values.Look(ref billName, "billName");
        Scribe_Values.Look(ref suspended, "suspended");
        Scribe_Values.Look(ref manualSuspend, "manualSuspend");
        Scribe_Values.Look(ref billTypeValue, "billType");
        billType = (BillType) billTypeValue;
    }

    /// <summary>Numeric order is load-bearing -- old saves stored these as raw ints.</summary>
    public enum BillType
    {
        GrowUntilX,
        GrowForever,
        IfTotalNutritionBelow,
        IfTotalNutritionFromPlantsBelow,
    }
}
