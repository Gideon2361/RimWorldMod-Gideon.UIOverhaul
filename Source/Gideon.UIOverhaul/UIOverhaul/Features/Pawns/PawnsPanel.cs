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

            // No column banding here, unlike the work tab. Banding exists to keep the eye on one column across a
            // wide grid of like values; this tab has six wide columns of unlike things -- a name, a sentence, two
            // bars -- where every column already looks different from its neighbors. The stripes only competed
            // with the bars' own colors.
            AlternatingColumnBands = false
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

                float wanted = Grid.RequestedWidth + WindowChrome;

                // The window grows for the pane rather than the grid shrinking into it. The columns are fixed
                // widths, so taking 330px off the grid would clip a column or force a horizontal scrollbar under a
                // table that had been fitting perfectly -- the row you clicked would move to make room for the
                // panel describing it.
                if (paneFor != null)
                    wanted += PawnWorkPane.PaneWidth + PaneGap;

                return Mathf.Min(wanted, UI.screenWidth - 16f);
            }
        }

        /// <summary>Gap between the grid and the pane, matching the architect tab's.</summary>
        private const float PaneGap = 8f;

        /// <summary>
        /// Width the work pane needs, or zero when it is closed.
        ///
        /// Read by the window every frame to decide how much wider than the player's own size it should be.
        /// See <see cref="Tabs.IUITabWidthReservation"/>.
        /// </summary>
        internal static float PaneReservation => paneFor == null ? 0f : PawnWorkPane.PaneWidth + PaneGap;

        internal static float WindowHeight => Mathf.Min(760f, UI.screenHeight * 0.8f);

        /// <summary>
        /// The one pawn whose schedule is open, or null when none is.
        ///
        /// <b>One rather than a set, and this reverses an earlier decision.</b> It was a
        /// <c>HashSet&lt;Pawn&gt;</c> so that opening one row did not close another, on the reasoning that
        /// comparing two colonists' days side by side is most of the reason to look at the schedule. In practice
        /// that left rows expanded behind the player as they worked down the list, and it disagreed with the
        /// work pane beside it, which has always shown one pawn at a time.
        ///
        /// A single field rather than a set that is cleared before each add, so "only one is open" is a thing
        /// the type cannot express otherwise, rather than a rule some future call site has to remember.
        /// </summary>
        private static Pawn expandedPawn;

        private static readonly List<Pawn> Roster = new List<Pawn>();

        /// <summary>
        /// The pawn the work pane is open for, or null when it is closed.
        ///
        /// <b>Kept beside <see cref="expandedPawn"/> rather than derived from it.</b> They move together today,
        /// since one row is open at a time and opening it opens the pane. They are still two ideas: the pane can
        /// be closed on its own, and a later change that opens it from somewhere other than a row would have
        /// nothing to say about which row is unfolded.
        /// </summary>
        private static Pawn paneFor;

        /// <summary>
        /// Drops the pane and the fold state for a pawn who no longer exists.
        ///
        /// Subscribed rather than swept, so a colonist who dies while their pane is open is gone from it in the same
        /// frame. A destroyed pawn in <see cref="paneFor"/> would otherwise be asked for their priorities on the
        /// next draw, which is exactly the read the caches now throw <c>InvalidCacheRequest</c> for.
        /// </summary>
        /// <remarks>
        /// Wrapped, because a static constructor that throws takes the whole type with it: every later access
        /// raises TypeInitializationException, so a failure here would not cost the pane its cleanup, it would cost
        /// the tab its existence. Nothing inside can realistically fail, which is exactly why it is cheap to guard.
        ///
        /// The handler itself is not guarded here. PawnLifecycle invokes each subscriber through UIGuard already, so
        /// a second guard would only report the same failure twice.
        /// </remarks>
        static PawnsPanel()
        {
            try
            {
                PawnLifecycle.Gone += pawn =>
                {
                    if (expandedPawn == pawn)
                        expandedPawn = null;

                    if (paneFor == pawn)
                        paneFor = null;
                };
            }
            catch (System.Exception ex)
            {
                UIGuard.Report("Pawns.SubscribeLifecycle", ex,
                    "The pawns tab will not notice a colonist being destroyed, so an open work pane may have to be "
                    + "closed by hand.");
            }
        }

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        internal static void Draw(Rect inRect)
        {
            EnsureColumns();
            Collect();

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect content = inRect.ContractedBy(6f);

            // Above everything, spanning the grid and the pane both. The filters govern what the whole tab is
            // about, so putting them inside the grid's own area would read as a property of the table rather
            // than of the view.
            Rect filters = new Rect(content.x, content.y, content.width, FilterBarHeight);
            DrawFilterBar(filters, palette);

            content = new Rect(content.x, filters.yMax + FilterBarGap, content.width,
                Mathf.Max(0f, content.height - FilterBarHeight - FilterBarGap));

            // The pane takes its width off the right before the grid is laid out, so the grid draws into what is
            // left rather than being covered by it -- the same order the architect tab's option pane uses.
            if (paneFor != null)
            {
                Rect pane = new Rect(content.xMax - PawnWorkPane.PaneWidth, content.y,
                    PawnWorkPane.PaneWidth, content.height);

                content = new Rect(content.x, content.y,
                    content.width - PawnWorkPane.PaneWidth - PaneGap, content.height);

                // The window grows on the next frame rather than this one, since its rect is settled outside the
                // GUI pass. So the frame a pane opens on draws the grid one pane narrower and then it widens: a
                // single frame of a tighter table, rather than a pane drawn over the top of one.
                if (!PawnWorkPane.Draw(pane, paneFor, palette))
                    Close(paneFor);
            }

            Grid.Draw(content, palette);

            // After the grid, so the scroll view a portrait was clicked in has been closed out.
            PawnCameraJump.Resolve();
        }

        // ---------------------------------------------------------------------------------------
        // Category filters
        // ---------------------------------------------------------------------------------------

        private const float FilterBarHeight = 26f;
        private const float FilterBarGap = 6f;
        private const float FilterButtonGap = 4f;

        /// <summary>Padding either side of a filter button's label.</summary>
        private const float FilterButtonPadding = 22f;

        /// <summary>
        /// One button per category the game can actually produce.
        ///
        /// <b>The workbench's tab styling, and its correction with it.</b> An unselected button sits on a raised
        /// surface with full strength text, not on <c>ControlBackgroundFaded</c> with dimmed text -- that
        /// combination is this palette's vocabulary for a control that <i>cannot</i> be used, and it made the
        /// off half of a choice read as broken rather than as available. See <c>Dialog_XmlWorkbench.Mode</c>,
        /// where the same mistake was made and fixed.
        ///
        /// <b>Selected is filled in the category's own colour</b> rather than in one accent for all of them, so
        /// each button says which kind of person it governs and not merely that it is on.
        ///
        /// The same colours are the accent stripe on every row, so this bar reads as the legend for them. See
        /// <see cref="DrawRowBackground"/>.
        /// </summary>
        private static void DrawFilterBar(Rect bar, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;

                float x = bar.x;

                foreach (PawnCategory category in PawnCategories.All)
                {
                    if (!PawnCategories.Available(category))
                        continue;

                    string label = PawnCategories.Label(category);
                    float width = Text.CalcSize(label).x + FilterButtonPadding;

                    // Stops rather than wrapping or clipping. The bar sits above a table that is already the
                    // width it needs, so running out of room here means the window is narrower than anything
                    // was designed for, and a half drawn button is worse than a missing one.
                    if (x + width > bar.xMax)
                        break;

                    Rect button = new Rect(x, bar.y, width, bar.height);

                    if (DrawFilterButton(button, label, PawnCategories.Shown(category),
                            PawnCategories.Color(category, palette), palette))
                    {
                        PawnCategories.Toggle(category);
                        Grid.Scroll = Vector2.zero;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }

                    x += width + FilterButtonGap;
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static bool DrawFilterButton(Rect rect, string label, bool on, Color color,
            UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            if (on)
                UIElementPainter.FillRounded(rect, color);
            else
                UIElementPainter.OutlineRounded(rect, palette.Border,
                    over ? palette.SurfaceRaised : palette.PanelBackground);

            // Near black on the filled state, full strength text on the empty one. Both are the workbench's,
            // and both are chosen for contrast against what they actually sit on rather than for a rule about
            // selected controls being brighter.
            GUI.color = on ? palette.WindowBackground : palette.TextPrimary;

            Widgets.Label(rect, label);

            return Widgets.ButtonInvisible(rect);
        }

        // ---------------------------------------------------------------------------------------
        // Search
        //
        // Filters which colonists get rows at all, rather than dimming the ones that do not match, for the same
        // reason the work tab does: a list read by comparing rows is harder to read with gaps in it.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Our own text box, and the reason that is safe here is a patch rather than anything in this file.
        ///
        /// <b>A field of our own would let W and A pan the map while being typed into.</b>
        /// <c>WindowStack.AnySearchWidgetFocused</c> is the single gate every key binding consults, and it walks
        /// the window stack asking each window for its <c>CommonSearchWidget</c> -- so it can only ever see a
        /// vanilla <c>QuickSearchWidget</c> owned by a window, which this is not.
        ///
        /// <c>Patch_WindowStack_AnySearchWidgetFocused</c> closes that gate for every
        /// <see cref="UITextBoxControl"/> rather than for one window, so this box inherits the protection by
        /// being one. That is the whole reason it was written as a general patch when the work tab hit this, and
        /// it is why nothing here has to be done about the camera.
        /// </summary>
        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search",
            Icon = TexButton.Search,
            MaxLength = 30
        };

        /// <summary>
        /// The search field, in the name column's heading.
        ///
        /// The "Colonist" label goes rather than sharing the cell with it. The column under a search box that
        /// filters names does not need telling that it holds names, and a heading split between a label and a
        /// control leaves too little of either.
        /// </summary>
        private static void DrawSearchHeader(Rect cell, UIColorPaletteDef palette)
        {
            // Scroll reset on change: filtering can leave the view scrolled past everything that still matches,
            // which reads as the search finding nothing.
            if (Search.Draw(new Rect(cell.x + 6f, cell.y + 7f, cell.width - 12f, 26f), palette))
                Grid.Scroll = Vector2.zero;
        }

        private static void EnsureColumns()
        {
            if (Grid.Columns.Count == 6)
                return;

            Grid.Columns.Clear();

            // Tall enough for the search field with air around it. The other headings are one line of text and
            // sit centred in whatever this is, so raising it costs them nothing.
            Grid.HeaderHeight = 40f;

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Colonist",
                Width = NameColumnWidth,
                Bandable = false,

                // The search takes the name column's heading, as it does on the work tab: it filters by name, so
                // it belongs over the names, and this is the one heading wide enough to hold a control.
                DrawHeader = DrawSearchHeader,
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

        /// <summary>Reused between frames, so a rebuild does not allocate a list per map.</summary>
        private static readonly List<Pawn> Considered = new List<Pawn>();

        /// <summary>
        /// Every pawn on a map this tab could list, whatever the filters say.
        ///
        /// <b>Assembled from vanilla's own indexed lists where it can be.</b> Colonists, prisoners and slaves
        /// are each a list <c>MapPawns</c> already maintains, so taking them costs nothing. The two modded
        /// categories have no such list and are found by walking the map's humanlikes -- which is why that walk
        /// only happens when one of those mods is actually loaded, and never on a vanilla game.
        ///
        /// <b>Deduplicated on the way in,</b> because the sources overlap by design: a slave appears in the
        /// slave list, and Hospitality may also have an opinion about a pawn already claimed elsewhere. The
        /// category itself is decided once per pawn afterwards, in <c>PawnCategories.Of</c>.
        /// </summary>
        private static void Candidates(Map map)
        {
            Considered.Clear();

            MapPawns pawns = map.mapPawns;

            if (pawns == null)
                return;

            Take(pawns.FreeColonists);
            Take(pawns.PrisonersOfColonySpawned);
            Take(pawns.SlavesOfColonySpawned);

            if (!PawnCategories.Available(PawnCategory.Patient)
                && !PawnCategories.Available(PawnCategory.Guest))
                return;

            foreach (Pawn pawn in pawns.AllHumanlikeSpawned)
            {
                PawnCategory category = PawnCategories.Of(pawn);

                if (category == PawnCategory.Patient || category == PawnCategory.Guest)
                    Take(pawn);
            }
        }

        private static void Take(List<Pawn> source)
        {
            if (source == null)
                return;

            foreach (Pawn pawn in source)
                Take(pawn);
        }

        private static void Take(Pawn pawn)
        {
            if (pawn != null && !Considered.Contains(pawn))
                Considered.Add(pawn);
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

            // A match inside a folded group would otherwise not be shown, which reads as the search failing. The
            // folds themselves are remembered and come back when the search is cleared.
            Grid.SuppressCollapse = !Search.IsEmpty;

            List<Map> maps = Find.Maps;
            if (maps == null)
                return;

            List<Pawn> group = new List<Pawn>();

            foreach (Map map in maps)
            {
                group.Clear();
                Candidates(map);

                foreach (Pawn pawn in Considered)
                {
                    // Every pawn the tab could list joins the roster, matching or not. The roster answers "is
                    // this pawn still here", which is what decides whether an open pane still has a subject --
                    // and a pawn filtered out of view has not gone anywhere. Building it from the matches
                    // instead would close the pane the moment you searched for somebody else.
                    Roster.Add(pawn);

                    if (PawnCategories.Shown(PawnCategories.Of(pawn)) && PawnSearch.Matches(Search, pawn))
                        group.Add(pawn);
                }

                if (group.Count == 0)
                    continue;

                group.SortBy(p => p.LabelShortCap);

                Grid.Rows.Add(new UIDesignatorTabRow
                {
                    SectionLabel = MapLabels.NameOf(map),
                    SectionSuffix = group.Count == 1 ? "1 person" : group.Count + " people"
                });

                foreach (Pawn pawn in group)
                {
                    bool open = expandedPawn == pawn;

                    Grid.Rows.Add(new UIDesignatorTabRow
                    {
                        Payload = pawn,
                        Height = open ? RowHeight + ScheduleStripHeight : (float?) null,
                        DrawBackground = DrawRowBackground,
                        DrawOverlay = open ? (System.Action<Rect, UIDesignatorTabRow, UIColorPaletteDef>)
                            DrawScheduleStrip : null
                    });
                }
            }

            // A pane for a pawn who has no row is a panel pointing at nothing -- they have joined a caravan, been
            // captured, or left with a transport pod. Not a destroyed pawn, which PawnLifecycle handles directly;
            // this is the milder case of a colonist who still exists but is no longer listed.
            if (paneFor != null && !Roster.Contains(paneFor))
                paneFor = null;

            // No sweeping here any more. The readings live in PawnAttributes as shared per-attribute caches, and
            // holding a departed colonist is handled where it belongs: UICacheController prunes keys whose subject
            // has gone, and PawnLifecycle forgets a pawn outright the moment they are destroyed. Sweeping from here
            // as well would only duplicate that, and would do it against Roster rather than against whether the
            // pawn still exists, which is a different question.
        }

        /// <summary>
        /// The row's card, tinted by how much trouble the pawn is in, plus the whole-row click that opens the
        /// schedule.
        ///
        /// The click lives here rather than in a cell because the target is the row: every cell would otherwise
        /// have to forward it, and the gaps between cells would be dead. Registered on the top band only, so
        /// clicking inside an open schedule strip does not immediately close it again.
        /// </summary>
        /// <summary>
        /// The row's chrome, and the stripe down its left edge.
        ///
        /// <b>The stripe says which kind of person this is, not how they are.</b> It carried health until the
        /// tab started listing prisoners, slaves, patients and guests together, at which point the more urgent
        /// question a row has to answer became "why is this person on my colonist list" -- and health had
        /// somewhere better to be said anyway. The condition column two cells over now carries a severity
        /// badge and colored text, which is a stronger signal than three pixels of edge ever was, so nothing
        /// was lost in the trade.
        ///
        /// The colours are <see cref="PawnCategories.Color"/>, the same ones the filter bar fills its buttons
        /// with, so the bar reads as the legend for the stripes beneath it.
        /// </summary>
        private static void DrawRowBackground(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;

            RowCard.AccentColor = PawnCategories.Color(PawnCategories.Of(pawn), palette);
            RowCard.BackgroundColor = palette.PanelBackground;
            RowCard.DrawChrome(row, palette);

            Rect band = new Rect(row.x, row.y, row.width, Mathf.Min(RowHeight, row.height));

            if (expandedPawn == pawn)
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
        /// <summary>
        /// Opens or closes a row's schedule, and moves the work pane with it.
        ///
        /// One method for both hit targets -- the arrow and the rest of the band -- so the two cannot come to
        /// behave differently. Same sound the group headings use, because it is the same gesture.
        ///
        /// Expanding is what opens the pane, which is why there is no separate control for it: the row the player
        /// just opened is the pawn they are looking at.
        ///
        /// <b>Opening one row closes whichever was open.</b> Assigning the field is the whole of it -- there is no
        /// list to walk collapsing the others, because there is never more than one to collapse.
        /// </summary>
        private static void Toggle(Pawn pawn)
        {
            if (expandedPawn == pawn)
            {
                expandedPawn = null;
                Close(pawn);
            }
            else
            {
                expandedPawn = pawn;
                Open(pawn);
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static void Open(Pawn pawn)
        {
            if (paneFor == pawn)
                return;

            paneFor = pawn;

            // A new pawn starts at the top of their list rather than at wherever the previous one was scrolled to.
            PawnWorkPane.Reset();
        }

        private static void Close(Pawn pawn)
        {
            if (paneFor == pawn)
                paneFor = null;
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
            bool open = expandedPawn == pawn;
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
            PawnHealthSummary summary = PawnAttributes.Condition.Get(pawn);

            Rect band = TopBand(cell);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            // Captured and restored in a finally, like the font and the anchor beside it. This used to set it
            // back to a hardcoded true on the straight-line path, which is wrong twice over: it overwrites a
            // caller who wanted it false, and anything throwing in between left it false for the rest of the
            // frame -- which is what Text.StartOfOnGUI complains about, from somewhere with no clue who did it.
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;

                Rect line = new Rect(band.x + 8f, band.y, Mathf.Max(0f, band.width - 12f), band.height);

                // The badge takes the color and the label follows in it, so the two agree without either being
                // told about the other. DrawLeading returns where the text starts, which is the only reason this
                // cell never has to know how wide a badge is.
                float x = UITagControl.DrawLeading(line, summary.Tag, summary.TagColor(palette), palette);

                GUI.color = summary.Color(palette);

                Widgets.Label(new Rect(x, line.y, Mathf.Max(0f, line.xMax - x), line.height), summary.Label);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

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

            // The fraction is read live: vanilla already caches it behind a dirty flag, so it is cheaper than the
            // dictionary lookup a cache of our own would need. The two strings are cached, because each allocates.
            float fraction = PawnAttributes.HealthFractionOf(pawn);

            Color fill = fraction > 0.9f ? palette.Success
                : fraction > 0.35f ? palette.Info
                : palette.Danger;

            DrawLabeledBar(cell, palette, fraction, fill,
                PawnAttributes.HealthReading.Get(pawn), PawnAttributes.HealthTooltip.Get(pawn));
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

            Rect band = TopBand(cell);

            if (!PawnAttributes.HasMood(pawn))
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

            DrawLabeledBar(cell, palette, PawnAttributes.MoodFractionOf(pawn), palette.Mood,
                PawnAttributes.MoodReading.Get(pawn), PawnAttributes.MoodTooltip.Get(pawn));
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

            string report = PawnAttributes.Activity.Get(pawn);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = palette.TextSecondary;

                Rect textRect = new Rect(band.x + 8f, band.y, band.width - 12f, band.height);
                Widgets.Label(textRect, report.Truncate(textRect.width));
            }
            finally
            {
                // Truncate measures the string to fit, so it is the sort of call that can fail on a rect this
                // panel has resized to something unexpected. Restoring in a finally is what keeps that from
                // leaving word wrap off for the rest of the frame.
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (Mouse.IsOver(band))
                TooltipHandler.TipRegion(band, (TipSignal) report);
        }


        private static void DrawScheduleHintCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;
            Rect band = TopBand(cell);

            TimeAssignmentDef now = PawnAttributes.AssignmentOf(pawn);

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
        /// The strip itself, the brush picker and the painting all live in <see cref="ScheduleStrip"/>, because the
        /// template manager edits a day the same way and two copies of a paintable strip would be two copies that
        /// could drift apart. What stays here is the part that is about a pawn's row: where it sits under the row,
        /// which pawn it reads, and that painting invalidates that pawn's cached readings.
        /// </summary>
        private static void DrawScheduleStrip(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;

            if (pawn.timetable == null)
                return;

            Rect strip = new Rect(row.x + RowCard.AccentWidth + 8f, row.y + RowHeight,
                row.width - RowCard.AccentWidth - 16f, ScheduleStripHeight);

            const float gap = 8f;

            Rect picker = new Rect(strip.x, strip.y + 6f, ScheduleStrip.BrushWidth, ScheduleStrip.CellHeight);
            ScheduleStrip.DrawBrushPicker(picker, palette);

            Rect hours = new Rect(picker.xMax + gap, strip.y + 6f, strip.xMax - picker.xMax - gap,
                ScheduleStrip.CellHeight);

            ScheduleStrip.DrawHours(hours, palette,
                hour => pawn.timetable.GetAssignment(hour),
                (hour, assignment) =>
                {
                    pawn.timetable.SetAssignment(hour, assignment);

                    // The strip reads the timetable live, but the Schedule column's swatch is a cached reading, so
                    // it has to be told.
                    PawnAttributes.Invalidate(pawn);
                },
                GenLocalDate.HourOfDay(pawn));
        }

    }
}
