using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// Which way a grid's column headings run.
    ///
    /// Named orientations rather than a free angle, so the three that work are the three on offer. Each value
    /// *is* its angle in degrees, which is what the control translates it to -- so the enum documents the
    /// geometry rather than hiding it behind a lookup.
    /// </summary>
    public enum UIHeaderAngle
    {
        /// <summary>Level, for a grid whose columns are wide enough for their own names.</summary>
        Horizontal = 0,

        /// <summary>Straight up, which is the narrowest a column can get.</summary>
        Vertical = 90,

        /// <summary>Leaning, which keeps headings readable left to right while columns stay narrow.</summary>
        Diagonal = 60
    }

    /// <summary>How a tab divides its space before anything is drawn in it.</summary>
    public enum UIDesignatorTabLayout
    {
        /// <summary>One region, undivided. The grid fills the tab.</summary>
        Flat,

        /// <summary>A left and a right panel, as the grow-zone tab has a list and a detail pane.</summary>
        TwoColumn,

        /// <summary>Left, middle and right, as the architect tab has categories, contents and options.</summary>
        ThreeColumn
    }

    /// <summary>
    /// One panel of a <see cref="UIDesignatorTabLayout"/>.
    ///
    /// Not to be confused with <see cref="UIDesignatorTabColumn"/>, which is a column *of the grid*. This is one
    /// of the two or three regions the tab is divided into; a grid lives inside one of them.
    /// </summary>
    public class UIDesignatorTabLayoutColumn
    {
        /// <summary>
        /// Width in pixels, or zero to take what is left.
        ///
        /// Zero is what makes a three-column tab work: the side panels are the size their contents need, and
        /// the middle takes the rest, which is the only one whose width depends on the window. Several flexible
        /// panels share the remainder equally.
        /// </summary>
        public float Width;

        /// <summary>Draws this panel's contents. Called after the grid, if this panel hosts one.</summary>
        public Action<Rect, UIColorPaletteDef> DrawContent;

        /// <summary>
        /// Whether the grid is drawn in this panel.
        ///
        /// If no panel claims it, one is chosen by layout: the only panel when flat, the left one in two
        /// columns, the middle one in three.
        /// </summary>
        public bool HostsGrid;

        /// <summary>Fills the panel with <c>PanelBackground</c> first, so it reads as a panel.</summary>
        public bool DrawPanelBackground;
    }

    /// <summary>
    /// One column of a <see cref="UIDesignatorTabControl"/>.
    ///
    /// A column owns its width and its heading; the cells are drawn by <see cref="DrawCell"/>, which the
    /// control calls once per row with the rect that cell occupies. Nothing about what a cell contains is the
    /// control's business, which is what lets one grid hold portraits, numbers, checkboxes and buttons.
    /// </summary>
    public class UIDesignatorTabColumn
    {
        /// <summary>Heading text. Null draws no heading for this column, leaving its space blank.</summary>
        public string Label;

        /// <summary>
        /// Width in pixels. The reason this is per column rather than one width for the grid: a name column and
        /// a column holding a single number have nothing in common, and a grid that made them equal would be
        /// paying the wider one's price everywhere.
        /// </summary>
        public float Width = 52f;

        /// <summary>Tooltip over the whole column heading.</summary>
        public string Tooltip;

        /// <summary>
        /// Draws this column's heading instead of <see cref="Label"/>, in the heading cell.
        ///
        /// For a heading that is a control rather than a word. The work tab's manual-priorities checkbox lives
        /// in the heading over the name column, because it governs every column to its right -- and a checkbox
        /// is not a label. Never rotated: something interactive under a rotated matrix takes its clicks in the
        /// wrong place.
        /// </summary>
        public Action<Rect, UIColorPaletteDef> DrawHeader;

        /// <summary>
        /// Draws one cell. Called with the cell's rect, the row it belongs to, and the active palette.
        ///
        /// Null draws nothing, which is useful for a spacer column.
        /// </summary>
        public Action<Rect, UIDesignatorTabRow, UIColorPaletteDef> DrawCell;

        /// <summary>
        /// Whether this column takes part in the alternating banding.
        ///
        /// False for the columns at the left that are not part of the repeating grid -- a name, a row of tools
        /// -- so the alternation starts counting at the first column that is. Banding those would put a stripe
        /// through content that is not a column of like values.
        /// </summary>
        public bool Bandable = true;

        /// <summary>
        /// Whether this column's heading turns with
        /// <see cref="UIDesignatorTabControl.HeaderLabelOrientation"/>.
        ///
        /// False keeps it level in a grid whose other headings are turned. A wide column at the left usually has
        /// room for its name and reads better level, even where the narrow ones do not.
        /// </summary>
        public bool RotateLabel = true;
    }

    /// <summary>
    /// One row of a <see cref="UIDesignatorTabControl"/>: either a data row, or a section heading that groups
    /// the rows under it.
    ///
    /// Both kinds live in the same list, in the order they should appear. A separate list of groups each holding
    /// their own rows is the obvious alternative and is worse: every layout, hit test and scroll calculation
    /// then has to walk two levels, and the control gains nothing it can do with the nesting.
    /// </summary>
    public class UIDesignatorTabRow
    {
        /// <summary>
        /// Whatever this row is about -- a pawn, a bill, a thing. The control never looks at it; it is here so
        /// a cell drawer can be a static method rather than a closure per row per frame.
        /// </summary>
        public object Payload;

        /// <summary>Overrides the control's row height for this row alone.</summary>
        public float? Height;

        /// <summary>Non-empty makes this a section heading rather than a data row.</summary>
        public string SectionLabel;

        /// <summary>Right-aligned text on a section heading -- a count, usually.</summary>
        public string SectionSuffix;

        /// <summary>
        /// Draws the row's background, before any cell. Card chrome, status washes, a selection highlight.
        ///
        /// Separate from the cells because it spans all of them, and because the control has to paint its
        /// column banding between this and the cells: over the background, under the content.
        ///
        /// Takes the row, as a cell drawer does, so this can be a static method reading <see cref="Payload"/>
        /// rather than a closure allocated per row per frame.
        /// </summary>
        public Action<Rect, UIDesignatorTabRow, UIColorPaletteDef> DrawBackground;

        public bool IsSection => !SectionLabel.NullOrEmpty();
    }

    /// <summary>
    /// A grid for a main tab: a column per thing being set, a row per thing being set on, and a heading row
    /// that stays put while the rows scroll under it.
    ///
    /// Written for the work tab and then made general, because the shape recurs -- assign, animals, wildlife
    /// and schedule are all the same figure, and every one of them wants the heading to stay visible.
    ///
    /// What the control owns: layout, the scroll view, the pinned heading, heading rotation and its banding,
    /// section headings, and the order the layers are painted in. What the caller owns: the contents of every
    /// cell, and the lists. The split is deliberate -- a control that also decided what a cell contains would
    /// only ever suit the tab it was written for.
    ///
    /// <code>
    /// // Built once, refilled as the data changes.
    /// grid.Columns.Add(new UIDesignatorTabColumn
    /// {
    ///     Label = "Name", Width = 210f, Bandable = false, RotateLabel = false,
    ///     DrawCell = (cell, row, palette) => DrawPawn(cell, (Pawn) row.Payload, palette)
    /// });
    ///
    /// grid.HeaderLabelOrientation = UIHeaderAngle.Diagonal;
    /// grid.Draw(inRect);
    /// </code>
    /// </summary>
    public class UIDesignatorTabControl
    {
        /// <summary>
        /// How the tab is divided. Flat by default: one region, which the grid fills.
        /// </summary>
        public UIDesignatorTabLayout Layout = UIDesignatorTabLayout.Flat;

        /// <summary>
        /// The panels <see cref="Layout"/> divides the tab into, left to right. The caller owns this list.
        ///
        /// Optional. Leave it empty and each layout gets sensible panels -- a flexible pane beside a 320px one
        /// in two columns, 220 and 280 either side of a flexible middle in three. Fill it in to set the widths,
        /// which is the usual reason to touch it: how wide a list of categories needs to be is a property of the
        /// categories, not of this control.
        ///
        /// Entries past what the layout uses are ignored; missing ones fall back to the default for that slot.
        /// </summary>
        public readonly List<UIDesignatorTabLayoutColumn> LayoutColumns =
            new List<UIDesignatorTabLayoutColumn>();

        /// <summary>Space between panels. Zero butts them together.</summary>
        public float LayoutGap = 8f;

        /// <summary>Left to right. The caller owns this list.</summary>
        public readonly List<UIDesignatorTabColumn> Columns = new List<UIDesignatorTabColumn>();

        /// <summary>Top to bottom, section headings included in place. The caller owns this list.</summary>
        public readonly List<UIDesignatorTabRow> Rows = new List<UIDesignatorTabRow>();

        /// <summary>
        /// Whether to draw the heading row at all.
        ///
        /// False gives the whole rect to the rows, for a tab that draws its own heading or wants none. The
        /// pinned-heading behavior is the control's, but it is not compulsory.
        /// </summary>
        public bool HasHeaderRow = true;

        /// <summary>
        /// Which way the column headings run. <see cref="UIHeaderAngle.Horizontal"/> by default.
        ///
        /// Anything other than horizontal stops a column having to be as wide as its own name: level,
        /// "Construction" sets the width of a column whose contents are a 26px box, and the grid pays for the
        /// longest word in every column across its whole height.
        /// </summary>
        public UIHeaderAngle HeaderLabelOrientation = UIHeaderAngle.Horizontal;

        /// <summary>
        /// The orientation as an angle in degrees. Each enum value is its own angle, so this is a cast rather
        /// than a table -- add a value to <see cref="UIHeaderAngle"/> and the geometry follows with no change
        /// here.
        /// </summary>
        private float HeaderLabelAngle => (int) HeaderLabelOrientation;

        /// <summary>
        /// Height of the heading row. Null derives it: enough for one line when the headings are level, enough
        /// for a leaning one when they are not.
        /// </summary>
        public float? HeaderHeight;

        public float RowHeight = 62f;

        public float RowGap = 2f;

        public float SectionHeaderHeight = 30f;

        /// <summary>
        /// Whether every other bandable column is tinted, so a column can be followed down the grid.
        ///
        /// Alternating rather than banding every column: consecutive stripes need a separator between them, and
        /// a stripe beside a stripe reads as neither.
        /// </summary>
        public bool AlternatingColumnBands = true;

        /// <summary>
        /// How strongly a banded column tints the rows.
        ///
        /// Weaker than the heading's own band, which is opaque: up there the band is the background, and down
        /// here the row is. A solid fill in the rows reads as a hole cut in the row rather than as a column.
        /// </summary>
        public float ColumnBandAlpha = 0.85f;

        /// <summary>Reserved for the vertical scrollbar when reporting <see cref="RequestedWidth"/>.</summary>
        public float ScrollBarWidth = 20f;

        /// <summary>
        /// Scroll position. Public so a caller can put it back where it was, or reset it when the data changes
        /// out from under the player.
        /// </summary>
        public Vector2 Scroll;

        /// <summary>How far a heading's band starts behind its column, so it crosses the bottom edge.</summary>
        private const float BandOverhang = 26f;

        private const float LevelHeaderHeight = 30f;
        private const float LeaningHeaderHeight = 76f;

        public float HeaderHeightResolved
        {
            get
            {
                if (HeaderHeight.HasValue)
                    return HeaderHeight.Value;

                return Leaning ? LeaningHeaderHeight : LevelHeaderHeight;
            }
        }

        /// <summary>
        /// Whether the headings are turned at all, which is what decides between the plain label path and the
        /// rotated one with its bands and clipping. Vertical and diagonal are both "turned"; only the angle
        /// differs, and at 90 degrees the sheared band degenerates to an upright stripe on its own.
        /// </summary>
        private bool Leaning => HeaderLabelOrientation != UIHeaderAngle.Horizontal;

        /// <summary>Total width of the columns, which is the width the grid wants for its content.</summary>
        public float ColumnsWidth
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < Columns.Count; i++)
                    total += Columns[i].Width;

                return total;
            }
        }

        /// <summary>
        /// What to ask a window for, so the grid needs no horizontal scrolling.
        ///
        /// The caller still has to add whatever its own chrome costs -- window margins, its own padding -- and
        /// to cap the result at the screen.
        /// </summary>
        /// <summary>
        /// Whichever of the two needs more room, not both.
        ///
        /// The scrollbar lives at the right of the *rows* and the last heading's tail lives at the right of the
        /// *heading row*. They are never on screen at the same height, so one allowance covers both -- and adding
        /// them made the tab wider than its own contents by the smaller of the two.
        /// </summary>
        public float RequestedWidth => ColumnsWidth + Mathf.Max(ScrollBarWidth, HeaderOverhang);

        /// <summary>
        /// Room the last column's heading needs to the right of the grid.
        ///
        /// A leaning heading runs up and to the *right* of its own column, so the last one ends outside the
        /// columns entirely -- and a grid sized to its columns alone clips that heading's tail against the panel
        /// edge. Which is exactly what happened to "Research" the first time this drew.
        ///
        /// Zero when the headings are level or vertical, neither of which travels sideways.
        /// </summary>
        public float HeaderOverhang
        {
            get
            {
                if (!HasHeaderRow || !Leaning || HeaderLabelOrientation == UIHeaderAngle.Vertical
                    || Columns.Count == 0)
                    return 0f;

                float radians = HeaderLabelAngle * Mathf.Deg2Rad;

                // The far end of the label, plus what the text's own height adds at this angle, less the half
                // column the heading starts from -- the pivot is the column's center, not its right edge.
                float reach = LabelLength * Mathf.Cos(radians)
                              + LabelHalfHeight * Mathf.Sin(radians)
                              - Columns[Columns.Count - 1].Width * 0.5f;

                return Mathf.Max(0f, reach);
            }
        }

        /// <summary>
        /// Section headings the player has collapsed, by label.
        ///
        /// By label rather than by row, because the rows are rebuilt -- most callers refill the list every frame
        /// from live data, so a reference to a row object would not survive to the next one. Public, so a caller
        /// can persist which groups were folded or open one itself.
        /// </summary>
        public readonly HashSet<string> CollapsedSections = new HashSet<string>();

        /// <summary>Whether clicking a section heading folds the rows under it.</summary>
        public bool CollapsibleSections = true;

        /// <summary>
        /// Draws every section expanded without forgetting which were folded.
        ///
        /// For a caller that is filtering the rows: a search whose match is inside a folded group finds nothing,
        /// which reads as the search being broken. Set this while a filter is active and clear it after, and the
        /// player's folds come back untouched.
        /// </summary>
        public bool SuppressCollapse;

        public bool IsCollapsed(UIDesignatorTabRow section)
        {
            return CollapsibleSections && !SuppressCollapse && section.IsSection
                   && CollapsedSections.Contains(section.SectionLabel);
        }

        /// <summary>Total height of the rows, including section headings and the gaps between rows.</summary>
        public float RowsHeight
        {
            get
            {
                float total = 0f;
                bool hidden = false;

                for (int i = 0; i < Rows.Count; i++)
                {
                    UIDesignatorTabRow row = Rows[i];

                    // A section heading is always laid out; it is what a collapsed group is collapsed to.
                    if (row.IsSection)
                    {
                        hidden = IsCollapsed(row);
                        total += HeightOf(row);
                        continue;
                    }

                    if (hidden)
                        continue;

                    total += HeightOf(row) + RowGap;
                }

                return total;
            }
        }

        private float HeightOf(UIDesignatorTabRow row)
        {
            if (row.Height.HasValue)
                return row.Height.Value;

            return row.IsSection ? SectionHeaderHeight : RowHeight;
        }

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Draws the tab: the layout's panels, and the grid inside whichever panel hosts it.
        /// </summary>
        public void Draw(Rect inRect, UIColorPaletteDef palette = null)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            int count = PanelCount;

            // The flat case is the common one and skips all of the width arithmetic below, which would only
            // divide the rect by one.
            if (count == 1)
            {
                DrawPanel(inRect, PanelAt(0, count), true, palette);
                return;
            }

            float fixedWidth = 0f;
            int flexible = 0;

            for (int i = 0; i < count; i++)
            {
                UIDesignatorTabLayoutColumn panel = PanelAt(i, count);

                if (panel.Width > 0f)
                    fixedWidth += panel.Width;
                else
                    flexible++;
            }

            float remaining = Mathf.Max(0f, inRect.width - fixedWidth - LayoutGap * (count - 1));
            float share = flexible > 0 ? remaining / flexible : 0f;

            int gridPanel = GridPanelIndex(count);
            float x = inRect.x;

            for (int i = 0; i < count; i++)
            {
                UIDesignatorTabLayoutColumn panel = PanelAt(i, count);
                float width = panel.Width > 0f ? panel.Width : share;

                DrawPanel(new Rect(x, inRect.y, width, inRect.height), panel, i == gridPanel, palette);
                x += width + LayoutGap;
            }
        }

        private void DrawPanel(Rect rect, UIDesignatorTabLayoutColumn panel, bool hostsGrid,
            UIColorPaletteDef palette)
        {
            if (panel.DrawPanelBackground)
                Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            if (hostsGrid)
                DrawGrid(rect, palette);

            // After the grid rather than before, so a panel that hosts one can still draw over it -- a footer, a
            // count, an overlay while something is being dragged.
            panel.DrawContent?.Invoke(rect, palette);
        }

        private int PanelCount
        {
            get
            {
                switch (Layout)
                {
                    case UIDesignatorTabLayout.TwoColumn: return 2;
                    case UIDesignatorTabLayout.ThreeColumn: return 3;
                    default: return 1;
                }
            }
        }

        /// <summary>
        /// The panel in a slot: the caller's if it supplied one, otherwise the default for that slot.
        ///
        /// Defaults rather than an error for a short list, because the widths are the only part most callers care
        /// about and a tab should draw before it is fully configured.
        /// </summary>
        private UIDesignatorTabLayoutColumn PanelAt(int index, int count)
        {
            if (index < LayoutColumns.Count && LayoutColumns[index] != null)
                return LayoutColumns[index];

            return DefaultPanel(index, count);
        }

        private static UIDesignatorTabLayoutColumn DefaultPanel(int index, int count)
        {
            if (count == 2)
            {
                return index == 0
                    ? new UIDesignatorTabLayoutColumn()
                    : new UIDesignatorTabLayoutColumn { Width = 320f, DrawPanelBackground = true };
            }

            if (count == 3)
            {
                if (index == 0)
                    return new UIDesignatorTabLayoutColumn { Width = 220f, DrawPanelBackground = true };

                if (index == 2)
                    return new UIDesignatorTabLayoutColumn { Width = 280f, DrawPanelBackground = true };
            }

            return new UIDesignatorTabLayoutColumn();
        }

        /// <summary>
        /// Which panel the grid goes in. A panel that asked for it wins; otherwise the one that layout implies --
        /// the left pane of two, as the grow-zone tab lists on the left, and the middle of three, as the
        /// architect tab puts its contents between categories and options.
        /// </summary>
        private int GridPanelIndex(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (i < LayoutColumns.Count && LayoutColumns[i] != null && LayoutColumns[i].HostsGrid)
                    return i;
            }

            return count == 3 ? 1 : 0;
        }

        private void DrawGrid(Rect inRect, UIColorPaletteDef palette)
        {
            float headerHeight = HasHeaderRow ? HeaderHeightResolved : 0f;

            Rect header = new Rect(inRect.x, inRect.y, inRect.width, headerHeight);
            Rect body = new Rect(inRect.x, inRect.y + headerHeight, inRect.width, inRect.height - headerHeight);

            // The heading is drawn before the scroll view and outside it, which is the whole of what pins it:
            // a vertical scroll moves the rows and cannot move something that is not in the view. It still
            // follows the horizontal scroll, because a heading has to stay over the column it names.
            if (HasHeaderRow)
                DrawHeader(header, palette);

            float rowsHeight = RowsHeight;

            // The scrollbar's width is only taken out of the rows when there is going to be a scrollbar.
            // Reserving it unconditionally left a strip of nothing down the right of every row -- which is the
            // gap between "as wide as the grid" and "as wide as the window".
            float available = body.width - (rowsHeight > body.height ? ScrollBarWidth : 0f);

            Rect view = new Rect(0f, 0f, Mathf.Max(ColumnsWidth, available), rowsHeight);

            Widgets.BeginScrollView(body, ref Scroll, view);

            float y = 0f;
            bool hidden = false;

            foreach (UIDesignatorTabRow row in Rows)
            {
                float height = HeightOf(row);
                Rect rect = new Rect(0f, y, view.width, height);

                if (row.IsSection)
                {
                    hidden = IsCollapsed(row);
                    DrawSectionHeader(rect, row, palette);
                    y += height;
                    continue;
                }

                if (hidden)
                    continue;

                DrawRow(rect, row, palette);
                y += height + RowGap;
            }

            Widgets.EndScrollView();
        }

        private void DrawRow(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            data.DrawBackground?.Invoke(row, data, palette);

            // Between the background and the cells on purpose: over whatever the row painted, so the banding
            // is visible, and under the cells, so nothing is painted over a value.
            if (AlternatingColumnBands)
                DrawColumnBands(row, palette);

            float x = row.x;

            foreach (UIDesignatorTabColumn column in Columns)
            {
                Rect cell = new Rect(x, row.y, column.Width, row.height);
                x += column.Width;

                column.DrawCell?.Invoke(cell, data, palette);
            }
        }

        /// <summary>
        /// The banded columns, over one row.
        ///
        /// Covers the gap below the row as well as the row itself, so a band is continuous down the grid rather
        /// than dashed at every row boundary. A section heading is drawn after the row above it and paints over
        /// the overhang.
        /// </summary>
        private void DrawColumnBands(Rect row, UIColorPaletteDef palette)
        {
            Color wash = new Color(palette.SurfaceSunken.r, palette.SurfaceSunken.g, palette.SurfaceSunken.b,
                ColumnBandAlpha);

            float x = row.x;
            int bandable = 0;

            foreach (UIDesignatorTabColumn column in Columns)
            {
                if (column.Bandable)
                {
                    if (bandable % 2 == 1)
                        Widgets.DrawBoxSolid(new Rect(x, row.y, column.Width, row.height + RowGap), wash);

                    bandable++;
                }

                x += column.Width;
            }
        }

        private void DrawSectionHeader(Rect rect, UIDesignatorTabRow row, UIColorPaletteDef palette)
        {
            bool collapsible = CollapsibleSections && !row.SectionLabel.NullOrEmpty();
            bool collapsed = IsCollapsed(row);
            bool over = collapsible && Mouse.IsOver(rect);

            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);

            if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), palette.Accent);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            float textX = rect.x + 12f;

            if (collapsible)
            {
                // Vanilla's own tree arrows, so the affordance is one players already read as foldable rather
                // than a glyph of our own invention.
                Texture2D arrow = collapsed ? TexButton.Reveal : TexButton.Collapse;
                Rect arrowRect = new Rect(rect.x + 10f, rect.y + (rect.height - ArrowSize) * 0.5f,
                    ArrowSize, ArrowSize);

                if (arrow != null)
                {
                    GUI.color = over ? palette.TextPrimary : palette.TextSecondary;
                    GUI.DrawTexture(arrowRect, arrow);
                    GUI.color = previousColor;
                }

                textX = arrowRect.xMax + 8f;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(textX, rect.y, rect.xMax - textX - 140f, rect.height), row.SectionLabel);

            if (!row.SectionSuffix.NullOrEmpty())
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextSecondary;
                Widgets.Label(new Rect(rect.xMax - 130f, rect.y, 120f, rect.height), row.SectionSuffix);
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            // The whole heading is the hit target, not just the arrow: a 16px arrow is a fussy thing to hit, and
            // there is nothing else on the heading to press by mistake.
            if (collapsible && Widgets.ButtonInvisible(rect))
            {
                if (collapsed)
                    CollapsedSections.Remove(row.SectionLabel);
                else
                    CollapsedSections.Add(row.SectionLabel);

                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        private const float ArrowSize = 18f;

        // ---------------------------------------------------------------------------------------
        // The heading row
        // ---------------------------------------------------------------------------------------

        private void DrawHeader(Rect header, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(header, palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            Text.WordWrap = false;

            // Grouped so headings and their bands clip to the heading row. When they lean, both reach past an
            // edge: a band has to overhang the bottom to meet its column flush, and a long heading would
            // otherwise be drawn over whatever is above the grid. Coordinates inside a group are local to it.
            GUI.BeginGroup(header);

            float x = -Scroll.x;
            int bandable = 0;

            foreach (UIDesignatorTabColumn column in Columns)
            {
                Rect cell = new Rect(x, 0f, column.Width, header.height);
                x += column.Width;

                bool banded = AlternatingColumnBands && column.Bandable && bandable % 2 == 1;
                if (column.Bandable)
                    bandable++;

                if (cell.xMax < 0f || cell.x > header.width)
                    continue;

                bool leaning = Leaning && column.RotateLabel;

                // A band is only sheared when the heading actually leans. At 90 degrees the shear produces an
                // upright stripe the width of the column -- which is the cell -- so vertical takes the same
                // path as horizontal and fills it, rather than a rotated rect that would leave the top of the
                // heading row unpainted for no reason.
                bool sheared = leaning && HeaderLabelOrientation != UIHeaderAngle.Vertical;

                if (banded)
                {
                    if (sheared)
                        DrawLeaningBand(cell, header.width, palette);
                    else
                        Widgets.DrawBoxSolid(cell, palette.SurfaceSunken);
                }

                // A custom heading wins over the label, and is never rotated: whatever it draws is likely
                // interactive, and something interactive under a rotated matrix takes its clicks in the wrong
                // place.
                if (column.DrawHeader != null)
                    column.DrawHeader(cell, palette);
                else if (!column.Label.NullOrEmpty())
                {
                    if (leaning)
                        DrawLeaningLabel(cell, column.Label, palette);
                    else
                        DrawLevelLabel(cell, column.Label, palette);
                }

                // Registered on the upright cell, never inside the rotation. A tooltip region is taken in
                // screen space, so one registered under a rotated matrix sits where the pointer is not.
                if (!column.Tooltip.NullOrEmpty() && Mouse.IsOver(cell))
                    TooltipHandler.TipRegion(cell, (TipSignal) column.Tooltip);
            }

            GUI.EndGroup();

            Text.WordWrap = previousWrap;
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        private static void DrawLevelLabel(Rect cell, string label, UIColorPaletteDef palette)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;
            GUI.color = Mouse.IsOver(cell) ? palette.TextPrimary : palette.TextSecondary;

            Widgets.Label(new Rect(cell.x, cell.y, cell.width, cell.height - 4f), label);
        }

        /// <summary>
        /// One heading, turned up from the bottom of its own column per <see cref="HeaderLabelOrientation"/>.
        ///
        /// Rotated about the bottom of the column so the headings fan up and to the right and each one points
        /// at the column it belongs to.
        /// </summary>
        private void DrawLeaningLabel(Rect cell, string label, UIColorPaletteDef palette)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Mouse.IsOver(cell) ? palette.TextPrimary : palette.TextSecondary;

            // Shifted along the lean by however far a band has travelled at this height, so the text runs up the
            // middle of its own lane rather than along the lane's left edge.
            Vector2 pivot = new Vector2(cell.center.x + PivotInset * LeanSlope, cell.yMax - PivotInset);

            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(-HeaderLabelAngle, pivot);

            // Bounded by how far the heading row reaches at this angle. Word wrap is off, so a heading longer
            // than that is clipped rather than wrapped into the first row.
            Widgets.Label(new Rect(pivot.x + 3f, pivot.y - 10f, LabelLength, 20f), label);

            GUI.matrix = previous;
        }

        /// <summary>Where a heading's pivot sits above the bottom of the heading row.</summary>
        private const float PivotInset = 4f;

        /// <summary>Half the height of the box a heading's text is drawn in.</summary>
        private const float LabelHalfHeight = 10f;

        /// <summary>
        /// How long a heading may be before it is clipped, measured along the lean.
        ///
        /// Bounded by how far the heading row reaches, with the text's own half-height taken off: a rotated text
        /// box reaches further than its center line does, and ignoring that shaved the tops off the longest
        /// names.
        /// </summary>
        private float LabelLength
        {
            get
            {
                float radians = HeaderLabelAngle * Mathf.Deg2Rad;

                return (HeaderHeightResolved - PivotInset - LabelHalfHeight * Mathf.Cos(radians))
                       / Mathf.Max(0.1f, Mathf.Sin(radians));
            }
        }

        /// <summary>
        /// The band behind a leaning heading.
        ///
        /// Sheared to match the heading rather than drawn upright: an upright stripe is crossed by three or four
        /// headings on the way up and links its own to nothing. The cross-section is the column's width measured
        /// across the lean, so the band's footprint at any height is exactly its own column.
        /// </summary>
        /// <summary>Height of one strip a leaning band is built from. See <see cref="DrawLeaningBand"/>.</summary>
        private const float BandStripHeight = 2f;

        /// <summary>
        /// The band behind a leaning heading, built from horizontal strips rather than drawn as a rotated
        /// rectangle.
        ///
        /// Rotating is the obvious way and it cannot be bounded. Unity's clipping -- <c>GUI.BeginGroup</c>, and
        /// every clip built on it -- stops working once <c>GUI.matrix</c> carries a rotation, so a rotated band
        /// drawn to overhang an edge really does draw past it, over whatever the panel sits on. Strips are
        /// axis-aligned rectangles whose bounds are arithmetic, so the band ends exactly where the heading row
        /// does, with no clip involved.
        ///
        /// The bottom strip is the column: same left edge, same width. Every strip above it is that column
        /// shifted right by its own rise, so the band's width matches the column's at every height -- which is
        /// what makes the heading's stripe and the row's stripe read as one lane.
        /// </summary>
        /// <param name="localWidth">Width of the heading row in group-local space, so strips stay inside it.</param>
        private void DrawLeaningBand(Rect cell, float localWidth, UIColorPaletteDef palette)
        {
            float slope = LeanSlope;

            for (float top = cell.y; top < cell.yMax; top += BandStripHeight)
            {
                float height = Mathf.Min(BandStripHeight, cell.yMax - top);

                // Measured from the bottom edge of the heading row, which is where the band meets its column.
                float rise = cell.yMax - (top + height * 0.5f);
                float x = cell.x + rise * slope;

                float left = Mathf.Max(x, 0f);
                float right = Mathf.Min(x + cell.width, localWidth);

                if (right <= left)
                    continue;

                Widgets.DrawBoxSolid(new Rect(left, top, right - left, height), palette.SurfaceSunken);
            }
        }

        /// <summary>Horizontal shift per pixel of rise, which is the lean expressed as a gradient.</summary>
        private float LeanSlope
        {
            get
            {
                float radians = HeaderLabelAngle * Mathf.Deg2Rad;
                float sin = Mathf.Sin(radians);

                return sin <= 0.001f ? 0f : Mathf.Cos(radians) / sin;
            }
        }
    }
}
