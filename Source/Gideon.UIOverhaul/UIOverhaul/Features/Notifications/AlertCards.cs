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

        /// <summary>Widest a card may get. Past this, a label wraps rather than eating the map.</summary>
        private const float MaxWidth = 260f;

        private const float CardGap = 2f;

        /// <summary>Height of the strip that says something is snoozed.</summary>
        private const float SnoozeStripHeight = 16f;

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
                return;

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

            float width = WidthFor(shown);
            float x = UI.screenWidth - width;

            // Measured before drawing so the stack can be bottom-anchored against the letters, the way vanilla
            // anchors its own: alerts grow upward from the letter stack rather than downward from the top.
            float total = 0f;

            foreach (Alert alert in shown)
                total += HeightFor(alert, width) + CardGap;

            if (snoozed > 0)
                total += SnoozeStripHeight + CardGap;

            float y = Mathf.Max(0f, Find.LetterStack.LastTopY - total);

            for (int i = 0; i < shown.Count; i++)
            {
                float height = HeightFor(shown[i], width);

                DrawCard(readout, alerts, shown[i], new Rect(x, y, width, height), palette);

                y += height + CardGap;
            }

            if (snoozed > 0)
                DrawSnoozeStrip(new Rect(x, y, width, SnoozeStripHeight), alerts, snoozed, palette);
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
        /// <b>Orange is derived rather than added to the palette.</b> There is no orange role, and adding one would
        /// mean a new field on <see cref="UIColorPaletteDef"/>, an entry in both shipped palettes, and a missing
        /// value in every palette a player has already written. Halfway between warning and danger is what orange
        /// <i>is</i>, so blending the two roles gives the right color on any theme, including one whose warning is
        /// not yellow -- the step stays proportionate to whatever the theme chose.
        /// </summary>
        private static Color EdgeFor(AlertPriority priority, UIColorPaletteDef palette)
        {
            if (priority == AlertPriority.Critical)
                return palette.Danger;

            if (priority == AlertPriority.High)
                return Color.Lerp(palette.Warning, palette.Danger, 0.5f);

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

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextPrimary;

            Widgets.Label(text, LabelOf(alert));

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

        private static void Tooltip(Rect card, Alert alert)
        {
            string explanation = UIGuard.Try("Notifications.AlertExplanation",
                () => alert.GetExplanation().Resolve(), string.Empty,
                "One alert's tooltip shows its label only.");

            string tip = LabelOf(alert);

            if (!explanation.NullOrEmpty())
                tip += "\n\n" + explanation;

            tip += "\n\nRight click to snooze. Shift click to hide it for good.";

            TooltipHandler.TipRegion(card, (TipSignal) tip);
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
    /// </summary>
    [HarmonyPatch(typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutOnGUI))]
    public static class Patch_AlertsReadout_OnGUI
    {
        /// <summary>Not applied at all when another mod already owns this surface.</summary>
        public static bool Prepare() => NotificationCompatibility.ShouldPatch();

        public static bool Prefix(AlertsReadout __instance)
        {
            if (!AlertCards.Available)
                return true;

            if (Event.current.type == EventType.Layout || Event.current.type == EventType.MouseDrag)
                return false;

            return UIGuard.Replaced("Notifications.Alerts", () => AlertCards.Draw(__instance),
                "Alerts are drawn in the vanilla style for the rest of the session.");
        }
    }
}
