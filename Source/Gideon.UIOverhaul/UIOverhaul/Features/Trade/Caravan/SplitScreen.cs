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
using PlanetCaravan = RimWorld.Planet.Caravan;

namespace Gideon.UIOverhaul.Features.Trade.Caravan
{
    /// <summary>
    /// Splitting a caravan: the same rail and table, and a spine holding both halves side by side.
    ///
    /// <b>This is the screen the shell suits best, because the question is a comparison.</b> Splitting is not
    /// "what am I taking" -- it is "what does each of these two groups look like afterwards", and both answers
    /// matter at once. Vanilla computes every number for both halves already: mass, capacity, speed, days of
    /// food, spoilage, foraging and visibility all exist twice on the dialog. It then draws them as two rows of
    /// small numbers above a tab strip, so the player reads one, switches tab, and holds the other in their head.
    /// Here they stand in two columns and the comparison is just there.
    ///
    /// <b>The two colonist rules are shown as they are about to be broken, not after.</b> A split needs one
    /// non-downed owner in each half, and vanilla says so with a rejection message once you press accept. Both
    /// counts sit on the spine while you pack, and the button says <c>Fix the split</c> rather than staying
    /// pressable -- but <c>TrySplitCaravan</c> still runs its own checks, so what is enforced is RimWorld's and
    /// what is shown is a warning that arrives early.
    ///
    /// <b>Nothing here performs a split.</b> The window in the stack is RimWorld's, the rows are its own
    /// transferables, and the button calls its <c>TrySplitCaravan</c>. See <see cref="SplitReflection"/>.
    /// </summary>
    internal static class SplitScreen
    {
        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private class Screen
        {
            internal string Category = CaravanScreen.All;

            internal Vector2 RailScroll;
            internal bool RailDragging;
            internal float RailDragOffset;

            internal Vector2 TableScroll;
            internal bool TableDragging;
            internal float TableDragOffset;

            internal Vector2 SpineScroll;
            internal bool SpineDragging;
            internal float SpineDragOffset;
        }

        private static readonly Dictionary<Dialog_SplitCaravan, Screen> Screens =
            new Dictionary<Dialog_SplitCaravan, Screen>();

        private static readonly List<TransferableOneWay> Rows = new List<TransferableOneWay>();
        private static readonly List<TradeRailEntry> Rail = new List<TradeRailEntry>();

        /// <summary>
        /// The state for this window, dropping any belonging to windows that have since closed.
        ///
        /// <b>Pruned here rather than on a close patch, and that is a correctness decision rather than a tidy
        /// one.</b> The form dialog overrides <c>PostClose</c>, so patching it there names that window's own
        /// method. <c>Dialog_SplitCaravan</c> does not override it -- so the same annotation would resolve to
        /// <c>Window.PostClose</c> and fire for every window in the game, or fail to resolve and take the mod's
        /// whole <c>PatchAll</c> down with it. Neither is a risk worth running for a scroll position.
        ///
        /// The sweep costs a window-stack lookup per entry per frame, and the dictionary holds one entry in
        /// almost every real session: a player has one split window open, or none.
        /// </summary>
        private static Screen StateOf(Dialog_SplitCaravan dialog)
        {
            Screen screen;

            if (Screens.TryGetValue(dialog, out screen))
                return screen;

            UIGuard.Try("Split.Prune", () =>
            {
                List<Dialog_SplitCaravan> stale = null;

                foreach (KeyValuePair<Dialog_SplitCaravan, Screen> entry in Screens)
                {
                    if (entry.Key != null && Find.WindowStack.IsOpen(entry.Key))
                        continue;

                    if (stale == null)
                        stale = new List<Dialog_SplitCaravan>();

                    stale.Add(entry.Key);
                }

                // Collected first and removed after, because a dictionary cannot be written to while it is being
                // walked.
                for (int i = 0; stale != null && i < stale.Count; i++)
                    Screens.Remove(stale[i]);
            }, null);

            screen = new Screen();

            Screens[dialog] = screen;

            return screen;
        }

        internal static void Draw(Dialog_SplitCaravan dialog, Rect inRect)
        {
            TradeShell.Guarded("Split.Window", inRect, () => Contents(dialog, inRect),
                "The split-caravan window failed to draw. Nothing has been split. Close it and try again, or "
                + "switch the window off under Additional Features to use RimWorld's own.");
        }

        private static void Contents(Dialog_SplitCaravan dialog, Rect inRect)
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

            List<TransferableOneWay> all = SplitReflection.Transferables(dialog);

            TradeShell.Header(headerRect, "SplitCaravan".Translate(), Detail(dialog, all), palette);

