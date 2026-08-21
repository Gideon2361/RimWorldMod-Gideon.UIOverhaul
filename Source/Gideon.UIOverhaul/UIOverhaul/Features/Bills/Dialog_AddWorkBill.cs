using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Recipe picker for new bills on a workbench, laid out exactly like the growing zone's plant picker: dark
    /// chrome with no title bar, a fixed width card list on the left, a hero and a collapsible detail pane on the
    /// right, and an action bar along the bottom.
    ///
    /// <b>Asked for by name on 2026-08-19.</b> The workbench used to switch you to the colony wide bills tab,
    /// which answered a different question from the one you asked by clicking on a bench. This is the growing zone
    /// window applied to recipes, so the two halves of the mod that add work to a thing now work the same way.
    ///
    /// <b>Every card is a decision, so every card carries the figures behind it.</b> Vanilla's answer is a float
    /// menu of names, which tells you nothing about what a recipe costs, what it needs, or whether anybody in the
    /// colony can do it. The whole point of a card is that the choice is made from the list rather than by picking
    /// something and finding out afterwards.
    ///
    /// <b>Drawn through <c>GzpPalette</c> deliberately.</b> Its colours all resolve to the active
    /// <c>UIColorPaletteDef</c> and its helpers are what give the grow zone window its look, so sharing them is
    /// the only way "the same window" stays true as the palette changes. See the longer note on
    /// <see cref="WorkBillRow"/>.
    /// </summary>
    public class Dialog_AddWorkBill : Window
    {
        private const float ListWidth = 444f;
        private const float CardHeight = 108f;
        private const float LineHeight = 26f;
        private const float RowStep = 28f;
        private const float StatIcon = 20f;
        private const float CardGap = 6f;
        private const float HeaderHeight = 46f;
        private const float HeroHeight = 68f;
        private const float FooterHeight = 52f;
        private const float Pad = 12f;
        private const float EdgeInset = 8f;
        private const float Gutter = 8f;

        /// <summary>Side of the favorite star. Large enough to hit without hunting, small enough to ignore.</summary>
        private const float StarSize = 18f;

        /// <summary>Short numeric stats per card, laid out in one row of this many columns.</summary>
        private const int StatColumns = 4;

        /// <summary>
        /// Which detail sections are folded, shared by every instance of this window.
        ///
        /// Static so that folding a section stays folded the next time the window opens, which is what a player
        /// expects of a section they have decided they do not care about. It is a display preference and is
        /// deliberately not written to disk.
        /// </summary>
        private static readonly HashSet<string> CollapsedSections = new HashSet<string>();

        private readonly Building_WorkTable bench;
        private readonly List<RecipeOffer> offers;
        private readonly Action added;

        private RecipeOffer selected;
        private string search = "";
        private Vector2 listScroll;
        private Vector2 detailScroll;
        private bool listDragging;
        private float listDragOffset;
        private bool detailDragging;
        private float detailDragOffset;

        /// <summary>
        /// Height the detail sections came to when they were last drawn. Zero until the first frame has laid them
        /// out, which the detail pane reads as "no measurement yet".
        /// </summary>
        private float measuredDetailHeight;

        /// <summary>
        /// The card chrome, shared by every row. Reconfigured per recipe as the list is drawn: cards here are
        /// drawn and forgotten inside a loop, so one instance is enough and allocating one per recipe per frame
        /// would be waste.
        /// </summary>
        private readonly UICardControl recipeCard = new UICardControl();

        private readonly UICardControl heroCard = new UICardControl();

        public Dialog_AddWorkBill(Building_WorkTable table, Action onAdded)
        {
            bench = table;
            added = onAdded;
            offers = BillActions.Available(table);
            selected = offers.Count > 0 ? offers[0] : null;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(900f, 640f);

        protected override float Margin => 0f;

        /// <summary>
        /// What the list shows: everything the bench offers, narrowed by the search box, starred first.
        ///
        /// The ordering is applied after filtering rather than once up front, so a search that matches a favorite
        /// still floats it to the top of whatever is left.
        /// </summary>
        private List<RecipeOffer> Visible()
        {
            if (search.NullOrEmpty())
                return RecipeFavorites.Ordered(offers);

            List<RecipeOffer> found = new List<RecipeOffer>();

            foreach (RecipeOffer offer in offers)
            {
                if (offer.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    found.Add(offer);
            }

            return RecipeFavorites.Ordered(found);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + HeaderHeight);

            UIGuardedPanel.Draw("Bills.AddBillDialog", inRect, () => Contents(inRect),
                "The add-bill window shows a failure notice; bills already on the bench are unaffected.");
        }

        private void Contents(Rect inRect)
        {
            // Chrome fill for the whole window. The header, the outer border and the gutter between the panels
            // are all just this showing through.
            Widgets.DrawBoxSolid(inRect, GzpPalette.BGD);
            Text.Font = GameFont.Small;

            Header(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight));

            Rect body = new Rect(inRect.x, inRect.y + HeaderHeight, inRect.width,
                inRect.height - HeaderHeight - FooterHeight).ContractedBy(EdgeInset);

            Rect list = new Rect(body.x, body.y, ListWidth, body.height);
            Rect detail = new Rect(list.xMax + Gutter, body.y, body.width - ListWidth - Gutter, body.height);

            List(list);
            Detail(detail);
            Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight));

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void Header(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            Color previous = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = GzpPalette.Stat;

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 8f, 300f, 30f), "Add Bill");

            Text.Font = GameFont.Small;

            Rect close = new Rect(rect.xMax - Pad - 24f, rect.y + 11f, 24f, 24f);
            Rect box = new Rect(close.x - 10f - 240f, rect.y + 10f, 240f, 26f);

            GUI.color = GzpPalette.Stat;
            search = GzpPalette.FlatTextField(box, search);

            if (search.NullOrEmpty() && !Mouse.IsOver(box))
            {
                GUI.color = GzpPalette.TextDim;
                Widgets.Label(box.ContractedBy(7f, 2f), "Search recipes...");
            }

            GUI.color = previous;

            if (GzpPalette.IconButton(close, GzpTex.Close, "Close"))
                Close();
        }

        private void List(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BG);

            Rect inner = rect.ContractedBy(Pad);
            List<RecipeOffer> visible = Visible();

            float height = visible.Count * (CardHeight + CardGap);
            Rect view = new Rect(0f, 0f, GzpPalette.ContentWidth(inner), height);

            Widgets.BeginScrollView(inner, ref listScroll, view, false);

            float y = 0f;

            foreach (RecipeOffer offer in visible)
            {
                Card(new Rect(0f, y, view.width, CardHeight), offer);

                y += CardHeight + CardGap;
            }

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(inner, height, ref listScroll, ref listDragging, ref listDragOffset);

            if (visible.Count != 0)
                return;

            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;

            Widgets.Label(new Rect(inner.x, inner.y + 8f, inner.width, 24f),
                offers.Count == 0 ? "This bench has no recipes available." : "No matching recipes.");

            GUI.color = previous;
        }

        private void Card(Rect card, RecipeOffer offer)
        {
            bool chosen = offer == selected;
            RecipeDef recipe = offer.Recipe;

            recipeCard.Padding = 0f;
            recipeCard.AccentColor = Workable(recipe) ? GzpPalette.Accent : GzpPalette.Bad;
            recipeCard.BackgroundColor = GzpPalette.PanelBG;
            recipeCard.Selected = chosen;

            // Chrome only. Anything clickable drawn on top of the card has to be asked before the card's own
            // click, so the card takes its click at the end of this method instead.
            recipeCard.DrawChrome(card);

            Rect icon = new Rect(card.x + 10f, card.y + 8f, 44f, 44f);

            if (offer.Icon != null)
                Widgets.DefIcon(icon, offer.Icon);

            float textX = icon.xMax + 10f;
            float textWidth = card.xMax - 10f - textX;
            float headWidth = textWidth - 36f;

            GzpPalette.CardLabel(new Rect(textX, card.y + 6f, headWidth, LineHeight), offer.Label,
                chosen ? GzpPalette.Accent : GzpPalette.Stat);

            GzpPalette.CardLabel(new Rect(textX, card.y + 6f + RowStep, headWidth, LineHeight), Source(recipe),
                GzpPalette.TextDim);

            float statY = card.y + 6f + RowStep * 2f + 4f;
            float column = textWidth / StatColumns;
            int slot = 0;

            foreach (Stat stat in Stats(recipe))
            {
                StatPair(textX + slot % StatColumns * column, statY + slot / StatColumns * RowStep, column,
                    stat.Icon, stat.Value, stat.Tip, stat.Color);

                slot++;
            }

            Star(card, offer);

            // The card's own click, taken last so every control above it got its chance first. The star is drawn
            // and hit tested before this for exactly that reason: GUI.Button consumes on mouse down, so whatever
            // covers the pointer first takes the event.
            if (Widgets.ButtonInvisible(card))
                selected = offer;
        }

        /// <summary>
        /// The favorite star, in the icon's column below it.
        ///
        /// <b>Dim until it is either starred or under the cursor.</b> A column of bright stars down an unsorted
        /// list would compete with the recipe names, which are what somebody is actually reading.
        ///
        /// Positioned from the same numbers as the icon rather than its own literals, so moving the icon moves
        /// this with it.
        /// </summary>
        private static void Star(Rect card, RecipeOffer offer)
        {
            bool favorite = RecipeFavorites.IsFavorite(offer);

            Rect star = new Rect(card.x + 10f + (44f - StarSize) * 0.5f, card.y + 8f + 44f + 6f, StarSize,
                StarSize);

            bool over = Mouse.IsOver(star);
            Texture2D shape = favorite ? UIShapes.StarFilled : UIShapes.StarHollow;

            if (shape != null)
            {
                Color previous = GUI.color;

                GUI.color = favorite
                    ? GzpPalette.Accent
                    : over
                        ? GzpPalette.Stat
                        : GzpPalette.TextDim;

                GUI.DrawTexture(star, shape);

                GUI.color = previous;
            }

            TooltipHandler.TipRegion(star, (TipSignal)(favorite
                ? "A favorite. Starred recipes are listed first, on every bench and in every colony."
                : "Star this recipe so it is listed first, on every bench and in every colony."));

            if (!Widgets.ButtonInvisible(star))
                return;

            RecipeFavorites.Toggle(offer);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>One figure on a card, with an icon and a tooltip.</summary>
        private struct Stat
        {
            internal Texture Icon;
            internal string Value;
            internal string Tip;
            internal Color? Color;
        }

        /// <summary>
        /// The short figures on a card: what it takes, what it needs, and what comes out.
        ///
        /// <b>Chosen to answer "should I add this?" rather than to fill the row.</b> Work is what it costs, the
        /// skill is who can do it, the ingredient count is how much bookkeeping it brings, and the yield is the
        /// point of the whole thing. Anything else is in the detail pane where there is room to explain it.
        /// </summary>
        private IEnumerable<Stat> Stats(RecipeDef recipe)
        {
            yield return new Stat
            {
                Icon = GzpTex.Lifespan,
                Value = Mathf.RoundToInt(recipe.WorkAmountTotal(null)).ToString(),
                Tip = "Work to make one, before any worker's speed is applied."
            };

            if (recipe.workSkill != null)
            {
                int need = recipe.skillRequirements != null && recipe.skillRequirements.Count > 0
                    ? recipe.skillRequirements[0].minLevel
                    : 0;

                bool anybody = Workable(recipe);

                yield return new Stat
                {
                    Icon = GzpTex.Skill,
                    Value = recipe.workSkill.LabelCap + (need > 0 ? " " + need : string.Empty),
                    Tip = anybody
                        ? "Uses " + recipe.workSkill.label + "."
                        : "Nobody in the colony meets this recipe's skill requirement.",
                    Color = anybody ? (Color?)null : GzpPalette.Bad
                };
            }

            int ingredients = recipe.ingredients?.Count ?? 0;

            yield return new Stat
            {
                Icon = GzpTex.Beauty,
                Value = ingredients.ToString(),
                Tip = ingredients == 1 ? "Takes one ingredient." : "Takes " + ingredients + " ingredients."
            };

            ThingDef product = recipe.ProducedThingDef;

            if (product != null)
            {
                int count = recipe.products != null && recipe.products.Count > 0 ? recipe.products[0].count : 1;

                yield return new Stat
                {
                    Icon = GzpTex.Nutrition,
                    Value = count + "x",
                    Tip = "Makes " + count + " " + product.label + " per run."
                };
            }
        }

        private static void StatPair(float x, float y, float width, Texture icon, string value, string tip,
            Color? color = null)
        {
            Rect row = new Rect(x, y, width, LineHeight);

            if (icon != null)
                GUI.DrawTexture(new Rect(x, y + 3f, StatIcon, StatIcon), icon);

            Color previous = GUI.color;
            GUI.color = color ?? GzpPalette.Stat;

            GzpPalette.CardLabel(new Rect(x + StatIcon + 4f, y, Mathf.Max(0f, width - StatIcon - 6f), LineHeight),
                value, GUI.color);

            GUI.color = previous;

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(row, (TipSignal)tip);
        }

        /// <summary>
        /// Whether anybody in the colony meets the recipe's skill requirement.
        ///
        /// Asked of the recipe rather than reimplemented, and only of colonists on this bench's map, since that is
        /// who would actually walk to it. A colony with nobody who qualifies still gets to add the bill; the card
        /// just says so first, which is the point of a card.
        /// </summary>
        private bool Workable(RecipeDef recipe)
        {
            return UIGuard.Try("Bills.CardWorkable", () =>
            {
                List<Pawn> colonists = bench?.Map?.mapPawns?.FreeColonistsSpawned;

                if (colonists == null || colonists.Count == 0)
                    return true;

                foreach (Pawn pawn in colonists)
                {
                    if (recipe.PawnSatisfiesSkillRequirements(pawn))
                        return true;
                }

                return false;
            }, true, null);
        }

        private static string Source(RecipeDef recipe)
        {
            string mod = recipe?.modContentPack?.Name;

            return mod.NullOrEmpty() ? "RimWorld" : mod;
        }

        private void Detail(Rect rect)
        {
            // BG rather than PanelBG: the cards drawn inside this pane are PanelBG, so a PanelBG pane would
            // render the hero card invisible against it.
            Widgets.DrawBoxSolid(rect, GzpPalette.BG);

            if (selected == null)
            {
                Color dim = GUI.color;
                GUI.color = GzpPalette.TextDim;

                Widgets.Label(rect.ContractedBy(Pad), "No recipes available on this bench.");

                GUI.color = dim;

                return;
            }

            Rect inner = rect.ContractedBy(Pad);

            Hero(new Rect(inner.x, inner.y, inner.width, HeroHeight));

            Rect body = new Rect(inner.x, inner.y + HeroHeight + 8f, inner.width, inner.height - HeroHeight - 8f);
            float width = GzpPalette.ContentWidth(body);

            // Height taken from what the previous frame actually laid out rather than estimated. The sections vary
            // with the recipe, its description and whichever ones the player has folded, and an estimate that runs
            // over lets the pane scroll into blank space. Measuring is free as a side effect of drawing, one frame
            // late, which nothing can see, and self correcting because the next frame measures again.
            float height = measuredDetailHeight > 0f ? measuredDetailHeight : body.height;

            Widgets.BeginScrollView(body, ref detailScroll, new Rect(0f, 0f, width, height), false);

            float y = 0f;

            Sections(ref y, width);

            Widgets.EndScrollView();

            measuredDetailHeight = y;

            GzpPalette.FlatScrollbar(body, height, ref detailScroll, ref detailDragging, ref detailDragOffset);
        }

        private void Hero(Rect hero)
        {
            // No accent stripe and no hover: this is a heading, not a row in a list. AccentColor left null is
            // what suppresses the stripe, and DrawChrome is what leaves the info card button below clickable.
            heroCard.Padding = 0f;
            heroCard.AccentColor = null;
            heroCard.HoverHighlight = false;
            heroCard.BackgroundColor = GzpPalette.PanelBG;
            heroCard.BackgroundTexture = null;
            heroCard.BackgroundTint = null;
            heroCard.DrawChrome(hero);

            Rect icon = new Rect(hero.x + 10f, hero.y + 10f, 48f, 48f);

            if (selected.Icon != null)
                Widgets.DefIcon(icon, selected.Icon);

            Color previous = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = GzpPalette.Stat;

            Widgets.Label(new Rect(icon.xMax + 12f, hero.y + 18f, hero.width - 110f, 32f), selected.Label);

            Text.Font = GameFont.Small;
            GUI.color = previous;

            if (selected.Icon != null)
                Widgets.InfoCardButton(hero.xMax - 30f, hero.y + 12f, selected.Icon);
        }

        private void Sections(ref float y, float width)
        {
            RecipeDef recipe = selected.Recipe;

            if (!recipe.description.NullOrEmpty()
                && GzpPalette.SectionHeader(ref y, 0f, width, "Description", CollapsedSections))
            {
                Color previous = GUI.color;
                float height = Text.CalcHeight(recipe.description, width);

                GUI.color = GzpPalette.TextDim;

                Widgets.Label(new Rect(0f, y, width, height), recipe.description);

                GUI.color = previous;

                y += height + 6f;
            }

            if (GzpPalette.SectionHeader(ref y, 0f, width, "Ingredients", CollapsedSections))
            {
                List<IngredientCount> ingredients = recipe.ingredients;

                if (ingredients == null || ingredients.Count == 0)
                {
                    GzpPalette.InfoLine(ref y, 0f, width, "Consumes", "nothing");
                }
                else
                {
                    foreach (IngredientCount ingredient in ingredients)
                    {
                        GzpPalette.InfoLine(ref y, 0f, width, ingredient.Summary,
                            ingredient.IsFixedIngredient ? "fixed" : "your choice");
                    }
                }

                y += 6f;
            }

            if (GzpPalette.SectionHeader(ref y, 0f, width, "Yield", CollapsedSections))
            {
                ThingDef product = recipe.ProducedThingDef;

                GzpPalette.InfoLine(ref y, 0f, width, "Produces",
                    product == null ? "nothing carried away" : product.LabelCap.ToString());

                if (product != null)
                {
                    int count = recipe.products != null && recipe.products.Count > 0 ? recipe.products[0].count : 1;

                    GzpPalette.InfoLine(ref y, 0f, width, "Per run", count.ToString());
                    GzpPalette.InfoLine(ref y, 0f, width, "Market value",
                        product.BaseMarketValue.ToString("F0"));
                }

                y += 6f;
            }

            if (!GzpPalette.SectionHeader(ref y, 0f, width, "Requirements", CollapsedSections))
                return;

            GzpPalette.InfoLine(ref y, 0f, width, "Work",
                Mathf.RoundToInt(recipe.WorkAmountTotal(null)).ToString());

            if (recipe.workSkill != null)
                GzpPalette.InfoLine(ref y, 0f, width, "Skill", recipe.workSkill.LabelCap);

            if (recipe.skillRequirements != null)
            {
                foreach (SkillRequirement requirement in recipe.skillRequirements)
                {
                    if (requirement?.skill == null)
                        continue;

                    GzpPalette.InfoLine(ref y, 0f, width, requirement.skill.LabelCap + " needed",
                        requirement.minLevel.ToString(),
                        Workable(recipe) ? (Color?)null : GzpPalette.Bad);
                }
            }

            if (!Workable(recipe))
            {
                Color previous = GUI.color;
                GUI.color = GzpPalette.Bad;

                Widgets.Label(new Rect(0f, y, width, 36f),
                    "Nobody in this colony is skilled enough. The bill can still be added and will wait.");

                GUI.color = previous;

                y += 40f;
            }

            if (recipe.researchPrerequisite != null)
            {
                GzpPalette.InfoLine(ref y, 0f, width, "Research", recipe.researchPrerequisite.LabelCap);
            }

            y += 6f;
        }

        private void Footer(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            BillStack stack = bench?.billStack;
            int count = stack?.Count ?? 0;
            bool full = count >= BillCap.Current;

            Color previous = GUI.color;
            GUI.color = full ? GzpPalette.Bad : GzpPalette.TextDim;

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 16f, 340f, 24f),
                full
                    ? "Bill limit reached (" + BillCap.Current + ")."
                    : count + " / " + BillCap.Current + " bills on this bench");

            GUI.color = previous;

            Rect add = new Rect(rect.xMax - Pad - 150f, rect.y + 10f, 150f, 32f);

            // Stays open after adding, because adding a run of bills to a fresh bench is the common case and
            // reopening the window for each one is the sort of friction this whole feature exists to remove.
            if (!GzpPalette.GrayButton(add, "Add Bill", selected != null && !full, true))
                return;

            BillActions.Make(bench, selected, added);
        }
    }
}
