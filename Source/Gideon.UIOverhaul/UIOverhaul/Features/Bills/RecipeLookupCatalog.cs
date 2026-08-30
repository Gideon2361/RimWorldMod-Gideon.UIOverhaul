using System;
using System.Collections.Generic;
using System.Text;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>One row of the lookup: a thing you can make, and what it takes to make it.</summary>
    internal struct RecipeEntry
    {
        /// <summary>The thing itself. This is what the row is about and what the icon draws.</summary>
        internal ThingDef Product;

        internal string Label;

        /// <summary>Which mod added the <i>thing</i>, which is the one a player is looking for.</summary>
        internal string Mod;

        /// <summary>Where it is made, as a readable list.</summary>
        internal string Benches;

        /// <summary>Whether the colony has at least one of those benches standing right now.</summary>
        internal bool Owned;

        /// <summary>The full bench list and the recipes that make it, when the row had no room for them.</summary>
        internal string Tip;

        /// <summary>Name, mod, benches and recipe names, lower case, matched against the query as one string.</summary>
        internal string Haystack;
    }

    /// <summary>
    /// Everything the colony could be told to make, indexed by the thing rather than by the recipe.
    ///
    /// <b>The thing is what the player knows; the recipe is what they came to find out.</b> An early cut of this
    /// listed recipes, which put "make component" on the card and left somebody searching for "component" to
    /// match on the verb by luck. Corrected on 2026-08-29. One row is one thing you can end up holding, and
    /// everything else on it -- the bench, the mod, the tooltip -- describes how to get it.
    ///
    /// <b>Several recipes making one thing collapse into one row.</b> Simple and bulk versions of a meal, a
    /// weapon buildable at two benches, a drug with a long and a short route: a player looking for the thing
    /// wants every way to get it in one place, not the same picture three times. Their benches are unioned and
    /// their names go in the tooltip.
    ///
    /// <b>Every product of a recipe, not just its headline one.</b> Reading <c>products</c> rather than
    /// <c>ProducedThingDef</c> is what puts steel under "steel" when the only route to it is smelting -- that
    /// property answers null the moment a recipe makes more than one thing.
    ///
    /// <b>Workbench recipes only.</b> "Craftable in a bill" is the question and this tab is about
    /// <c>Building_WorkTable</c>, so a surgery, a gestator recipe and a growing zone are all out.
    ///
    /// <b>What this cannot see:</b> <c>specialProducts</c>. Butchering and smelting name no products at all --
    /// what comes out is decided from the thing being destroyed, at the time it is destroyed. There is nothing
    /// static to index, so a thing obtainable only that way is honestly absent rather than wrongly listed.
    /// </summary>
    internal static class RecipeLookupCatalog
    {
        /// <summary>How many bench names fit on the row before the rest go to the tooltip.</summary>
        private const int NamesShown = 3;

        private static List<RecipeEntry> cached;

        private static int stamp = -1;

        /// <summary>
        /// The catalogue, with its ownership flags refreshed for the maps in play.
        ///
        /// Rebuilt when the number of maps changes, which is the cheap approximation of "the colony might own
        /// different benches now". A bench built while the window is open is not picked up until it is reopened:
        /// the right trade for a reference list read for a few seconds at a time, against rescanning every map's
        /// buildings per frame.
        /// </summary>
        internal static List<RecipeEntry> All
        {
            get
            {
                int now = UIGuard.Try("Bills.LookupStamp",
                    () => Find.Maps != null ? Find.Maps.Count : 0, 0, null);

                if (cached != null && stamp == now)
                    return cached;

                cached = UIGuard.Try("Bills.LookupBuild", Gather, new List<RecipeEntry>(),
                    "The lookup could not read the recipe list.");

                stamp = now;

                return cached;
            }
        }

        /// <summary>What one product is made of, while the catalogue is being assembled.</summary>
        private class Ways
        {
            internal readonly List<ThingDef> Benches = new List<ThingDef>();

            internal readonly List<string> Recipes = new List<string>();
        }

        private static List<RecipeEntry> Gather()
        {
            HashSet<ThingDef> owned = OwnedBenches();

            Dictionary<ThingDef, Ways> made = new Dictionary<ThingDef, Ways>();

            List<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;

            List<ThingDef> benches = new List<ThingDef>();

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];

                if (recipe == null || recipe.products == null || recipe.products.Count == 0)
                    continue;

                benches.Clear();

                foreach (ThingDef user in recipe.AllRecipeUsers)
                {
                    // The one test that decides what belongs here. A pawn user is a surgery; a gestator or a
                    // zone is a recipe the bills tab never sees.
                    if (user != null && user.thingClass != null
                                     && typeof(Building_WorkTable).IsAssignableFrom(user.thingClass)
                                     && !benches.Contains(user))
                        benches.Add(user);
                }

                if (benches.Count == 0)
                    continue;

                string name = recipe.LabelCap.NullOrEmpty() ? recipe.defName : recipe.LabelCap.ToString();

                for (int p = 0; p < recipe.products.Count; p++)
                {
                    ThingDefCountClass product = recipe.products[p];

                    if (product == null || product.thingDef == null)
                        continue;

                    Ways ways;

                    if (!made.TryGetValue(product.thingDef, out ways))
                    {
                        ways = new Ways();
                        made[product.thingDef] = ways;
                    }

                    for (int b = 0; b < benches.Count; b++)
                    {
                        if (!ways.Benches.Contains(benches[b]))
                            ways.Benches.Add(benches[b]);
                    }

                    if (!ways.Recipes.Contains(name))
                        ways.Recipes.Add(name);
                }
            }

            List<RecipeEntry> entries = new List<RecipeEntry>();

            foreach (KeyValuePair<ThingDef, Ways> pair in made)
                entries.Add(Read(pair.Key, pair.Value, owned));

            entries.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));

            return entries;
        }

        private static RecipeEntry Read(ThingDef product, Ways ways, HashSet<ThingDef> owned)
        {
            string label = product.LabelCap.NullOrEmpty() ? product.defName : product.LabelCap.ToString();

            // The thing's mod, not the recipe's. They are usually the same and the difference matters when they
            // are not: a mod that adds a way to make a vanilla item has not added the item.
            string mod = product.modContentPack != null && !product.modContentPack.Name.NullOrEmpty()
                ? product.modContentPack.Name
                : "RimWorld";

            bool anyOwned = false;

            for (int i = 0; i < ways.Benches.Count; i++)
            {
                if (owned.Contains(ways.Benches[i]))
                {
                    anyOwned = true;

                    break;
                }
            }

            StringBuilder line = new StringBuilder();
            StringBuilder full = new StringBuilder();

            for (int i = 0; i < ways.Benches.Count; i++)
            {
                string name = ways.Benches[i].LabelCap.NullOrEmpty()
                    ? ways.Benches[i].defName
                    : ways.Benches[i].LabelCap.ToString();

                if (full.Length > 0)
                    full.Append(", ");

                full.Append(name);

                if (i >= NamesShown)
                    continue;

                if (line.Length > 0)
                    line.Append(", ");

                line.Append(name);
            }

            if (ways.Benches.Count > NamesShown)
                line.Append(" and ").Append(ways.Benches.Count - NamesShown).Append(" more");

            StringBuilder tip = new StringBuilder();

            if (ways.Benches.Count > NamesShown)
                tip.Append("Made at: ").Append(full);

            // The recipe names are the tooltip's real job now that the row is about the thing: they are how the
            // bill will be named when it is added, so this is the bridge from "I want one of these" to the wizard.
            if (ways.Recipes.Count > 0)
            {
                if (tip.Length > 0)
                    tip.Append("\n\n");

                tip.Append(ways.Recipes.Count == 1 ? "Bill: " : "Bills: ")
                    .Append(string.Join(", ", ways.Recipes.ToArray()));
            }

            RecipeEntry entry = new RecipeEntry
            {
                Product = product,
                Label = label,
                Mod = mod,
                Benches = line.ToString(),
                Owned = anyOwned,
                Tip = tip.Length > 0 ? tip.ToString() : null
            };

            entry.Haystack = (label + " " + mod + " " + full + " "
                              + string.Join(" ", ways.Recipes.ToArray())).ToLowerInvariant();

            return entry;
        }

        /// <summary>Which bench defs the colony actually has standing, across every map it holds.</summary>
        private static HashSet<ThingDef> OwnedBenches()
        {
            HashSet<ThingDef> owned = new HashSet<ThingDef>();

            List<Map> maps = Find.Maps;

            if (maps == null)
                return owned;

            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];

                if (map == null || map.listerBuildings == null)
                    continue;

                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building is Building_WorkTable && building.def != null)
                        owned.Add(building.def);
                }
            }

            return owned;
        }
    }
}
