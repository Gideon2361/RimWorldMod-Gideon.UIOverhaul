using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade.Shell
{
    /// <summary>
    /// A price, said twice over: the number, the word for how good it is, and a colour saying whose favour that
    /// is in.
    ///
    /// <b>Vanilla ties both meanings to one hue and then inverts it between the two columns.</b>
    /// <c>TradeUI.DrawPrice</c> paints a cheap buy pure green and a cheap sell pure red, from the same
    /// <c>PriceType</c>, in the same window, feet apart. Which is defensible in isolation -- green means good for
    /// you on both sides -- and unreadable in practice, because the player is also trying to learn what "cheap"
    /// looks like and the answer changes depending on which half of the row they are in.
    ///
    /// <b>So the two facts are split into two channels.</b> The <i>word</i> is the price level and never moves:
    /// exorbitant is exorbitant on both sides. The <i>colour</i> is whose favour it is in and never contradicts
    /// itself: green is good for you, everywhere, on both sides, forever. A player can then learn one of them
    /// without the other lying to them.
    ///
    /// <b>The five words are RimWorld's own.</b> <c>PriceTypeVeryCheap</c> through <c>PriceTypeExorbitant</c>
    /// already resolve to "very cheap", "cheap", "normal", "expensive" and "exorbitant" in every language the
    /// game ships, so this adds nothing to translate. Aaron rejected "dear" for expensive on 2026-08-25 --
    /// "that's not a word anyone would use to mean expensive" -- which is exactly the argument for reusing the
    /// keys rather than writing our own.
    /// </summary>
    internal static class TradePriceCell
    {
        /// <summary>Width the cell needs: the number, the word under it, and the scale beside them.</summary>
        internal const float Width = 116f;

        /// <summary>
        /// How good this price is for the player, from -2 to +2.
        ///
        /// <b>The sign flips with the direction and the word does not,</b> which is the whole idea. Buying cheap
        /// is good and selling cheap is bad, and both of those rows still say "cheap".
        /// </summary>
        private static int Favour(PriceType price, TradeAction action)
        {
            int level;

            switch (price)
            {
                case PriceType.VeryCheap:
                    level = 2;
                    break;
                case PriceType.Cheap:
                    level = 1;
                    break;
                case PriceType.Expensive:
                    level = -1;
                    break;
                case PriceType.Exorbitant:
                    level = -2;
                    break;
                default:
                    level = 0;
                    break;
            }

            return action == TradeAction.PlayerBuys ? level : -level;
        }

        /// <summary>
        /// Where this price sits on the five-step scale, from 0 at very cheap to 4 at exorbitant.
        ///
        /// Separate from <see cref="Favour"/> and deliberately not derived from it: the scale draws the price
        /// level, which does not move between the buy and sell sides, and deriving it from a number that does
        /// would put the mark in different places for the same word.
        /// </summary>
        private static int Step(PriceType price)
        {
            switch (price)
            {
                case PriceType.VeryCheap:
                    return 0;
                case PriceType.Cheap:
                    return 1;
                case PriceType.Expensive:
                    return 3;
                case PriceType.Exorbitant:
                    return 4;
                default:
                    return 2;
            }
        }

        /// <summary>
        /// The colour for a favour level.
        ///
        /// <b>Four palette roles rather than five, and no hand-mixed hues.</b> A theme that restates what success
        /// and danger look like restates this with it, which a literal green could not do. The two middle steps
        /// borrow the strong colour at reduced alpha rather than being separate roles, because "slightly good" is
        /// not a thing a palette has an opinion about and inventing a role for it would be inventing a meaning.
        /// </summary>
        private static Color Tint(int favour, UIColorPaletteDef palette)
        {
            if (favour >= 2)
                return palette.Success;

            if (favour == 1)
                return Fade(palette.Success, 0.78f);

            if (favour == -1)
                return palette.Warning;

            if (favour <= -2)
                return palette.Danger;

            return palette.TextPrimary;
        }

        private static Color Fade(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>
        /// The translated word for a price level, from RimWorld's own keys.
        ///
        /// Undefined falls through to normal rather than to an empty cell. It means the trader kind has no
        /// opinion about this thing, which is what normal already means, and a blank where every other row has a
        /// word reads as a bug.
        /// </summary>
        internal static string Word(PriceType price)
        {
            switch (price)
            {
                case PriceType.VeryCheap:
                    return "PriceTypeVeryCheap".Translate();
                case PriceType.Cheap:
                    return "PriceTypeCheap".Translate();
                case PriceType.Expensive:
                    return "PriceTypeExpensive".Translate();
                case PriceType.Exorbitant:
                    return "PriceTypeExorbitant".Translate();
                default:
                    return "PriceTypeNormal".Translate();
            }
        }

        /// <summary>
        /// The price alone, for a table with a column for each direction.
        ///
        /// <b>The number keeps its favour colour and gives up its word.</b> With "you get" and "you pay" both on
        /// the row there is no room to write "exorbitant" twice, and dropping the colour instead would lose the
        /// half of the pair that works at a glance -- green is good for you, on both sides, always. The word is
        /// not lost, only moved: it opens the tooltip, above vanilla's own derivation of the price.
        /// </summary>
        internal static void Compact(Rect rect, Tradeable tradeable, TradeAction action,
            UIColorPaletteDef palette)
        {
            if (tradeable == null || tradeable.IsCurrency || !tradeable.TraderWillTrade)
                return;

            UIGuard.Try("Trade.PriceCompact", () =>
            {
                PriceType price = tradeable.PriceTypeFor(action);
                float amount = tradeable.GetPriceFor(action);

                Color tint = Tint(Favour(price, action), palette);

                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                try
                {
                    Text.WordWrap = false;
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = tint;

                    Widgets.Label(rect, TradeSession.TradeCurrency == TradeCurrency.Silver
                        ? amount.ToStringMoney()
                        : amount.ToString("F0"));
                }
                finally
                {
                    Text.WordWrap = true;
                    GUI.color = previousColor;
                    Text.Anchor = previousAnchor;
                    Text.Font = previousFont;
                }

                if (!Mouse.IsOver(rect))
                    return;

                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

                TooltipHandler.TipRegion(rect, new TipSignal(
                    () => Word(price).CapitalizeFirst() + "\n\n" + tradeable.GetPriceTooltip(action),
                    tradeable.GetHashCode() * 811 + (int) action));
            }, "One price cell did not draw. The price itself is unaffected.");
        }

        /// <summary>
        /// Draws the cell for one tradeable in one direction.
        ///
        /// <b>The tooltip is vanilla's, unedited.</b> <c>Tradeable.GetPriceTooltip</c> builds the whole
        /// derivation -- base value, the trader kind's factor, the negotiator's bonus, the faction leader's,
        /// every drug and produce modifier -- and reproducing any of that would be writing a second pricing
        /// model that agrees until it does not. Built lazily through the function form of <c>TipSignal</c>,
        /// because the string is expensive and a hovered row asks for it many times a second.
        /// </summary>
        internal static void Draw(Rect rect, Tradeable tradeable, TradeAction action, UIColorPaletteDef palette)
        {
            if (tradeable == null)
                return;

            UIGuard.Try("Trade.PriceCell", () => Body(rect, tradeable, action, palette),
                "One price cell did not draw. The price itself is unaffected.");
        }

        private static void Body(Rect rect, Tradeable tradeable, TradeAction action, UIColorPaletteDef palette)
        {
            if (!tradeable.TraderWillTrade || tradeable.IsCurrency)
                return;

            PriceType price = tradeable.PriceTypeFor(action);
            float amount = tradeable.GetPriceFor(action);

            int favour = Favour(price, action);
            Color tint = Tint(favour, palette);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;

                // The shell's own splitter, so this column and the item name beside it share two baselines
                // rather than each rounding its own. It is also what stopped the word clipping out of the row.
                Rect numberLine;
                Rect wordLine;

                TradeShell.TwoLine(new Rect(rect.x, rect.y, rect.width - 22f, rect.height), out numberLine,
                    out wordLine);

                // Number and word stacked, both right-aligned on the same edge, because that edge is what a
                // player runs their eye down when comparing rows.
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = tint;

                Widgets.Label(numberLine,
                    TradeSession.TradeCurrency == TradeCurrency.Silver
                        ? amount.ToStringMoney()
                        : amount.ToString("F0"));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Fade(tint, 0.75f);

                Widgets.Label(wordLine, Word(price));

                Scale(new Rect(rect.xMax - 14f, numberLine.y + 1f, 7f,
                    Mathf.Max(10f, wordLine.yMax - numberLine.y - 2f)), Step(price), tint, palette);
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!Mouse.IsOver(rect))
                return;

            Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            TooltipHandler.TipRegion(rect,
                new TipSignal(() => tradeable.GetPriceTooltip(action), tradeable.GetHashCode() * 613));
        }

        /// <summary>
        /// The five-step scale, drawn vertically beside the number.
        ///
        /// <b>Cheap at the top and exorbitant at the bottom,</b> which is the order the numbers themselves are in
        /// and the order the mark falls as a price gets worse to buy. Only the occupied step is coloured; the
        /// rest are the track, so the shape of the column tells you where you are without needing the word.
        /// </summary>
        private static void Scale(Rect rect, int step, Color tint, UIColorPaletteDef palette)
        {
            float cell = rect.height / 5f;

            for (int i = 0; i < 5; i++)
            {
                Rect box = new Rect(rect.x, rect.y + cell * i, rect.width, Mathf.Max(1f, cell - 1f));

                Widgets.DrawBoxSolid(box, i == step ? tint : palette.SurfaceSunken);
            }
        }
    }
}
