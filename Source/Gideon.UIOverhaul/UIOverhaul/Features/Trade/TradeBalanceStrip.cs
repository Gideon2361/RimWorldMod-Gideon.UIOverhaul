using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade
{
    /// <summary>
    /// The whole deal in one line: what each side holds, what each will hold, and which way the balance tips.
    ///
    /// <b>The one question a trade window has to answer before you press Accept.</b> Everything else on the
    /// screen is about a row -- what it costs, how many you keep. This is about the deal, and until now it was
    /// two small readouts at the bottom of the spine, under a heading, in the same weight as everything else.
    /// Vanilla does not answer it at all: it has a silver row that flashes when you overspend, which is a
    /// warning rather than a readout, and no statement anywhere of what the trader will be left holding.
    ///
    /// <b>Both sides, because a trade has two.</b> The trader's funds decide whether a sale can complete, and
    /// vanilla only mentions them by refusing at the moment of accepting -- a confirmation box asking whether to
    /// go ahead against a number the player has never seen. Watching it fall while building the deal turns that
    /// surprise into a decision.
    ///
    /// <b>The bar is the only invented thing here and it is deliberately coarse.</b> It shows the swing against
    /// the larger of the two purses, so it answers "is this a big deal for either of us" and nothing finer. The
    /// exact number is written under it, which is what anybody actually reads; the bar is for the glance.
    ///
    /// <b>Absent in gift mode, rather than empty.</b> A gift has no currency -- <c>UpdateCurrencyCount</c>
    /// returns without doing anything and <c>CurrencyTradeable</c> is not part of the deal -- so every figure
    /// here would be a zero dressed up as a fact. What a gift moves is goodwill, and the spine says so.
    /// </summary>
    internal static class TradeBalanceStrip
    {
        /// <summary>Height of the strip, sized for a caption, a number and a note.</summary>
        internal const float Height = 64f;

        private const float Pad = 12f;

        /// <summary>Whether there is anything to draw, which is the same question as "is this a real trade".</summary>
        internal static bool Applies
        {
            get
            {
                return UIGuard.Try("Trade.BalanceApplies",
                    () => !TradeSession.giftMode && TradeSession.deal != null
                                                 && TradeSession.deal.CurrencyTradeable != null, false, null);
            }
        }

        internal static void Draw(Rect rect, UIColorPaletteDef palette)
        {
            UIGuard.Try("Trade.BalanceStrip", () => Body(rect, palette),
                "The deal's balance strip did not draw. Both silver figures are also in the panel on the right.");
        }

        private static void Body(Rect rect, UIColorPaletteDef palette)
        {
            Tradeable currency = TradeSession.deal.CurrencyTradeable;

            if (currency == null)
                return;

            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            int yoursNow = currency.CountHeldBy(Transactor.Colony);
            int yoursAfter = currency.CountPostDealFor(Transactor.Colony);

            int theirsNow = currency.CountHeldBy(Transactor.Trader);
            int theirsAfter = currency.CountPostDealFor(Transactor.Trader);

            // The swing, read off the colony's own side. The trader's is its mirror by construction -- silver
            // does not appear or vanish in a trade -- so one subtraction describes both.
            int net = yoursAfter - yoursNow;

            float side = Mathf.Round((rect.width - Pad * 2f) * 0.3f);
            float middle = rect.width - Pad * 2f - side * 2f;

            Rect left = new Rect(rect.x + Pad, rect.y, side, rect.height);
            Rect centre = new Rect(left.xMax, rect.y, middle, rect.height);
            Rect right = new Rect(centre.xMax, rect.y, side, rect.height);

            Purse(left, "Your silver", yoursNow, yoursAfter, palette, TextAnchor.MiddleLeft);

            Balance(centre, net, Mathf.Max(yoursNow, theirsNow), palette);

            Purse(right, Carries(), theirsNow, theirsAfter, palette, TextAnchor.MiddleRight);
        }

        private static string Carries()
        {
            return UIGuard.Try("Trade.BalanceTraderName", () =>
            {
                string name = TradeSession.trader != null ? TradeSession.trader.TraderName : null;

                return name.NullOrEmpty() ? "They carry" : name + " carries";
            }, "They carry", null);
        }

        /// <summary>
        /// One side's silver: the caption, what they hold now, and what they will hold.
        ///
        /// <b>The "after" line is the one that matters and it is the small one,</b> which is the right way round:
        /// the large number is the fact you already know and the small one is the consequence you are deciding
        /// about, so the eye lands on the anchor first and the change second. Colouring the change is what makes
        /// it findable without making it shout.
        /// </summary>
        private static void Purse(Rect rect, string caption, int now, int after, UIColorPaletteDef palette,
            TextAnchor anchor)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;

                float tiny = UIFonts.LineHeightOf(GameFont.Tiny);
                float big = UIFonts.LineHeightOf(GameFont.Medium);

                float y = rect.y + Mathf.Max(0f, Mathf.Round((rect.height - tiny * 2f - big) * 0.5f));

                Text.Font = GameFont.Tiny;
                Text.Anchor = anchor;
                GUI.color = palette.TextDisabled;

                // <b>Monospaced, because all three lines are about a number that changes while you watch.</b> In
                // a proportional face a 1 is narrower than a 4, so a purse ticking from 4211 to 3811 shifts every
                // digit sideways as it counts. Fixed width holds the column still, which is the whole reason to
                // spend a face on a readout this small.
                //
                // The bar between these two keeps the game's font: it has one word and no figures, so there is
                // nothing for a monospace to line up.
                UITextControl.LabelEllipses(new Rect(rect.x, y, rect.width, tiny),
                    caption != null ? caption.ToUpperInvariant() : string.Empty,
                    UIFace.IBMPlexMono, GameFont.Tiny);

                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                UITextControl.Label(new Rect(rect.x, y + tiny, rect.width, big), now.ToStringCached(),
                    UIFace.IBMPlexMono, GameFont.Medium);

                Text.Font = GameFont.Tiny;

                // Unchanged is drawn dim rather than omitted, so the line does not appear and disappear as the
                // deal is built -- a row that comes and goes moves everything under it.
                GUI.color = after == now
                    ? palette.TextDisabled
                    : after < 0
                        ? palette.Danger
                        : after > now
                            ? palette.Success
                            : palette.Warning;

                UITextControl.LabelEllipses(new Rect(rect.x, y + tiny + big, rect.width, tiny),
                    "after this deal " + after.ToStringCached(), UIFace.IBMPlexMono, GameFont.Tiny);
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// Which way the deal tips, as a bar growing from the middle.
        ///
        /// <b>Centred rather than filled from the left,</b> because the quantity has a sign and a left-filled bar
        /// cannot show one. Middle is an even trade; right is silver coming to the colony, left is silver leaving
        /// it, and the direction matches the side of the strip that gains.
        /// </summary>
        /// <param name="scale">
        /// What the swing is measured against: the larger of the two purses. A deal is "big" relative to what the
        /// parties actually have, not relative to some absolute figure -- forty silver is the whole of a tribal
        /// trader's cash and a rounding error to an orbital one.
        /// </param>
        private static void Balance(Rect rect, int net, int scale, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;

                float tiny = UIFonts.LineHeightOf(GameFont.Tiny);

                float y = rect.y + Mathf.Max(0f, Mathf.Round((rect.height - tiny * 2f - 10f) * 0.5f));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(rect.x, y, rect.width, tiny), "DEAL BALANCE");

                float trackWidth = Mathf.Min(rect.width - 20f, 260f);

                Rect track = new Rect(rect.center.x - trackWidth * 0.5f, y + tiny + 3f, trackWidth, 6f);

                Widgets.DrawBoxSolid(track, palette.SurfaceSunken);

                // Guarded against a scale of nothing: two parties with no silver between them make every deal
                // infinitely large, and a bar pinned to one end would be the arithmetic showing through.
                float fraction = scale > 0 ? Mathf.Clamp01(Mathf.Abs(net) / (float) scale) : 0f;

                if (net != 0 && fraction > 0f)
                {
                    float half = track.width * 0.5f;
                    float length = Mathf.Max(2f, half * fraction);

                    Rect fill = net > 0
                        ? new Rect(track.center.x, track.y, length, track.height)
                        : new Rect(track.center.x - length, track.y, length, track.height);

                    Widgets.DrawBoxSolid(fill, net > 0 ? palette.Success : palette.Warning);
                }

                // The centre tick, so an even deal reads as even rather than as an empty track.
                Widgets.DrawBoxSolid(new Rect(track.center.x - 1f, track.y - 2f, 2f, track.height + 4f),
                    palette.TextDisabled);

                Text.Anchor = TextAnchor.MiddleCenter;

                GUI.color = net == 0 ? palette.TextDisabled : net > 0 ? palette.Success : palette.Warning;

                string summary = net == 0
                    ? "an even trade"
                    : net > 0
                        ? "+" + net.ToStringCached() + " to you"
                        : Mathf.Abs(net).ToStringCached() + " from you";

                Widgets.Label(new Rect(rect.x, track.yMax + 3f, rect.width, tiny), summary);
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }
    }
}
