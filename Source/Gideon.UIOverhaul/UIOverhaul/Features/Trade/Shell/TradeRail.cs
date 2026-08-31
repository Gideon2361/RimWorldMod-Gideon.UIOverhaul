using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Trade.Shell
{
    /// <summary>
    /// One entry on the left rail: a name, how many things are behind it, and the key a screen filters by.
    ///
    /// <b>The count is the reason the rail exists.</b> A list of category names tells a player nothing they could
    /// not have guessed; a list saying there are four medicines and no drugs on this ship saves them opening two
    /// of them. Vanilla's answer to the same question is to scroll.
    /// </summary>
    internal struct TradeRailEntry
    {
        /// <summary>What the screen filters by. Null makes this a group caption rather than a choice.</summary>
        internal string Key;

        /// <summary>What the player reads.</summary>
        internal string Label;

        /// <summary>How many rows are behind it. Negative hides the number entirely.</summary>
        internal int Count;

        /// <summary>Colour for the count, for a rail that wants to flag one of its entries.</summary>
        internal Color? CountColor;

        internal static TradeRailEntry Group(string label)
        {
            return new TradeRailEntry { Key = null, Label = label, Count = -1 };
        }

        internal static TradeRailEntry Of(string key, string label, int count)
        {
            return new TradeRailEntry { Key = key, Label = label, Count = count };
        }
    }

    /// <summary>
    /// The left rail: what you are looking at, and how much of it there is.
    ///
    /// <b>Groups are captions, not collapsibles.</b> A rail with eight entries in two groups does not need to
    /// fold, and something that folds is something a player can hide from themselves and then not find. The
    /// caption is drawn dim and unclickable and the entries under it behave identically to any other.
    ///
    /// <b>An entry with nothing behind it stays visible and goes dim.</b> Removing it would make the rail's
    /// contents change shape as the search box is typed into, so the thing a player is reaching for moves while
    /// they reach. Dim says the same thing and holds still. This is the same choice the comms screen makes about
    /// targets it cannot call.
    /// </summary>
    internal static class TradeRail
    {
        private const float EntryHeight = 26f;

        /// <summary>
        /// Draws the rail and returns the key the player picked, or null if they picked nothing this frame.
        ///
        /// The caller keeps the selection. This holds no state at all, which is what lets one screen use two of
        /// them -- the beacon screen's "which beacon" and "what to show" rails are the same code.
        ///
        /// <b>Now an adapter over <see cref="UIRailControl"/>.</b> This rail was the version that had been
        /// through enough revisions to be worth keeping, so it became the shared control rather than being
        /// replaced by it. What survives here is the translation from a trade entry to a rail element: the
        /// upper cased captions, and the rule that a category with nothing in it dims instead of vanishing.
        ///
        /// <see cref="TradeRailEntry"/> stays because <see cref="Pills"/> and <see cref="Segments"/> lay the
        /// same entries out in two other shapes.
        /// </summary>
        internal static string Draw(Rect rect, List<TradeRailEntry> entries, string selected,
            ref Vector2 scroll, ref bool dragging, ref float dragOffset, UIColorPaletteDef palette)
        {
            if (entries == null || entries.Count == 0)
                return null;

            palette = palette ?? UIColorPaletteDef.Active;

            if (palette == null)
                return null;

            List<UIRailElement> elements = new List<UIRailElement>(entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                TradeRailEntry entry = entries[i];

                if (entry.Key == null)
                {
                    elements.Add(new UIRailSectionHeaderControl(entry.Label)
                    {
                        Uppercase = true,
                        Color = palette.TextDisabled
                    });

                    continue;
                }

                // A category the search has emptied stays clickable and goes dim. Removing it would make the
                // rail change shape as the box is typed into, moving whatever the player was reaching for.
                bool empty = entry.Count == 0;

                elements.Add(new UIRailClickableEntry(entry.Key, entry.Label)
                {
                    Count = entry.Count,
                    CountColor = entry.CountColor,
                    TextColor = empty ? palette.TextDisabled : (Color?) null,
                    Rise = EntryHeight
                });
            }

            return UIRailControl.Draw(rect, elements, selected, ref scroll, ref dragging, ref dragOffset,
                palette, false);
        }

        /// <summary>
        /// A row of filter pills, in place of the rail, for a screen whose table wants the full width.
        ///
        /// <b>Horizontal because the table beside it grew.</b> A rail is the right shape when the middle column
        /// can spare 170 pixels; once the trade table carried two price columns and a stepper, it could not, and
        /// a row of pills across the top costs one line instead of a column. The counts go with it: eight pills
        /// each carrying a number is a line of arithmetic nobody reads, and the count that mattered -- how much
        /// is on offer at all -- is on the table's own header.
        ///
        /// <b>Wraps rather than scrolls or shrinks.</b> A trader with every category and a narrow window gets two
        /// lines of pills; the alternatives are a horizontal scroller holding filters the player cannot see, or
        /// pills too small to read. Returns the height used so the caller can put the table under whatever it
        /// came to.
        /// </summary>
        internal static string Pills(Rect rect, List<TradeRailEntry> options, string selected,
            UIColorPaletteDef palette, out float height)
        {
            height = 0f;

            if (options == null || options.Count == 0)
                return null;

            string picked = null;

            const float PillHeight = 26f;
            const float PillGap = 5f;
            const float SidePadding = 13f;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;

                float x = rect.x;
                float y = rect.y;

                for (int i = 0; i < options.Count; i++)
                {
                    TradeRailEntry option = options[i];

                    string label = option.Label ?? string.Empty;

                    float width = Text.CalcSize(label).x + SidePadding * 2f;

                    if (x > rect.x && x + width > rect.xMax)
                    {
                        x = rect.x;
                        y += PillHeight + PillGap;
                    }

                    Rect pill = new Rect(x, y, width, PillHeight);

                    bool on = option.Key == selected;
                    bool over = Mouse.IsOver(pill);

                    // The selected pill is a filled block and the rest are outlines. A wash would not survive a
                    // light theme, and an outline that merely thickened would not read across a row of eight.
                    if (on)
                    {
                        Widgets.DrawBoxSolid(pill, palette.Accent);
                    }
                    else
                    {
                        Widgets.DrawBoxSolid(pill, palette.SurfaceRaised);

                        if (over)
                            Widgets.DrawBoxSolid(pill, palette.HoverOverlay);
                    }

                    GUI.color = on ? palette.WindowBackground : palette.TextSecondary;

                    Widgets.Label(pill, label);

                    if (!on && Widgets.ButtonInvisible(pill))
                    {
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

                        picked = option.Key;
                    }

                    x += width + PillGap;
                }

                height = y + PillHeight - rect.y;
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return picked;
        }

        /// <summary>
        /// A row of segmented buttons, used for the Buy / Sell / Gift switch and its equivalents.
        ///
        /// <b>Segments rather than a dropdown</b> because there are never more than three and the whole point is
        /// that you can see which one you are not in. Returns the picked key or null.
        /// </summary>
        internal static string Segments(Rect rect, List<TradeRailEntry> options, string selected,
            UIColorPaletteDef palette)
        {
            if (options == null || options.Count == 0)
                return null;

            string picked = null;

            float width = rect.width / options.Count;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;

                for (int i = 0; i < options.Count; i++)
                {
                    TradeRailEntry option = options[i];

                    Rect segment = new Rect(rect.x + width * i, rect.y, width, rect.height);

                    bool on = option.Key == selected;
                    bool over = Mouse.IsOver(segment);

                    Widgets.DrawBoxSolid(segment, on ? palette.AccentMuted : palette.SurfaceRaised);

                    if (over && !on)
                        Widgets.DrawBoxSolid(segment, palette.HoverOverlay);

                    GUI.color = on ? palette.TextPrimary : palette.TextSecondary;

                    Widgets.Label(segment, option.Label ?? string.Empty);

                    if (on)
                        Widgets.DrawBoxSolid(new Rect(segment.x, segment.yMax - 2f, segment.width, 2f), palette.Accent);

                    if (!Widgets.ButtonInvisible(segment) || on)
                        continue;

                    SoundDefOf.Tick_High.PlayOneShotOnCamera();

                    picked = option.Key;
                }
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return picked;
        }
    }
}
