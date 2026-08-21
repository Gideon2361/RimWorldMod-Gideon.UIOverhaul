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
    /// Everything about one production bill that will not fit on its row: what it may consume, how far it may look
    /// for it, who may work it and how good they have to be.
    ///
    /// <b>This window exists because a production bill is not a growing bill.</b> The growing zone row says
    /// everything there is to say about a growing bill, so that feature needs no equivalent. Replacing the
    /// workbench tab with the same card list would otherwise have quietly dropped four settings vanilla put on
    /// the bench, and sending the player to the colony wide tab for them is exactly what Aaron asked not to
    /// happen. So they moved here rather than being lost.
    ///
    /// <b>Same chrome as the picker,</b> down to the header, the footer and the close button, because it opens
    /// from the same list and a second visual language between them would read as a different mod.
    ///
    /// <b>It edits the bill in place and has no cancel.</b> That is how every other bill control in the game
    /// behaves, including vanilla's own dialog: there is no draft of a bill to commit or discard, and offering a
    /// cancel button would promise an undo that nothing behind it implements.
    /// </summary>
    public class Dialog_WorkBillSettings : Window
    {
        private const float HeaderHeight = 46f;
        private const float FooterHeight = 52f;
        private const float Pad = 12f;
        private const float EdgeInset = 8f;
        private const float Gutter = 8f;

        /// <summary>
        /// The settings column.
        ///
        /// Widened from 320 on Aaron's report, 2026-08-19: the sentence under the radius wrapped to a second line
        /// that the fixed gap below it did not account for, so it collided with the WORKER heading. The gap is now
        /// measured rather than assumed, and the column is wide enough that the common case is one line anyway.
        /// </summary>
        private const float SettingsWidth = 380f;

        private readonly Bill_Production bill;

        private readonly BillNumberBox radiusBox = new BillNumberBox();
        private readonly BillNumberBox skillLowBox = new BillNumberBox();
        private readonly BillNumberBox skillHighBox = new BillNumberBox();

        /// <summary>The ingredient tree's own scroll position and search, which RimWorld keeps outside the filter.</summary>
        private readonly ThingFilterUI.UIState filterState = new ThingFilterUI.UIState();

        public Dialog_WorkBillSettings(Bill_Production subject)
        {
            bill = subject;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(920f, 620f);

        protected override float Margin => 0f;

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + HeaderHeight);

            UIGuardedPanel.Draw("Bills.BenchSettings", inRect, () => Contents(inRect),
                "The bill settings window shows a failure notice. The bill itself is unchanged.");
        }

        private void Contents(Rect inRect)
        {
            if (bill == null)
            {
                Close();

                return;
            }

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Widgets.DrawBoxSolid(inRect, GzpPalette.BGD);
            Text.Font = GameFont.Small;

            Header(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight));

            Rect body = new Rect(inRect.x, inRect.y + HeaderHeight, inRect.width,
                inRect.height - HeaderHeight - FooterHeight).ContractedBy(EdgeInset);

            Rect settings = new Rect(body.x, body.y, SettingsWidth, body.height);
            Rect ingredients = new Rect(settings.xMax + Gutter, body.y, body.width - SettingsWidth - Gutter,
                body.height);

            Settings(settings, palette);
            Ingredients(ingredients, palette);

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

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 8f, rect.width - 80f, 30f), bill.LabelCap);

            Text.Font = GameFont.Small;
            GUI.color = previous;

            if (GzpPalette.IconButton(new Rect(rect.xMax - Pad - 24f, rect.y + 11f, 24f, 24f), GzpTex.Close,
                    "Close"))
                Close();
        }

        /// <summary>Who may work it, and how far it may reach for what it needs.</summary>
        private void Settings(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BG);

            Rect inner = rect.ContractedBy(Pad);
            float y = inner.y;

            // No Recipe line: the window's own title is the bill's label, which is the recipe's name plus
            // whatever the player renamed it to, so a Recipe row underneath was the same words truncated to half
            // the column.
            GzpPalette.InfoLine(ref y, inner.x, inner.width, "Repeats", bill.RepeatInfoText);

            y += 10f;

            Heading(inner, ref y, "REACH");

            int radius = Mathf.Clamp(Mathf.RoundToInt(bill.ingredientSearchRadius), 3, 999);
            int wanted = radiusBox.Draw(new Rect(inner.x, y, inner.width, 26f), palette, "Radius", bill, radius, 3,
                999);

            if (wanted != radius)
                bill.ingredientSearchRadius = wanted;

            y += 30f;

            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;

            string note = radius >= 999
                ? "The whole map. A crafter may walk to the far corner for one item."
                : "Ingredients further than " + radius + " tiles away are ignored.";

            // Measured rather than assumed. This was a fixed 32 with a 36 advance, and the two line case needed
            // more than that, so the second line landed on the WORKER heading below it. Asking for the height is
            // the only version of this that cannot be wrong at some string length.
            float noteHeight = Text.CalcHeight(note, inner.width);

            Widgets.Label(new Rect(inner.x, y, inner.width, noteHeight), note);

            GUI.color = previous;

            y += noteHeight + 10f;

            Heading(inner, ref y, "WORKER");

            if (GzpPalette.GrayButton(new Rect(inner.x, y, inner.width, 28f), BillActions.WorkerLabel(bill)))
                BillActions.ChooseWorker(bill, null);

            y += 34f;

            Skill(inner, ref y, palette);
        }

        /// <summary>
        /// The skill range, or nothing where it cannot apply.
        ///
        /// Hidden exactly where RimWorld hides it: a bill given to one named pawn is already answered, a recipe
        /// with no work skill has nothing to measure, and a mech has no skills. A control the game will ignore is
        /// worse than no control, because the player sets it and then watches it do nothing.
        /// </summary>
        private void Skill(Rect inner, ref float y, UIColorPaletteDef palette)
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

            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;

            Widgets.Label(new Rect(inner.x, y + 62f, inner.width, 18f),
                bill.recipe.workSkill.LabelCap + " between " + bill.allowedSkillRange.min + " and "
                + bill.allowedSkillRange.max);

            GUI.color = previous;

            y += 84f;
        }

        /// <summary>
        /// The ingredient tree, drawn by RimWorld's own panel so this mod's reskin of it applies.
        ///
        /// Routing through <c>ThingFilterUI</c> means the tree here, the tree in the colony window and the tree in
        /// every storage building are one implementation. A second tree would be a second thing to keep correct as
        /// categories, special filters and hit point ranges change between versions.
        /// </summary>
        private void Ingredients(Rect rect, UIColorPaletteDef palette)
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

        private void Heading(Rect inner, ref float y, string title)
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

        private void Footer(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 16f, rect.width - 200f, 24f),
                "Changes apply as you make them.");

            GUI.color = previous;

            if (GzpPalette.GrayButton(new Rect(rect.xMax - Pad - 120f, rect.y + 10f, 120f, 32f), "Done", true, true))
                Close();
        }
    }
}
