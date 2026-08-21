using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>What applying a template would do, or did.</summary>
    internal sealed class BillTemplateOutcome
    {
        /// <summary>False when the template cannot be used here at all. <see cref="Unusable"/> says why.</summary>
        internal bool Usable = true;

        internal string Unusable;

        /// <summary>How many settings were carried across.</summary>
        internal int Applied;

        /// <summary>
        /// What did not travel, in words the player can act on.
        ///
        /// A skipped value leaves the bill's current one alone. Nothing here is ever replaced by a guess.
        /// </summary>
        internal List<string> Skipped = new List<string>();

        /// <summary>Ingredient defs the template names that this game does not have.</summary>
        internal List<string> MissingDefs = new List<string>();

        internal int FoundDefs;
        internal int WantedDefs;

        /// <summary>The line the window puts under the Apply button.</summary>
        internal string Line()
        {
            if (!Usable)
                return Unusable;

            string applied = "Applies " + Applied + (Applied == 1 ? " setting" : " settings");

            return Skipped.Count == 0
                ? applied + ", skips nothing."
                : applied + ", skips " + Skipped.Count + ".";
        }
    }

    /// <summary>
    /// Turns a live bill into a template, and a template back onto a live bill.
    ///
    /// <b>Separated from the store for the same reason <c>SaveSweepDefs</c> is separated from the scanner.</b>
    /// Whether <c>MakeComponentIndustrial</c> is still a recipe is a question only a loaded game can answer, so the
    /// store and the file format stay free of the game and can be exercised from a harness, while everything that
    /// needs a <c>DefDatabase</c> or a <c>Map</c> lives here.
    ///
    /// <b>Preview and apply are the same walk.</b> Passing no bill counts what would happen and changes nothing;
    /// passing one does it. Two code paths that could disagree about what is about to happen would make the
    /// confirmation line worthless, which is the lesson the save sweep already paid for.
    ///
    /// <b>Nothing unresolved is guessed.</b> A value that cannot be carried across leaves the bill's current one
    /// exactly as it was, and is named in the report instead.
    /// </summary>
    internal static class BillTemplateApply
    {
        /// <summary>Captures a bill's whole configuration, including the parts that will not travel.</summary>
        internal static BillTemplate Capture(Bill_Production bill, string name)
        {
            return UIGuard.Try("Bills.Templates.Capture", () => Read(bill, name), null,
                "That bill could not be saved as a template.");
        }

        private static BillTemplate Read(Bill_Production bill, string name)
        {
            if (bill == null)
                return null;

            BillTemplate template = new BillTemplate
            {
                Name = name,
                Kind = BillTemplateKind.Bill,
                Origin = bill.billStack?.billGiver?.LabelShort,
                Saved = DateTime.Now.ToString("yyyy-MM-dd"),
                Recipe = bill.recipe?.defName,
                RepeatMode = bill.repeatMode?.defName,
                StoreMode = bill.GetStoreMode()?.defName,
                RepeatCount = bill.repeatCount,
                TargetCount = bill.targetCount,
                PauseWhenSatisfied = bill.pauseWhenSatisfied,
                UnpauseWhenYouHave = bill.unpauseWhenYouHave,
                SearchRadius = bill.ingredientSearchRadius,
                IncludeEquipped = bill.includeEquipped,
                IncludeTainted = bill.includeTainted,
                LimitToAllowedStuff = bill.limitToAllowedStuff,
                SlavesOnly = bill.SlavesOnly,
                MechsOnly = bill.MechsOnly,
                NonMechsOnly = bill.NonMechsOnly,
                HpMin = bill.hpRange.min,
                HpMax = bill.hpRange.max,
                QualityMin = bill.qualityRange.min.ToString(),
                QualityMax = bill.qualityRange.max.ToString(),
                SkillMin = bill.allowedSkillRange.min,
                SkillMax = bill.allowedSkillRange.max,

                // Both of these are colony bound and are stored so the report can name what it is dropping,
                // never so that they can be applied somewhere else.
                WorkerName = bill.PawnRestriction?.LabelShortCap,
                StoreZone = Label(bill.GetSlotGroup())
            };

            Fill(template, bill.ingredientFilter);

            return template;
        }

        /// <summary>Captures an ingredient filter on its own, for reuse across unrelated bills.</summary>
        internal static BillTemplate CaptureFilter(ThingFilter filter, string name, string origin)
        {
            return UIGuard.Try("Bills.Templates.CaptureFilter", () =>
            {
                BillTemplate template = new BillTemplate
                {
                    Name = name,
                    Kind = BillTemplateKind.Filter,
                    Origin = origin,
                    Saved = DateTime.Now.ToString("yyyy-MM-dd")
                };

                Fill(template, filter);

                return template;
            }, null, "That filter could not be saved as a template.");
        }

        private static void Fill(BillTemplate template, ThingFilter filter)
        {
            if (filter == null)
                return;

            foreach (ThingDef def in filter.AllowedThingDefs)
            {
                if (def != null)
                    template.Allowed.Add(def.defName);
            }

            template.Allowed.Sort(StringComparer.Ordinal);
        }

        /// <summary>Counts what applying would do, without touching anything.</summary>
        internal static BillTemplateOutcome Preview(BillTemplate template, Map map)
        {
            return UIGuard.Try("Bills.Templates.Preview", () => Run(template, null, map),
                new BillTemplateOutcome { Usable = false, Unusable = "This template could not be read." }, null);
        }

        /// <summary>
        /// Every bill on a bench as one template.
        ///
        /// <b>The order is kept because the order is the priority.</b> A bench's bills are worked top down, so a
        /// template that sorted them would import a working setup that behaves differently from the one it was
        /// taken from.
        ///
        /// The bench's own def is recorded so applying it elsewhere can say whether the bench matches. See the
        /// note on <see cref="BillTemplate.BenchDef"/> for why a mismatch warns rather than refuses.
        /// </summary>
        internal static BillTemplate CaptureBench(Building_WorkTable bench, string name)
        {
            return UIGuard.Try("Bills.Templates.CaptureBench", () =>
            {
                if (bench == null)
                    return null;

                BillTemplate template = new BillTemplate
                {
                    Name = name,
                    Kind = BillTemplateKind.Bench,
                    BenchDef = bench.def?.defName,
                    Origin = bench.LabelCap,
                    Saved = DateTime.Now.ToString("yyyy-MM-dd")
                };

                List<Bill> bills = bench.billStack?.Bills;

                if (bills == null)
                    return template;

                foreach (Bill bill in bills)
                {
                    // Only production bills carry anything a template can hold. Anything else a mod has put on
                    // the bench is left behind rather than saved as an empty entry that would import as nothing.
                    if (!(bill is Bill_Production production))
                        continue;

                    BillTemplate child = Capture(production, production.LabelCap);

                    if (child != null)
                        template.Bills.Add(child);
                }

                return template;
            }, null, "That bench could not be saved as a template.");
        }

        /// <summary>
        /// Makes a new bill on a bench from a bill template.
        ///
        /// <b>This is the half the feature was missing.</b> Applying a template used to need a bill to apply it
        /// onto, so a template was a way to reconfigure something that already existed rather than a way to set
        /// something up. Asked for by Aaron on 2026-08-20: choose a template, choose a bench, get the bill.
        ///
        /// <b>The bench has to offer the recipe.</b> A bill is created by the recipe, not by us, and putting one
        /// on a bench whose def does not list it makes a bill nothing will ever work. That is refused rather than
        /// created, and named, which is the same treatment a missing def already gets.
        ///
        /// The settings are then applied by the ordinary <see cref="Apply"/> walk, so there is exactly one place
        /// that knows what travels onto a bill and what does not.
        /// </summary>
        internal static BillTemplateOutcome CreateOn(BillTemplate template, Building_WorkTable bench)
        {
            return UIGuard.Try("Bills.Templates.Create", () => Create(template, bench),
                new BillTemplateOutcome { Usable = false, Unusable = "That template could not be applied." },
                "No bill was created. The bench is unchanged.");
        }

        private static BillTemplateOutcome Create(BillTemplate template, Building_WorkTable bench)
        {
            BillTemplateOutcome outcome = new BillTemplateOutcome();

            if (template == null || bench == null)
            {
                outcome.Usable = false;
                outcome.Unusable = "There is nothing to apply, or nowhere to apply it.";

                return outcome;
            }

            if (template.Kind != BillTemplateKind.Bill)
            {
                outcome.Usable = false;
                outcome.Unusable = "Only a bill template can make a bill.";

                return outcome;
            }

            RecipeDef recipe = template.Recipe.NullOrEmpty()
                ? null
                : DefDatabase<RecipeDef>.GetNamedSilentFail(template.Recipe);

            if (recipe == null)
            {
                outcome.Usable = false;
                outcome.Unusable = "Needs a recipe this game does not have: " + template.Recipe;

                return outcome;
            }

            if (bench.def?.AllRecipes == null || !bench.def.AllRecipes.Contains(recipe))
            {
                outcome.Usable = false;
                outcome.Unusable = bench.LabelCap + " cannot make " + recipe.label + ".";

                return outcome;
            }

            if (bench.billStack == null || bench.billStack.Count >= BillCap.Current)
            {
                outcome.Usable = false;
                outcome.Unusable = bench.LabelCap + " already has its " + BillCap.Current + " bills.";

                return outcome;
            }

            // Made by the recipe and added before the settings are applied, because applying a store mode reads
            // the bill's own bench to resolve a stockpile: a bill not yet on a stack has no map to look at.
            Bill bill = recipe.MakeNewBill();

            bench.billStack.AddBill(bill);

            if (!(bill is Bill_Production production))
            {
                outcome.Applied = 0;

                outcome.Skipped.Add("This recipe makes a kind of bill a template cannot configure, so it was "
                                    + "added with its own defaults.");

                return outcome;
            }

            return Run(template, production, bench.Map);
        }

        /// <summary>
        /// Puts every bill in a bench template onto a bench.
        ///
        /// <b>Added to what is there, never replacing it.</b> Clearing the bench first would be the tidier import
        /// and is not this mod's to do: bills are player configuration, and no import is worth silently deleting
        /// a set of them. A bench that already has bills ends up with both, which is visible and undoable.
        ///
        /// <b>Each bill is judged on its own.</b> One the bench cannot make is skipped and named, and the rest
        /// still arrive, which is the rule Aaron set for template import in the first place.
        /// </summary>
        internal static BillTemplateOutcome ApplyBench(BillTemplate template, Building_WorkTable bench)
        {
            return UIGuard.Try("Bills.Templates.ApplyBench", () =>
            {
                BillTemplateOutcome outcome = new BillTemplateOutcome();

                if (template == null || bench == null || template.Kind != BillTemplateKind.Bench)
                {
                    outcome.Usable = false;
                    outcome.Unusable = "That is not a bench template.";

                    return outcome;
                }

                if (!template.BenchDef.NullOrEmpty() && bench.def != null
                                                     && template.BenchDef != bench.def.defName)
                {
                    // A warning rather than a refusal. Two benches of different defs share most of their recipes
                    // often enough that refusing would cost more than it protects, and every bill the target
                    // cannot make is skipped below anyway.
                    outcome.Skipped.Add("Saved from a different bench, so some bills may not fit.");
                }

                foreach (BillTemplate child in template.Bills)
                {
                    BillTemplateOutcome one = Create(child, bench);

                    if (one.Usable)
                    {
                        outcome.Applied++;

                        continue;
                    }

                    outcome.Skipped.Add((child?.Name ?? "A bill") + ": " + one.Unusable);
                }

                return outcome;
            }, new BillTemplateOutcome { Usable = false, Unusable = "That bench template could not be applied." },
                "No bills were added. The bench is unchanged.");
        }

        /// <summary>Applies the template onto a bill, returning what travelled and what did not.</summary>
        internal static BillTemplateOutcome Apply(BillTemplate template, Bill_Production bill, Map map)
        {
            return UIGuard.Try("Bills.Templates.Apply", () => Run(template, bill, map),
                new BillTemplateOutcome { Usable = false, Unusable = "This template could not be applied." },
                "The template was not applied. The bill is unchanged.");
        }

        /// <summary>
        /// The one walk both preview and apply use.
        ///
        /// <paramref name="bill"/> null means count only. Every branch adds to the outcome whether or not it writes,
        /// so the numbers the window shows are produced by the code that does the work.
        /// </summary>
        private static BillTemplateOutcome Run(BillTemplate template, Bill_Production bill, Map map)
        {
            BillTemplateOutcome outcome = new BillTemplateOutcome();

            if (template == null)
            {
                outcome.Usable = false;
                outcome.Unusable = "There is no template here.";

                return outcome;
            }

            if (template.Kind == BillTemplateKind.Bill && !string.IsNullOrEmpty(template.Recipe)
                && DefDatabase<RecipeDef>.GetNamedSilentFail(template.Recipe) == null)
            {
                outcome.Usable = false;
                outcome.Unusable = "Needs a recipe this game does not have: " + template.Recipe;

                return outcome;
            }

            ApplyFilter(template, bill, outcome);

            if (template.Kind == BillTemplateKind.Filter)
                return outcome;

            ApplyPortable(template, bill, outcome);
            ApplyRepeatMode(template, bill, outcome);
            ApplyWorker(template, bill, outcome);
            ApplyStore(template, bill, map, outcome);

            return outcome;
        }

        /// <summary>
        /// The ingredient filter, one def at a time.
        ///
        /// A def this game does not have is skipped and named while every other def still applies, which is the
        /// whole reason a template is stored as names rather than references.
        /// </summary>
        private static void ApplyFilter(BillTemplate template, Bill_Production bill, BillTemplateOutcome outcome)
        {
            if (template.Allowed.Count == 0)
                return;

            outcome.WantedDefs = template.Allowed.Count;

            List<ThingDef> found = new List<ThingDef>();

            foreach (string name in template.Allowed)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);

                if (def == null)
                    outcome.MissingDefs.Add(name);
                else
                    found.Add(def);
            }

            outcome.FoundDefs = found.Count;

            if (outcome.MissingDefs.Count > 0)
            {
                outcome.Skipped.Add(outcome.MissingDefs.Count
                                    + (outcome.MissingDefs.Count == 1 ? " ingredient is" : " ingredients are")
                                    + " not in this game: " + string.Join(", ", outcome.MissingDefs.ToArray()));
            }

            if (found.Count == 0)
                return;

            outcome.Applied++;

            if (bill?.ingredientFilter == null)
                return;

            bill.ingredientFilter.SetDisallowAll();

            foreach (ThingDef def in found)
                bill.ingredientFilter.SetAllow(def, true);
        }

        /// <summary>Plain values, which always travel.</summary>
        private static void ApplyPortable(BillTemplate template, Bill_Production bill, BillTemplateOutcome outcome)
        {
            outcome.Applied += 9;

            if (bill == null)
                return;

            bill.repeatCount = template.RepeatCount;
            bill.targetCount = template.TargetCount;
            bill.pauseWhenSatisfied = template.PauseWhenSatisfied;
            bill.unpauseWhenYouHave = template.UnpauseWhenYouHave;
            bill.ingredientSearchRadius = template.SearchRadius;
            bill.includeEquipped = template.IncludeEquipped;
            bill.includeTainted = template.IncludeTainted;
            bill.limitToAllowedStuff = template.LimitToAllowedStuff;
            bill.hpRange = new FloatRange(template.HpMin, template.HpMax);
            bill.allowedSkillRange = new IntRange(template.SkillMin, template.SkillMax);

            if (Enum.TryParse(template.QualityMin, out QualityCategory min)
                && Enum.TryParse(template.QualityMax, out QualityCategory max))
            {
                bill.qualityRange = new QualityRange(min, max);
            }
        }

        /// <summary>
        /// The repeat mode, which is a def and so could in principle be missing.
        ///
        /// Unlike an ingredient it cannot simply be dropped, because a bill must repeat somehow, so an unknown name
        /// falls back to counting rather than leaving the bill in no mode at all. The fallback is reported.
        /// </summary>
        private static void ApplyRepeatMode(BillTemplate template, Bill_Production bill,
            BillTemplateOutcome outcome)
        {
            if (string.IsNullOrEmpty(template.RepeatMode))
                return;

            BillRepeatModeDef mode = DefDatabase<BillRepeatModeDef>.GetNamedSilentFail(template.RepeatMode);

            if (mode == null)
            {
                outcome.Skipped.Add("Repeat mode " + template.RepeatMode
                                                   + " is not in this game, so the bill keeps its own");

                return;
            }

            outcome.Applied++;

            if (bill != null)
                bill.repeatMode = mode;
        }

        /// <summary>
        /// The worker restriction, which straddles two tiers.
        ///
        /// Restricting to slaves, to mechs or to non&#8209;mechs is a plain flag and travels. Restricting to a
        /// named pawn cannot: that pawn does not exist in another colony, and picking somebody else would be a
        /// guess about who should do the work. Only the skill range comes with it.
        /// </summary>
        private static void ApplyWorker(BillTemplate template, Bill_Production bill, BillTemplateOutcome outcome)
        {
            if (!string.IsNullOrEmpty(template.WorkerName))
            {
                outcome.Skipped.Add("Worker restriction to " + template.WorkerName
                                                            + " does not travel, so only the skill range is applied");
            }

            outcome.Applied++;

            if (bill == null)
                return;

            if (template.SlavesOnly)
                bill.SetAnySlaveRestriction();
            else if (template.MechsOnly)
                bill.SetAnyMechRestriction();
            else if (template.NonMechsOnly)
                bill.SetAnyNonMechRestriction();
            else
                bill.SetAnyPawnRestriction();
        }

        /// <summary>
        /// The store mode, and the stockpile it may name.
        ///
        /// A stockpile is matched by name, since a player who calls one "Component shelf" in two colonies means the
        /// same thing both times. No match falls back to the mode's default and says so, rather than storing into
        /// whichever stockpile happened to be first.
        /// </summary>
        private static void ApplyStore(BillTemplate template, Bill_Production bill, Map map,
            BillTemplateOutcome outcome)
        {
            if (string.IsNullOrEmpty(template.StoreMode))
                return;

            BillStoreModeDef mode = DefDatabase<BillStoreModeDef>.GetNamedSilentFail(template.StoreMode);

            if (mode == null)
            {
                outcome.Skipped.Add("Store mode " + template.StoreMode
                                                  + " is not in this game, so the bill keeps its own");

                return;
            }

            ISlotGroup group = null;

            if (!string.IsNullOrEmpty(template.StoreZone))
            {
                group = Find(map, template.StoreZone);

                if (group == null)
                {
                    outcome.Skipped.Add("No stockpile here is called " + template.StoreZone
                                                                      + ", so the output goes to the best one");

                    mode = BillStoreModeDefOf.BestStockpile;
                }
            }

            outcome.Applied++;

            if (bill == null)
                return;

            if (group == null && mode == BillStoreModeDefOf.SpecificStockpile)
                mode = BillStoreModeDefOf.BestStockpile;

            bill.SetStoreMode(mode, group);
        }

        /// <summary>A stockpile on this map with that label, or null.</summary>
        private static ISlotGroup Find(Map map, string label)
        {
            List<SlotGroup> groups = map?.haulDestinationManager?.AllGroupsListForReading;

            if (groups == null)
                return null;

            foreach (SlotGroup group in groups)
            {
                if (string.Equals(Label(group), label, StringComparison.OrdinalIgnoreCase))
                    return group;
            }

            return null;
        }

        /// <summary>
        /// What the game calls a slot group, using the game's own helper.
        ///
        /// Vanilla's bill dialog labels its store options with <c>SlotGroup.GetGroupLabel</c>, so using the same
        /// call means a template records the name the player actually saw.
        /// </summary>
        private static string Label(ISlotGroup group)
        {
            return group == null ? null : SlotGroup.GetGroupLabel(group);
        }
    }
}
