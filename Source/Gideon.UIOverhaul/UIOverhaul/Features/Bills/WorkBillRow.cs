using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Draws a <see cref="Bill_Production"/> as a card, in place of vanilla's <c>Bill.DoInterface</c>.
    ///
    /// <b>This is the growing zone bill row applied to a workbench,</b> which is what Aaron asked for on
    /// 2026-08-19: the same card, the same accent stripe carrying state, the same four icon buttons, the same
    /// mode button and target group. A player who has learned one has learned the other.
    ///
    /// <b>It draws through <c>GzpPalette</c> on purpose,</b> even though that name says growing zones. Every
    /// colour in there resolves to the active <c>UIColorPaletteDef</c> and the drawing helpers are the ones that
    /// give the grow zone card its look, so reusing them is the only way "same design" can stay true as the
    /// palette changes. Cloning them would produce two card styles that agree today and drift by the next
    /// palette edit. The name is the thing that is wrong, not the sharing.
    ///
    /// <b>The one addition is a fifth button.</b> A growing bill is fully described by a plant and a count, and a
    /// production bill is not: it also carries an ingredient filter, a search radius, a worker restriction and a
    /// skill range, none of which fit on a 78 pixel row. Those live behind the settings button, in a window of
    /// their own, rather than being dropped or sent to the colony tab.
    /// </summary>
    internal static class WorkBillRow
    {
        internal const float RowHeight = 78f;

        private const float IconSize = 40f;
        private const float ButtonSize = 20f;
        private const float Pad = 10f;
        private const float TargetFieldWidth = 74f;

        /// <summary>Card chrome shared by every row. See the note in <see cref="Draw"/>.</summary>
        private static readonly UICardControl RowCard = new UICardControl();

        /// <summary>
        /// Draws one row.
        ///
        /// <paramref name="target"/> is the typing box for this row's position in the list, owned by the tab. A
        /// number you can type needs a control that survives between frames, and one per bill would mean one per
        /// bill the player has ever scrolled past.
        ///
        /// <paramref name="changed"/> runs when this row alters the stack, so the tab can re-read it rather than
        /// keep drawing a list that no longer matches.
        /// </summary>
        internal static void Draw(Rect rect, Bill_Production bill, Building_WorkTable bench, int index, int total,
            BillNumberBox target, System.Action changed)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            bool forever = bill.repeatMode == BillRepeatModeDefOf.Forever;

            // DrawChrome, never Draw. Draw ends with a ButtonInvisible over the whole card and GUI.Button
            // consumes the click on mouse-down, so the card would take the event and every control drawn
            // afterwards would see EventType.Used. This row is nothing but controls.
            RowCard.Padding = 0f;
            RowCard.AccentColor = StateColor(bill, palette);
            RowCard.BackgroundColor = GzpPalette.PanelBG;
            RowCard.DrawChrome(rect);

            Color previous = GUI.color;
            Text.Font = GameFont.Small;

            Rect icon = new Rect(rect.x + Pad, rect.y + Pad, IconSize, IconSize);
            ThingDef shown = bill.recipe?.UIIconThing;

            // The recipe's own icon thing rather than what it produces. A recipe may name one explicitly, and the
            // ones that do are the ones that need it: surgery, disassembly, anything whose product is nothing or
            // is a corpse. A recipe with neither is left blank rather than given a placeholder, since a question
            // mark in a row reads as something being wrong with the bill.
            if (shown != null)
                Widgets.DefIcon(icon, shown);

            float contentX = icon.xMax + Pad;
            float buttonsWidth = ButtonSize * 5f + 8f;

            GUI.color = bill.suspended || bill.paused ? GzpPalette.TextDim : GzpPalette.Stat;

            Widgets.Label(new Rect(contentX, rect.y + 6f, rect.width - contentX - buttonsWidth - Pad, 24f),
                bill.LabelCap);

            GUI.color = previous;

            Buttons(rect, bill, bench, index, total, changed);
            ModeAndTarget(rect, bill, palette, contentX, forever, target);

            if (!forever)
                Progress(rect, bill, contentX);

            Badge(rect, bill);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// The stripe colour, which is what the list conveys at a glance.
        ///
        /// Suspended is a warning rather than a danger: the player turned it off deliberately, so it is a state to
        /// notice, not a fault. Paused is read as satisfied, because that is what pauses a production bill, and a
        /// bill that has made everything it was asked for is the good outcome rather than a stalled one.
        /// </summary>
        private static Color StateColor(Bill_Production bill, UIColorPaletteDef palette)
        {
            if (bill.suspended)
                return palette.Warning;

            if (bill.paused)
                return palette.Success;

            return palette.Accent;
        }

        private static void Buttons(Rect rect, Bill_Production bill, Building_WorkTable bench, int index, int total,
            System.Action changed)
        {
            BillStack stack = bench?.billStack;
            float x = rect.xMax - Pad - ButtonSize;
            float y = rect.y + 6f;

            if (GzpPalette.IconButton(new Rect(x, y, ButtonSize, ButtonSize), BillGlyphs.Trash, "Delete bill",
                    GzpPalette.Bad))
            {
                stack?.Delete(bill);
                SoundDefOf.Click.PlayOneShotOnCamera();
                changed?.Invoke();

                return;
            }

            x -= ButtonSize + 2f;

            if (GzpPalette.IconButton(new Rect(x, y, ButtonSize, ButtonSize), BillGlyphs.Pause,
                    bill.suspended ? "Resume bill" : "Suspend bill",
                    bill.suspended ? GzpPalette.Warn : GzpPalette.TextDim))
            {
                bill.suspended = !bill.suspended;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            x -= ButtonSize + 2f;

            // Reorder guards its lower bound only, and moving the last bill down would insert past the end of the
            // list, so both directions are bounded here rather than trusted.
            if (index < total - 1 && stack != null
                                 && GzpPalette.IconButton(new Rect(x, y, ButtonSize, ButtonSize),
                                     TexButton.ReorderDown, "Lower priority"))
            {
                stack.Reorder(bill, 1);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                changed?.Invoke();

                return;
            }

            x -= ButtonSize + 2f;

            if (index > 0 && stack != null
                          && GzpPalette.IconButton(new Rect(x, y, ButtonSize, ButtonSize), TexButton.ReorderUp,
                              "Raise priority"))
            {
                stack.Reorder(bill, -1);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                changed?.Invoke();

                return;
            }

            x -= ButtonSize + 2f;

            if (GzpPalette.IconButton(new Rect(x, y, ButtonSize, ButtonSize), TexButton.OpenStatsReport,
                    "Ingredients, radius, worker and skill"))
            {
                Find.WindowStack.Add(new Dialog_WorkBillSettings(bill));
            }
        }

        /// <summary>
        /// The repeat mode and the number that goes with it.
        ///
        /// <b>Forever gets a sentence rather than an empty space,</b> so the row reads as deliberate instead of as
        /// a control that failed to draw.
        /// </summary>
        private static void ModeAndTarget(Rect rect, Bill_Production bill, UIColorPaletteDef palette, float contentX,
            bool forever, BillNumberBox target)
        {
            float lineY = rect.y + 32f;
            Rect mode = new Rect(contentX, lineY, 130f, 24f);

            if (UIActionButtonControl.Draw(mode, ModeLabel(bill)))
                Find.WindowStack.Add(new FloatMenu(ModeOptions(bill)));

            Color previous = GUI.color;

            if (forever)
            {
                GUI.color = GzpPalette.TextDim;

                Widgets.Label(new Rect(mode.xMax + 8f, lineY + 2f, rect.width - mode.xMax - 20f, 22f),
                    "Always making");

                GUI.color = previous;

                return;
            }

            bool counted = bill.repeatMode == BillRepeatModeDefOf.TargetCount;
            int value = counted ? bill.targetCount : bill.repeatCount;

            // Right aligned group, sized so the label-less box's own minus and plus land where the growing row
            // puts them.
            Rect box = new Rect(rect.xMax - Pad - TargetFieldWidth - ButtonSize * 2f - 4f, lineY + 1f,
                TargetFieldWidth + ButtonSize * 2f + 4f, 24f);

            int typed = target.Draw(box, palette, null, bill, value, counted ? 0 : 1, 99999);

            if (typed != value)
            {
                if (counted)
                    bill.targetCount = typed;
                else
                    bill.repeatCount = typed;
            }

            // <b>What the colony already has, not a repeat of the button.</b> This said "until you have" beside a
            // button that said "Until you have", which was redundant, and in a rect too narrow for it the two
            // words wrapped across each other into an unreadable smear. The count is the figure the target is
            // actually being compared against, and it is the one thing on this row that the mode button cannot
            // tell you.
            //
            // Nothing at all in Do X times mode: the remaining count is already the number in the box, so there
            // would be nothing left to say.
            if (!counted)
                return;

            int held = Held(bill);

            GUI.color = held >= bill.targetCount ? GzpPalette.Warn : GzpPalette.TextDim;

            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;

            Text.Anchor = TextAnchor.MiddleRight;

            // Forced off like every other single line label in this mod. A number that does not fit should be
            // shortened by the layout, never folded into a second line the row has no height for.
            Text.WordWrap = false;

            Widgets.Label(new Rect(mode.xMax + 8f, lineY + 2f, box.x - mode.xMax - 16f, 22f), held + "  /");

            Text.WordWrap = wrap;
            Text.Anchor = anchor;
            GUI.color = previous;
        }

        private static string ModeLabel(Bill_Production bill)
        {
            if (bill.repeatMode == BillRepeatModeDefOf.Forever)
                return "Forever";

            return bill.repeatMode == BillRepeatModeDefOf.TargetCount ? "Until you have" : "Do X times";
        }

        private static List<FloatMenuOption> ModeOptions(Bill_Production bill)
        {
            return new List<FloatMenuOption>
            {
                new FloatMenuOption("Do X times", () => bill.repeatMode = BillRepeatModeDefOf.RepeatCount),
                new FloatMenuOption("Until you have", () => bill.repeatMode = BillRepeatModeDefOf.TargetCount),
                new FloatMenuOption("Forever", () => bill.repeatMode = BillRepeatModeDefOf.Forever)
            };
        }

        /// <summary>
        /// A bar along the bottom showing how far along the bill is.
        ///
        /// <b>Two different meanings, because the two modes count different things.</b> Do X times counts what is
        /// left to make, which the bill tracks itself. Until you have counts what the colony holds, and that
        /// figure belongs to the map rather than the bill, so it is asked of the recipe's own worker counter,
        /// which is what the game uses to decide whether the bill should run at all.
        /// </summary>
        private static void Progress(Rect rect, Bill_Production bill, float contentX)
        {
            float fill;

            if (bill.repeatMode == BillRepeatModeDefOf.TargetCount)
            {
                int held = Held(bill);

                fill = bill.targetCount <= 0 ? 1f : held / (float)bill.targetCount;
            }
            else
            {
                // repeatCount counts down as the bill runs, so there is no total to divide by. A full bar for a
                // bill with work left would be a lie, so the bar shows how much is left rather than how much is
                // done, which needs no total.
                fill = bill.repeatCount <= 0 ? 1f : Mathf.Clamp01(bill.repeatCount / 100f);
            }

            Rect bar = new Rect(contentX, rect.yMax - 14f, rect.width - contentX - Pad, 5f);

            GzpPalette.Bar(bar, Mathf.Clamp01(fill), fill >= 1f ? GzpPalette.Warn : GzpPalette.Accent);
        }

        /// <summary>
        /// How many of the bill's product the colony already has.
        ///
        /// Asked of the recipe's own worker counter, guarded and defaulting to zero: it reaches the map's
        /// resource counter and a bill not yet placed on a bench has no map to reach.
        /// </summary>
        private static int Held(Bill_Production bill)
        {
            return Gideon.UIFramework.Helpers.UIGuard.Try("Bills.RowCount", () =>
            {
                RecipeWorkerCounter counter = bill.recipe?.WorkerCounter;

                // Asked before counting, as vanilla asks: a recipe whose product cannot be counted returns a
                // meaningless figure rather than refusing, and a progress bar built on that would be worse than
                // no bar at all.
                return counter == null || !counter.CanCountProducts(bill) ? 0 : counter.CountProducts(bill);
            }, 0, null);
        }

        /// <summary>
        /// A scrim and a word across a bill that is not going to run right now.
        ///
        /// Loud on purpose. A suspended bill looks exactly like a working one from a distance, and a player
        /// wondering why nothing is being made needs to see the answer from the list rather than by opening each
        /// row.
        /// </summary>
        private static void Badge(Rect rect, Bill_Production bill)
        {
            if (!bill.suspended && !bill.paused)
                return;

            Widgets.DrawBoxSolid(rect, GzpPalette.DimScrim);

            string label = bill.suspended ? "SUSPENDED" : "TARGET MET";
            Color color = bill.suspended ? GzpPalette.Bad : GzpPalette.Warn;

            Vector2 size = Text.CalcSize(label);
            Rect badge = new Rect(rect.center.x - size.x / 2f - 10f, rect.center.y - 11f, size.x + 20f, 22f);

            Widgets.DrawBoxSolid(badge, GzpPalette.BGD);

            Color previous = GUI.color;
            TextAnchor anchor = Text.Anchor;

            GUI.color = color;
            Text.Anchor = TextAnchor.MiddleCenter;

            Widgets.Label(badge, label);

            Text.Anchor = anchor;
            GUI.color = previous;
        }
    }
}
