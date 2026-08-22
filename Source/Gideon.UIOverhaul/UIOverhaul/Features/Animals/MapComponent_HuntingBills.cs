using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The hunting bills for one map, and the thing that acts on them.
    ///
    /// <b>Per map because everything it reasons about is per map.</b> The meat in the stockpiles, the wildlife
    /// standing outside and the designations that connect them all belong to one map, and a colony with a
    /// gravship site has two entirely separate versions of this question.
    ///
    /// <b>It designates and nothing else.</b> No jobs are created, no hunters are chosen, no work is prioritised.
    /// The bill puts a hunt order on an animal exactly as the player would, and vanilla's own work giver takes it
    /// from there. That boundary is what keeps this from being a colony manager: it automates the clicking, not
    /// the deciding.
    ///
    /// <b>Every fourth second, and only while the game runs.</b> A stock level cannot move while the game is
    /// paused, and the resource counter itself only refreshes every 204 ticks, so evaluating faster would read
    /// the same numbers and reach the same conclusion. The offset by map id staggers several maps so they do not
    /// all evaluate on the same tick.
    ///
    /// <b>What it will never do.</b> It will not cancel a hunt somebody is walking towards, because a bill going
    /// quiet is not a reason to waste the trip. It will not take the last of a species, take a predator, or take
    /// anything likelier to turn on the colony than the bill allows. And it will not act at all while suspended,
    /// which is the switch to reach for rather than deleting a bill you will want again in autumn.
    /// </summary>
    public class MapComponent_HuntingBills : MapComponent
    {
        /// <summary>Ticks between evaluations. Four seconds at normal speed.</summary>
        private const int IntervalTicks = 240;

        private List<HuntingBill> bills = new List<HuntingBill>();

        /// <summary>Scratch for one evaluation. Never held past the end of <see cref="Evaluate"/>.</summary>
        private readonly List<AnimalGroup> candidates = new List<AnimalGroup>();

        private readonly Dictionary<ThingDef, int> quota = new Dictionary<ThingDef, int>();

        public MapComponent_HuntingBills(Map map) : base(map)
        {
        }

        /// <summary>The bills on this map, in the order the player arranged them.</summary>
        internal List<HuntingBill> Bills
        {
            get
            {
                if (bills == null)
                    bills = new List<HuntingBill>();

                return bills;
            }
        }

        /// <summary>The component for a map, or null when there is not one to be had.</summary>
        internal static MapComponent_HuntingBills For(Map map)
        {
            return map?.GetComponent<MapComponent_HuntingBills>();
        }

        internal void Add(HuntingBill bill)
        {
            if (bill != null)
                Bills.Add(bill);
        }

        internal void Remove(HuntingBill bill)
        {
            if (bill != null)
                Bills.Remove(bill);
        }

        /// <summary>
        /// Moves a bill up or down the list.
        ///
        /// Order is not priority: every bill is evaluated every time, and two bills wanting the same deer are
        /// resolved by the first one taking it. It is the reading order the player chose, and it decides who wins
        /// that race, which is close enough to priority to be worth being able to change.
        /// </summary>
        internal void Move(HuntingBill bill, int by)
        {
            int at = Bills.IndexOf(bill);

            if (at < 0)
                return;

            int to = Mathf.Clamp(at + by, 0, Bills.Count - 1);

            if (to == at)
                return;

            Bills.RemoveAt(at);
            Bills.Insert(to, bill);
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;

            if ((now + map.uniqueID) % IntervalTicks != 0)
                return;

            if (bills == null || bills.Count == 0)
                return;

            // Guarded per bill rather than once around the loop: vanilla catches around the whole component, so
            // an escape would abandon every bill after the one that threw and would do it again every interval.
            for (int i = 0; i < bills.Count; i++)
            {
                HuntingBill bill = bills[i];

                if (bill == null || bill.suspended)
                    continue;

                HuntingBill captured = bill;

                UIGuard.Try("Animals.HuntingBill", () => Evaluate(captured),
                    "One hunting bill stopped issuing orders. Other bills are unaffected, and hunting can still "
                    + "be ordered by hand from the animals tab.");
            }
        }

        /// <summary>
        /// Brings one bill up to its target, if it is short and is allowed to act.
        ///
        /// <b>The order of the tests is the whole behaviour.</b> What is already ordered counts as arriving, so a
        /// bill that has designated enough does nothing. A bill inside its resume threshold waits rather than
        /// sending somebody out for one hare. Only then does it look for animals, and even then it stops at the
        /// outstanding cap so a lean winter cannot turn into every hunter on the map walking in a different
        /// direction.
        ///
        /// <b>The other two modes skip all of that and only obey the cap.</b> Neither has a stock to be short of.
        /// A forever bill wants the species gone, and an over population bill wants it thinned to a headcount,
        /// which was already applied when the quota was worked out. So both take whatever the quota and the
        /// safeguards leave them, and yield is not consulted in either: a species whose meat nobody wants is
        /// precisely the one being culled.
        /// </summary>
        private void Evaluate(HuntingBill bill)
        {
            int outstandingCount;
            float outstandingAmount;

            Outstanding(bill, out outstandingCount, out outstandingAmount);

            float needed = float.MaxValue;

            if (bill.Stocked)
            {
                int stock = bill.Stock(map);

                if (stock >= bill.targetCount)
                    return;

                float projected = stock + outstandingAmount;

                if (projected >= bill.targetCount)
                    return;

                // Hysteresis. Above the resume threshold the bill is short but not short enough to act, which is
                // what stops a colony one unit under its target from hunting continuously.
                if (stock > bill.ResumeThreshold)
                    return;

                needed = bill.targetCount - projected;
            }

            int slots = Mathf.Max(0, bill.maxOutstanding - outstandingCount);

            if (slots <= 0)
                return;

            Collect(bill);

            if (candidates.Count == 0)
                return;

            int ordered = 0;

            // One species at a time, in the roster's own order, which puts predators first and then the largest
            // return. Within a species the group path picks which individuals, so a bill and a player pulling the
            // stepper choose the same animals for the same reasons.
            for (int i = 0; i < candidates.Count && slots > 0 && needed > 0f; i++)
            {
                AnimalGroup group = candidates[i];

                int allowed = Mathf.Min(quota[group.Def], slots);

                if (allowed <= 0)
                    continue;

                int wanted;

                if (!bill.Stocked)
                {
                    // Forever and over population both take everything the quota allows: the quota is where the
                    // headcount was already applied, so there is nothing left for this loop to weigh up.
                    wanted = allowed;
                }
                else
                {
                    float each = bill.Contribution(group.Members[0]);

                    if (each <= 0f)
                        continue;

                    wanted = Mathf.Min(allowed, Mathf.CeilToInt(needed / each));
                    needed -= wanted * each;
                }

                if (wanted <= 0)
                    continue;

                AnimalDesignations.SetHuntCount(group, group.HuntOrdered + wanted);

                slots -= wanted;
                ordered += wanted;
            }

            if (ordered <= 0)
                return;

            bill.lastActedTick = Find.TickManager.TicksGame;
            bill.lastOrderedCount = ordered;
        }

        /// <summary>
        /// What is already ordered towards this bill: how many hunts, and how much they will yield.
        ///
        /// Counted from the map's own hunt designations rather than from anything this component remembers,
        /// because the player designates by hand as well, and a hunt they ordered feeds the same stockpile. A bill
        /// that ignored those would order a second lot on top.
        /// </summary>
        private void Outstanding(HuntingBill bill, out int count, out float amount)
        {
            count = 0;
            amount = 0f;

            DesignationManager designations = map?.designationManager;

            if (designations == null)
                return;

            foreach (Designation designation in designations.SpawnedDesignationsOfDef(DesignationDefOf.Hunt))
            {
                Pawn animal = designation?.target.Thing as Pawn;

                if (animal == null || animal.Dead)
                    continue;

                float contribution = bill.Contribution(animal);

                if (contribution <= 0f)
                    continue;

                count++;
                amount += contribution;
            }
        }

        /// <summary>
        /// The wildlife groups this bill may take from, with a per species quota.
        ///
        /// <b>The quota is the floor doing its work.</b> A species with three animals and a floor of two has one
        /// to give, and any hunts already ordered on it come out of that one. Getting this wrong in the other
        /// direction is how a standing order clears a map.
        ///
        /// The groups come from the shared roster and are only valid for the length of this evaluation, since a
        /// later rebuild recycles them. Nothing here keeps one.
        /// </summary>
        private void Collect(HuntingBill bill)
        {
            candidates.Clear();
            quota.Clear();

            List<AnimalSection> sections = AnimalRoster.Sections;

            for (int s = 0; s < sections.Count; s++)
            {
                AnimalSection section = sections[s];

                if (section.Kind != AnimalKind.Wild || section.Map != map)
                    continue;

                for (int g = 0; g < section.Groups.Count; g++)
                {
                    AnimalGroup group = section.Groups[g];

                    int takeable = bill.Takeable(group) - group.HuntOrdered;

                    if (takeable <= 0)
                        continue;

                    candidates.Add(group);
                    quota[group.Def] = takeable;
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref bills, "gideonHuntingBills", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (bills == null)
                    bills = new List<HuntingBill>();
                else
                    bills.RemoveAll(bill => bill == null);
            }
        }
    }
}
