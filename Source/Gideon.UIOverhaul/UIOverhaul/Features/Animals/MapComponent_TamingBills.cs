using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The map's taming bills, and the clock that acts on them.
    ///
    /// <b>A map component for the same reasons the hunting bills are one.</b> The instruction is about this map's
    /// herd and this map's wildlife, it has to survive a save, and it has to keep acting while the player is
    /// looking at something else.
    ///
    /// <b>It designates and nothing more.</b> A taming order is <c>DesignationDefOf.Tame</c> on a wild animal,
    /// which is exactly what a player clicking Tame produces, so every handler, work priority, skill requirement
    /// and interval in the game applies unchanged. This never moves a pawn, never starts a job, and never touches
    /// an animal that is already tame.
    /// </summary>
    public class MapComponent_TamingBills : MapComponent
    {
        /// <summary>Once every few seconds. Taming takes days, so anything finer is work for nothing.</summary>
        private const int IntervalTicks = 600;

        private List<TamingBill> bills = new List<TamingBill>();

        public MapComponent_TamingBills(Map map) : base(map)
        {
        }

        internal List<TamingBill> Bills
        {
            get
            {
                if (bills == null)
                    bills = new List<TamingBill>();

                return bills;
            }
        }

        internal static MapComponent_TamingBills For(Map map)
        {
            return map?.GetComponent<MapComponent_TamingBills>();
        }

        internal void Add(TamingBill bill)
        {
            if (bill != null && !Bills.Contains(bill))
                Bills.Add(bill);
        }

        internal void Remove(TamingBill bill)
        {
            if (bill == null)
                return;

            Bills.Remove(bill);

            // The orders it placed are left standing. A bill is an instruction to order, not a claim on what it
            // ordered: a handler already walking towards an animal has done work, and cancelling that because
            // somebody tidied up a bill list is a loss with no upside. The same rule the hunting bills follow.
        }

        internal void Move(TamingBill bill, int by)
        {
            UIGuard.Try("Animals.MoveTamingBill", () =>
            {
                int from = Bills.IndexOf(bill);

                if (from < 0)
                    return;

                int to = Mathf.Clamp(from + by, 0, Bills.Count - 1);

                if (to == from)
                    return;

                Bills.RemoveAt(from);
                Bills.Insert(to, bill);
            }, null);
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;

            if ((now + map.uniqueID) % IntervalTicks != 0)
                return;

            if (bills == null || bills.Count == 0)
                return;

            // Guarded per bill, as the hunting component is: vanilla catches around the whole component, so an
            // escape would abandon every bill after the one that threw, every interval, silently.
            for (int i = 0; i < bills.Count; i++)
            {
                TamingBill bill = bills[i];

                if (bill == null || bill.suspended || bill.Idle)
                    continue;

                TamingBill captured = bill;

                UIGuard.Try("Animals.TamingBill", () => Evaluate(captured),
                    "One taming bill stopped issuing orders. Other bills are unaffected, and taming can still be "
                    + "ordered by hand from the animals tab.");
            }
        }

        /// <summary>
        /// Orders taming for whatever this bill is short of.
        ///
        /// <b>Short is counted per species and per sex,</b> which is the whole point of the two numbers: a bill
        /// wanting two of each with three males and no females is short one female and not short at all of males,
        /// and ordering by total would tame a third male.
        ///
        /// <b>What is already ordered counts as arriving.</b> An outstanding tame order on a wild female muffalo
        /// is a female muffalo on the way, so it is subtracted from the shortfall. Without that the bill would
        /// re-order every interval until the first one finished, which for taming is days.
        /// </summary>
        private void Evaluate(TamingBill bill)
        {
            int outstanding = Outstanding(bill);

            if (outstanding >= bill.maxOutstanding)
                return;

            int budget = bill.maxOutstanding - outstanding;
            int ordered = 0;

            for (int i = 0; i < bill.targets.Count && budget > 0; i++)
            {
                TamingTarget target = bill.targets[i];

                if (target == null || target.species == null)
                    continue;

                ordered += Fill(bill, target, Gender.Female, ref budget);
                ordered += Fill(bill, target, Gender.Male, ref budget);

                // The total last, and it needs no arithmetic to keep from double counting: the two passes above
                // have already placed their designations and Coming reads those, so the shortfall this one sees
                // is only what is still missing after them.
                ordered += Fill(bill, target, null, ref budget);
            }

            if (ordered <= 0)
                return;

            bill.lastActedTick = Find.TickManager.TicksGame;
            bill.lastOrderedCount = ordered;

            AnimalRoster.Invalidate();
        }

        /// <summary>
        /// Orders up to the shortfall for one species, of one sex or of any sex.
        ///
        /// Females first at the call site above, because a herd that can grow needs them and a bill cut short by
        /// its own outstanding cap should spend what it has on the half that matters. The sexless pass runs last
        /// for the same reason: it will take whatever is nearest, so letting it go first could spend the whole
        /// budget on males and leave a breeding pair unfilled.
        ///
        /// A null <paramref name="gender"/> means the total: it counts and tames animals of every sex, including
        /// <c>Gender.None</c>, which is the only way a wraith is ever tamed.
        /// </summary>
        private int Fill(TamingBill bill, TamingTarget target, Gender? gender, ref int budget)
        {
            int wanted = target.Wanted(gender);

            if (wanted <= 0 || budget <= 0)
                return 0;

            int held = gender.HasValue
                ? TamingBill.Held(map, target.species, gender.Value)
                : TamingBill.HeldAny(map, target.species);

            int coming = Coming(target.species, gender);
            int shortfall = wanted - held - coming;

            if (shortfall <= 0)
                return 0;

            List<Pawn> candidates = Candidates(bill, target.species, gender);

            int placed = 0;

            for (int i = 0; i < candidates.Count && placed < shortfall && budget > 0; i++)
            {
                if (!Order(candidates[i]))
                    continue;

                placed++;
                budget--;
            }

            return placed;
        }

        /// <summary>
        /// Wild animals of this species and sex that this bill is willing to tame, best chance first.
        ///
        /// <b>The minimum chance is measured against the bill's own tamer,</b> falling back to the colony's best
        /// handler when none is assigned. That is what makes assigning one mean something: a bill planned around
        /// a novice refuses more than a bill planned around the animal handler who does this for a living.
        /// </summary>
        private List<Pawn> Candidates(TamingBill bill, ThingDef species, Gender? gender)
        {
            List<Pawn> found = new List<Pawn>();

            // Read-only in 1.6, so it is walked rather than assigned to a List.
            System.Collections.Generic.IReadOnlyList<Pawn> wild = map.mapPawns.AllPawnsSpawned;

            if (wild == null)
                return found;

            for (int i = 0; i < wild.Count; i++)
            {
                Pawn animal = wild[i];

                if (animal == null || animal.def != species)
                    continue;

                if (gender.HasValue && animal.gender != gender.Value)
                    continue;

                if (animal.Faction != null || animal.Dead)
                    continue;

                if (!AnimalDesignations.CanTame(animal))
                    continue;

                if (AnimalDesignations.Ordered(animal, DesignationDefOf.Tame))
                    continue;

                // A manhunting animal is not a taming candidate, whatever the odds say: nobody is walking up to
                // it, and the order would sit there until it calmed down or somebody shot it.
                if (animal.InAggroMentalState)
                    continue;

                float chance = AnimalFacts.TameOddsWith(animal, bill.tamer).Chance;

                // A negative chance means the game's own curve could not be read. Refusing on that would stop the
                // feature working entirely for a reason that has nothing to do with the animal, so an unknown
                // chance is allowed through and the guard simply does not apply.
                if (chance >= 0f && chance < bill.minTameChance)
                    continue;

                found.Add(animal);
            }

            found.Sort((a, b) => AnimalFacts.TameOddsWith(b, bill.tamer).Chance
                .CompareTo(AnimalFacts.TameOddsWith(a, bill.tamer).Chance));

            return found;
        }

        private bool Order(Pawn animal)
        {
            return UIGuard.Try("Animals.OrderTame", () =>
            {
                DesignationManager designations = map.designationManager;

                if (designations == null || designations.DesignationOn(animal, DesignationDefOf.Tame) != null)
                    return false;

                designations.AddDesignation(new Designation(animal, DesignationDefOf.Tame));

                return true;
            }, false, null);
        }

        /// <summary>How many taming orders are outstanding for anything this bill asked for.</summary>
        private int Outstanding(TamingBill bill)
        {
            int count = 0;

            for (int i = 0; i < bill.targets.Count; i++)
            {
                TamingTarget target = bill.targets[i];

                if (target == null || target.species == null)
                    continue;

                // One sexless call rather than the two sexed ones it used to add together. Those two missed
                // Gender.None entirely, so orders on sexless animals were invisible to the outstanding cap and
                // the bill could keep spending budget it had already spent.
                count += Coming(target.species, null);
            }

            return count;
        }

        /// <summary>
        /// Outstanding tame orders on wild animals of this species, of one sex or of any sex.
        ///
        /// A null sex counts every one of them, <c>Gender.None</c> included.
        /// </summary>
        private int Coming(ThingDef species, Gender? gender)
        {
            return UIGuard.Try("Animals.TameComing", () =>
            {
                DesignationManager designations = map.designationManager;

                if (designations == null)
                    return 0;

                List<Designation> all = designations.AllDesignations;

                if (all == null)
                    return 0;

                int count = 0;

                for (int i = 0; i < all.Count; i++)
                {
                    Designation designation = all[i];

                    if (designation == null || designation.def != DesignationDefOf.Tame)
                        continue;

                    Pawn animal = designation.target.Thing as Pawn;

                    if (animal == null || animal.def != species)
                        continue;

                    if (gender.HasValue && animal.gender != gender.Value)
                        continue;

                    count++;
                }

                return count;
            }, 0, null);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref bills, "gideonTamingBills", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && bills == null)
                bills = new List<TamingBill>();
        }
    }
}
