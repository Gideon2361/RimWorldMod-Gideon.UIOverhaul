using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// The things the bills window does to the colony rather than to itself: making a bill, choosing which bench
    /// it goes on, and restricting who works it.
    ///
    /// <b>Separated from the window because none of it is drawing.</b> Each of these opens a menu and then
    /// changes the game, and keeping them out of the layout code is what stops a mistake in a rectangle from
    /// turning into a mistake in somebody's colony.
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
        /// Opens the recipe menu for one bench and adds whatever is chosen.
        ///
        /// <paramref name="added"/> runs after a bill is actually created, so the window can re-read itself. It
        /// does not run when the menu is dismissed.
        /// </summary>
        internal static void AddBill(Building_WorkTable bench, System.Action added)
        {
            UIGuard.Try("Bills.AddMenu", () =>
            {
                if (bench == null)
                    return;

                List<FloatMenuOption> options = Recipes(bench, added);

                if (options.Count == 0)
                {
                    options.Add(new FloatMenuOption("NoneBrackets".Translate(), null));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }, "The list of recipes could not be built, so no bill was added.");
        }

        /// <summary>
        /// Asks which bench first, then which recipe.
        ///
        /// <b>Every worktable on every map, not only the ones already holding a bill.</b> The whole point of
        /// adding from the colony view is reaching a bench that has nothing on it yet, which is exactly the bench
        /// the list does not show.
        /// </summary>
        internal static void AddBillAnywhere(System.Action added)
        {
            UIGuard.Try("Bills.AddMenuAnywhere", () =>
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                foreach (Building_WorkTable bench in Benches())
                {
                    Building_WorkTable captured = bench;
                    string where = Place(captured);

                    options.Add(new FloatMenuOption(
                        where.NullOrEmpty() ? captured.LabelCap : captured.LabelCap + " (" + where + ")",
                        () => AddBill(captured, added)));
                }

                if (options.Count == 0)
                {
                    options.Add(new FloatMenuOption("NoneBrackets".Translate(), null));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }, "The list of benches could not be built, so no bill was added.");
        }

        /// <summary>Every colonist worktable on every loaded map, in the order the maps are listed.</summary>
        internal static List<Building_WorkTable> Benches()
        {
            List<Building_WorkTable> benches = new List<Building_WorkTable>();
            List<Map> maps = Find.Maps;

            if (maps == null)
                return benches;

            foreach (Map map in maps)
            {
                List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;

                if (buildings == null)
                    continue;

                foreach (Building building in buildings)
                {
                    if (building is Building_WorkTable bench)
                        benches.Add(bench);
                }
            }

            return benches;
        }

        /// <summary>The room a bench is in, and its map when there is more than one.</summary>
        private static string Place(Building_WorkTable bench)
        {
            Map map = bench?.Map;

            if (map == null)
                return null;

            string room = null;
            Room found = bench.Position.GetRoom(map);

            if (found != null && !found.PsychologicallyOutdoors)
                room = found.Role?.LabelCap;

            bool many = Find.Maps != null && Find.Maps.Count > 1;
            string place = map.Parent?.LabelCap;

            if (!many || place.NullOrEmpty())
                return room;

            return room.NullOrEmpty() ? place : room + " - " + place;
        }

        private static List<FloatMenuOption> Recipes(Building_WorkTable bench, System.Action added)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<RecipeDef> all = bench.def?.AllRecipes;

            if (all == null)
                return options;

            foreach (RecipeDef recipe in all)
            {
                if (recipe == null || !recipe.AvailableNow || !recipe.AvailableOnNow(bench))
                    continue;

                Offer(options, bench, recipe, null, added);

                foreach (Precept_ThingStyle style in Styles(recipe))
                    Offer(options, bench, recipe, style, added);
            }

            return options;
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

        private static void Offer(List<FloatMenuOption> options, Building_WorkTable bench, RecipeDef recipe,
            Precept_ThingStyle style, System.Action added)
        {
            string label = style != null
                ? "RecipeMake".Translate(style.LabelCap).CapitalizeFirst().ToString()
                : recipe.LabelCap.ToString();

            options.Add(new FloatMenuOption(label, () => Make(bench, recipe, style, added),
                recipe.UIIconThing, recipe.UIIcon));
        }

        /// <summary>
        /// Creates the bill and puts it on the bench.
        ///
        /// The two warnings come before the bill rather than instead of it, exactly as vanilla does: the player
        /// asked for the bill, so they get the bill, and they are told what will stop it running.
        /// </summary>
        private static void Make(Building_WorkTable bench, RecipeDef recipe, Precept_ThingStyle style,
            System.Action added)
        {
            UIGuard.Try("Bills.Add", () =>
            {
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

                Bill bill = recipe.MakeNewBill(style);

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
