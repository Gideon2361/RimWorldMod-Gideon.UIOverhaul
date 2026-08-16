using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Stages;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>
    /// The debug log, redrawn.
    ///
    /// <b>Only the contents are replaced, not the window.</b> <c>EditWindow_Log</c> carries a surprising amount of
    /// behavior that has nothing to do with how it looks: opening itself when an error appears, the developer
    /// toggle key, <c>wantsToOpen</c>, closing on escape according to a preference, remembering that it was open,
    /// and telling itself when a message is dequeued off the end of the queue. All of that is worth keeping and
    /// none of it is worth reproducing, so this replaces <c>DoWindowContents</c> and leaves the window alone. The
    /// frame around it is already this mod's, because <c>Widgets.DrawWindowBackground</c> is patched elsewhere.
    ///
    /// <b>Every vanilla control is still here.</b> A restyle that quietly drops a feature is a regression with
    /// better colours, so all of it survives: clear, copy, auto-open, pause on error, the three severity filters
    /// and a resizable detail pane. What is added is what the old one lacked -- counts on the filters, a search
    /// box, a severity stripe that can be read without reading, and a details pane that separates the message
    /// from its stack trace instead of running them together.
    ///
    /// <b>It stands down entirely when Modern Dev Tools is loaded.</b> That mod replaces this window and several
    /// others, and does it well; two mods replacing one surface is decided by load order and produces one of
    /// them at random. Standing aside there is the same judgement <c>NotificationCompatibility</c> makes, and for
    /// the same reason: the test is not "is another mod present" but "will this still be handled if we step
    /// aside". It will.
    /// </summary>
    internal static class ModernLog
    {
        private const float ToolbarHeight = 24f;
        private const float RowPad = 3f;
        private const float StripeWidth = 3f;
        private const float SplitterHeight = 6f;
        private const float MinListHeight = 80f;
        private const float MinDetailHeight = 60f;

        private static Vector2 listScroll;
        private static Vector2 detailScroll;

        private static bool showMessages = true;
        private static bool showWarnings = true;
        private static bool showErrors = true;

        private static LogMessage selected;
        private static float detailHeight = 220f;
        private static bool draggingSplitter;

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search"
        };

        /// <summary>
        /// Vanilla's own auto-open flag, which is private.
        ///
        /// Borrowed rather than replaced with one of ours: <c>TryAutoOpen</c> reads this field, so a separate
        /// flag would leave the button saying one thing while the window did another.
        ///
        /// <b>Resolved through the FieldInfo overload, and that is the one that works here.</b>
        /// <c>StaticFieldRefAccess&lt;T&gt;(Type, string)</c> returns a <c>ref</c> -- it reads and assigns like a
        /// field and cannot be held or tested for null. The <c>FieldInfo</c> overload returns a delegate, which
        /// is what lets this be null when the field is gone and the button simply not drawn. The same
        /// distinction is written up on <c>UISliderSkin</c>, where it was wanted the other way round.
        /// </summary>
        private static readonly AccessTools.FieldRef<bool> AutoOpen = ResolveAutoOpen();

        private static AccessTools.FieldRef<bool> ResolveAutoOpen()
        {
            try
            {
                System.Reflection.FieldInfo field = AccessTools.Field(typeof(EditWindow_Log), "canAutoOpen");

                return field == null ? null : AccessTools.StaticFieldRefAccess<bool>(field);
            }
            catch
            {
                return null;
            }
        }

        internal static void Draw(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            List<LogMessage> all = Snapshot();
            List<LogMessage> shown = Filter(all);

            Rect toolbar = new Rect(inRect.x, inRect.y, inRect.width, ToolbarHeight);
            DrawToolbar(toolbar, all, palette);

            // The detail pane only takes room when a message is selected, so an unselected log is the full
            // height of the list rather than permanently short by an empty panel.
            float detail = selected != null ? Mathf.Clamp(detailHeight, MinDetailHeight,
                Mathf.Max(MinDetailHeight, inRect.height - MinListHeight - ToolbarHeight)) : 0f;

            Rect list = new Rect(inRect.x, toolbar.yMax + 2f, inRect.width,
                Mathf.Max(0f, inRect.height - ToolbarHeight - 2f - (detail > 0f ? detail + SplitterHeight : 0f)));

            DrawList(list, shown, palette);

            if (selected == null)
                return;

            Rect splitter = new Rect(inRect.x, list.yMax, inRect.width, SplitterHeight);
            DrawSplitter(splitter, inRect, palette);

            DrawDetail(new Rect(inRect.x, splitter.yMax, inRect.width, detail), palette);
        }

        /// <summary>
        /// A copy of the queue.
        ///
        /// Taken whole rather than enumerated while drawing: <c>Log.Messages</c> is the live queue, and a message
        /// logged from another thread part way through a draw would otherwise change the collection underneath
        /// the enumerator. The cap is a thousand, so copying it is nothing.
        /// </summary>
        private static List<LogMessage> Snapshot()
        {
            return UIGuard.Try("Diagnostics.ReadLogQueue", () => new List<LogMessage>(Log.Messages),
                new List<LogMessage>(), "The debug log appears empty.");
        }

        private static List<LogMessage> Filter(List<LogMessage> all)
        {
            List<LogMessage> shown = new List<LogMessage>(all.Count);

            foreach (LogMessage message in all)
            {
                if (message == null || !Wanted(message.type))
                    continue;

                if (!Search.IsEmpty && !Search.Matches(message.text) && !Search.Matches(message.StackTrace))
                    continue;

                shown.Add(message);
            }

            return shown;
        }

        private static bool Wanted(LogMessageType type)
        {
            switch (type)
            {
                case LogMessageType.Error:
                    return showErrors;

                case LogMessageType.Warning:
                    return showWarnings;

                default:
                    return showMessages;
            }
        }

        private static void DrawToolbar(Rect rect, List<LogMessage> all, UIColorPaletteDef palette)
        {
            int messages = 0;
            int warnings = 0;
            int errors = 0;

            foreach (LogMessage message in all)
            {
                if (message == null)
                    continue;

                if (message.type == LogMessageType.Error)
                    errors++;
                else if (message.type == LogMessageType.Warning)
                    warnings++;
                else
                    messages++;
            }

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;

            float x = rect.x;

            // The three severity filters, each carrying its own count. Vanilla has the same three as icon
            // toggles with no numbers, which means the only way to find out whether there are any errors is to
            // switch the other two off.
            Toggle(ref x, rect, "Info " + messages, ref showMessages, palette.TextSecondary, palette);
            Toggle(ref x, rect, "Warnings " + warnings, ref showWarnings, palette.Warning, palette);
            Toggle(ref x, rect, "Errors " + errors, ref showErrors, palette.Danger, palette);

            x += 6f;

            // The vanilla controls that are not filters. Auto-open and pause on error are stateful, so they read
            // as toggles rather than as buttons that do something invisible.
            bool auto = AutoOpen != null && AutoOpen();

            if (AutoOpen != null && Toggle(ref x, rect, "Auto-open", ref auto, palette.Accent, palette))
                AutoOpen() = auto;

            bool pause = DebugSettings.pauseOnError;

            if (Toggle(ref x, rect, "Pause on error", ref pause, palette.Accent, palette))
                DebugSettings.pauseOnError = pause;

            // Right-hand end: the destructive and the useful, and the search box between them and the filters.
            Rect clear = new Rect(rect.xMax - 54f, rect.y, 52f, rect.height);
            Rect copy = new Rect(clear.x - 60f, rect.y, 56f, rect.height);
            Rect search = new Rect(x + 6f, rect.y, Mathf.Max(60f, copy.x - x - 12f), rect.height);

            Search.Draw(search, palette);

            if (Button(copy, "Copy", palette))
            {
                UIGuard.Try("Diagnostics.CopyLog",
                    () => { GUIUtility.systemCopyBuffer = AsText(Filter(all)); },
                    "The log could not be copied to the clipboard.");

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (Button(clear, "Clear", palette))
            {
                UIGuard.Try("Diagnostics.ClearLog", () =>
                    {
                        Log.Clear();
                        EditWindow_Log.ClearAll();
                    },
                    "The log was not cleared.");

                selected = null;
                listScroll = Vector2.zero;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Text.Font = previousFont;
        }

        /// <summary>
        /// A labelled toggle that advances the toolbar cursor. Answers whether it was clicked this frame, so a
        /// caller writing through to somebody else's field knows when to.
        /// </summary>
        private static bool Toggle(ref float x, Rect row, string label, ref bool value, Color tint,
            UIColorPaletteDef palette)
        {
            float width = Text.CalcSize(label).x + 18f;
            Rect rect = new Rect(x, row.y, width, row.height);

            bool over = Mouse.IsOver(rect);

            Widgets.DrawBoxSolid(rect, value ? palette.AccentMuted
                : over ? palette.HoverOverlay : palette.ControlBackgroundFaded);

            Color previous = GUI.color;
            GUI.color = value ? tint : palette.Border;
            Widgets.DrawBox(rect, 1);

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = value ? palette.TextPrimary : palette.TextDisabled;

            Widgets.Label(rect, label);

            Text.Anchor = previousAnchor;
            GUI.color = previous;

            x += width + 4f;

            if (!Widgets.ButtonInvisible(rect))
                return false;

            value = !value;
            SoundDefOf.Click.PlayOneShotOnCamera();

            return true;
        }

        private static void DrawList(Rect rect, List<LogMessage> shown, UIColorPaletteDef palette)
        {
            float rowHeight = UIFonts.LineHeightOf(GameFont.Tiny) + RowPad * 2f;
            float contentHeight = shown.Count * rowHeight;

            Rect view = new Rect(0f, 0f, rect.width - 18f, contentHeight);

            Widgets.BeginScrollView(rect, ref listScroll, view);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                // Wrapping off. Every row is one line tall and a log message is arbitrarily long, so a wrapped
                // block would be centred on its row and drawn through the rows either side. Same fault the
                // loading console had.
                Text.WordWrap = false;

                int first = Mathf.Max(0, Mathf.FloorToInt(listScroll.y / rowHeight) - 1);
                int last = Mathf.Min(shown.Count, first + Mathf.CeilToInt(rect.height / rowHeight) + 2);

                for (int i = first; i < last; i++)
                    DrawRow(new Rect(0f, i * rowHeight, view.width, rowHeight), shown[i], palette);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Widgets.EndScrollView();
        }

        private static void DrawRow(Rect rect, LogMessage message, UIColorPaletteDef palette)
        {
            bool chosen = selected == message;

            if (chosen)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
            else if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Color tone = ToneOf(message.type, palette);

            // The stripe is the part that can be read without reading. Vanilla colours the text instead, which
            // makes an error harder to read at exactly the moment it matters most.
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 1f, StripeWidth, rect.height - 2f), tone);

            float x = rect.x + StripeWidth + 6f;

            if (message.repeats > 1)
            {
                string count = "x" + message.repeats;
                float width = Text.CalcSize(count).x + 8f;

                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x, rect.y, width, rect.height), count);
                Text.Anchor = TextAnchor.MiddleLeft;

                x += width + 4f;
            }

            GUI.color = message.type == LogMessageType.Message ? palette.TextSecondary : palette.TextPrimary;

            Widgets.LabelEllipses(new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - x - 4f), rect.height),
                UILoadingLog.FirstLine(message.text));

            if (!Widgets.ButtonInvisible(rect))
                return;

            // Clicking the open one closes it, which is how the list gets its full height back without hunting
            // for a control that does that and nothing else.
            selected = chosen ? null : message;
            detailScroll = Vector2.zero;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static Color ToneOf(LogMessageType type, UIColorPaletteDef palette)
        {
            switch (type)
            {
                case LogMessageType.Error:
                    return palette.Danger;

                case LogMessageType.Warning:
                    return palette.Warning;

                default:
                    return palette.TextDisabled;
            }
        }

        /// <summary>
        /// The draggable edge between the list and the detail pane.
        ///
        /// Vanilla offers three fixed sizes through buttons labelled "Trace big", "Trace medium" and "Trace
        /// small". A dragged edge does the same job continuously and is what anybody expects of a split pane.
        /// </summary>
        private static void DrawSplitter(Rect rect, Rect bounds, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.center.y - 1f, rect.width, 2f),
                Mouse.IsOver(rect) || draggingSplitter ? palette.Accent : palette.Border);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(rect))
            {
                draggingSplitter = true;
                Event.current.Use();
            }

            if (!draggingSplitter)
                return;

            // Ended on the button being up rather than on a MouseUp event alone, so a drag released outside the
            // window does not leave the splitter glued to the cursor.
            if (Event.current.type == EventType.MouseUp || !Input.GetMouseButton(0))
            {
                draggingSplitter = false;

                return;
            }

            // Measured from the bottom of the window, which is the edge the detail pane is anchored to. Taking
            // it from the current pane height instead would accumulate, which is the fault the tab resizer had.
            detailHeight = Mathf.Clamp(bounds.yMax - Event.current.mousePosition.y, MinDetailHeight,
                Mathf.Max(MinDetailHeight, bounds.height - MinListHeight - ToolbarHeight));
        }

        /// <summary>
        /// The selected message in full: its text, then its stack trace, kept apart.
        ///
        /// Vanilla runs the two together in one text area. They are different things -- one is what happened and
        /// the other is where -- and the message is usually the short half being scrolled past to reach.
        /// </summary>
        private static void DrawDetail(Rect rect, UIColorPaletteDef palette)
        {
            // Raised rather than sunken: this is a reading surface on the panel, and SurfaceSunken is this
            // palette's empty-socket color, two steps below the window. See the note in LoadingConsole.
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(6f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                float line = UIFonts.LineHeightOf(GameFont.Tiny) + 2f;
                Rect head = new Rect(inner.x, inner.y, inner.width, line);

                Rect copyTrace = new Rect(inner.xMax - 84f, inner.y, 84f, line);
                Rect copyMessage = new Rect(copyTrace.x - 90f, inner.y, 86f, line);

                GUI.color = ToneOf(selected.type, palette);
                Widgets.Label(head, selected.type.ToString()
                                    + (selected.repeats > 1 ? "  x" + selected.repeats : string.Empty));

                if (Button(copyMessage, "Copy message", palette))
                {
                    UIGuard.Try("Diagnostics.CopyLogMessage",
                        () => { GUIUtility.systemCopyBuffer = selected.text; }, null);

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                if (Button(copyTrace, "Copy trace", palette))
                {
                    UIGuard.Try("Diagnostics.CopyLogTrace",
                        () => { GUIUtility.systemCopyBuffer = selected.StackTrace; }, null);

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                Rect body = new Rect(inner.x, head.yMax + 2f, inner.width,
                    Mathf.Max(0f, inner.yMax - head.yMax - 2f));

                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;

                string text = selected.text + "\n\n" + selected.StackTrace;

                float height = Text.CalcHeight(text, body.width - 18f);
                Rect view = new Rect(0f, 0f, body.width - 18f, Mathf.Max(height, body.height));

                // Read-only text areas rather than labels, so a line of the message or a frame of the trace can
                // be selected and copied on its own. Vanilla's log does the same, and a restyle that took away
                // the ability to copy part of a stack trace would be a regression dressed as an improvement.
                float messageHeight = Text.CalcHeight(selected.text, view.width);

                Widgets.BeginScrollView(body, ref detailScroll, view);

                UIElementPainter.SelectableText(new Rect(0f, 0f, view.width, messageHeight), selected.text,
                    palette.TextPrimary);

                UIElementPainter.SelectableText(new Rect(0f, messageHeight + 8f, view.width,
                    Mathf.Max(0f, view.height - messageHeight - 8f)), selected.StackTrace, palette.TextSecondary);

                Widgets.EndScrollView();
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static string AsText(List<LogMessage> messages)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder(messages.Count * 128);

            foreach (LogMessage message in messages)
            {
                text.Append(message.type).Append(": ").Append(message.text);

                if (message.repeats > 1)
                    text.Append("  (x").Append(message.repeats).Append(')');

                text.Append('\n').Append(message.StackTrace).Append("\n\n");
            }

            return text.ToString();
        }

        private static bool Button(Rect rect, string label, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);
            UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextPrimary;

            Widgets.Label(rect, label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>Forgets the selection, for when the queue is emptied underneath it.</summary>
        internal static void Notify_Cleared()
        {
            selected = null;
            listScroll = Vector2.zero;
        }
    }

    /// <summary>
    /// Hands the debug log's contents to <see cref="ModernLog"/>.
    ///
    /// A replacing prefix on the contents rather than on the window, so everything <c>EditWindow_Log</c> does
    /// about opening, closing and auto-opening itself is left exactly as it was.
    ///
    /// <b>Not applied at all when Modern Dev Tools is loaded.</b> That mod replaces this window outright, and two
    /// replacements on one surface are resolved by load order, which means the player gets one of them at random
    /// with no way to tell why. <c>Prepare</c> runs once when the class is processed, so standing down means
    /// never patching rather than patching and then declining every frame.
    ///
    /// <b>There is no setting in front of this, deliberately.</b> The only two things a switch could choose
    /// between are this log and the one it exists to replace, which is not a decision worth putting to anybody --
    /// the same reasoning that retired the speed glyph toggle. Whether it applies is decided entirely by whether
    /// another mod already owns the window, and that is not a preference either.
    /// </summary>
    [HarmonyPatch(typeof(EditWindow_Log), nameof(EditWindow_Log.DoWindowContents))]
    public static class Patch_EditWindow_Log_DoWindowContents
    {
        public static bool Prepare() => DevToolsCompatibility.ShouldPatch();

        public static bool Prefix(Rect inRect)
        {
            return UIGuard.Replaced("Diagnostics.ModernLog", () => ModernLog.Draw(inRect),
                "The debug log is drawn RimWorld's own way for the rest of the session.");
        }
    }

    /// <summary>
    /// Keeps our selection in step when vanilla drops a message off the end of the queue.
    ///
    /// The queue is capped, so a message that has scrolled off no longer exists. Vanilla clears its own
    /// selection here; ours needs the same or the detail pane would go on showing something the log no longer
    /// contains.
    /// </summary>
    [HarmonyPatch(typeof(EditWindow_Log), nameof(EditWindow_Log.ClearAll))]
    public static class Patch_EditWindow_Log_ClearAll
    {
        public static bool Prepare() => DevToolsCompatibility.ShouldPatch();

        public static void Postfix()
        {
            UIGuard.Try("Diagnostics.ModernLogClear", ModernLog.Notify_Cleared, null);
        }
    }
}
