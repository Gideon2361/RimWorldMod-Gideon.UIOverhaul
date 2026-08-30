using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Notifications;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Calendar
{
    /// <summary>
    /// The year at a glance, as a row in the corner panel.
    ///
    /// <b>Twelve segments rather than sixty days.</b> The corner is 240 wide, so a day per cell would be under
    /// four pixels -- too small to carry a color, let alone a marker. A year is twelve twelfths, which is also the
    /// granularity the growing season is computed at, so the bar and the data underneath it agree exactly instead
    /// of the bar rounding something finer.
    ///
    /// <b>What it is for is the growing season.</b> RimWorld knows precisely which twelfths crops grow in at a
    /// given latitude -- <c>GenTemperature.TwelfthsInAverageTemperatureRange</c> -- and then only ever tells the
    /// player as a sentence in the world tile inspector. It is the thing a colony year is actually planned
    /// around, and it deserves to be visible while playing rather than while choosing a landing site.
    ///
    /// <b>The glyph is the only clickable thing on the row.</b> Everything else here is a readout; the calendar
    /// with an arrow opens <see cref="Dialog_Calendar"/>, and it is drawn in the accent so it reads as a door
    /// rather than as decoration.
    /// </summary>
    internal static class CalendarWidget
    {
        /// <summary>
        /// Height of the whole widget: header, bar, quadrum names.
        ///
        /// <b>Measured rather than declared, and twice over.</b> These rows were first 14, to match the bar,
        /// which clipped every label because Tiny's line box is taller than that. Raising them to 18 fixed the
        /// symptom for anyone whose game actually draws Tiny -- and left it for everyone whose game quietly
        /// substitutes Small, which is a language with no tiny font, the disable-tiny-text preference, the Steam
        /// Deck, or any frame drawn during a long event. <see cref="UIFonts"/> answers what will really be drawn.
        /// </summary>
        internal static float Height => HeaderHeight + Gap + BarHeight + 2f + FooterHeight;

        private static float HeaderHeight => UIFonts.RowHeight(GameFont.Tiny);

        private static float FooterHeight => UIFonts.RowHeight(GameFont.Tiny);

        private const float BarHeight = 14f;
        private const float GlyphSize = 14f;
        private const float Gap = 4f;

        /// <summary>Twelfths in a year. Vanilla's own division, and the one the growing data uses.</summary>
        private const int Twelfths = 12;

        /// <summary>
        /// The default temperature band for "most crops", which is what <c>Zone_Growing</c> uses when it is not
        /// asked about a particular plant. Copied from there rather than invented.
        /// </summary>
        private const float MinGrowthTemp = 6f;

        private const float MaxGrowthTemp = 42f;

        /// <summary>
        /// The growing twelfths, recomputed when the tile changes rather than every frame.
        ///
        /// The answer depends only on the tile's climate, so it is fixed for the life of a colony. Asking every
        /// frame would walk a year of average temperatures to be told the same thing again.
        /// </summary>
        private static readonly HashSet<int> growingTwelfths = new HashSet<int>();

        /// <summary>Keyed by map rather than by tile, since a map never moves and the id is always readable.</summary>
        private static int cachedForMap = int.MinValue;

        internal static void Draw(Rect rect, Map map, UIColorPaletteDef palette)
        {
            EnsureGrowing(map);

            // Asked through MapTile rather than off the map, because a pocket map has no tile and reading one
            // throws -- which took the whole widget with it, not just the growing season below.
            float longitude = Shared.MapTile.LongitudeOf(map);
            long ticks = GenTicks.TicksAbs;

            Rect header = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            Rect bar = new Rect(rect.x, header.yMax + Gap, rect.width, BarHeight);
            Rect footer = new Rect(rect.x, bar.yMax + 2f, rect.width, FooterHeight);

            DrawHeader(header, palette);
            DrawBar(bar, ticks, longitude, palette);
            DrawFooter(footer, ticks, longitude, palette);
        }

        private static void DrawHeader(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;

                // Every label here is one line by construction. Left wrapping on, a label a pixel too wide grows
                // a second line inside a row that has height for one, and loses half of each.
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleLeft;

                Rect glyph = new Rect(rect.x, rect.y + (rect.height - GlyphSize) * 0.5f, GlyphSize, GlyphSize);

                if (Widgets.ButtonImage(glyph, NotificationIcons.CalendarOut, palette.Accent,
                        palette.BorderFocused))
                {
                    SoundDefOf.TabOpen.PlayOneShotOnCamera();
                    Find.WindowStack.Add(new Dialog_Calendar());
                }

                TooltipHandler.TipRegion(glyph, (TipSignal) "Open the calendar.");

                string count = growingTwelfths.Count == 0
                    ? "none"
                    : growingTwelfths.Count * GenDate.DaysPerTwelfth + " of " + GenDate.DaysPerYear + " days";

                // The count's lane is measured rather than assumed, so the label beside it gets exactly what is
                // left. A fixed fraction of the row is how two labels end up on top of each other the first time
                // a translation or a tile makes one of them longer.
                float countWidth = Text.CalcSize(count).x + 6f;

                GUI.color = palette.TextSecondary;
                Widgets.LabelEllipses(
                    new Rect(glyph.xMax + 6f, rect.y,
                        Mathf.Max(0f, rect.xMax - countWidth - glyph.xMax - 6f), rect.height),
                    "Growing season");

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = growingTwelfths.Count > 0 ? palette.Success : palette.TextDisabled;

                Widgets.Label(rect, count);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWrap;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The year as twelve segments, with today marked.
        ///
        /// The shoulder twelfths -- those next to a non-growing one -- take a darker green, because the edges of a
        /// growing season are where a crop planted a day late fails and the bar should not present them as being
        /// as safe as midsummer.
        /// </summary>
        private static void DrawBar(Rect rect, long ticks, float longitude, UIColorPaletteDef palette)
        {
            float segment = (rect.width - (Twelfths - 1)) / Twelfths;

            for (int i = 0; i < Twelfths; i++)
            {
                Rect cell = new Rect(rect.x + i * (segment + 1f), rect.y, segment, rect.height);

                Widgets.DrawBoxSolid(cell, SegmentColor(i, palette));
            }

            // Today, as a fraction of the year rather than a segment: the marker is the one thing here that
            // should move visibly from day to day.
            float yearPct = Mathf.Clamp01(GenDate.DayOfYear(ticks, longitude) / (float) GenDate.DaysPerYear);
            float x = rect.x + yearPct * rect.width;

            Widgets.DrawBoxSolid(new Rect(x - 1f, rect.y - 3f, 2f, rect.height + 6f), palette.Accent);
        }

        private static Color SegmentColor(int twelfth, UIColorPaletteDef palette)
        {
            if (!growingTwelfths.Contains(twelfth))
                return palette.ControlBackgroundFaded;

            bool shoulder = !growingTwelfths.Contains((twelfth + Twelfths - 1) % Twelfths)
                            || !growingTwelfths.Contains((twelfth + 1) % Twelfths);

            // Darkened rather than a second palette role: this is the same green saying "less of it", which is
            // what a multiply reads as, and a growing-season-edge color is not a theme concept.
            return shoulder
                ? new Color(palette.Success.r * 0.55f, palette.Success.g * 0.55f, palette.Success.b * 0.55f,
                    palette.Success.a)
                : palette.Success;
        }

        private static void DrawFooter(Rect rect, long ticks, float longitude, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;

                // Every label here is one line by construction. Left wrapping on, a label a pixel too wide grows
                // a second line inside a row that has height for one, and loses half of each.
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleLeft;

                Quadrum current = GenDate.Quadrum(ticks, longitude);
                List<Quadrum> quadrums = QuadrumUtility.QuadrumsInChronologicalOrder;
                float width = rect.width / quadrums.Count;

                // <b>Full names only if every one of them fits.</b> A quarter of 228 pixels is about 57, and
                // "Decembary" does not fit that at any font this widget uses -- which is why the fourth quadrum
                // was missing from the row entirely rather than merely being short. Measuring all four and
                // dropping to the short forms together keeps the row consistent: three long names and one
                // abbreviation would read as a rendering fault rather than as a deliberate shortening.
                bool useShort = false;

                for (int i = 0; i < quadrums.Count; i++)
                    if (Text.CalcSize(quadrums[i].Label()).x > width - 2f)
                    {
                        useShort = true;
                        break;
                    }

                for (int i = 0; i < quadrums.Count; i++)
                {
                    GUI.color = quadrums[i] == current ? palette.TextPrimary : palette.TextDisabled;

                    Widgets.LabelEllipses(new Rect(rect.x + i * width, rect.y, width, rect.height),
                        useShort ? quadrums[i].LabelShort() : quadrums[i].Label());
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWrap;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// Works out which twelfths crops grow in here, once per tile.
        /// </summary>
        private static void EnsureGrowing(Map map)
        {
            if (map.uniqueID == cachedForMap)
                return;

            cachedForMap = map.uniqueID;
            growingTwelfths.Clear();

            PlanetTile tile = Shared.MapTile.Of(map);

            // Nothing grows to a schedule where there is no sky, so an empty set is the honest answer rather
            // than a failure. The header already reads "none" for it.
            if (!tile.Valid)
                return;

            UIGuard.Try("Calendar.GrowingSeason", () =>
                {
                    List<Twelfth> twelfths = GenTemperature.TwelfthsInAverageTemperatureRange(
                        tile, MinGrowthTemp, MaxGrowthTemp);

                    if (twelfths == null)
                        return;

                    for (int i = 0; i < twelfths.Count; i++)
                        growingTwelfths.Add((int) twelfths[i]);
                },
                "The calendar's year bar shows no growing season.");
        }
    }
}
