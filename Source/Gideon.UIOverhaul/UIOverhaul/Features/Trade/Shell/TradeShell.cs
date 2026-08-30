using System;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade.Shell
{
    /// <summary>
    /// The chrome and the layout every trade screen shares: a header, a rail, a table and a spine.
    ///
    /// <b>Four screens, one shape.</b> Trading, packing a caravan, choosing who to call and reading a beacon's
    /// reach are the same act at different scales -- you pick what you are looking at on the left, you work in the
    /// middle, and the thing you are assembling stands on the right where you can see all of it at once. Vanilla
    /// gives each of these its own window with its own furniture, so a player who has learned one has learned
    /// nothing about the next.
    ///
    /// <b>Why the spine is not a footer.</b> The deal, the manifest and the beacon's tally are all objects with
    /// several lines and running totals, and a footer can hold one line. Put on the right, the thing being built
    /// stays whole and visible while the table beside it scrolls, which is the single change that stops a trade
    /// being "find your own nonzero numbers in two hundred rows".
    ///
    /// <b>This file owns layout and furniture only.</b> No screen's rules live here. What it knows is where the
    /// four regions go, how a heading reads, and what the footer's buttons look like -- the parts that must not
    /// drift between screens.
    /// </summary>
    internal static class TradeShell
    {
        /// <summary>Height of the title block at the top of every screen.</summary>
        internal const float HeaderHeight = 58f;

        /// <summary>Height of the button strip at the bottom of every screen.</summary>
        internal const float FooterHeight = 46f;

        /// <summary>Width of the left rail.</summary>
        internal const float RailWidth = 172f;

        /// <summary>Width of the right spine.</summary>
        internal const float SpineWidth = 304f;

        /// <summary>Clear space between the three body columns.</summary>
        internal const float Gap = 12f;

        /// <summary>
        /// Height of one row in a table whose rows carry a second line under the name.
        ///
        /// <b>Derived rather than written down, because 34 was wrong and could not have been right.</b> Reported
        /// on 2026-08-25 as clipped text: a trade row stacks a Small line over a Tiny one, and RimWorld computes
        /// both heights at run time from the loaded font -- <c>Text.lineHeights[i] = CalcHeight("W", 999f)</c> --
        /// so they move with the font, with the UI scale and with a translation that ships its own. Any constant
        /// here is a guess about somebody else's display, and the guess was four pixels short on this one.
        /// </summary>
        internal static float RowHeight
        {
            get
            {
                return UIFonts.LineHeightOf(GameFont.Small) + UIFonts.LineHeightOf(GameFont.Tiny) + 6f;
            }
        }

        /// <summary>
        /// Height of one row in a table whose rows are a single line.
        ///
        /// The caravan and split tables put everything on one line, so giving them the two-line height would be
        /// a third of every row left empty and a third fewer rows on screen.
        /// </summary>
        internal static float CompactRowHeight
        {
            get { return UIFonts.LineHeightOf(GameFont.Small) + 10f; }
        }

        /// <summary>
        /// Splits a row into the Small line and the Tiny line beneath it, centred as a pair.
        ///
        /// <b>One method so that two columns cannot disagree.</b> The name and its note, and the price and its
        /// word, are two independent stacks on the same row; each measuring its own offset is how they end up a
        /// pixel or two apart, which reads as a wobble down the table. Centring the pair rather than pinning it
        /// to the top also means a row that is taller than it needs to be looks deliberate.
        /// </summary>
        internal static void TwoLine(Rect rect, out Rect first, out Rect second)
        {
            float top = UIFonts.LineHeightOf(GameFont.Small);
            float bottom = UIFonts.LineHeightOf(GameFont.Tiny);

            float y = rect.y + Mathf.Max(0f, Mathf.Round((rect.height - top - bottom) * 0.5f));

            first = new Rect(rect.x, y, rect.width, top);
            second = new Rect(rect.x, y + top, rect.width, bottom);
        }

        /// <summary>Height of the column caption strip above a table.</summary>
        internal const float ColumnsHeight = 22f;

        /// <summary>
        /// Splits a window's contents into the four regions.
        ///
        /// <b>The rail and the spine are fixed and the table takes what is left,</b> rather than all three taking
        /// a share. A rail holds category names and a spine holds a running total: neither reads better for being
        /// wider, and both read badly for being narrower. The table is the only part whose usefulness grows with
        /// the space, so it gets the slack.
        ///
        /// A screen that wants no rail passes false and the table starts at the left edge. Nothing is left drawing
        /// into a strip of nothing, which is what reserving the space unconditionally would do.
        /// </summary>
        internal static void Layout(Rect inRect, bool rail, bool spine,
            out Rect headerRect, out Rect railRect, out Rect tableRect, out Rect spineRect, out Rect footerRect)
        {
            headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);

            footerRect = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);

            float top = headerRect.yMax + Gap;
            float height = Mathf.Max(0f, footerRect.y - Gap - top);

            float left = inRect.x;
            float right = inRect.xMax;

            railRect = new Rect(left, top, RailWidth, height);

            if (rail)
                left += RailWidth + Gap;

            spineRect = new Rect(right - SpineWidth, top, SpineWidth, height);

            if (spine)
                right -= SpineWidth + Gap;

            tableRect = new Rect(left, top, Mathf.Max(0f, right - left), height);
        }

        /// <summary>
        /// How wide a title will actually draw, for anything sharing its line.
        ///
        /// Exposed because the title's font lives here and the callers sharing that line should not have to know
        /// it. The standing line beside the trade title used to take a flat share of the row instead of what the
        /// title left behind, which truncated it while empty space sat between the two. Reported 2026-08-29.
        /// </summary>
        internal static float TitleWidth(string title)
        {
            GameFont previous = Text.Font;

            Text.Font = GameFont.Medium;

            float width = Text.CalcSize(title ?? string.Empty).x;

            Text.Font = previous;

            return width;
        }

        /// <summary>
        /// The title block: who you are dealing with, and one line of context under it.
        /// </summary>
        /// <param name="detail">
        /// The subtitle. Everything a player needs to judge the screen before reading a single row -- the trader's
        /// kind, the route's length, how long the ship stays. Kept to one line on purpose: a second line of
        /// context is a line nobody reads.
        /// </param>
        internal static void Header(Rect rect, string title, string detail, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                // Wrapping off around both lines. The title is a trader's name and the detail is a run of short
                // facts, and either can be longer than the window on a bad day; wrapped, they grow downwards into
                // the rail and the table rather than being cut where they run out of room.
                Text.WordWrap = false;

                float titleHeight = UIFonts.LineHeightOf(GameFont.Medium);

                Widgets.LabelEllipses(new Rect(rect.x, rect.y, rect.width - 260f, titleHeight), title ?? string.Empty);

                Text.Font = GameFont.Small;
                GUI.color = palette.TextSecondary;

                if (!detail.NullOrEmpty())
                {
                    Widgets.LabelEllipses(
                        new Rect(rect.x, rect.y + titleHeight + 2f, rect.width - 260f, UIFonts.LineHeightOf(GameFont.Small)),
                        detail);
                }

                GUI.color = palette.Border;

                Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
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
        /// A section caption, drawn under a rule. The same one the taming and hunting bills use, so a heading
        /// means the same thing everywhere in the mod.
        /// </summary>
        internal static float Heading(Rect rect, float y, string text, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                GUI.color = palette.Border;

                Widgets.DrawLineHorizontal(rect.x, y, rect.width);

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(rect.x, y + 4f, rect.width, UIFonts.LineHeightOf(GameFont.Tiny)), text);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            return y + UIFonts.LineHeightOf(GameFont.Tiny) + 8f;
        }

        /// <summary>Wrapped explanatory text under a heading. Returns the y below it.</summary>
        internal static float Note(Rect rect, float y, string text, UIColorPaletteDef palette,
            GameFont font = GameFont.Tiny)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = font;
                GUI.color = palette.TextSecondary;

                float height = Text.CalcHeight(text, rect.width);

                Widgets.Label(new Rect(rect.x, y, rect.width, height), text);

                return y + height + 6f;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// One caption above a table column.
        ///
        /// Tiny and dim, and anchored to match the cells beneath it: a right-aligned number under a left-aligned
        /// caption reads as belonging to the column beside it.
        /// </summary>
        internal static void Column(Rect rect, string text, UIColorPaletteDef palette,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = anchor;
                GUI.color = palette.TextDisabled;
                Text.WordWrap = false;

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

        /// <summary>
        /// The banding and hover wash under a table row.
        ///
        /// <b>Drawn from the row's own index rather than its position on screen,</b> so the stripes stay attached
        /// to the rows as the list scrolls instead of shimmering under them.
        /// </summary>
        internal static void RowBackground(Rect rect, int index, bool selected, UIColorPaletteDef palette)
        {
            if (index % 2 == 1)
                Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            if (selected)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);

            if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);
        }

        /// <summary>
        /// A key hint for the footer: the key in a chip, then what it does.
        ///
        /// Returns the width used, so a caller can lay several out in a row without measuring them itself.
        /// </summary>
        internal static float KeyHint(Rect rect, float x, string key, string what, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;

                float keyWidth = Text.CalcSize(key).x + 12f;

                Rect chip = new Rect(x, rect.y + (rect.height - 18f) * 0.5f, keyWidth, 18f);

                Widgets.DrawBoxSolid(chip, palette.SurfaceRaised);

                GUI.color = palette.TextSecondary;

                Widgets.Label(chip, key);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextDisabled;

                float whatWidth = Text.CalcSize(what).x + 4f;

                Widgets.Label(new Rect(chip.xMax + 5f, rect.y, whatWidth, rect.height), what);

                return keyWidth + 5f + whatWidth + 14f;
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
        /// A line of the shape "label ....... value", with the value allowed its own colour.
        ///
        /// The spine is made almost entirely of these, so the split is fixed here rather than chosen per caller:
        /// a set of these lines only reads as a table if every one of them breaks in the same place.
        /// </summary>
        internal static float Readout(Rect rect, float y, string label, string value, UIColorPaletteDef palette,
            Color? valueColor = null, GameFont font = GameFont.Small)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            float height = UIFonts.RowHeight(font, 4f);

            try
            {
                Text.Font = font;
                Text.WordWrap = false;

                float split = Mathf.Round(rect.width * 0.52f);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                Widgets.LabelEllipses(new Rect(rect.x, y, split - 4f, height), label);

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = valueColor ?? palette.TextPrimary;

                Widgets.LabelEllipses(new Rect(rect.x + split, y, rect.width - split, height), value);
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return y + height;
        }

        /// <summary>
        /// Draws the body of a screen behind the framework's guard.
        ///
        /// Every one of these windows drives a live transaction, so a draw that throws must not leave the player
        /// looking at an empty box with no way to tell whether the deal is still there. The notice names the log
        /// site, and the sentence says what is safe to do next.
        /// </summary>
        internal static void Guarded(string site, Rect inRect, Action body, string consequence)
        {
            UIGuardedPanel.Draw(site, inRect, body, consequence);
        }

        /// <summary>
        /// A pill saying something short and coloured about a row -- "standing", "spoils before arrival",
        /// "not on a trade beacon".
        ///
        /// Returns the width it took. Drawn from the right edge of <paramref name="rect"/> backwards so a row can
        /// stack several without knowing how wide any of them are.
        /// </summary>
        internal static float Pill(Rect rect, float right, string text, Color color, UIColorPaletteDef palette)
        {
            if (text.NullOrEmpty())
                return 0f;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;

                float width = Text.CalcSize(text).x + 12f;

                Rect pill = new Rect(right - width, rect.y + (rect.height - 17f) * 0.5f, width, 17f);

                Widgets.DrawBoxSolid(pill, new Color(color.r, color.g, color.b, 0.18f));

                GUI.color = color;

                Widgets.Label(pill, text);

                return width + 5f;
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
        /// The three footer buttons every screen ends with: the commit, the undo and the way out.
        ///
        /// <b>The commit's label is a sentence about what will happen,</b> not "OK". A caravan that is over
        /// capacity says <c>Fix the load</c> and refuses; a deal the trader cannot fund still says Accept, because
        /// vanilla will ask about that itself and taking the decision away would be ours to answer for.
        ///
        /// <b>Reset, Cancel, Accept, in that order left to right.</b> The commit is the one button the player is
        /// reaching for, so it takes the corner -- the end of the row, where the pointer already is after reading
        /// down the table, and the position every dialog in the game puts its confirm in. Reset sits furthest from
        /// it because it is the most destructive of the three: it throws away the deal without closing anything,
        /// and it is the one a misjudged click should be least able to find. Reordered on 2026-08-25.
        /// </summary>
        internal static void Footer(Rect rect, UIColorPaletteDef palette, string commitLabel, bool commitEnabled,
            Action commit, Action reset, Action cancel)
        {
            const float ButtonWidth = 148f;
            const float ButtonHeight = 34f;

            float y = rect.y + (rect.height - ButtonHeight) * 0.5f;

            // Laid out from the right so the commit keeps the corner whatever else is present. A screen that
            // passes no reset closes the gap rather than leaving a hole where a button is not.
            Rect commitRect = new Rect(rect.xMax - ButtonWidth, y, ButtonWidth, ButtonHeight);

            float x = commitRect.x;

            Rect cancelRect = new Rect(0f, y, ButtonWidth, ButtonHeight);

            if (cancel != null)
            {
                x -= ButtonWidth + 8f;

                cancelRect.x = x;
            }

            Rect resetRect = new Rect(0f, y, ButtonWidth, ButtonHeight);

            if (reset != null)
            {
                x -= ButtonWidth + 8f;

                resetRect.x = x;
            }

            // <b>The commit is the only filled one.</b> It is what the window is for; Reset and Cancel are the
            // ways out of it and are peers of each other, so they are outlines. Two filled buttons on one row
            // emphasize nothing.
            if (reset != null
                && UIActionButtonControl.Draw(resetRect, "ResetButton".Translate(), palette))
                reset();

            if (cancel != null
                && UIActionButtonControl.Draw(cancelRect, "CancelButton".Translate(), palette))
                cancel();

            if (UIActionButtonControl.Draw(commitRect, commitLabel, palette, true, commitEnabled) && commit != null)
                commit();
        }
    }
}
