using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The shapes every body in the rebuilt inspect pane is drawn from: a caption, a fact, a bar, a meter and a
    /// labelled entry.
    ///
    /// <b>Five shapes for seven tabs, deliberately.</b> The mockup Aaron approved uses the same five throughout
    /// and changes only what is poured into them, which is what makes the Health tab and the Gear tab read as one
    /// panel rather than two. A sixth shape invented for one tab is how that stops being true, so anything that
    /// does not fit one of these is either a reason to change one of these or a reason to say the fact
    /// differently.
    ///
    /// <b>Every method takes the column and a running y and hands back the next y,</b> the same contract
    /// <see cref="Features.Animals.AnimalPaneParts"/> uses. Nothing here predicts how tall a section will be:
    /// that formula is wrong the first time a row is added to the section and it fails silently, which is a fault
    /// this mod has already paid for three times.
    /// </summary>
    internal static class InspectPaneParts
    {
        /// <summary>The gap between rows inside a section.</summary>
        internal const float RowGap = 3f;

        /// <summary>The gap above a caption that is not the first in its column.</summary>
        internal const float BlockGap = 10f;

        /// <summary>Height of a progress track.</summary>
        internal const float TrackHeight = 6f;

        /// <summary>
        /// The most of a row the value on the right may take, whatever it says.
        ///
        /// A ceiling rather than a share: the value is measured and given only what it needs, and this is only
        /// reached by a value long enough that the label would otherwise disappear entirely.
        /// </summary>
        private const float ValueCeiling = 0.55f;

        /// <summary>The gutter between a row's label and its value.</summary>
        private const float SplitGap = 8f;

        /// <summary>
        /// Divides a row between the label on the left and the value on the right.
        ///
        /// <b>Measured, not shared out by a fixed fraction, and the difference is a real fault.</b> Every one of
        /// these rows used to give the label a flat share of the column -- 58 or 62 percent -- whether the value
        /// was "tended 71%" or nothing at all. On the health body's condition list, where the values are two or
        /// three characters, that threw away a third of the column and ellipsised "Control Expansion x10 -
        /// Brain" at "Br..." with a hundred pixels of empty lane beside it.
        ///
        /// The value is measured with <see cref="UIRichText.WidthOf"/>, which allows for both the tags it may
        /// carry and the thirteen pixels the ellipsis machinery keeps back, so a value that fits is never
        /// shortened either.
        /// </summary>
        internal static void Split(Rect view, float y, float height, string right, out Rect labelRect,
            out Rect valueRect)
        {
            float valueWidth = right.NullOrEmpty()
                ? 0f
                : Mathf.Min(view.width * ValueCeiling, UIRichText.WidthOf(right));

            float labelWidth = Mathf.Max(16f, view.width - valueWidth - (valueWidth > 0f ? SplitGap : 0f));

            labelRect = new Rect(view.x, y, labelWidth, height);
            valueRect = new Rect(view.xMax - valueWidth, y, valueWidth, height);
        }

        /// <summary>
        /// A section caption: a small dim label on the left, an optional aside on the right, and a hairline under
        /// both.
        ///
        /// <b>The aside carries the summary that saves reading the section.</b> "2 impaired" over the capacities,
        /// "net -16" over the mood breakdown, "3.1 / 35 kg" over what is carried: in each case the caption
        /// answers the question and the rows below it explain the answer, which is the arrangement that lets a
        /// pane be glanced at as well as read.
        /// </summary>
        internal static float Cap(Rect view, float y, string left, string right, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextDisabled;

                float height = UIFonts.LineHeightOf(GameFont.Tiny);

                Rect labelRect;
                Rect valueRect;

                Split(view, y, height, right, out labelRect, out valueRect);

                UIRichText.Label(labelRect, left ?? string.Empty);

                if (!right.NullOrEmpty())
                {
                    Text.Anchor = TextAnchor.UpperRight;

                    UIRichText.Label(valueRect, right);
                }

                y += height + 2f;

                GUI.color = palette.Border;

                Widgets.DrawLineHorizontal(view.x, y, view.width);

                return y + 5f;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>One fact: a dim name on the left and its value on the right, coloured by what it means.</summary>
        internal static float Fact(Rect view, float y, string name, string value, Color color,
            UIColorPaletteDef palette)
        {
            float height = UIFonts.LineHeightOf(GameFont.Tiny);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextSecondary;

                string shown = value ?? "-";

                Rect labelRect;
                Rect valueRect;

                Split(view, y, height, shown, out labelRect, out valueRect);

                UIRichText.Label(labelRect, name ?? string.Empty);

                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = color;

                UIRichText.Label(valueRect, shown);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return y + height + RowGap;
        }

        /// <summary>
        /// A bare progress track: a sunken lane with a fill in it.
        ///
        /// <b>Drawn rather than handed to <c>UIProgressBarControl</c>,</b> because the ticks and the ranged fill
        /// below both write into the same lane and a control that owns its own background cannot be written over
        /// afterwards without the seams showing.
        /// </summary>
        internal static void Track(Rect lane, float fraction, Color fill, UIColorPaletteDef palette)
        {
            UIElementPainter.FillRounded(lane, palette.SurfaceSunken);

            float width = Mathf.Round(lane.width * Mathf.Clamp01(fraction));

            if (width >= 2f)
                UIElementPainter.FillRounded(new Rect(lane.x, lane.y, width, lane.height), fill);
        }

        /// <summary>
        /// A mark across a track, for a threshold the fill is measured against.
        ///
        /// <b>The one thing that makes a bar mean something.</b> A mood at 34 percent is comfortable for one
        /// colonist and a tantrum for another, so a mood bar without its own pawn's break points drawn on it is a
        /// number nobody can act on.
        /// </summary>
        internal static void Tick(Rect lane, float fraction, Color color, bool major = false)
        {
            float x = lane.x + Mathf.Round(lane.width * Mathf.Clamp01(fraction));

            Widgets.DrawBoxSolid(new Rect(x - (major ? 1f : 0.5f), lane.y - 1f, major ? 2f : 1f,
                lane.height + 2f), color);
        }

        /// <summary>
        /// A need: its name, its value, a track under both, and an optional note under that.
        ///
        /// <paramref name="ticks"/> are drawn over the fill in the order given, and the first is drawn heavier
        /// than the rest: on the mood bar that is the point of no return, and the two lighter ones are warnings
        /// on the way to it.
        /// </summary>
        internal static float Need(Rect view, float y, string name, string value, Color valueColor, float fraction,
            Color fill, float[] ticks, string note, UIColorPaletteDef palette)
        {
            float line = UIFonts.LineHeightOf(GameFont.Tiny);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextSecondary;

                Rect labelRect;
                Rect valueRect;

                Split(view, y, line, value, out labelRect, out valueRect);

                UIRichText.Label(labelRect, name ?? string.Empty);

                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = valueColor;

                UIRichText.Label(valueRect, value ?? string.Empty);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            y += line + 1f;

            Rect lane = new Rect(view.x, y, view.width, TrackHeight);

            Track(lane, fraction, fill, palette);

            if (ticks != null)
            {
                for (int i = 0; i < ticks.Length; i++)
                    Tick(lane, ticks[i], palette.TextPrimary, i == 0);
            }

            y = lane.yMax + 2f;

            if (!note.NullOrEmpty())
                y = Note(view, y, note, palette);

            return y + RowGap;
        }

        /// <summary>
        /// A name, an inline track and a value on one line. What a list of capacities or worn apparel is made of.
        ///
        /// The track sits between the two rather than under them because these come in runs of four or more, and
        /// a stacked bar per row would turn a short list into the whole column.
        /// </summary>
        internal static float Meter(Rect view, float y, string name, float fraction, Color fill, string value,
            Color valueColor, UIColorPaletteDef palette)
        {
            float line = UIFonts.LineHeightOf(GameFont.Tiny);
            float row = Mathf.Max(line, 14f);

            // The value is measured rather than given a flat 42 pixels, since these carry percentages on the
            // capacity list and "180 / 180" on a building's health. The lane keeps a third of the row whatever
            // happens, because a meter whose bar has been squeezed to nothing is a fact row with extra steps.
            float valueWidth = value.NullOrEmpty() ? 0f : UIRichText.WidthOf(value);
            float laneWidth = Mathf.Max(Mathf.Round(view.width * 0.3f), 20f);
            float nameWidth = Mathf.Max(30f, view.width - laneWidth - valueWidth - 12f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                UIRichText.Label(new Rect(view.x, y, nameWidth, row), name ?? string.Empty);

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = valueColor;

                UIRichText.Label(new Rect(view.xMax - valueWidth, y, valueWidth, row), value ?? string.Empty);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Track(new Rect(view.x + nameWidth + 6f, y + (row - TrackHeight) * 0.5f, laneWidth, TrackHeight),
                fraction, fill, palette);

            return y + row + 1f;
        }

        /// <summary>
        /// An entry: a heading line with a value on the right, and an optional explanation under it.
        ///
        /// This is the shape for anything with a story rather than a number, which is most of the Health tab and
        /// all of Social. The note is where the second sentence goes, and it wraps rather than ellipsing, because
        /// half of "immunity 61 percent against severity 44 percent" is worse than none of it.
        /// </summary>
        internal static float Entry(Rect view, float y, string left, string right, Color rightColor, string note,
            UIColorPaletteDef palette)
        {
            float line = UIFonts.LineHeightOf(GameFont.Tiny);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                Rect labelRect;
                Rect valueRect;

                Split(view, y, line, right, out labelRect, out valueRect);

                UIRichText.Label(labelRect, left ?? string.Empty);

                if (!right.NullOrEmpty())
                {
                    Text.Anchor = TextAnchor.UpperRight;
                    GUI.color = rightColor;

                    UIRichText.Label(valueRect, right);
                }
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            y += line + 1f;

            if (!note.NullOrEmpty())
                y = Note(view, y, note, palette);

            return y + RowGap;
        }

        /// <summary>
        /// A dim wrapped line under something else.
        ///
        /// <b>Measured before it is drawn rather than assumed to be one line.</b> IMGUI clips text to the rect it
        /// is handed, so a note given a single line height loses its second line without saying so, and the
        /// caller's running y is wrong from there down.
        /// </summary>
        internal static float Note(Rect view, float y, string text, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextDisabled;

                float height = Text.CalcHeight(text, view.width);

                Widgets.Label(new Rect(view.x, y, view.width, height), text);

                return y + height;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// A signed bar drawn out from the middle: right for a positive opinion, left for a negative one.
        ///
        /// <b>Zero is the middle, not the left edge,</b> which is the whole reason this is not a
        /// <see cref="Track"/> with a different fill. An opinion runs from minus a hundred to plus a hundred and
        /// the question a reader has is which side of nothing it is on; a bar filling from the left answers a
        /// question nobody asked.
        /// </summary>
        internal static void SignedBar(Rect lane, float signedFraction, Color fill, UIColorPaletteDef palette)
        {
            UIElementPainter.FillRounded(lane, palette.SurfaceSunken);

            float middle = Mathf.Round(lane.x + lane.width * 0.5f);
            float reach = Mathf.Round(lane.width * 0.5f * Mathf.Clamp(Mathf.Abs(signedFraction), 0f, 1f));

            if (reach >= 2f)
            {
                Rect filled = signedFraction >= 0f
                    ? new Rect(middle, lane.y, reach, lane.height)
                    : new Rect(middle - reach, lane.y, reach, lane.height);

                UIElementPainter.FillRounded(filled, fill);
            }

            Widgets.DrawBoxSolid(new Rect(middle - 0.5f, lane.y - 1f, 1f, lane.height + 2f), palette.TextDisabled);
        }

        /// <summary>
        /// Three or four numbers side by side under one caption, each with its own label.
        ///
        /// For facts that only mean anything next to each other: sharp, blunt and heat armour is one reading in
        /// three parts, and stacking them as facts would make them look like three unrelated rows.
        /// </summary>
        internal static float Pips(Rect view, float y, string[] labels, string[] values, Color[] colors,
            UIColorPaletteDef palette)
        {
            if (labels == null || values == null || labels.Length == 0)
                return y;

            float line = UIFonts.LineHeightOf(GameFont.Tiny);
            float height = line * 2f + 8f;
            float width = (view.width - (labels.Length - 1) * 4f) / labels.Length;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperCenter;

                for (int i = 0; i < labels.Length; i++)
                {
                    Rect cell = new Rect(view.x + i * (width + 4f), y, width, height);

                    UIElementPainter.OutlineRounded(cell, palette.Border, palette.SurfaceSunken);

                    GUI.color = palette.TextDisabled;

                    UIRichText.Label(new Rect(cell.x, cell.y + 3f, cell.width, line), labels[i]);

                    GUI.color = colors != null && i < colors.Length ? colors[i] : palette.TextPrimary;

                    UIRichText.Label(new Rect(cell.x, cell.y + 3f + line, cell.width, line),
                        i < values.Length ? values[i] : "-");
                }
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return y + height + RowGap;
        }

        /// <summary>
        /// A small rounded label, used where a fact is a word rather than a number: a trait, a disabled work
        /// type, a log filter.
        ///
        /// Returns the rect it took, so a caller laying several across a line knows where the next one starts.
        /// </summary>
        internal static Rect Tag(Rect view, float x, float y, string text, Color color, bool filled,
            UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;

                // Capped against the room left from where this chip actually starts, not against the column's
                // full width. Clamping to view.width let a chip placed halfway along a line run off the end of
                // the column and be clipped by the group: "Great memory" came out as "Great mem" with nothing to
                // say a word had been lost.
                float room = Mathf.Max(24f, view.xMax - x);

                float width = Mathf.Min(room, TagWidth(text));
                float height = UIFonts.LineHeightOf(GameFont.Tiny) + 5f;

                Rect chip = new Rect(x, y, width, height);

                if (filled)
                    UIElementPainter.FillRounded(chip, color);
                else
                    UIElementPainter.OutlineRounded(chip, color, palette.PanelBackground);

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = filled ? palette.WindowBackground : color;

                // Ellipsed rather than clipped, for the case the cap above still bites: a column too narrow for
                // the longest trait in the game.
                //
                // <b>Given the whole chip, not an inset one.</b> Widgets.LabelEllipses holds thirteen pixels of
                // its rect back for the dots, so inset padding stacks on top of that reserve and every chip
                // ellipses whatever its size: a chip measured at text plus fourteen and then handed a rect
                // twelve narrower had eleven pixels less than its own text needed, and "Perfect memory" came out
                // as "Perfect me..." in a chip with room to spare. The reserve is the padding.
                UIRichText.Label(chip, text ?? string.Empty);

                return chip;
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// How wide a chip wants to be, so a flow can decide where it goes before drawing it.
        ///
        /// <b>Measured through <see cref="UIRichText.WidthOf"/> rather than off <c>CalcSize</c> alone,</b> because
        /// that is the figure the drawing side will judge it against: <c>LabelEllipses</c> holds thirteen pixels
        /// back for the dots, so a chip sized to the bare text ellipses text that would have fitted. The six on
        /// top is the visible padding, and everything else is the reserve.
        /// </summary>
        internal static float TagWidth(string text)
        {
            GameFont previousFont = Text.Font;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;

                return UIRichText.WidthOf(text ?? string.Empty) + 6f;
            }
            finally
            {
                Text.WordWrap = previousWrap;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// One chip in a wrapping row of them, placed before it is drawn.
        ///
        /// <b>The wrap has to be decided in front of the chip, not behind it.</b> All four chip rows in the pane
        /// drew at the current x and then asked whether the <i>next</i> one would fit, so the one that did not fit
        /// had already been drawn overhanging the column. Measuring first is the whole fix, and it lives here
        /// because four callers had the same eight lines and therefore the same bug.
        ///
        /// <paramref name="x"/>, <paramref name="y"/> and <paramref name="rowHeight"/> are the flow's running
        /// state: seed x at the column's left edge and the other two at zero, and read y back afterwards.
        /// </summary>
        internal static Rect Chip(Rect view, ref float x, ref float y, ref float rowHeight, string text,
            Color color, bool filled, UIColorPaletteDef palette, float gap = 4f)
        {
            float wanted = TagWidth(text);

            // Wrapped only when something has already been placed on this line: a chip wider than the whole
            // column has nowhere better to go, and moving it to a fresh line it also overflows would cost a blank
            // line and fix nothing. Tag ellipses that one instead.
            if (x > view.x && x + wanted > view.xMax)
            {
                x = view.x;
                y += rowHeight + gap;
                rowHeight = 0f;
            }

            Rect chip = Tag(view, x, y, text, color, filled, palette);

            rowHeight = Mathf.Max(rowHeight, chip.height);
            x = chip.xMax + gap;

            return chip;
        }

        /// <summary>
        /// The colour a fraction of something should read in: green when it is fine, amber when it is going, red
        /// when it is nearly gone.
        ///
        /// <b>One place, so a bar cannot disagree with the number printed beside it.</b> The thresholds are the
        /// same two everywhere in the pane, which is what lets a reader learn the colours once.
        /// </summary>
        internal static Color Level(float fraction, UIColorPaletteDef palette)
        {
            if (fraction <= 0.2f)
                return palette.Danger;

            return fraction <= 0.45f ? palette.Warning : palette.Accent;
        }

        /// <summary>
        /// The colour for a vital sign, on four steps rather than <see cref="Level"/>'s three.
        ///
        /// <b>Health earns the extra step because it is the one number you watch fall.</b> A need at 60 percent
        /// is fine and a colonist at 60 percent is not, so the scale has to say something between "fine" and
        /// "hurt": green, yellow, orange, red.
        ///
        /// <b>The orange is mixed from the palette rather than written as a literal,</b> so a theme that restates
        /// what warning and danger look like gets a matching step between them instead of one hardcoded colour
        /// sitting in the middle of its own scheme.
        /// </summary>
        internal static Color Vital(float fraction, UIColorPaletteDef palette)
        {
            if (fraction >= 0.85f)
                return palette.Success;

            if (fraction >= 0.6f)
                return palette.Warning;

            return fraction >= 0.35f
                ? Color.Lerp(palette.Warning, palette.Danger, 0.5f)
                : palette.Danger;
        }

        /// <summary>A percentage in the form the rest of the pane writes them.</summary>
        internal static string Percent(float fraction)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(fraction) * 100f) + "%";
        }
    }
}
