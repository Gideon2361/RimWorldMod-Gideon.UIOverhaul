using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Factions
{
    /// <summary>
    /// What a row opens: the three things vanilla builds on hover, written out.
    ///
    /// <b>Every figure here already exists and is thrown away sixty times a second.</b> Hovering the standing
    /// column assembles the ongoing events and the recent events; hovering the natural goodwill rectangle
    /// beside it assembles the breakdown of the resting value. Three separate hovers over two rectangles, each
    /// giving up its answer only while the cursor is still, and none of them readable next to the number they
    /// explain.
    ///
    /// <b>The card unfolds under its row rather than replacing the list.</b> The question it answers is about
    /// one faction among several, and an answer that hides the comparison it came from is half an answer.
    ///
    /// <b>A section with nothing in it says so.</b> "Nothing is holding it down" is the answer to why a
    /// standing stopped climbing just as much as a list of quarrels is, and a panel that vanishes when empty
    /// leaves the player unsure whether they read it wrong.
    /// </summary>
    internal static class FactionCard
    {
        private const float Pad = 12f;
        private const float Gap = 8f;
        private const float LedgerRow = 19f;
        private const float LedgerHead = 20f;
        private const float FactRow = 24f;
        private const float ButtonHeight = 26f;
        private const float ScaleBlock = 60f;

        /// <summary>How many of the year's events are listed before the rest are summed into one line.</summary>
        private const int MovedShown = 7;

        private static readonly List<GoodwillEntry> Held = new List<GoodwillEntry>();

        private static readonly List<GoodwillEntry> Resting = new List<GoodwillEntry>();

        private static readonly List<GoodwillEntry> Moved = new List<GoodwillEntry>();

        private static Faction cachedFor;

        private static int cachedFrame = -1;

        private static int quests;

        /// <summary>
        /// Reads the three ledgers, once a frame for the one faction that needs them.
        ///
        /// <b>Cached on the frame rather than on nothing,</b> because the height of the card and the drawing of
        /// it are two passes over the same data and walking every history event def twice a frame would double
        /// a cost that is already the most expensive thing on the tab.
        /// </summary>
        private static void Ensure(FactionRow row)
        {
            if (cachedFor == row.faction && cachedFrame == Time.frameCount)
                return;

            cachedFor = row.faction;
            cachedFrame = Time.frameCount;

            FactionsFacts.Ceilings(row.faction, Held);
            FactionsFacts.Resting(row.faction, Resting);
            FactionsFacts.Moved(row.faction, Moved);

            quests = FactionActions.QuestCount(row.faction);
        }

        internal static float HeightOf(FactionRow row)
        {
            Ensure(row);

            float left = Pad;

            if (row.hasGoodwill)
                left += ScaleBlock + Gap;

            left += Ledger(Held.Count) + Gap;
            left += Ledger(Resting.Count) + Gap;
            left += Ledger(Mathf.Min(Moved.Count, MovedShown) + (Moved.Count > MovedShown ? 1 : 0));
            left += Pad;

            float right = Pad + LedgerHead + Facts(row) * FactRow + Gap + LedgerHead
                          + ButtonHeight * 2f + 6f + Pad;

            return Mathf.Max(left, right);
        }

        /// <summary>A ledger's height: its heading, then its rows, or one line saying it is empty.</summary>
        private static float Ledger(int rows)
        {
            return LedgerHead + Mathf.Max(1, rows) * LedgerRow;
        }

        private static int Facts(FactionRow row)
        {
            int count = 2;

            if (row.ideo != null)
                count++;

            if (row.enemies != null && row.enemies.Count > 0)
                count++;

            if (quests > 0)
                count++;

            return count;
        }

        internal static float Draw(Rect rect, FactionRow row, UIColorPaletteDef palette)
        {
            Ensure(row);

            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width, palette.Border);

            float rightWidth = Mathf.Clamp(rect.width * 0.34f, 220f, 300f);

            Rect left = new Rect(rect.x, rect.y + Pad, rect.width - rightWidth - Pad,
                rect.height - Pad * 2f);

            Rect right = new Rect(left.xMax + Pad, rect.y + Pad, rightWidth, rect.height - Pad * 2f);

            float y = left.y;

            if (row.hasGoodwill)
            {
                Scale(new Rect(left.x, y, left.width, ScaleBlock), row, palette);

                y += ScaleBlock + Gap;
            }

            y = Block(new Rect(left.x, y, left.width, 0f), "Holding it down",
                row.ceiling < 100 ? palette.Warning : palette.TextSecondary, Held,
                row.hasGoodwill
                    ? "Nothing is. This standing can reach " + FactionsFacts.Signed(100) + "."
                    : "There is no standing to hold down.",
                palette, 0) + Gap;

            y = Block(new Rect(left.x, y, left.width, 0f), "Why it rests where it does",
                palette.TextSecondary, Resting,
                row.hasGoodwill
                    ? "Nothing pulls them either way. The band sits on zero."
                    : "They have no resting value.",
                palette, 0) + Gap;

            Block(new Rect(left.x, y, left.width, 0f), "What moved it this year", palette.TextSecondary,
                Moved, "Nothing you did in the last year changed their opinion.", palette, MovedShown);

            float cursor = Facts(new Rect(right.x, right.y, right.width, 0f), row, palette);

            Actions(new Rect(right.x, cursor + Gap, right.width, LedgerHead + ButtonHeight * 2f + 6f), row,
                palette);

            return rect.yMax;
        }

        // -------------------------------------------------------------------------------------------
        // The larger scale
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The same control the row draws, at a size that can carry an axis.
        ///
        /// The heading is a sentence rather than a label because the sentence is the correction: a standing
        /// inside its band is resting, and the pair of numbers vanilla shows says nothing of the kind.
        /// </summary>
        private static void Scale(Rect rect, FactionRow row, UIColorPaletteDef palette)
        {
            TabParts.RowLabel(new Rect(rect.x, rect.y, rect.width, 16f), Verdict(row),
                row.drifting ? palette.Warning : palette.TextSecondary, GameFont.Tiny, FactionsFaces.Body,
                FactionsFaces.Size.Body);

            FactionsPanel.Scale(new Rect(rect.x, rect.y + 16f, rect.width, 26f), row, palette, true);

            Rect axis = new Rect(rect.x, rect.y + 44f, rect.width, 14f);

            TabParts.RowLabel(axis, FactionsFacts.Signed(-100), palette.TextDisabled, GameFont.Tiny,
                FactionsFaces.Mono, FactionsFaces.Size.Label);

            Centered(axis, "0", palette);
            Right(axis, FactionsFacts.Signed(100), palette);
        }

        /// <summary>The one line that says what the scale under it means.</summary>
        private static string Verdict(FactionRow row)
        {
            if (row.drifting)
            {
                return "Outside its band, so it is pulled "
                       + (row.driftDirection > 0 ? "up " : "down ") + FactionsFacts.DriftStep
                       + " every 50 days until it is back inside";
            }

            if (row.ceiling < 100 && row.stored > row.goodwill)
            {
                return "Inside its band, but clipped: " + FactionsFacts.Signed(row.stored)
                       + " underneath, shown as " + FactionsFacts.Signed(row.goodwill);
            }

            return "Inside its band. Nothing is moving it on its own";
        }

        private static void Centered(Rect rect, string text, UIColorPaletteDef palette)
        {
            Aligned(rect, text, TextAnchor.MiddleCenter, palette);
        }

        private static void Right(Rect rect, string text, UIColorPaletteDef palette)
        {
            Aligned(rect, text, TextAnchor.MiddleRight, palette);
        }

        private static void Aligned(Rect rect, string text, TextAnchor anchor, UIColorPaletteDef palette)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = anchor;
                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(rect, text, FactionsFaces.Mono, FactionsFaces.Size.Label);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        // -------------------------------------------------------------------------------------------
        // Ledgers
        // -------------------------------------------------------------------------------------------

        /// <param name="cap">
        /// The most rows to list before the rest are summed into one line. Zero lists everything, which is
        /// what the two short ledgers want: a faction is held down by one or two things and its resting value
        /// has a handful of terms, while a year of events can run to twenty.
        /// </param>
        private static float Block(Rect rect, string title, Color tint, List<GoodwillEntry> entries,
            string empty, UIColorPaletteDef palette, int cap)
        {
            int rows = entries.Count;
            int listed = cap > 0 ? Mathf.Min(rows, cap) : rows;
            int hidden = rows - listed;

            float height = Ledger(listed + (hidden > 0 ? 1 : 0));

            Rect box = new Rect(rect.x, rect.y, rect.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.SurfaceSunken);

            TabParts.RowLabel(new Rect(box.x + 10f, box.y, box.width - 20f, LedgerHead),
                FactionsFaces.Caps(title), tint, GameFont.Tiny, FactionsFaces.Mono,
                FactionsFaces.Size.BlockHead);

            float y = box.y + LedgerHead;

            if (rows == 0)
            {
                TabParts.RowLabel(new Rect(box.x + 10f, y, box.width - 20f, LedgerRow), empty,
                    palette.TextDisabled, GameFont.Tiny, FactionsFaces.Body, FactionsFaces.Size.Sub);

                return box.yMax;
            }

            for (int i = 0; i < listed; i++)
            {
                Entry(new Rect(box.x + 10f, y, box.width - 20f, LedgerRow), entries[i], palette);

                y += LedgerRow;
            }

            if (hidden > 0)
            {
                int rest = 0;

                for (int i = listed; i < rows; i++)
                    rest += entries[i].amount;

                Entry(new Rect(box.x + 10f, y, box.width - 20f, LedgerRow), new GoodwillEntry
                {
                    label = hidden + " smaller things",
                    amount = rest
                }, palette);
            }

            return box.yMax;
        }

        private static void Entry(Rect band, GoodwillEntry entry, UIColorPaletteDef palette)
        {
            string figure = entry.ceiling
                ? "max " + FactionsFacts.Signed(entry.amount)
                : FactionsFacts.Signed(entry.amount);

            float width = UITextControl.Width(figure, FactionsFaces.Mono, FactionsFaces.Size.Figure) + 8f;

            string label = entry.count > 1 ? entry.label + "  x" + entry.count : entry.label;

            TabParts.RowLabel(new Rect(band.x, band.y, Mathf.Max(0f, band.width - width), band.height), label,
                palette.TextSecondary, GameFont.Tiny, FactionsFaces.Body, FactionsFaces.Size.Body);

            Color tint = entry.ceiling
                ? palette.Warning
                : entry.amount > 0
                    ? palette.Success
                    : palette.Danger;

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = tint;

                UITextControl.LabelEllipses(new Rect(band.xMax - width, band.y, width, band.height), figure,
                    FactionsFaces.Mono, FactionsFaces.Size.Figure);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        // -------------------------------------------------------------------------------------------
        // Facts and actions
        // -------------------------------------------------------------------------------------------

        private static float Facts(Rect rect, FactionRow row, UIColorPaletteDef palette)
        {
            float height = LedgerHead + Facts(row) * FactRow;

            Rect box = new Rect(rect.x, rect.y, rect.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.SurfaceSunken);

            TabParts.RowLabel(new Rect(box.x + 10f, box.y, box.width - 20f, LedgerHead),
                FactionsFaces.Caps("Who they are"), palette.TextSecondary, GameFont.Tiny, FactionsFaces.Mono,
                FactionsFaces.Size.BlockHead);

            float y = box.y + LedgerHead;

            y = Fact(new Rect(box.x + 10f, y, box.width - 20f, FactRow), "Kind", row.kind, palette);

            y = Fact(new Rect(box.x + 10f, y, box.width - 20f, FactRow), "Leader",
                row.leader.NullOrEmpty() ? "none" : row.leader, palette);

            if (row.ideo != null)
            {
                y = Fact(new Rect(box.x + 10f, y, box.width - 20f, FactRow), "Ideoligion", row.ideo.name,
                    palette);
            }

            if (row.enemies != null && row.enemies.Count > 0)
            {
                y = Fact(new Rect(box.x + 10f, y, box.width - 20f, FactRow), "At war with",
                    row.enemies.Count + (row.enemies.Count == 1 ? " faction" : " factions"), palette);
            }

            if (quests > 0)
            {
                Fact(new Rect(box.x + 10f, y, box.width - 20f, FactRow), "Quests",
                    quests + (quests == 1 ? " open quest" : " open quests"), palette);
            }

            return box.yMax;
        }

        private static float Fact(Rect band, string key, string value, UIColorPaletteDef palette)
        {
            const float keyWidth = 78f;

            TabParts.RowLabel(new Rect(band.x, band.y, keyWidth, band.height), FactionsFaces.Caps(key),
                palette.TextDisabled, GameFont.Tiny, FactionsFaces.Mono, FactionsFaces.Size.Label);

            TabParts.RowLabel(new Rect(band.x + keyWidth, band.y, band.width - keyWidth, band.height), value,
                palette.TextPrimary, GameFont.Small, FactionsFaces.Condensed, FactionsFaces.Size.Sub);

            return band.yMax;
        }

        /// <summary>
        /// The four things this screen can do, with the reasons a call is refused written on the button.
        ///
        /// <b>A refused button says why on hover rather than disappearing.</b> "Nobody here can get to the
        /// console and talk" is the answer to a question the player is about to ask, and a button that is not
        /// there answers nothing.
        /// </summary>
        private static void Actions(Rect rect, FactionRow row, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            TabParts.RowLabel(new Rect(rect.x + 10f, rect.y, rect.width - 20f, LedgerHead),
                FactionsFaces.Caps("Do something"), palette.TextSecondary, GameFont.Tiny, FactionsFaces.Mono,
                FactionsFaces.Size.BlockHead);

            float half = (rect.width - 26f) * 0.5f;
            float x = rect.x + 10f;
            float y = rect.y + LedgerHead;

            string refusal = FactionActions.CallProblem(row.faction);

            if (TabParts.Button(new Rect(x, y, half, ButtonHeight), "Call them", palette, refusal == null,
                    false, refusal))
            {
                FactionActions.Call(row.faction);
            }

            bool mapped = row.settlements > 0;

            if (TabParts.Button(new Rect(x + half + 6f, y, half, ButtonHeight), "Show on map", palette,
                    mapped, false, mapped ? null : "They hold no settlements to look at."))
            {
                FactionActions.ShowOnMap(row.faction);
            }

            y += ButtonHeight + 6f;

            if (TabParts.Button(new Rect(x, y, half, ButtonHeight), "Their quests", palette, quests > 0,
                    false, quests > 0 ? null : "No open quest involves them."))
            {
                FactionActions.ShowQuests();
            }

            if (TabParts.Button(new Rect(x + half + 6f, y, half, ButtonHeight), "Info card", palette))
                FactionActions.Inspect(row.faction);
        }
    }
}
