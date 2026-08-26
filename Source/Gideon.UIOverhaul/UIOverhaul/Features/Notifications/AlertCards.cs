using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Draws the alerts readout as cards: sized to their label, with an edge that drains as they go unaddressed.
    ///
    /// <b>Cards that fit, which is the whole reason this is worth replacing.</b> Vanilla gives every alert a fixed
    /// 154 pixels and clips whatever does not fit, so the readout is a column of half-sentences. These measure the
    /// label and take the height they need.
    ///
    /// <b>The left edge is a clock, not a decoration.</b> It starts full and bright and shrinks and fades over two
    /// minutes of running time, so a problem that just appeared looks nothing like one that has been sitting there
    /// since before you went to make tea. Draining stops while the game is paused -- see <see cref="AlertState"/>,
    /// which measures on the same clock the caches use, because an alert you are reading is not one you are ignoring.
    ///
    /// <b>Three clicks, and the two destructive ones are the deliberate ones.</b> Left click is vanilla's: jump to
    /// the problem. Right click snoozes for a few minutes. Shift-click hides the alert for good, and is recoverable
    /// from the file the hidden set lives in. Ordinary clicking cannot silence anything by accident.
    /// </summary>
    internal static class AlertCards
    {
        private const float MinWidth = 154f;

        /// <summary>
        /// Widest a card may get: whatever width the letter rows are using.
        ///
        /// <b>Shared with the letters rather than a constant of its own, asked for 2026-08-25.</b> Alerts and
        /// letters stack in the same column, so two separate ceilings gave the column a ragged edge and held the
        /// alerts at 260 however much room the player had given the letters beside them. That is what left
        /// "Raids arriving in 7 hours" short of space in a stack whose letters were wider than the alerts.
        ///
        /// Following the letters means the player's own width setting governs both and there is no second
        /// setting to find. Cards still size themselves to their content up to this, so a short alert stays
        /// narrow rather than every one of them eating the map.
        ///
        /// Floored at <see cref="MinWidth"/>, so letters set very narrow cannot collapse an alert card.
        /// </summary>
        private static float MaxWidth => Mathf.Max(MinWidth, LetterRows.Width);

        private const float CardGap = 2f;

        /// <summary>Height of the strip that says something is snoozed.</summary>
        /// <summary>
        /// The snoozed-count strip, tall enough for the line it holds.
        ///
        /// Was a flat 16, which fits <c>GameFont.Tiny</c> and clips whatever is substituted for it when
        /// <c>Text.TinyFontSupported</c> is false -- a language with no tiny font, the disable-tiny-text
        /// preference, the Steam Deck, or any frame drawn during a long event. The floor keeps the strip from
        /// shrinking below its original size where Tiny is available. See <see cref="UIFonts"/>.
        /// </summary>
        private static float SnoozeStripHeight => Mathf.Max(16f, UIFonts.LineHeightOf(GameFont.Tiny));

        private static readonly UINotificationCard Card = new UINotificationCard { IconSize = 0f };

        private static readonly AccessTools.FieldRef<AlertsReadout, List<Alert>> ActiveAlerts =
            AccessTools.FieldRefAccess<AlertsReadout, List<Alert>>("activeAlerts");

        private static readonly AccessTools.FieldRef<AlertsReadout, int> MouseoverIndex =
            AccessTools.FieldRefAccess<AlertsReadout, int>("mouseoverAlertIndex");

        /// <summary>
        /// Vanilla's click handler, which is protected.
        ///
        /// Invoked rather than reimplemented: every alert type overrides it to jump somewhere specific, and guessing
        /// at that from the outside would mean a readout whose clicks went to the wrong place for anything a mod
        /// added. Reflection dispatches virtually, so each alert still runs its own.
        /// </summary>
        private static readonly System.Reflection.MethodInfo OnClick =
            AccessTools.Method(typeof(Alert), "OnClick");

        /// <summary>
        /// Whether this can run at all.
        ///
        /// Every reflected member is required, because each missing one produces a differently broken readout rather
        /// than a merely plainer one -- no list is nothing to draw, no click handler is a readout you cannot use, and
        /// no mouseover index silently stops alerts running their own hover update. Vanilla draws instead.
        /// </summary>
        internal static bool Available => ActiveAlerts != null && MouseoverIndex != null && OnClick != null;

        /// <summary>
        /// The order alerts stack in: worst first, so the top of the column is the thing to deal with.
        ///
        /// Three levels, which is all vanilla has -- <c>AlertPriority</c> is Medium, High, Critical, with no Low.
        /// Worth stating because "medium" being the floor is not what the name suggests, and a fourth tier added
        /// here would silently never match anything.
        /// </summary>
        private static readonly AlertPriority[] DrawOrder =
        {
            AlertPriority.Critical, AlertPriority.High, AlertPriority.Medium
        };

        internal static void Draw(AlertsReadout readout)
        {
            List<Alert> alerts = ActiveAlerts(readout);

            if (alerts == null || alerts.Count == 0)
            {
                // Reported as zero rather than simply skipped. A stale reservation expires on its own after a
                // frame, but saying so now is what lets the letters below reclaim the space on this one instead
                // of the next.
                NotificationLayout.Report(NotificationSurface.Alerts,
                    NotificationLayout.DockOf(NotificationSurface.Alerts), 0f);

                return;
            }

            AlertState.Prune(alerts);

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Text.Font = GameFont.Small;

            List<Alert> shown = new List<Alert>();
            int snoozed = 0;

            foreach (AlertPriority priority in DrawOrder)
            {
                foreach (Alert alert in alerts)
                {
                    if (alert == null || alert.Priority != priority || AlertState.IsHidden(alert))
                        continue;

                    if (AlertState.IsSnoozed(alert))
                    {
                        snoozed++;
                        continue;
                    }

                    shown.Add(alert);
                }
            }

            NotificationDock dock = NotificationLayout.DockOf(NotificationSurface.Alerts);

            float width = WidthFor(shown);
            float x = NotificationLayout.ColumnX(dock, width);

            // Measured before drawing, because the column is anchored at one end and drawn from the other.
            // Alerts are always laid out worst first downward, so a dock that grows upward needs the total height
            // to know where the top of the block goes.
            float total = 0f;

            foreach (Alert alert in shown)
                total += HeightFor(alert, width) + CardGap;

            if (snoozed > 0)
                total += SnoozeStripHeight + CardGap;

            // <b>No longer Find.LetterStack.LastTopY, and that is the point of the layout.</b> Vanilla anchors
            // this column to wherever the letter stack stopped, which is a direct dependency between two surfaces
            // and the reason neither could be moved: alerts docked at the top right would still have been
            // positioned by a number describing the bottom right corner. Both surfaces ask NotificationLayout
            // now, and it keeps them from overlapping when they do share a corner.
            float anchor = NotificationLayout.Anchor(NotificationSurface.Alerts, dock);
            float y = NotificationLayout.GrowsUp(dock) ? Mathf.Max(0f, anchor - total) : anchor;

            for (int i = 0; i < shown.Count; i++)
            {
                float height = HeightFor(shown[i], width);

                DrawCard(readout, alerts, shown[i], new Rect(x, y, width, height), palette);

                y += height + CardGap;
            }

            if (snoozed > 0)
                DrawSnoozeStrip(new Rect(x, y, width, SnoozeStripHeight), alerts, snoozed, palette);

            NotificationLayout.Report(NotificationSurface.Alerts, dock, Mathf.Max(0f, total - CardGap));
        }

        private static float WidthFor(List<Alert> shown)
        {
            float widest = MinWidth;

            foreach (Alert alert in shown)
            {
                float label = Text.CalcSize(LabelOf(alert)).x + Card.EdgeWidth + Card.ContentInset * 2f;

                if (label > widest)
                    widest = label;
            }

            return Mathf.Min(widest, MaxWidth);
        }

        private static float HeightFor(Alert alert, float width)
        {
            float text = width - Card.EdgeWidth - Card.ContentInset * 2f;

            // Wrapped height rather than a line count, because at this width most alerts are one line and a few are
            // three, and guessing wrong in either direction is either a clipped warning or a column of empty space.
            return Mathf.Max(Card.HeightFor(1),
                Text.CalcHeight(LabelOf(alert), text) + Card.VerticalPad * 2f);
        }

        /// <summary>
        /// The label, defensively.
        ///
        /// An alert's label is computed by whoever wrote it, and a modded one that throws while the readout is
        /// drawing would take the whole column with it. A named placeholder is worth more than a blank card: it says
        /// which alert is misbehaving.
        /// </summary>
        private static string LabelOf(Alert alert)
        {
            return UIGuard.Try("Notifications.AlertLabel", () => alert.Label, alert.GetType().Name,
                "One alert shows its type name instead of its label.");
        }

        /// <summary>
        /// The edge color for a priority: yellow, orange, red as the three tiers climb.
        ///
        /// <b>A ramp rather than three unrelated colors.</b> The point of the edge is that a column of alerts can be
        /// read as a gradient without reading any of the words, and that only works if the three tiers are steps
        /// along one path. An earlier version used the information role for Medium, which is teal -- a perfectly good
        /// color that says "here is something to know" rather than "here is a smaller version of that red thing", so
        /// the column read as two unrelated scales.
        ///
        /// <b>The orange comes from <see cref="NotificationColors"/> rather than from here.</b> It used to be
        /// derived in this method, and the derivation has moved because the letter stack needs the same color: a
        /// small threat's letter and a high priority alert about the same event sit inches apart on the same edge
        /// of the screen, and two independently mixed oranges would read as two different severities. The
        /// reasoning for deriving it at all rather than adding an orange palette role is recorded there.
        /// </summary>
        private static Color EdgeFor(AlertPriority priority, UIColorPaletteDef palette)
        {
            if (priority == AlertPriority.Critical)
                return palette.Danger;

            if (priority == AlertPriority.High)
                return NotificationColors.Orange(palette);

            return palette.Warning;
        }

        private static void DrawCard(AlertsReadout readout, List<Alert> all, Alert alert, Rect card,
            UIColorPaletteDef palette)
        {
            bool critical = alert.Priority == AlertPriority.Critical;
            float freshness = AlertState.Freshness(alert);
            bool hovered = Mouse.IsOver(card);

            Color edge = EdgeFor(alert.Priority, palette);

            Rect text = Card.DrawChrome(card, palette, edge, 1f, hovered);

            // The drain, drawn over the full-height edge the card just painted: the bar keeps its color but loses
            // its length and its brightness, so an ignored alert dims without disappearing. Painted from the bottom
            // so it reads as draining rather than as growing.
            float spent = card.height * (1f - freshness);

            if (spent > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(card.x, card.y, Card.EdgeWidth, spent),
                    new Color(edge.r, edge.g, edge.b, 0.25f));
            }

            if (critical)
            {
                // A wash rather than a solid fill, so the label stays readable over it. The hazard mark is drawn as
                // well, because color alone is the one channel some players do not have.
                Widgets.DrawBoxSolid(card, new Color(palette.Danger.r, palette.Danger.g, palette.Danger.b, 0.13f));

                if (NotificationIcons.Hazard != null)
                {
                    Rect badge = new Rect(card.xMax - 16f, card.y + 3f, 12f, 12f);

                    Color previousBadge = GUI.color;
                    GUI.color = palette.Danger;
                    GUI.DrawTexture(badge, NotificationIcons.Hazard, ScaleMode.ScaleToFit);
                    GUI.color = previousBadge;

                    text = new Rect(text.x, text.y, text.width - 18f, text.height);
                }
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextPrimary;

            // <b>Wrapping off, because a card is one line by construction.</b> Reported 2026-08-25: "Raids
            // arriving in 7 hours" is wider than the strip, so IMGUI laid it out over two lines and the card cut
            // both of them in half. Vertically clipped text reads as a rendering fault rather than as a label
            // that did not fit, which is the whole reason every other single-line label in this mod says this.
            Text.WordWrap = false;

            // Shortened here rather than left to clip, and through UIRichText because an alert label carries
            // colour -- a count in the danger tone, a pawn named in theirs. Cutting such a string by raw
            // characters lands inside the tag and prints the markup as words; this one cuts by visible
            // characters and closes what it opened. The hazard badge has already taken its 18px off the lane
            // above, so the width this measures against is the width actually available.
            UIRichText.Label(text, LabelOf(alert));

            Text.WordWrap = previousWrap;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            GUI.color = previousColor;

            if (hovered)
            {
                // Vanilla reads this to run the hovered alert's own update, which is what makes some of them
                // highlight their subject on the map. Not setting it drops that silently.
                MouseoverIndex(readout) = all.IndexOf(alert);

                Tooltip(card, alert);
            }

            Handle(card, alert);
        }

        /// <summary>
        /// The hover text: the alert's label, its own explanation, and what the two clicks do.
        ///
        /// <b>Built lazily, through the tip signal that takes a function.</b> A card sees several events a frame
        /// and the explanation is the expensive half -- <see cref="Explanation"/> recalculates the alert, which
        /// for some of them walks every colonist. This way it is built when the tooltip is actually drawn and not
        /// otherwise, which is also what vanilla does by only filling its info pane on a repaint.
        ///
        /// The alert's own hash is the tip's id, so two cards never share a cached tooltip. Alerts are one
        /// instance per type for the life of the game, which is what makes that stable.
        /// </summary>
        private static void Tooltip(Rect card, Alert alert)
        {
            TooltipHandler.TipRegion(card, new TipSignal(() => TooltipText(alert), alert.GetHashCode()));
        }

        private static string TooltipText(Alert alert)
        {
            // Named by alert type, so the next one of these says which alert rather than only that an alert did
            // it. Sites are deduplicated by name, and there are as many names as there are alert types.
            string explanation = UIGuard.Try("Notifications.AlertExplanation." + alert.GetType().Name,
                () => Explanation(alert), string.Empty, "One alert's tooltip shows its label only.");

            string tip = LabelOf(alert);

            if (!explanation.NullOrEmpty())
                tip += "\n\n" + explanation;

            return tip + "\n\nRight click to snooze. Shift click to hide it for good.";
        }

        /// <summary>
        /// One alert's explanation, asked for the way the game asks for it.
        ///
        /// <b>Recalculate first, then refuse if the alert is no longer active.</b> That order is copied from
        /// <c>Alert.DrawInfoPane</c> and it is not ceremony: an alert's explanation is often built from culprits
        /// that <c>GetReport</c> gathers into a field, and <c>AlertsReadout</c> recalculates only a slice of its
        /// alerts each frame. So the alert under the pointer can easily be one whose subject died two frames ago,
        /// and asking that one for an explanation is a null reference inside somebody else's code. Reported by
        /// Aaron on 2026-08-23, caught by the guard, and the tooltip had been showing the label alone.
        ///
        /// <b>The empty check is on the tagged string, not the resolved one.</b> An alert that never set a
        /// default explanation and does not override this hands back a tagged string wrapping null; resolving it
        /// is harmless but pointless, and the caller wants to know there is nothing to append.
        /// </summary>
        private static string Explanation(Alert alert)
        {
            alert.Recalculate();

            if (!alert.Active)
                return string.Empty;

            TaggedString explanation = alert.GetExplanation();

            return explanation.NullOrEmpty() ? string.Empty : explanation.Resolve();
        }

        private static void Handle(Rect card, Alert alert)
        {
            if (!Widgets.ButtonInvisible(card))
                return;

            if (Event.current.shift)
            {
                AlertState.Hide(alert);
                Messages.Message("Hid the alert \"" + LabelOf(alert) + "\". Restore it from this mod's settings.",
                    MessageTypeDefOf.SilentInput, false);

                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            if (Event.current.button == 1)
            {
                AlertState.Snooze(alert);
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            UIGuard.Try("Notifications.AlertClick", () => OnClick.Invoke(alert, null),
                "This alert did not jump to its subject.");
        }

        /// <summary>
        /// One strip saying something is snoozed, with the longest remaining snooze as a bar.
        ///
        /// Snoozed alerts are not simply hidden, because a silenced warning with nothing to show for it is how a
        /// player forgets they silenced it. This is the smallest thing that keeps the fact visible and gives it back.
        /// </summary>
        private static void DrawSnoozeStrip(Rect strip, List<Alert> all, int count, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(strip, new Color(palette.PanelBackground.r, palette.PanelBackground.g,
                palette.PanelBackground.b, 0.6f));

            float longest = 0f;

            foreach (Alert alert in all)
                longest = Mathf.Max(longest, AlertState.SnoozeRemaining(alert));

            Widgets.DrawBoxSolid(new Rect(strip.x, strip.yMax - 2f, strip.width * longest, 2f),
                new Color(palette.Info.r, palette.Info.g, palette.Info.b, 0.5f));

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextSecondary;

            Widgets.Label(strip, count == 1 ? "1 alert snoozed" : count + " alerts snoozed");

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            GUI.color = previousColor;

            TooltipHandler.TipRegion(strip, (TipSignal) "Click to bring snoozed alerts back now.");

            if (!Widgets.ButtonInvisible(strip))
                return;

            foreach (Alert alert in all)
            {
                if (AlertState.IsSnoozed(alert))
                    AlertState.Wake(alert);
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
        }
    }

    /// <summary>
    /// Hands the alerts readout over to <see cref="AlertCards"/>.
    ///
    /// Vanilla's own early-out is reproduced rather than left to the original: it skips layout and drag events, and
    /// drawing on a layout event would allocate a control id per alert and shift every id after it.
    ///
    /// <b>The postfix is the mirror of the one on the letter stack.</b> A player can hand the alerts back to
    /// vanilla and keep this mod's messages docked in the same corner, and vanilla's readout reports nothing to
    /// <see cref="NotificationLayout"/> -- so the messages would stack over it. <c>AlertsHeight</c> is what vanilla
    /// itself uses to place the column, so reporting that on its behalf places anything above it correctly.
    /// </summary>
    [HarmonyPatch(typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutOnGUI))]
    public static class Patch_AlertsReadout_OnGUI
    {
        /// <summary>Not applied at all when another mod already owns this surface.</summary>
        public static bool Prepare() => NotificationCompatibility.ShouldPatch();

        public static bool Prefix(AlertsReadout __instance, out bool __state)
        {
            __state = false;

            if (!AlertCards.Available || !NotificationSettings.Restyle(NotificationSurface.Alerts))
            {
                __state = true;

                return true;
            }

            if (Event.current.type == EventType.Layout || Event.current.type == EventType.MouseDrag)
                return false;

            __state = UIGuard.Replaced("Notifications.Alerts", () => AlertCards.Draw(__instance),
                "Alerts are drawn in the vanilla style for the rest of the session.");

            return __state;
        }

        public static void Postfix(bool __state)
        {
            if (!__state)
                return;

            UIGuard.Try("Notifications.MeasureVanillaAlerts", () =>
                    NotificationLayout.Report(NotificationSurface.Alerts, NotificationDock.BottomRight,
                        Find.Alerts?.AlertsHeight ?? 0f),
                "This mod's messages may overlap RimWorld's own alerts readout.");
        }
    }
}
