using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using Gideon.UIOverhaul.Features.Trade.Shell;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Trade.Caravan
{
    /// <summary>
    /// The caravan packing screen: the same rail, table and spine as the trade window, with mass and days in
    /// place of silver.
    ///
    /// <b>The question this screen has to answer is "will this arrive".</b> Vanilla answers it with one number
    /// that turns red, and leaves the player to find the cause across three tabs and nine collapsed sections. So
    /// the manifest stands beside the table with the whole projection in it -- arrival, travel time, food, what
    /// the route forages, visibility, the load -- and the row that put the caravan over capacity says so on
    /// itself rather than being one of ninety rows you have to go and find.
    ///
    /// <b>The state is entirely vanilla's.</b> This draws over RimWorld's own dialog rather than replacing it;
    /// see <see cref="CaravanReflection"/> for why that is the right way round here and was not for trading.
    /// Mass, capacity, speed, food, foraging, visibility and the arrival estimate are its cached getters; the
    /// send is its <c>TrySend</c>, with its own error checks and its own confirmation dialogs; every count change
    /// goes through <c>AdjustTo</c> and is followed by its <c>Notify_TransferablesChanged</c>.
    ///
    /// <b>Static, not a window.</b> There is no second window -- the instance in the stack is RimWorld's, and
    /// this is the body a prefix draws into it. What little state a screen needs is keyed on that instance, so
    /// two caravans formed in one session do not share a scroll position.
    /// </summary>
    internal static class CaravanScreen
    {
        // Internal rather than private because the split-caravan screen files rows exactly the same way, and two
        // definitions of "what counts as an animal" would eventually disagree about a colony mech.
        internal const string All = "all";
        internal const string Colonists = "colonists";
        internal const string Animals = "animals";
        internal const string Items = "items";
        internal const string Supplies = "supplies";

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        /// <summary>
        /// What the screen remembers between frames, per dialog instance.
        ///
        /// <b>Keyed on the dialog rather than held flat,</b> because a player can reform one caravan, cancel, and
        /// form another in the same session -- and a scroll position or a category carried across from the last
        /// one would put them somewhere they did not ask to be. The entry is dropped when a window closes.
        /// </summary>
        private class Screen
        {
            internal string Category = All;

            internal Vector2 RailScroll;
            internal bool RailDragging;
            internal float RailDragOffset;

            internal Vector2 TableScroll;
            internal bool TableDragging;
            internal float TableDragOffset;

            internal Vector2 SpineScroll;
            internal bool SpineDragging;
            internal float SpineDragOffset;

            /// <summary>The row that took the caravan past its capacity, if one did. See <see cref="Blame"/>.</summary>
            internal TransferableOneWay Culprit;
        }

        private static readonly Dictionary<Dialog_FormCaravan, Screen> Screens =
            new Dictionary<Dialog_FormCaravan, Screen>();

        private static readonly List<TransferableOneWay> Rows = new List<TransferableOneWay>();
        private static readonly List<TradeRailEntry> Rail = new List<TradeRailEntry>();

        /// <summary>Drops a closed window's state. Called from the patch's own close hook.</summary>
        internal static void Forget(Dialog_FormCaravan dialog)
        {
            if (dialog != null)
                Screens.Remove(dialog);
        }

        private static Screen StateOf(Dialog_FormCaravan dialog)
        {
            Screen screen;

            if (Screens.TryGetValue(dialog, out screen))
                return screen;

            screen = new Screen();

            Screens[dialog] = screen;

            return screen;
        }

        internal static void Draw(Dialog_FormCaravan dialog, Rect inRect)
        {
            TradeShell.Guarded("Caravan.Window", inRect, () => Contents(dialog, inRect),
                "The caravan window failed to draw. Nothing has been sent. Close it and start again, or switch "
                + "the window off under Additional Features to use RimWorld's own.");
        }

        private static void Contents(Dialog_FormCaravan dialog, Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Screen screen = StateOf(dialog);

            // The dialog's own margin is zero, so the padding a window normally gets has to come from here.
            inRect = inRect.ContractedBy(16f);

            Rect headerRect;
            Rect railRect;
            Rect tableRect;
            Rect spineRect;
            Rect footerRect;

            TradeShell.Layout(inRect, true, true, out headerRect, out railRect, out tableRect, out spineRect,
                out footerRect);

            bool reform = CaravanReflection.Reform(dialog);

            TradeShell.Header(headerRect, (reform ? "ReformCaravan" : "FormCaravan").Translate(),
                Detail(dialog), palette);

            DrawRail(dialog, screen, railRect, palette);
            Table(dialog, screen, tableRect, palette);
            Spine(dialog, screen, spineRect, palette);
            Footer(dialog, screen, footerRect, palette);
        }

        private static string Detail(Dialog_FormCaravan dialog)
        {
            if (!CaravanReflection.HasDestination(dialog))
            {
                return CaravanReflection.CanChooseRoute(dialog)
                    ? "No destination chosen yet"
                    : "Destination already set";
            }

            float days = CaravanReflection.TicksToArrive(dialog) / 60000f;

            return "About " + days.ToString("0.#") + " days to arrive · everything below is judged against that "
                   + "journey";
        }

        // ---------------------------------------------------------------------------------------

        private static void DrawRail(Dialog_FormCaravan dialog, Screen screen, Rect rect,
            UIColorPaletteDef palette)
        {
            List<TransferableOneWay> all = dialog.transferables;

            Rail.Clear();

            Rail.Add(TradeRailEntry.Of(All, "Everything", Count(all, All)));

            Rail.Add(TradeRailEntry.Group("Manifest"));
            Rail.Add(TradeRailEntry.Of(Colonists, "Colonists", Count(all, Colonists)));
            Rail.Add(TradeRailEntry.Of(Animals, "Animals", Count(all, Animals)));
            Rail.Add(TradeRailEntry.Of(Items, "Items", Count(all, Items)));
            Rail.Add(TradeRailEntry.Of(Supplies, "Food and medicine", Count(all, Supplies)));

            string picked = TradeRail.Draw(rect, Rail, screen.Category, ref screen.RailScroll,
                ref screen.RailDragging, ref screen.RailDragOffset, palette);

            if (picked == null)
                return;

            screen.Category = picked;
            screen.TableScroll = Vector2.zero;
        }

        /// <summary>
        /// How many rows a category holds, counting what is available rather than what is taken.
        ///
        /// A rail whose numbers moved as the player packed would be reporting their own clicks back at them. The
        /// manifest is where what is taken is counted.
        /// </summary>
        internal static int Count(List<TransferableOneWay> all, string category)
        {
            int count = 0;

            for (int i = 0; all != null && i < all.Count; i++)
            {
                if (Matches(all[i], category))
                    count++;
            }

            return count;
        }

        /// <summary>Whether a row belongs to a category. Shared with the split screen; see the constants above.</summary>
        internal static bool Matches(TransferableOneWay transferable, string category)
        {
            if (transferable == null || !transferable.HasAnyThing)
                return false;

            if (category == All)
                return true;

            Pawn pawn = transferable.AnyThing as Pawn;

            if (category == Colonists)
                return pawn != null && !pawn.RaceProps.Animal;

            if (category == Animals)
                return pawn != null && pawn.RaceProps.Animal;

            ThingDef def = transferable.ThingDef;

            if (def == null)
                return false;

            bool supply = def.IsMedicine || def.IsNutritionGivingIngestible;

            if (category == Supplies)
                return pawn == null && supply;

            return pawn == null;
        }

        // ---------------------------------------------------------------------------------------

        private const float MassWidth = 70f;
        private const float ValueWidth = 72f;
        private const float RouteWidth = 168f;
        private const float LeftWidth = 62f;
        private const float CellGap = 6f;

        private static void Table(Dialog_FormCaravan dialog, Screen screen, Rect rect, UIColorPaletteDef palette)
        {
            Search.Draw(new Rect(rect.x, rect.y, Mathf.Min(320f, rect.width), 28f), palette);

            float top = rect.y + 36f;

            float width = GzpPalette.ContentWidth(rect);

            float fixedWidth = MassWidth + ValueWidth + RouteWidth + TradeStepper.Width + LeftWidth + CellGap * 5f;

            float nameWidth = Mathf.Max(120f, width - fixedWidth);

            Captions(new Rect(rect.x, top, width, TradeShell.ColumnsHeight), nameWidth, palette);

            top += TradeShell.ColumnsHeight;

            Collect(dialog, screen);

            float tripDays = CaravanReflection.TicksToArrive(dialog) / 60000f;

            // The compact height: these rows are one line, unlike the trade table's.
            float rowHeight = TradeShell.CompactRowHeight;

            Rect list = new Rect(rect.x, top, rect.width, Mathf.Max(0f, rect.yMax - top));
            Rect view = new Rect(0f, 0f, width, Rows.Count * rowHeight + 2f);

            Widgets.BeginScrollView(list, ref screen.TableScroll, view, false);

            float first = screen.TableScroll.y - rowHeight;
            float last = screen.TableScroll.y + list.height;

            for (int i = 0; i < Rows.Count; i++)
            {
                float y = i * rowHeight;

                if (y < first || y > last)
                    continue;

                Row(dialog, screen, new Rect(0f, y, view.width, rowHeight), Rows[i], i, nameWidth, tripDays,
                    palette);
            }

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(list, view.height, ref screen.TableScroll, ref screen.TableDragging,
                ref screen.TableDragOffset);
        }

        private static void Collect(Dialog_FormCaravan dialog, Screen screen)
        {
            Rows.Clear();

            List<TransferableOneWay> all = dialog.transferables;

            for (int i = 0; all != null && i < all.Count; i++)
            {
                TransferableOneWay transferable = all[i];

                if (!Matches(transferable, screen.Category))
                    continue;

                if (!Search.IsEmpty && !Search.Matches(transferable.Label))
                    continue;

                Rows.Add(transferable);
            }

            // Anything being taken first, then by label. Same rule as the trade table and for the same reason:
            // a row you have committed to is the one you may want to change.
            Rows.Sort((left, right) =>
            {
                bool takenLeft = left.CountToTransfer > 0;
                bool takenRight = right.CountToTransfer > 0;

                if (takenLeft != takenRight)
                    return takenLeft ? -1 : 1;

                return string.Compare(left.Label ?? string.Empty, right.Label ?? string.Empty,
                    System.StringComparison.CurrentCultureIgnoreCase);
            });
        }

        private static void Captions(Rect rect, float nameWidth, UIColorPaletteDef palette)
        {
            float x = rect.x;

            TradeShell.Column(new Rect(x, rect.y, nameWidth, rect.height), "Item", palette);

            x += nameWidth + CellGap;

            TradeShell.Column(new Rect(x, rect.y, MassWidth, rect.height), "Mass", palette,
                TextAnchor.MiddleRight);

            x += MassWidth + CellGap;

            TradeShell.Column(new Rect(x, rect.y, ValueWidth, rect.height), "Value", palette,
                TextAnchor.MiddleRight);

            x += ValueWidth + CellGap;

            TradeShell.Column(new Rect(x, rect.y, RouteWidth, rect.height), "Route", palette);

            x += RouteWidth + CellGap;

            TradeShell.Column(new Rect(x, rect.y, TradeStepper.Width, rect.height), "Take", palette);

            x += TradeStepper.Width + CellGap;

            TradeShell.Column(new Rect(x, rect.y, LeftWidth, rect.height), "Left", palette,
                TextAnchor.MiddleRight);

            GUI.color = palette.Border;
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            GUI.color = Color.white;
        }

        private static void Row(Dialog_FormCaravan dialog, Screen screen, Rect rect,
            TransferableOneWay transferable, int index, float nameWidth, float tripDays,
            UIColorPaletteDef palette)
        {
            bool taken = transferable.CountToTransfer > 0;
            bool culprit = screen.Culprit == transferable;

            TradeShell.RowBackground(rect, index, taken, palette);

            if (culprit)
            {
                // The row that broke the budget, marked where the problem is rather than only totalled at the
                // bottom. Vanilla computes exactly this threshold and then spends it only on stopping the arrow
                // buttons.
                Widgets.DrawBoxSolid(rect,
                    new Color(palette.Danger.r, palette.Danger.g, palette.Danger.b, 0.14f));
            }

            float x = rect.x;

            Name(new Rect(x, rect.y, nameWidth, rect.height), transferable, palette);

            x += nameWidth + CellGap;

            CaravanVerdict verdict = CaravanRoute.For(transferable, tripDays);

            Right(new Rect(x, rect.y, MassWidth, rect.height),
                CaravanRoute.MassOf(transferable).ToString("0.#") + " kg",
                taken ? palette.TextSecondary : palette.TextDisabled, palette);

            x += MassWidth + CellGap;

            Right(new Rect(x, rect.y, ValueWidth, rect.height), CaravanRoute.ValueOf(transferable).ToStringMoney(),
                taken ? palette.TextSecondary : palette.TextDisabled, palette);

            x += ValueWidth + CellGap;

            Route(new Rect(x, rect.y, RouteWidth, rect.height), verdict, palette);

            x += RouteWidth + CellGap;

            if (transferable.Interactive)
            {
                Rect stepper = new Rect(x, rect.y + (rect.height - 24f) * 0.5f, TradeStepper.Width, 24f);

                // Always +1: TransferableOneWay counts towards its destination, so the number the player types is
                // the number that travels. There is no other direction to read it in.
                if (TradeStepper.Draw(stepper, transferable, 1, palette))
                {
                    CaravanReflection.NotifyChanged(dialog);

                    Blame(dialog, screen, transferable);
                }
            }
            else
            {
                TradeStepper.Refused(new Rect(x, rect.y, TradeStepper.Width, rect.height), "Fixed", palette);
            }

            x += TradeStepper.Width + CellGap;

            int left = transferable.MaxCount - Mathf.Max(0, transferable.CountToTransfer);

            Right(new Rect(x, rect.y, LeftWidth, rect.height), left > 0 ? left.ToStringCached() : "--",
                left > 0 ? palette.TextDisabled : palette.Warning, palette);
        }

        /// <summary>
        /// Remembers which row put the caravan over its capacity.
        ///
        /// <b>The row that crossed the line, not the heaviest row.</b> Blaming the heaviest would point at the
        /// colonists on almost every caravan, which is true and useless; the one just added is the one the player
        /// can undo. Cleared as soon as the load is under capacity again, so the mark disappears when the problem
        /// does.
        /// </summary>
        private static void Blame(Dialog_FormCaravan dialog, Screen screen, TransferableOneWay transferable)
        {
            if (dialog.MassUsage > dialog.MassCapacity)
            {
                if (screen.Culprit == null)
                    screen.Culprit = transferable;

                return;
            }

            screen.Culprit = null;
        }

        private static void Name(Rect rect, TransferableOneWay transferable, UIColorPaletteDef palette)
        {
            float icon = Mathf.Min(rect.height - 6f, 26f);

            UIGuard.Try("Caravan.RowIcon", () =>
            {
                Thing thing = transferable.AnyThing;

                if (thing != null)
                    Widgets.ThingIcon(new Rect(rect.x, rect.y + (rect.height - icon) * 0.5f, icon, icon), thing);
            }, null);

            float x = rect.x + icon + 6f;
            float width = Mathf.Max(20f, rect.xMax - x);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                Widgets.LabelEllipses(new Rect(x, rect.y, width, rect.height), transferable.LabelCap);
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect,
                    new TipSignal(() => transferable.TipDescription, transferable.GetHashCode() * 419));
        }

        private static void Route(Rect rect, CaravanVerdict verdict, UIColorPaletteDef palette)
        {
            if (verdict.Text.NullOrEmpty())
                return;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = verdict.Tone(palette);

                Widgets.LabelEllipses(rect, verdict.Text);
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static void Right(Rect rect, string text, Color color, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = color;

                Widgets.Label(rect, text);
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The manifest: whether this arrives, and what it costs to get there.
        ///
        /// <b>The arrival verdict comes first, because it is the question.</b> Everything under it is the working
        /// -- the load, the speed, the food and the clock -- laid out so a no is traceable to the line that
        /// caused it rather than to a total that turned red.
        /// </summary>
        private static void Spine(Dialog_FormCaravan dialog, Screen screen, Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Rect inner = rect.ContractedBy(10f);

            float width = GzpPalette.ContentWidth(inner);

            Rect view = new Rect(0f, 0f, width, 480f);

            Widgets.BeginScrollView(inner, ref screen.SpineScroll, view, false);

            float y = 0f;

            y = TradeShell.Heading(view, y, "MANIFEST", palette);

            float usage = dialog.MassUsage;
            float capacity = dialog.MassCapacity;

            bool over = usage > capacity;
            bool destination = CaravanReflection.HasDestination(dialog);

            y = TradeShell.Readout(view, y, "Will arrive",
                !destination ? "no route yet" : over ? "no" : "yes", palette,
                !destination ? palette.TextDisabled : over ? palette.Danger : palette.Success);

            if (destination)
            {
                y = TradeShell.Readout(view, y, "Travel time",
                    (CaravanReflection.TicksToArrive(dialog) / 60000f).ToString("0.#") + " days", palette);
            }

            y += 4f;

            y = TradeShell.Heading(view, y, "LOAD", palette);

            y = TradeShell.Readout(view, y, "Mass",
                usage.ToString("0.#") + " / " + capacity.ToString("0.#") + " kg", palette,
                over ? palette.Danger : palette.TextPrimary);

            float fraction = capacity <= 0f ? 1f : Mathf.Clamp01(usage / capacity);

            GzpPalette.Bar(new Rect(view.x, y, view.width, 6f), fraction,
                over ? palette.Danger : fraction > 0.85f ? palette.Warning : palette.Accent);

            y += 12f;

            if (over)
            {
                y = TradeShell.Note(view, y,
                    "Over by " + (usage - capacity).ToString("0.#") + " kg. Take something off, or add a pack "
                    + "animal.", palette);
            }

            y = TradeShell.Readout(view, y, "Speed",
                CaravanReflection.TilesPerDay(dialog).ToString("0.#") + " tiles / day", palette,
                palette.TextSecondary);

            y = TradeShell.Readout(view, y, "Visibility",
                CaravanReflection.Visibility(dialog).ToStringPercent(), palette, palette.TextSecondary);

            y += 4f;

            y = TradeShell.Heading(view, y, "SUPPLIES", palette);

            y = Food(dialog, view, y, palette);

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(inner, view.height, ref screen.SpineScroll, ref screen.SpineDragging,
                ref screen.SpineDragOffset);
        }

        /// <summary>
        /// Food aboard, against how long the journey is and how long the food lasts.
        ///
        /// <b>Three numbers that only mean anything together.</b> Nine days of food is fine for a six day trip
        /// and useless if the first of it spoils on day three, and neither of those matters if the route forages
        /// enough to live on. Vanilla computes all three and reports them in three different places.
        /// </summary>
        private static float Food(Dialog_FormCaravan dialog, Rect view, float y, UIColorPaletteDef palette)
        {
            float days;
            float tillRot;

            CaravanReflection.Food(dialog, out days, out tillRot);

            float trip = CaravanReflection.TicksToArrive(dialog) / 60000f;

            bool enough = trip <= 0f || days >= trip;

            y = TradeShell.Readout(view, y, "Food aboard", days.ToString("0.#") + " days", palette,
                enough ? palette.TextPrimary : palette.Danger);

            if (tillRot > 0f && tillRot < days)
            {
                y = TradeShell.Readout(view, y, "First spoilage", tillRot.ToString("0.#") + " days", palette,
                    trip > 0f && tillRot < trip ? palette.Danger : palette.Warning);
            }

            ThingDef foraged;
            float perDay;

            CaravanReflection.Foraged(dialog, out foraged, out perDay);

            if (perDay > 0f)
            {
                y = TradeShell.Readout(view, y, "Foraged",
                    "+" + perDay.ToString("0.##") + " / day"
                    + (foraged != null ? " · " + foraged.label : string.Empty), palette, palette.Success,
                    GameFont.Tiny);
            }

            bool auto = CaravanReflection.AutoSelectSupplies(dialog);

            bool changed = auto;

            if (UICheckboxControl.Draw(new Rect(view.x, y + 4f, view.width, 26f), ref changed, palette,
                    "AutomaticallySelectTravelSupplies".Translate()) && changed != auto)
                CaravanReflection.SetAutoSelectSupplies(dialog, changed);

            y += 32f;

            if (!enough)
            {
                y = TradeShell.Note(view, y,
                    "Not enough food for the journey as it stands. The estimate is rough and moves with who is "
                    + "aboard, but it is short by more than rounding.", palette);
            }

            return y;
        }

        // ---------------------------------------------------------------------------------------

        private static void Footer(Dialog_FormCaravan dialog, Screen screen, Rect rect,
            UIColorPaletteDef palette)
        {
            float x = rect.x;

            if (CaravanReflection.CanChooseRoute(dialog))
            {
                if (UIActionButtonControl.Draw(new Rect(x, rect.y + (rect.height - 34f) * 0.5f, 148f, 34f),
                        "ChangeRouteButton".Translate(), palette))
                {
                    // Vanilla plays its close sound here even though the window stays: the route planner takes
                    // over the screen, so it reads as leaving.
                    SoundDefOf.CommsWindow_Close.PlayOneShotOnCamera();

                    UIGuard.Try("Caravan.RoutePlanner", () => Find.WorldRoutePlanner.Start(dialog),
                        "The route planner did not open.");
                }

                x += 156f;
            }

            if (Prefs.DevMode && CaravanReflection.CanSendEverything
                && UIActionButtonControl.Draw(new Rect(x, rect.y + (rect.height - 34f) * 0.5f, 148f, 34f),
                    "DEV: take all", palette))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();

                CaravanReflection.SendEverything(dialog);
                CaravanReflection.NotifyChanged(dialog);
            }

            bool over = dialog.MassUsage > dialog.MassCapacity;

            // <b>The button says what will happen.</b> Over capacity it reads "Fix the load" and does nothing,
            // rather than staying pressable and handing the player one of vanilla's error dialogs. A route that
            // has not been chosen is left to TrySend, which knows how to ask for one.
            TradeShell.Footer(rect, palette,
                over ? "Fix the load" : "Send".Translate(), !over,
                () => CaravanReflection.TrySend(dialog),
                () =>
                {
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();

                    CaravanReflection.Recache(dialog);

                    screen.Culprit = null;
                },
                CaravanReflection.ShowCancelButton(dialog) ? (System.Action) (() => dialog.Close()) : null);
        }
    }
}
