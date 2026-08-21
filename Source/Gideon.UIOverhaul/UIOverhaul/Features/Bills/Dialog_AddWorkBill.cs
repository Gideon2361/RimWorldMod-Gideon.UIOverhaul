using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
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

        /// <summary>The step strip between the header and the body.</summary>
        private const float StripHeight = 34f;

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

        /// <summary>The bench being added to. Null until the first step is answered.</summary>
        private Building_WorkTable bench;

        /// <summary>What that bench can make. Rebuilt when the bench changes, since it is the bench's own list.</summary>
        private List<RecipeOffer> offers = new List<RecipeOffer>();

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

        /// <summary>
        /// Opens on the recipe step for a bench that is already known.
        ///
        /// The bench tab and the plus on a bench heading both come in this way, because they have already answered
        /// the first question and asking it again would be a question with one answer.
        /// </summary>
        public Dialog_AddWorkBill(Building_WorkTable table, Action onAdded) : this(onAdded)
        {
            Choose(table);

            // Left false on this route, which is what makes step one a fact rather than a choice: the bench was
            // never picked here, so offering to go back to a screen the reader has not seen would step out of the
            // interaction rather than within it.
            askedForBench = false;
        }

        /// <summary>
        /// Opens on the bench step, for the colony window's Add bill.
        ///
        /// <b>Three steps rather than two float menus.</b> Add bill used to open a menu of every worktable and then
        /// a menu of every recipe, both bare lists of names, the first of which runs to sixty entries on a
        /// developed base with no way to tell two identical benches apart. Approved from a mockup on 2026-08-20.
        /// </summary>
        public Dialog_AddWorkBill(Action onAdded)
        {
            added = onAdded;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(900f, 640f);

        protected override float Margin => 0f;

        /// <summary>Which of the three screens is showing.</summary>
        private enum Step
        {
            Bench,
            Recipe,
            Setup
        }

        private Step step = Step.Bench;

        /// <summary>Whether this window asked which bench, as opposed to being told.</summary>
        private bool askedForBench = true;

        private readonly BenchGrid benches = new BenchGrid();

        private readonly WorkBillSettingsPane settings = new WorkBillSettingsPane();

        /// <summary>
        /// The bill being configured on the last step, made but not yet on the bench.
        ///
        /// <b>A real bill rather than a draft of one, and that is the design.</b> The obvious build was a step that
        /// holds answers and commits them at the end, which would have given every control in
        /// <see cref="WorkBillSettingsPane"/> a second behaviour, and the failure would be a setting that silently
        /// does nothing in one of the two places it is offered. Handing over a genuine
        /// <c>Bill_Production</c> means the pane cannot tell the difference and does not have to.
        ///
        /// <b>Its <c>billStack</c> is set even though it is not in that stack.</b> The worker menu reaches through
        /// <c>bill.billStack.billGiver</c> to find the work giver, and the ingredient tree wants a map; without
        /// this the pane would draw with an empty pawn list. Being in the stack is what makes a bill live, and it
        /// is not in it, so nothing works this bill until Add is pressed.
        /// </summary>
        private Bill_Production draft;

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
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // Chrome fill for the whole window. The header, the outer border and the gutter between the panels
            // are all just this showing through.
            Widgets.DrawBoxSolid(inRect, GzpPalette.BGD);
            Text.Font = GameFont.Small;

            Header(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight));
            Strip(new Rect(inRect.x, inRect.y + HeaderHeight, inRect.width, StripHeight), palette);

            Rect body = new Rect(inRect.x, inRect.y + HeaderHeight + StripHeight, inRect.width,
                inRect.height - HeaderHeight - StripHeight - FooterHeight).ContractedBy(EdgeInset);

            switch (step)
            {
                case Step.Bench:
                    Building_WorkTable picked = benches.Draw(body, search, false, palette);

                    if (picked != null)
                        Choose(picked);

                    break;

                case Step.Setup:
                    settings.Draw(body, draft, palette, false);

                    break;

                default:
                    Rect list = new Rect(body.x, body.y, ListWidth, body.height);
                    Rect detail = new Rect(list.xMax + Gutter, body.y, body.width - ListWidth - Gutter,
                        body.height);

                    List(list);
                    Detail(detail);

                    break;
            }

            Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// The three steps across the top, current one carrying the accent rule.
        ///
        /// <b>An answered step reads back as its answer,</b> so the strip is a record of the choices rather than a
        /// menu of them: step one stops saying Bench and says which bench. Clicking an answered step goes back to
        /// it. A step ahead of where you are is dim and inert, because it still tells you the shape of what you are
        /// doing even when you cannot reach it.
        ///
        /// <b>Step one is not clickable when the window opened on step two,</b> which is the bench tab's route: the
        /// bench was never chosen here, so going back to a screen the reader has not seen would be a step
        /// backwards out of the interaction rather than within it.
        /// </summary>
        private void Strip(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            float x = rect.x + Pad;

            x = Tab(rect, x, 1, bench == null ? "Bench" : bench.LabelCap, Step.Bench, bench != null, palette);
            x = Tab(rect, x, 2, selected == null ? "Recipe" : selected.Label, Step.Recipe, draft != null, palette);

            Tab(rect, x, 3, "Set up", Step.Setup, false, palette);

            if (step != Step.Bench)
                return;

            Color previous = GUI.color;
            TextAnchor anchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = GzpPalette.TextDim;

            Widgets.Label(new Rect(rect.x, rect.y, rect.width - Pad, rect.height),
                benches.Shown + (benches.Shown == 1 ? " bench" : " benches"));

            Text.Font = GameFont.Small;
            Text.Anchor = anchor;
            GUI.color = previous;
        }

        /// <summary>One tab. Returns where the next one starts.</summary>
        private float Tab(Rect rect, float x, int number, string label, Step which, bool done,
            UIColorPaletteDef palette)
        {
            bool here = step == which;
            bool reachable = done && !here && Answered(which);

            Text.Font = GameFont.Small;

            float width = Text.CalcSize(label).x + 54f;
            Rect tab = new Rect(x, rect.y, width, rect.height);

            if (here)
            {
                Widgets.DrawBoxSolid(tab, GzpPalette.BG);
                Widgets.DrawBoxSolid(new Rect(tab.x, tab.y, tab.width, 2f), GzpPalette.Accent);
            }
            else if (reachable && Mouse.IsOver(tab))
            {
                Widgets.DrawBoxSolid(tab, palette.HoverOverlay);
            }

            Color previous = GUI.color;
            TextAnchor anchor = Text.Anchor;

            Rect pip = new Rect(tab.x + 14f, tab.center.y - 8f, 16f, 16f);

            if (here)
            {
                UIElementPainter.FillRounded(pip, GzpPalette.Accent);
            }
            else if (done)
            {
                UIElementPainter.FillRounded(pip, palette.AccentMuted);
            }
            else
            {
                UIElementPainter.OutlineRounded(pip, GzpPalette.TextDim, GzpPalette.BGD);
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = here ? GzpPalette.BGD : done ? GzpPalette.Accent : GzpPalette.TextDim;

            // A tick rather than the number once a step is answered, because the number is only useful while it
            // is still a step to take.
            Widgets.Label(pip, done && !here ? "+" : number.ToString());

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = here ? GzpPalette.Accent : done ? GzpPalette.TextDim : palette.TextDisabled;

            bool wrap = Text.WordWrap;
            Text.WordWrap = false;

            Widgets.Label(new Rect(pip.xMax + 8f, tab.y, tab.width - 46f, tab.height), label);

            Text.WordWrap = wrap;
            Text.Anchor = anchor;
            GUI.color = previous;

            if (reachable && Widgets.ButtonInvisible(tab))
                Back(which);

            return tab.xMax + 2f;
        }

        /// <summary>Whether a step can be returned to at all, as opposed to merely having been passed through.</summary>
        private bool Answered(Step which)
        {
            switch (which)
            {
                // Only when this window asked the question. Opened from a bench, the bench is a fact.
                case Step.Bench: return askedForBench;
                case Step.Recipe: return bench != null;
                default: return false;
            }
        }

        /// <summary>
        /// Takes the bench for step one and moves to the recipes.
        ///
        /// <b>A click on a card is the choice; there is no Next.</b> A bench is not a decision anybody reviews, and
        /// the strip puts it one click from being undone, so a confirm step would add a control and a click to
        /// every bill anyone ever adds.
        /// </summary>
        private void Choose(Building_WorkTable table)
        {
            bench = table;
            offers = BillActions.Available(table);

            // The recipe is kept when the new bench can still make it, and cleared when it cannot. That does the
            // expected thing whether the bench is being corrected or reconsidered, and the rule never has to be
            // explained.
            if (selected != null && !Offered(selected))
                selected = null;

            if (selected == null)
                selected = offers.Count > 0 ? offers[0] : null;

            search = string.Empty;
            step = Step.Recipe;
        }

        /// <summary>Whether the current bench still offers this exact recipe and style.</summary>
        private bool Offered(RecipeOffer offer)
        {
            foreach (RecipeOffer candidate in offers)
            {
                if (candidate.Recipe == offer.Recipe && candidate.Style == offer.Style)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Goes back to an earlier step, discarding anything the later ones had built.
        ///
        /// The draft is dropped rather than kept, because it was made from a recipe that may no longer be the
        /// chosen one. Nothing is lost: it was never on a bench, and step three starts from the recipe's own
        /// defaults either way.
        /// </summary>
        private void Back(Step which)
        {
            draft = null;
            step = which;

            if (which == Step.Bench)
            {
                search = string.Empty;
                benches.Reset();
            }
        }

        /// <summary>
        /// Makes the bill and moves to the last step.
        ///
        /// The bill is real from here on, and knows its bench, but is not in the bench's stack until Add. See the
        /// note on <see cref="draft"/> for why it is not a draft object.
        /// </summary>
        private void Configure()
        {
            if (bench?.billStack == null || selected?.Recipe == null)
                return;

            UIGuard.Try("Bills.Wizard.Draft", () =>
            {
                Bill made = selected.Recipe.MakeNewBill(selected.Style);

                if (!(made is Bill_Production production))
                {
                    // A recipe whose bill this pane cannot configure goes straight on with its own defaults,
                    // since there is nothing for step three to show. It still closes, because Add closing is
                    // about finishing rather than about which screen finished it.
                    BillActions.Make(bench, selected, added);
                    Close();

                    return;
                }

                production.billStack = bench.billStack;

                draft = production;
                step = Step.Setup;
            }, "That bill could not be prepared. Nothing has been added.");
        }

        /// <summary>
        /// Puts the configured bill on the bench and closes.
        ///
        /// <b>Closing is the end of the interaction, not a return to the middle of it.</b> This used to go back to
        /// the recipes with the bench kept, on the reasoning that filling a bench is one bench choice and several
        /// recipe choices. In use that reads wrong: Add is the last thing asked for, and a window still standing
        /// afterwards looks like the bill did not take. A second bill is one more click on Add bill.
        /// </summary>
        private void Finish()
        {
            if (draft == null || bench?.billStack == null)
                return;

            UIGuard.Try("Bills.Wizard.Add", () =>
            {
                bench.billStack.AddBill(draft);

                if (draft.recipe?.conceptLearned != null)
                {
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(draft.recipe.conceptLearned,
                        KnowledgeAmount.Total);
                }

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();

                draft = null;

                // Closed before the callback, following the same rule as the bench picker: a caller that opens a
                // window of its own is then not opening it underneath this one.
                Close();

                added?.Invoke();
            }, "The bill was not added.");
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

            // No search on the last step: it configures one bill and there is nothing to look through. The
            // ingredient tree has its own search inside it.
            if (step != Step.Setup)
            {
                Rect box = new Rect(close.x - 10f - 240f, rect.y + 10f, 240f, 26f);

                GUI.color = GzpPalette.Stat;
                search = GzpPalette.FlatTextField(box, search);

                if (search.NullOrEmpty() && !Mouse.IsOver(box))
                {
                    GUI.color = GzpPalette.TextDim;

                    Widgets.Label(box.ContractedBy(7f, 2f),
                        step == Step.Bench ? "Search benches..." : "Search recipes...");
                }
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

        /// <summary>
        /// The action bar, which says something different on each step.
        ///
        /// <b>The right hand button is the only way forward, and it is never Add until the last step.</b> With
        /// three steps the button that finishes has to be on the screen that finishes, or it would create a bill
        /// carrying whatever the settings step had not been asked yet.
        /// </summary>
        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            BillStack stack = bench?.billStack;
            int count = stack?.Count ?? 0;
            bool full = stack != null && count >= BillCap.Current;

            Color previous = GUI.color;

            GUI.color = full ? GzpPalette.Bad : GzpPalette.TextDim;

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 16f, rect.width - 300f, 24f), Line(count, full));

            GUI.color = previous;

            Rect right = new Rect(rect.xMax - Pad - 150f, rect.y + 10f, 150f, 32f);

            switch (step)
            {
                case Step.Bench:
                    if (GzpPalette.GrayButton(new Rect(right.x + 40f, right.y, 110f, right.height), "Cancel"))
                        Close();

                    return;

                case Step.Recipe:
                    // Back only where the bench was chosen here. From the bench tab there is nowhere behind this.
                    if (askedForBench
                        && GzpPalette.GrayButton(new Rect(right.x - 118f, right.y, 110f, right.height), "Back"))
                        Back(Step.Bench);

                    if (GzpPalette.GrayButton(right, "Next: set up", selected != null && !full, true))
                        Configure();

                    return;

                default:
                    if (GzpPalette.GrayButton(new Rect(right.x - 118f, right.y, 110f, right.height), "Back"))
                        Back(Step.Recipe);

                    if (GzpPalette.GrayButton(right, "Add Bill", draft != null && !full, true))
                        Finish();

                    return;
            }
        }

        /// <summary>The status line, which is the only place the chosen bench is named in full on later steps.</summary>
        private string Line(int count, bool full)
        {
            if (step == Step.Bench)
                return "Choose a bench to see what it can make.";

            if (full)
                return bench.LabelCap + " already has its " + BillCap.Current + " bills.";

            if (step == Step.Setup)
                return "The bill is created when you finish. Nothing has changed yet.";

            return count + " / " + BillCap.Current + " bills on " + bench.LabelCap;
        }
    }
}
