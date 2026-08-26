using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using Gideon.UIOverhaul.Features.Trade.Shell;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Trade
{
    /// <summary>
    /// The trade window: a rail to choose what you are looking at, a table to work in, and the deal standing
    /// beside them where you can see all of it.
    ///
    /// <b>We own presentation and interaction; vanilla owns the transaction.</b> That single split is what makes
    /// a custom window affordable rather than a second trade system to maintain. <c>TradeSession</c> hands over
    /// <c>trader</c>, <c>playerNegotiator</c>, <c>deal</c> and <c>giftMode</c> as public statics; <c>TradeDeal</c>
    /// publishes <c>AllTradeables</c>, <c>UpdateCurrencyCount()</c>, <c>DoesTraderHaveEnoughSilver()</c> and
    /// <c>TryExecute(out bool)</c>; every count change goes through <c>Transferable.AdjustTo</c>, whose protected
    /// setter exists precisely so nobody does it another way. <b>Nothing below reimplements a price, a goodwill
    /// change, a slavery check or a caravan mass rule.</b>
    ///
    /// <b>What it changes.</b> Vanilla puts everything both sides own into one flat list with buying and selling
    /// interleaved, and asks you to find your deal in it by spotting nonzero numbers among two hundred rows.
    /// Here the two directions are separate views, each railed by category; the price says its level in a word and
    /// its favour in a colour, which vanilla ties to one hue and then inverts between the columns; the count is a
    /// field you can type in rather than a cluster of five arrows; the row says what you will hold afterwards,
    /// from a method the engine already computes and draws nowhere; and the deal is a visible object with both
    /// totals, the trader's funds and the resulting silver.
    ///
    /// <b>The one compatibility hazard, stated plainly.</b> A mod that patches <c>Dialog_Trade</c> will never see
    /// this window, so anything adding a column, a button or a filter to the vanilla dialog silently stops
    /// working. That is not curable -- it is what replacing a window means -- so the setting that hands
    /// <c>Dialog_Trade</c> back shipped with this screen rather than after the first bug report. See
    /// <see cref="Patch_TradeWindow"/>.
    ///
    /// <b>The one engine trap, and why the accept button checks before it commits.</b>
    /// <c>TradeDeal.TryExecute</c> answers an unaffordable deal with
    /// <c>Find.WindowStack.WindowOfType&lt;Dialog_Trade&gt;().FlashSilver()</c> -- an unguarded dereference of a
    /// window that is not in the stack while ours is. So <see cref="Affordable"/> mirrors that method's own
    /// precondition exactly and refuses first, with our own flash and vanilla's own message. It is the only place
    /// in this feature where we reproduce a check rather than delegate one, and it is reproduced so that the
    /// delegation below it is safe.
    /// </summary>
    internal class Dialog_UITrade : Window
    {
        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private readonly List<Tradeable> rows = new List<Tradeable>();
        private readonly List<Tradeable> scratch = new List<Tradeable>();
        private readonly List<TradeRailEntry> rail = new List<TradeRailEntry>();
        private readonly List<TradeRailEntry> sides = new List<TradeRailEntry>();

        /// <summary>
        /// The rows a standing want filled in before the player touched anything.
        ///
        /// Held so the table can mark them. A deal that had lines in it on opening is a deal the player did not
        /// make, and the one thing that must not happen is somebody accepting a purchase they never chose.
        /// </summary>
        private HashSet<Tradeable> prefilled = new HashSet<Tradeable>();

        private TradeSide side = TradeSide.Buy;
        private string category = TradeCatalog.All;

        /// <summary>
        /// Whether this visit was only ever about giving things away.
        ///
        /// <b>Read once, in the constructor, because after that the flag it comes from is a live setting.</b>
        /// <c>TradeSession.SetupWith</c> seeds <c>giftMode</c> from the <c>giftsOnly</c> argument, and from then
        /// on the player's own switch writes to that same static -- so asking it later cannot tell "this is a
        /// gift visit" apart from "the player pressed Gift a moment ago". Vanilla keeps a private field for
        /// exactly this and gates its own mode button on it.
        /// </summary>
        private readonly bool giftsOnly;

        private Vector2 tableScroll;
        private bool tableDragging;
        private float tableDragOffset;

        private Vector2 spineScroll;
        private bool spineDragging;
        private float spineDragOffset;

        // The caravan readout, recomputed only when the deal moves. Vanilla keeps the same flags on its own
        // dialog and for the same reason: the mass calculators walk every pawn and every stack, which is nothing
        // once and far too much sixty times a second.
        private bool caravan;
        private List<Thing> caravanContents;
        private bool massDirty = true;
        private float massUsage;
        private float massCapacity;

        internal Dialog_UITrade()
        {
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = false;
            soundAppear = SoundDefOf.CommsWindow_Open;
            soundClose = SoundDefOf.CommsWindow_Close;

            // Vanilla plays this behind an orbital trade and it is half of what a trade ship feels like. Read
            // from the same test it uses.
            if (TradeSession.trader is PassingShip)
                soundAmbient = SoundDefOf.RadioComms_Ambience;

            // Captured before anything can move it. See the field.
            giftsOnly = TradeSession.giftMode;

            if (TradeSession.giftMode)
                side = TradeSide.Gift;
        }

        /// <summary>
        /// <b>Sized to the widest row, not to the screen.</b> The table's fixed columns come to 560 pixels and a
        /// name comfortably fits in 400 more, so past about a thousand every extra pixel went to the name column
        /// and sat there empty -- a window two thirds white space, with the price you are reading a foot away
        /// from the item it belongs to. Narrowed on 2026-08-25 after seeing it at 1360.
        /// </summary>
        public override Vector2 InitialSize =>
            new Vector2(Mathf.Min(1020f, UI.screenWidth - 20f), Mathf.Min(940f, UI.screenHeight - 20f));

        /// <summary>
        /// Vanilla's own answer, and not a formality: it is what stops a message toast from being unreadable over
        /// a full-bleed window, and this window is fuller than the one it replaces.
        /// </summary>
        public override bool CausesMessageBackground()
        {
            return true;
        }

        public override void PostOpen()
        {
            base.PostOpen();

            Search.Clear();

            TradeStepper.Forget();

            UIGuard.Try("Trade.Open", () =>
            {
                // Fully qualified: our own Trade.Caravan namespace shadows RimWorld.Planet.Caravan inside this one,
                // and the compiler resolves the nearer name first.
                RimWorld.Planet.Caravan playerCaravan = TradeSession.playerNegotiator?.GetCaravan();

                caravan = playerCaravan != null;

                if (caravan)
                {
                    caravanContents = new List<Thing>();
                    caravanContents.AddRange(playerCaravan.PawnsListForReading);
                    caravanContents.AddRange(CaravanInventoryUtility.AllInventoryItems(playerCaravan));
                }

                // <b>Said before anything is filled in, because it changes what the numbers mean.</b> Vanilla
                // opens this dialog over its own window and it is worth keeping: a negotiator who cannot hear or
                // speak properly is trading at a penalty the player has no other way to notice.
                Warn();

                prefilled = TradeWants.Prefill(TradeSession.deal);
            }, "Standing wants were not applied to this trade. The deal itself is unaffected.");
        }

        private void Warn()
        {
            if (TradeSession.giftMode || caravan || TradeSession.playerNegotiator == null)
                return;

            Pawn negotiator = TradeSession.playerNegotiator;

            float talking = negotiator.health.capacities.GetLevel(PawnCapacityDefOf.Talking);
            float hearing = negotiator.health.capacities.GetLevel(PawnCapacityDefOf.Hearing);

            if (talking >= 0.95f && hearing >= 0.95f)
                return;

            TaggedString text = talking < 0.95f
                ? "NegotiatorTalkingImpaired".Translate(negotiator.LabelShort, negotiator)
                : "NegotiatorHearingImpaired".Translate(negotiator.LabelShort, negotiator);

            text += "\n\n" + "NegotiatorCapacityImpaired".Translate();

            Find.WindowStack.Add(new Dialog_MessageBox(text));
        }

        public override void Close(bool doCloseSound = true)
        {
            // Vanilla stops this on the way out and skipping it leaves a half-grabbed drag slider live over the
            // map. Ours has no sliders, but a mod's column may have added one to this same session.
            DragSliderManager.ForceStop();

            base.Close(doCloseSound);

            TradeStepper.Forget();

            // The quest a visiting trader was carrying is handed over when the window shuts, not when the deal
            // executes -- so it must happen on every exit including cancel, exactly as vanilla does it.
            UIGuard.Try("Trade.CloseQuest", () =>
            {
                Pawn pawn = TradeSession.trader as Pawn;

                if (pawn != null && pawn.mindState != null && pawn.mindState.hasQuest)
                    TradeUtility.ReceiveQuestFromTrader(pawn, TradeSession.playerNegotiator);
            }, "A quest a trader was carrying was not handed over.");
        }

        public override void DoWindowContents(Rect inRect)
        {
            TradeShell.Guarded("Trade.Window", inRect, () => Contents(inRect),
                "This trade window failed to draw. Nothing has been bought or sold. Close it and reopen the "
                + "trade, or switch the window off under Additional Features to use RimWorld's own.");
        }

        private void Contents(Rect inRect)
        {
            if (!TradeSession.Active || TradeSession.deal == null)
            {
                Close();

                return;
            }

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // Vanilla recomputes the silver line every frame from the rest of the deal, and so must we: it is
            // what makes the running total under the spine true rather than one interaction behind.
            TradeSession.deal.UpdateCurrencyCount();

            Rect headerRect;
            Rect railRect;
            Rect tableRect;
            Rect spineRect;
            Rect footerRect;

            // <b>No rail and no spine.</b> Both were paid for by the table, which is the only part of this
            // window whose usefulness grows with the space it gets: the rail's categories became a row of pills
            // one line tall, and the spine's job -- showing the deal as an object -- was taken over by the
            // balance strip, which says the same thing across the full width in half the height. What is left is
            // a table with room for two price columns, which is the layout the screen was always trying to be.
            TradeShell.Layout(inRect, false, false, out headerRect, out railRect, out tableRect, out spineRect,
                out footerRect);

            // <b>The balance strip is carved out of the bottom of the body rather than laid out with it.</b> It
            // spans the whole window, which the shell's three-column split has no way to express -- and giving
            // Layout a fourth region for something only this screen has would be pushing one screen's furniture
            // into the shape all four share. Shortening the three columns afterwards is the smaller change and
            // keeps the strip's existence a fact about the trade window alone.
            // <b>Directly under the header, above the table.</b> It is the summary of the whole deal, and a
            // summary belongs where the eye starts rather than where it stops -- the footer already carries the
            // commit, and putting the numbers next to the button that spends them made the button the thing you
            // read first. It also means the two silver figures sit near the trader's name, which is who they
            // belong to.
            Rect balanceRect = new Rect(inRect.x, tableRect.y, inRect.width, 0f);

            if (TradeBalanceStrip.Applies)
            {
                balanceRect = new Rect(inRect.x, tableRect.y, inRect.width, TradeBalanceStrip.Height);

                float lost = TradeBalanceStrip.Height + TradeShell.Gap;

                tableRect = new Rect(tableRect.x, tableRect.y + lost, tableRect.width,
                    Mathf.Max(0f, tableRect.height - lost));
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                Header(headerRect, palette);

                if (balanceRect.height > 0f)
                    TradeBalanceStrip.Draw(balanceRect, palette);

                Table(tableRect, palette);

                Footer(footerRect, palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Header
        // ---------------------------------------------------------------------------------------

        private void Header(Rect rect, UIColorPaletteDef palette)
        {
            // <b>The switch always offers every mode, including the one you are in.</b> It used to drop Buy and
            // Sell while gift mode was on, which left one segment, which fell through the count test below and
            // drew nothing at all -- so pressing Gift removed the only way back to trading and the player had to
            // close the window. Reported on 2026-08-25. A mode switch that hides the other modes is not a switch.
            //
            // Buy, Sell and Gift are three views of one deal, so they are segments rather than buttons: what
            // matters is seeing the one you are not in.
            sides.Clear();

            // A gift-only visit has nothing to switch between -- there is no trading on offer, and vanilla hides
            // its own mode button on the same condition. The whole control goes rather than being drawn with one
            // segment in it.
            if (!giftsOnly)
            {
                sides.Add(TradeRailEntry.Of("buy", "Buy", -1));
                sides.Add(TradeRailEntry.Of("sell", "Sell", -1));

                if (Giftable())
                    sides.Add(TradeRailEntry.Of("gift", "Gift", -1));
            }

            // <b>Measured before anything is drawn, because the standing line shares this line with it.</b> That
            // line used to be right-aligned to the panel edge and was simply painted over by the switch, so a
            // trader's faction and goodwill ended mid-word underneath the Buy button. Reported on 2026-08-25.
            float switchWidth = sides.Count > 0 ? Mathf.Min(240f, sides.Count * 80f) : 0f;

            // The trader's name alone on the left; who they are and how they feel about you on the right, on the
            // same line rather than under it. Two facts of equal weight read better side by side than stacked,
            // and it buys back the line the pills now cost.
            TradeShell.Header(rect, TraderName(), null, palette);

            Standing(rect, switchWidth > 0f ? switchWidth + 12f : 0f, palette);

            if (sides.Count == 0)
                return;

            string picked = TradeRail.Segments(
                new Rect(rect.xMax - switchWidth, rect.y + 6f, switchWidth, 30f), sides, Key(side), palette);

            if (picked != null)
                Switch(picked);
        }

        private static string Key(TradeSide value)
        {
            return value == TradeSide.Buy ? "buy" : value == TradeSide.Sell ? "sell" : "gift";
        }

        /// <summary>
        /// Moves between the three views, rebuilding the deal when the move is into or out of gift mode.
        ///
        /// <b>Buy and sell are a view change; gift is a mode change.</b> The first two read one deal two ways and
        /// cost nothing. Gift makes <c>TradeSession</c> rebuild <c>AllTradeables</c> with no trader goods in it at
        /// all, so every <c>Tradeable</c> the table was holding is replaced -- which is why the count boxes are
        /// dropped with it. A box surviving that would be writing the last deal's number into a new object.
        /// </summary>
        private void Switch(string picked)
        {
            bool wantGift = picked == "gift";

            if (wantGift != TradeSession.giftMode)
            {
                UIGuard.Try("Trade.SwitchMode", () =>
                {
                    TradeSession.giftMode = wantGift;
                    TradeSession.deal.Reset();

                    TradeStepper.Forget();

                    prefilled = wantGift
                        ? new HashSet<Tradeable>()
                        : TradeWants.Prefill(TradeSession.deal);

                    massDirty = true;
                }, "Switching between trading and gifting failed. The deal was not changed.");

                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }

            side = wantGift ? TradeSide.Gift : picked == "sell" ? TradeSide.Sell : TradeSide.Buy;

            category = TradeCatalog.All;
            tableScroll = Vector2.zero;
        }

        /// <summary>
        /// Whether this trader can be given gifts, which is vanilla's own set of conditions.
        ///
        /// A faction is needed for goodwill to move, a permanent enemy has no goodwill to move, and a favour
        /// trader deals in a currency that gifts are not denominated in. Only consulted when the visit is not
        /// already gift-only, which is vanilla's fourth condition and is tested by the caller.
        /// </summary>
        private static bool Giftable()
        {
            return UIGuard.Try("Trade.Giftable", () =>
            {
                Faction faction = TradeSession.trader?.Faction;

                return faction != null && !faction.def.permanentEnemy
                       && TradeSession.trader.TradeCurrency != TradeCurrency.Favor;
            }, false, null);
        }

        private static string TraderName()
        {
            return UIGuard.Try("Trade.TraderName", () => TradeSession.trader?.TraderName, "Trader", null);
        }

        /// <summary>
        /// The line under the trader's name: what kind they are, and how the negotiator changes the price.
        ///
        /// <b>The price improvement belongs here rather than in a corner.</b> It is the one number that applies to
        /// every row in the window, so it is context for the whole screen rather than a fact about the negotiator.
        /// </summary>
        private static string TraderDetail()
        {
            return UIGuard.Try("Trade.TraderDetail", () =>
            {
                List<string> parts = new List<string>();

                TraderKindDef kind = TradeSession.trader?.TraderKind;

                if (kind != null)
                    parts.Add(kind.LabelCap);

                Faction faction = TradeSession.trader?.Faction;

                if (faction != null)
                    parts.Add(faction.Name + " · " + faction.PlayerGoodwill.ToStringWithSign());

                Pawn negotiator = TradeSession.playerNegotiator;

                if (negotiator != null)
                {
                    parts.Add(negotiator.LabelShortCap + " negotiating, "
                              + negotiator.GetStatValue(StatDefOf.TradePriceImprovement).ToStringPercent()
                              + " price improvement");
                }

                // Joined on a separator rather than through ToCommaList, which would put an "and" in the middle
                // of a run of facts that are not a list of things.
                return string.Join(" · ", parts.ToArray());
            }, string.Empty, null);
        }

        // ---------------------------------------------------------------------------------------
        // Rail
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The search box, the category pills and the sort control, on one band above the table.
        ///
        /// <b>Only the categories a trader actually has.</b> Eight fixed pills would mean six greyed ones on
        /// every shaman and four on every bulk trader, and a filter you cannot use is a filter in the way. The
        /// two saved views and the refused view join them when there is anything behind them, so the row grows
        /// with what is there rather than being padded out to a constant.
        ///
        /// Returns the height it used, since the pills wrap on a narrow window.
        /// </summary>
        private float Filters(Rect rect, UIColorPaletteDef palette)
        {
            float searchWidth = Mathf.Min(240f, rect.width * 0.3f);

            Search.Draw(new Rect(rect.x, rect.y, searchWidth, 28f), palette);

            float sortWidth = SortControl(rect, palette);

            rail.Clear();

            rail.Add(TradeRailEntry.Of(TradeCatalog.All, "All", -1));

            if (TradeWants.WantCount > 0)
                rail.Add(TradeRailEntry.Of(TradeCatalog.Wants, "Standing wants", -1));

            if (TradeWants.FavouriteCount > 0)
                rail.Add(TradeRailEntry.Of(TradeCatalog.Favourites, "Favourites", -1));

            string[] categories = TradeCatalog.Categories;

            for (int i = 0; i < categories.Length; i++)
            {
                TradeCatalog.Rows(TradeSession.deal, side, categories[i], Search.Text, scratch);

                if (scratch.Count > 0)
                    rail.Add(TradeRailEntry.Of(categories[i], TradeCatalog.NameOf(categories[i]), -1));
            }

            TradeCatalog.Rows(TradeSession.deal, side, TradeCatalog.Refused, Search.Text, scratch);

            if (scratch.Count > 0)
                rail.Add(TradeRailEntry.Of(TradeCatalog.Refused, "Not accepted", -1));

            float left = rect.x + searchWidth + 12f;

            float pillHeight;

            string picked = TradeRail.Pills(
                new Rect(left, rect.y + 1f, Mathf.Max(60f, rect.xMax - left - sortWidth - 12f), 28f), rail,
                category, palette, out pillHeight);

            if (picked != null)
            {
                category = picked;
                tableScroll = Vector2.zero;
            }

            return Mathf.Max(28f, pillHeight) + 10f;
        }

        /// <summary>
        /// The sort control: a sentence rather than a dropdown, with the changeable word as the button.
        ///
        /// <b>"Sorted by Category, then Name" says what the list is doing;</b> a bare dropdown reading "Category"
        /// says what a control is set to and leaves the player to work out what that means for the rows. The
        /// second half is fixed because it is always true -- every order falls through to the name so that equal
        /// rows never shuffle between frames.
        /// </summary>
        private float SortControl(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            float width;

            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;

                string chosen = TradeCatalog.NameOfSort(TradeCatalog.Sort);

                string tail = TradeCatalog.Sort == TradeCatalog.TradeSort.Name
                    ? "Sorted by Name"
                    : "Sorted by " + chosen + ", then Name";

                width = Text.CalcSize(tail).x + 16f;

                Rect button = new Rect(rect.xMax - width, rect.y, width, 28f);

                if (Mouse.IsOver(button))
                    Widgets.DrawBoxSolid(button, palette.HoverOverlay);

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Mouse.IsOver(button) ? palette.Accent : palette.TextDisabled;

                Widgets.Label(new Rect(button.x, button.y, button.width - 8f, button.height), tail);

                if (Widgets.ButtonInvisible(button))
                    OpenSortMenu();
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return width;
        }

        private void OpenSortMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (TradeCatalog.TradeSort sort in System.Enum.GetValues(typeof(TradeCatalog.TradeSort)))
            {
                TradeCatalog.TradeSort captured = sort;

                options.Add(new FloatMenuOption(TradeCatalog.NameOfSort(captured), () =>
                {
                    TradeCatalog.Sort = captured;

                    tableScroll = Vector2.zero;
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ---------------------------------------------------------------------------------------
        // Table
        // ---------------------------------------------------------------------------------------

        private const float StarWidth = 22f;
        private const float BadgeWidth = 26f;
        private const float CountWidth = 70f;
        private const float PriceWidth = 84f;
        private const float CellGap = 8f;

        /// <summary>
        /// Kept clear at the right of every row, between the last number and the scrollbar.
        ///
        /// <b>The columns end here, the row banding does not.</b> A stripe that stopped short of the scrollbar
        /// would read as a row that had been cut off; a right-aligned number pressed against a moving thumb reads
        /// as a number about to be covered by it. The scrollbar's own four-pixel gutter is the gap it needs to
        /// not touch the frame -- it was never a gap for content to breathe in. Reported on 2026-08-25.
        /// </summary>
        private const float TrailPad = 14f;

        /// <summary>
        /// Where each column starts, worked out once per frame from the width actually available.
        ///
        /// <b>Both price columns at once, which is the point of the layout.</b> A row shows what you would
        /// receive for one and what you would pay for one, side by side with the count between them -- so the
        /// spread is visible without switching views, and the stepper sits literally between the two numbers it
        /// moves you along. Vanilla shows one price per row per direction and asks you to remember the other.
        ///
        /// <b>Columns drop out rather than being squeezed.</b> On a narrow window the counts go first, then the
        /// price for the direction you are not in -- because a name shortened to eight characters is a row you
        /// cannot identify, while a missing count is a fact the tooltip still carries.
        /// </summary>
        private struct Columns
        {
            internal float Badge;
            internal float Name;
            internal float Yours;
            internal float YouGet;
            internal float Stepper;
            internal float YouPay;
            internal float Theirs;

            internal bool ShowCounts;
            internal bool ShowBothPrices;

            internal float NameWidth;

            internal static Columns For(float width, TradeSide side)
            {
                Columns columns = new Columns();

                // Taken off the top so it comes out of the name column with everything else, rather than being
                // subtracted at the end from whichever column happened to land last.
                width -= TrailPad;

                float baseWidth = StarWidth + CellGap + BadgeWidth + CellGap + TradeStepper.Width + CellGap;

                // One price column is never dropped: a trade row without a price is a row you cannot judge.
                float onePrice = PriceWidth + CellGap;
                float bothPrices = onePrice * 2f;
                float bothCounts = (CountWidth + CellGap) * 2f;

                columns.ShowBothPrices = width - baseWidth - bothPrices - bothCounts > 300f;
                columns.ShowCounts = width - baseWidth - onePrice - bothCounts > 260f;

                float used = baseWidth + (columns.ShowBothPrices ? bothPrices : onePrice)
                                       + (columns.ShowCounts ? bothCounts : 0f);

                columns.NameWidth = Mathf.Max(140f, width - used);

                float x = StarWidth + CellGap;

                columns.Badge = x;

                x += BadgeWidth + CellGap;

                columns.Name = x;

                x += columns.NameWidth + CellGap;

                columns.Yours = x;

                if (columns.ShowCounts)
                    x += CountWidth + CellGap;

                columns.YouGet = x;

                // With one price column it sits on whichever side the view is about, so the number under the
                // heading is the number that view is for.
                if (columns.ShowBothPrices || side == TradeSide.Sell || side == TradeSide.Gift)
                    x += PriceWidth + CellGap;

                columns.Stepper = x;

                x += TradeStepper.Width + CellGap;

                columns.YouPay = x;

                if (columns.ShowBothPrices || side == TradeSide.Buy)
                    x += PriceWidth + CellGap;

                columns.Theirs = x;

                return columns;
            }

            /// <summary>Whether the "you get" column is drawn in this view at this width.</summary>
            internal bool Get(TradeSide side)
            {
                return ShowBothPrices || side != TradeSide.Buy;
            }

            /// <summary>Whether the "you pay" column is drawn in this view at this width.</summary>
            internal bool Pay(TradeSide side)
            {
                return ShowBothPrices || side == TradeSide.Buy;
            }
        }

        private void Table(Rect rect, UIColorPaletteDef palette)
        {
            float band = Filters(rect, palette);

            float top = rect.y + band;

            float width = GzpPalette.ContentWidth(rect);

            Columns columns = Columns.For(width, side);

            Captions(new Rect(rect.x, top, width, TradeShell.ColumnsHeight), columns, palette);

            top += TradeShell.ColumnsHeight;

            // <b>Silver is pinned above the list rather than sorted into it.</b> It is not an item you are
            // choosing between -- it is the price of everything else, and a currency row that scrolled away
            // would take the running total off screen exactly when the list got long enough to need it.
            if (TradeBalanceStrip.Applies)
            {
                SilverRow(new Rect(rect.x, top, width, TradeShell.RowHeight), columns, palette);

                top += TradeShell.RowHeight;
            }

            TradeCatalog.Rows(TradeSession.deal, side, category, Search.Text, rows);

            Rect list = new Rect(rect.x, top, rect.width, Mathf.Max(0f, rect.yMax - top));
            Rect view = new Rect(0f, 0f, width, rows.Count * TradeShell.RowHeight + 2f);

            Widgets.BeginScrollView(list, ref tableScroll, view, false);

            // <b>Only the rows on screen are drawn,</b> which vanilla also does and which matters more here: each
            // of ours carries a price tooltip, a text field and several measured labels, and a trade ship's list
            // runs to a couple of hundred.
            float first = tableScroll.y - TradeShell.RowHeight;
            float last = tableScroll.y + list.height;

            for (int i = 0; i < rows.Count; i++)
            {
                float y = i * TradeShell.RowHeight;

                if (y < first || y > last)
                    continue;

                Row(new Rect(0f, y, view.width, TradeShell.RowHeight), rows[i], i, columns, palette);
            }

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(list, view.height, ref tableScroll, ref tableDragging, ref tableDragOffset);

            if (rows.Count == 0)
                Empty(list, palette);
        }

        private void Captions(Rect rect, Columns columns, UIColorPaletteDef palette)
        {
            TradeShell.Column(new Rect(rect.x + columns.Badge, rect.y, columns.NameWidth + BadgeWidth,
                rect.height), "Item", palette);

            if (columns.ShowCounts)
            {
                TradeShell.Column(new Rect(rect.x + columns.Yours, rect.y, CountWidth, rect.height), "Yours",
                    palette, TextAnchor.MiddleRight);

                TradeShell.Column(new Rect(rect.x + columns.Theirs, rect.y, CountWidth, rect.height), "Theirs",
                    palette, TextAnchor.MiddleRight);
            }

            if (columns.Get(side))
                TradeShell.Column(new Rect(rect.x + columns.YouGet, rect.y, PriceWidth, rect.height), "You get",
                    palette, TextAnchor.MiddleRight);

            TradeShell.Column(new Rect(rect.x + columns.Stepper, rect.y, TradeStepper.Width, rect.height),
                side == TradeSide.Buy ? "Buy" : side == TradeSide.Sell ? "Sell" : "Offer", palette,
                TextAnchor.MiddleCenter);

            if (columns.Pay(side))
                TradeShell.Column(new Rect(rect.x + columns.YouPay, rect.y, PriceWidth, rect.height), "You pay",
                    palette, TextAnchor.MiddleRight);

            GUI.color = palette.Border;

            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);

            GUI.color = Color.white;
        }

        /// <summary>
        /// The currency row: what each side holds, and what this deal does to it.
        ///
        /// <b>Read only, deliberately.</b> The number is not a choice -- <c>UpdateCurrencyCount</c> recomputes it
        /// from every other line on the deal each frame, so a stepper here would be a control whose value is
        /// overwritten before the player let go of it. Vanilla makes it adjustable and then does exactly that.
        /// </summary>
        private void SilverRow(Rect rect, Columns columns, UIColorPaletteDef palette)
        {
            Tradeable currency = TradeSession.deal.CurrencyTradeable;

            if (currency == null)
                return;

            Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);

            Badge(new Rect(rect.x + columns.Badge, rect.y, BadgeWidth, rect.height), currency, palette);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                Widgets.LabelEllipses(new Rect(rect.x + columns.Name, rect.y, columns.NameWidth, rect.height),
                    ThingDefOf.Silver.LabelCap);

                if (columns.ShowCounts)
                {
                    Count(new Rect(rect.x + columns.Yours, rect.y, CountWidth, rect.height),
                        currency.CountHeldBy(Transactor.Colony), palette);

                    Count(new Rect(rect.x + columns.Theirs, rect.y, CountWidth, rect.height),
                        currency.CountHeldBy(Transactor.Trader), palette);
                }

                int delta = currency.CountToTransferToSource;

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = delta == 0 ? palette.TextDisabled : delta > 0 ? palette.Success : palette.Warning;

                Widgets.Label(new Rect(rect.x + columns.Stepper, rect.y, TradeStepper.Width, rect.height),
                    delta == 0 ? "--" : (delta > 0 ? "+" : string.Empty) + delta.ToStringCached());
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, (TipSignal)
                    "What this deal does to your silver. It follows the rest of the deal and cannot be set "
                    + "directly.");
            }
        }

        private void Row(Rect rect, Tradeable tradeable, int index, Columns columns, UIColorPaletteDef palette)
        {
            TradeShell.RowBackground(rect, index, tradeable.CountToTransfer != 0, palette);

            ThingDef def = tradeable.ThingDef;

            if (TradeStepper.Star(new Rect(rect.x, rect.y, StarWidth, rect.height),
                    TradeWants.IsFavourite(def), palette))
                TradeWants.ToggleFavourite(def);

            Badge(new Rect(rect.x + columns.Badge, rect.y, BadgeWidth, rect.height), tradeable, palette);

            Name(new Rect(rect.x + columns.Name, rect.y, columns.NameWidth, rect.height), tradeable, palette);

            if (columns.ShowCounts)
            {
                Count(new Rect(rect.x + columns.Yours, rect.y, CountWidth, rect.height),
                    tradeable.CountHeldBy(Transactor.Colony), palette);

                Count(new Rect(rect.x + columns.Theirs, rect.y, CountWidth, rect.height),
                    tradeable.CountHeldBy(Transactor.Trader), palette);
            }

            if (columns.Get(side))
                TradePriceCell.Compact(new Rect(rect.x + columns.YouGet, rect.y, PriceWidth, rect.height),
                    tradeable, TradeAction.PlayerSells, palette);

            if (columns.Pay(side))
                TradePriceCell.Compact(new Rect(rect.x + columns.YouPay, rect.y, PriceWidth, rect.height),
                    tradeable, TradeAction.PlayerBuys, palette);

            string refusal = TradeCatalog.RefusalFor(tradeable);

            if (refusal != null)
            {
                TradeStepper.Refused(new Rect(rect.x + columns.Stepper, rect.y, TradeStepper.Width, rect.height),
                    refusal, palette);

                return;
            }

            Rect stepper = new Rect(rect.x + columns.Stepper, rect.y + (rect.height - 24f) * 0.5f,
                TradeStepper.Width, 24f);

            if (!TradeStepper.Draw(stepper, tradeable, TradeCatalog.SignFor(side), palette))
                return;

            massDirty = true;

            // Once the player has moved a row themselves it is theirs, not a suggestion, so the standing want
            // mark comes off it.
            prefilled.Remove(tradeable);
        }

        /// <summary>
        /// The thing itself, at the left of its row.
        ///
        /// <b>An icon, not a letter.</b> The mockup drew a category initial here and it was the wrong call:
        /// RimWorld has artwork for every one of these, a player recognises a medicine bottle far faster than
        /// they decode an M, and the initials collide anyway -- Animals and Apparel both begin with A. Reported
        /// on 2026-08-25, looking at a column of R's.
        ///
        /// <b>The letter survives as the fallback and only that.</b> A tradeable with no drawable thing behind
        /// it is rare -- a royal favour has no item at all -- and an empty cell in a column of pictures reads as
        /// a missing texture rather than as a thing without one.
        /// </summary>
        private static void Badge(Rect rect, Tradeable tradeable, UIColorPaletteDef palette)
        {
            float size = Mathf.Min(rect.width, 24f);

            Rect box = new Rect(rect.x, rect.y + (rect.height - size) * 0.5f, size, size);

            Thing thing = UIGuard.Try("Trade.BadgeThing", () => tradeable?.AnyThing, null, null);

            // Vanilla's own icon drawing, which handles stuff colour, pawn portraits, minified buildings and
            // stack overlays. Anything drawn here by hand would be a second opinion about all four.
            if (thing != null && UIGuard.Try("Trade.BadgeIcon", () => Widgets.ThingIcon(box, thing)))
                return;

            Widgets.DrawBoxSolid(box, palette.SurfaceSunken);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                Widgets.Label(box, TradeCatalog.BadgeOf(tradeable));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }


        /// <summary>
        /// The name, what kind of thing it is, and any chip the row has earned.
        ///
        /// <b>Two lines in one row, and the second is where this window earns its place.</b> The subtitle says
        /// what the thing is and what it weighs -- or who a person is and what they are good at -- from facts the
        /// engine already holds and vanilla's list has nowhere to put.
        /// </summary>
        private void Name(Rect rect, Tradeable tradeable, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;

                Rect nameLine;
                Rect noteLine;

                TradeShell.TwoLine(rect, out nameLine, out noteLine);

                float pills = 0f;

                if (prefilled.Contains(tradeable))
                    pills += TradeShell.Pill(rect, rect.xMax, "standing", palette.Accent, palette);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = tradeable.TraderWillTrade ? palette.TextPrimary : palette.TextDisabled;

                Widgets.LabelEllipses(
                    new Rect(nameLine.x, nameLine.y, Mathf.Max(20f, nameLine.width - pills), nameLine.height),
                    tradeable.LabelCap);

                string note = TradeCatalog.Subtitle(tradeable);

                string need = TradeCatalog.NeedLine(tradeable, MapOf());

                // The subtitle says what the thing is; the need line says what you already have of it. Both are
                // useful and only one fits, so the holding is preferred whenever there is one -- it is the fact
                // that changes between one trade and the next.
                string second = need.NullOrEmpty() ? note : need;

                if (second.NullOrEmpty())
                    return;

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.LabelEllipses(noteLine, second);
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

            // Vanilla's own description for the thing, built lazily: it is the full info card text and a hovered
            // row asks for it many times a second.
            TooltipHandler.TipRegion(rect,
                new TipSignal(() => tradeable.TipDescription, tradeable.GetHashCode() * 251));

            if (Widgets.ButtonInvisible(rect))
                Find.WindowStack.Add(tradeable.NewInfoDialog);
        }

        private static void Count(Rect rect, int count, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = count > 0 ? palette.TextSecondary : palette.TextDisabled;

                Widgets.Label(rect, count > 0 ? count.ToStringCached() : "--");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// Who the trader is and where you stand with them, at the right of the title.
        ///
        /// <b>The goodwill is coloured and the rest is not,</b> because it is the only part that changes what you
        /// should do. A caravan from an ally will take a loss to keep you sweet; one from a faction you have been
        /// raiding will not, and that is worth knowing before reading a single price.
        /// </summary>
        private void Standing(Rect rect, float reserved, UIColorPaletteDef palette)
        {
            string detail = TraderDetail();

            if (detail.NullOrEmpty())
                return;

            Faction faction = UIGuard.Try("Trade.Standing", () => TradeSession.trader?.Faction, null, null);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperRight;

                GUI.color = faction == null
                    ? palette.TextSecondary
                    : faction.PlayerRelationKind == FactionRelationKind.Ally
                        ? palette.Success
                        : faction.HostileTo(Faction.OfPlayer)
                            ? palette.Danger
                            : palette.TextSecondary;

                // Ends where the switch begins. The caller measures that, because it is the one that knows how
                // many segments there are and whether there are any at all.
                float right = rect.xMax - reserved;
                float width = Mathf.Max(160f, (right - rect.x) * 0.55f);

                Widgets.LabelEllipses(new Rect(right - width, rect.y + 4f, width,
                    UIFonts.LineHeightOf(GameFont.Small)), detail);
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
        /// What the table says when it has nothing to show.
        ///
        /// <b>It says which of the two reasons applies,</b> because they need different actions: a filter that
        /// matched nothing is undone by clearing the box, and a trader who deals in nothing you have is undone by
        /// leaving. A single "no results" would leave the player clearing a search they had not typed.
        /// </summary>
        private void Empty(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                Widgets.Label(rect,
                    !Search.IsEmpty
                        ? "Nothing here matches \"" + Search.Text + "\"."
                        : category == TradeCatalog.Refused
                            ? "This trader refuses nothing you have."
                            : side == TradeSide.Buy
                                ? "Nothing here to buy."
                                : "Nothing here to offer.");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static Map MapOf()
        {
            return UIGuard.Try("Trade.Map", () => TradeSession.playerNegotiator?.MapHeld, null, null);
        }

        // ---------------------------------------------------------------------------------------
        // Spine
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The deal as an object: every committed line, both totals, and what the silver does.
        ///
        /// <b>This is the part vanilla has no equivalent of.</b> There, the deal exists only as nonzero numbers
        /// scattered through a list of two hundred rows plus a silver row that flashes when you overspend. Here
        /// it is a column you can read top to bottom before committing, and every line has a cross beside it.
        /// </summary>
        private void Spine(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Rect inner = rect.ContractedBy(10f);

            float width = GzpPalette.ContentWidth(inner);

            List<Tradeable> receiving = new List<Tradeable>();
            List<Tradeable> giving = new List<Tradeable>();

            float receiveTotal = 0f;
            float giveTotal = 0f;

            Lines(receiving, giving, ref receiveTotal, ref giveTotal);

            Tradeable currency = TradeSession.deal.CurrencyTradeable;

            int silverDelta = currency == null ? 0 : currency.CountToTransferToSource;

            if (silverDelta < 0)
                giveTotal += -silverDelta;
            else if (silverDelta > 0)
                receiveTotal += silverDelta;

            // Measured before drawing so the scroll view is given a true height. An over-estimate scrolls into
            // blank space under the content, which is the fault the framework's own scrollbar note warns about.
            float height = 26f
                           + Section(receiving, silverDelta > 0)
                           + Section(giving, silverDelta < 0)
                           + 150f;

            Rect view = new Rect(0f, 0f, width, height);

            Widgets.BeginScrollView(inner, ref spineScroll, view, false);

            float y = 0f;

            y = TradeShell.Heading(view, y, TradeSession.giftMode ? "THE GIFT" : "THE DEAL", palette);

            y = Group(view, y, "You receive", receiveTotal, receiving,
                silverDelta > 0 ? silverDelta : 0, palette, palette.Success);

            y = Group(view, y, TradeSession.giftMode ? "You give away" : "You give", giveTotal, giving,
                silverDelta < 0 ? -silverDelta : 0, palette, palette.Warning);

            y = Totals(view, y, currency, palette);

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(inner, view.height, ref spineScroll, ref spineDragging, ref spineDragOffset);
        }

        private static float Section(List<Tradeable> lines, bool silver)
        {
            return 30f + (lines.Count + (silver ? 1 : 0)) * 22f;
        }

        /// <summary>
        /// Splits the deal into what is coming and what is going, and totals both.
        ///
        /// <b>Read from <c>ActionToDo</c> rather than from the view.</b> A player who has queued a sale and then
        /// switched to the buy view has not un-queued it, and a spine that only showed the direction currently on
        /// screen would be hiding half the deal from the one place that exists to show all of it.
        /// </summary>
        private void Lines(List<Tradeable> receiving, List<Tradeable> giving, ref float receiveTotal,
            ref float giveTotal)
        {
            List<Tradeable> all = TradeSession.deal.AllTradeables;

            float receive = 0f;
            float give = 0f;

            for (int i = 0; all != null && i < all.Count; i++)
            {
                Tradeable tradeable = all[i];

                if (tradeable == null || tradeable.IsCurrency || tradeable.ActionToDo == TradeAction.None)
                    continue;

                if (tradeable.ActionToDo == TradeAction.PlayerBuys)
                {
                    receiving.Add(tradeable);

                    receive += Mathf.Abs(tradeable.CurTotalCurrencyCostForSource);
                }
                else
                {
                    giving.Add(tradeable);

                    give += Mathf.Abs(tradeable.CurTotalCurrencyCostForDestination);
                }
            }

            receiveTotal = receive;
            giveTotal = give;
        }

        private float Group(Rect view, float y, string label, float total, List<Tradeable> lines, int silver,
            UIColorPaletteDef palette, Color tone)
        {
            y = TradeShell.Readout(view, y, label,
                TradeSession.giftMode ? lines.Count + " lines" : total.ToStringMoney(), palette, tone);

            if (lines.Count == 0 && silver == 0)
            {
                return TradeShell.Readout(view, y, string.Empty, "nothing yet", palette, palette.TextDisabled,
                    GameFont.Tiny) + 6f;
            }

            for (int i = 0; i < lines.Count; i++)
                y = Line(view, y, lines[i], palette);

            if (silver > 0)
            {
                // The silver line carries no cross. It is not a choice the player made -- it is the sum of every
                // other line, recomputed by UpdateCurrencyCount each frame -- so a control offering to remove it
                // would be offering to break the arithmetic.
                y = TradeShell.Readout(view, y,
                    "    " + ThingDefOf.Silver.LabelCap + "  x" + silver.ToStringCached(),
                    ((float) silver).ToStringMoney(), palette, palette.TextSecondary, GameFont.Tiny);
            }

            return y + 6f;
        }

        private float Line(Rect view, float y, Tradeable tradeable, UIColorPaletteDef palette)
        {
            int count = Mathf.Abs(tradeable.CountToTransfer);

            float value = Mathf.Abs(tradeable.ActionToDo == TradeAction.PlayerBuys
                ? tradeable.CurTotalCurrencyCostForSource
                : tradeable.CurTotalCurrencyCostForDestination);

            // The readout is given a lane 18px short of the column so the cross has somewhere to sit that the
            // value cannot run into. Its own return is the row height, which is what keeps these lines packed at
            // the font's spacing rather than at a number written here.
            float next = TradeShell.Readout(new Rect(view.x, view.y, view.width - 18f, view.height), y,
                "    " + tradeable.LabelCap + "  x" + count.ToStringCached(), value.ToStringMoney(), palette,
                palette.TextSecondary, GameFont.Tiny);

            Rect row = new Rect(view.x, y, view.width, next - y);

            if (Mouse.IsOver(row))
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            if (TradeStepper.Glyph(new Rect(row.xMax - 16f, row.y + 2f, 16f, Mathf.Min(16f, row.height)), "x",
                    "Take this off the deal", palette))
            {
                UIGuard.Try("Trade.DropLine", () =>
                {
                    if (tradeable.CanAdjustTo(0).Accepted)
                        tradeable.AdjustTo(0);

                    prefilled.Remove(tradeable);

                    massDirty = true;
                }, "That line was not removed from the deal.");

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            return next;
        }

        /// <summary>
        /// What the deal leaves behind: the trader's funds, the colony's silver, and the caravan's load.
        ///
        /// <b>The trader's silver is stated rather than only enforced.</b> Vanilla checks it and then asks a
        /// confirmation question at the moment of accepting, which is the first the player hears of it. A number
        /// they can watch while building the deal turns that surprise into a decision.
        /// </summary>
        private float Totals(Rect view, float y, Tradeable currency, UIColorPaletteDef palette)
        {
            y = TradeShell.Heading(view, y, "AFTERWARDS", palette);

            if (currency != null && !TradeSession.giftMode)
            {
                int theirs = currency.CountPostDealFor(Transactor.Trader);

                // <b>The two silver figures moved to the balance strip and are not repeated here.</b> They are
                // the same numbers, and a panel restating what is written across the bottom of the same window in
                // twice the size is noise. What stays is the warning, because that is a conclusion drawn from
                // them rather than the figures themselves.
                if (theirs < 0)
                {
                    y = TradeShell.Note(view, y,
                        "They cannot cover this. Accepting will ask whether to go ahead anyway.", palette);
                }
                else
                {
                    y = TradeShell.Readout(view, y, "Silver moves",
                        (currency.CountToTransferToSource >= 0 ? "+" : string.Empty)
                        + currency.CountToTransferToSource.ToStringCached(), palette,
                        currency.CountToTransferToSource >= 0 ? palette.Success : palette.Warning);
                }
            }

            if (TradeSession.giftMode)
            {
                int goodwill = UIGuard.Try("Trade.Goodwill",
                    () => FactionGiftUtility.GetGoodwillChange(TradeSession.deal.AllTradeables,
                        TradeSession.trader.Faction), 0, null);

                y = TradeShell.Readout(view, y, "Goodwill", goodwill.ToStringWithSign(), palette,
                    goodwill > 0 ? palette.Success : goodwill < 0 ? palette.Danger : palette.TextSecondary);
            }

            if (!caravan)
                return y;

            Mass();

            bool over = massUsage > massCapacity;

            y = TradeShell.Readout(view, y, "Caravan load",
                massUsage.ToString("0") + " / " + massCapacity.ToString("0") + " kg", palette,
                over ? palette.Danger : palette.TextPrimary);

            if (over)
            {
                y = TradeShell.Note(view, y,
                    "Over capacity by " + (massUsage - massCapacity).ToString("0") + " kg. The caravan will be "
                    + "slowed until something comes off.", palette);
            }

            return y;
        }

        /// <summary>
        /// The caravan's load after this deal, from vanilla's own calculators.
        ///
        /// <b>Recomputed only when the deal moves,</b> for the reason vanilla keeps the same flags: these walk
        /// every pawn and every stack in the caravan, which is nothing once and far too much per frame.
        /// </summary>
        private void Mass()
        {
            if (!massDirty || caravanContents == null)
                return;

            massDirty = false;

            UIGuard.Try("Trade.CaravanMass", () =>
            {
                List<Tradeable> all = TradeSession.deal.AllTradeables;

                massUsage = CollectionsMassCalculator.MassUsageLeftAfterTradeableTransfer(caravanContents, all,
                    IgnorePawnsInventoryMode.Ignore);

                massCapacity = CollectionsMassCalculator.CapacityLeftAfterTradeableTransfer(caravanContents, all,
                    null);
            }, "The caravan's load could not be worked out for this deal. The trade itself is unaffected.");
        }

        // ---------------------------------------------------------------------------------------
        // Footer
        // ---------------------------------------------------------------------------------------

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            float x = rect.x;

            x += TradeShell.KeyHint(rect, x, "Click", "info card", palette);
            x += TradeShell.KeyHint(rect, x, "★", "favourite", palette);

            TradeShell.KeyHint(rect, x, "all", "take everything", palette);

            // Vanilla's own button for what this trader will buy, kept because it answers a question our sell
            // view raises rather than settles: the list here is what you hold that they will take, and this is
            // the whole of what they would take. Placed clear of the three footer buttons, whose widths and gap
            // are TradeShell's and are restated here rather than guessed.
            Rect sellable = new Rect(rect.xMax - 3f * 148f - 2f * 8f - 42f,
                rect.y + (rect.height - 30f) * 0.5f, 32f, 30f);

            if (GzpPalette.IconButton(sellable, TexButton.Info, "CommandShowSellableItemsDesc".Translate()))
                Find.WindowStack.Add(new Dialog_SellableItems(TradeSession.trader));

            TradeShell.Footer(rect, palette, CommitLabel(), true, Accept, Reset, () => Close());
        }

        private string CommitLabel()
        {
            if (!TradeSession.giftMode)
                return "AcceptButton".Translate();

            int goodwill = UIGuard.Try("Trade.GiftLabel",
                () => FactionGiftUtility.GetGoodwillChange(TradeSession.deal.AllTradeables,
                    TradeSession.trader.Faction), 0, null);

            return "OfferGifts".Translate() + " (" + goodwill.ToStringWithSign() + ")";
        }

        private void Reset()
        {
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();

            UIGuard.Try("Trade.Reset", () =>
            {
                TradeSession.deal.Reset();

                // Dropped with the deal: every Tradeable is a new object now, so a box keyed on an old one would
                // be a box nothing can reach, and the memory would be the least of it.
                TradeStepper.Forget();

                // Reset means empty, not "back to what the standing wants suggested". A player pressing this is
                // clearing the board, and re-filling it would leave them no way to reach a genuinely empty deal.
                prefilled = new HashSet<Tradeable>();

                massDirty = true;
            }, "The deal was not reset.");
        }

        /// <summary>
        /// Whether the colony can pay for this deal.
        ///
        /// <b>This mirrors <c>TradeDeal.TryExecute</c>'s own precondition, deliberately and exactly.</b> That
        /// method answers an unaffordable deal with
        /// <c>Find.WindowStack.WindowOfType&lt;Dialog_Trade&gt;().FlashSilver()</c>, which is an unguarded
        /// dereference: with our window in the stack instead of vanilla's, that call finds null and throws in the
        /// middle of executing a trade. So the state is refused here, before the call, and the player gets the
        /// same message vanilla would have shown them.
        ///
        /// It is the one check in this feature that is reproduced rather than delegated, and the reason it is
        /// reproduced is so that everything below it can be delegated safely.
        /// </summary>
        private static bool Affordable()
        {
            return UIGuard.Try("Trade.Affordable", () =>
            {
                if (TradeSession.giftMode)
                    return true;

                Tradeable currency = TradeSession.deal.CurrencyTradeable;

                return currency != null && currency.CountPostDealFor(Transactor.Colony) >= 0;
            }, false, null);
        }

        private void Accept()
        {
            if (!Affordable())
            {
                Flash();

                SoundDefOf.ClickReject.PlayOneShotOnCamera();

                Messages.Message("MessageColonyCannotAfford".Translate(), MessageTypeDefOf.RejectInput, false);

                return;
            }

            Action execute = () => UIGuard.Try("Trade.Execute", Execute,
                "The trade did not go through. Nothing has changed hands.");

            if (TradeSession.deal.DoesTraderHaveEnoughSilver())
            {
                execute();
            }
            else
            {
                // Vanilla's own sequence: flash the silver, reject-click, and ask. A trader short of funds is a
                // deal that still can go ahead, at a price, and taking that choice away would be ours to answer
                // for.
                Flash();

                SoundDefOf.ClickReject.PlayOneShotOnCamera();

                Find.WindowStack.Add(
                    Dialog_MessageBox.CreateConfirmation("ConfirmTraderShortFunds".Translate(), execute));
            }

            if (Event.current != null)
                Event.current.Use();
        }

        private void Execute()
        {
            bool traded;

            if (!TradeSession.deal.TryExecute(out traded))
                return;

            if (!traded)
            {
                Close();

                return;
            }

            SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();

            RimWorld.Planet.Caravan playerCaravan = TradeSession.playerNegotiator?.GetCaravan();

            if (playerCaravan != null)
                playerCaravan.RecacheInventory();

            Close(false);
        }

        /// <summary>
        /// Marks the silver as the thing that went wrong.
        ///
        /// <b>Written to vanilla's own static,</b> which is public and is what <c>TradeUI</c> reads to flash its
        /// currency row. Ours reads the same field, so a mod still drawing a vanilla row somewhere in this session
        /// flashes in step with us rather than a second apart.
        /// </summary>
        private static void Flash()
        {
            Dialog_Trade.lastCurrencyFlashTime = Time.time;
        }
    }
}
