using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Notices that the set of bills in the game has changed, whoever changed it.
    ///
    /// <b>Three methods, because those are the three that change the population.</b> <c>AddBill</c>,
    /// <c>Delete</c> and <c>Clear</c>. <c>RemoveIncompletableBills</c> goes through <c>Delete</c> and needs no
    /// entry of its own, and <c>Reorder</c> is not here because the colony window's list is grouped by bench and
    /// reordering within one does not change what it holds.
    ///
    /// <b>Patched at the stack rather than at our own entry points,</b> which is the whole value of it. The
    /// colony wide window cannot gather every bill on every map per frame -- it asks each one whether anybody in
    /// the colony is allowed to work it -- so it re-reads on demand, and "on demand" had quietly come to mean
    /// "wherever somebody remembered to say so". Suspend, delete and reorder said so. Importing a bench template
    /// did not, which is what Aaron found on 2026-08-23, and neither would the next thing anybody wrote.
    ///
    /// <b>Postfixes, so a stack that refused the change does not mark anything stale.</b> And nothing here reads
    /// the bill or the stack: bumping an integer cannot fail, cannot be re-entrant, and cannot care which mod
    /// called it.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_BillStackChanged
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BillStack), nameof(BillStack.AddBill))]
        public static void Added()
        {
            Changed();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BillStack), nameof(BillStack.Delete))]
        public static void Deleted()
        {
            Changed();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BillStack), nameof(BillStack.Clear))]
        public static void Cleared()
        {
            Changed();
        }

        /// <summary>
        /// Guarded like everything else reachable from RimWorld, though there is little here to throw.
        ///
        /// This runs inside bill creation, which happens during pawn work as well as from the interface, so the
        /// guard's job is to make sure a fault here can never interrupt a colonist finishing a job.
        /// </summary>
        private static void Changed()
        {
            UIGuard.Try("Bills.StackChanged", BillCatalog.Notify_BillsChanged, null);
        }
    }
}
