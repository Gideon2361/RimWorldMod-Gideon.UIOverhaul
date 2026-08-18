using System;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Integrations
{
    /// <summary>
    /// Taming food, borrowed from nercury's <b>Assign Animal Food</b> so it can be set beside the policies a
    /// colonist already carries.
    ///
    /// <b>Taming food is a colonist's policy, not an animal's,</b> which is why it belongs on our pawn strip at
    /// all. It governs what a colonist takes with them when they go to tame something, so the mod's own column
    /// applies it to humanlike player pawns; the conditions here are that same test. Its two other slots, animal
    /// food and caravan food, are an animal's and are deliberately not brought over.
    ///
    /// <b>Every touch is reflective, because we do not reference the mod.</b> A hard reference would make this
    /// assembly refuse to load for everybody who does not have it, which is the one outcome a soft dependency
    /// exists to avoid. The members reached are:
    /// <list type="bullet">
    /// <item><c>AssignAnimalFood.CompPawnFoodPolicies</c>, a <c>ThingComp</c> the mod puts on pawns, holding
    /// <c>tamerPolicyId</c> as a plain int field. Negative means the colonist has not been given one.</item>
    /// <item><c>AssignAnimalFood.PolicyResolver.ResolveTamer(Pawn)</c>, which answers with the policy that would
    /// actually be used, falling back to the colony's own seed policy when the pawn has none.</item>
    /// </list>
    ///
    /// <b>The id is written, not a policy object,</b> which is how the mod's own column writes it. Ids come from
    /// RimWorld's <c>foodRestrictionDatabase</c>, so the list offered is the same list vanilla offers for food and
    /// a policy added by some third mod appears in it without anything here knowing.
    ///
    /// <b>Reflection is resolved once and latched.</b> If the mod's internals move in a future version, the
    /// lookup fails once, reports through UIGuard, and the strip simply does not offer the picker. Reflecting per
    /// frame per pawn would be both slow and a way to report the same failure a thousand times.
    /// </summary>
    internal static class AssignAnimalFoodIntegration
    {
        /// <summary>Lowercase, because that is how RimWorld normalizes a package id.</summary>
        internal const string PackageId = "nercury.assignanimalfood";

        private static bool resolved;
        private static bool usable;

        private static Type compType;
        private static FieldInfo tamerPolicyId;
        private static MethodInfo resolveTamer;

        /// <summary>
        /// Whether this pawn should be offered a taming food picker.
        ///
        /// The comp's presence is the last word and is checked rather than inferred: the mod adds it through a def
        /// patch, so which pawns carry one is its decision and not something to predict from race or faction.
        /// </summary>
        internal static bool Applies(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return false;

            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
                return false;

            return Comp(pawn) != null;
        }

        /// <summary>
        /// The policy this colonist would actually tame with, including the colony fallback when they have none
        /// of their own.
        ///
        /// The mod's own resolver is asked rather than the field read directly, so the fallback shown here is
        /// whatever the mod decides it is now, rather than a guess frozen at the time this was written.
        /// </summary>
        internal static FoodPolicy Current(Pawn pawn)
        {
            if (!Ready() || pawn == null)
                return null;

            return UIGuard.Try("Integrations.ReadTamingPolicy",
                () => resolveTamer.Invoke(null, new object[] { pawn }) as FoodPolicy, null,
                "The taming food picker shows nothing for this colonist.");
        }

        /// <summary>Gives this colonist a taming food policy.</summary>
        internal static void Set(Pawn pawn, FoodPolicy policy)
        {
            if (!Ready() || pawn == null || policy == null)
                return;

            UIGuard.Try("Integrations.SetTamingPolicy", () =>
            {
                object comp = Comp(pawn);

                if (comp != null)
                    tamerPolicyId.SetValue(comp, policy.id);
            }, "The taming food policy was not changed.");
        }

        private static object Comp(Pawn pawn)
        {
            if (!Ready() || pawn.AllComps == null)
                return null;

            // Walked rather than fetched with TryGetComp, which needs the comp's type as a generic argument and
            // therefore cannot be used for a type only known by name at run time.
            for (int i = 0; i < pawn.AllComps.Count; i++)
            {
                ThingComp comp = pawn.AllComps[i];

                if (comp != null && compType.IsInstanceOfType(comp))
                    return comp;
            }

            return null;
        }

        private static bool Ready()
        {
            if (resolved)
                return usable;

            resolved = true;
            usable = false;

            if (!ModIntegrations.Loaded(PackageId))
                return false;

            usable = UIGuard.Try("Integrations.BindAssignAnimalFood", () =>
            {
                compType = AccessTools.TypeByName("AssignAnimalFood.CompPawnFoodPolicies");
                Type resolver = AccessTools.TypeByName("AssignAnimalFood.PolicyResolver");

                if (compType == null || resolver == null)
                    return false;

                tamerPolicyId = AccessTools.Field(compType, "tamerPolicyId");
                resolveTamer = AccessTools.Method(resolver, "ResolveTamer", new[] { typeof(Pawn) });

                return tamerPolicyId != null && tamerPolicyId.FieldType == typeof(int) && resolveTamer != null;
            }, false, "Assign Animal Food is installed, but its taming policy could not be reached, so no "
                      + "taming food picker is offered.");

            return usable;
        }
    }
}
