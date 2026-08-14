using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns.Templates
{
    /// <summary>
    /// The assign tab's policies, as a template records them: apparel, drug, food and reading, plus medical care,
    /// hostility response and self-tend.
    ///
    /// <b>Policies are referenced by label, and that is a compromise worth being explicit about.</b> Unlike a work
    /// type or a time assignment, a policy is not a def. <c>ApparelPolicy</c>, <c>DrugPolicy</c>,
    /// <c>FoodPolicy</c> and <c>ReadingPolicy</c> all derive from <c>Policy</c>, which carries an <c>id</c> and a
    /// <c>label</c>, and they live in databases belonging to the save rather than to the game's content. The id is
    /// an integer handed out per save, so it means nothing in another colony; the label is what the player typed
    /// and is the only part with meaning outside the save it came from.
    ///
    /// So a template says "the policy called Nudist", not "policy 4". Within one colony that is exact. Carried to
    /// another colony it finds a policy of the same name if one exists, and leaves the pawn's current policy alone
    /// if not. Renaming a policy breaks the link, which is the honest cost of the only key that travels.
    ///
    /// <b>Area restriction is deliberately absent.</b> It is in the same tab and looks like it belongs here, but an
    /// <c>Area</c> belongs to a map, not to a save: <c>Pawn_PlayerSettings</c> stores allowed areas per map. A
    /// template that set one would be meaningless on any other map, including a second map in the same colony, so
    /// leaving it out is better than carrying something that silently fails most of the time.
    /// </summary>
    public class PawnPolicySet
    {
        /// <summary>Label of the ApparelPolicy, or null to leave the pawn's alone.</summary>
        public string apparel;

        public string drug;

        public string food;

        public string reading;

        /// <summary>
        /// Nullable because "no opinion" has to be distinguishable from a real value, and every one of these is a
        /// non-nullable enum or bool whose default is a legitimate setting. Medical care defaults to NoMeds and
        /// self-tend to false, so a template that had simply never recorded them would otherwise read as one
        /// demanding no medicine and no self-tending.
        /// </summary>
        public MedicalCareCategory? medicalCare;

        public HostilityResponseMode? hostilityResponse;

        public bool? selfTend;

        /// <summary>Reads a pawn's current policies. Anything the pawn does not have a tracker for stays null.</summary>
        public static PawnPolicySet From(Pawn pawn)
        {
            PawnPolicySet set = new PawnPolicySet();

            if (pawn == null)
                return set;

            set.apparel = pawn.outfits?.CurrentApparelPolicy?.label;
            set.drug = pawn.drugs?.CurrentPolicy?.label;
            set.food = pawn.foodRestriction?.CurrentFoodPolicy?.label;
            set.reading = pawn.reading?.CurrentPolicy?.label;

            Pawn_PlayerSettings settings = pawn.playerSettings;

            if (settings != null)
            {
                set.medicalCare = settings.medCare;
                set.hostilityResponse = settings.hostilityResponse;
                set.selfTend = settings.selfTend;
            }

            return set;
        }

        /// <summary>
        /// Writes these policies onto a pawn, leaving alone anything this set has no opinion about and anything
        /// that cannot be resolved.
        /// </summary>
        /// <returns>
        /// The labels of policies that were named but could not be found, so the caller can tell the player which
        /// parts of the template did not land. Empty when everything applied.
        /// </returns>
        public void ApplyTo(Pawn pawn, System.Collections.Generic.List<string> unresolved)
        {
            if (pawn == null)
                return;

            ApplyApparel(pawn, unresolved);
            ApplyDrug(pawn, unresolved);
            ApplyFood(pawn, unresolved);
            ApplyReading(pawn, unresolved);

            Pawn_PlayerSettings settings = pawn.playerSettings;

            if (settings == null)
                return;

            if (medicalCare.HasValue)
                settings.medCare = medicalCare.Value;

            if (hostilityResponse.HasValue)
                settings.hostilityResponse = hostilityResponse.Value;

            if (selfTend.HasValue)
                settings.selfTend = selfTend.Value;
        }

        // Four near-identical methods rather than one generic one. The databases share no interface: each is a
        // differently named property on Game holding a differently named list, so a generic version would need
        // reflection or a delegate per policy type to reach them, and would be longer than this is.

        private void ApplyApparel(Pawn pawn, System.Collections.Generic.List<string> unresolved)
        {
            if (apparel.NullOrEmpty() || pawn.outfits == null)
                return;

            foreach (ApparelPolicy policy in Current.Game?.outfitDatabase?.AllOutfits
                                            ?? (System.Collections.Generic.IEnumerable<ApparelPolicy>)
                                            new ApparelPolicy[0])
            {
                if (Matches(policy?.label, apparel))
                {
                    pawn.outfits.CurrentApparelPolicy = policy;
                    return;
                }
            }

            unresolved?.Add("apparel policy \"" + apparel + "\"");
        }

        private void ApplyDrug(Pawn pawn, System.Collections.Generic.List<string> unresolved)
        {
            if (drug.NullOrEmpty() || pawn.drugs == null)
                return;

            foreach (DrugPolicy policy in Current.Game?.drugPolicyDatabase?.AllPolicies
                                          ?? (System.Collections.Generic.IEnumerable<DrugPolicy>)
                                          new DrugPolicy[0])
            {
                if (Matches(policy?.label, drug))
                {
                    pawn.drugs.CurrentPolicy = policy;
                    return;
                }
            }

            unresolved?.Add("drug policy \"" + drug + "\"");
        }

        private void ApplyFood(Pawn pawn, System.Collections.Generic.List<string> unresolved)
        {
            if (food.NullOrEmpty() || pawn.foodRestriction == null)
                return;

            foreach (FoodPolicy policy in Current.Game?.foodRestrictionDatabase?.AllFoodRestrictions
                                          ?? (System.Collections.Generic.IEnumerable<FoodPolicy>)
                                          new FoodPolicy[0])
            {
                if (Matches(policy?.label, food))
                {
                    pawn.foodRestriction.CurrentFoodPolicy = policy;
                    return;
                }
            }

            unresolved?.Add("food policy \"" + food + "\"");
        }

        private void ApplyReading(Pawn pawn, System.Collections.Generic.List<string> unresolved)
        {
            if (reading.NullOrEmpty() || pawn.reading == null)
                return;

            foreach (ReadingPolicy policy in Current.Game?.readingPolicyDatabase?.AllReadingPolicies
                                             ?? (System.Collections.Generic.IEnumerable<ReadingPolicy>)
                                             new ReadingPolicy[0])
            {
                if (Matches(policy?.label, reading))
                {
                    pawn.reading.CurrentPolicy = policy;
                    return;
                }
            }

            unresolved?.Add("reading policy \"" + reading + "\"");
        }

        /// <summary>
        /// Case-insensitive, because the player typed both sides of this comparison and a template that failed on
        /// "nudist" against "Nudist" would read as broken rather than as precise.
        /// </summary>
        private static bool Matches(string policyLabel, string wanted)
        {
            return !policyLabel.NullOrEmpty()
                   && policyLabel.Equals(wanted, System.StringComparison.OrdinalIgnoreCase);
        }

        public PawnPolicySet Clone()
        {
            return new PawnPolicySet
            {
                apparel = apparel,
                drug = drug,
                food = food,
                reading = reading,
                medicalCare = medicalCare,
                hostilityResponse = hostilityResponse,
                selfTend = selfTend
            };
        }
    }
}
