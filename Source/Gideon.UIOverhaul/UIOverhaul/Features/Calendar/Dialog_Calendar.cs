using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Calendar
{
    /// <summary>
    /// Fifteen days with today in the middle: seven behind, seven ahead.
    ///
    /// <b>A rolling window rather than a month.</b> RimWorld's year is four quadrums of fifteen days and nothing
    /// in it repeats weekly, so a wall-calendar grid would impose a structure the game does not have. What a
    /// player actually wants is "what just happened and what is coming", which is a window centered on now.
    ///
    /// <b>The two halves are not equally full, on purpose.</b> Behind today is the archive -- every letter and
    /// message the colony raised, with the day it happened on. Ahead of today is only what genuinely has a fire
    /// tick: birthdays, quest offers expiring, conditions lifting, and the storyteller's scheduled intervals.
    /// RimWorld decides most of what is coming at the moment it arrives, so a right-hand side as dense as the
    /// left would be fabricated. See <see cref="CalendarEntries"/>.
    ///
    /// <b>Forecast marks say a kind and no more.</b> The storyteller settles the timing of an incident well
    /// ahead of firing it, and settles which incident only at the last moment, so a mark can honestly say "major
    /// threat" and cannot honestly say "raid". Once it fires it stops being a forecast and becomes an archive
    /// entry on the same day, with its real name and its real detail -- which is the whole design in one
    /// sentence: predict the shape, record the substance.
    /// </summary>
    public class Dialog_Calendar : Window
    {
        /// <summary>Days either side of today. Fifteen total.</summary>
        private const int Radius = 7;

        private const int Columns = 5;
        private const float Pad = 10f;
        private const float CellGap = 4f;
        /// <summary>
        /// Row heights measured from what will actually be drawn, not from what Tiny would have been.
        ///
        /// Same reason as <see cref="CalendarWidget"/>: asking for Tiny does not guarantee getting it, and a day
        /// cell packed with rows sized for the wrong font clips every one of them. See <see cref="UIFonts"/>.
        /// </summary>
        private static float CellHeaderHeight => UIFonts.RowHeight(GameFont.Tiny);

        private static float EntryHeight => UIFonts.LineHeightOf(GameFont.Tiny) + 2f;

        private const float IconSize = 12f;

        private Vector2 scroll;

        public Dialog_Calendar()
        {
            doCloseX = true;
            doCloseButton = false;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(880f, 620f);

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Calendar.Dialog", inRect, () => DrawContents(inRect),
                "The calendar shows a failure notice. Nothing else is affected.");
        }

        private void DrawContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            Map map = Find.CurrentMap;

            if (map == null)
                return;

            float longitude = Find.WorldGrid.LongLatOf(map.Tile).x;
            int today = CalendarEntries.DayIndex(GenTicks.TicksAbs, longitude);
            int first = today - Radius;
            int last = today + Radius;

            Dictionary<int, List<CalendarEntry>> byDay = CalendarEntries.Gather(map, first, last, Radius);

            Rect header = new Rect(inRect.x, inRect.y, inRect.width, 28f);
            DrawHeader(header, palette);

            Rect grid = new Rect(inRect.x, header.yMax + Pad, inRect.width,
                Mathf.Max(0f, inRect.yMax - header.yMax - Pad));

            int rows = Mathf.CeilToInt((Radius * 2 + 1) / (float) Columns);
            float cellWidth = (grid.width - CellGap * (Columns - 1)) / Columns;
            float cellHeight = (grid.height - CellGap * (rows - 1)) / rows;

            for (int i = 0; i <= Radius * 2; i++)
            {
                int day = first + i;
                int column = i % Columns;
                int row = i / Columns;

                Rect cell = new Rect(grid.x + column * (cellWidth + CellGap),
                    grid.y + row * (cellHeight + CellGap), cellWidth, cellHeight);

                byDay.TryGetValue(day, out List<CalendarEntry> entries);

                DrawDay(cell, day, today, longitude, entries, palette);
            }
        }

        private static void DrawHeader(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;
                Widgets.Label(rect, "Calendar");

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                Widgets.Label(rect,
                    CalendarForecast.Available
                        ? "Past events are recorded. Upcoming marks are scheduled but not yet chosen."
                        : "Upcoming storyteller marks are unavailable in this version of RimWorld.");

                Text.Anchor = TextAnchor.UpperLeft;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// One day: its date, and what is on it.
        ///
        /// Today is filled with the raised surface and given an accent edge, because a fifteen cell grid with no
        /// anchor is a grid nobody can read the middle of.
        /// </summary>
        private void DrawDay(Rect rect, int day, int today, float longitude, List<CalendarEntry> entries,
            UIColorPaletteDef palette)
        {
            bool isToday = day == today;
            bool past = day < today;

            Widgets.DrawBoxSolid(rect, isToday ? palette.SurfaceRaised : palette.PanelBackground);

            Color previousColor = GUI.color;
            GUI.color = isToday ? palette.Accent : palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                Rect head = new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, CellHeaderHeight);

                GUI.color = isToday ? palette.TextPrimary : past ? palette.TextSecondary : palette.TextSecondary;
                Widgets.Label(head, DayLabel(day, longitude));

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;
                Widgets.Label(head, isToday ? "today" : past ? (today - day) + "d ago" : "in " + (day - today) + "d");

                GUI.color = previousColor;
                Text.Anchor = TextAnchor.MiddleLeft;

                if (entries == null || entries.Count == 0)
                    return;

                entries.Sort((a, b) => a.Tick.CompareTo(b.Tick));

                float y = head.yMax + 2f;
                int shown = 0;
                int room = Mathf.FloorToInt((rect.yMax - 4f - y) / EntryHeight);

                for (int i = 0; i < entries.Count && shown < room; i++, shown++)
                    DrawEntry(new Rect(rect.x + 4f, y + shown * EntryHeight, rect.width - 8f, EntryHeight),
                        entries[i], palette);

                if (entries.Count > shown)
                {
                    GUI.color = palette.TextDisabled;
                    Widgets.Label(new Rect(rect.x + 4f, y + shown * EntryHeight, rect.width - 8f, EntryHeight),
                        "+" + (entries.Count - shown) + " more");
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static void DrawEntry(Rect rect, CalendarEntry entry, UIColorPaletteDef palette)
        {
            if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Color previousColor = GUI.color;

            if (entry.Icon != null)
            {
                GUI.color = entry.Tint;
                GUI.DrawTexture(new Rect(rect.x, rect.y + (rect.height - IconSize) * 0.5f, IconSize, IconSize),
                    entry.Icon);
                GUI.color = previousColor;
            }

            // A forecast is dimmed relative to a fact, so the two never read alike even at a glance.
            GUI.color = entry.Kind == CalendarEntryKind.Forecast ? palette.TextSecondary : palette.TextPrimary;

            Widgets.LabelEllipses(new Rect(rect.x + IconSize + 4f, rect.y, rect.width - IconSize - 4f,
                rect.height), entry.Label);

            GUI.color = previousColor;

            if (!entry.Tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) entry.Tooltip);

            // Only the recorded ones go anywhere. A forecast has no letter to open, because the thing it stands
            // for has not happened.
            if (entry.Archived != null && Widgets.ButtonInvisible(rect))
                entry.Archived.OpenArchived();
        }

        private static string DayLabel(int day, float longitude)
        {
            int dayOfYear = ((day % GenDate.DaysPerYear) + GenDate.DaysPerYear) % GenDate.DaysPerYear;
            Quadrum quadrum = QuadrumUtility.QuadrumsInChronologicalOrder[dayOfYear / GenDate.DaysPerQuadrum];

            return (dayOfYear % GenDate.DaysPerQuadrum + 1) + " " + quadrum.LabelShort();
        }
    }
}