            DrawRail(screen, all, railRect, palette);
            Table(dialog, screen, all, tableRect, palette);
            Spine(dialog, screen, all, spineRect, palette);
            Footer(dialog, all, footerRect, palette);
        }

        private static string Detail(Dialog_SplitCaravan dialog, List<TransferableOneWay> all)
        {
            PlanetCaravan caravan = SplitReflection.Caravan(dialog);

            string name = caravan != null ? caravan.LabelCap : "Caravan";

            int going = Owners(all, true);
            int staying = Owners(all, false);

            string line = name + " · " + staying + " staying, " + going + " going";

            int ticks = SplitReflection.TicksToArrive(dialog);

            if (ticks > 0)
                line += " · " + (ticks / 60000f).ToString("0.#") + " days left on the road";

            return line;
        }

        /// <summary>
        /// How many non-downed colonists end up on one side of the split.
        ///
        /// <b>RimWorld's own definition of who counts, asked directly.</b> <c>CaravanUtility.IsOwner</c> plus
        /// not-downed is exactly the test <c>CheckForErrors</c> makes, so the two numbers on the spine and the
        /// rejection the accept button would have produced cannot disagree. This is a display of the rule, not a
        /// second copy of it: the enforcement stays inside <c>TrySplitCaravan</c>.
        /// </summary>
        private static int Owners(List<TransferableOneWay> all, bool going)
        {
            return UIGuard.Try("Split.Owners", () =>
            {
                int count = 0;

                for (int i = 0; all != null && i < all.Count; i++)
                {
                    TransferableOneWay transferable = all[i];

                    if (transferable == null || transferable.things == null)
                        continue;

                    int taken = Mathf.Max(0, transferable.CountToTransfer);

                    for (int t = 0; t < transferable.things.Count; t++)
                    {
                        Pawn pawn = transferable.things[t] as Pawn;

                        if (pawn == null || pawn.Downed || !CaravanUtility.IsOwner(pawn, Faction.OfPlayer))
                            continue;

                        // A pawn row transfers whole pawns, so the first N things in the row are the ones going.
                        // Counting by position is how TransferableUtility.GetPawnsFromTransferables reads it too.
                        bool leaves = t < taken;

                        if (leaves == going)
                            count++;
                    }
                }

                return count;
            }, 0, null);
        }

        // ---------------------------------------------------------------------------------------

        private static void DrawRail(Screen screen, List<TransferableOneWay> all, Rect rect,
            UIColorPaletteDef palette)
        {
            Rail.Clear();

            Rail.Add(TradeRailEntry.Of(CaravanScreen.All, "Everything",
                CaravanScreen.Count(all, CaravanScreen.All)));

            Rail.Add(TradeRailEntry.Group("The caravan"));
            Rail.Add(TradeRailEntry.Of(CaravanScreen.Colonists, "Colonists",
                CaravanScreen.Count(all, CaravanScreen.Colonists)));
            Rail.Add(TradeRailEntry.Of(CaravanScreen.Animals, "Animals",
                CaravanScreen.Count(all, CaravanScreen.Animals)));
            Rail.Add(TradeRailEntry.Of(CaravanScreen.Items, "Items",
                CaravanScreen.Count(all, CaravanScreen.Items)));
            Rail.Add(TradeRailEntry.Of(CaravanScreen.Supplies, "Food and medicine",
                CaravanScreen.Count(all, CaravanScreen.Supplies)));

            string picked = TradeRail.Draw(rect, Rail, screen.Category, ref screen.RailScroll,
                ref screen.RailDragging, ref screen.RailDragOffset, palette);

            if (picked == null)
                return;

            screen.Category = picked;
            screen.TableScroll = Vector2.zero;
        }

        // ---------------------------------------------------------------------------------------

        private const float MassWidth = 70f;
        private const float ValueWidth = 72f;
        private const float RouteWidth = 168f;
        private const float StayWidth = 70f;
        private const float CellGap = 6f;

