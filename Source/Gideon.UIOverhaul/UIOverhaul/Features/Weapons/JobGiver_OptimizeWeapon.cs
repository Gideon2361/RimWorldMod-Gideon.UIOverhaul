using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Weapons
{
    /// <summary>
    /// Sends a colonist to fetch the best weapon their policy allows, the way the apparel job giver sends them to
    /// re-dress.
    ///
    /// <b>This is the half of the feature that does not exist in vanilla, and it is worth being blunt about it.</b>
    /// RimWorld has no weapon optimization for colonists at all. <c>JobGiver_PickupDroppedWeapon</c> only
    /// retrieves the one weapon a pawn was already holding when they went down, and
    /// <c>JobGiver_PickUpOpportunisticWeapon</c> runs from raider and mercenary duties, never from the humanlike
    /// tree. So a weapons policy with no job giver behind it would be a control that changes nothing, which is
    /// the one thing worse than not having it.
    ///
    /// <b>It swaps; it does not disarm.</b> <c>JobGiver_OptimizeApparel</c> strips apparel a policy disallows on
    /// the spot, because the worst case is a colonist in the wrong shirt. The worst case here is a colonist
    /// standing unarmed in a raid because a policy was tightened while they were across the map from anything
    /// allowed, so a disallowed weapon is only ever put down as part of picking up an allowed one. Tightening a
    /// policy therefore changes what they will carry next rather than what they are holding now.
    ///
    /// <b>Ranged over melee, then value within each.</b> Vanilla's own weapon preference is a three-tier integer
    /// and there is no stat in the game that compares a rifle to a sword -- <c>MeleeWeapon_AverageDPS</c> has no
    /// ranged counterpart, which is not an oversight. So the tiers are vanilla's, and within a tier the ordering
    /// is market value for ranged and average DPS for melee: the first is what a player's own eye uses to rank
    /// guns, the second is the real answer where the game supplies one. No balance model is invented here, and
    /// the policy is the thing actually doing the choosing.
    /// </summary>
    public class JobGiver_OptimizeWeapon : ThinkNode_JobGiver
    {
        /// <summary>
        /// How long a colonist is left alone after a look that found nothing.
        ///
        /// One in-game hour. The search below walks the map's weapons and does a reachability test per candidate,
        /// which is the same order of cost as the apparel job giver's scan and wants the same treatment: vanilla
        /// throttles that one through <c>mindState.nextApparelOptimizeTick</c>, and there is no such field to
        /// borrow for weapons.
        /// </summary>
        private const int RestTicks = 2500;

        /// <summary>How far a colonist will walk for a better weapon.</summary>
        private const float SearchRadius = 40f;

        /// <summary>
        /// When each pawn may be looked at again.
        ///
        /// Not saved, and it does not need to be: the only cost of losing it is one extra scan per colonist after
        /// a load, and a throttle that lived in the save would be state this feature does not otherwise have.
        /// </summary>
        private static readonly Dictionary<int, int> Rested = new Dictionary<int, int>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Guarded because this runs from RimWorld's own think tree, on every colonist, several times a
            // second. An exception here is not a panel that fails to draw: it is a pawn with no job at all, over
            // and over, and a log that fills in seconds.
            return UIGuard.Try<Job>("Weapons.Optimize", () => Consider(pawn), null, null);
        }

        private static Job Consider(Pawn pawn)
        {
            if (!WeaponPolicies.Applies(pawn) || !pawn.Spawned || pawn.Drafted)
                return null;

            // Somebody carrying a weapon they were told to carry is not being second-guessed. Vanilla's forced
            // handler does the same job for apparel.
            if (pawn.equipment.Primary != null && pawn.equipment.Primary.def.destroyOnDrop)
                return null;

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return null;

            int next;

            if (Rested.TryGetValue(pawn.thingIDNumber, out next) && Find.TickManager.TicksGame < next)
                return null;

            Rested[pawn.thingIDNumber] = Find.TickManager.TicksGame + RestTicks;

            WeaponPolicies set = WeaponPolicies.Current;

            if (set == null)
                return null;

            WeaponPolicy policy = set.For(pawn);

            if (policy == null || policy.filter == null)
                return null;

            Thing wanted = Best(pawn, policy);

            if (wanted == null)
                return null;

            Job job = JobMaker.MakeJob(JobDefOf.Equip, wanted);

            job.expiryInterval = 100;
            job.checkOverrideOnExpire = true;

            return job;
        }

        /// <summary>
        /// The best allowed weapon within reach that beats what they are holding, or null.
        ///
        /// <b>Only what is in a stockpile,</b> which is the same rule the apparel job giver follows. A weapon
        /// lying where a raider dropped it is not the colony's to hand out yet, and a colonist wandering off
        /// mid-work to collect battlefield loot is a hauling decision rather than a policy one.
        /// </summary>
        private static Thing Best(Pawn pawn, WeaponPolicy policy)
        {
            Thing carried = pawn.equipment.Primary;

            // A weapon the policy no longer allows scores nothing, so anything allowed beats it. That is the
            // whole of how a tightened policy takes effect: the next allowed weapon wins rather than this one
            // being dropped where they stand.
            float best = carried != null && policy.filter.Allows(carried) ? Score(pawn, carried) : -1f;

            Thing found = null;

            List<Thing> weapons = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

            for (int i = 0; weapons != null && i < weapons.Count; i++)
            {
                Thing weapon = weapons[i];

                if (weapon == null || weapon == carried || !weapon.Spawned)
                    continue;

                if (!policy.filter.Allows(weapon) || !weapon.IsInAnyStorage())
                    continue;

                if (weapon.IsForbidden(pawn) || weapon.IsBurning())
                    continue;

                if ((weapon.Position - pawn.Position).LengthHorizontalSquared > SearchRadius * SearchRadius)
                    continue;

                if (!EquipmentUtility.CanEquip(weapon, pawn))
                    continue;

                if (weapon.def.IsRangedWeapon && pawn.WorkTagIsDisabled(WorkTags.Shooting))
                    continue;

                float score = Score(pawn, weapon);

                if (score <= best)
                    continue;

                // Reachability last, because it is the expensive one and most candidates have already been
                // ruled out on something cheaper by the time anything gets this far.
                if (!pawn.CanReserveAndReach(weapon, PathEndMode.OnCell, pawn.NormalMaxDanger()))
                    continue;

                best = score;
                found = weapon;
            }

            return found;
        }

        /// <summary>
        /// What a weapon is worth to this pawn. Higher is better; nothing scores below zero.
        ///
        /// The tier is multiplied out rather than added, so no amount of quality on a knife outranks a gun for
        /// somebody who can shoot -- which is the ordering vanilla's own integers express, kept intact.
        /// </summary>
        private static float Score(Pawn pawn, Thing weapon)
        {
            const float rangedTier = 1000000f;

            if (weapon.def.IsRangedWeapon)
                return rangedTier + Mathf.Max(0f, weapon.MarketValue);

            return Mathf.Max(0f, weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS));
        }
    }
}
