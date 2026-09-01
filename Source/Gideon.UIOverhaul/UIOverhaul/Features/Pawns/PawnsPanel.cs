using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Pawns.Templates;
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

        /// <summary>
        /// Width of the Area column: a colour chip, an area name and the caret.
        ///
        /// Wider than the Schedule column beside it because an area is named by the player and "Allowed area 4"
        /// is not a short word, where a time assignment is one of four the game names itself.
        /// </summary>
        private const float AreaColumnWidth = 150f;

        /// <summary>A switch and the padding around it, plus room for the heading over it.</summary>
        private const float SelfTendColumnWidth = 86f;

        /// <summary>
        /// Three icon segments and the padding either side of them.
        ///
        /// Derived from the segment row rather than written down, so the column cannot end up narrower than the
        /// control in it -- which is the fault that truncated the corpses tab's buttons. The segments are square,
        /// so their row is as wide as their height makes it.
        /// </summary>
        private static float ResponseColumnWidth =>
            Mathf.Ceil(TabParts.IconSegmentsWidth(PawnCombat.Modes.Length, SegmentHeight) + 16f);

        /// <summary>How tall one attack-mode segment is, and therefore how wide.</summary>
        private const float SegmentHeight = 24f;

        /// <summary>Five tool buttons and the padding either side of them.</summary>
        private const float EditColumnWidth = 5f * PawnTools.ButtonSize + 4f * PawnTools.ButtonGap + 16f;

        private const float RowHeight = 62f;
        private const float BarHeight = 14f;
        private const float WindowChrome = 24f;

        /// <summary>How much taller an expanded row is: the schedule strip plus room to breathe around it.</summary>
        private const float ScheduleStripHeight = 54f;

        /// <summary>
        /// How much taller this pawn's row becomes when it is opened: the schedule, and the policies under it.
        ///
        /// <b>Measured per pawn rather than fixed.</b> Neither band applies to everybody -- an animal has no
        /// timetable and no policies, a guest has no configurable food -- and a row charged for a band that then
        /// draws nothing opens onto empty background. Each part answers for its own height, so a pawn who has
        /// neither simply does not grow.
        /// </summary>
        /// <summary>
        /// How much taller this pawn's row becomes when it is opened: the day, the standing orders, and the work
        /// priorities.
        ///
        /// <b>Measured per pawn rather than fixed.</b> None of the three applies to everybody. An animal has no
        /// timetable and no policies, a guest has no configurable food, a mech can never be given work, and a row
        /// charged for a band that then draws nothing opens onto empty background. Each part answers for its own
        /// height, so a pawn who has none of them simply does not grow.
        ///
        /// <b>The work grid needs a width to answer,</b> because how many columns fit decides how many rows there
        /// are. The columns' own width is that answer: it is what the row is laid out across, and it is known
        /// before the row is sized.
        /// </summary>
        private static float ExpansionHeightFor(Pawn pawn)
        {
            return ScheduleHeightFor(pawn) + PolicyStrip.HeightFor(pawn)
                   + PawnWorkGrid.HeightFor(pawn, ExpansionWidth()) + BandGap * 2f;
        }

        /// <summary>The width the bands under a row are laid out in.</summary>
        private static float ExpansionWidth()
        {
            return Mathf.Max(120f, Grid.ColumnsWidth - RowCard.AccentWidth - 16f);
        }

        /// <summary>Between the three bands, so they read as three things rather than one block.</summary>
        private const float BandGap = 6f;

        private static float ScheduleHeightFor(Pawn pawn)
        {
            return pawn?.timetable == null ? 0f : ScheduleStripHeight;
        }

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

                // The rail is the one thing that widens this again, and only while there is more than one map
                // to pick between. The work priorities used to widen it too -- a 330px pane beside the table
                // that the window grew for and shrank back from -- but those are drawn inside the opened row
                // now.
                return Mathf.Min(Grid.RequestedWidth + WindowChrome + PawnMapRail.WidthNow,
                    UI.screenWidth - 16f);
            }
        }

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
        ///
        /// <b>It is now the only such field.</b> There was a second, for the pawn the work pane was open for, kept
        /// separate because a pane could be closed on its own. With the priorities inside the row there is one
        /// state again: the row is open or it is not.
        /// </summary>
        private static Pawn expandedPawn;

        private static readonly List<Pawn> Roster = new List<Pawn>();

        /// <summary>
        /// Drops the fold state for a pawn who no longer exists.
        ///
        /// Subscribed rather than swept, so a colonist who dies while their row is open is gone from it in the same
        /// frame. A destroyed pawn left in <see cref="expandedPawn"/> would otherwise be asked for their priorities
        /// on the next draw, which is exactly the read the caches now throw <c>InvalidCacheRequest</c> for.
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
            Color hue = Identity(palette);

            Rect content = inRect.ContractedBy(6f);

            Rect head = new Rect(content.x, content.y, content.width, HeaderHeight);
            DrawHeader(head, palette, hue);

            content = new Rect(content.x, head.yMax + HeaderGap, content.width,
                Mathf.Max(0f, content.yMax - head.yMax - HeaderGap));

            // Above both the rail and the table. The filters govern what the whole tab is about, so putting them
            // inside the grid's own area would read as a property of the table rather than of the view -- and
            // they are not a property of the rail either, which keeps its counts whatever is filtered.
            Rect filters = new Rect(content.x, content.y, content.width, FilterBarHeight);
            DrawFilterBar(filters, palette);

            content = new Rect(content.x, filters.yMax + FilterBarGap, content.width,
                Mathf.Max(0f, content.height - FilterBarHeight - FilterBarGap));

            if (PawnMapRail.Wanted)
            {
                Rect rail = new Rect(content.x, content.y, PawnMapRail.Width, content.height);

                // Collected again rather than left a frame stale. The pick changes which rows exist, and the
                // grid is drawn below this in the same frame; the second pass costs one click's worth of work.
                if (PawnMapRail.Draw(rail, palette, hue))
                {
                    Grid.Scroll = Vector2.zero;
                    Collect();
                }

                content = new Rect(rail.xMax + PawnMapRail.Gap, content.y,
                    Mathf.Max(0f, content.xMax - rail.xMax - PawnMapRail.Gap), content.height);
            }

            Grid.Draw(content, palette);

            // After the grid, so the scroll view a portrait was clicked in has been closed out.
            PawnCameraJump.Resolve();
        }

        // ---------------------------------------------------------------------------------------
        // Header
        // ---------------------------------------------------------------------------------------

        private const float HeaderHeight = 60f;
        private const float HeaderGap = 8f;
        private const float MarkSize = 28f;

        /// <summary>
        /// This tab's own color, the seventh of the per-tab identities.
        ///
        /// <b>It never touches a row.</b> This tab already spends six palette colors on its categories, and
        /// those say who a person is; this says which tab you are on. Keeping the two jobs apart is what lets
        /// a rose title sit above a blue-striped row without reading as two accents arguing.
        /// </summary>
        private static Color Identity(UIColorPaletteDef palette)
        {
            return palette.TabPawns;
        }

        /// <summary>
        /// The mark, the tab's name, which map is being read, and the four figures worth having without
        /// opening anything.
        ///
        /// <b>The figures ignore the category filters.</b> The subtitle says what is here and the filters say
        /// what you are looking at; unticking Patients should not make a hurt one stop counting.
        /// </summary>
        private static void DrawHeader(Rect rect, UIColorPaletteDef palette, Color hue)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            PawnMapRail.MapTally showing = PawnMapRail.Showing();

            Rect inner = rect.ContractedBy(10f);
            float text = inner.x;

            if (PawnTex.Mark != null)
            {
                Rect mark = new Rect(inner.x, inner.y + (inner.height - MarkSize) * 0.5f, MarkSize, MarkSize);

                Color previous = GUI.color;

                GUI.color = hue;

                GUI.DrawTexture(mark, PawnTex.Mark, ScaleMode.ScaleToFit);

                GUI.color = previous;

                text = mark.xMax + 10f;
            }

            TabParts.RowLabel(new Rect(text, inner.y + 1f, 420f, 24f), "Pawns", hue, GameFont.Medium,
                PawnFaces.Display, PawnFaces.Size.Title);

            TabParts.RowLabel(new Rect(text, inner.y + 25f, 420f, 18f), Subtitle(showing),
                palette.TextSecondary, GameFont.Tiny, PawnFaces.Condensed, PawnFaces.Size.Subtitle);

            float right = inner.xMax;

            right = Readout(inner, right, showing.People.ToString(),
                PawnMapRail.Wanted && PawnMapRail.Selected == PawnMapRail.AllMaps ? "everywhere" : "on this map",
                palette.TextPrimary, palette);

            right = Readout(inner, right,
                showing.MoodCount > 0
                    ? (showing.MoodTotal / showing.MoodCount).ToStringPercent()
                    : "--",
                "avg mood", palette.TextPrimary, palette);

            right = Readout(inner, right, showing.Idle.ToString(), "idle",
                showing.Idle > 0 ? palette.TextPrimary : palette.TextDisabled, palette);

            Readout(inner, right, showing.NeedsCare.ToString(), "needs care",
                showing.NeedsCare > 0 ? (showing.Urgent ? palette.Danger : palette.Warning)
                    : palette.TextDisabled, palette);
        }

        /// <summary>
        /// Which map, and what kind of people are on it.
        ///
        /// The breakdown rather than a bare total, because the total is already the rightmost figure and the
        /// question the rest of the tab is about is what the mix is.
        /// </summary>
        private static string Subtitle(PawnMapRail.MapTally showing)
        {
            string text = showing.Label;
            bool first = true;

            for (int i = 0; i < PawnCategories.All.Length; i++)
            {
                int members = showing.ByCategory[i];

                if (members == 0)
                    continue;

                string word = PawnCategories.Label(PawnCategories.All[i]).ToLowerInvariant();

                // "1 patients" reads as a bug. Undead has no plural s to shed, so it survives this unchanged.
                if (members == 1 && word.EndsWith("s"))
                    word = word.Substring(0, word.Length - 1);

                text += (first ? "  -  " : ", ") + members + " " + word;
                first = false;
            }

            return first ? text + "  -  nobody here" : text;
        }

        /// <summary>One figure and its caption, laid out from the right edge inward.</summary>
        private static float Readout(Rect inner, float right, string value, string caption, Color tint,
            UIColorPaletteDef palette)
        {
            float width = Mathf.Max(UITextControl.Width(value, PawnFaces.Mono, PawnFaces.Size.Readout),
                UITextControl.Width(caption.ToUpperInvariant(), PawnFaces.Mono, PawnFaces.Size.Caption)) + 4f;

            Rect box = new Rect(right - width, inner.y, width, inner.height);
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleRight;

            TabParts.RowLabel(new Rect(box.x, box.y + 2f, box.width, 20f), value, tint, GameFont.Small,
                PawnFaces.Mono, PawnFaces.Size.Readout);

            TabParts.RowLabel(new Rect(box.x, box.y + 24f, box.width, 14f), caption.ToUpperInvariant(),
                palette.TextDisabled, GameFont.Tiny, PawnFaces.Mono, PawnFaces.Size.Caption);

            Text.Anchor = previousAnchor;

            return box.x - 18f;
        }

        // ---------------------------------------------------------------------------------------
        // Category filters
        // ---------------------------------------------------------------------------------------

        private const float FilterBarHeight = 26f;
        private const float FilterBarGap = 6f;
        private const float FilterButtonGap = 4f;

        /// <summary>
        /// One button per category the game can actually produce.
        ///
        /// <b>The workbench's tab styling, and its correction with it.</b> An unselected button sits on a raised
        /// surface with full strength text, not on <c>ControlBackgroundFaded</c> with dimmed text -- that
        /// combination is this palette's vocabulary for a control that <i>cannot</i> be used, and it made the
        /// off half of a choice read as broken rather than as available. See <c>Dialog_XmlWorkbench.Mode</c>,
        /// where the same mistake was made and fixed.
        ///
        /// <b>The colour is a bar inside the chip, not a fill behind it.</b> Filling six chips in six saturated
        /// colours put the loudest thing in the mod above a table whose own bars are trying to be the coloured
        /// thing, and emphasising everything emphasises nothing. The bar is the same width and shape as the
        /// stripe down a row's left edge, which is what it stands for -- a closer match to its meaning than a
        /// filled rectangle was. See <see cref="DrawRowBackground"/>.
        ///
        /// <b>The counts are the real gain.</b> Without them the only way to learn there are two prisoners is
        /// to scroll to the Prisoners heading, and a filter worth pressing looks exactly like one that would
        /// empty the table.
        /// </summary>
        private static void DrawFilterBar(Rect bar, UIColorPaletteDef palette)
        {
            PawnMapRail.MapTally showing = PawnMapRail.Showing();

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                float x = bar.x;

                for (int i = 0; i < PawnCategories.All.Length; i++)
                {
                    PawnCategory category = PawnCategories.All[i];

                    if (!PawnCategories.Available(category))
                        continue;

                    string label = PawnCategories.Label(category);
                    string count = showing.ByCategory[i].ToString();

                    float width = FilterBarWidth + UITextControl.Width(label, PawnFaces.Condensed,
                        PawnFaces.Size.Chip) + UITextControl.Width(count, PawnFaces.Mono,
                        PawnFaces.Size.RailCount);

                    // Stops rather than wrapping or clipping. The bar sits above a table that is already the
                    // width it needs, so running out of room here means the window is narrower than anything
                    // was designed for, and a half drawn button is worse than a missing one.
                    if (x + width > bar.xMax)
                        break;

                    Rect button = new Rect(x, bar.y, width, bar.height);

                    if (DrawFilterButton(button, label, count, PawnCategories.Shown(category),
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

        /// <summary>The chip's fixed parts: lead pad, bar, both gaps, and the trailing pad.</summary>
        private const float FilterBarWidth = 9f + BarWidth + 7f + 7f + 11f;

        /// <summary>Matched to <c>UICardControl.AccentWidth</c> on a row, because it stands for that stripe.</summary>
        private const float BarWidth = 3f;

        private static bool DrawFilterButton(Rect rect, string label, string count, bool on, Color color,
            UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            // Selected tints the border and washes the inside; unselected leaves the panel showing through.
            // Neither state is filled, so the six of them cannot compete with each other or with the table.
            UIElementPainter.OutlineRounded(rect,
                on ? UIElementPainter.Composite(palette.Border, Wash(color, 0.55f)) : palette.Border,
                on ? UIElementPainter.Composite(palette.PanelBackground, Wash(color, 0.10f))
                    : over ? palette.SurfaceRaised : palette.PanelBackground);

            Rect stripe = new Rect(rect.x + 9f, rect.y + 6f, BarWidth, rect.height - 12f);

            // Square rather than rounded: the 9-slice's corners are wider than a 3px bar, so it would round
            // itself away to nothing. The row stripe this stands for is square for the same reason.
            //
            // Dimmed with its label rather than hidden, so an off chip reads as available rather than absent.
            Widgets.DrawBoxSolid(stripe, on ? color : Wash(color, 0.4f));

            float x = stripe.xMax + 7f;
            float labelWidth = UITextControl.Width(label, PawnFaces.Condensed, PawnFaces.Size.Chip);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = on ? palette.TextPrimary : palette.TextDisabled;

            UITextControl.Label(new Rect(x, rect.y, labelWidth, rect.height), label, PawnFaces.Condensed,
                PawnFaces.Size.Chip);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = on ? palette.TextSecondary : palette.TextDisabled;

            UITextControl.Label(new Rect(x + labelWidth, rect.y, rect.xMax - 11f - x - labelWidth,
                rect.height), count, PawnFaces.Mono, PawnFaces.Size.RailCount);

            return Widgets.ButtonInvisible(rect);
        }

        private static Color Wash(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
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

        /// <summary>
        /// Builds the columns once.
        ///
        /// <b>Tested for emptiness rather than for a count.</b> It read <c>Count == 6</c> while eight columns
        /// were being added, so the test never passed and the columns -- and the eight closures with them --
        /// were rebuilt on every frame and on every size query. A count written next to a builder that grows is
        /// a number that has to agree with something it cannot see; nothing else clears this list, so "is it
        /// built" is the honest question.
        /// </summary>
        private static void EnsureColumns()
        {
            if (Grid.Columns.Count > 0)
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

            // Next to the schedule because vanilla's Restrict tab pairs them, and a control rather than a hint
            // because it is the one thing on that tab this mod had no way to do at all.
            //
            // A column rather than another picker on the expanded row's policy strip, which is where the apparel,
            // food and drug policies live: an area is changed for a dozen pawns at once when a raid lands, and
            // behind a row expansion that is a dozen expand-and-collapse cycles.
            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Area",
                Width = AreaColumnWidth,
                Tooltip = "Where each pawn is allowed to be. Unrestricted lets them use the whole map.",
                OwnsClicks = true,
                DrawCell = DrawAreaCell
            });

            // The last two settings from vanilla's Assign tab this mod could read but not change, asked for on
            // 2026-08-23. Both are here beside the area and the schedule rather than inside the expanded row for
            // the reason the area is: these are the columns you sweep down after a raid, and behind a row
            // expansion a colony-wide sweep is a dozen expand-and-collapse cycles.
            //
            // Vanilla files them two tabs apart -- self-tend at the foot of the Health tab, attack mode as an
            // icon in the inspect pane's corner -- so putting them side by side is most of the point.
            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Attack mode",
                Width = ResponseColumnWidth,
                Tooltip = "HostilityReponseTip".Translate().ToString() + "\n\nIgnore, attack, or flee, left to right.",
                OwnsClicks = true,
                DrawCell = DrawResponseCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Self-tend",
                Width = SelfTendColumnWidth,
                Tooltip = "Whether each colonist patches their own wounds up rather than waiting for a doctor.",
                OwnsClicks = true,
                DrawCell = DrawSelfTendCell
            });

            // The whole pawn in one place, which is what the three bands underneath cannot offer: each of those
            // copies its own part, and copying a colonist wholesale means expanding the row and clicking three
            // buttons. Here it is one, and it is on the row rather than inside it, so a dozen pawns can be
            // templated without a dozen expand-and-collapse cycles.
            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Edit",
                Width = EditColumnWidth,
                OwnsClicks = true,
                Tooltip = "Clear, copy, paste and template everything about a pawn at once: their work "
                          + "priorities, their schedule and their policies.\n\nThe bands under an expanded row "
                          + "have the same four tools for each part on its own.",
                DrawCell = DrawEditToolsCell
            });
        }

        /// <summary>
        /// The whole-pawn tool row, on the row itself.
        ///
        /// Scoped to <see cref="PawnTemplateScope.Everything"/>, so a template saved here carries the priorities,
        /// the schedule and the policies, and one applied here writes all three.
        /// </summary>
        private static void DrawEditToolsCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = data.Payload as Pawn;

            if (pawn == null)
                return;

            // The top band only, like every other cell here: an expanded row's cell is as tall as the row plus
            // its bands, and a toolbar centred in that would float somewhere down beside the work grid.
            Rect band = TopBand(cell);

            // Five buttons on every row is fifty glyphs on a screen of ten people, all of them drawn at the
            // same strength as the data they sit beside. They come up to full on the row under the pointer and
            // on the open one. Nothing moves and nothing is hidden: the cluster keeps its place, its width and
            // its hit targets, so a click aimed from muscle memory still lands.
            bool lit = expandedPawn == pawn || hoveredPawn == pawn;

            PawnTools.Row(new Rect(band.x + 4f, band.y, band.width - 8f, band.height), pawn,
                PawnTemplateScope.Everything, palette, lit ? 1f : RestingTools);
        }

        /// <summary>How much of the row tools is drawn when the row is neither hovered nor open.</summary>
        private const float RestingTools = 0.3f;

        /// <summary>
        /// The pawn whose row is under the pointer, or null. Written by <see cref="DrawRowBackground"/> and
        /// cleared each time the rows are rebuilt.
        /// </summary>
        private static Pawn hoveredPawn;

        /// <summary>Reused between frames, so a rebuild does not allocate a list per map.</summary>
        private static readonly List<Pawn> Considered = new List<Pawn>();

        /// <summary>
        /// Scratch for the undead handed over by One with Death, reused rather than allocated per rebuild.
        ///
        /// Separate from <see cref="Considered"/> because that one is deduplicated on the way in and this is the
        /// raw list being fed into it.
        /// </summary>
        private static readonly List<Pawn> Undead = new List<Pawn>();

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

            // Taken from the necromancer's own list rather than sifted out of a map list, because an undead is not
            // necessarily humanlike: a raised animal would be missed entirely by the sweep below. See
            // OneWithDeathIntegration.Fill, which is a no op when that mod is absent.
            //
            // The map is passed in because that list is the only source here that is not already a map's own. It
            // holds everything the necromancers control anywhere, and this method runs once per map.
            //
            // Through Take rather than added directly, so an undead that is also in the colonist list arrives
            // once. Its category is decided afterwards, as every other pawn's is.
            Undead.Clear();

            Integrations.OneWithDeathIntegration.Fill(map, Undead);

            foreach (Pawn undead in Undead)
                Take(undead);

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
        /// <summary>
        /// How many distinct categories are represented in one map's listing.
        ///
        /// Counted rather than assumed, because the answer decides whether subgroup headings appear at all and
        /// it changes with the tabs across the top: filtering down to colonists alone should put the list back
        /// to a plain alphabetical run under the map, not leave one heading sitting over the whole of it.
        /// </summary>
        /// <summary>
        /// Folds one pawn into their map's readings.
        ///
        /// Everything here is already being paid for elsewhere on the frame: the condition comes from the
        /// cache the Condition column reads, and the rest are live properties vanilla keeps behind its own
        /// dirty flags. Nothing is walked twice to fill the header and the rail.
        /// </summary>
        private static void Tally(PawnMapRail.MapTally tally, Pawn pawn)
        {
            tally.People++;

            int slot = System.Array.IndexOf(PawnCategories.All, PawnCategories.Of(pawn));

            if (slot >= 0)
                tally.ByCategory[slot]++;

            PawnHealthSummary summary = PawnAttributes.Condition.Get(pawn);

            if (summary.NeedsCare)
            {
                tally.NeedsCare++;
                tally.Urgent |= summary.Urgent;
            }

            // Guarded rather than assumed: this tab lists animals when a necromancer is raising them, and an
            // undead raccoon has no mood need to read.
            Need_Mood mood = pawn.needs == null ? null : pawn.needs.mood;

            if (mood != null)
            {
                tally.MoodTotal += mood.CurLevelPercentage;
                tally.MoodCount++;
            }

            if (pawn.mindState != null && pawn.mindState.IsIdle)
                tally.Idle++;
        }

        private static int CategoriesIn(List<Pawn> group)
        {
            int found = 0;

            foreach (PawnCategory category in PawnCategories.All)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    if (PawnCategories.Of(group[i]) != category)
                        continue;

                    found++;

                    break;
                }
            }

            return found;
        }

        /// <summary>
        /// Fills in a subgroup heading's count once its members have been laid out.
        ///
        /// The heading is written before the run it heads, so its count is not known yet; the row is found again
        /// by its fold key and finished. Cheaper than walking the group twice per category, and the control reads
        /// the row object it was handed rather than a copy of it, so writing to it afterwards is enough.
        /// </summary>
        private static void Heading(string key, int members)
        {
            for (int i = Grid.Rows.Count - 1; i >= 0; i--)
            {
                UIDesignatorTabRow row = Grid.Rows[i];

                if (!row.IsSection || row.SectionKey != key)
                    continue;

                row.SectionSuffix = members == 1 ? "1 person" : members + " people";

                return;
            }
        }

        private static void Collect()
        {
            Grid.Rows.Clear();
            Roster.Clear();
            PawnMapRail.Tallies.Clear();

            hoveredPawn = null;

            // A match inside a folded group would otherwise not be shown, which reads as the search failing. The
            // folds themselves are remembered and come back when the search is cleared.
            Grid.SuppressCollapse = !Search.IsEmpty;

            List<Map> maps = Find.Maps;
            if (maps == null)
                return;

            PawnMapRail.Validate(maps);

            List<Pawn> group = new List<Pawn>();

            foreach (Map map in maps)
            {
                group.Clear();
                Candidates(map);

                PawnMapRail.MapTally tally = new PawnMapRail.MapTally
                {
                    Id = map.uniqueID,
                    Label = MapLabels.NameOf(map)
                };

                foreach (Pawn pawn in Considered)
                {
                    // Every pawn the tab could list joins the roster, matching or not. The roster answers "is
                    // this pawn still here", which is what decides whether an open pane still has a subject --
                    // and a pawn filtered out of view has not gone anywhere. Building it from the matches
                    // instead would close the pane the moment you searched for somebody else.
                    Roster.Add(pawn);

                    Tally(tally, pawn);

                    if (PawnCategories.Shown(PawnCategories.Of(pawn)) && PawnSearch.Matches(Search, pawn))
                        group.Add(pawn);
                }

                // Added whether or not it contributes rows, so the rail keeps a map you have filtered
                // everybody out of. A map you cannot get back to is worse than an empty table.
                PawnMapRail.Tallies.Add(tally);

                if (!PawnMapRail.Shows(map))
                    continue;

                if (group.Count == 0)
                    continue;

                // Sorted once, here, rather than once per category below. Splitting a sorted list by category
                // keeps each part sorted, so the alphabetical order inside every subgroup is free.
                group.SortBy(p => p.LabelShortCap);

                string fold = "map:" + map.uniqueID;

                // Only while every map is on screen at once. With one map picked in the rail, or with only one
                // map to pick, this heading is the same words above every row below it -- and the rail entry
                // or the header's subtitle has already said them.
                if (PawnMapRail.WantsHeadings)
                {
                    Grid.Rows.Add(new UIDesignatorTabRow
                    {
                        SectionLabel = tally.Label,
                        SectionSuffix = group.Count == 1 ? "1 person" : group.Count + " people",

                        // Keyed by the map rather than by its name, so two maps that happen to read alike fold
                        // separately and renaming one does not lose its fold.
                        SectionKey = fold
                    });
                }

                // A subgroup heading is worth its line only when there is something to tell apart. A colony of
                // nothing but colonists would otherwise get a "Colonists" bar under every map heading, saying
                // exactly what the tab across the top already says.
                //
                // Except with the map heading gone: then this is the only heading there is, and dropping it too
                // would leave the table with nothing saying what it holds.
                bool split = CategoriesIn(group) > 1 || !PawnMapRail.WantsHeadings;

                foreach (PawnCategory category in PawnCategories.All)
                {
                    int members = 0;

                    for (int i = 0; i < group.Count; i++)
                    {
                        if (PawnCategories.Of(group[i]) != category)
                            continue;

                        if (members == 0 && split)
                            Grid.Rows.Add(new UIDesignatorTabRow
                            {
                                SectionLabel = PawnCategories.Label(category),

                                // Indented only while there is a map heading above it to be indented under.
                                SectionDepth = PawnMapRail.WantsHeadings ? 1 : 0,
                                SectionKey = fold + "/" + category
                            });

                        members++;

                        Pawn pawn = group[i];
                        bool open = expandedPawn == pawn;

                        Grid.Rows.Add(new UIDesignatorTabRow
                        {
                            Payload = pawn,
                            Height = open ? RowHeight + ExpansionHeightFor(pawn) : (float?) null,
                            DrawBackground = DrawRowBackground,
                            DrawOverlay = open ? (System.Action<Rect, UIDesignatorTabRow, UIColorPaletteDef>)
                                DrawExpansion : null
                        });
                    }

                    // Written after the members are counted, since the heading goes in before the count is
                    // known and the control reads the row it was given rather than a copy.
                    if (members > 0 && split)
                        Heading(fold + "/" + category, members);
                }
            }

            // An open row for a pawn who is no longer listed would be a fold nobody can reach to close: they have
            // joined a caravan, been captured, or left with a transport pod. Not a destroyed pawn, which
            // PawnLifecycle handles directly; this is the milder case of a colonist who still exists.
            if (expandedPawn != null && !Roster.Contains(expandedPawn))
                expandedPawn = null;

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

            // Recorded here because a cell only ever sees its own rect, and the tools cell needs to know
            // whether the whole row is under the pointer. Safe to read from a cell: the control draws a row's
            // background before its cells, so by the time the tools are drawn this frame their own row has
            // already had its say. Cleared at the top of Collect.
            if (Mouse.IsOver(band))
                hoveredPawn = pawn;

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
        /// The row's hit target: the band between the portrait and the area column, both of which own their clicks.
        ///
        /// A single rect between the two rather than the band with two holes in it. What is given up is the arrow
        /// and the margins around it, and the arrow has its own hit target covering that -- so between them, the
        /// whole band toggles except the face and the area button.
        ///
        /// <b>The area column was swallowed until 2026-08-22.</b> Reported by Aaron: its dropdown opened and shut
        /// the row instead of the area menu. Same cause as the portrait before it -- this background is drawn
        /// before any cell, so its ButtonInvisible is the first one under the cursor and takes the click -- and the
        /// same fix, which is to cut the cell out by geometry rather than to hope for a draw order.
        ///
        /// <b>Measured from the columns, not from the band.</b> The band is as wide as the *window*: the control
        /// lays rows out across <c>Mathf.Max(ColumnsWidth, available)</c>, so on a tab dragged wider than its
        /// columns the band has slack on the right and <c>band.xMax - AreaColumnWidth</c> would cut a strip of
        /// empty space while leaving the real column live. The columns' own width is where the column is.
        ///
        /// <b>And the columns say which ones to cut, which they did not until 2026-08-23.</b> This subtracted
        /// <c>AreaColumnWidth</c> by name -- correct while the area was the last column, and wrong from the moment
        /// the Edit column was added after it, because the subtraction then stopped at Edit's left edge and let
        /// the row swallow the area dropdown again. Exactly the bug Aaron had already reported once. The columns
        /// now declare <c>OwnsClicks</c> and the width comes off them, so adding a column cannot bring it back.
        /// </summary>
        private static Rect RowClickZone(Rect band)
        {
            float left = PortraitFrame(band).xMax + PortraitInset;
            float right = Mathf.Min(band.xMax, band.x + Grid.ColumnsWidth - Grid.OwnedTailWidth);

            return new Rect(left, band.y, Mathf.Max(0f, right - left), band.height);
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
            expandedPawn = expandedPawn == pawn ? null : pawn;

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

                // Healthy goes quiet, and only here. Healthy is the default state of a colonist, so a green
                // one on every row is a solid band of colour for the exception to fail to stand out against
                // -- the column is scanned precisely when something has gone wrong. Everything abnormal keeps
                // its state colour, which now has an empty column to stand out in.
                //
                // Local rather than in Summary.Color, which the hospital panel and the inspect pane also read.
                // The argument is about a column of mostly-healthy people; neither of those is one.
                GUI.color = summary.State == PawnHealthState.Healthy
                    ? palette.TextSecondary
                    : summary.Color(palette);

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
                Blank(band, null, palette);
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
        /// The allowed area, as a button carrying the area's own colour.
        ///
        /// <b>Drawn like the policy pickers on the expanded row,</b> because it is the same kind of control: a
        /// per-pawn choice out of a list the player manages. Sharing their look is what stops the tab having two
        /// unrelated ways of saying "press this to choose".
        ///
        /// <b>The chip is the area's colour, not decoration.</b> It is the colour that area is outlined in on the
        /// map, and hovering an entry in the menu outlines it there, so the chip is how a name in this column and
        /// a region on the map are the same thing.
        /// </summary>
        private static void DrawAreaCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;
            Rect band = TopBand(cell);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;

            if (!PawnAreas.Assignable(pawn))
            {
                Blank(band, PawnAreas.Reason(pawn), palette);
            }
            else
            {
                Rect button = new Rect(band.x + 4f, band.center.y - 11f, Mathf.Max(0f, band.width - 8f), 22f);
                bool over = Mouse.IsOver(button);

                UIElementPainter.PaintButton(button, palette, over, over && Input.GetMouseButton(0));

                Area area = PawnAreas.Current(pawn);
                Rect chip = new Rect(button.x + 6f, button.center.y - 6f, 12f, 12f);

                GUI.DrawTexture(chip, area != null ? area.ColorTexture : BaseContent.GreyTex);

                GUI.color = palette.Border;
                Widgets.DrawBox(chip, 1);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                // The caret's room comes out of the label rather than being drawn over it, so a long area name
                // ends in an ellipsis instead of running underneath the arrow.
                Rect label = new Rect(chip.xMax + 5f, button.y,
                    Mathf.Max(0f, button.xMax - chip.xMax - 24f), button.height);

                if (label.width >= 20f)
                    Widgets.LabelEllipses(label, PawnAreas.Label(pawn));

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(button.x, button.y, Mathf.Max(0f, button.width - 6f), button.height), "▾");

                if (Widgets.ButtonInvisible(button))
                    PawnAreas.Choose(pawn);
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// What a cell says when the setting in it does not apply to this pawn, and why on hover.
        ///
        /// <b>A dash rather than an empty cell,</b> because a blank reads as a fault while a dash reads as an
        /// absence -- and the reason is on hover rather than in the cell, since a sentence would need a column
        /// four times this wide to serve the one row in twenty that needs it.
        ///
        /// Shared by the four columns that can be inapplicable: the schedule, the area, the attack mode and
        /// self-tend. Four copies of six lines is how the dash would end up a different grey in one of them.
        /// </summary>
        private static void Blank(Rect band, string reason, UIColorPaletteDef palette)
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

            if (!reason.NullOrEmpty() && Mouse.IsOver(band))
                TooltipHandler.TipRegion(band, (TipSignal) reason);
        }

        /// <summary>
        /// Attack mode, as three segments carrying RimWorld's own icons.
        ///
        /// <b>Segments rather than the dropdown vanilla uses,</b> which is the standing preference: three modes is
        /// exactly the case a segmented control is for, every state is reachable in one click instead of one to
        /// open and one to choose, and the filled segment says which mode is on without being touched. Down a
        /// list of twenty colonists that last part is the whole reason the column is worth its width.
        ///
        /// <b>The icons are RimWorld's,</b> so the picture here and the picture in the inspect pane's corner are
        /// the same picture. Nothing about which mode means what is decided here.
        /// </summary>
        private static void DrawResponseCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = data.Payload as Pawn;

            if (pawn == null)
                return;

            Rect band = TopBand(cell);

            if (!PawnCombat.Respondable(pawn))
            {
                Blank(band, PawnCombat.ResponseReason(pawn), palette);

                return;
            }

            float width = TabParts.IconSegmentsWidth(PawnCombat.Modes.Length, SegmentHeight);
            float x = band.center.x - width * 0.5f;
            float y = band.center.y - SegmentHeight * 0.5f;

            HostilityResponseMode current = PawnCombat.Response(pawn);
            bool canAttack = PawnCombat.CanAttack(pawn);

            for (int i = 0; i < PawnCombat.Modes.Length; i++)
            {
                // Declared inside the loop so each segment's callback closes over its own mode. A variable
                // hoisted out would leave all three segments setting whichever mode the loop ended on.
                HostilityResponseMode mode = PawnCombat.Modes[i];

                TabParts.Segment(new Rect(x, y, SegmentHeight, SegmentHeight), PawnCombat.Icon(mode),
                    mode == current, palette, () => PawnCombat.SetResponse(pawn, mode),
                    PawnCombat.ResponseTooltip(pawn, mode),
                    mode == HostilityResponseMode.Attack && !canAttack);

                x += SegmentHeight + TabParts.SegmentGap;
            }
        }

        /// <summary>
        /// Whether this colonist patches themselves up, as a switch.
        ///
        /// <b>The switch is not the state.</b> It is drawn from the pawn every frame and the write can be refused
        /// -- turning it on for somebody incapable of Doctor work is refused by vanilla with a message -- so a
        /// switch that flicks back is the game saying no rather than the control failing. Holding a local copy of
        /// the value would be what makes that look like a bug.
        /// </summary>
        private static void DrawSelfTendCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = data.Payload as Pawn;

            if (pawn == null)
                return;

            Rect band = TopBand(cell);

            if (!PawnCombat.SelfTendable(pawn))
            {
                Blank(band, PawnCombat.SelfTendReason(pawn), palette);

                return;
            }

            bool on = PawnCombat.SelfTends(pawn);

            // The switch alone, centred: a label here would repeat the heading in every row.
            if (UICheckboxControl.Draw(new Rect(band.x, band.center.y - SwitchRowHeight * 0.5f, band.width,
                    SwitchRowHeight), ref on, palette, null, PawnCombat.SelfTendTooltip(pawn)))
                PawnCombat.SetSelfTend(pawn, on);
        }

        /// <summary>The hit target around a bare switch: tall enough to be easy, short of the row's own height.</summary>
        private const float SwitchRowHeight = 26f;

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
        // The expanded row: schedule, then policies
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Everything revealed under an opened row: the day, then the standing orders.
        ///
        /// Drawn as an overlay rather than as columns because both span the whole grid. 24 hours will not fit in
        /// any one column, and splitting either across columns would tie its layout to the column widths.
        ///
        /// <b>The two are laid out here and drawn elsewhere.</b> This owns only where each band sits under the
        /// row; what goes in them belongs to <see cref="ScheduleStrip"/> and <see cref="PolicyStrip"/>, both of
        /// which are also used away from this tab.
        /// </summary>
        private static void DrawExpansion(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = data.Payload as Pawn;

            if (pawn == null)
                return;

            // Laid out across the columns rather than across the row, which is wider: the control gives a row the
            // whole window when the tab has been dragged past its columns, and a work grid that used that width
            // would run out under the empty space to the right of the table.
            Rect area = new Rect(row.x + RowCard.AccentWidth + 8f, row.y + RowHeight, ExpansionWidth(),
                ExpansionHeightFor(pawn));

            float y = area.y;
            float schedule = ScheduleHeightFor(pawn);

            // Each band is offset by what the one above actually took, not by a constant. A pawn with no timetable
            // still has policies, and an arrangement that assumed the schedule was always there would leave them
            // floating below a gap.
            if (schedule > 0f)
            {
                DrawScheduleStrip(new Rect(area.x, y, area.width, schedule), pawn, palette);

                y += schedule + BandGap;
            }

            float policies = PolicyStrip.HeightFor(pawn);

            if (policies > 0f)
            {
                // Narrowed by the toolbar before the chips are laid out. PolicyStrip drops the chips that do not
                // fit rather than squeezing them, so drawing the tools over the full width would have silently
                // cost the player the last policy on the line.
                float tools = PawnTools.WidthFor(PawnTemplateScope.Policies);

                PolicyStrip.Draw(new Rect(area.x, y, area.width - tools - 8f, policies), pawn, palette);

                PawnTools.Row(new Rect(area.xMax - tools, y, tools, policies), pawn,
                    PawnTemplateScope.Policies, palette);

                y += policies + BandGap;
            }

            float work = PawnWorkGrid.HeightFor(pawn, area.width);

            if (work > 0f)
                PawnWorkGrid.Draw(new Rect(area.x, y, area.width, work), pawn, palette);
        }

        /// <summary>
        /// The hour-by-hour schedule for one pawn.
        ///
        /// The strip itself, the brush picker and the painting all live in <see cref="ScheduleStrip"/>, because the
        /// template manager edits a day the same way and two copies of a paintable strip would be two copies that
        /// could drift apart. What stays here is the part that is about a pawn's row: which pawn it reads, and
        /// that painting invalidates that pawn's cached readings.
        /// </summary>
        private static void DrawScheduleStrip(Rect strip, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.timetable == null)
                return;

            const float gap = 8f;

            Rect picker = new Rect(strip.x, strip.y + 6f, ScheduleStrip.BrushWidth, ScheduleStrip.CellHeight);
            ScheduleStrip.DrawBrushPicker(picker, palette);

            // The same tools the work grid carries, scoped to the schedule. Taken off the right before the hours
            // are laid out rather than drawn over them: twenty four cells divided into whatever is left is fine,
            // and twenty four cells with a toolbar on top of the last two is not.
            float tools = PawnTools.WidthFor(PawnTemplateScope.Schedule);

            PawnTools.Row(new Rect(strip.xMax - tools, strip.y + 6f, tools, ScheduleStrip.CellHeight), pawn,
                PawnTemplateScope.Schedule, palette);

            Rect hours = new Rect(picker.xMax + gap, strip.y + 6f,
                strip.xMax - picker.xMax - gap - tools - gap, ScheduleStrip.CellHeight);

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
