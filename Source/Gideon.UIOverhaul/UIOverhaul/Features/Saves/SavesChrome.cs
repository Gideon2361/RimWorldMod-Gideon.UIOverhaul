using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
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
        /// Restores the anchor and colour to what it found rather than to a guess: <c>Text.StartOfOnGUI</c>
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
        /// centred label with no caret is a button, and a button does not say that a list will appear.
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

        /// <summary>The strip a save window's actions sit on: a rule, and the panel colour behind it.</summary>
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
