using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>
    /// Everything the tab can do to a body, done the way the player would have done it by hand.
    ///
    /// <b>Nothing here reaches past the game's own machinery.</b> Burying claims a grave, which is the same call
    /// the grave's own assign gizmo makes; stripping adds the designation vanilla's own designator adds;
    /// cremating and butchering add a bill to a bench that can do the recipe. So every action is undoable by the
    /// same means as always, shows up in the same work lists, and costs the same colonist labour. A screen that
    /// destroyed a corpse the instant you clicked would be a different game.
    ///
    /// <b>A bill names a def, not a body, and that is the one place this leaks.</b> RimWorld has no way to say
    /// "cremate that raider"; the nearest true thing is "cremate one human corpse, strangers only", so that is
    /// what gets queued, and a folded group of three queues a count of three. The tab says which bench took the
    /// order, because a click whose whole effect is invisible on this screen has to say where it went.
    /// </summary>
    internal static class CorpseActions
    {
        private const string CremateRecipe = "CremateCorpse";

        private const string ButcherRecipe = "ButcherCorpseFlesh";

        private const string ShredRecipe = "ButcherCorpseMechanoid";

        private const string SmashRecipe = "SmashCorpseMechanoid";

        /// <summary>Scratch for a grave search. Never held past the call that filled it.</summary>
        private static readonly List<Building_Grave> Candidates = new List<Building_Grave>();

        // ---------------------------------------------------------------------------------------
        // Stripping
        // ---------------------------------------------------------------------------------------

        internal static bool StripQueued(Corpse corpse)
        {
            return UIGuard.Try("Corpses.StripQueued", () =>
            {
                if (corpse == null || !corpse.Spawned || corpse.Map == null)
                    return false;

                return corpse.Map.designationManager.DesignationOn(corpse, DesignationDefOf.Strip) != null;
            }, false, null);
        }

        /// <summary>
        /// Marks every body on the row to be stripped, and warns about goodwill exactly once.
        ///
        /// The message is vanilla's, called through its own helper: stripping an ally's dead is a diplomatic act
        /// and the warning belongs to the faction system rather than to this tab.
        /// </summary>
        internal static void Strip(CorpseEntry entry)
        {
            UIGuard.Try("Corpses.Strip", () =>
            {
                bool warned = false;

                for (int i = 0; i < entry.Members.Count; i++)
                {
                    Corpse corpse = entry.Members[i];

                    if (corpse == null || !corpse.Spawned || corpse.Map == null)
                        continue;

                    if (!StrippableUtility.CanBeStrippedByColony(corpse))
                        continue;

                    if (corpse.Map.designationManager.DesignationOn(corpse, DesignationDefOf.Strip) != null)
                        continue;

                    corpse.Map.designationManager.AddDesignation(new Designation(corpse, DesignationDefOf.Strip));

                    corpse.SetForbidden(false, false);

                    if (warned)
                        continue;

                    warned = true;

                    StrippableUtility.CheckSendStrippingImpactsGoodwillMessage(corpse);
                }

                CorpseRoster.Invalidate();
            }, "The bodies could not be marked for stripping.");
        }

        internal static void CancelStrip(CorpseEntry entry)
        {
            UIGuard.Try("Corpses.CancelStrip", () =>
            {
                for (int i = 0; i < entry.Members.Count; i++)
                {
                    Corpse corpse = entry.Members[i];

                    if (corpse == null || !corpse.Spawned || corpse.Map == null)
                        continue;

                    Designation marked = corpse.Map.designationManager.DesignationOn(corpse,
                        DesignationDefOf.Strip);

                    if (marked != null)
                        corpse.Map.designationManager.RemoveDesignation(marked);
                }

                CorpseRoster.Invalidate();
            }, "The stripping order could not be cancelled.");
        }

        // ---------------------------------------------------------------------------------------
        // Burial
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Whether there is a grave this body could go in, and what is wrong when there is not.
        ///
        /// <b>Only graves whose own filter already accepts the body count.</b> Claiming a grave overrides its
        /// storage settings outright, so a Bury button that ignored them would put a raider in the sarcophagus a
        /// player deliberately reserved for colonists. The filter is a decision they made; this respects it, and
        /// the Graves view is where it is changed.
        /// </summary>
        internal static bool CanBury(CorpseEntry entry, out string reason)
        {
            string problem = null;

            bool can = UIGuard.Try("Corpses.CanBury", () =>
            {
                if (entry.Grave != null)
                {
                    problem = "Already buried.";

                    return false;
                }

                Building_Grave grave = BestGrave(entry.Corpse, entry.Pawn, entry.Map);

                if (grave != null)
                    return true;

                problem = "No free grave on this map accepts " + entry.Name
                          + ". Build one, or widen what an existing grave accepts on the Graves view.";

                return false;
            }, false, null);

            reason = problem;

            return can;
        }

        /// <summary>
        /// Reserves a grave for each body on the row and unforbids it so a hauler will fetch it.
        ///
        /// <b>Reserving rather than teleporting.</b> <c>ClaimGrave</c> is what the grave's own gizmo calls: it
        /// raises the grave's storage priority to critical and makes it accept that one corpse and no other, so
        /// the colony's own haulers do the carrying and the job appears in the work list like any other.
        /// </summary>
        internal static void Bury(CorpseEntry entry)
        {
            UIGuard.Try("Corpses.Bury", () =>
            {
                int buried = 0;

                for (int i = 0; i < entry.Members.Count; i++)
                {
                    Corpse corpse = entry.Members[i];

                    if (corpse == null || corpse.Destroyed)
                        continue;

                    Pawn pawn = corpse.InnerPawn;

                    if (pawn == null || pawn.ownership == null)
                        continue;

                    Building_Grave grave = BestGrave(corpse, pawn, entry.Map);

                    if (grave == null)
                        break;

                    pawn.ownership.ClaimGrave(grave);

                    corpse.SetForbidden(false, false);

                    buried++;
                }

                if (buried < entry.Members.Count)
                    Messages.Message(
                        "Graves were reserved for " + buried + " of " + entry.Members.Count
                        + ". There were no more free graves that accept them.",
                        MessageTypeDefOf.CautionInput, false);

                CorpseRoster.Invalidate();
                GraveRoster.Invalidate();
            }, "The burial could not be arranged.");
        }

        /// <summary>
        /// The grave this body should go in: one that will have it, ornamented first for our own, nearest after.
        ///
        /// Sarcophagi are preferred for our own dead and avoided for everybody else, which is the same judgement
        /// the default storage settings encode -- a sarcophagus ships disallowing strangers, and a colony that
        /// built one built it for somebody in particular.
        /// </summary>
        internal static Building_Grave BestGrave(Corpse corpse, Pawn pawn, Map map)
        {
            return UIGuard.Try("Corpses.BestGrave", () =>
            {
                Free(map, corpse, Candidates);

                if (Candidates.Count == 0)
                    return null;

                bool ours = pawn != null && pawn.Faction != null && pawn.Faction.IsPlayer
                            && pawn.RaceProps != null && pawn.RaceProps.Humanlike;

                IntVec3 from = corpse.PositionHeld;

                Building_Grave best = null;
                float bestScore = float.MaxValue;

                for (int i = 0; i < Candidates.Count; i++)
                {
                    Building_Grave grave = Candidates[i];

                    bool ornate = grave is Building_Sarcophagus;

                    // Distance in cells, with a flat bias that is larger than any sane colony's map so the
                    // ornamented-or-not decision always beats the walk.
                    float score = (grave.Position - from).LengthHorizontal;

                    if (ornate != ours)
                        score += 10000f;

                    if (score >= bestScore)
                        continue;

                    bestScore = score;
                    best = grave;
                }

                Candidates.Clear();

                return best;
            }, null, null);
        }

        /// <summary>Every player grave on the map that is empty, unreserved, and willing to take this body.</summary>
        internal static void Free(Map map, Corpse corpse, List<Building_Grave> into)
        {
            into.Clear();

            if (map == null || map.listerThings == null)
                return;

            List<Thing> graves = map.listerThings.ThingsInGroup(ThingRequestGroup.Grave);

            for (int i = 0; graves != null && i < graves.Count; i++)
            {
                Building_Grave grave = graves[i] as Building_Grave;

                if (grave == null || grave.HasCorpse || grave.AssignedPawn != null)
                    continue;

                if (grave.Faction == null || !grave.Faction.IsPlayer)
                    continue;

                StorageSettings settings = grave.GetStoreSettings();

                if (corpse != null && (settings == null || !settings.AllowedToAccept(corpse)))
                    continue;

                into.Add(grave);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Bills
        // ---------------------------------------------------------------------------------------

        internal static bool CanCremate(CorpseEntry entry, out string reason)
        {
            return CanQueue(entry, CremateRecipe, "crematorium", out reason);
        }

        internal static void Cremate(CorpseEntry entry)
        {
            Queue(entry, CremateRecipe, "Cremating");
        }

        internal static bool CanButcher(CorpseEntry entry, out string reason)
        {
            if (entry.Kind == CorpseKind.Mechanoids)
                return CanQueue(entry, ShredRecipe, "machining table", out reason)
                       || CanQueue(entry, SmashRecipe, "smithy", out reason);

            if (entry.Stage != RotStage.Fresh)
            {
                reason = "A butcher will not take a body this far gone.";

                return false;
            }

            if (entry.Meat <= 0 && entry.Leather <= 0)
            {
                reason = "Nothing to get out of it.";

                return false;
            }

            return CanQueue(entry, ButcherRecipe, "butcher table", out reason);
        }

        internal static void Butcher(CorpseEntry entry)
        {
            if (entry.Kind != CorpseKind.Mechanoids)
            {
                Queue(entry, ButcherRecipe, "Butchering");

                return;
            }

            string ignored;

            Queue(entry, CanQueue(entry, ShredRecipe, "machining table", out ignored) ? ShredRecipe : SmashRecipe,
                "Breaking down");
        }

        private static bool CanQueue(CorpseEntry entry, string recipeName, string what, out string reason)
        {
            string problem = null;

            bool can = UIGuard.Try("Corpses.CanQueue", () =>
            {
                RecipeDef recipe = Recipe(recipeName);

                if (recipe == null)
                {
                    problem = "This install has no " + what + " recipe.";

                    return false;
                }

                if (Bench(entry.Map, recipe) != null)
                    return true;

                problem = "There is no " + what + " on this map with room for another bill.";

                return false;
            }, false, null);

            reason = problem;

            return can;
        }

        /// <summary>
        /// Adds one bill for each body on the row to whichever bench can do the work.
        ///
        /// <b>The bill is narrowed to this kind of body and no other.</b> Everything is disallowed and then the
        /// one corpse def is allowed back, plus the single special filter that separates a colonist from a slave
        /// from a stranger. So a Cremate on a raider cannot reach into the freezer and take a colonist, which is
        /// what a bill left on its defaults would happily do.
        /// </summary>
        private static void Queue(CorpseEntry entry, string recipeName, string verb)
        {
            UIGuard.Try("Corpses.Queue", () =>
            {
                RecipeDef recipe = Recipe(recipeName);

                if (recipe == null)
                    return;

                Building_WorkTable bench = Bench(entry.Map, recipe);

                if (bench == null)
                    return;

                Bill_Production bill = recipe.MakeNewBill() as Bill_Production;

                if (bill == null)
                    return;

                bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                bill.repeatCount = entry.Members.Count;

                Narrow(bill.ingredientFilter, entry.Corpse, entry.Pawn);

                bench.billStack.AddBill(bill);

                Messages.Message(
                    verb + " " + entry.Members.Count + " x " + entry.Name + " was added to the "
                    + bench.LabelShortCap + ".", bench, MessageTypeDefOf.TaskCompletion, false);

                CorpseRoster.Invalidate();
            }, "The order could not be added to a bench.");
        }

        /// <summary>Cuts a bill's ingredients down to exactly the kind of body the row is about.</summary>
        private static void Narrow(ThingFilter filter, Corpse corpse, Pawn pawn)
        {
            if (filter == null || corpse == null)
                return;

            filter.SetDisallowAll();
            filter.SetAllow(corpse.def, true);

            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return;

            string wanted = ModsConfig.IdeologyActive && pawn.IsSlave
                ? "AllowCorpsesSlave"
                : pawn.Faction != null && pawn.Faction.IsPlayer
                    ? "AllowCorpsesColonist"
                    : "AllowCorpsesStranger";

            SpecialThingFilterDef special = DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(wanted);

            if (special != null)
                filter.SetAllow(special, true);
        }

        private static RecipeDef Recipe(string defName)
        {
            return DefDatabase<RecipeDef>.GetNamedSilentFail(defName);
        }

        /// <summary>
        /// The bench that should take the order: the one with the shortest queue that can still take a bill.
        ///
        /// Shortest queue rather than nearest, because a bench is a place work is brought to. Two crematoria
        /// exist so that two colonists can be burning bodies at once, and piling every order onto whichever one
        /// happens to be closest to the corpse defeats the reason the second was built.
        /// </summary>
        private static Building_WorkTable Bench(Map map, RecipeDef recipe)
        {
            if (map == null || map.listerBuildings == null || recipe == null)
                return null;

            List<Building> all = map.listerBuildings.allBuildingsColonist;

            Building_WorkTable best = null;
            int bestLoad = int.MaxValue;

            for (int i = 0; all != null && i < all.Count; i++)
            {
                Building_WorkTable bench = all[i] as Building_WorkTable;

                if (bench == null || bench.def == null || bench.def.AllRecipes == null)
                    continue;

                if (!bench.def.AllRecipes.Contains(recipe))
                    continue;

                int load = bench.billStack != null ? bench.billStack.Count : 0;

                if (load >= BillStack.MaxCount || load >= bestLoad)
                    continue;

                bestLoad = load;
                best = bench;
            }

            return best;
        }
    }
}
