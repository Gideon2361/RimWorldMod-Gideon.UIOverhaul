using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Orders
{
    [DefOf]
    public static class OrdersDefOf
    {
        /// <summary>
        /// Carrying a hurt but mobile pawn to a bed.
        ///
        /// Vanilla's <c>JobDriver_TakeToBed</c>, reached through a def that is neither Rescue nor Capture so its
        /// downed check does not apply. See Defs/Jobs_Rescue.xml for why that is the whole of the trick.
        /// </summary>
        public static JobDef Gideon_RescueImpaired;

        static OrdersDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(OrdersDefOf));
        }
    }
}
