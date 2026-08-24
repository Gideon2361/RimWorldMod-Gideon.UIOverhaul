using System.Collections.Generic;
using System.Linq;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Weapons
{
    /// <summary>
    /// Every weapon policy in the colony, and which colonist is on which.
    ///
    /// <b>Both halves live here because neither has anywhere else to go.</b> Apparel gets a
    /// <c>Game.outfitDatabase</c> for the list and a <c>Pawn_OutfitTracker</c> for the assignment; weapons have
    /// neither, and there is no seam on <c>Pawn</c> to add one without patching. A <c>GameComponent</c> is the
    /// game's own answer for "state a mod needs saved alongside the colony", and RimWorld builds one of every
    /// subclass automatically -- no def, no registration, nothing to keep in step.
    ///
    /// <b>And it is the save-safe answer, which a comp on the pawn would not be.</b> A component whose class is
    /// missing on load is skipped entirely, so a save made with this mod opens without it and loses exactly this
    /// data and nothing else. Everything referencing a <c>WeaponPolicy</c> is inside this component's own block,
    /// so there is no dangling reference anywhere in the save to resolve.
    ///
    /// <b>Assignments are references on both sides,</b> which means they survive a pawn being renamed, downed,
    /// captured or carried into a caravan -- and go null rather than dangling when a pawn is destroyed for good.
    /// The prune after load is what clears those, since RimWorld resolves a dead reference to null and leaves the
    /// entry sitting there.
    /// </summary>
    public class WeaponPolicies : GameComponent
    {
        private List<WeaponPolicy> policies = new List<WeaponPolicy>();

        private Dictionary<Pawn, WeaponPolicy> assigned = new Dictionary<Pawn, WeaponPolicy>();

        /// <summary>Scribe scratch. <c>Scribe_Collections</c> writes a dictionary through two parallel lists.</summary>
        private List<Pawn> scribedPawns;

        private List<WeaponPolicy> scribedPolicies;

        public WeaponPolicies(Game game)
        {
        }

        /// <summary>The colony's set, or null outside a game.</summary>
        internal static WeaponPolicies Current
        {
            get
            {
                return UIGuard.Try("Weapons.Component",
                    () => Verse.Current.Game != null ? Verse.Current.Game.GetComponent<WeaponPolicies>() : null,
                    null, null);
            }
        }

        /// <summary>
        /// Every policy, never empty.
        ///
        /// The starting set is built on first read rather than in the constructor: a component is constructed
        /// before the def database is usable on a load, and the categories below have to resolve.
        /// </summary>
        internal List<WeaponPolicy> All
        {
            get
            {
                if (policies.Count == 0)
                    Seed();

                return policies;
            }
        }

        /// <summary>
        /// Whether this pawn is one the policy applies to.
        ///
        /// <b>The same four questions the game asks before letting anybody carry anything,</b> rather than a
        /// guess: something to equip with, the colony's own, humanlike, and not somebody violence is disabled
        /// for. A pacifist has no weapon decision to make and offering them one would be a control that cannot
        /// do anything.
        /// </summary>
        internal static bool Applies(Pawn pawn)
        {
            return UIGuard.Try("Weapons.Applies", () =>
            {
                if (pawn == null || pawn.equipment == null || pawn.Destroyed)
                    return false;

                if (pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike)
                    return false;

                if (pawn.IsMutant && pawn.mutant.Def.disablePolicies)
                    return false;

                return !pawn.WorkTagIsDisabled(WorkTags.Violent);
            }, false, null);
        }

        /// <summary>The policy this pawn is on, falling back to the default rather than to null.</summary>
        internal WeaponPolicy For(Pawn pawn)
        {
            if (pawn == null)
                return null;

            WeaponPolicy held;

            if (assigned.TryGetValue(pawn, out held) && held != null)
                return held;

            return Default();
        }

        internal void Set(Pawn pawn, WeaponPolicy policy)
        {
            if (pawn == null)
                return;

            if (policy == null)
                assigned.Remove(pawn);
            else
                assigned[pawn] = policy;
        }

        internal WeaponPolicy Default()
        {
            return All[0];
        }

        /// <summary>Moves a policy to the front, which is what "default" means here and in the outfit database.</summary>
        internal void SetDefault(WeaponPolicy policy)
        {
            int index = policies.IndexOf(policy);

            if (index < 0)
                return;

            policies[index] = policies[0];
            policies[0] = policy;
        }

        internal WeaponPolicy MakeNew()
        {
            int id = policies.Any() ? policies.Max(policy => policy.id) + 1 : 1;

            WeaponPolicy made = new WeaponPolicy(id, "Weapons " + id);

            Allow(made, ThingCategoryDefOf.Weapons);

            policies.Add(made);

            return made;
        }

        /// <summary>
        /// Refuses to delete a policy somebody is on, which is the rule the outfit database follows.
        ///
        /// Naming the colonist is the whole value of the refusal: "in use" without a name means opening the
        /// pawns tab and reading down a column.
        /// </summary>
        internal AcceptanceReport TryDelete(WeaponPolicy policy)
        {
            if (policies.Count <= 1)
                return new AcceptanceReport("The last weapon policy cannot be deleted.");

            foreach (KeyValuePair<Pawn, WeaponPolicy> pair in assigned)
            {
                if (pair.Value == policy && pair.Key != null && !pair.Key.Destroyed)
                    return new AcceptanceReport(pair.Key.LabelShortCap + " is on this policy.");
            }

            policies.Remove(policy);

            return AcceptanceReport.WasAccepted;
        }

        /// <summary>
        /// The three a colony starts with.
        ///
        /// <b>Anything, ranged and melee, because those are the three orders a player actually gives.</b> The
        /// apparel database seeds by temperature and by worker versus soldier; the equivalent question for a
        /// weapon is which hand it is used with, and everything finer than that is a choice worth making
        /// deliberately in the manager.
        ///
        /// A missing subcategory leaves that policy allowing everything rather than allowing nothing, which is
        /// the safe direction: a policy that permits too much is visible and fixable, one that permits nothing
        /// leaves a colonist standing unarmed with no explanation.
        /// </summary>
        private void Seed()
        {
            WeaponPolicy anything = MakeNew();
            anything.label = "Anything";

            ThingCategoryDef melee = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("WeaponsMelee");
            ThingCategoryDef ranged = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("WeaponsRanged");

            if (ranged != null)
            {
                WeaponPolicy only = MakeNew();
                only.label = "Ranged only";

                Allow(only, melee, false);
            }

            if (melee != null)
            {
                WeaponPolicy only = MakeNew();
                only.label = "Melee only";

                Allow(only, ranged, false);
            }
        }

        private static void Allow(WeaponPolicy policy, ThingCategoryDef category, bool allow = true)
        {
            if (policy != null && category != null)
                policy.filter.SetAllow(category, allow);
        }

        /// <summary>
        /// Never guarded: this is the save. See <see cref="WeaponPolicy.ExposeData"/>.
        ///
        /// The policies are written before the assignments, because the assignments are references into them and
        /// a reference can only resolve to something the loader has already met.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref policies, "policies", LookMode.Deep);
            Scribe_Collections.Look(ref assigned, "assigned", LookMode.Reference, LookMode.Reference,
                ref scribedPawns, ref scribedPolicies);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            if (policies == null)
                policies = new List<WeaponPolicy>();

            if (assigned == null)
                assigned = new Dictionary<Pawn, WeaponPolicy>();

            Prune();
        }

        /// <summary>
        /// Drops entries whose pawn or policy no longer exists.
        ///
        /// A reference to something the save no longer contains resolves to null rather than failing, so without
        /// this the dictionary accumulates a null-keyed entry per dead colonist -- and a second null key is a
        /// duplicate-key exception on the next add.
        /// </summary>
        private void Prune()
        {
            List<Pawn> gone = null;

            foreach (KeyValuePair<Pawn, WeaponPolicy> pair in assigned)
            {
                if (pair.Key != null && pair.Value != null)
                    continue;

                if (gone == null)
                    gone = new List<Pawn>();

                gone.Add(pair.Key);
            }

            for (int i = 0; gone != null && i < gone.Count; i++)
                assigned.Remove(gone[i]);
        }
    }
}
