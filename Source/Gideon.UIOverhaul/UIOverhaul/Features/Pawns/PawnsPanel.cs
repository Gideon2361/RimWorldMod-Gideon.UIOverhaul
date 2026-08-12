using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The pawns tab: every player-controlled pawn, grouped by the map they are on, with their condition at a
    /// glance and their day's schedule one click away.
    ///
    /// Built on <see cref="UIDesignatorTabControl"/>, like the work tab, and grouped by map through
    /// <see cref="MapLabels"/> so a pocket dimension is named after the entrance that opens it rather than
    /// being one of several groups all called "Pocket map". The portrait and the click-to-center behavior come
    /// from <see cref="PawnPortraitCell"/> for the same reason -- one implementation, two tabs.
    ///
    /// Where it differs from the work tab: this is a status board rather than a grid of controls. Columns are
    /// wide and few, values are bars and sentences rather than numbers in boxes, and the row itself is the
    /// control -- clicking it opens the schedule.
    /// </summary>
    internal static class PawnsPanel
    {
        // ---------------------------------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Wide enough for the arrow, the portrait and a name beside them. Raised by the arrow's width and gap
        /// when the arrow was added, so the text kept the room it had rather than losing 24px of it.
        /// </summary>
        private const float NameColumnWidth = 264f;
        private const float HealthStateColumnWidth = 190f;
        private const float BarColumnWidth = 130f;
        private const float ActivityColumnWidth = 260f;

        private const float RowHeight = 62f;
        private const float BarHeight = 14f;
        private const float WindowChrome = 24f;

        /// <summary>How much taller an expanded row is: the schedule strip plus room to breathe around it.</summary>
        private const float ScheduleStripHeight = 54f;

        private static readonly UICardControl RowCard = new UICardControl { Padding = 0f, AccentWidth = 3f };

        private static readonly UIDesignatorTabControl Grid = new UIDesignatorTabControl
        {
            HeaderLabelOrientation = UIHeaderAngle.Horizontal,
            RowHeight = RowHeight,
            RowGap = 2f,
            SectionHeaderHeight = 30f,
            AlternatingColumnBands = true
        };

        /// <summary>
        /// The width the window asks for.
        ///
        /// <see cref="EnsureColumns"/> is called here, not only from <see cref="Draw"/>, and that is not
        /// belt-and-braces: RimWorld asks a window for its size *before* the first frame it draws. With the
        /// columns still unbuilt, RequestedWidth was the width of nothing and the tab opened about an inch
        /// wide, then corrected itself the next time it was opened -- by which point a draw had happened and
        /// the columns existed. Anything derived from the columns has to build them on demand.
        /// </summary>
        internal static float WindowWidth
        {
            get
            {
                EnsureColumns();
                return Mathf.Min(Grid.RequestedWidth + WindowChrome, UI.screenWidth - 16f);
            }
        }

        internal static float WindowHeight => Mathf.Min(760f, UI.screenHeight * 0.8f);

        /// <summary>
        /// Which pawns have their schedule open.
        ///
        /// A set of pawns rather than a single selection, so opening one does not close another -- comparing two
        /// colonists' days side by side is most of the reason to look at this at all.
        /// </summary>
        private static readonly HashSet<Pawn> Expanded = new HashSet<Pawn>();

        private static readonly List<Pawn> Roster = new List<Pawn>();

        /// <summary>
        /// What clicking an hour will paint. Shared across every row, because it is a tool the player picks up
        /// rather than a property of one pawn -- the same reason a paintbrush is not per-canvas.
        /// </summary>
        private static TimeAssignmentDef brush;

        private static TimeAssignmentDef Brush => brush ?? (brush = TimeAssignmentDefOf.Work);

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        internal static void Draw(Rect inRect)
        {
            EnsureColumns();
            Collect();

            Grid.Draw(inRect.ContractedBy(6f), UIColorPaletteDef.Active);

            // After the grid, so the scroll view a portrait was clicked in has been closed out.
            PawnCameraJump.Resolve();
        }

        private static void EnsureColumns()
        {
            if (Grid.Columns.Count == 6)
                return;

            Grid.Columns.Clear();

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Colonist",
                Width = NameColumnWidth,
                Bandable = false,
                DrawCell = DrawPawnCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Condition",
                Width = HealthStateColumnWidth,
                DrawCell = DrawConditionCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Health",
                Width = BarColumnWidth,
                DrawCell = DrawHealthBarCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Mood",
                Width = BarColumnWidth,
                DrawCell = DrawMoodCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Activity",
                Width = ActivityColumnWidth,
                DrawCell = DrawActivityCell
            });

            // A hint rather than a control: the schedule is opened by clicking the row, and a column that says
            // so is cheaper than a chevron nobody finds.
            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Schedule",
                Width = 110f,
                DrawCell = DrawScheduleHintCell
            });
        }

        /// <summary>
        /// Rebuilds the rows from the live game state, every frame.
        ///
        /// Grouped by map, and the group heading is <see cref="MapLabels.NameOf"/> -- which is what makes a
        /// pocket dimension show the name of its entrance box, and keeps showing the right one after a rename.
        /// </summary>
        private static void Collect()
        {
            Grid.Rows.Clear();
            Roster.Clear();

            List<Map> maps = Find.Maps;
            if (maps == null)
                return;

            List<Pawn> group = new List<Pawn>();

            foreach (Map map in maps)
            {
                group.Clear();

                foreach (Pawn pawn in map.mapPawns.FreeColonists)
                    group.Add(pawn);

                if (group.Count == 0)
                    continue;

                group.SortBy(p => p.LabelShortCap);

                Grid.Rows.Add(new UIDesignatorTabRow
                {
                    SectionLabel = MapLabels.NameOf(map),
                    SectionSuffix = group.Count == 1 ? "1 colonist" : group.Count + " colonists"
                });

                foreach (Pawn pawn in group)
                {
                    bool open = Expanded.Contains(pawn);

                    Grid.Rows.Add(new UIDesignatorTabRow
                    {
                        Payload = pawn,
                        Height = open ? RowHeight + ScheduleStripHeight : (float?) null,
                        DrawBackground = DrawRowBackground,
                        DrawOverlay = open ? (System.Action<Rect, UIDesignatorTabRow, UIColorPaletteDef>)
                            DrawScheduleStrip : null
                    });

                    Roster.Add(pawn);
                }
            }
        }

        /// <summary>
        /// The row's card, tinted by how much trouble the pawn is in, plus the whole-row click that opens the
        /// schedule.
        ///
        /// The click lives here rather than in a cell because the target is the row: every cell would otherwise
        /// have to forward it, and the gaps between cells would be dead. Registered on the top band only, so
        /// clicking inside an open schedule strip does not immediately close it again.
        /// </summary>
        private static void DrawRowBackground(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;
            PawnHealthSummary summary = PawnHealthSummary.For(pawn);

            RowCard.AccentColor = summary.State == PawnHealthState.Healthy
                ? palette.SurfaceRaised
                : summary.Color(palette);

            RowCard.BackgroundColor = palette.PanelBackground;
            RowCard.DrawChrome(row, palette);

            Rect band = new Rect(row.x, row.y, row.width, Mathf.Min(RowHeight, row.height));

            if (Expanded.Contains(pawn))
                Widgets.DrawBoxSolid(band, palette.SelectionOverlay);

            // The portrait is cut out of the row's hit target geometrically, rather than by relying on the
            // portrait's own button being drawn later and winning. It does not win: this background is drawn
            // before any cell, so its ButtonInvisible is the first one under the cursor and consumes the click
            // -- which is why clicking a face expanded the row instead of centering the view.
            //
            // Excluding it by rect is also the only version that cannot break again. Draw order here is the
            // control's business, not this panel's, and a hit test that depends on it is a hit test that breaks
            // the next time the control reorders anything.
            if (Widgets.ButtonInvisible(RowClickZone(band)))
                Toggle(pawn);
        }

        /// <summary>Left inset of the fold arrow inside the first column.</summary>
        private const float PortraitInset = 8f;

        /// <summary>Matches the group heading's arrow, so the two read as the same affordance.</summary>
        private const float ArrowSize = 18f;

        private const float ArrowGap = 6f;

        /// <summary>
        /// Where the fold arrow sits in a row: before the portrait, at the row's left edge.
        ///
        /// Vertically centered on the top band rather than on the whole row, so it stays level with the portrait
        /// and the name when the row is expanded and the rect grows underneath it.
        /// </summary>
        private static Rect ArrowFrame(Rect rowOrCell)
        {
            float band = Mathf.Min(RowHeight, rowOrCell.height);

            return new Rect(rowOrCell.x + PortraitInset, rowOrCell.y + (band - ArrowSize) * 0.5f,
                ArrowSize, ArrowSize);
        }

        /// <summary>
        /// Where the portrait sits in a row: after the arrow.
        ///
        /// Derived in one place, and derived *from* the arrow, so the cell that draws these and the background
        /// that has to avoid the portrait cannot disagree -- two copies of this arithmetic drifting apart is how
        /// the click region breaks silently. The first column starts at the row's own left edge, which is what
        /// lets all of it be computed from either rect.
        /// </summary>
        private static Rect PortraitFrame(Rect rowOrCell)
        {
            float top = rowOrCell.y + (Mathf.Min(RowHeight, rowOrCell.height) - PawnPortraitCell.Size) * 0.5f;

            return new Rect(ArrowFrame(rowOrCell).xMax + ArrowGap, top,
                PawnPortraitCell.Size, PawnPortraitCell.Size);
        }

        /// <summary>
        /// The row's hit target: everything in the band past the portrait, which owns its own click.
        ///
        /// A single rect starting past the portrait rather than the band with a hole in it. What is given up is
        /// the arrow and the margins around it, and the arrow has its own hit target covering that -- so between
        /// the two, the whole band toggles except the face.
        /// </summary>
        private static Rect RowClickZone(Rect band)
        {
            float left = PortraitFrame(band).xMax + PortraitInset;

            return new Rect(left, band.y, Mathf.Max(0f, band.xMax - left), band.height);
        }

        /// <summary>Whether the cursor is over anything that toggles the row, for tinting the arrow.</summary>
        private static bool OverToggle(Rect band)
        {
            return Mouse.IsOver(RowClickZone(band)) || Mouse.IsOver(ArrowFrame(band));
        }

        /// <summary>
        /// Opens or closes a row's schedule.
        ///
        /// One method for both hit targets -- the arrow and the rest of the band -- so the two cannot come to
        /// behave differently. Same sound the group headings use, because it is the same gesture.
        /// </summary>
        private static void Toggle(Pawn pawn)
        {
            if (!Expanded.Remove(pawn))
                Expanded.Add(pawn);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static void DrawPawnCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;

            Rect band = TopBand(cell);

            DrawFoldArrow(band, pawn, palette);

            Rect frame = PortraitFrame(cell);

            PawnPortraitCell.Draw(frame, pawn, palette, palette.PanelBackground);

            float textX = frame.xMax + 8f;
            float textWidth = cell.xMax - textX - 6f;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Anchor = TextAnchor.LowerLeft;
            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(textX, band.y + 8f, textWidth, 22f), pawn.LabelShortCap);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(textX, band.y + 32f, textWidth, Text.LineHeight), Subtitle(pawn));

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Mouse.IsOver(band))
            {
                string tip = pawn.LabelCap;

                if (PawnPortraitCell.IsOver(frame))
                    tip += "\n\nClick to select " + pawn.LabelShortCap + " and center the view on them.";
                else
                    tip += "\n\nClick to open this colonist's schedule.";

                TooltipHandler.TipRegion(band, (TipSignal) tip);
            }
        }

        /// <summary>
        /// The fold arrow, before the portrait.
        ///
        /// The same affordance the group headings use, down to the textures, the size and the tint: vanilla's own
        /// tree arrows, <c>Reveal</c> closed and <c>Collapse</c> open, so a player reads a foldable row without
        /// being taught a second glyph. It brightens whenever the cursor is anywhere that would toggle the row,
        /// not only over the arrow itself, which is how the group heading behaves.
        ///
        /// It carries its own hit target because the row's does not reach it -- the row's begins past the
        /// portrait. Between the two, everything in the band toggles except the face.
        ///
        /// Drawn from the cell rather than the row background so it sits above the card chrome, and after the
        /// background's own hit target is registered, so nothing consumes the arrow's click before it.
        /// </summary>
        private static void DrawFoldArrow(Rect band, Pawn pawn, UIColorPaletteDef palette)
        {
            bool open = Expanded.Contains(pawn);
            Rect arrowRect = ArrowFrame(band);

            Texture2D arrow = open ? TexButton.Collapse : TexButton.Reveal;

            if (arrow != null)
            {
                Color previous = GUI.color;

                GUI.color = OverToggle(band) ? palette.TextPrimary : palette.TextSecondary;
                GUI.DrawTexture(arrowRect, arrow);
                GUI.color = previous;
            }

            if (Widgets.ButtonInvisible(arrowRect))
                Toggle(pawn);
        }

        /// <summary>
        /// The line under the name: what the pawn is, in the terms the player thinks of them.
        ///
        /// The backstory title is the useful one here -- it is how colonists are told apart at a glance -- and
        /// it is exactly what the work tab's search had to stop matching on. Displaying it is right; filtering
        /// on it was not.
        /// </summary>
        private static string Subtitle(Pawn pawn)
        {
            string title = pawn.story?.TitleShortCap;

            if (!title.NullOrEmpty())
                return title;

            return pawn.KindLabel.CapitalizeFirst();
        }

        private static void DrawConditionCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;
            PawnHealthSummary summary = PawnHealthSummary.For(pawn);

            Rect band = TopBand(cell);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = summary.Color(palette);

            Widgets.Label(new Rect(band.x + 8f, band.y, band.width - 12f, band.height), summary.Label);

            Text.WordWrap = true;
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Mouse.IsOver(band))
                TooltipHandler.TipRegion(band, (TipSignal) summary.Detail);
        }

        /// <summary>
        /// Overall health as a bar.
        ///
        /// Green above 90%, blue through the middle, red when low -- so a scan down the column finds the
        /// casualties without reading a single number. The thresholds are on the fill only; the track stays
        /// neutral so the bar's length is still readable at a glance.
        /// </summary>
        private static void DrawHealthBarCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;

            float fraction = Mathf.Clamp01(pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f);

            Color fill = fraction > 0.9f ? palette.Success
                : fraction > 0.35f ? palette.Info
                : palette.Danger;

            DrawLabeledBar(cell, palette, fraction, fill, fraction.ToStringPercent(),
                "Overall health: " + fraction.ToStringPercent());
        }

        /// <summary>
        /// Mood as a bar, in the palette's <c>Mood</c> role.
        ///
        /// A framework role rather than a named custom color, because mood is not only this tab's idea: it is
        /// a reading other panels will want, and a role is what makes every one of them agree without passing
        /// a fallback around. A theme restates it by naming <c>&lt;mood&gt;</c>, the same as any other role.
        ///
        /// A pawn with no mood need -- a mech, most animals -- has no bar rather than an empty one, which
        /// would read as a colonist in despair.
        /// </summary>
        private static void DrawMoodCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;
            Need_Mood mood = pawn.needs?.mood;

            Rect band = TopBand(cell);

            if (mood == null)
            {
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;
                Widgets.Label(band, "--");

                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
                return;
            }

            float fraction = Mathf.Clamp01(mood.CurLevelPercentage);

            DrawLabeledBar(cell, palette, fraction, palette.Mood, mood.MoodString,
                "Mood: " + fraction.ToStringPercent() + " (" + mood.MoodString + ")"
                + "\n\nMental break at " + pawn.mindState?.mentalBreaker?.BreakThresholdMinor.ToStringPercent());
        }

        /// <summary>
        /// A bar with its reading centered under it, which is the shape both the health and mood columns want.
        ///
        /// The text goes under rather than inside: at this bar height a label inside would have to shrink to
        /// Tiny and sit on top of the fill, where it competes with the very color it is describing.
        /// </summary>
        private static void DrawLabeledBar(Rect cell, UIColorPaletteDef palette, float fraction, Color fill,
            string reading, string tooltip)
        {
            Rect band = TopBand(cell);

            Rect bar = new Rect(band.x + 10f, band.center.y - BarHeight, band.width - 20f, BarHeight);
            UIProgressBarControl.Draw(bar, fraction, palette, fill);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextSecondary;

            // Height from the font rather than a constant: Text.Font = Tiny is a request the game may answer
            // with Small, and a rect shorter than the line box clips the glyph tops.
            Widgets.Label(new Rect(band.x, bar.yMax + 2f, band.width, Text.LineHeight), reading);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Mouse.IsOver(band))
                TooltipHandler.TipRegion(band, (TipSignal) tooltip);
        }

        /// <summary>
        /// What the pawn is doing right now.
        ///
        /// <c>JobDriver.GetReport</c> is the same sentence the inspect pane shows, which is the point: a player
        /// reading this column should recognize the wording rather than learn a second vocabulary for it.
        ///
        /// A pawn can genuinely have no job for a frame -- between one and the next -- so this is guarded
        /// rather than assumed, and the report itself is wrapped: a modded JobDriver that throws from GetReport
        /// would otherwise take the whole tab down with it.
        /// </summary>
        private static void DrawActivityCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;

            Rect band = TopBand(cell);

            string report = Activity(pawn);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = palette.TextSecondary;

            Rect textRect = new Rect(band.x + 8f, band.y, band.width - 12f, band.height);
            Widgets.Label(textRect, report.Truncate(textRect.width));

            Text.WordWrap = true;
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Mouse.IsOver(band))
                TooltipHandler.TipRegion(band, (TipSignal) report);
        }

        private static string Activity(Pawn pawn)
        {
            JobDriver driver = pawn.jobs?.curDriver;

            if (driver == null)
                return "Idle";

            try
            {
                string report = driver.GetReport();
                return report.NullOrEmpty() ? "Idle" : report.CapitalizeFirst();
            }
            catch
            {
                // A mod's JobDriver, not ours to fix. Saying so beats an empty cell and beats a broken tab.
                return "(unavailable)";
            }
        }

        private static void DrawScheduleHintCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;
            Rect band = TopBand(cell);

            TimeAssignmentDef now = pawn.timetable?.CurrentAssignment;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            if (now == null)
            {
                GUI.color = palette.TextDisabled;
                Widgets.Label(band, "--");
            }
            else
            {
                Rect swatch = new Rect(band.center.x - 34f, band.center.y - 8f, 16f, 16f);
                Widgets.DrawBoxSolid(swatch, now.color);

                GUI.color = palette.Border;
                Widgets.DrawBox(swatch, 1);

                GUI.color = palette.TextSecondary;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(swatch.xMax + 6f, band.y, band.width, band.height), now.LabelCap);
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// The top slice of a cell, which is where a cell's content goes.
        ///
        /// An expanded row is taller than <see cref="RowHeight"/>, and the control hands every cell the full
        /// row rect. Trimming to the band keeps the values where they were before the row opened -- a portrait
        /// that drifted down the moment a schedule appeared would be worse than no expansion at all -- and
        /// leaves the rest of the row for the strip.
        /// </summary>
        private static Rect TopBand(Rect cell)
        {
            return new Rect(cell.x, cell.y, cell.width, Mathf.Min(RowHeight, cell.height));
        }

        // ---------------------------------------------------------------------------------------
        // The schedule strip
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The hour-by-hour schedule for one pawn, revealed under its row.
        ///
        /// Drawn as an overlay rather than as a column because it spans the whole grid: 24 hours will not fit in
        /// any one column, and splitting it across columns would tie the schedule's layout to the column widths.
        ///
        /// The dropdown is at the start of the row, and picking from it sets the brush rather than writing
        /// anything -- clicking an hour is what writes. That separation is what makes painting a block of hours
        /// one choice and several clicks instead of a choice per click.
        /// </summary>
        private static void DrawScheduleStrip(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;

            if (pawn.timetable == null)
                return;

            Rect strip = new Rect(row.x + RowCard.AccentWidth + 8f, row.y + RowHeight,
                row.width - RowCard.AccentWidth - 16f, ScheduleStripHeight);

            const float dropdownWidth = 120f;
            const float gap = 8f;

            Rect dropdown = new Rect(strip.x, strip.y + 6f, dropdownWidth, 24f);
            DrawBrushDropdown(dropdown, palette);

            Rect hours = new Rect(dropdown.xMax + gap, strip.y + 6f, strip.xMax - dropdown.xMax - gap, 24f);
            DrawHours(hours, pawn, palette);
        }

        /// <summary>
        /// The brush picker: which assignment a click paints.
        ///
        /// Every entry carries its own color swatch, in the def's own color, so the menu and the strip cannot
        /// disagree about what a color means -- and so the player picks a color rather than reading a word.
        /// </summary>
        private static void DrawBrushDropdown(Rect rect, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));

            Rect swatch = new Rect(rect.x + 5f, rect.y + 5f, 14f, 14f);
            Widgets.DrawBoxSolid(swatch, Brush.color);

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;

            GUI.color = palette.Border;
            Widgets.DrawBox(swatch, 1);

            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(swatch.xMax + 6f, rect.y, rect.width - 30f, rect.height), Brush.LabelCap);

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            GUI.color = previousColor;

            if (!Widgets.ButtonInvisible(rect))
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (TimeAssignmentDef def in Assignments)
            {
                TimeAssignmentDef captured = def;

                // The icon constructor rather than extraPartOnGUI. extraPartOnGUI draws to the right of the
                // label, which left the swatches ragged along the ends of variable-length words; iconTex is
                // drawn at the left and defaults to iconJustification = Left, so they line up in a column.
                //
                // A white 1x1 tinted by the def's color, rather than the def's own ColorTexture: that property
                // builds and caches a texture per def, and asking for it here would pin one for every
                // assignment a mod ever adds when a tint of the shared white does the same job.
                options.Add(new FloatMenuOption(captured.LabelCap, () => brush = captured,
                    BaseContent.WhiteTex, captured.color));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Every loaded assignment, not the five in the DefOf.
        ///
        /// Mods do add assignment types, and a dropdown that could not offer them would make this tab unable to
        /// set a schedule the vanilla tab can. Read from the database in load order, which is the order vanilla's
        /// own schedule tab uses, so the two read the same.
        /// </summary>
        private static List<TimeAssignmentDef> Assignments =>
            DefDatabase<TimeAssignmentDef>.AllDefsListForReading;

        /// <summary>
        /// The 24 hour cells.
        ///
        /// Dragging paints, not just clicking: setting eight hours of sleep is one gesture rather than eight
        /// clicks. Held-button painting is why this reads the mouse state directly instead of using
        /// ButtonInvisible -- a button reports a completed click, and a drag never completes one per cell.
        ///
        /// The current hour is outlined so the strip can be read against the clock without counting cells.
        /// </summary>
        private static void DrawHours(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            float cellWidth = rect.width / 24f;
            int currentHour = GenLocalDate.HourOfDay(pawn);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            for (int hour = 0; hour < 24; hour++)
            {
                Rect cell = new Rect(rect.x + hour * cellWidth, rect.y, cellWidth, rect.height);

                TimeAssignmentDef assignment = pawn.timetable.GetAssignment(hour);
                Widgets.DrawBoxSolid(cell.ContractedBy(0.5f), assignment.color);

                bool over = Mouse.IsOver(cell);

                if (over)
                    Widgets.DrawBoxSolid(cell.ContractedBy(0.5f), palette.HoverOverlay);

                GUI.color = hour == currentHour ? palette.Accent : palette.Border;
                Widgets.DrawBox(cell, hour == currentHour ? 2 : 1);

                GUI.color = palette.TextPrimary;
                Widgets.Label(cell, hour.ToString());

                // Mouse state rather than a click: this is what lets a drag paint a run of hours.
                if (over && Input.GetMouseButton(0) && assignment != Brush)
                {
                    pawn.timetable.SetAssignment(hour, Brush);
                    SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera();
                }

                if (over)
                {
                    TooltipHandler.TipRegion(cell, (TipSignal) (hour + ":00 -- " + assignment.LabelCap
                        + "\n\nClick or drag to set " + Brush.LabelCap + "."));
                }
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }
    }
}
