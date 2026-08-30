using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Orders
{
    /// <summary>
    /// The half dozen orders you give constantly, in the corner that is empty when nothing is selected.
    ///
    /// <b>Claim, deconstruct, mine, mine vein, allow and forbid are all four clicks deep.</b> Every one of them
    /// lives in Architect, then Orders, then a grid of twenty-odd tiles -- and every one of them is something a
    /// player does dozens of times an hour without ever wanting to think about it. Asked for on 2026-08-29.
    ///
    /// <b>Only when nothing is selected,</b> because that is the only time the bottom left is free. Select
    /// anything and the inspect pane takes the corner, which is the right thing for it to do: what you are
    /// looking at beats what you might want to do next.
    ///
    /// <b>These are vanilla's own designators, fetched rather than reimplemented.</b> Each button hands the real
    /// <c>Designator</c> to <c>DesignatorManager.Select</c>, so the drag behaviour, the cell validation, the
    /// disabled reasons and the sounds are all the game's -- and a mod that patches one of them changes this
    /// too. Nothing here knows what mining is.
    /// </summary>
    internal static class QuickOrders
    {
        /// <summary>
        /// The orders offered, in the order they are drawn.
        ///
        /// <b>Six, and the list is deliberately closed.</b> The value of this strip is that the same order is in
        /// the same place every time, which a list that grew with the situation would lose. Anything not here is
        /// still one Architect away.
        ///
        /// <c>Designator_Unforbid</c> is the one whose name reads backwards: it is the Allow button.
        /// </summary>
        private static readonly Type[] Wanted =
        {
            typeof(Designator_Claim),
            typeof(Designator_Deconstruct),
            typeof(Designator_Mine),
            typeof(Designator_MineVein),
            typeof(Designator_Unforbid),
            typeof(Designator_Forbid)
        };

        /// <summary>
        /// Half the size of a command button, and expressed that way rather than as a number.
        ///
        /// <b>These are not commands and should not compete with them.</b> A gizmo is 75 square and appears
        /// because you selected something -- it is the answer to a question you just asked. These six are always
        /// there and answer no question, so at the same size they would read as the loudest thing on the screen
        /// while being the least urgent. Half is small enough to ignore and large enough to hit.
        ///
        /// Taken from <c>Gizmo.Height</c> so the relationship survives RimWorld changing the number. Asked for
        /// on 2026-08-29, and only for these -- the contextual commands are untouched.
        /// </summary>
        private const float ButtonSize = Gizmo.Height / 2f;

        private const float Gap = 4f;

        /// <summary>
        /// Clear space kept for the mouseover readout, which shares this corner and is drawn by the game.
        ///
        /// <b>Beside it rather than above it, because the readout's height is not knowable.</b> It stacks terrain,
        /// glow, roof, temperature and whatever is under the cursor upward from the bottom, so a strip placed
        /// above it would sit at a different height every time the cursor moved. Placed to its right, this never
        /// moves.
        ///
        /// <b>The width is a reservation, not a measurement, and it was 250 until it was not enough.</b> The
        /// readout draws each line into a rect 999 wide and lets it run, so nothing clips it -- these buttons are
        /// simply painted over the end of it, this being a postfix on the method that drew it. "Gravship
        /// substructure (walk speed 100%)" needs about 290 and was losing its tail. Reported 2026-08-29.
        ///
        /// 420 covers every line the base game produces with room for a longer name from a mod. It is a guess
        /// with headroom rather than a guarantee: a sufficiently verbose terrain will still reach these buttons,
        /// and the honest fix for that would be measuring what the readout actually wrote, which means knowing
        /// what it decided to write -- a copy of its content rules that would go stale the first time they moved.
        /// </summary>
        private const float ReadoutColumn = 420f;

        /// <summary>Where the readout's first line sits, from <c>MouseoverReadout.BotLeft</c>.</summary>
        private const float BottomMargin = 65f;

        private static readonly List<Designator> Resolved = new List<Designator>();

        private static bool resolved;

        internal static void Draw()
        {
            if (!UIOverhaulSettingsFile.Current.showQuickOrders)
                return;

            if (Find.CurrentMap == null || Find.Selector == null || Find.Selector.NumSelected != 0)
                return;

            UIGuard.Try("Orders.QuickOrders", Paint,
                "The quick orders strip is not drawn. Every order it offers is still in the Architect menu.");
        }

        private static void Paint()
        {
            List<Designator> designators = Designators();

            if (designators.Count == 0)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            float width = designators.Count * ButtonSize + (designators.Count - 1) * Gap;
            float y = UI.screenHeight - BottomMargin - ButtonSize;

            Rect strip = new Rect(ReadoutColumn, y, width, ButtonSize);

            for (int i = 0; i < designators.Count; i++)
                Button(new Rect(strip.x + i * (ButtonSize + Gap), strip.y, ButtonSize, ButtonSize),
                    designators[i], palette);
        }

        private static void Button(Rect rect, Designator designator, UIColorPaletteDef palette)
        {
            bool disabled = designator.Disabled;
            bool chosen = Find.DesignatorManager != null
                          && Find.DesignatorManager.SelectedDesignator == designator;
            bool over = !disabled && Mouse.IsOver(rect);

            UIElementPainter.OutlineRounded(rect,
                chosen ? palette.Accent : over ? palette.Border : palette.Border,
                chosen
                    ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                    : over
                        ? UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay)
                        : palette.PanelBackground);

            Color previous = GUI.color;

            GUI.color = disabled ? palette.TextDisabled : palette.TextPrimary;

            if (designator.icon != null)
                Widgets.DrawTextureFitted(rect.ContractedBy(5f), designator.icon, 1f);

            GUI.color = previous;

            // The game's own words for both halves. A disabled reason written here would go stale the first time
            // a mod changed why an order is unavailable.
            string tip = designator.LabelCap;

            if (disabled && !designator.disabledReason.NullOrEmpty())
                tip += "\n\n" + designator.disabledReason;
            else if (!designator.Desc.NullOrEmpty())
                tip += "\n\n" + designator.Desc;

            TooltipHandler.TipRegion(rect, (TipSignal) tip);

            if (!Widgets.ButtonInvisible(rect) || disabled || Find.DesignatorManager == null)
                return;

            // Select, not activate. These are all drag designators, so choosing one arms the cursor and the
            // player then paints with it -- which is exactly what clicking the Architect tile does.
            Find.DesignatorManager.Select(designator);
        }

        /// <summary>
        /// The six designators, looked up once and kept.
        ///
        /// <b>Taken from the Orders category rather than constructed.</b> A <c>new Designator_Mine()</c> would be
        /// a second instance the game does not know about: it would not compare equal to the one the Architect
        /// menu selects, so the strip and the menu would disagree about what is currently armed, and it would
        /// miss any configuration the category applied on resolve.
        ///
        /// <b>Missing is survivable.</b> A designator absent from the category -- removed by another mod, or
        /// gone from a future RimWorld -- is simply left out, and the strip draws one button shorter rather than
        /// failing.
        /// </summary>
        private static List<Designator> Designators()
        {
            if (resolved)
                return Resolved;

            resolved = true;

            DesignationCategoryDef orders = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Orders");

            if (orders == null)
                return Resolved;

            for (int i = 0; i < Wanted.Length; i++)
            {
                List<Designator> all = orders.AllResolvedDesignators;

                for (int j = 0; all != null && j < all.Count; j++)
                {
                    if (all[j] != null && all[j].GetType() == Wanted[i])
                    {
                        Resolved.Add(all[j]);

                        break;
                    }
                }
            }

            return Resolved;
        }
    }

    /// <summary>
    /// Draws the strip alongside the mouseover readout, which is the thing it has to share a corner with.
    ///
    /// Hooked here rather than on a window because there is no window in that corner when nothing is selected --
    /// the readout is drawn straight onto the map interface, and this belongs at the same moment so the two are
    /// laid out against each other rather than one over the other.
    /// </summary>
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
    internal static class Patch_QuickOrdersOnGUI
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Find.UIRoot != null && Find.UIRoot.screenshotMode != null
                && Find.UIRoot.screenshotMode.FiltersCurrentEvent)
                return;

            QuickOrders.Draw();
        }
    }
}
