using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// One thing a bench can be told to make: a recipe, and the ideology style of it when there is one.
    ///
    /// <b>A pair rather than a recipe alone,</b> because the same recipe appears more than once when an
    /// ideoligion has styles for what it produces, and the style is what tells those entries apart. Losing the
    /// style would mean two identical looking cards that build different things.
    /// </summary>
    internal sealed class RecipeOffer
    {
        internal RecipeDef Recipe;

        /// <summary>Null for the plain recipe.</summary>
        internal Precept_ThingStyle Style;

        internal string Label => Style != null
            ? "RecipeMake".Translate(Style.LabelCap).CapitalizeFirst().ToString()
            : Recipe.LabelCap.ToString();

        /// <summary>What to draw for it, which is not always what it produces. See the note in the row drawer.</summary>
        internal ThingDef Icon => Recipe?.UIIconThing;
    }

    /// <summary>
    /// The things the bills interfaces do to the colony rather than to themselves: making a bill, choosing which
    /// bench it goes on, and restricting who works it.
    ///
    /// <b>Separated from the windows because none of it is drawing.</b> Each of these writes to a bill stack or
    /// opens a menu that will, and keeping them out of the layout code is what stops a mistake in a rectangle
    /// from turning into a mistake in somebody's colony.
    ///
    /// <b>The float menu routes are gone.</b> Add bill opened a menu of benches and then a menu of recipes until
    /// the wizard replaced both on 2026-08-20, so <c>AddBill</c>, <c>AddBillAnywhere</c> and the bench listing
    /// they needed were deleted rather than left unreachable. <see cref="Available"/> survives because the card
    /// picker asks the same question of a bench, and <see cref="Make"/> survives as the wizard's fallback for a
    /// recipe whose bill cannot be configured.
    ///
    /// <b>The recipe list is built the way RimWorld builds it,</b> including the ideology building variants and
    /// the two warnings it raises before adding: no mechanitor for a mechanitor recipe, and nobody skilled
    /// enough for the work. Those warnings are the reason to follow vanilla here rather than simply list
    /// <c>AllRecipes</c>; a player who adds a bill nobody can do should be told at the moment they add it, not
    /// discover it later in the needs-attention count.
    /// </summary>
    internal static class BillActions
    {
        /// <summary>
        /// Everything this bench can be asked to make right now, in the def's own order.
        ///
        /// Shared by the float menu and by the card picker so the two cannot disagree. Guarded, because it walks
        /// def lists and reaches through the player faction's ideoligions.
        /// </summary>
        internal static List<RecipeOffer> Available(Building_WorkTable bench)
        {
            return UIGuard.Try("Bills.AvailableRecipes", () =>
            {
                List<RecipeOffer> offers = new List<RecipeOffer>();
                List<RecipeDef> all = bench?.def?.AllRecipes;

                if (all == null)
                    return offers;

                foreach (RecipeDef recipe in all)
                {
                    if (recipe == null || !recipe.AvailableNow || !recipe.AvailableOnNow(bench))
                        continue;

                    offers.Add(new RecipeOffer { Recipe = recipe });

                    foreach (Precept_ThingStyle style in Styles(recipe))
                        offers.Add(new RecipeOffer { Recipe = recipe, Style = style });
                }

                return offers;
            }, new List<RecipeOffer>(), "The list of recipes for this bench could not be built.");
        }

        /// <summary>
        /// The ideology building variants of a recipe, if any.
        ///
        /// Guarded at every step rather than only on <c>IdeologyActive</c>: a colony can be between factions
        /// during a load, and the ideo list is reachable through three references that are each allowed to be
        /// null on the way.
        /// </summary>
        private static IEnumerable<Precept_ThingStyle> Styles(RecipeDef recipe)
        {
            if (!ModsConfig.IdeologyActive || recipe.ProducedThingDef == null)
                yield break;

            IEnumerable<Ideo> ideos = Faction.OfPlayer?.ideos?.AllIdeos;

            if (ideos == null)
                yield break;

            foreach (Ideo ideo in ideos)
            {
                IEnumerable<Precept_Building> buildings = ideo?.cachedPossibleBuildings;

                if (buildings == null)
                    continue;

                foreach (Precept_Building building in buildings)
                {
                    if (building != null && building.ThingDef == recipe.ProducedThingDef)
                        yield return building;
                }
            }
        }

        /// <summary>
        /// Creates the bill and puts it on the bench.
        ///
        /// The two warnings come before the bill rather than instead of it, exactly as vanilla does: the player
        /// asked for the bill, so they get the bill, and they are told what will stop it running.
        /// </summary>
        internal static void Make(Building_WorkTable bench, RecipeOffer offer, System.Action added)
        {
            UIGuard.Try("Bills.Add", () =>
            {
                if (bench == null || offer?.Recipe == null)
                    return;

                RecipeDef recipe = offer.Recipe;
                Map map = bench.Map;

                if (ModsConfig.BiotechActive && recipe.mechanitorOnlyRecipe
                    && map?.mapPawns != null
                    && !map.mapPawns.FreeColonists.Any(MechanitorUtility.IsMechanitor))
                {
                    Find.WindowStack.Add(
                        new Dialog_MessageBox("RecipeRequiresMechanitor".Translate(recipe.LabelCap)));
                }
                else if (map?.mapPawns != null
                         && !map.mapPawns.FreeColonists.Any(recipe.PawnSatisfiesSkillRequirements))
                {
                    Bill.CreateNoPawnsWithSkillDialog(recipe);
                }

                Bill bill = recipe.MakeNewBill(offer.Style);

                bench.billStack?.AddBill(bill);

                if (recipe.conceptLearned != null)
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(recipe.conceptLearned, KnowledgeAmount.Total);

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();

                added?.Invoke();
            }, "The bill was not added.");
        }

        // ------------------------------------------------------------------ who works it

        /// <summary>
        /// What the worker button says, which is the whole restriction in one line.
        ///
        /// Follows vanilla's own order of precedence, because that order is the order the rules are actually
        /// applied in: a named pawn beats any of the group restrictions, and the group ones are mutually
        /// exclusive.
        /// </summary>
        internal static string WorkerLabel(Bill_Production bill)
        {
            if (bill == null)
                return string.Empty;

            if (bill.PawnRestriction != null)
                return bill.PawnRestriction.LabelShortCap;

            if (ModsConfig.IdeologyActive && bill.SlavesOnly)
                return "AnySlave".Translate();

            if (ModsConfig.BiotechActive && bill.recipe != null && bill.recipe.mechanitorOnlyRecipe)
                return "AnyMechanitor".Translate();

            if (ModsConfig.BiotechActive && bill.MechsOnly)
                return "AnyMech".Translate();

            if (ModsConfig.BiotechActive && bill.NonMechsOnly)
                return "AnyNonMech".Translate();

            return "AnyWorker".Translate();
        }

        /// <summary>
        /// Opens the who-can-work-this menu.
        ///
        /// <b>The colonist entries come from RimWorld's own generator.</b> It sorts by skill, puts the pawns who
        /// have the work type switched off below the ones who do not, greys out anybody who can never do it, and
        /// says why in each case. Rebuilding that list here would be a worse copy of it that drifts every
        /// version.
        /// </summary>
        internal static void ChooseWorker(Bill_Production bill, System.Action chosen)
        {
            UIGuard.Try("Bills.WorkerMenu", () =>
            {
                if (bill == null)
                    return;

                List<FloatMenuOption> options = new List<FloatMenuOption>();
                bool mechanitorOnly = ModsConfig.BiotechActive && bill.recipe != null
                                                               && bill.recipe.mechanitorOnlyRecipe;

                options.Add(new FloatMenuOption(mechanitorOnly ? "AnyMechanitor".Translate() : "AnyWorker".Translate(),
                    () => Set(bill, b => b.SetAnyPawnRestriction(), chosen)));

                if (!mechanitorOnly)
                {
                    if (ModsConfig.IdeologyActive)
                    {
                        options.Add(new FloatMenuOption("AnySlave".Translate(),
                            () => Set(bill, b => b.SetAnySlaveRestriction(), chosen)));
                    }

                    if (ModsConfig.BiotechActive && bill.recipe != null
                                                 && MechWorkUtility.AnyWorkMechCouldDo(bill.recipe))
                    {
                        options.Add(new FloatMenuOption("AnyMech".Translate(),
                            () => Set(bill, b => b.SetAnyMechRestriction(), chosen)));

                        options.Add(new FloatMenuOption("AnyNonMech".Translate(),
                            () => Set(bill, b => b.SetAnyNonMechRestriction(), chosen)));
                    }
                }

                foreach (FloatMenuOption option in Colonists(bill, mechanitorOnly, chosen))
                    options.Add(option);

                Find.WindowStack.Add(new FloatMenu(options));
            }, "The worker list could not be built, so nothing was changed.");
        }

        /// <summary>
        /// The per colonist entries, or nothing at all when the bill is not on a bench yet.
        ///
        /// <b>The bill stack check is not paranoia.</b> RimWorld's generator reaches through
        /// <c>bill.billStack.billGiver</c> to find the work giver and logs an error when it cannot, so asking it
        /// about a bill that is not placed would put our fault in its name in the player's log.
        /// </summary>
        private static IEnumerable<FloatMenuOption> Colonists(Bill_Production bill, bool mechanitorOnly,
            System.Action chosen)
        {
            if (bill.billStack?.billGiver == null)
                yield break;

            IEnumerable<Widgets.DropdownMenuElement<Pawn>> entries = mechanitorOnly
                ? BillDialogUtility.GetPawnRestrictionOptionsForBill(bill, MechanitorUtility.IsMechanitor)
                : BillDialogUtility.GetPawnRestrictionOptionsForBill(bill);

            foreach (Widgets.DropdownMenuElement<Pawn> entry in entries)
            {
                FloatMenuOption option = entry.option;

                if (option == null)
                    continue;

                // A disabled entry arrives with no action and keeps it: that is how vanilla says "this pawn can
                // never do this work" while still naming them and the reason.
                if (option.action != null)
                {
                    Pawn pawn = entry.payload;

                    option.action = () => Set(bill, b => b.SetPawnRestriction(pawn), chosen);
                }

                yield return option;
            }
        }

        private static void Set(Bill_Production bill, System.Action<Bill_Production> change, System.Action chosen)
        {
            UIGuard.Try("Bills.SetWorker", () =>
            {
                change(bill);

                chosen?.Invoke();
            }, "The worker restriction was not changed.");
        }
    }
}
