using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// The standing drug orders for one map, and the thing that acts on them.
    ///
    /// <b>Per map because every part of the question is per map.</b> The patients, the drugs in the stockpiles and
    /// the doctors who would carry them are all on one map, and a colony with a gravship site has two entirely
    /// separate wards.
    ///
    /// <b>It queues bills and nothing else.</b> No jobs are created, no doctor is chosen, no work is prioritised.
    /// The order writes the same <c>Bill_Medical</c> a player would write by hand and vanilla's own work giver
    /// takes it from there. That boundary is what keeps this from being a colony manager: it automates the
    /// remembering, not the deciding.
    ///
    /// <b>Every second and a half, and only while the game runs.</b> Nothing an order reads can change while
    /// paused, and a dose measured in hours does not need checking sixty times a second. The offset by map id
    /// staggers several maps so they do not all evaluate on the same tick.
    /// </summary>
    public class MapComponent_StandingOrders : MapComponent
    {
        /// <summary>Ticks between evaluations. About a second and a half at normal speed.</summary>
        private const int IntervalTicks = 90;

        private List<StandingDrugOrder> orders = new List<StandingDrugOrder>();

        /// <summary>Scratch for one evaluation. Never held past the end of <see cref="Evaluate"/>.</summary>
        private readonly List<Pawn> candidates = new List<Pawn>();

        public MapComponent_StandingOrders(Map map) : base(map)
        {
        }

        internal List<StandingDrugOrder> Orders
        {
            get
            {
                if (orders == null)
                    orders = new List<StandingDrugOrder>();

                return orders;
            }
        }

        internal static MapComponent_StandingOrders For(Map map)
        {
            return map == null ? null : map.GetComponent<MapComponent_StandingOrders>();
        }

        internal void Add(StandingDrugOrder order)
        {
            if (order == null)
                return;

            Orders.Add(order);
            HospitalRoster.Invalidate();
        }

        internal void Remove(StandingDrugOrder order)
        {
            if (order == null)
                return;

            Orders.Remove(order);
            HospitalRoster.Invalidate();
        }

        internal void Move(StandingDrugOrder order, int by)
        {
            int at = Orders.IndexOf(order);

            if (at < 0)
                return;

            int to = Mathf.Clamp(at + by, 0, Orders.Count - 1);

            if (to == at)
                return;

            Orders.RemoveAt(at);
            Orders.Insert(to, order);
        }

        /// <summary>
        /// How many orders are pointed at this pawn, across every map.
        ///
        /// Across every map rather than only their own, because an order naming a colonist should still be theirs
        /// while they are away with a caravan: the count is a fact about the person, not about where they are
        /// standing.
        /// </summary>
        internal static int CountFor(Pawn pawn)
        {
            if (pawn == null)
                return 0;

            return UIGuard.Try("Hospital.CountOrders", () =>
            {
                List<Map> maps = Find.Maps;

                if (maps == null)
                    return 0;

                int count = 0;

                for (int m = 0; m < maps.Count; m++)
                {
                    MapComponent_StandingOrders component = For(maps[m]);

                    if (component == null)
                        continue;

                    List<StandingDrugOrder> found = component.Orders;

                    for (int i = 0; i < found.Count; i++)
                    {
                        if (found[i] != null && found[i].Targets(pawn))
                            count++;
                    }
                }

                return count;
            }, 0, null);
        }

        /// <summary>Every order pointed at this pawn on their own map, for the patient pane.</summary>
        internal static void For(Pawn pawn, List<StandingDrugOrder> into)
        {
            if (into == null)
                return;

            into.Clear();

            if (pawn == null)
                return;

            UIGuard.Try("Hospital.OrdersFor", () =>
            {
                MapComponent_StandingOrders component = For(pawn.MapHeld);

                if (component == null)
                    return;

                List<StandingDrugOrder> found = component.Orders;

                for (int i = 0; i < found.Count; i++)
                {
                    if (found[i] != null && found[i].Targets(pawn))
                        into.Add(found[i]);
                }
            }, null);
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;

            if ((now + map.uniqueID) % IntervalTicks != 0)
                return;

            if (orders == null || orders.Count == 0)
                return;

            // Guarded per order rather than once around the loop: an escape would abandon every order after the
            // one that threw and would do it again every interval.
            for (int i = 0; i < orders.Count; i++)
            {
                StandingDrugOrder order = orders[i];

                if (order == null || order.suspended || order.drug == null)
                    continue;

                StandingDrugOrder captured = order;

                UIGuard.Try("Hospital.StandingOrder", () => Evaluate(captured),
                    "One standing order stopped issuing doses. Other orders are unaffected, and the drug can "
                    + "still be administered by hand from the patient's health tab.");
            }
        }

        /// <summary>
        /// Queues a dose for everybody this order is due on and allowed to act on.
        ///
        /// <b>The clock runs whether or not the dose is given.</b> A condition that is not met skips this dose
        /// rather than banking it, which is what "only while in pain" has to mean: a patient who was comfortable
        /// this morning should not be handed four painkillers the moment the pain returns.
        /// </summary>
        private void Evaluate(StandingDrugOrder order)
        {
            candidates.Clear();

            if (order.target == StandingOrderTarget.OnePatient)
            {
                if (order.patient != null && order.patient.MapHeld == map)
                    candidates.Add(order.patient);
            }
            else
            {
                List<Pawn> pawns = map.mapPawns.FreeColonistsAndPrisonersSpawned;

                if (pawns != null)
                {
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        if (order.Targets(pawns[i]))
                            candidates.Add(pawns[i]);
                    }
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn pawn = candidates[i];

                if (pawn == null || pawn.Dead || !order.Due(pawn))
                    continue;

                if (order.BlockedBy(pawn) != null)
                    continue;

                order.Fire(pawn);
            }

            candidates.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref orders, "gideonStandingOrders", LookMode.Deep);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            if (orders == null)
            {
                orders = new List<StandingDrugOrder>();

                return;
            }

            // An order whose drug came from a mod that has since been removed has nothing left to administer, and
            // keeping it would leave a row on the tab that can never fire and cannot be explained.
            orders.RemoveAll(order => order == null || order.drug == null);
        }
    }
}
