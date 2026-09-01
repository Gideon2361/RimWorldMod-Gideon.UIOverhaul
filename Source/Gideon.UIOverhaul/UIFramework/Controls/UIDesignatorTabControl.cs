using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
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
        /// Where this column's heading sits in its cell, or null to take the grid's own default.
        ///
        /// A heading points at the column it names, so it wants the alignment that column's cells use: a name
        /// column's heading belongs at the left edge, a bar's over the middle. Only worth setting once the
        /// headings are the same size as the data, which is when a heading over the wrong part of a wide
        /// column stops looking like a heading at all.
        /// </summary>
        public TextAnchor? HeaderAnchor;

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

        /// <summary>
        /// Whether this column's cells take their own clicks, and so must be cut out of any hit test the panel
        /// lays over a whole row.
        ///
        /// <b>Declared by the column rather than counted by the panel, because a count goes stale.</b> A panel
        /// whose rows are clickable has to exclude the columns holding controls, and the obvious way to write
        /// that -- subtract the width of the last column -- is right exactly until somebody adds another column
        /// after it. That is not hypothetical: the pawns tab's area dropdown was swallowed by its row twice, the
        /// second time because an Edit column was added to its right and the subtraction still named the area.
        ///
        /// Reading it off the columns means the answer is recomputed the moment the columns change, which is the
        /// only version of this that cannot rot. See <see cref="UIDesignatorTabControl.OwnedTailWidth"/>.
        /// </summary>
        public bool OwnsClicks;
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
        /// How deeply this heading is nested: 0 is a top-level group, 1 a group inside the one above it.
        ///
        /// Folding a heading hides every row under it <i>and</i> every deeper heading, down to the next heading
        /// at the same depth or shallower. So collapsing a map takes its categories with it, which is the only
        /// behaviour that makes a fold mean "hide this group".
        /// </summary>
        public int SectionDepth;

        /// <summary>
        /// What this heading's fold is remembered under, when <see cref="SectionLabel"/> is not unique.
        ///
        /// Folds are keyed by string so they survive the rows being rebuilt every frame. That works while every
        /// heading reads differently, and breaks the moment two of them do not: "Colonists" under one map and
        /// "Colonists" under another are one key, so folding either folds both. Set this to something unique --
        /// the map's name and the category together -- and keep the label short for reading.
        ///
        /// Empty falls back to the label, which is right for every list whose headings are already distinct.
        /// </summary>
        public string SectionKey;

        /// <summary>The string this heading's fold is stored under. See <see cref="SectionKey"/>.</summary>
        internal string FoldKey
        {
            get { return SectionKey.NullOrEmpty() ? SectionLabel : SectionKey; }
        }

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

        /// <summary>
        /// Draws over the row, after every cell, spanning all of them.
        ///
        /// The counterpart to <see cref="DrawBackground"/>, and it exists for the case a column cannot serve: a
        /// row that expands to reveal something the width of the whole grid rather than the width of one column.
        /// A row using <see cref="Height"/> to grow gets the extra space here, and its cells should keep to the
        /// top band so the two do not overlap.
        ///
        /// After the cells rather than before, because what it draws is usually interactive, and something drawn
        /// under a cell would take its clicks second.
        /// </summary>
        public Action<Rect, UIDesignatorTabRow, UIColorPaletteDef> DrawOverlay;

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
        /// The typeface for level column headings, or <see cref="UIFace.Game"/> to leave them as they were.
        ///
        /// Opt-in rather than a new default, because this control draws a dozen tabs and a caption change is
        /// not the sort of thing to land on eleven of them as a side effect of restyling the twelfth. A tab
        /// adopting the header convention sets these three; every other tab is drawn exactly as before.
        /// </summary>
        internal UIFace HeaderFace = UIFace.Game;

        /// <summary>Point size for <see cref="HeaderFace"/>. Ignored while the face is the game's.</summary>
        internal float HeaderPoints;

        /// <summary>
        /// Whether a level heading is upper-cased, and given the letter-spacing that makes small caps read.
        ///
        /// Paired with the mono face rather than offered on its own: upper-case at caption size is what
        /// separates a heading from the data under it once the heading stops being a different size.
        /// </summary>
        internal bool HeaderUppercase;

        /// <summary>
        /// Whether the heading sits on the grid's own sunken surface with a hairline under it, rather than on
        /// the panel background.
        ///
        /// The hairline is the point. A heading row that shares a color with the rows below it needs an edge
        /// or the first row reads as part of the heading.
        /// </summary>
        internal bool HeaderSeated;

        /// <summary>
        /// How many leading columns stay put when the grid is scrolled sideways. Zero means none, which is the
        /// behavior every grid had before this existed.
        ///
        /// For the column that says which row you are looking at. A wide grid scrolled right shows a screen of
        /// numbers with nothing to attach them to, and counting rows back to the name defeats the point of
        /// putting them side by side.
        ///
        /// A count rather than a per-column flag, because only *leading* columns can be pinned: a pinned column
        /// with a scrolling one to its left would slide out from under its own heading. Expressing it as "the
        /// first N" makes that structural instead of a rule to be enforced and explained.
        ///
        /// Pinning changes how the grid is drawn -- see <see cref="DrawGrid"/> -- so a grid that pins nothing
        /// takes exactly the path it always did. Two things are worth knowing before switching it on:
        ///
        /// A row's <see cref="UIDesignatorTabRow.DrawOverlay"/> is drawn in the scrolling region and is clipped
        /// by it, so an overlay that spans the whole grid loses whatever falls under the pinned strip.
        ///
        /// Section headings are drawn in both regions, which is deliberate: their label sits at the left, so it
        /// stays readable in the pinned strip while the heading's background continues across the scroll.
        /// </summary>
        public int PinnedColumns;

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
        /// Width of everything from the first column that owns its clicks to the right-hand end of the grid.
        ///
        /// <b>Everything after the first one, not only the ones marked.</b> A panel subtracts this from
        /// <see cref="ColumnsWidth"/> to find where its row-wide hit test has to stop, and stopping short of a
        /// control is the one failure that matters: the row's own <c>ButtonInvisible</c> is painted before any
        /// cell, so it takes the click and the control underneath never sees it. Overshooting costs a strip of
        /// row that no longer toggles, which nobody notices. So a plain column sitting between two interactive
        /// ones is given up rather than carved around.
        ///
        /// Zero when no column owns its clicks, which leaves such a panel's hit test exactly as wide as its
        /// columns.
        /// </summary>
        public float OwnedTailWidth
        {
            get
            {
                float tail = 0f;
                bool counting = false;

                for (int i = 0; i < Columns.Count; i++)
                {
                    if (Columns[i].OwnsClicks)
                        counting = true;

                    if (counting)
                        tail += Columns[i].Width;
                }

                return tail;
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
                   && CollapsedSections.Contains(section.FoldKey);
        }

        /// <summary>
        /// Walks the fold state across one heading, and answers whether that heading is itself hidden.
        ///
        /// <b>One method because the row loop is written three times</b> -- once to measure, once to draw a
        /// single region and once to draw a split one -- and three copies of a nesting rule is three chances for
        /// them to disagree about what is on screen.
        ///
        /// <paramref name="hiddenDepth"/> is the depth of the shallowest folded heading currently in force, or
        /// -1 when nothing is folded. A heading deeper than that is inside the fold and is skipped whole; a
        /// heading at that depth or shallower ends the fold and then starts its own, if it is folded too.
        /// </summary>
        private bool SectionHidden(UIDesignatorTabRow row, ref int hiddenDepth)
        {
            if (hiddenDepth >= 0 && row.SectionDepth > hiddenDepth)
                return true;

            hiddenDepth = IsCollapsed(row) ? row.SectionDepth : -1;

            return false;
        }

        /// <summary>Total height of the rows, including section headings and the gaps between rows.</summary>
        public float RowsHeight
        {
            get
            {
                float total = 0f;
                int hiddenDepth = -1;

                for (int i = 0; i < Rows.Count; i++)
                {
                    UIDesignatorTabRow row = Rows[i];

                    // A section heading is laid out unless it is nested inside a folded one; it is what a
                    // collapsed group is collapsed to.
                    if (row.IsSection)
                    {
                        if (SectionHidden(row, ref hiddenDepth))
                            continue;

                        total += HeightOf(row);
                        continue;
                    }

                    if (hiddenDepth >= 0)
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
        /// <summary>
        /// Which section heading the pointer was over, and which one to draw as hovered.
        ///
        /// <b>Two fields, one frame apart, because a heading is drawn twice.</b> A pinned split draws each
        /// heading once for the scrolling region and once for the pinned strip, and the scrolling half goes
        /// first -- so when it asks whether it is hovered, the pointer may be in the pinned half it has not
        /// reached yet. Testing each rect alone lit only the half under the pointer, which made one bar read as
        /// two panels butted together. Recording the answer and using it on the next frame covers both halves
        /// at a cost of about sixteen milliseconds, which nobody can see.
        /// </summary>
        private string hoverSection;

        private string hoverSectionDrawn;

        public void Draw(Rect inRect, UIColorPaletteDef palette = null)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            // Rolled over before anything draws, so both passes this frame read the same answer.
            hoverSectionDrawn = hoverSection;
            hoverSection = null;

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

        /// <summary>
        /// Columns that stay put, clamped to something that makes sense.
        ///
        /// Pinning every column is not pinning -- there would be nothing left to scroll -- so the last column is
        /// always allowed to move. That also means a grid narrow enough to need no scrolling behaves the same
        /// pinned or not.
        /// </summary>
        private int PinnedCount => Mathf.Clamp(PinnedColumns, 0, Mathf.Max(0, Columns.Count - 1));

        private bool Pinning => PinnedCount > 0;

        /// <summary>Total width of the pinned columns, which is how much of the body they take.</summary>
        private float PinnedWidth
        {
            get
            {
                float total = 0f;
                int count = PinnedCount;

                for (int i = 0; i < count; i++)
                    total += Columns[i].Width;

                return total;
            }
        }

        private void DrawGrid(Rect inRect, UIColorPaletteDef palette)
        {
            float headerHeight = HasHeaderRow ? HeaderHeightResolved : 0f;

            Rect header = new Rect(inRect.x, inRect.y, inRect.width, headerHeight);
            Rect body = new Rect(inRect.x, inRect.y + headerHeight, inRect.width, inRect.height - headerHeight);

            if (Pinning)
            {
                DrawPinnedGrid(header, body, headerHeight, palette);
                return;
            }

            // The heading is drawn before the scroll view and outside it, which is the whole of what pins it:
            // a vertical scroll moves the rows and cannot move something that is not in the view. It still
            // follows the horizontal scroll, because a heading has to stay over the column it names.
            if (HasHeaderRow)
                DrawHeader(header, palette, 0, Columns.Count, -Scroll.x, 0);

            float rowsHeight = RowsHeight;

            // The scrollbar's width is only taken out of the rows when there is going to be a scrollbar.
            // Reserving it unconditionally left a strip of nothing down the right of every row -- which is the
            // gap between "as wide as the grid" and "as wide as the window".
            float available = body.width - (rowsHeight > body.height ? ScrollBarWidth : 0f);

            Rect view = new Rect(0f, 0f, Mathf.Max(ColumnsWidth, available), rowsHeight);

            Widgets.BeginScrollView(body, ref Scroll, view);

            float y = 0f;
            int hiddenDepth = -1;

            foreach (UIDesignatorTabRow row in Rows)
            {
                float height = HeightOf(row);
                Rect rect = new Rect(0f, y, view.width, height);

                if (row.IsSection)
                {
                    if (SectionHidden(row, ref hiddenDepth))
                        continue;

                    // One region, so this heading carries both halves.
                    DrawSectionHeader(rect, row, palette, showLabel: true, showSuffix: true);
                    y += height;
                    continue;
                }

                if (hiddenDepth >= 0)
                    continue;

                DrawRow(rect, row, palette, 0, Columns.Count);
                y += height + RowGap;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// The grid with its leading columns held still while the rest scrolls sideways.
        ///
        /// The pinned columns are drawn <b>outside the scroll view entirely</b>, in their own strip, rather than
        /// inside it with the scroll offset cancelled out. Cancelling the offset was the shorter version and does
        /// not work: the pinned cells would be drawn over content already painted beneath them, so the row card
        /// underneath -- accent stripe included -- would have to be repainted per strip, and anything the row
        /// drew for itself would show through. Reserving the width means nothing is ever drawn there twice.
        ///
        /// The cost is that the strip has to reproduce what the scroll view does for free: vertical scrolling is
        /// applied by hand as <c>-Scroll.y</c>, and clipping by a group. Those are the only two things a scroll
        /// view was giving it.
        ///
        /// Both regions walk the same row list in the same order with the same heights, so the two halves of a
        /// row always line up. The single shared <see cref="Scroll"/> is what keeps them in step vertically: the
        /// scroll view writes it, and the strip reads whatever it wrote.
        /// </summary>
        private void DrawPinnedGrid(Rect header, Rect body, float headerHeight, UIColorPaletteDef palette)
        {
            int pinned = PinnedCount;
            float pinnedWidth = PinnedWidth;

            Rect pinnedBody = new Rect(body.x, body.y, pinnedWidth, body.height);
            Rect scrollBody = new Rect(body.x + pinnedWidth, body.y, body.width - pinnedWidth, body.height);

            float rowsHeight = RowsHeight;
            float available = scrollBody.width - (rowsHeight > scrollBody.height ? ScrollBarWidth : 0f);

            // The view holds only the scrolling columns, so Scroll.x is measured from the first of them rather
            // than from the grid's left edge. Every offset below follows from that.
            float scrollingWidth = ColumnsWidth - pinnedWidth;
            Rect view = new Rect(0f, 0f, Mathf.Max(scrollingWidth, available), rowsHeight);

            if (HasHeaderRow)
            {
                Rect scrollHeader = new Rect(header.x + pinnedWidth, header.y, header.width - pinnedWidth,
                    headerHeight);

                // Scrolling headings first, then the pinned ones over them: a leaning heading overhangs to the
                // left, and the pinned strip has to be what wins where they meet.
                DrawHeader(scrollHeader, palette, pinned, Columns.Count, -Scroll.x, BandableBefore(pinned));

                Rect pinnedHeader = new Rect(header.x, header.y, pinnedWidth, headerHeight);
                DrawHeader(pinnedHeader, palette, 0, pinned, 0f, 0);
            }

            // The count sits on the right of the scrolling region, which is the only one that reaches the right
            // edge. The name goes to the pinned strip below, unless nothing is pinned, in which case this region
            // is the whole heading and carries both.
            bool nothingPinned = pinnedWidth <= 0f;

            Widgets.BeginScrollView(scrollBody, ref Scroll, view);
            WalkRows(view.width, -pinnedWidth, pinned, Columns.Count, true, palette,
                sectionLabels: nothingPinned, sectionSuffixes: true);
            Widgets.EndScrollView();

            // Clipped rather than trusted to stay inside: a row is handed its full width so its card and accent
            // stripe are positioned as they always are, and the group is what stops the rest of it painting over
            // the scrolling region.
            GUI.BeginGroup(pinnedBody);
            WalkRows(pinnedWidth, 0f, 0, pinned, false, palette,
                sectionLabels: true, sectionSuffixes: false);
            GUI.EndGroup();
        }

        /// <summary>
        /// Draws every visible row for one region.
        /// </summary>
        /// <param name="regionWidth">Width available, for a section heading to span.</param>
        /// <param name="rowX">
        /// Where a row's full-width rect starts in this region's coordinates. Negative in the scrolling region,
        /// because a row begins under the pinned strip -- which is what keeps its columns lined up with their
        /// headings and its banding in phase.
        /// </param>
        /// <param name="scrolled">
        /// False for the pinned strip, which is outside any scroll view and so has to apply the vertical offset
        /// itself. Everything else about the two passes is identical.
        /// </param>
        private void WalkRows(float regionWidth, float rowX, int firstColumn, int lastColumn, bool scrolled,
            UIColorPaletteDef palette, bool sectionLabels, bool sectionSuffixes)
        {
            float y = scrolled ? 0f : -Scroll.y;
            int hiddenDepth = -1;

            foreach (UIDesignatorTabRow row in Rows)
            {
                float height = HeightOf(row);

                if (row.IsSection)
                {
                    if (SectionHidden(row, ref hiddenDepth))
                        continue;

                    // Spans the region rather than the row, so the heading's background reaches the edge in both
                    // passes. Its label is at the left, which is why the pinned strip keeps it legible.
                    DrawSectionHeader(new Rect(0f, y, regionWidth, height), row, palette,
                        sectionLabels, sectionSuffixes);
                    y += height;
                    continue;
                }

                if (hiddenDepth >= 0)
                    continue;

                DrawRow(new Rect(rowX, y, ColumnsWidth, height), row, palette, firstColumn, lastColumn);
                y += height + RowGap;
            }
        }

        /// <summary>
        /// How many of the first <paramref name="count"/> columns take part in the banding.
        ///
        /// The alternation counts bandable columns across the whole grid, so the scrolling pass has to start
        /// counting where the pinned pass left off. Without this the stripes would restart at the split and the
        /// two halves of the grid would disagree about which columns are shaded.
        /// </summary>
        private int BandableBefore(int count)
        {
            int bandable = 0;

            for (int i = 0; i < count && i < Columns.Count; i++)
            {
                if (Columns[i].Bandable)
                    bandable++;
            }

            return bandable;
        }

        /// <summary>
        /// One row, or the part of one that belongs to a region.
        ///
        /// <paramref name="row"/> is always the row's <b>full</b> width, even when only some columns are being
        /// drawn from it. That is what lets the background, the banding and the cells all be positioned from the
        /// same origin in either region, so the two halves of a pinned row cannot drift apart; the caller's
        /// clipping is what keeps each pass inside its own strip.
        /// </summary>
        private void DrawRow(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette, int firstColumn,
            int lastColumn)
        {
            data.DrawBackground?.Invoke(row, data, palette);

            // Between the background and the cells on purpose: over whatever the row painted, so the banding
            // is visible, and under the cells, so nothing is painted over a value.
            if (AlternatingColumnBands)
                DrawColumnBands(row, palette, firstColumn, lastColumn);

            float x = row.x;

            for (int i = 0; i < Columns.Count && i < lastColumn; i++)
            {
                UIDesignatorTabColumn column = Columns[i];
                Rect cell = new Rect(x, row.y, column.Width, row.height);
                x += column.Width;

                if (i >= firstColumn)
                    column.DrawCell?.Invoke(cell, data, palette);
            }

            // Only in the region that owns the right-hand side of the row. An overlay spans the whole grid, and
            // drawing it in both regions would draw it twice; the scrolling one is where most of it lands.
            if (lastColumn >= Columns.Count)
                data.DrawOverlay?.Invoke(row, data, palette);
        }

        /// <summary>
        /// The banded columns, over one row.
        ///
        /// Covers the gap below the row as well as the row itself, so a band is continuous down the grid rather
        /// than dashed at every row boundary. A section heading is drawn after the row above it and paints over
        /// the overhang.
        /// </summary>
        private void DrawColumnBands(Rect row, UIColorPaletteDef palette, int firstColumn, int lastColumn)
        {
            Color wash = new Color(palette.SurfaceSunken.r, palette.SurfaceSunken.g, palette.SurfaceSunken.b,
                ColumnBandAlpha);

            float x = row.x;
            int bandable = 0;

            // Counted from column zero even when drawing starts later, so the alternation stays in phase across a
            // pinned split rather than restarting at it.
            for (int i = 0; i < Columns.Count && i < lastColumn; i++)
            {
                UIDesignatorTabColumn column = Columns[i];

                if (column.Bandable)
                {
                    if (bandable % 2 == 1 && i >= firstColumn)
                        Widgets.DrawBoxSolid(new Rect(x, row.y, column.Width, row.height + RowGap), wash);

                    bandable++;
                }

                x += column.Width;
            }
        }

        /// <summary>
        /// Draws a section heading across one region.
        ///
        /// <b>The heading is drawn once per region but its text belongs to one of them.</b> Both passes need the
        /// background, or the heading would stop at the pinned split; neither needs the words twice. Drawing the
        /// full heading in both gave the label a region-width rect in the pinned strip, where after the arrow and
        /// the space reserved for the suffix there is barely room for a word -- so a name as ordinary as "Colony"
        /// wrapped onto two lines and then clipped, next to a second, correct copy of itself.
        ///
        /// So the label goes on the left of the pinned strip and the count on the right of the scrolling region,
        /// which is where each of them was always meant to sit. With nothing pinned, one region does both.
        /// </summary>
        private void DrawSectionHeader(Rect rect, UIDesignatorTabRow row, UIColorPaletteDef palette,
            bool showLabel, bool showSuffix)
        {
            bool collapsible = CollapsibleSections && !row.SectionLabel.NullOrEmpty();
            bool collapsed = IsCollapsed(row);

            // Recorded for the next frame as well as used now, so hovering either half of a split heading
            // lights the whole of it. See the notes on hoverSection.
            bool pointerHere = collapsible && Mouse.IsOver(rect);

            if (pointerHere)
                hoverSection = row.FoldKey;

            bool over = collapsible
                        && (pointerHere
                            || (!row.FoldKey.NullOrEmpty() && row.FoldKey == hoverSectionDrawn));

            // A nested heading is indented and left on the panel's own ground rather than the sunken bar, so the
            // two levels read as a group and its parts instead of as two groups of equal weight.
            float indent = row.SectionDepth * NestIndent;

            Rect band = new Rect(rect.x + indent, rect.y, Mathf.Max(0f, rect.width - indent), rect.height);

            if (row.SectionDepth <= 0)
                Widgets.DrawBoxSolid(band, palette.SurfaceSunken);

            if (over)
                Widgets.DrawBoxSolid(band, palette.HoverOverlay);

            // Only on the half carrying the label. Drawn in both passes it put a second stripe partway along
            // the bar, at the seam where the pinned strip ends -- which is most of what made one heading look
            // like two containers side by side.
            //
            // The accent belongs to the top level. A nested heading takes the border colour, which is enough to
            // mark where it starts without competing with the group it sits inside.
            if (showLabel)
                Widgets.DrawBoxSolid(new Rect(band.x, rect.y, 3f, rect.height),
                    row.SectionDepth <= 0 ? palette.Accent : palette.Border);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            float textX = band.x + 12f;

            if (collapsible && showLabel)
            {
                // Vanilla's own tree arrows, so the affordance is one players already read as foldable rather
                // than a glyph of our own invention.
                Texture2D arrow = collapsed ? TexButton.Reveal : TexButton.Collapse;
                Rect arrowRect = new Rect(band.x + 10f, rect.y + (rect.height - ArrowSize) * 0.5f,
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

            if (showLabel)
            {
                // Room for the suffix is only taken out of the label when the suffix shares this region. In the
                // pinned strip it does not, and reserving it there is what left the name too narrow to fit.
                float reserved = showSuffix ? 140f : 10f;

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = row.SectionDepth <= 0 ? palette.TextPrimary : palette.TextSecondary;
                Widgets.Label(new Rect(textX, rect.y, Mathf.Max(0f, rect.xMax - textX - reserved), rect.height),
                    row.SectionLabel);
            }

            if (showSuffix && !row.SectionSuffix.NullOrEmpty())
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
                    CollapsedSections.Remove(row.FoldKey);
                else
                    CollapsedSections.Add(row.FoldKey);

                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        private const float ArrowSize = 18f;

        /// <summary>How far each nesting level moves a heading in from the one above it.</summary>
        private const float NestIndent = 16f;

        // ---------------------------------------------------------------------------------------
        // The heading row
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The heading row, or the part of one that belongs to a region.
        /// </summary>
        /// <param name="startX">
        /// Where the first drawn column's heading begins, in region coordinates. The scroll offset for a
        /// scrolling region; zero for a pinned strip, which is the whole of what holds it still.
        /// </param>
        /// <param name="bandableOffset">
        /// How many bandable columns precede <paramref name="firstColumn"/>, so the alternation continues across
        /// a pinned split instead of restarting at it.
        /// </param>
        private void DrawHeader(Rect header, UIColorPaletteDef palette, int firstColumn, int lastColumn,
            float startX, int bandableOffset)
        {
            Widgets.DrawBoxSolid(header, HeaderSeated ? palette.SurfaceSunken : palette.PanelBackground);

            // Outside the group below, so it spans the whole heading rather than stopping where the columns do.
            if (HeaderSeated)
                Widgets.DrawBoxSolid(new Rect(header.x, header.yMax - 1f, header.width, 1f), palette.Border);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;

                // Grouped so headings and their bands clip to the heading row. When they lean, both reach past an
                // edge: a band has to overhang the bottom to meet its column flush, and a long heading would
                // otherwise be drawn over whatever is above the grid. Coordinates inside a group are local to it.
                //
                // With columns pinned the group does a second job: it is what stops a scrolling heading being drawn
                // across the pinned strip beside it.
                GUI.BeginGroup(header);

                float x = startX;
                int bandable = bandableOffset;

                for (int i = firstColumn; i < Columns.Count && i < lastColumn; i++)
                {
                    UIDesignatorTabColumn column = Columns[i];

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
                            DrawLevelLabel(cell, column.Label, palette, column.HeaderAnchor);
                    }

                    // Registered on the upright cell, never inside the rotation. A tooltip region is taken in
                    // screen space, so one registered under a rotated matrix sits where the pointer is not.
                    if (!column.Tooltip.NullOrEmpty() && Mouse.IsOver(cell))
                        TooltipHandler.TipRegion(cell, (TipSignal) column.Tooltip);
                }
            }
            finally
            {
                GUI.EndGroup();

                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private void DrawLevelLabel(Rect cell, string label, UIColorPaletteDef palette, TextAnchor? anchor)
        {
            bool styled = HeaderFace != UIFace.Game && HeaderPoints > 0f;

            // Centred over the column unless the column says otherwise, which is what a column whose cells are
            // left-aligned text wants: a heading centred over a name column points at nothing.
            Text.Anchor = anchor ?? (styled ? TextAnchor.MiddleLeft : TextAnchor.LowerCenter);

            // A styled heading is the same weight as the data under it, so hover cannot be the only thing
            // separating them -- it sits at TextDisabled and comes up to TextSecondary, one step below the
            // rows rather than one step above.
            GUI.color = styled
                ? Mouse.IsOver(cell) ? palette.TextSecondary : palette.TextDisabled
                : Mouse.IsOver(cell) ? palette.TextPrimary : palette.TextSecondary;

            if (!styled)
            {
                Text.Font = GameFont.Tiny;

                Widgets.Label(new Rect(cell.x, cell.y, cell.width, cell.height - 4f), label);

                return;
            }

            Rect box = new Rect(cell.x + HeaderPad, cell.y, Mathf.Max(0f, cell.width - HeaderPad * 2f),
                cell.height);

            UITextControl.LabelEllipses(box, HeaderUppercase ? label.ToUpperInvariant() : label, HeaderFace,
                HeaderPoints);
        }

        /// <summary>Breathing room either side of a styled heading, matching a cell's own inset.</summary>
        private const float HeaderPad = 10f;

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