        private static void Table(Dialog_SplitCaravan dialog, Screen screen, List<TransferableOneWay> all,
            Rect rect, UIColorPaletteDef palette)
        {
            Search.Draw(new Rect(rect.x, rect.y, Mathf.Min(320f, rect.width), 28f), palette);

            float top = rect.y + 36f;

            float width = GzpPalette.ContentWidth(rect);

            float fixedWidth = MassWidth + ValueWidth + RouteWidth + TradeStepper.Width + StayWidth + CellGap * 5f;

            float nameWidth = Mathf.Max(120f, width - fixedWidth);

            Captions(new Rect(rect.x, top, width, TradeShell.ColumnsHeight), nameWidth, palette);

            top += TradeShell.ColumnsHeight;

            Collect(all, screen);

            // Judged against the road the source caravan is already on, when it is on one. A stationary caravan
            // being split has no journey to measure spoilage against, so the rot lines state the fact instead of
            // drawing a conclusion. Same rule as the form screen.
            float tripDays = SplitReflection.TicksToArrive(dialog) / 60000f;

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

                Row(dialog, new Rect(0f, y, view.width, rowHeight), Rows[i], i, nameWidth, tripDays, palette);
            }

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(list, view.height, ref screen.TableScroll, ref screen.TableDragging,
                ref screen.TableDragOffset);
        }

        private static void Collect(List<TransferableOneWay> all, Screen screen)
        {
            Rows.Clear();

            for (int i = 0; all != null && i < all.Count; i++)
            {
                TransferableOneWay transferable = all[i];

                if (!CaravanScreen.Matches(transferable, screen.Category))
                    continue;

                if (!Search.IsEmpty && !Search.Matches(transferable.Label))
                    continue;

                Rows.Add(transferable);
            }

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

            TradeShell.Column(new Rect(x, rect.y, RouteWidth, rect.height), "Condition", palette);

            x += RouteWidth + CellGap;

            TradeShell.Column(new Rect(x, rect.y, TradeStepper.Width, rect.height), "Going", palette);

            x += TradeStepper.Width + CellGap;

            TradeShell.Column(new Rect(x, rect.y, StayWidth, rect.height), "Staying", palette,
                TextAnchor.MiddleRight);

            GUI.color = palette.Border;
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            GUI.color = Color.white;
        }

        private static void Row(Dialog_SplitCaravan dialog, Rect rect, TransferableOneWay transferable, int index,
            float nameWidth, float tripDays, UIColorPaletteDef palette)
        {
            bool going = transferable.CountToTransfer > 0;

            TradeShell.RowBackground(rect, index, going, palette);

            float x = rect.x;

            Name(new Rect(x, rect.y, nameWidth, rect.height), transferable, palette);

            x += nameWidth + CellGap;

            Right(new Rect(x, rect.y, MassWidth, rect.height),
                CaravanRoute.MassOf(transferable).ToString("0.#") + " kg",
                going ? palette.TextSecondary : palette.TextDisabled);

            x += MassWidth + CellGap;

            Right(new Rect(x, rect.y, ValueWidth, rect.height),
                CaravanRoute.ValueOf(transferable).ToStringMoney(),
                going ? palette.TextSecondary : palette.TextDisabled);

            x += ValueWidth + CellGap;

            CaravanVerdict verdict = CaravanRoute.For(transferable, tripDays);

            Route(new Rect(x, rect.y, RouteWidth, rect.height), verdict, palette);

            x += RouteWidth + CellGap;

            if (transferable.Interactive)
            {
                Rect stepper = new Rect(x, rect.y + (rect.height - 24f) * 0.5f, TradeStepper.Width, 24f);

                // Always +1: a TransferableOneWay counts towards its destination, and here the destination is the
                // new caravan. The number typed is the number that leaves.
                if (TradeStepper.Draw(stepper, transferable, 1, palette))
                    SplitReflection.NotifyChanged(dialog);
            }
            else
            {
                TradeStepper.Refused(new Rect(x, rect.y, TradeStepper.Width, rect.height), "Fixed", palette);
            }

            x += TradeStepper.Width + CellGap;

            int staying = transferable.MaxCount - Mathf.Max(0, transferable.CountToTransfer);

            // Nothing staying is worth flagging on this screen in a way it is not on the form screen: taking the
            // whole of something out of a caravan on the road leaves the other half without any of it, and that
            // is a decision rather than an oversight only if the player can see it.
            Right(new Rect(x, rect.y, StayWidth, rect.height), staying > 0 ? staying.ToStringCached() : "none",
                staying > 0 ? palette.TextDisabled : palette.Warning);
        }

        private static void Name(Rect rect, TransferableOneWay transferable, UIColorPaletteDef palette)
        {
            float icon = Mathf.Min(rect.height - 6f, 26f);

            UIGuard.Try("Split.RowIcon", () =>
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
                    new TipSignal(() => transferable.TipDescription, transferable.GetHashCode() * 733));
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

        private static void Right(Rect rect, string text, Color color)
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
        /// Both halves, one under the other, measured the same way.
        ///
        /// <b>The order is deliberate: going first, staying second.</b> The half being created is the one the
        /// player is building and the one that can be wrong in a way they have not noticed; the half staying
        /// behind is the consequence. Reading the consequence first would put the answer above the question.
        /// </summary>
        private static void Spine(Dialog_SplitCaravan dialog, Screen screen, List<TransferableOneWay> all,
            Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Rect inner = rect.ContractedBy(10f);

            float width = GzpPalette.ContentWidth(inner);

            Rect view = new Rect(0f, 0f, width, 520f);

            Widgets.BeginScrollView(inner, ref screen.SpineScroll, view, false);

            int going = Owners(all, true);
            int staying = Owners(all, false);

            float y = 0f;

            y = Half(view, y, "GOING", SplitReflection.Going(dialog), going, palette);

            y += 6f;

            y = Half(view, y, "STAYING", SplitReflection.Staying(dialog), staying, palette);

            if (going == 0 || staying == 0)
            {
                y += 6f;

                y = TradeShell.Note(view, y,
                    going == 0
                        ? "A new caravan needs at least one colonist who is not downed. Nothing will leave until "
                          + "one is going."
                        : "The caravan left behind needs at least one colonist who is not downed. Leave one, or "
                          + "there is nothing to split from.", palette);
            }

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(inner, view.height, ref screen.SpineScroll, ref screen.SpineDragging,
                ref screen.SpineDragOffset);
        }

        /// <summary>
        /// One half: who is in it, what it weighs, and how it travels.
        ///
        /// Every number is vanilla's own cached getter for that side. The point of drawing them together is that
        /// a split is a trade between the two, and a load that solves one half by breaking the other is the whole
        /// failure mode.
        /// </summary>
        private static float Half(Rect view, float y, string title, SplitReflection.Side side, int owners,
            UIColorPaletteDef palette)
        {
            y = TradeShell.Heading(view, y, title, palette);

            y = TradeShell.Readout(view, y, "Colonists", owners.ToStringCached(), palette,
                owners > 0 ? palette.TextPrimary : palette.Danger);

            y = TradeShell.Readout(view, y, "Mass",
                side.MassUsage.ToString("0.#") + " / " + side.MassCapacity.ToString("0.#") + " kg", palette,
                side.Over ? palette.Danger : palette.TextPrimary);

            float fraction = side.MassCapacity <= 0f ? 1f : Mathf.Clamp01(side.MassUsage / side.MassCapacity);

            GzpPalette.Bar(new Rect(view.x, y, view.width, 6f), fraction,
                side.Over ? palette.Danger : fraction > 0.85f ? palette.Warning : palette.Accent);

            y += 12f;

            y = TradeShell.Readout(view, y, "Speed", side.TilesPerDay.ToString("0.#") + " tiles / day", palette,
                palette.TextSecondary, GameFont.Tiny);

            y = TradeShell.Readout(view, y, "Food", side.Days.ToString("0.#") + " days", palette,
                side.Days <= 0f ? palette.Danger : palette.TextSecondary, GameFont.Tiny);

            if (side.TillRot > 0f && side.TillRot < side.Days)
            {
                y = TradeShell.Readout(view, y, "First spoilage", side.TillRot.ToString("0.#") + " days", palette,
                    palette.Warning, GameFont.Tiny);
            }

            if (side.Foraged > 0f)
            {
                y = TradeShell.Readout(view, y, "Foraged",
                    "+" + side.Foraged.ToString("0.##") + " / day"
                    + (side.ForagedFood != null ? " · " + side.ForagedFood.label : string.Empty), palette,
                    palette.Success, GameFont.Tiny);
            }

            return TradeShell.Readout(view, y, "Visibility", side.Visibility.ToStringPercent(), palette,
                palette.TextSecondary, GameFont.Tiny);
        }

        // ---------------------------------------------------------------------------------------

        private static void Footer(Dialog_SplitCaravan dialog, List<TransferableOneWay> all, Rect rect,
            UIColorPaletteDef palette)
        {
            TradeShell.KeyHint(rect, rect.x, "Click", "info card", palette);

            bool splittable = Owners(all, true) > 0 && Owners(all, false) > 0;

            // <b>Says what will happen.</b> With one half short of a colonist the button reads "Fix the split"
            // and does nothing, rather than staying pressable and handing back one of vanilla's rejection
            // messages. TrySplitCaravan still runs both checks itself, so this is an early warning rather than a
            // second enforcement.
            TradeShell.Footer(rect, palette, splittable ? "AcceptButton".Translate() : "Fix the split", splittable,
                () =>
                {
                    if (!SplitReflection.TrySplit(dialog))
                        return;

                    SoundDefOf.Tick_High.PlayOneShotOnCamera();

                    dialog.Close(false);
                },
                () =>
                {
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();

                    SplitReflection.Recache(dialog);
                },
                () => dialog.Close());
        }
    }
}
