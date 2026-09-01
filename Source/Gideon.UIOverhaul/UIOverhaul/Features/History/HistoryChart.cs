using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.History
{
    /// <summary>
    /// The plot, its axes and the ribbon of things that happened underneath it.
    ///
    /// <b>Drawn here rather than through <c>SimpleCurveDrawer</c>.</b> Vanilla's drawer takes a list of curves
    /// and a style and gives back four crossing lines; it has no notion of a stacked band, of an event under the
    /// axis, or of a stretch of the axis its data does not cover. Those three are the whole point of this
    /// screen, so the drawing is ours. Everything it reads is still vanilla's: the same
    /// <c>HistoryAutoRecorder.records</c> lists, and the same fixed scale, integers-only and positive-only flags
    /// off <c>HistoryAutoRecorderGroupDef</c>, so a modded group keeps the shape its author asked for.
    ///
    /// <b>Sampled per column rather than per record.</b> A curve is drawn by walking the pixels and asking the
    /// series what it was on that day, which costs the same whether the span is thirty days or four hundred.
    /// Walking records instead costs nothing at thirty days and draws two thousand segments at four hundred,
    /// which is what vanilla does.
    /// </summary>
    internal static class HistoryChart
    {
        /// <summary>
        /// Pixels per sample.
        ///
        /// Two rather than one: at one the stacked bands cost a thousand fills a frame for a result nobody can
        /// tell apart from this, and the outline is drawn as real segments on top either way, which is where
        /// the crispness a reader actually notices comes from.
        /// </summary>
        private const float Step = 2f;

        private const float AxisWidth = 52f;

        private const float DayRowHeight = 15f;

        private const float RibbonHeight = 28f;

        /// <summary>How many horizontal gridlines, including the one at the top.</summary>
        private const int Gridlines = 5;

        internal static float FurnitureHeight
        {
            get { return DayRowHeight + RibbonHeight; }
        }

        /// <summary>
        /// Draws the whole plot. Returns the moment the player clicked, or null.
        /// </summary>
        internal static HistoryMoment Draw(Rect rect, List<HistorySeries> series,
            HistoryAutoRecorderGroupDef def, float fromDay, float toDay, List<HistoryMoment> moments,
            float horizonDay, UIColorPaletteDef palette)
        {
            if (series == null || series.Count == 0 || rect.width <= AxisWidth + 20f)
                return null;

            if (toDay - fromDay < 0.01f)
                toDay = fromDay + 0.01f;

            Rect plot = new Rect(rect.x + AxisWidth, rect.y, rect.width - AxisWidth,
                Mathf.Max(20f, rect.height - FurnitureHeight));

            Rect dayRow = new Rect(plot.x, plot.yMax, plot.width, DayRowHeight);
            Rect ribbon = new Rect(plot.x, dayRow.yMax, plot.width, RibbonHeight);

            int total = HistoryFacts.TotalIndex(series);

            Scale(series, def, total, fromDay, toDay, out float low, out float high);

            Grid(rect, plot, low, high, def, series, palette);
            Bands(plot, series, total, fromDay, toDay, low, high, palette);
            Lines(plot, series, total, fromDay, toDay, low, high, palette);
            DayAxis(dayRow, fromDay, toDay, palette);

            return Ribbon(ribbon, plot, moments, fromDay, toDay, horizonDay, palette);
        }

        // -------------------------------------------------------------------------------------------
        // Scale
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The range the y axis covers.
        ///
        /// <b>A group that asked for a fixed scale gets it.</b> Mood is authored 0 to 100 precisely so that a
        /// colony at 60 percent looks like a colony at 60 percent rather than filling the panel; auto-scaling it
        /// would make every mood graph look identical.
        ///
        /// <b>Otherwise the maximum comes from what is on screen, not from the whole run.</b> Scaling to the
        /// run's peak means zooming into the last thirty days of a colony that was once rich draws a flat line
        /// along the bottom.
        /// </summary>
        private static void Scale(List<HistorySeries> series, HistoryAutoRecorderGroupDef def, int total,
            float fromDay, float toDay, out float low, out float high)
        {
            if (def != null && def.useFixedScale)
            {
                low = def.fixedScale.x;
                high = def.fixedScale.y;

                if (high - low < 0.0001f)
                    high = low + 1f;

                return;
            }

            float max = float.MinValue;
            float min = float.MaxValue;

            for (int sample = 0; sample <= 64; sample++)
            {
                float day = Mathf.Lerp(fromDay, toDay, sample / 64f);

                if (total >= 0)
                {
                    // Stacked: the top of the stack is the total, so nothing else can exceed it.
                    max = Mathf.Max(max, series[total].At(day));
                    min = Mathf.Min(min, series[total].At(day));

                    continue;
                }

                for (int i = 0; i < series.Count; i++)
                {
                    float value = series[i].At(day);

                    max = Mathf.Max(max, value);
                    min = Mathf.Min(min, value);
                }
            }

            if (max <= float.MinValue || min >= float.MaxValue)
            {
                low = 0f;
                high = 1f;

                return;
            }

            low = def == null || def.onlyPositiveValues ? 0f : Mathf.Min(0f, min);
            high = Nice(max);

            if (high - low < 0.0001f)
                high = low + 1f;
        }

        /// <summary>
        /// The next round number at or above a value, so the top gridline is a figure a person would say.
        ///
        /// Ten percent of headroom first, so a curve that touches its own peak does not run along the top edge
        /// of the panel with nothing above it.
        /// </summary>
        private static float Nice(float value)
        {
            if (value <= 0f)
                return 1f;

            float wanted = value * 1.1f;
            float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(wanted)));
            float normalized = wanted / magnitude;

            float rounded = normalized <= 1f ? 1f
                : normalized <= 2f ? 2f
                : normalized <= 2.5f ? 2.5f
                : normalized <= 5f ? 5f
                : 10f;

            return rounded * magnitude;
        }

        private static float YFor(Rect plot, float value, float low, float high)
        {
            return plot.yMax - (value - low) / (high - low) * plot.height;
        }

        // -------------------------------------------------------------------------------------------
        // Furniture
        // -------------------------------------------------------------------------------------------

        private static void Grid(Rect rect, Rect plot, float low, float high,
            HistoryAutoRecorderGroupDef def, List<HistorySeries> series, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(plot, palette.SurfaceSunken);

            string format = series.Count > 0 ? series[0].ValueFormat : null;
            bool integers = def != null && def.integersOnly;

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                for (int i = 0; i < Gridlines; i++)
                {
                    float fraction = (i + 1) / (float) Gridlines;
                    float value = Mathf.Lerp(low, high, fraction);
                    float y = YFor(plot, value, low, high);

                    // Integers only means a line at 2.5 colonists is a line that cannot happen. Skipping it
                    // beats rounding it, which would put two gridlines on the same pixel.
                    if (integers && Mathf.Abs(value - Mathf.Round(value)) > 0.01f)
                        continue;

                    Widgets.DrawLine(new Vector2(plot.x, y), new Vector2(plot.xMax, y), palette.Border, 1f);

                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = palette.TextDisabled;

                    UITextControl.Label(new Rect(rect.x, y - 7f, AxisWidth - 6f, 14f),
                        AxisLabel(value, format, integers), HistoryFaces.Mono, HistoryFaces.Size.Axis);
                }

                // The zero line is drawn even when it is not a gridline, because a chart with negative values
                // in it is unreadable without knowing where nought is. Debug's threat points is the only group
                // in the base game that goes below.
                if (low < 0f && high > 0f)
                {
                    float zero = YFor(plot, 0f, low, high);

                    Widgets.DrawLine(new Vector2(plot.x, zero), new Vector2(plot.xMax, zero),
                        palette.TextDisabled, 1f);
                }
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
            }
        }

        /// <summary>
        /// A y axis figure, in whatever unit the recorder said it was in.
        ///
        /// <c>valueFormat</c> is authored per recorder -- <c>${0}</c> for wealth, <c>{0}%</c> for mood -- and
        /// reading it means the axis says dollars on the wealth graph and percent on the mood one without this
        /// knowing either group exists.
        /// </summary>
        private static string AxisLabel(float value, string format, bool integers)
        {
            if (!format.NullOrEmpty() && format.Contains("$"))
                return HistoryFacts.ShortSilver(value);

            string figure = integers || Mathf.Abs(value) >= 10f
                ? Mathf.RoundToInt(value).ToString("N0")
                : value.ToString("0.#");

            return !format.NullOrEmpty() && format.Contains("%") ? figure + "%" : figure;
        }

        /// <summary>
        /// The days along the bottom.
        ///
        /// <b>Seven at most, and fewer when the span is short.</b> A thirty day view with seven labels repeats
        /// the same number twice; the count comes down until each label is a different day.
        /// </summary>
        private static void DayAxis(Rect row, float fromDay, float toDay, UIColorPaletteDef palette)
        {
            int marks = Mathf.Clamp(Mathf.FloorToInt(toDay - fromDay) + 1, 2, 7);

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                GUI.color = palette.TextDisabled;

                for (int i = 0; i < marks; i++)
                {
                    float fraction = marks == 1 ? 0f : i / (float) (marks - 1);
                    float day = Mathf.Lerp(fromDay, toDay, fraction);
                    float x = row.x + fraction * row.width;

                    // The end labels are pulled inside the plot rather than centred on it, so neither runs off
                    // the edge of the panel.
                    Text.Anchor = i == 0 ? TextAnchor.UpperLeft
                        : i == marks - 1 ? TextAnchor.UpperRight
                        : TextAnchor.UpperCenter;

                    UITextControl.Label(new Rect(x - 30f, row.y, 60f, row.height),
                        Mathf.RoundToInt(day).ToString(), HistoryFaces.Mono, HistoryFaces.Size.Axis);
                }
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
            }
        }

        // -------------------------------------------------------------------------------------------
        // The data
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The stacked bands under a total, drawn as one fill per sampled column.
        ///
        /// Does nothing when the group has no total, which is every group but wealth.
        /// </summary>
        private static void Bands(Rect plot, List<HistorySeries> series, int total, float fromDay, float toDay,
            float low, float high, UIColorPaletteDef palette)
        {
            if (total < 0)
                return;

            for (float x = plot.x; x < plot.xMax; x += Step)
            {
                float day = Mathf.Lerp(fromDay, toDay, (x - plot.x) / plot.width);
                float width = Mathf.Min(Step, plot.xMax - x);
                float running = 0f;
                float lowY = YFor(plot, low, low, high);

                for (int i = 0; i < series.Count; i++)
                {
                    if (i == total)
                        continue;

                    running += series[i].At(day);

                    float topY = Mathf.Max(plot.y, YFor(plot, running, low, high));

                    if (lowY - topY > 0.5f)
                    {
                        Widgets.DrawBoxSolid(new Rect(x, topY, width, lowY - topY),
                            HistoryFaces.Series(palette, i));
                    }

                    lowY = topY;
                }
            }
        }

        /// <summary>
        /// The curves: the total's outline over the bands, or every series as a line when nothing stacks.
        /// </summary>
        private static void Lines(Rect plot, List<HistorySeries> series, int total, float fromDay, float toDay,
            float low, float high, UIColorPaletteDef palette)
        {
            for (int i = 0; i < series.Count; i++)
            {
                if (total >= 0 && i != total)
                    continue;

                Color color = HistoryFaces.Series(palette, i);
                float previousX = plot.x;
                float previousY = YFor(plot, series[i].At(fromDay), low, high);

                for (float x = plot.x + Step; x <= plot.xMax; x += Step)
                {
                    float day = Mathf.Lerp(fromDay, toDay, (x - plot.x) / plot.width);
                    float y = YFor(plot, series[i].At(day), low, high);

                    // Clamped so a fixed scale a value has left -- a modded mood recorder above 100 -- draws
                    // along the edge of the panel rather than over the rows above it.
                    Widgets.DrawLine(new Vector2(previousX, Mathf.Clamp(previousY, plot.y, plot.yMax)),
                        new Vector2(x, Mathf.Clamp(y, plot.y, plot.yMax)), color, 2f);

                    previousX = x;
                    previousY = y;
                }
            }
        }

        // -------------------------------------------------------------------------------------------
        // The ribbon
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// What happened, on the same axis as what it did to the colony.
        ///
        /// <b>The hatched stretch on the left is the honest part.</b> The curve above it is complete back to day
        /// one, and the dots are not: the archive keeps two hundred entries. Without a mark between them a four
        /// hundred day colony reads as one where nothing happened until recently. Tales still draw inside it,
        /// because those genuinely do go back that far, which says the thing better than a caption could.
        /// </summary>
        private static HistoryMoment Ribbon(Rect ribbon, Rect plot, List<HistoryMoment> moments, float fromDay,
            float toDay, float horizonDay, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(ribbon, palette.SurfaceSunken);

            if (horizonDay > fromDay)
            {
                float edge = Mathf.Min(ribbon.xMax, XFor(ribbon, Mathf.Min(horizonDay, toDay), fromDay, toDay));
                Rect forgotten = new Rect(ribbon.x, ribbon.y, edge - ribbon.x, ribbon.height);

                if (forgotten.width > 2f)
                {
                    Widgets.DrawBoxSolid(forgotten, palette.HoverOverlay);
                    Widgets.DrawLine(new Vector2(edge, ribbon.y), new Vector2(edge, ribbon.yMax),
                        palette.Border, 1f);

                    if (forgotten.width > 130f)
                    {
                        TextAnchor anchor = Text.Anchor;
                        Color color = GUI.color;

                        Text.Anchor = TextAnchor.MiddleRight;
                        GUI.color = palette.TextDisabled;

                        UITextControl.Label(new Rect(forgotten.x, forgotten.y, forgotten.width - 7f,
                                forgotten.height), "OLDER THAN THE ARCHIVE KEEPS",
                            HistoryFaces.Mono, HistoryFaces.Size.Axis);

                        GUI.color = color;
                        Text.Anchor = anchor;
                    }

                    TooltipHandler.TipRegion(forgotten, (TipSignal)
                        ("The archive holds the last " + HistoryFacts.ArchiveCap
                         + " letters and messages, so nothing before day "
                         + Mathf.RoundToInt(horizonDay) + " is left to mark. Pin an entry to keep it.\n\n"
                         + "The curve above is complete: the recorders are never pruned."));
                }
            }

            if (moments == null || moments.Count == 0)
                return null;

            HistoryMoment clicked = null;
            bool over = Mouse.IsOver(ribbon);
            float lastLabelEdge = float.MinValue;

            // Tales last, so a labelled mark is never drawn under a dot.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < moments.Count; i++)
                {
                    HistoryMoment moment = moments[i];

                    if (moment == null || moment.Tale != (pass == 1))
                        continue;

                    float x = XFor(ribbon, moment.Day, fromDay, toDay);

                    if (x < ribbon.x - 4f || x > ribbon.xMax + 4f)
                        continue;

                    Rect hit = Mark(ribbon, plot, x, moment, palette, ref lastLabelEdge);

                    if (!over)
                        continue;

                    if (!moment.Label.NullOrEmpty())
                        TooltipHandler.TipRegion(hit, (TipSignal) (HistoryFacts.DateOf(moment.TicksGame)
                                                                   + "\n" + moment.Label));

                    if (Widgets.ButtonInvisible(hit))
                        clicked = moment;
                }
            }

            return clicked;
        }

        private static float XFor(Rect area, float day, float fromDay, float toDay)
        {
            return area.x + (day - fromDay) / (toDay - fromDay) * area.width;
        }

        /// <summary>
        /// One mark, in the weight its kind earns: a dot for a letter, a bar for a battle, a labelled tick for
        /// a tale.
        ///
        /// <b>Letters keep the color the letter def gave them.</b> That is what vanilla's archive row draws them
        /// in, and it means a threat is the same red on this ribbon as in the letter stack it arrived in.
        /// Recoloring them from our palette would be this mod deciding what a modded letter means.
        /// </summary>
        private static Rect Mark(Rect ribbon, Rect plot, float x, HistoryMoment moment,
            UIColorPaletteDef palette, ref float lastLabelEdge)
        {
            if (moment.Tale)
            {
                Widgets.DrawLine(new Vector2(x, plot.y), new Vector2(x, plot.yMax),
                    new Color(palette.TextSecondary.r, palette.TextSecondary.g, palette.TextSecondary.b, 0.22f),
                    1f);

                Widgets.DrawBoxSolid(new Rect(x - 0.5f, ribbon.y + 3f, 1f, ribbon.height - 6f),
                    palette.TextSecondary);

                Widgets.DrawBoxSolid(new Rect(x - 3f, ribbon.y + 2f, 6f, 6f), palette.TextSecondary);

                if (!moment.Label.NullOrEmpty() && x > lastLabelEdge + 12f)
                {
                    float width = Mathf.Min(150f,
                        UITextControl.Width(moment.Label, HistoryFaces.Condensed, HistoryFaces.Size.Axis) + 6f);

                    if (x + width < ribbon.xMax)
                    {
                        TextAnchor anchor = Text.Anchor;
                        Color color = GUI.color;

                        Text.Anchor = TextAnchor.MiddleLeft;
                        GUI.color = palette.TextSecondary;

                        UITextControl.LabelEllipses(new Rect(x + 5f, ribbon.y, width, ribbon.height * 0.5f),
                            moment.Label, HistoryFaces.Condensed, HistoryFaces.Size.Axis);

                        GUI.color = color;
                        Text.Anchor = anchor;

                        lastLabelEdge = x + width;
                    }
                }

                return new Rect(x - 5f, ribbon.y, 10f, ribbon.height);
            }

            if (moment.Battle != null)
            {
                Widgets.DrawBoxSolid(new Rect(x - 1.5f, ribbon.y + 8f, 3f, 13f), palette.Danger);

                return new Rect(x - 4f, ribbon.y + 6f, 8f, 17f);
            }

            Color tint = moment.Tint;

            // A letter def with no color set arrives as clear rather than as white, and a clear dot is a dot
            // nobody can see.
            if (tint.a < 0.05f)
                tint = palette.TextSecondary;

            Widgets.DrawBoxSolid(new Rect(x - 3f, ribbon.y + 11f, 6f, 6f), tint);

            return new Rect(x - 4f, ribbon.y + 9f, 8f, 10f);
        }
    }
}
