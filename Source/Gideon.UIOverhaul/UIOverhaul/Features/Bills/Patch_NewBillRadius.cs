using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Gives a newly made bill the player's default ingredient search radius.
    ///
    /// <b>Backlog 20, and the whole of it.</b> Vanilla starts every bill at 999, which is the entire map, so a
    /// crafter will walk to the far corner for one piece of steel rather than use the pile next door. Setting that
    /// once beats setting it on every bill forever.
    ///
    /// <b>It changes nothing until the player asks.</b> The setting itself ships at vanilla's 999, so this patch is
    /// a no op for anybody who never opens it. Shipping a smaller default would quietly stall bills in colonies
    /// whose stockpiles are simply far from the bench, which is a gameplay change dressed up as a convenience.
    ///
    /// <b>Only new bills.</b> Existing ones are left exactly as the player set them; the bills window offers
    /// changing them all as a deliberate action instead.
    /// </summary>
    [HarmonyPatch(typeof(BillUtility), nameof(BillUtility.MakeNewBill))]
    internal static class Patch_NewBillRadius
    {
        [HarmonyPostfix]
        public static void Postfix(Bill __result)
        {
            if (__result == null)
                return;

            UIGuard.Try("Bills.NewBillRadius", () =>
            {
                float radius = UIOverhaulSettingsFile.Current?.defaultIngredientRadius ?? 999f;

                // 999 is what the bill already has, so there is nothing to do and nothing to explain if the
                // settings file could not be read.
                if (radius > 0f && radius < 999f)
                    __result.ingredientSearchRadius = radius;
            }, null);
        }
    }
}
