using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Work
{
    /// <summary>
    /// The work tab, redrawn: a card per pawn, grouped by where they are, with a priority box and a skill
    /// readout per work type.
    ///
    /// Vanilla's version is a PawnTable -- a column-worker framework built around one map's colonists and a
    /// 1-4 priority. Neither assumption survives what this needs, so the table is replaced rather than
    /// extended: priorities run 0-9 (see <see cref="WorkPriorityRange"/>), pawns come from every map at once,
    /// and each cell carries the pawn's skill for that work as well as the number.
    ///
    /// The grid itself is <see cref="UIDesignatorTabControl"/>. Everything that is true of any tab of this shape
    /// lives there -- the pinned heading, the leaning titles and their bands, the section headings, the column
    /// banding, the scroll view, the layer order. What is left here is what only the work tab knows: which pawns
    /// there are, what a cell contains, and what a row's status means.
    ///
    /// That split is why the file is shorter than the tab is. If something about the *shape* of the grid looks
    /// wrong, it is in the control and every tab built on it shares the fix.
    /// </summary>
    public static class WorkPanel
    {
        /// <summary>
        /// As wide as the grid needs, up to the screen.
        ///
        /// Derived rather than a fixed cap. The point of a column per work type is comparing them at a glance,
        /// and a fixed width meant scrolling sideways to reach the last few -- which defeats it, and which
        /// also changed with every mod that adds a work type. Anything that still does not fit, on a small
        /// screen or with many mods, scrolls as before.
        /// </summary>
        public static float WindowWidth
        {
            get
            {
                EnsureColumns();
                return Mathf.Min(Grid.RequestedWidth + WindowChrome, UI.screenWidth - 16f);
            }
        }

        public static float WindowHeight => Mathf.Min(760f, UI.screenHeight * 0.8f);

        /// <summary>
        /// What the window loses before <see cref="Draw"/> gets to lay anything out: the 6px margin
        /// MainTabWindow_PawnTable subtracts on each side, and the 6px this panel contracts by on each side.
        /// </summary>
        private const float WindowChrome = 24f;

        private const float PawnColumnWidth = 210f;

        /// <summary>
        /// The edit tools column: clear, copy and paste on the top row, save and apply a template beneath.
        ///
        /// Immediately after the name and before the grid, because all five act on the whole row. Past the
        /// first work column they would read as belonging to whichever column they landed beside.
        ///
        /// Two rows of three rather than one row of five, which would have cost another 60px of a window that
        /// is already as wide as the grid needs. The split is also the grouping: the top row edits this pawn's
        /// numbers, the bottom row deals in saved templates.
        /// </summary>
        private const float ToolsColumnWidth = 98f;

        private const float ToolButtonSize = 26f;
        private const float ToolButtonGap = 4f;

        /// <summary>
        /// Width of one work type's column.
        ///
        /// No longer set by the title, which now runs diagonally and can be any length. What is left setting it
        /// is the skill readout under each box -- "skill 12" at <c>Tiny</c> -- since the box itself is only
        /// 26px. Narrowing past that would clip the number, which is the one thing in the cell that has to be
        /// read rather than recognized.
        /// </summary>
        private const float WorkColumnWidth = 44f;

        private const float PriorityBoxSize = 26f;

        /// <summary>
        /// Space between a priority box and the skill readout under it.
        ///
        /// 2px put the text's ascenders against the bottom of the box. There is room to spare in a 62px row,
        /// and a number touching the edge of a box reads as a rendering fault rather than as two separate
        /// things. <see cref="SkillLabelRect"/> is what keeps the two apart; this is only how far apart.
        /// </summary>
        private const float SkillLabelGap = 6f;

        private static readonly UICardControl RowCard = new UICardControl { Padding = 0f, AccentWidth = 3f };

        /// <summary>
        /// The grid itself, which owns everything this class used to do by hand: the layout, the scroll view,
        /// the pinned heading with its leaning titles and their bands, the section headings, the column banding,
        /// and the order the layers are painted in.
        ///
        /// What is left here is what only the work tab knows -- what a cell contains, what a row's status is,
        /// and which pawns there are.
        /// </summary>
        private static readonly UIDesignatorTabControl Grid = new UIDesignatorTabControl
        {
            HeaderLabelOrientation = UIHeaderAngle.Diagonal,
            RowHeight = 62f,
            RowGap = 2f,
            SectionHeaderHeight = 30f
        };

        /// <summary>
        /// Builds the columns, once. Rebuilt if the work type list changes under us, which happens when the mod
        /// list changes rather than during play.
        /// </summary>
        private static void EnsureColumns()
        {
            if (Grid.Columns.Count == WorkTypes.Count + 2)
                return;

            Grid.Columns.Clear();

            // Neither of the first two is part of the repeating grid, so neither is banded or leaned: a name and
            // a row of buttons are not a column of like values, and both have room for a level heading.
            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Width = PawnColumnWidth,
                Bandable = false,
                RotateLabel = false,
                DrawHeader = DrawManualToggleHeader,
                DrawCell = (cell, row, palette) => DrawPawnCell(cell, (Pawn) row.Payload, palette)
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Edit tools",
                Width = ToolsColumnWidth,
                Bandable = false,
                RotateLabel = false,
                DrawCell = (cell, row, palette) => DrawToolsCell(cell, (Pawn) row.Payload, palette)
            });

            for (int i = 0; i < WorkTypes.Count; i++)
            {
                // Captured per column rather than looked up per cell: the closure is built once here, not once
                // per row per frame.
                WorkTypeDef work = WorkTypes[i];
                int index = i;

                Grid.Columns.Add(new UIDesignatorTabColumn
                {
                    Label = ColumnLabel(work),
                    Width = WorkColumnWidth,
                    Tooltip = work.gerundLabel.CapitalizeFirst() + "\n\n" + work.description,
                    DrawCell = (cell, row, palette) =>
                    {
                        Pawn pawn = (Pawn) row.Payload;
                        DrawWorkCell(cell, pawn, work, IsDisabled(pawn, index), palette);
                    }
                });
            }
        }

        /// <summary>Work types in the order vanilla shows them, cached because the sort is stable.</summary>
        private static List<WorkTypeDef> workTypes;

        /// <summary>
        /// The same list the grid's columns come from, for anything that has to line up with them --
        /// <see cref="Dialog_WorkTemplates"/> lists a template's contents in this order so a template reads in
        /// the same sequence as the row it was captured from.
        /// </summary>
        internal static List<WorkTypeDef> VisibleWorkTypes => WorkTypes;

        private static List<WorkTypeDef> WorkTypes
        {
            get
            {
                if (workTypes != null)
                    return workTypes;

                workTypes = new List<WorkTypeDef>();
                foreach (WorkTypeDef def in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if (def.visible)
                        workTypes.Add(def);
                }

                // naturalPriority descending is vanilla's own column order, so the columns land where a player
                // already expects them rather than in def-load order.
                workTypes.SortByDescending(def => def.naturalPriority);
                return workTypes;
            }
        }

        /// <summary>
        /// Every pawn in the grid, flat and in the order the rows are in.
        ///
        /// Kept beside the rows because three things -- the manual-priorities toggle and the snapshot it takes
        /// either side of itself -- act on every pawn at once and have no interest in which map they are on.
        /// </summary>
        private static readonly List<Pawn> Pawns = new List<Pawn>();

        public static void Draw(Rect inRect)
        {
            // The upstream half of the search field's focus diagnostics, and it has to be the very first thing
            // here: it measures how many control ids exist before this panel draws anything, which is what
            // separates "something outside us shifted the ids" from "we shifted them ourselves".
            //
            // This is also effectively the start of the window's contents, since our prefix replaces
            // MainTabWindow_Work.DoWindowContents outright rather than running alongside it.
            //
            // Gated on the launch-latched flag rather than on UIDebug.Enabled, because allocating a control id
            // only while a setting is on would make toggling that setting a draw-order shift -- the very fault
            // this measures. Off, it costs one bool test.
            if (UIDebug.InstrumentControlIds)
                UITextBoxControl.DiagnosticUpstreamSentinel = GUIUtility.GetControlID(FocusType.Passive);

            EnsureColumns();
            Collect();

            // The numbers most likely to explain a shift that turns out to originate inside this panel:
            // filtering changes the row count, and the row count changes whether the grid needs a scrollbar.
            // A lambda, so nothing is formatted unless a report actually fires.
            UITextBoxControl.DiagnosticContext = () => $"rows={Grid.Rows.Count}, columns={Grid.Columns.Count}, "
                                                      + $"scrollY={Grid.Scroll.y:F0}, search=\"{Search.Text}\"";

            Grid.Draw(inRect.ContractedBy(6f), UIColorPaletteDef.Active);

            // After the grid, so the scroll view it was clicked in has been closed out.
            PawnCameraJump.Resolve();
        }

        // ---------------------------------------------------------------------------------------
        // Pawns
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Every player-controlled pawn that can be given work, on every map, grouped by map.
        ///
        /// Walked from Find.Maps rather than through PawnsFinder's all-maps helpers because the grouping
        /// needs to know which map each pawn came from, and a flat list would have to be re-bucketed by
        /// reading pawn.Map back off every entry.
        /// </summary>
        private static void Collect()
        {
            Grid.Rows.Clear();
            Pawns.Clear();

            // A match inside a folded group would otherwise not be shown, which reads as the search failing. The
            // folds themselves are remembered and come back when the search is cleared.
            Grid.SuppressCollapse = !Search.IsEmpty;

            List<Map> maps = Find.Maps;
            if (maps == null)
                return;

            List<Pawn> colonists = new List<Pawn>();

            foreach (Map map in maps)
            {
                colonists.Clear();

                foreach (Pawn pawn in map.mapPawns.FreeColonists)
                {
                    // A pawn with no work settings cannot be assigned anything -- newborns, and some
                    // mod-added colonists -- and a row of empty boxes for them is just noise.
                    if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                        continue;

                    if (Matches(pawn))
                        colonists.Add(pawn);
                }

                if (colonists.Count == 0)
                    continue;

                colonists.SortBy(p => p.LabelShortCap);

                // A section heading is a row like any other, in place in the same list, which is what lets the
                // grid lay the whole thing out in one pass.
                Grid.Rows.Add(new UIDesignatorTabRow
                {
                    SectionLabel = MapLabels.NameOf(map),
                    SectionSuffix = colonists.Count == 1 ? "1 colonist" : colonists.Count + " colonists"
                });

                foreach (Pawn pawn in colonists)
                {
                    Grid.Rows.Add(new UIDesignatorTabRow
                    {
                        Payload = pawn,
                        DrawBackground = DrawRowBackground
                    });

                    Pawns.Add(pawn);
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        // ---------------------------------------------------------------------------------------
        // Manual priorities
        //
        // Vanilla's Notify_UseWorkPrioritiesChanged normalizes every non-zero priority to the default when the
        // mode is switched, which is what destroys a carefully tuned grid the moment someone toggles the box
        // to see what it does. The numbers are copied out before the switch and put back after switching
        // returns, so the round trip is lossless.
        //
        // Held for the session rather than in the save: this exists for the accidental click, which is
        // resolved in seconds. Saving while switched off still loses the detail, and making that lossless
        // needs a GameComponent.
        // ---------------------------------------------------------------------------------------

        private static readonly Dictionary<Pawn, int[]> Remembered = new Dictionary<Pawn, int[]>();

        private static void Remember()
        {
            Remembered.Clear();

            foreach (Pawn pawn in Pawns)
            {
                int[] saved = new int[WorkTypes.Count];
                for (int i = 0; i < WorkTypes.Count; i++)
                    saved[i] = pawn.workSettings.GetPriority(WorkTypes[i]);

                Remembered[pawn] = saved;
            }
        }

        private static void Restore()
        {
            foreach (Pawn pawn in Pawns)
            {
                if (!Remembered.TryGetValue(pawn, out int[] saved))
                    continue;

                for (int i = 0; i < WorkTypes.Count && i < saved.Length; i++)
                {
                    WorkTypeDef work = WorkTypes[i];

                    // A work type the pawn has since lost -- an injury, a lost ideoligion role -- cannot be
                    // assigned, and SetPriority logs an error rather than ignoring it.
                    if (!pawn.WorkTypeIsDisabled(work))
                        pawn.workSettings.SetPriority(work, saved[i]);
                }
            }
        }

        /// <summary>
        /// The name column's heading: a search field over the manual-priorities toggle.
        ///
        /// Both belong here for the same reason -- each governs the whole grid rather than one column, and this is
        /// the one heading cell wide enough to hold a control. The toggle sits at the bottom, level with where the
        /// leaning titles start; the search field takes the space above it, which was empty.
        /// </summary>
        private static void DrawManualToggleHeader(Rect cell, UIColorPaletteDef palette)
        {
            DrawSearchField(new Rect(cell.x + 6f, cell.y + 8f, cell.width - 12f, 26f), palette);
            DrawManualToggle(new Rect(cell.x + 6f, cell.yMax - 34f, cell.width - 12f, 28f), palette);
        }

        // ---------------------------------------------------------------------------------------
        // Search
        //
        // Filters which pawns get rows at all, rather than dimming the ones that do not match. A work tab is read
        // by comparing rows against each other, and a list with gaps in it is harder to compare than a short list.
        //
        // Matched on first name, nickname and last name -- and on nothing else. See Matches.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Our own text box, not vanilla's <c>QuickSearchWidget</c>.
        ///
        /// The widget was used here first, for one good reason: only a <c>QuickSearchWidget</c> reachable through
        /// a window's <c>CommonSearchWidget</c> is visible to <c>WindowStack.AnySearchWidgetFocused</c>, the gate
        /// that stops every key binding in the game -- camera dolly included -- from firing while a search field
        /// has focus. A field of our own let W and A pan the map as we typed.
        ///
        /// What it did not fix was focus itself. <c>GUI.SetNextControlName</c> does not make a control's identity
        /// independent of draw order, which is what I had assumed: Unity keys focus on an integer id derived from
        /// draw order and only hangs the name on it, so an id shift still drops focus mid-word and vanilla's
        /// widget has no defense against that. <see cref="UITextBoxControl"/> repairs focus by name instead, and
        /// <c>Patch_WindowStack_AnySearchWidgetFocused</c> gives it the same key-binding protection the vanilla
        /// widget got -- for every text box of ours, rather than for this one window.
        /// </summary>
        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search",
            Icon = TexButton.Search,
            MaxLength = 30
        };

        /// <summary>
        /// Whether a pawn survives the search, matched on name alone.
        ///
        /// Read off <c>Name</c> rather than any of the label properties, because a pawn's label is not its
        /// name. <c>Pawn.LabelNoCount</c> is the name followed by the backstory title -- "Maxwell, Sailor" --
        /// and the title half is run through <c>Colorize</c>, so the string also carries <c>&lt;color=#...&gt;</c>
        /// markup. Filtering on it matched a colonist's profession, which is how searching "sa" turned up a
        /// sailor named Maxwell alongside Sam, and would equally have matched "col" against every titled pawn
        /// in the colony.
        ///
        /// First, nick and last are each tested separately rather than against the assembled full name, so a
        /// search cannot match across the join between two of them.
        /// </summary>
        private static bool Matches(Pawn pawn)
        {
            if (Search.IsEmpty)
                return true;

            if (pawn.Name is NameTriple triple)
            {
                return Search.Matches(triple.First)
                       || Search.Matches(triple.Nick)
                       || Search.Matches(triple.Last);
            }

            if (pawn.Name is NameSingle single)
                return Search.Matches(single.Name);

            // A Name subclass from a mod, or no name at all. ToStringShort is the nearest thing to a bare
            // name that every Name is required to have; LabelShortCap covers a pawn with no Name, where it
            // falls through to the kind label rather than dereferencing null.
            return pawn.Name != null
                ? Search.Matches(pawn.Name.ToStringShort)
                : Search.Matches(pawn.LabelShortCap);
        }

        /// <summary>
        /// The search field. All of the chrome, the focus handling and the clear button belong to the control.
        /// </summary>
        private static void DrawSearchField(Rect rect, UIColorPaletteDef palette)
        {
            // Scroll reset on change: filtering can leave the view scrolled past everything that still matches,
            // which reads as the search finding nothing.
            if (Search.Draw(rect, palette))
                Grid.Scroll = Vector2.zero;
        }

        private static void DrawManualToggle(Rect rect, UIColorPaletteDef palette)
        {
            bool manual = Find.PlaySettings.useWorkPriorities;
            bool was = manual;

            if (!UICheckboxControl.Draw(rect, ref manual, palette, "Manual priorities",
                    "Off assigns work as on or off. On lets each job carry a priority from 1 to "
                    + WorkPriorityRange.Lowest + ", done lowest number first.\n\nSwitching this off keeps "
                    + "your priorities so turning it back on restores them."))
                return;

            // Copied out before the switch, put back after: the switch itself is what flattens them.
            if (was && !manual)
                Remember();

            Find.PlaySettings.useWorkPriorities = manual;

            foreach (Pawn pawn in Pawns)
                pawn.workSettings.Notify_UseWorkPrioritiesChanged();

            if (!was && manual)
                Restore();
        }

        /// <summary>
        /// A work type's name for a column title, capitalized per word: "Bed Rest", not "bed rest".
        ///
        /// <c>labelShort</c> is the compact name the game keeps for exactly this purpose, and it is authored
        /// lowercase for use mid-sentence. A column heading is not mid-sentence.
        /// </summary>
        private static string ColumnLabel(WorkTypeDef work)
        {
            string source = work.labelShort.NullOrEmpty() ? work.gerundLabel : work.labelShort;
            if (source.NullOrEmpty())
                return work.defName;

            string[] words = source.Split(' ');
            for (int i = 0; i < words.Length; i++)
                words[i] = words[i].CapitalizeFirst();

            return string.Join(" ", words);
        }

        // ---------------------------------------------------------------------------------------
        // Row status
        //
        // The stripe means the same thing here as it does on a grow-zone plant card: grey for nothing to
        // report, warning for something to look at, danger for something wrong. It had been the pawn's
        // favorite color, which made a long list colorful and said nothing -- and a stripe that carries a
        // meaning everywhere else in the mod cannot be decorative in one tab.
        // ---------------------------------------------------------------------------------------

        /// <summary>Warning wash alpha, matching the plant cards' own notice washes.</summary>
        private const float WarningWashAlpha = 0.22f;

        private const float DangerWashAlpha = 0.24f;

        /// <summary>
        /// Which of one pawn's work types they cannot do, indexed against <see cref="WorkTypes"/>, and who it
        /// was filled for.
        ///
        /// Reused between frames and between pawns rather than allocated per row. WorkTypeIsDisabled walks the
        /// pawn's story, health and ideoligion, and both the row's status stripe and every cell in the row want
        /// the answer -- so it is asked once per work type per pawn instead of once per reader.
        ///
        /// The row's background is drawn before its cells, which is what makes one shared array safe: the
        /// background fills it for that pawn, and the cells that follow are that pawn's. <see cref="IsDisabled"/>
        /// refills it anyway if it is asked about someone else, so a change to the draw order costs correctness
        /// nothing.
        /// </summary>
        private static bool[] disabledWork = new bool[0];

        private static Pawn disabledFor;

        private static bool CacheDisabledWork(Pawn pawn)
        {
            if (disabledWork.Length < WorkTypes.Count)
                disabledWork = new bool[WorkTypes.Count];

            bool any = false;

            for (int i = 0; i < WorkTypes.Count; i++)
            {
                disabledWork[i] = pawn.WorkTypeIsDisabled(WorkTypes[i]);
                any |= disabledWork[i];
            }

            disabledFor = pawn;
            return any;
        }

        private static bool IsDisabled(Pawn pawn, int index)
        {
            if (disabledFor != pawn)
                CacheDisabledWork(pawn);

            return index < disabledWork.Length && disabledWork[index];
        }

        /// <summary>
        /// A row's chrome: the card, its status stripe, and the wash that goes with it.
        ///
        /// Drawn by the grid before it bands the columns and before any cell, so the layering is: card, status,
        /// banding, content.
        /// </summary>
        private static void DrawRowBackground(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            Pawn pawn = (Pawn) data.Payload;

            bool anyDisabled = CacheDisabledWork(pawn);

            // Incapacitated outranks missing work types: a downed pawn is not doing any of it regardless of
            // what they are capable of.
            bool downed = pawn.Downed;

            RowCard.AccentColor = downed ? palette.Danger
                : anyDisabled ? palette.Warning
                : palette.SurfaceRaised;

            RowCard.BackgroundColor = palette.PanelBackground;
            RowCard.DrawChrome(row, palette);

            // Inset past the accent bar, which is already this color at full strength and should not be washed
            // over with a diluted copy of itself.
            if (downed)
            {
                UIElementPainter.PaintStripeWash(
                    new Rect(row.x + RowCard.AccentWidth, row.y, row.width - RowCard.AccentWidth, row.height),
                    Wash(palette.Danger, DangerWashAlpha));
            }
        }

        private static Color Wash(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        private static void DrawPawnCell(Rect cell, Pawn pawn, UIColorPaletteDef palette)
        {
            Rect portrait = new Rect(cell.x + 8f, cell.y + (cell.height - PawnPortraitCell.Size) * 0.5f,
                PawnPortraitCell.Size, PawnPortraitCell.Size);

            bool overPortrait = PawnPortraitCell.IsOver(portrait);

            // The circular crop is done by painting over the corners rather than by clipping, so the color
            // behind has to be passed in. Plain card color for most rows; for a downed one the card is under a
            // stripe wash, and painting flat card color over that would leave four clean triangles around the
            // head. Half the wash alpha is that pattern's average, since the tile is half stripe and half gap --
            // and an average is indistinguishable from stripes across the 7px this covers, where re-drawing the
            // wash itself would put stripes over the face.
            PawnPortraitCell.Draw(portrait, pawn, palette, pawn.Downed
                ? Color.Lerp(palette.PanelBackground, palette.Danger, DangerWashAlpha * 0.5f)
                : palette.PanelBackground);

            float textX = portrait.xMax + 8f;
            float textWidth = cell.xMax - textX - 6f;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Anchor = TextAnchor.LowerLeft;
            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(textX, cell.y + 8f, textWidth, 22f), pawn.LabelShortCap);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(textX, cell.y + 32f, textWidth, 16f), Subtitle(pawn));

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Mouse.IsOver(cell))
            {
                // One tooltip for the cell rather than a second one registered on the portrait: TooltipHandler
                // shows every tip registered under the cursor, so two regions would stack two boxes for what is
                // one thing being pointed at.
                string tip = pawn.LabelCap;

                if (overPortrait)
                    tip += "\n\nClick to center the view on " + pawn.LabelShortCap + ".";

                TooltipHandler.TipRegion(cell, (TipSignal) tip);
            }
        }

        /// <summary>
        /// The three row-wide tools: clear every priority, save them as a template, apply a saved one.
        ///
        /// Icon buttons rather than a menu behind one button. All three are one click from the row they act on,
        /// and a menu would make the common case -- copying one pawn's arrangement onto the next -- two clicks
        /// deeper for no gain in clarity.
        /// </summary>
        private static void DrawToolsCell(Rect cell, Pawn pawn, UIColorPaletteDef palette)
        {
            const float step = ToolButtonSize + ToolButtonGap;

            float top = cell.y + (cell.height - (ToolButtonSize * 2f + ToolButtonGap)) * 0.5f;
            float bottom = top + step;
            float x = cell.x + 6f;

            // --- this pawn's numbers -----------------------------------------------------------

            if (ToolButton(new Rect(x, top, ToolButtonSize, ToolButtonSize), WorkToolIcons.Clear, "0", palette,
                    "Clear every work priority for " + pawn.LabelShortCap))
            {
                ConfirmClear(pawn);
            }

            if (ToolButton(new Rect(x + step, top, ToolButtonSize, ToolButtonSize), WorkToolIcons.Copy, "C",
                    palette, "Copy " + pawn.LabelShortCap + "'s priorities"))
            {
                Copy(pawn);
            }

            // Nothing to paste is a disabled button rather than a hidden one: a tool that appears once you
            // have used another tool is a tool nobody finds.
            if (ToolButton(new Rect(x + step * 2f, top, ToolButtonSize, ToolButtonSize), WorkToolIcons.Paste,
                    "P", palette, PasteTooltip(pawn), clipboard == null))
            {
                Paste(pawn);
            }

            // --- saved templates ---------------------------------------------------------------

            if (ToolButton(new Rect(x, bottom, ToolButtonSize, ToolButtonSize), WorkToolIcons.Save, "S",
                    palette, "Save " + pawn.LabelShortCap + "'s priorities as a template"))
            {
                WorkPriorityTemplate saved = WorkTemplateStore.CaptureFrom(pawn);
                Find.WindowStack.Add(new Dialog_WorkTemplates(null, saved));
            }

            if (ToolButton(new Rect(x + step, bottom, ToolButtonSize, ToolButtonSize), WorkToolIcons.Apply,
                    "A", palette, "Apply a saved template to " + pawn.LabelShortCap))
            {
                Find.WindowStack.Add(new Dialog_WorkTemplates(pawn));
            }
        }

        // ---------------------------------------------------------------------------------------
        // Copy and paste
        //
        // The clipboard is a WorkPriorityTemplate, which is not a coincidence: an unnamed set of priorities
        // lifted off one pawn to put on another is exactly what a template is, and reusing the type means
        // copy and paste inherit its handling of work a pawn cannot do rather than repeating it.
        //
        // Held for the session and never written to disk. A template is the deliberate, named, kept version;
        // this is the one you are using right now, and persisting it would blur the two.
        // ---------------------------------------------------------------------------------------

        private static WorkPriorityTemplate clipboard;

        private static void Copy(Pawn pawn)
        {
            // From() skips work this pawn cannot do, so their incapabilities are not copied as zeros onto
            // someone who is perfectly capable of the work.
            clipboard = WorkPriorityTemplate.From(pawn, pawn.LabelShortCap);

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            Messages.Message("Copied " + pawn.LabelShortCap + "'s work priorities.",
                MessageTypeDefOf.SilentInput, false);
        }

        private static void Paste(Pawn pawn)
        {
            if (clipboard == null)
                return;

            // ApplyTo() leaves work this pawn cannot do at 0 rather than erroring, which is the other half of
            // respecting capabilities: the copy ignored the source's, and this ignores the target's.
            int skipped = clipboard.ApplyTo(pawn);

            // The remembered snapshot described the old numbers, and would put them back the next time manual
            // priorities were switched off and on again.
            Remembered.Remove(pawn);

            SoundDefOf.Tick_High.PlayOneShotOnCamera();

            string message = "Pasted " + clipboard.name + "'s work priorities onto " + pawn.LabelShortCap + ".";
            if (skipped > 0)
            {
                message += " " + skipped + (skipped == 1 ? " work type was" : " work types were")
                                         + " left off; " + pawn.LabelShortCap + " cannot do "
                                         + (skipped == 1 ? "it." : "them.");
            }

            Messages.Message(message, MessageTypeDefOf.SilentInput, false);
        }

        private static string PasteTooltip(Pawn pawn)
        {
            if (clipboard == null)
                return "Nothing copied yet. Use the copy button on a colonist to pick up their priorities.";

            return "Paste " + clipboard.name + "'s priorities onto " + pawn.LabelShortCap
                   + ".\n\nWork " + pawn.LabelShortCap + " cannot do is left disabled.";
        }

        /// <summary>
        /// Clearing is confirmed, unlike everything else on the row.
        ///
        /// It is the one button here that destroys work the player did and cannot be undone -- the other two
        /// only read the row or write a template over it, and a template can be applied again. A pawn with
        /// nothing set has nothing to lose, so that case skips the question.
        /// </summary>
        private static void ConfirmClear(Pawn pawn)
        {
            List<WorkTypeDef> assigned = new List<WorkTypeDef>();

            foreach (WorkTypeDef work in WorkTypes)
            {
                if (!pawn.WorkTypeIsDisabled(work) && pawn.workSettings.GetPriority(work) > 0)
                    assigned.Add(work);
            }

            if (assigned.Count == 0)
                return;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Clear all " + assigned.Count + " work priorities for " + pawn.LabelShortCap + "?",
                () =>
                {
                    foreach (WorkTypeDef work in assigned)
                        pawn.workSettings.SetPriority(work, 0);

                    // The remembered snapshot described the old numbers; keeping it would put them back the
                    // next time manual priorities were switched off and on again.
                    Remembered.Remove(pawn);
                }, true));
        }

        /// <param name="disabled">
        /// Drawn dimmed and reports no clicks, but still consumes the press so it does not fall through to the
        /// card underneath. The tooltip is still registered, which is where a disabled button says why.
        /// </param>
        private static bool ToolButton(Rect rect, Texture2D icon, string fallbackGlyph,
            UIColorPaletteDef palette, string tooltip, bool disabled = false)
        {
            TooltipHandler.TipRegion(rect, (TipSignal) tooltip);

            bool over = !disabled && Mouse.IsOver(rect);
            UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));

            Color previousColor = GUI.color;

            if (icon != null)
            {
                GUI.color = disabled ? palette.TextDisabled
                    : over ? palette.TextPrimary
                    : palette.TextSecondary;

                GUI.DrawTexture(rect.ContractedBy(4f), icon, ScaleMode.ScaleToFit);
            }
            else
            {
                // A missing art file leaves a working button rather than an invisible one.
                TextAnchor previousAnchor = Text.Anchor;
                GameFont previousFont = Text.Font;

                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                GUI.color = over ? palette.TextPrimary : palette.TextSecondary;
                Widgets.Label(rect, fallbackGlyph);

                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
            }

            GUI.color = previousColor;

            return Widgets.ButtonInvisible(rect);
        }

        private static string Subtitle(Pawn pawn)
        {
            string gender = pawn.gender != Gender.None ? pawn.gender.GetLabel().CapitalizeFirst() : null;
            int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;

            return gender.NullOrEmpty() ? age + " years old" : gender + ", " + age;
        }

        /// <summary>
        /// One priority box, with the pawn's skill for that work under it.
        ///
        /// Left click raises the priority and right click lowers it, both wrapping, so any value from 0 to 9
        /// is reachable without a modifier key. Zero is drawn faded but stays live -- the box has to be
        /// clickable to get back out of zero.
        /// </summary>
        private static void DrawWorkCell(Rect cell, Pawn pawn, WorkTypeDef work, bool disabled,
            UIColorPaletteDef palette)
        {
            if (disabled)
            {
                DrawIncapable(cell, palette);
                return;
            }

            int priority = pawn.workSettings.GetPriority(work);

            Rect box = new Rect(cell.center.x - PriorityBoxSize * 0.5f, cell.y + 8f,
                PriorityBoxSize, PriorityBoxSize);

            // With manual priorities off the number means nothing -- the game treats any non-zero the same --
            // so a box showing one would be lying about what it controls. A checkbox says exactly what the
            // mode does.
            if (!Find.PlaySettings.useWorkPriorities)
            {
                DrawEnabledCheckbox(cell, box, pawn, work, priority, palette);
                return;
            }

            bool over = Mouse.IsOver(box);

            Widgets.DrawBoxSolid(box, priority == 0 ? palette.SurfaceSunken : palette.SurfaceRaised);

            if (over)
                Widgets.DrawBoxSolid(box, palette.HoverOverlay);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorOfPriority(priority, palette);
            Widgets.Label(box, priority.ToString());

            // Skill under the box, so a glance down a column shows who is actually good at the work rather
            // than only who has been told to do it.
            Text.Font = GameFont.Tiny;
            GUI.color = SkillColor(pawn, work, palette, out string skillLabel);
            Widgets.Label(SkillLabelRect(cell, box), skillLabel);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (over)
            {
                TooltipHandler.TipRegion(box, (TipSignal) (work.gerundLabel.CapitalizeFirst()
                                                           + "\n\nLeft click raises, right click lowers."));
            }

            if (Event.current.type == EventType.MouseDown && over)
            {
                int step = Event.current.button == 1 ? -1 : 1;
                int next = priority + step;

                // Wraps at both ends, so a full circuit is possible from either button.
                if (next > WorkPriorityRange.Lowest)
                    next = 0;
                else if (next < 0)
                    next = WorkPriorityRange.Lowest;

                pawn.workSettings.SetPriority(work, next);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Event.current.Use();
            }
        }

        /// <summary>
        /// The on/off form of a cell, used when manual priorities are switched off.
        ///
        /// Toggling on writes the default priority rather than 1, so switching manual priorities back on
        /// leaves the pawn at the middle of the range instead of at the most urgent value in it -- which is
        /// what vanilla does, and what a player who never used priorities would expect.
        ///
        /// The skill readout stays: it is as useful for deciding whether to enable work at all as it is for
        /// ranking it.
        /// </summary>
        private static void DrawEnabledCheckbox(Rect cell, Rect box, Pawn pawn, WorkTypeDef work,
            int priority, UIColorPaletteDef palette)
        {
            bool enabled = priority > 0;

            if (UICheckboxControl.Draw(box, ref enabled, palette))
            {
                pawn.workSettings.SetPriority(work, enabled ? Pawn_WorkSettings.DefaultPriority : 0);
                Remembered.Remove(pawn);
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = SkillColor(pawn, work, palette, out string skillLabel);
            Widgets.Label(SkillLabelRect(cell, box), skillLabel);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// Where the skill readout goes: the whole strip between the priority box and the bottom of the cell.
        ///
        /// Both cell styles call this, so the number cannot end up in two different places depending on
        /// whether manual priorities are on.
        ///
        /// The height is not a constant, because the font is not one. <c>Widgets.Label</c> passes the rect
        /// through to <c>GUI.Label</c> untouched apart from UI-scale snapping, so the rect is the clip
        /// rectangle: a line box taller than the rect loses its ascenders and descenders, and a
        /// <c>MiddleCenter</c> anchor spends that overflow at both ends, which shaves the tops of the digits.
        ///
        /// And the font is genuinely not known here. <c>Text.Font = GameFont.Tiny</c> is a request, not a
        /// result -- the setter substitutes <c>Small</c> whenever <c>TinyFontSupported</c> is false, which
        /// covers a language whose <c>canBeTiny</c> is false, the "disable tiny text" preference, the Steam
        /// Deck, and any draw that happens during a long event. Small's line box is around half again Tiny's,
        /// so a constant tuned for one clips the other.
        ///
        /// Taking the free space below the box solves both: it is measured from the cell, so it follows
        /// <c>RowHeight</c>, and its top sits below the box by construction, so no font size can push the
        /// number back into it. <c>Text.LineHeight</c> is the floor for the case where a row is ever short
        /// enough that the free space alone would clip -- overflowing into the row gap is invisible, since the
        /// next row's own background paints over it, while clipped glyphs are not.
        ///
        /// Read <c>Text.LineHeight</c> rather than <c>LineHeightOf(GameFont.Tiny)</c>, because the getter
        /// indexes by the current font and therefore already reflects any substitution the setter made.
        /// Callers set the font before calling this.
        /// </summary>
        private static Rect SkillLabelRect(Rect cell, Rect box)
        {
            float top = box.yMax + SkillLabelGap;
            return new Rect(cell.x, top, cell.width, Mathf.Max(Text.LineHeight, cell.yMax - top));
        }

        /// <summary>
        /// A work type this pawn cannot do: the warning wash across the cell, and a dash where the number
        /// would be.
        ///
        /// The wash is what makes the row's warning stripe answerable -- the stripe says something is
        /// missing, and these say which. A dash on its own was too quiet to find by scanning a wide grid.
        /// </summary>
        private static void DrawIncapable(Rect cell, UIColorPaletteDef palette)
        {
            UIElementPainter.PaintStripeWash(cell, Wash(palette.Warning, WarningWashAlpha));

            Rect line = new Rect(cell.center.x - 8f, cell.center.y - 1f, 16f, 2f);
            Widgets.DrawBoxSolid(line, palette.TextDisabled);
        }

        /// <summary>
        /// The priority bands: 1-3 is work the pawn should get to first, 4-6 middling, 7-9 last resort. Zero
        /// is disabled and drawn in the disabled text color so the box reads as switched off.
        /// </summary>
        internal static Color ColorOfPriority(int priority, UIColorPaletteDef palette)
        {
            if (priority <= 0)
                return palette.TextDisabled;

            if (priority <= 3)
                return palette.Success;

            if (priority <= 6)
                return palette.Accent;

            return palette.Danger;
        }

        /// <summary>
        /// The pawn's skill for a work type, and the color it is drawn in.
        ///
        /// A work type can draw on more than one skill -- Doctor is Medicine alone, but Construction weighs
        /// Construction and Artistic -- so the average across relevantSkills is what the number reports,
        /// matching how the game itself decides competence at the work rather than at one skill.
        /// </summary>
        private static Color SkillColor(Pawn pawn, WorkTypeDef work, UIColorPaletteDef palette,
            out string label)
        {
            if (pawn.skills == null || work.relevantSkills == null || work.relevantSkills.Count == 0)
            {
                label = string.Empty;
                return palette.TextSecondary;
            }

            int total = 0;
            foreach (SkillDef skill in work.relevantSkills)
                total += pawn.skills.GetSkill(skill).Level;

            int average = total / work.relevantSkills.Count;
            label = "skill " + average;

            if (average < 5)
                return palette.Warning;

            if (average > 15)
                return palette.Success;

            return palette.TextSecondary;
        }
    }
}
