using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Trade.Shell;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade
{
    /// <summary>
    /// Which side of the deal a view is showing.
    ///
    /// <b>Gift is its own thing rather than a flavour of selling,</b> because vanilla makes it one:
    /// <c>TradeSession.giftMode</c> rebuilds the deal with no trader goods in it at all and flips
    /// <c>PositiveCountDirection</c>, so the same list read the same way means something different.
    /// </summary>
    internal enum TradeSide
    {
        Buy,
        Sell,
        Gift
    }

    /// <summary>
    /// The rows a trade view is showing, sorted, filtered and counted.
    ///
    /// <b>Splitting the list is the change the whole screen is built around.</b> Vanilla puts everything both
    /// sides own into one flat list with buying and selling interleaved, and asks the player to find their deal
    /// in it by spotting nonzero numbers -- the only structure being two sort dropdowns. Buying and selling are
    /// two different jobs done at two different moments: you browse a shop, then you browse your own stores. So
    /// they get two views over the same <c>AllTradeables</c>, each with its own rail.
    ///
    /// <b>Nothing here is a trade rule.</b> This decides what a row is filed under and what order rows come in.
    /// What may be sold, at what price, in what quantity, and to whom is entirely vanilla's, read through
    /// <c>Tradeable</c>. The one judgement it makes is which category a thing belongs in, and getting that wrong
    /// files a row under the wrong heading rather than mispricing anything.
    /// </summary>
    internal static class TradeCatalog
    {
        internal const string All = "all";
        internal const string Favourites = "fav";
        internal const string Wants = "want";

        /// <summary>
        /// The view holding everything this trader will not buy at all.
        ///
        /// <b>Hidden from every other view, which is a reversal.</b> The first cut kept refused rows in the list
        /// with the reason in place of their count, on the argument that a list which quietly omits things
        /// answers "where is my hyperweave" with silence. In practice a specialised trader refuses nearly
        /// everything a colony owns -- reported on 2026-08-25 against a shaman merchant, where the sell view was
        /// a wall of "Trader is not willing to buy this" with the four sellable rows lost in it -- so the
        /// argument was right about the question and wrong about the cost of answering it that way.
        ///
        /// Given its own rail entry rather than dropped, so the answer is still one click away and its count is
        /// visible without clicking. That keeps what the original reasoning was actually protecting.
        /// </summary>
        internal const string Refused = "refused";

        /// <summary>
        /// How the table is ordered, beyond the two passes that always come first.
        ///
        /// <b>Anything in the deal, then favourites, then this.</b> Those two are not choices: a row you have
        /// committed to is a row you may want to change, and hunting for it is the whole complaint about
        /// vanilla's list. What the player picks is how the remaining several hundred rows are arranged.
        /// </summary>
        internal enum TradeSort
        {
            Category,
            Name,
            Price,
            Held
        }

        internal static string NameOfSort(TradeSort sort)
        {
            switch (sort)
            {
                case TradeSort.Name:
                    return "Name";
                case TradeSort.Price:
                    return "Price";
                case TradeSort.Held:
                    return "How many held";
                default:
                    return "Category";
            }
        }

        /// <summary>What the table is currently ordered by. One setting for the window, not per view.</summary>
        internal static TradeSort Sort = TradeSort.Category;

        /// <summary>
        /// The categories, in pill order.
        ///
        /// <b>Ordered by how often somebody is looking for one,</b> not alphabetically and not by how many rows
        /// each holds. Medicine and food are what a colony runs out of; people are rare and consequential and
        /// belong at the bottom where they are not scrolled past. A count-ordered rail would rearrange itself
        /// between one trader and the next, which is the one thing a rail must not do.
        /// </summary>
        private static readonly string[] Order =
        {
            "medicine", "food", "drugs", "resources", "weapons", "apparel", "animals", "people", "other"
        };

        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "medicine", "Medicine" },
            { "food", "Food" },
            { "drugs", "Drugs" },
            { "resources", "Resources" },
            { "weapons", "Weapons" },
            { "apparel", "Apparel" },
            { "animals", "Animals" },
            { "people", "People" },
            { "other", "Other" }
        };

        /// <summary>
        /// Which category a tradeable belongs in.
        ///
        /// <b>Tested in order of how specific each test is,</b> because the categories overlap by nature: beer is
        /// a drug and a food, a medicine is a resource, and a wooden club is a weapon before it is anything made
        /// of wood. First match wins, and the order is the answer to "what would a player call this".
        ///
        /// <b>Pawns are split by what they are, not by what they cost.</b> A colonist being sold and a muffalo
        /// being bought both arrive as pawns in the same list, and filing them together would put a person under
        /// a heading with the pack animals.
        /// </summary>
        internal static string CategoryOf(Tradeable tradeable)
        {
            return UIGuard.Try("Trade.Category", () =>
            {
                ThingDef def = tradeable?.ThingDef;

                if (def == null)
                    return "other";

                if (def.race != null)
                    return def.race.Humanlike ? "people" : "animals";

                if (def.IsMedicine)
                    return "medicine";

                if (def.IsDrug)
                    return "drugs";

                if (def.IsNutritionGivingIngestible)
                    return "food";

                if (def.IsWeapon)
                    return "weapons";

                if (def.IsApparel)
                    return "apparel";

                // CountAsResource is the game's own answer to "does this stack in the resource readout", which is
                // as close as RimWorld gets to a definition of a raw material.
                if (def.CountAsResource || def.IsStuff)
                    return "resources";

                return "other";
            }, "other", null);
        }

        internal static string NameOf(string category)
        {
            string name;

            return Names.TryGetValue(category, out name) ? name : "Other";
        }

        /// <summary>The category keys, in the order they are offered. See <see cref="Order"/>.</summary>
        internal static string[] Categories
        {
            get { return Order; }
        }

        /// <summary>
        /// The letter on a row's badge.
        ///
        /// <b>A type marker, not an abbreviation.</b> One character cannot name a thing, and is not trying to:
        /// it is there so that a column of rows reads as a column of kinds at a glance, and so that scrolling
        /// past forty resources looks different from scrolling past four people. Taken from the category rather
        /// than the item's own name, so every medicine carries the same mark.
        /// </summary>
        internal static string BadgeOf(Tradeable tradeable)
        {
            if (tradeable != null && tradeable.IsCurrency)
                return "$";

            string category = CategoryOf(tradeable);

            return category.NullOrEmpty() ? "?" : category.Substring(0, 1).ToUpperInvariant();
        }

        /// <summary>
        /// The line under a row's name: what kind of thing it is, and the one number that decides whether to
        /// carry it.
        ///
        /// <b>Different questions for different things, which is why this is not one format string.</b> For an
        /// item the useful pair is its category and its mass, because mass is what a caravan runs out of and it
        /// is nowhere else on this screen. For a person it is who they are and what they are good at, since
        /// nobody buys a stranger by weight. For an animal it is what stage of life it is at.
        ///
        /// Kept to one line and to facts the game already holds. Anything longer competes with the name above it.
        /// </summary>
        internal static string Subtitle(Tradeable tradeable)
        {
            return UIGuard.Try<string>("Trade.Subtitle", () =>
            {
                if (tradeable == null || tradeable.ThingDef == null)
                    return null;

                Pawn pawn = tradeable.AnyThing as Pawn;

                if (pawn != null)
                    return Who(pawn);

                ThingDef def = tradeable.ThingDef;

                List<string> parts = new List<string>();

                ThingCategoryDef first = def.FirstThingCategory;

                if (first != null)
                    parts.Add(first.LabelCap);

                float mass = def.BaseMass;

                if (mass > 0f)
                    parts.Add(mass.ToString("0.##") + " kg each");

                return parts.Count == 0 ? null : string.Join(" · ", parts.ToArray());
            }, null, null);
        }

        /// <summary>
        /// A person or animal in one line: what they are, and what they are worth having.
        ///
        /// <b>Two skills rather than all twelve.</b> The question a trade row raises about a stranger is whether
        /// they are worth the silver, and the honest short answer is the best thing they do. Traits follow
        /// because they are the half of a colonist that a skill list cannot warn you about.
        /// </summary>
        private static string Who(Pawn pawn)
        {
            List<string> parts = new List<string>();

            if (pawn.RaceProps != null && pawn.RaceProps.Animal)
            {
                if (pawn.ageTracker != null && pawn.ageTracker.CurLifeStage != null)
                    parts.Add(pawn.ageTracker.CurLifeStage.LabelCap);

                if (pawn.gender != Gender.None)
                    parts.Add(pawn.gender.GetLabel().CapitalizeFirst());

                if (pawn.training != null && pawn.RaceProps.trainability != TrainabilityDefOf.None)
                    parts.Add(pawn.RaceProps.trainability.LabelCap + " trainability");

                return parts.Count == 0 ? null : string.Join(" · ", parts.ToArray());
            }

            if (pawn.story != null && pawn.story.Adulthood != null)
                parts.Add(pawn.story.Adulthood.TitleCapFor(pawn.gender));
            else if (pawn.story != null && pawn.story.Childhood != null)
                parts.Add(pawn.story.Childhood.TitleCapFor(pawn.gender));

            string skills = Best(pawn);

            if (!skills.NullOrEmpty())
                parts.Add(skills);

            if (pawn.story != null && pawn.story.traits != null)
            {
                List<Trait> traits = pawn.story.traits.allTraits;

                List<string> named = new List<string>();

                for (int i = 0; traits != null && i < traits.Count && named.Count < 2; i++)
                {
                    if (traits[i] != null && !traits[i].Suppressed)
                        named.Add(traits[i].LabelCap);
                }

                if (named.Count > 0)
                    parts.Add(string.Join(", ", named.ToArray()));
            }

            return parts.Count == 0 ? null : string.Join(" · ", parts.ToArray());
        }

        /// <summary>The two highest skills, named with their level.</summary>
        private static string Best(Pawn pawn)
        {
            if (pawn.skills == null || pawn.skills.skills == null)
                return null;

            SkillRecord first = null;
            SkillRecord second = null;

            for (int i = 0; i < pawn.skills.skills.Count; i++)
            {
                SkillRecord record = pawn.skills.skills[i];

                if (record == null || record.TotallyDisabled)
                    continue;

                if (first == null || record.Level > first.Level)
                {
                    second = first;
                    first = record;
                }
                else if (second == null || record.Level > second.Level)
                {
                    second = record;
                }
            }

            if (first == null)
                return null;

            string text = first.def.skillLabel.CapitalizeFirst() + " " + first.Level;

            if (second != null && second.Level > 0)
                text += ", " + second.def.skillLabel.CapitalizeFirst() + " " + second.Level;

            return text;
        }

        /// <summary>
        /// Whether this tradeable belongs in the view for one side of the deal.
        ///
        /// <b>A row is in the buy view when the trader has some, and in the sell view when the colony does</b> --
        /// which is not the same as "the count is positive". A player who has queued a sale still needs to see the
        /// row in the sell view to change their mind about it, and a thing the colony holds none of has no place
        /// in a list of what to sell.
        ///
        /// One exception, and it earns its keep: a row with a count already set stays in its view whatever the
        /// holdings say. Buying the trader's last three of something drops their count to zero, and a row that
        /// vanished at that moment would take the player's own decision off the screen with it.
        /// </summary>
        private static bool Belongs(Tradeable tradeable, TradeSide side)
        {
            if (tradeable == null || tradeable.IsCurrency || !tradeable.HasAnyThing)
                return false;

            // Whether the trader will deal in this at all is decided in Rows rather than here, because it does
            // not remove a row from the window -- it moves it to the Not accepted view. This method answers only
            // "does the right side hold any of it".
            if (side == TradeSide.Buy)
                return tradeable.CountHeldBy(Transactor.Trader) > 0 || tradeable.CountToTransfer > 0;

            return tradeable.CountHeldBy(Transactor.Colony) > 0 || tradeable.CountToTransfer != 0;
        }

        /// <summary>
        /// The rows for a view, in the order they will be drawn.
        /// </summary>
        /// <param name="category">A category key, or <see cref="All"/>, <see cref="Favourites"/> or <see cref="Wants"/>.</param>
        /// <param name="search">A search box's text. Null or empty matches everything.</param>
        /// <remarks>
        /// <b>Sorted in four passes, in decreasing order of how much the player cares.</b> Anything already in the
        /// deal comes first, because a row you have committed to is a row you may want to change and hunting for
        /// it is the whole complaint about vanilla's list. Favourites next, then the refusals sink to the bottom
        /// -- they are worth seeing and never worth seeing first -- and the rest fall back to label order, which
        /// is the only ordering a player can predict.
        /// </remarks>
        internal static List<Tradeable> Rows(TradeDeal deal, TradeSide side, string category, string search,
            List<Tradeable> into)
        {
            into.Clear();

            if (deal == null)
                return into;

            UIGuard.Try("Trade.Rows", () =>
            {
                List<Tradeable> all = deal.AllTradeables;

                // <b>The refused rows are in exactly one view and never in any other.</b> Their own view holds
                // all of them and nothing else; every other view holds none of them. A row cannot be in both,
                // which is what keeps the rail's counts adding up.
                bool wantRefused = category == Refused;

                for (int i = 0; all != null && i < all.Count; i++)
                {
                    Tradeable tradeable = all[i];

                    if (!Belongs(tradeable, side))
                        continue;

                    if (!tradeable.TraderWillTrade != wantRefused)
                        continue;

                    if (!Matches(tradeable, category))
                        continue;

                    if (!search.NullOrEmpty()
                        && tradeable.Label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    into.Add(tradeable);
                }

                into.Sort(Compare);
            }, "The trade list could not be built. Closing and reopening the window rebuilds it.");

            return into;
        }

        private static bool Matches(Tradeable tradeable, string category)
        {
            // The refused view's filtering is done by the willing-to-trade test in Rows, so everything reaching
            // here has already earned its place in it.
            if (category.NullOrEmpty() || category == All || category == Refused)
                return true;

            if (category == Favourites)
                return TradeWants.IsFavourite(tradeable.ThingDef);

            if (category == Wants)
                return TradeWants.Wanted(tradeable.ThingDef) > 0;

            return CategoryOf(tradeable) == category;
        }

        private static int Compare(Tradeable left, Tradeable right)
        {
            int order = Rank(right).CompareTo(Rank(left));

            if (order != 0)
                return order;

            // The player's chosen order, applied to everything the two fixed passes above did not already
            // separate. Every branch falls through to the name, so the list is never arbitrary among equals --
            // an unstable order is what makes a long list feel like it is moving under you.
            switch (Sort)
            {
                case TradeSort.Price:
                {
                    float priceLeft = Price(left);
                    float priceRight = Price(right);

                    if (!Mathf.Approximately(priceLeft, priceRight))
                        return priceRight.CompareTo(priceLeft);

                    break;
                }

                case TradeSort.Held:
                {
                    int heldLeft = left.CountHeldBy(Transactor.Colony);
                    int heldRight = right.CountHeldBy(Transactor.Colony);

                    if (heldLeft != heldRight)
                        return heldRight.CompareTo(heldLeft);

                    break;
                }

                case TradeSort.Category:
                {
                    int categoryOrder = IndexOf(CategoryOf(left)).CompareTo(IndexOf(CategoryOf(right)));

                    if (categoryOrder != 0)
                        return categoryOrder;

                    break;
                }
            }

            string a = left.Label ?? string.Empty;
            string b = right.Label ?? string.Empty;

            return string.Compare(a, b, System.StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>Where a category falls in the rail's own order, so the table and the pills agree.</summary>
        private static int IndexOf(string category)
        {
            for (int i = 0; i < Order.Length; i++)
            {
                if (Order[i] == category)
                    return i;
            }

            return Order.Length;
        }

        /// <summary>What a row costs, read in whichever direction the trader will actually deal.</summary>
        private static float Price(Tradeable tradeable)
        {
            return UIGuard.Try("Trade.SortPrice",
                () => tradeable.TraderWillTrade ? tradeable.GetPriceFor(TradeAction.PlayerBuys) : 0f, 0f, null);
        }

        /// <summary>Higher sorts earlier. See the sorting note on <see cref="Rows"/>.</summary>
        private static int Rank(Tradeable tradeable)
        {
            if (!tradeable.TraderWillTrade)
                return -1;

            if (tradeable.CountToTransfer != 0)
                return 3;

            if (TradeWants.IsFavourite(tradeable.ThingDef))
                return 2;

            return TradeWants.Wanted(tradeable.ThingDef) > 0 ? 1 : 0;
        }

        /// <summary>
        /// The rail for a view: the saved group, then every category with what it holds.
        ///
        /// <b>Counted against the search box but not against the category,</b> which is what makes the numbers
        /// mean anything. A rail whose counts changed when you clicked one of its entries would be telling you
        /// about your own click; these tell you what is on the ship.
        /// </summary>
        internal static List<TradeRailEntry> Rail(TradeDeal deal, TradeSide side, string search,
            List<TradeRailEntry> into, List<Tradeable> scratch)
        {
            into.Clear();

            Rows(deal, side, All, search, scratch);

            into.Add(TradeRailEntry.Of(All, side == TradeSide.Buy ? "Their stock" : "Your goods", scratch.Count));

            int favourites = 0;
            int wants = 0;

            Dictionary<string, int> counts = new Dictionary<string, int>();

            for (int i = 0; i < scratch.Count; i++)
            {
                Tradeable tradeable = scratch[i];

                string category = CategoryOf(tradeable);

                int held;

                counts[category] = counts.TryGetValue(category, out held) ? held + 1 : 1;

                if (TradeWants.IsFavourite(tradeable.ThingDef))
                    favourites++;

                if (TradeWants.Wanted(tradeable.ThingDef) > 0)
                    wants++;
            }

            // The saved group is drawn even when both are empty, because it is also where the player goes to make
            // one. A group that only appeared once you already had a favourite would be a feature you could only
            // find by accident.
            into.Add(TradeRailEntry.Group("Saved"));
            into.Add(TradeRailEntry.Of(Wants, "Standing wants", wants));
            into.Add(TradeRailEntry.Of(Favourites, "Favourites", favourites));

            into.Add(TradeRailEntry.Group(side == TradeSide.Buy ? "On offer" : "In the colony"));

            for (int i = 0; i < Order.Length; i++)
            {
                string category = Order[i];

                int held;

                counts.TryGetValue(category, out held);

                // An empty category is dropped rather than dimmed here, unlike the rail's usual habit: there are
                // nine of them and most traders carry four, so keeping them all would be five dead rows to scroll
                // past on every screen. The saved group above is the part that has to hold still, and it does.
                if (held > 0)
                    into.Add(TradeRailEntry.Of(category, NameOf(category), held));
            }

            // <b>Where the hidden rows went.</b> Everything this trader refuses outright lives here and nowhere
            // else, so "why is my hyperweave not listed" has an answer with a number on it rather than requiring
            // the player to know that a shaman merchant deals in four things. Offered only when there is
            // something behind it -- a trader who takes everything should not be told about a view of nothing.
            int refused = Rows(deal, side, Refused, search, scratch).Count;

            if (refused > 0)
            {
                TradeRailEntry entry = TradeRailEntry.Of(Refused, "Not accepted", refused);

                entry.CountColor = UIFramework.Defs.UIColorPaletteDef.Active.TextDisabled;

                into.Add(entry);
            }

            return into;
        }

        /// <summary>
        /// Whether a row is refused, and vanilla's own sentence for why.
        ///
        /// <b>Both refusals are the game's, quoted rather than paraphrased.</b> The trader kind's is a flat
        /// "will not trade"; the Ideology one is a negotiator refusing to sell people, and it is checked exactly
        /// the way <c>TradeUI</c> checks it -- the same history event, asked of the same pawn -- so a negotiator
        /// who would refuse at the moment of sale is the negotiator who refuses here.
        /// </summary>
        internal static string RefusalFor(Tradeable tradeable)
        {
            return UIGuard.Try<string>("Trade.Refusal", () =>
            {
                if (tradeable == null)
                    return null;

                if (!tradeable.TraderWillTrade)
                    return "TraderWillNotTrade".Translate();

                if (!ModsConfig.IdeologyActive || TradeSession.trader == null
                    || TradeSession.playerNegotiator == null)
                    return null;

                if (!TransferableUIUtility.TradeIsPlayerSellingToSlavery(tradeable, TradeSession.trader.Faction))
                    return null;

                HistoryEvent sale = new HistoryEvent(HistoryEventDefOf.SoldSlave,
                    TradeSession.playerNegotiator.Named(HistoryEventArgsNames.Doer));

                return sale.DoerWillingToDo()
                    ? null
                    : (string) "NegotiatorWillNotTradeSlaves".Translate(TradeSession.playerNegotiator);
            }, null, null);
        }

        /// <summary>
        /// The line under a row's name: how many the colony has, and what that is worth knowing about.
        ///
        /// <b>Rough by design, and hedged in wording.</b> Aaron settled this on 2026-08-25: a projection from
        /// stock and current use, never a simulation, and never gating a control. It only has to answer "is this
        /// the trip to buy medicine". A hunting party or one more mouth moves the number, which is why it says
        /// "about" and why nothing is refused on the strength of it.
        ///
        /// <b>The food estimate divides the colony's own edible nutrition by its own eaters.</b>
        /// <c>ResourceCounter.TotalHumanEdibleNutrition</c> is a running total the game keeps anyway, and 1.6
        /// nutrition a day is what a colonist eats -- so this is two numbers the game already has, divided. It is
        /// deliberately not <c>DaysWorthOfFoodCalculator</c>, which exists for caravans, walks every pawn's
        /// inventory and would run once per row per frame.
        /// </summary>
        internal static string NeedLine(Tradeable tradeable, Map map)
        {
            return UIGuard.Try<string>("Trade.NeedLine", () =>
            {
                if (tradeable == null || tradeable.ThingDef == null)
                    return null;

                int held = tradeable.CountHeldBy(Transactor.Colony);

                ThingDef def = tradeable.ThingDef;

                if (def.race != null)
                    return null;

                if (held <= 0)
                    return "You hold none";

                string line = "You hold " + held.ToStringCached();

                // <b>Worded as a fact about the colony, because that is what it is.</b> It read "about 232 days
                // of food at current use" on the row for one item, which says that item feeds you for 232 days;
                // the number is the colony's whole larder divided by its eaters and is the same on every food
                // row. Seen on a screenshot 2026-08-25, on a vial of corrupted blood among other things.
                if (map != null && def.IsNutritionGivingIngestible && def.ingestible != null
                    && def.ingestible.HumanEdible)
                {
                    float days = FoodDays(map);

                    if (days > 0f)
                        line += " · colony has about " + days.ToString("0.#") + " days of food";
                }

                return line;
            }, null, null);
        }

        /// <summary>Nutrition a colonist eats in a day, which is RimWorld's own figure for a human.</summary>
        private const float NutritionPerColonistDay = 1.6f;

        private static float FoodDays(Map map)
        {
            if (map.resourceCounter == null || map.mapPawns == null)
                return 0f;

            int eaters = map.mapPawns.FreeColonistsSpawnedCount;

            if (eaters <= 0)
                return 0f;

            return map.resourceCounter.TotalHumanEdibleNutrition / (eaters * NutritionPerColonistDay);
        }

        /// <summary>
        /// Which way a count runs in this view: <c>+1</c> when the player is asking for a positive number.
        ///
        /// <b>Buying is positive and selling is negative, in trade mode.</b> That is not a convention we chose:
        /// <c>Tradeable.PositiveCountDirection</c> is Source while trading, so a positive count moves goods to the
        /// colony, and vanilla's own header says "positive buys, negative sells". In gift mode the same property
        /// answers Destination and the sign flips, which is why this asks rather than assuming.
        /// </summary>
        internal static int SignFor(TradeSide side)
        {
            return side == TradeSide.Sell ? -1 : 1;
        }

        /// <summary>Which direction a price should be read in for a view.</summary>
        internal static TradeAction ActionFor(TradeSide side)
        {
            return side == TradeSide.Buy ? TradeAction.PlayerBuys : TradeAction.PlayerSells;
        }
    }
}
