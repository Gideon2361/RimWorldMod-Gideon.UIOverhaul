using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>The picker's filter chips. Every operation falls into exactly one.</summary>
    internal enum HospitalOperationKind
    {
        /// <summary>Tending, curing, administering: nothing about the body changes permanently.</summary>
        Medical,

        /// <summary>An added body part: a peg leg, a bionic arm, an archotech eye.</summary>
        Prosthetic,

        /// <summary>Something installed that is not a limb: a joywire, a painstopper, a persona core.</summary>
        Implant,

        /// <summary>Taking something out or off, including amputations.</summary>
        Removal
    }

    /// <summary>
    /// One thing you could have done to a patient: a recipe, the parts it could be done to, and why not.
    ///
    /// <b>Body parts are a list rather than separate entries.</b> RimWorld's own menu offers "install bionic eye
    /// (left eye)" and "install bionic eye (right eye)" as two options; they are one operation and a choice, and
    /// collapsing them is what makes the list short enough to read.
    /// </summary>
    internal sealed class HospitalOperation
    {
        internal RecipeDef Recipe;

        /// <summary>The parts this could be done to, in RimWorld's own order. Empty when it targets no part.</summary>
        internal readonly List<BodyPartRecord> Parts = new List<BodyPartRecord>();

        internal string Label;

        internal HospitalOperationKind Kind;

        /// <summary>Why the game would refuse, or null when it would not.</summary>
        internal string Reason;

        /// <summary>Ingredients the map does not have. Empty when everything is in stock.</summary>
        internal readonly List<ThingDef> Missing = new List<ThingDef>();

        internal bool Possible
        {
            get { return Reason.NullOrEmpty() && Missing.Count == 0; }
        }
    }

    /// <summary>
    /// Everything the tab needs to know about operating on somebody.
    ///
    /// <b>The enumeration is RimWorld's own, test for test.</b> <c>HealthCardUtility.DrawMedOperationsTab</c> walks
    /// the patient's recipes, asks the worker whether it is available, asks which body parts it applies to and
    /// checks what is in stock. All of that is reproduced here rather than approximated, because an operation this
    /// screen offers and the game then refuses is worse than one it never offered.
    ///
    /// <b>With one deliberate departure.</b> Vanilla hides an operation whose missing ingredient is a tech hediff
    /// or a drug -- so "install bionic arm" simply is not in the menu when you have no bionic arm, and there is
    /// nothing to tell you that is why. Here it is listed with the reason on its face and the button refused, so
    /// the list doubles as a shopping list. That was the mockup's promise and it is the one place this knowingly
    /// shows more than the game would.
    ///
    /// <b>The bills it writes are the game's.</b> <c>HealthCardUtility.CreateSurgeryBill</c> is public and does
    /// the messages, the medicine warning and the concept unlock, so nothing here is a parallel system that could
    /// disagree with the operations tab.
    /// </summary>
    internal static class HospitalSurgery
    {
        /// <summary>Scratch for one enumeration. Never held past the end of a call.</summary>
        private static readonly List<ThingDef> MissingScratch = new List<ThingDef>();

        // -------------------------------------------------------------------------------------------
        // Classification
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Whether this recipe only hands the patient something, which is what makes it repeatable.
        ///
        /// <b>The test is the worker, not the name.</b> The administer family consumes an item and changes nothing
        /// permanent, so it can honestly run on a clock; an implant or an amputation cannot, because a person has
        /// two eyes and "keep four bionic eyes installed" is not a schedule. Matching on the worker means a mod's
        /// own administer recipe is covered and a mod's cleverly named implant is not.
        /// </summary>
        internal static bool IsDose(RecipeDef recipe)
        {
            if (recipe == null)
                return false;

            return UIGuard.Try("Hospital.IsDose", () =>
            {
                RecipeWorker worker = recipe.Worker;

                return worker is Recipe_AdministerIngestible || worker is Recipe_AdministerUsableItem;
            }, false, null);
        }

        /// <summary>The drug a dose recipe hands over, or null when this is not a dose.</summary>
        internal static ThingDef DrugOf(RecipeDef recipe)
        {
            if (!IsDose(recipe))
                return null;

            return UIGuard.Try<ThingDef>("Hospital.DrugOf", () =>
            {
                if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                    return null;

                List<ThingDef> allowed = recipe.ingredients[0].filter.AllowedThingDefs as List<ThingDef>;

                if (allowed != null)
                    return allowed.Count > 0 ? allowed[0] : null;

                foreach (ThingDef def in recipe.ingredients[0].filter.AllowedThingDefs)
                    return def;

                return null;
            }, null, null);
        }

        internal static HospitalOperationKind KindOf(RecipeDef recipe)
        {
            return UIGuard.Try("Hospital.KindOf", () =>
            {
                RecipeWorker worker = recipe.Worker;

                if (worker is Recipe_RemoveBodyPart || worker is Recipe_RemoveImplant
                                                    || worker is Recipe_RemoveHediff)
                    return HospitalOperationKind.Removal;

                if (recipe.addsHediff == null)
                    return HospitalOperationKind.Medical;

                // An added part has the props that make it show up as a limb on the health tab; anything else that
                // adds a hediff is an implant sitting inside an otherwise intact body.
                return recipe.addsHediff.addedPartProps != null
                    ? HospitalOperationKind.Prosthetic
                    : HospitalOperationKind.Implant;
            }, HospitalOperationKind.Medical, null);
        }

        // -------------------------------------------------------------------------------------------
        // Surgeons
        // -------------------------------------------------------------------------------------------

        /// <summary>The Medicine level this recipe asks for, or zero when it asks for none.</summary>
        internal static int RequiredSkill(RecipeDef recipe)
        {
            if (recipe == null || recipe.skillRequirements == null)
                return 0;

            return UIGuard.Try("Hospital.RequiredSkill", () =>
            {
                int highest = 0;

                for (int i = 0; i < recipe.skillRequirements.Count; i++)
                {
                    SkillRequirement requirement = recipe.skillRequirements[i];

                    if (requirement != null && requirement.skill == SkillDefOf.Medicine)
                        highest = Mathf.Max(highest, requirement.minLevel);
                }

                return highest;
            }, 0, null);
        }

        internal static int SkillOf(Pawn pawn)
        {
            return UIGuard.Try("Hospital.SkillOf",
                () => pawn.skills != null ? pawn.skills.GetSkill(SkillDefOf.Medicine).Level : 0, 0, null);
        }

        /// <summary>
        /// Everybody on this map who could actually perform this operation, best first.
        ///
        /// <b>Capability rather than the work tab.</b> Somebody with doctoring switched off still appears, because
        /// the answer to "who could do this" is a fact about the colony and turning their priority back on is one
        /// click. Somebody whose skill is too low does not appear, because no amount of clicking fixes that.
        /// </summary>
        internal static void Surgeons(Map map, RecipeDef recipe, Pawn patient, List<Pawn> into)
        {
            if (into == null)
                return;

            into.Clear();

            if (map == null || recipe == null)
                return;

            UIGuard.Try("Hospital.Surgeons", () =>
            {
                List<Pawn> candidates = map.mapPawns.FreeColonistsSpawned;

                if (candidates == null)
                    return;

                for (int i = 0; i < candidates.Count; i++)
                {
                    Pawn pawn = candidates[i];

                    if (pawn == null || pawn == patient || pawn.Dead)
                        continue;

                    if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                        continue;

                    if (!recipe.PawnSatisfiesSkillRequirements(pawn))
                        continue;

                    into.Add(pawn);
                }

                into.SortByDescending(pawn => ChanceFor(recipe, pawn, patient, null));
            }, null);
        }

        /// <summary>The likeliest surgeon on this map, or null when nobody qualifies.</summary>
        internal static Pawn BestSurgeon(Map map, RecipeDef recipe, Pawn patient)
        {
            List<Pawn> found = new List<Pawn>();

            Surgeons(map, recipe, patient, found);

            return found.Count > 0 ? found[0] : null;
        }

        /// <summary>
        /// The chance this surgeon has of getting it right, as the game itself will work it out.
        ///
        /// <b>This is not an estimate.</b> <c>SurgeryOutcomeEffectDef.GetQuality</c> is the number the success
        /// roll is made against -- the first outcome in the list succeeds on exactly this chance -- and every comp
        /// it runs is a pure read: the surgeon's stat, the bed and room, the recipe's own factor, an inspiration,
        /// and the clamp to 98 percent. Calling it costs nothing and changes nothing.
        ///
        /// <b>What it cannot know is the medicine.</b> The bill has not been written yet, so there is nothing to
        /// read a potency off and the medicine comp returns its neutral one. Glitterworld will beat this number
        /// and herbal will fall short of it, which is why the picker says so under the figure rather than leaving
        /// a confident number to be wrong.
        /// </summary>
        internal static float ChanceFor(RecipeDef recipe, Pawn surgeon, Pawn patient, BodyPartRecord part)
        {
            if (recipe == null || surgeon == null || patient == null)
                return 0f;

            return UIGuard.Try("Hospital.Chance", () =>
            {
                if (recipe.surgeryOutcomeEffect == null)
                    return 1f;

                return Mathf.Clamp01(recipe.surgeryOutcomeEffect.GetQuality(recipe, surgeon, patient, null, part,
                    null));
            }, 0f, null);
        }

        // -------------------------------------------------------------------------------------------
        // Enumeration
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Every operation this patient could have, and the ones the game would refuse with the reason attached.
        ///
        /// The shape of the walk is vanilla's, from <c>HealthCardUtility</c>: available recipes, the worker's own
        /// acceptance report, the parts it applies to, and what is missing from the map. What differs is only
        /// which of those results are shown.
        /// </summary>
        internal static void Options(Pawn patient, List<HospitalOperation> into)
        {
            if (into == null)
                return;

            into.Clear();

            if (patient == null || patient.def == null)
                return;

            UIGuard.Try("Hospital.Options", () => Enumerate(patient, into),
                "The list of operations could not be built. Operations can still be queued from the pawn's own "
                + "health tab.");
        }

        private static void Enumerate(Pawn patient, List<HospitalOperation> into)
        {
            List<RecipeDef> recipes = patient.def.AllRecipes;

            if (recipes == null)
                return;

            Map map = patient.MapHeld;

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];

                if (recipe == null || !recipe.AvailableNow)
                    continue;

                AcceptanceReport report = recipe.Worker.AvailableReport(patient);

                // Vanilla's own visibility rule: accepted, or refused with something worth reading. A silent
                // refusal means the operation makes no sense for this pawn at all.
                if (!report.Accepted && report.Reason.NullOrEmpty())
                    continue;

                HospitalOperation option = new HospitalOperation
                {
                    Recipe = recipe,
                    Kind = KindOf(recipe),
                    Reason = report.Accepted ? null : report.Reason
                };

                MissingScratch.Clear();

                foreach (ThingDef missing in recipe.PotentiallyMissingIngredients(null, map))
                {
                    if (missing != null)
                        MissingScratch.Add(missing);
                }

                option.Missing.AddRange(MissingScratch);
                MissingScratch.Clear();

                if (recipe.targetsBodyPart)
                {
                    foreach (BodyPartRecord part in recipe.Worker.GetPartsToApplyOn(patient, recipe))
                    {
                        if (recipe.AvailableOnNow(patient, part))
                            option.Parts.Add(part);
                    }

                    if (option.Parts.Count == 0)
                        continue;
                }
                else if (patient.health != null && patient.health.hediffSet != null
                                                && recipe.addsHediff != null
                                                && patient.health.hediffSet.HasHediff(recipe.addsHediff))
                {
                    continue;
                }

                option.Label = LabelFor(recipe, patient, option.Parts.Count > 0 ? option.Parts[0] : null);

                into.Add(option);
            }

            into.SortBy(option => (int) option.Kind, option => option.Label);
        }

        /// <summary>
        /// What to call an operation, without the body part.
        ///
        /// The part is left off on purpose: it is a choice made in the right hand pane rather than part of the
        /// name, which is the whole reason six eye operations collapse into one row.
        /// </summary>
        private static string LabelFor(RecipeDef recipe, Pawn patient, BodyPartRecord part)
        {
            return UIGuard.Try("Hospital.OptionLabel",
                () => recipe.Worker.GetLabelWhenUsedOn(patient, part).CapitalizeFirst(),
                recipe.LabelCap.ToString(), null);
        }

        // -------------------------------------------------------------------------------------------
        // Stock
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// How many of a thing the colony has to hand on this map.
        ///
        /// <b>Counted off the thing lister rather than the resource counter,</b> because a bionic arm is not a
        /// resource: the counter only tracks things the game files as such, and asking it about a prosthetic
        /// returns a confident zero. Stack counts are summed so medicine reads as fourteen rather than as three
        /// stacks.
        /// </summary>
        internal static int Stock(Map map, ThingDef def)
        {
            if (map == null || def == null)
                return 0;

            return UIGuard.Try("Hospital.Stock", () =>
            {
                List<Thing> things = map.listerThings.ThingsOfDef(def);

                if (things == null)
                    return 0;

                int count = 0;

                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];

                    if (thing != null && !thing.IsForbidden(Faction.OfPlayer))
                        count += thing.stackCount;
                }

                return count;
            }, 0, null);
        }

        // -------------------------------------------------------------------------------------------
        // Committing
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Writes the bill, through RimWorld's own method and with its own confirmations.
        ///
        /// <b>The confirmations are not decoration.</b> Installing a royal implant can break a title, and a
        /// euthanasia recipe asks outright; both live in <c>RecipeWorker</c> and in <c>CompRoyalImplant</c>, and
        /// skipping them would let this screen do quietly what the game insists on asking about.
        /// </summary>
        internal static void Queue(Pawn patient, RecipeDef recipe, BodyPartRecord part, Pawn nurse = null)
        {
            UIGuard.Try("Hospital.Queue", () =>
            {
                HediffDef adds = recipe.addsHediff ?? recipe.changesHediffLevel;

                if (adds != null)
                {
                    TaggedString violation = CompRoyalImplant.CheckForViolations(patient, adds,
                        recipe.hediffLevelOffset);

                    if (!violation.NullOrEmpty())
                    {
                        Confirm(violation, patient, recipe, part, nurse);

                        return;
                    }
                }

                TaggedString confirmation = recipe.Worker.GetConfirmation(patient);

                if (!confirmation.NullOrEmpty())
                {
                    Confirm(confirmation, patient, recipe, part, nurse);

                    return;
                }

                Write(patient, recipe, part, nurse);
            }, "The operation could not be queued. It can still be added from the pawn's own health tab.");
        }

        private static void Confirm(TaggedString text, Pawn patient, RecipeDef recipe, BodyPartRecord part,
            Pawn nurse)
        {
            Find.WindowStack.Add(new Dialog_MessageBox(text, "Yes".Translate(),
                () => Write(patient, recipe, part, nurse), "No".Translate()));
        }

        /// <summary>
        /// The bill itself, plus the nurse if one was named.
        ///
        /// <b>A nurse is RimWorld's own pawn restriction,</b> which is exactly the semantics wanted: nobody else
        /// is offered the job, and the named person is not made to drop what they are doing. The same shape as the
        /// assigned studier, and this time the game already had the field.
        /// </summary>
        internal static Bill_Medical Write(Pawn patient, RecipeDef recipe, BodyPartRecord part, Pawn nurse)
        {
            Bill_Medical bill = HealthCardUtility.CreateSurgeryBill(patient, recipe, part);

            if (bill != null && nurse != null)
                bill.SetPawnRestriction(nurse);

            HospitalRoster.Invalidate();

            return bill;
        }
    }
}
