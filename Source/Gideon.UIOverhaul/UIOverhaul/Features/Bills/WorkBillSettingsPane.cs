using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// The settings a production bill carries beyond its recipe and its count: what it may consume, how far to
    /// look for it, where the products go, who may work it, and how good they have to be.
    ///
    /// <b>Where the products go arrived last, on 2026-08-22.</b> It was the one control RimWorld's own dialog had
    /// that this pane did not, which meant a player who moved to our window lost the setting that decides whether
    /// a hundred meals pile up on the kitchen floor. See <see cref="BillStoreMode"/>.
    ///
    /// <b>One pane, two hosts.</b> It opens from a bill row as <see cref="Dialog_WorkBillSettings"/> and it is the
    /// last step of <see cref="Dialog_AddWorkBill"/>. Extracted rather than written twice, because the two would
    /// have drifted the first time either was adjusted, and the drift would show as a control that exists in one
    /// place and not the other.
    ///
    /// <b>It always writes straight through to the bill it is given, and there is deliberately no draft mode.</b>
    /// That was the risk when the wizard was designed: a step that holds answers and commits them at the end is a
    /// second behaviour for every control, and the failure would be a setting that silently does nothing in one of
    /// the two places. The wizard avoids it by handing over a real <c>Bill_Production</c> that is simply not on a
    /// bench yet, so this pane cannot tell the difference and does not have to.
    ///
    /// <b>A bill given to this pane must know its bench.</b> The worker menu reaches through
    /// <c>bill.billStack.billGiver</c> to find the work giver, and the ingredient tree wants a map. A bill the
    /// wizard has not added yet still has its <c>billStack</c> set for exactly that reason; see the note on
    /// <see cref="Dialog_AddWorkBill"/>.
    ///
    /// <b>Instance rather than static, because the typing boxes are stateful.</b> Each host keeps one pane, so the
    /// boxes survive between frames and refill themselves when the bill changes.
    /// </summary>
    internal sealed class WorkBillSettingsPane
    {
        private const float Pad = 12f;

        private readonly BillNumberBox radiusBox = new BillNumberBox();
        private readonly BillNumberBox skillLowBox = new BillNumberBox();
        private readonly BillNumberBox skillHighBox = new BillNumberBox();
        private readonly BillNumberBox repeatCountBox = new BillNumberBox();
        private readonly BillNumberBox targetCountBox = new BillNumberBox();
        private readonly BillNumberBox unpauseBox = new BillNumberBox();

        /// <summary>
        /// The ingredient tree's own scroll position and search, which RimWorld keeps outside the filter.
        ///
        /// One per pane rather than one per bill: a pane shows a single bill at a time, and a state per bill would
        /// accumulate one for every bill ever opened.
        /// </summary>
        private readonly ThingFilterUI.UIState filterState = new ThingFilterUI.UIState();

        /// <summary>How wide the settings column is. The ingredient tree takes what is left.</summary>
        internal const float SettingsWidth = 380f;

        /// <summary>
        /// Draws both columns into one rectangle.
        ///
        /// <b>The repeat controls used to be a sentence, and only in one of the two hosts.</b> The wizard showed
        /// nothing at all, on the reasoning that a read-only line describing untouched defaults tells a player
        /// less than no line does -- which was right about the line and wrong about what should be there instead.
        /// A bill created through the wizard came out running once and had to be reopened to say otherwise.
        /// Reported on 2026-08-25; the section is now editable and drawn in both hosts.
        /// </summary>
        internal void Draw(Rect body, Bill_Production bill, UIColorPaletteDef palette)
        {
            if (bill == null)
                return;

            Rect settings = new Rect(body.x, body.y, SettingsWidth, body.height);
            Rect ingredients = new Rect(settings.xMax + 8f, body.y, body.width - SettingsWidth - 8f, body.height);

            Settings(settings, bill, palette);
            Ingredients(ingredients, bill, palette);
        }

        /// <summary>Where the last draw of the settings column ended, which is what the next one scrolls.</summary>
        private float settingsHeight = 420f;

        private Vector2 settingsScroll;

        /// <summary>
        /// The settings column.
        ///
        /// <b>Scrolled, since the output section arrived.</b> Aaron's screenshot of 2026-08-22 showed the skill
        /// line cut in half by the bottom of the column: five sections no longer fit a fixed height panel on every
        /// window size, and the one that fell off the end was the last one drawn rather than the least important.
        ///
        /// The height is measured rather than predicted, for the reason the species pane's is: what fits depends
        /// on which sections this bill even has, and a formula that has to be updated whenever a section is added
        /// is a formula that will eventually be wrong by exactly one section.
        /// </summary>
        private void Settings(Rect rect, Bill_Production bill, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BG);

            Rect outer = rect.ContractedBy(Pad);
            Rect view = new Rect(0f, 0f, outer.width - (settingsHeight > outer.height ? 18f : 0f),
                settingsHeight);

            Widgets.BeginScrollView(outer, ref settingsScroll, view);

            Rect inner = new Rect(0f, 0f, view.width, view.height);
            float y = inner.y;

            // No Recipe line: the host's own title is the bill's label, which is the recipe's name plus whatever
            // it was renamed to, so a Recipe row underneath was the same words truncated to half the column.
            Heading(inner, ref y, "REPEAT");

            Repeat(inner, ref y, bill, palette);

            Heading(inner, ref y, "REACH");

            int radius = Mathf.Clamp(Mathf.RoundToInt(bill.ingredientSearchRadius), 3, 999);
            int wanted = radiusBox.Draw(new Rect(inner.x, y, inner.width, 26f), palette, "Radius", bill, radius, 3,
                999);

            if (wanted != radius)
                bill.ingredientSearchRadius = wanted;

            // No sentence under the number saying what the number means. It read "Ingredients further than 30
            // tiles away are ignored", which is the word "Radius" and the figure 30 spelled out again, and it
            // cost two lines of a pane that has to hold the whole bill. Removed 2026-08-23 on Aaron's
            // instruction. The one thing the line said that the box does not is that 999 reaches the whole map,
            // and that belongs on the box, not under it.
            y += 40f;

            Heading(inner, ref y, "OUTPUT");

            float output = BillStoreMode.HeightFor(bill);

            BillStoreMode.Draw(new Rect(inner.x, y, inner.width, output), bill, palette);

            y += output + 10f;

            Heading(inner, ref y, "WORKER");

            if (UIActionButtonControl.Draw(new Rect(inner.x, y, inner.width, 28f), BillActions.WorkerLabel(bill)))
                BillActions.ChooseWorker(bill, null);

            y += 34f;

            Skill(inner, ref y, bill, palette);

            Widgets.EndScrollView();

            settingsHeight = y + 4f;
        }

        /// <summary>
        /// How many times the bill runs, and when it stops.
        ///
        /// <b>Three segments rather than a dropdown,</b> matching the colony window's editor. There are exactly
        /// three modes, they never grow, and which one is on changes what the rest of the section is -- a control
        /// whose choice reshapes the panel beneath it should not hide two thirds of itself behind a click.
        ///
        /// <b>Two count boxes, not one box under two names.</b> Do X times counts down as the bill runs and Until
        /// you have counts what the colony holds: different fields, different meanings, and only ever one of them
        /// on screen. Sharing a box saved nothing and let text typed into one be written to the other, because a
        /// box refuses to refill itself while it holds focus and clicking a mode segment does not take focus away.
        /// </summary>
        private void Repeat(Rect inner, ref float y, Bill_Production bill, UIColorPaletteDef palette)
        {
            float third = Mathf.Floor(inner.width / 3f);

            Mode(new Rect(inner.x, y, third, 26f), bill, BillRepeatModeDefOf.RepeatCount, "Do X times", palette);
            Mode(new Rect(inner.x + third, y, third, 26f), bill, BillRepeatModeDefOf.TargetCount, "Until you have",
                palette);
            Mode(new Rect(inner.x + third * 2f, y, inner.width - third * 2f, 26f), bill,
                BillRepeatModeDefOf.Forever, "Forever", palette);

            y += 32f;

            // Forever has nothing to count to, so the box goes rather than being drawn disabled: there is no
            // number a player could be about to set, which is what a grayed control implies.
            if (bill.repeatMode == BillRepeatModeDefOf.Forever)
            {
                y += 8f;

                return;
            }

            bool target = bill.repeatMode == BillRepeatModeDefOf.TargetCount;
            int value = target ? bill.targetCount : bill.repeatCount;

            Rect box = new Rect(inner.x, y, inner.width, 26f);

            int changed = target
                ? targetCountBox.Draw(box, palette, "Target", bill, value, 1, 99999)
                : repeatCountBox.Draw(box, palette, "Count", bill, value, 1, 99999);

            if (changed != value)
            {
                if (target)
                    bill.targetCount = changed;
                else
                    bill.repeatCount = changed;
            }

            y += 32f;

            if (!target)
            {
                y += 8f;

                return;
            }

            // <b>Pausing belongs to Until you have and to nothing else.</b> A bill told to run four times cannot
            // be satisfied -- it finishes -- so the pair below would be two controls that never come into effect.
            // This is RimWorld's own rule and it is worth keeping: a setting that silently does nothing is worse
            // than a setting that is not offered.
            bool pause = bill.pauseWhenSatisfied;

            if (UICheckboxControl.Draw(new Rect(inner.x, y, inner.width, 24f), ref pause, palette,
                    "Pause when satisfied",
                    "Stop working once the colony holds the target, instead of finishing the bill, and start again "
                    + "when the count falls back to the number below."))
            {
                bill.pauseWhenSatisfied = pause;
            }

            y += 28f;

            if (!pause)
            {
                y += 8f;

                return;
            }

            // One below the target at the most. Resuming at the target itself would satisfy the bill on the same
            // tick it unpaused, which is a bill that never stops and never visibly runs.
            int ceiling = Mathf.Max(0, bill.targetCount - 1);
            int resume = Mathf.Clamp(bill.unpauseWhenYouHave, 0, ceiling);

            int wanted = unpauseBox.Draw(new Rect(inner.x, y, inner.width, 26f), palette, "Resume below", bill,
                resume, 0, ceiling);

            if (wanted != bill.unpauseWhenYouHave)
                bill.unpauseWhenYouHave = wanted;

            y += 40f;
        }

        /// <summary>
        /// One segment of a radio row: the mod's button, with the selected one toggled on.
        ///
        /// <b>Hand drawn until 2026-08-25, and it showed.</b> It painted its own outline and never looked at the
        /// mouse, so a row of these sat in a window full of buttons that all lit up when the pointer crossed them
        /// and stayed dark. The control has a toggled state for exactly this -- accent border, accent text, muted
        /// accent fill -- so the look is unchanged and the behaviour is now everyone else's.
        /// </summary>
        private static void Mode(Rect rect, Bill_Production bill, BillRepeatModeDef mode, string label,
            UIColorPaletteDef palette)
        {
            bool on = bill.repeatMode == mode;

            if (UIActionButtonControl.Draw(rect, label, palette, false, true, GameFont.Tiny, null, on) && !on)
                bill.repeatMode = mode;
        }

        /// <summary>
        /// The skill range, or nothing where it cannot apply.
        ///
        /// Hidden exactly where RimWorld hides it: a bill given to one named pawn is already answered, a recipe
        /// with no work skill has nothing to measure, and a mech has no skills. A control the game will ignore is
        /// worse than no control, because the player sets it and then watches it do nothing.
        /// </summary>
        private void Skill(Rect inner, ref float y, Bill_Production bill, UIColorPaletteDef palette)
        {
            if (bill.PawnRestriction != null || bill.recipe?.workSkill == null || bill.MechsOnly)
                return;

            IntRange range = bill.allowedSkillRange;

            int low = skillLowBox.Draw(new Rect(inner.x, y, inner.width, 26f), palette, "Min skill", bill,
                range.min, 0, 20);

            int high = skillHighBox.Draw(new Rect(inner.x, y + 30f, inner.width, 26f), palette, "Max skill", bill,
                range.max, 0, 20);

            // Clamped against each other rather than independently, so pushing one past the other carries it
            // along instead of producing a range nobody can satisfy.
            if (low != range.min)
                high = Mathf.Max(high, low);
            else if (high != range.max)
                low = Mathf.Min(low, high);

            if (low != range.min || high != range.max)
                bill.allowedSkillRange = new IntRange(Mathf.Min(low, high), Mathf.Max(low, high));

            // "Cooking between 0 and 20" was the two boxes above it read back in a sentence, and at the pane's
            // width it truncated as well. Removed 2026-08-23 on Aaron's instruction. The skill the range applies
            // to is the only part the boxes do not say, and Min skill sitting under a WORKER heading in a recipe's
            // own editor is not ambiguous about which skill it means.
            y += 66f;
        }

        /// <summary>
        /// The ingredient tree, drawn by RimWorld's own panel so this mod's reskin of it applies.
        ///
        /// Routing through <c>ThingFilterUI</c> means the tree here, the tree in the colony window and the tree in
        /// every storage building are one implementation. A second tree would be a second thing to keep correct as
        /// categories, special filters and hit point ranges change between versions.
        /// </summary>
        private void Ingredients(Rect rect, Bill_Production bill, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BG);

            Rect inner = rect.ContractedBy(Pad);
            RecipeDef recipe = bill.recipe;

            if (recipe == null)
                return;

            if (!Choosable(recipe))
            {
                Color previous = GUI.color;
                GUI.color = GzpPalette.TextDim;

                Widgets.Label(new Rect(inner.x, inner.y, inner.width, 40f),
                    "This recipe uses fixed ingredients, so there is nothing to choose.");

                GUI.color = previous;

                return;
            }

            ThingFilterUI.DoThingFilterConfigWindow(inner, filterState, bill.ingredientFilter,
                recipe.fixedIngredientFilter, 4, null, Hidden(recipe), false, false, false,
                recipe.GetPremultipliedSmallIngredients(), bill.Map);
        }

        /// <summary>Whether any of the recipe's ingredients leave the player a choice.</summary>
        private static bool Choosable(RecipeDef recipe)
        {
            List<IngredientCount> ingredients = recipe.ingredients;

            if (ingredients == null)
                return false;

            foreach (IngredientCount ingredient in ingredients)
            {
                if (!ingredient.IsFixedIngredient)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The special filters this recipe's tree must not offer.
        ///
        /// The four diet toggles are hidden when Ideology is active, which reads backwards until you see why: with
        /// Ideology installed, whether a colony eats meat or people is a precept rather than a per bill switch, so
        /// the toggles would be four controls that argue with the ideoligion. This is RimWorld's own rule,
        /// reproduced because the list it keeps is private to its bill dialog.
        /// </summary>
        private static IEnumerable<SpecialThingFilterDef> Hidden(RecipeDef recipe)
        {
            if (ModsConfig.IdeologyActive)
            {
                yield return SpecialThingFilterDefOf.AllowCarnivore;
                yield return SpecialThingFilterDefOf.AllowVegetarian;
                yield return SpecialThingFilterDefOf.AllowCannibal;
                yield return SpecialThingFilterDefOf.AllowInsectMeat;
            }

            List<SpecialThingFilterDef> forced = recipe.forceHiddenSpecialFilters;

            if (forced == null)
                yield break;

            foreach (SpecialThingFilterDef filter in forced)
                yield return filter;
        }

        private static void Heading(Rect inner, ref float y, string title)
        {
            Color previous = GUI.color;

            Text.Font = GameFont.Tiny;
            GUI.color = GzpPalette.TextDim;

            Widgets.Label(new Rect(inner.x, y, inner.width, 18f), title);

            Text.Font = GameFont.Small;
            GUI.color = previous;

            Widgets.DrawBoxSolid(new Rect(inner.x, y + 18f, inner.width, 1f), GzpPalette.BGD);

            y += 24f;
        }
    }
}
