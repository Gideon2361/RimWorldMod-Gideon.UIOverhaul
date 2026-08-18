using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// The pieces every save window draws the same way.
    ///
    /// <b>Shared because the first version was not.</b> The buttons, the size formatting and the footer were
    /// written separately in three windows, and they immediately disagreed: the primary button came out muted
    /// in one and accented in another, which is how a set of windows stops looking like one feature. One
    /// definition per element is the only arrangement that survives a fourth window being added.
    /// </summary>
    internal static class SavesChrome
    {
        /// <summary>
        /// Closes this mod's settings window, if it happens to be open.
        ///
        /// <b>Because it sits over the saving notice.</b> Saving runs as a long event that draws its own
        /// centered message, and our settings window is drawn above it with the screen behind absorbed: the
        /// player pressed Save, the notice was covered, and to them nothing at all happened. A game that is
        /// busy looks identical to a game that has hung, and the second reading is the one people act on.
        ///
        /// Called from both save windows rather than from the buttons that open them, so it holds no matter how
        /// they were reached, including Escape and the interception of vanilla's own save dialog.
        /// </summary>
        internal static void CloseSettingsWindow()
        {
            UIGuard.Try("Saves.CloseSettings", () =>
            {
                WindowStack stack = Find.WindowStack;

                if (stack == null)
                    return;

                Window options = stack.WindowOfType<Dialog_UIOptions>();

                if (options != null)
                    stack.TryRemove(options, false);
            }, "The settings window stays open behind this one.");
        }

        /// <summary>Height of the action strip at the bottom of a save window.</summary>
        internal const float FooterHeight = 52f;

        /// <summary>
        /// A button.
        ///
        /// <b>Primary is filled at full accent, always.</b> An earlier version filled it with
        /// <c>AccentMuted</c> and only reached the accent on hover, which made the one button the window is
        /// built around look like the disabled one until the cursor touched it. Muted accent is a resting
        /// surface, not a call to action.
        /// </summary>
        internal static bool Button(Rect rect, string label, UIColorPaletteDef palette, bool primary = false)
        {
            bool over = Mouse.IsOver(rect);

            if (primary)
            {
                UIElementPainter.FillRounded(rect, palette.Accent);

                if (over)
                    UIElementPainter.FillRounded(rect, palette.HoverOverlay);
            }
            else
            {
                UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));
            }

            Write(rect, label, primary ? palette.WindowBackground : palette.TextPrimary, GameFont.Small);

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>
        /// A button that cannot be pressed, in the palette's vocabulary for one.
        ///
        /// Restores the anchor and color to what it found rather than to a guess: <c>Text.StartOfOnGUI</c>
        /// checks that state each frame and complains once when it was left changed.
        /// </summary>
        internal static void Disabled(Rect rect, string label, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.ControlBackgroundFaded);

            Write(rect, label, palette.TextDisabled, GameFont.Small);
        }

        /// <summary>
        /// A drop-down field: a label on the left, a caret on the right.
        ///
        /// Reads as a picker rather than a button, which matters next to the name box it sits beside. A
        /// centered label with no caret is a button, and a button does not say that a list will appear.
        /// </summary>
        internal static bool Picker(Rect rect, string label, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.OutlineRounded(rect, over ? palette.BorderFocused : palette.Border,
                palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextPrimary;

            Rect text = new Rect(rect.x + 10f, rect.y, Mathf.Max(0f, rect.width - 28f), rect.height);

            if (text.width >= 24f)
                Widgets.LabelEllipses(text, label);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 9f, rect.height), "▾");

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>Height of the per-save action row, in both windows.</summary>
        internal const float ActionRowHeight = 26f;

        /// <summary>
        /// How long an armed delete stays armed with nothing happening.
        ///
        /// Long enough to read the name and move the mouse, short enough that walking away from the keyboard
        /// never leaves a destructive button one click from being pressed.
        /// </summary>
        private const float ArmedSeconds = 4f;

        /// <summary>
        /// Which save a window has armed for deletion, and since when.
        ///
        /// <b>State per window rather than per row,</b> because only one save can be armed at a time and the
        /// window is what owns "which one". Held by full path rather than by <c>FileInfo</c>, since the list is
        /// rebuilt from disk and the instance a click armed will not be the instance drawn next frame.
        /// </summary>
        internal sealed class ArmedDelete
        {
            private string path;
            private float since;

            internal bool IsArmed(string forPath)
            {
                if (path == null || forPath == null)
                    return false;

                if (!string.Equals(path, forPath, StringComparison.OrdinalIgnoreCase))
                    return false;

                // Real time rather than game time, so this still expires while the game is paused -- which it
                // always is, since these windows pause it.
                if (Time.realtimeSinceStartup - since <= ArmedSeconds)
                    return true;

                path = null;

                return false;
            }

            internal void Arm(string forPath)
            {
                path = forPath;
                since = Time.realtimeSinceStartup;
            }

            internal void Disarm()
            {
                path = null;
            }
        }

        /// <summary>What a save window's action row asked for this frame.</summary>
        internal enum SaveAction
        {
            None,
            Rename,
            Move,
            Sweep,
            Delete
        }

        /// <summary>
        /// Rename, Move and Delete for one save, with the delete confirming in place.
        ///
        /// <b>The armed state takes over the whole row and names the save.</b> That naming is the reason to
        /// confirm at all: the risk worth guarding against is deleting the wrong save, not clicking by
        /// accident, so a confirmation that does not say which one is barely a confirmation. Nothing moves and
        /// no window opens, which is what makes it cheap enough to need no way of switching it off.
        ///
        /// <b>Shared between both windows on purpose.</b> The save and load windows are one feature with a mode
        /// bar between them, and an earlier round of this code proved what happens when the same element is
        /// written twice: the two copies disagree within a day.
        /// </summary>
        /// <param name="disabledWhy">Non-null disables all three and explains why instead.</param>
        internal static SaveAction ActionRow(Rect rect, string savePath, string saveName, ArmedDelete armed,
            UIColorPaletteDef palette, string disabledWhy)
        {
            if (!disabledWhy.NullOrEmpty())
            {
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextDisabled;

                if (rect.width >= 24f)
                    Widgets.LabelEllipses(rect, disabledWhy);

                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;

                return SaveAction.None;
            }

            return armed.IsArmed(savePath)
                ? DrawArmed(rect, saveName, armed, palette)
                : DrawIdle(rect, savePath, armed, palette);
        }

        private static SaveAction DrawIdle(Rect rect, string savePath, ArmedDelete armed,
            UIColorPaletteDef palette)
        {
            float width = Mathf.Min(96f, (rect.width - 18f) / 4f);

            Rect rename = new Rect(rect.x, rect.y, width, rect.height);
            Rect move = new Rect(rename.xMax + 6f, rect.y, width, rect.height);
            Rect sweep = new Rect(move.xMax + 6f, rect.y, width, rect.height);
            Rect delete = new Rect(sweep.xMax + 6f, rect.y, width, rect.height);

            if (width < 40f)
                return SaveAction.None;

            if (Small(rename, "Rename", palette, palette.TextPrimary))
                return SaveAction.Rename;

            if (Small(move, "Move", palette, palette.TextPrimary))
                return SaveAction.Move;

            // Sits before Delete rather than after it, so the destructive one stays last in the row.
            if (Small(sweep, "Sweep", palette, palette.TextPrimary))
                return SaveAction.Sweep;

            // Tinted rather than filled. A permanently red button in a row somebody reads every time they open
            // the window is alarm fatigue; the fill arrives once it is armed and means something.
            if (Small(delete, "Delete", palette, palette.Danger))
                armed.Arm(savePath);

            return SaveAction.None;
        }

        private static SaveAction DrawArmed(Rect rect, string saveName, ArmedDelete armed,
            UIColorPaletteDef palette)
        {
            Rect cancel = new Rect(rect.xMax - 76f, rect.y, 76f, rect.height);
            Rect confirm = new Rect(cancel.x - 86f, rect.y, 80f, rect.height);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.Danger;

            Rect asked = new Rect(rect.x, rect.y, Mathf.Max(0f, confirm.x - rect.x - 8f), rect.height);

            if (asked.width >= 24f)
                Widgets.LabelEllipses(asked, "Delete " + saveName + "?");

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Small(cancel, "Cancel", palette, palette.TextSecondary))
            {
                armed.Disarm();

                return SaveAction.None;
            }

            if (!Filled(confirm, "Delete", palette, palette.Danger))
                return SaveAction.None;

            armed.Disarm();

            return SaveAction.Delete;
        }

        /// <summary>A quiet outlined button sized for an action row.</summary>
        private static bool Small(Rect rect, string label, UIColorPaletteDef palette, Color text)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.OutlineRounded(rect, over ? palette.BorderFocused : palette.Border,
                palette.PanelBackground);

            if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Write(rect, label, text, GameFont.Tiny);

            return Widgets.ButtonInvisible(rect);
        }

        private static bool Filled(Rect rect, string label, UIColorPaletteDef palette, Color fill)
        {
            UIElementPainter.FillRounded(rect, fill);

            if (Mouse.IsOver(rect))
                UIElementPainter.FillRounded(rect, palette.HoverOverlay);

            Write(rect, label, palette.WindowBackground, GameFont.Tiny);

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>The strip a save window's actions sit on: a rule, and the panel color behind it.</summary>
        internal static void Footer(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Color previous = GUI.color;
            GUI.color = palette.Border;

            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);

            GUI.color = previous;
        }

        /// <summary>
        /// A small uppercase caption over a field.
        ///
        /// <b>Grown to the font's own line height, because Widgets.Label clips.</b> Callers naturally hand a
        /// caption a 16 pixel row, and Tiny renders taller than that, so the tops of every capital were being
        /// sliced off. IMGUI does not overflow a label out of its rect; it cuts it. Measuring rather than
        /// hardcoding a taller number means this stays right if the UI scale or the font changes underneath.
        /// </summary>
        internal static void Caption(Rect rect, string text, UIColorPaletteDef palette)
        {
            float line = UIFonts.LineHeightOf(GameFont.Tiny);

            Rect fit = new Rect(rect.x, rect.y, rect.width, Mathf.Max(rect.height, line));

            Write(fit, text, palette.TextDisabled, GameFont.Tiny, TextAnchor.MiddleLeft);
        }

        internal static string Size(long bytes)
        {
            return bytes >= 1048576L
                ? (bytes / 1048576f).ToString("F1") + " MB"
                : Mathf.Max(1f, bytes / 1024f).ToString("F0") + " KB";
        }

        /// <summary>
        /// How long ago, in words.
        ///
        /// <b>Relative rather than a timestamp,</b> because the question being asked of this column is "is
        /// this the one I was just playing", and nobody answers that by reading a date. The exact time is on
        /// the tooltip for the rare case where it is wanted.
        /// </summary>
        internal static string Ago(DateTime when)
        {
            TimeSpan since = DateTime.Now - when;

            if (since.TotalSeconds < 90)
                return "just now";

            if (since.TotalMinutes < 60)
                return Mathf.RoundToInt((float) since.TotalMinutes) + " minutes ago";

            if (since.TotalHours < 24)
            {
                int hours = Mathf.RoundToInt((float) since.TotalHours);

                return hours == 1 ? "an hour ago" : hours + " hours ago";
            }

            int days = Mathf.RoundToInt((float) since.TotalDays);

            if (days == 1)
                return "yesterday";

            return days < 60 ? days + " days ago" : when.ToString("d");
        }

        private static void Write(Rect rect, string label, Color color, GameFont font,
            TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = font;
            Text.Anchor = anchor;
            GUI.color = color;

            Widgets.Label(rect, label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }
    }
}
