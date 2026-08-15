using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Draws vanilla's transient messages as cards instead of as floating text.
    ///
    /// <b>Presentation only. Every behavior is vanilla's, deliberately.</b> Left click jumps to the message's target
    /// or opens the quest it belongs to, right click dismisses it, hovering highlights the target on the map, a
    /// repeated message flashes, and a message lives thirteen seconds and fades over the last six tenths. None of
    /// that is reimplemented from a description -- it was read out of <c>Message.Draw</c> and reproduced, because a
    /// restyle that quietly drops the right-click dismiss is not a restyle, it is a regression with better colors.
    ///
    /// <b>Two pieces of vanilla state have to be written, and both are private.</b> Dismissing works by pushing the
    /// message's start time into the far past so vanilla's own expiry removes it, and <c>lastDrawRect</c> has to be
    /// kept current or <c>Messages.CollidesWithAnyMessage</c> stops knowing where the messages are -- which is what
    /// other parts of the UI use to avoid drawing underneath them. Reflection for the first, a public field for the
    /// second.
    ///
    /// <b>Still one ImmediateWindow per message, as vanilla does it.</b> That is not incidental: the window is what
    /// puts a message at <c>WindowLayer.Super</c>, above the tabs and dialogs, and what gives its click a chance to
    /// be seen at all. Drawing inline in the patched method instead would put the cards under every window and make
    /// them unclickable, which is the kind of thing that looks like it works until something opens.
    /// </summary>
    internal static class MessageCards
    {
        /// <summary>Vertical gap between stacked cards. Wider than vanilla's, since these have edges to separate.</summary>
        private const float CardGap = 3f;

        private const float MinCardWidth = 180f;

        /// <summary>
        /// How much room the text is allowed, before the card's own chrome.
        ///
        /// Capped so one long message cannot draw a card across the whole screen. Vanilla has no such cap because it
        /// draws bare text, which fails more gracefully than a background the width of the map.
        /// </summary>
        private const float MaxTextWidth = 420f;

        private static readonly UINotificationCard Card = new UINotificationCard();

        /// <summary>
        /// Salt for the per-message window id, so ours cannot collide with the id vanilla's own draw would have used
        /// for the same message. Arbitrary, and only has to be stable and unlike vanilla's.
        /// </summary>
        private const int WindowIdSalt = 0x4D53_4744;

        private static readonly AccessTools.FieldRef<Message, float> StartingTime =
            AccessTools.FieldRefAccess<Message, float>("startingTime");

        private static List<Message> live;

        /// <summary>
        /// Vanilla's live message list.
        ///
        /// Resolved once and held, rather than read per frame: the field is a list instance that vanilla mutates in
        /// place -- <c>Update</c> removes expired entries, <c>Clear</c> empties it -- and never reassigns, so one
        /// lookup stays correct for the session. <c>Messages.Clear</c> calling <c>liveMessages.Clear()</c> rather
        /// than assigning a new list is what makes that safe, and is worth knowing before anyone caches it again.
        /// </summary>
        private static List<Message> Live
        {
            get
            {
                return live ?? (live = AccessTools.StaticFieldRefAccess<List<Message>>(
                    typeof(Messages), "liveMessages"));
            }
        }

        /// <summary>
        /// Whether this feature can run at all.
        ///
        /// Both reflected members are checked, because a partial failure is the bad case: without the start-time
        /// field the cards would draw and refuse to be dismissed, which is worse than not restyling them. Vanilla
        /// draws instead, and the player gets working messages that look like the base game.
        /// </summary>
        internal static bool Available => StartingTime != null && Live != null;

        /// <summary>
        /// Draws every live message as a card, stacked away from whichever corner it is docked at.
        ///
        /// <b>Each card is placed individually rather than in a column of one width,</b> because these are sized
        /// to their own text. At a left dock that means a ragged right edge, which is what vanilla's bare text
        /// gives too; at a right dock every card is aligned to the screen edge and the ragged edge is on the left.
        /// </summary>
        internal static void Draw()
        {
            List<Message> messages = Live;

            Text.Font = GameFont.Small;

            NotificationDock dock = NotificationLayout.DockOf(NotificationSurface.Messages);
            bool up = NotificationLayout.GrowsUp(dock);

            float cursor = NotificationLayout.Anchor(NotificationSurface.Messages, dock);

            // The tutorial pushes messages down to keep its own panel clear. Read the same way vanilla reads it,
            // so a restyled message does not sit under the lesson box. Only meaningful at a top dock -- the panel
            // it is avoiding is at the top of the screen, and pushing a bottom docked stack further down would
            // move it toward the edge it is already anchored to.
            if (!up && Current.Game != null && Find.ActiveLesson.ActiveLessonVisible)
                cursor += Find.ActiveLesson.Current.MessagesYOffset;

            float used = 0f;

            // Newest first, matching vanilla: it walks the list backwards, so the most recent message is the one
            // nearest the anchor and older ones move away from it.
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                Message message = messages[i];

                if (message == null)
                    continue;

                float height = Card.HeightFor(1);
                float width = WidthFor(message);
                float x = NotificationLayout.ColumnX(dock, width);

                if (up)
                    cursor -= height;

                Rect card = new Rect(x, cursor, width, height);

                cursor += up ? -CardGap : height + CardGap;
                used += height + CardGap;

                // Kept current for CollidesWithAnyMessage, which other UI uses to avoid overlapping the messages.
                // Assigned before drawing rather than after, so a message that throws while drawing has still
                // reported where it is.
                message.lastDrawRect = card;

                DrawOne(message, card);
            }

            NotificationLayout.Report(NotificationSurface.Messages, dock, Mathf.Max(0f, used - CardGap));
        }

        private static float WidthFor(Message message)
        {
            float text = Mathf.Min(Text.CalcSize(message.text ?? string.Empty).x, MaxTextWidth);

            return Mathf.Max(MinCardWidth, text + Card.EdgeWidth + Card.ContentInset * 2f);
        }

        /// <summary>
        /// One message, in its own window at the layer vanilla uses.
        ///
        /// The window's contents are guarded rather than the loop above, so one message that cannot draw costs that
        /// message and not the rest of the stack. The delegate runs later, inside the window stack's own pass, which
        /// is exactly the deferred case <c>UIGuard.Wrap</c> exists for -- but the rect it closes over is this
        /// frame's, so it is built per message per frame rather than cached.
        /// </summary>
        private static void DrawOne(Message message, Rect card)
        {
            Find.WindowStack.ImmediateWindow(Gen.HashCombineInt(message.GetHashCode(), WindowIdSalt), card,
                WindowLayer.Super, () => UIGuard.Try("Notifications.MessageCard", () => Contents(message, card)),
                false, false, 0f);
        }

        private static void Contents(Message message, Rect card)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // ImmediateWindow puts the origin at the window's own top left, so everything inside is drawn against
            // the card at zero rather than against where it sits on screen.
            Rect local = card.AtZero();

            float alpha = message.Alpha;
            bool canJump = CanJump(message);
            bool hovered = Mouse.IsOver(local);

            Rect text = Card.DrawChrome(local, palette, NotificationColors.For(message.def, palette), alpha,
                hovered && canJump);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            // Text at full strength against the card's own fade, so the last thing to disappear is the words.
            GUI.color = new Color(palette.TextPrimary.r, palette.TextPrimary.g, palette.TextPrimary.b, alpha);
            Widgets.Label(text, (message.text ?? string.Empty).Truncate(text.width));

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            GUI.color = previousColor;

            if (canJump)
            {
                // Vanilla registers this so the tutorial can point at a message. Kept, because a restyle that
                // breaks the tutorial's highlight is a restyle that breaks the tutorial.
                UIHighlighter.HighlightOpportunity(local, "Messages");
            }

            if (hovered)
            {
                if (!message.text.NullOrEmpty())
                    TooltipHandler.TipRegion(local, (TipSignal) message.text);

                // What makes hovering a message highlight its subject on the map. Vanilla's Update reads the index
                // this sets, so not calling it would silently drop that behavior.
                Messages.Notify_Mouseover(message);
            }

            if (Current.ProgramState == ProgramState.Playing && Widgets.ButtonInvisible(local))
                Clicked(message);
        }

        /// <summary>
        /// Vanilla's click behavior, reproduced: right click dismisses, left click jumps or opens the quest.
        /// </summary>
        private static void Clicked(Message message)
        {
            if (Event.current.button == 1)
            {
                Dismiss(message);
                return;
            }

            GlobalTargetInfo target = message.lookTargets.TryGetPrimaryTarget();

            if (CameraJumper.CanJump(target))
            {
                CameraJumper.TryJumpAndSelect(target);
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.ClickingMessages, KnowledgeAmount.Total);
                return;
            }

            if (message.quest == null || message.quest.hidden)
                return;

            if (Find.MainTabsRoot.OpenTab == MainButtonDefOf.Quests)
                SoundDefOf.Click.PlayOneShotOnCamera();
            else
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Quests);

            ((MainTabWindow_Quests) MainButtonDefOf.Quests.TabWindow).Select(message.quest);
        }

        /// <summary>
        /// Dismisses a message the way vanilla does: by making it old enough to have expired.
        ///
        /// There is no public dismiss, and adding one would mean patching more of vanilla than this needs. Pushing
        /// the start time far into the past lets vanilla's own <c>Update</c> remove it on its own schedule, which
        /// keeps one owner of the list rather than two.
        /// </summary>
        private static void Dismiss(Message message)
        {
            StartingTime(message) = -99999f;
        }

        private static bool CanJump(Message message)
        {
            return CameraJumper.CanJump(message.lookTargets.TryGetPrimaryTarget())
                   || (message.quest != null && !message.quest.hidden);
        }
    }

    /// <summary>
    /// Hands vanilla's message drawing over to <see cref="MessageCards"/>.
    ///
    /// A replacing prefix rather than a postfix, because both would draw and the result would be our cards with
    /// vanilla's text floating on top of them.
    ///
    /// <b>Falls back to vanilla drawing rather than to nothing.</b> If our draw throws, or the reflection this needs
    /// could not be resolved, the prefix returns true and the base game draws its own messages. A player with a
    /// broken restyle still sees what is happening in their colony, which is the one thing this surface exists for.
    /// </summary>
    [HarmonyPatch(typeof(Messages), nameof(Messages.MessagesDoGUI))]
    public static class Patch_Messages_MessagesDoGUI
    {
        /// <summary>Not applied at all when another mod already owns this surface.</summary>
        public static bool Prepare() => NotificationCompatibility.ShouldPatch();

        public static bool Prefix()
        {
            if (!MessageCards.Available || !NotificationSettings.Restyle(NotificationSurface.Messages))
                return true;

            return UIGuard.Replaced("Notifications.Messages", MessageCards.Draw,
                "Messages are drawn in the vanilla style for the rest of the session.");
        }
    }
}
