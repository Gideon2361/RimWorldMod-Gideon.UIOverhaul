using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
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

        private const float PortraitSize = 46f;
        private const float PriorityBoxSize = 26f;

        /// <summary>
        /// Space between a priority box and the skill readout under it.
        ///
        /// Tiny renders taller than its line height, so 2px of gap put the text's ascenders against the bottom
        /// of the box. There is room to spare in a 62px row, and a number touching the edge of a box reads as a
        /// rendering fault rather than as two separate things.
        /// </summary>
        private const float SkillLabelGap = 6f;

        /// <summary>
        /// How far up the body the portrait camera looks, and how far in.
        ///
        /// PortraitsCache takes both, which is what makes a face crop possible without any patching. The
        /// offset lifts the camera to head height; the zoom then fills the frame with it. Values are in world
        /// units, so they hold for any pawn size.
        /// </summary>
        private static readonly Vector3 FaceOffset = new Vector3(0f, 0f, 0.34f);

        private const float FaceZoom = 2.1f;

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
            EnsureColumns();
            Collect();

            Grid.Draw(inRect.ContractedBy(6f), UIColorPaletteDef.Active);
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
            Grid.SuppressCollapse = Search.filter.Active;

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
                    SectionLabel = NameOf(map),
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

        /// <summary>
        /// What to call a map.
        ///
        /// MapParent.LabelCap is what the world view shows when its tile is selected, which covers colonies
        /// by their given name and everything else -- caravan sites, ships, pocket maps from mods -- by
        /// whatever that parent calls itself. Going through the parent rather than special-casing Settlement
        /// is what makes mod-added map kinds work without naming them here.
        /// </summary>
        private static string NameOf(Map map)
        {
            if (map.Parent != null && !map.Parent.LabelCap.NullOrEmpty())
                return map.Parent.LabelCap;

            return "Unknown location";
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
        // Matched against the short name and the full one, so "Aleks" and a surname both find the same colonist.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Vanilla's own search widget, not a text field of ours.
        ///
        /// Two things come with it that a hand-rolled field cannot have:
        ///
        /// It names its control through <c>GUI.SetNextControlName</c>, so its identity does not depend on how many
        /// controls happened to be drawn before it. IMGUI derives a control's id from draw order, so a field whose
        /// neighbors come and go -- a clear button that only exists once there is text to clear -- changes id and
        /// silently loses focus mid-word.
        ///
        /// And the game can see it. <c>KeyBindingDef.IsDown</c>, <c>IsDownEvent</c>, <c>KeyDownEvent</c> and
        /// <c>JustPressed</c> all consult <c>WindowStack.AnySearchWidgetFocused</c>, which walks the window stack
        /// asking each window for its <c>CommonSearchWidget</c>. Every key binding in the game, camera dolly
        /// included, is suppressed while one of those has focus -- and only while one of *those* has focus, which
        /// is why our own field let W and A pan the map as we typed. See
        /// <see cref="Patch_Window_CommonSearchWidget"/> for the half that registers this one.
        /// </summary>
        internal static readonly QuickSearchWidget Search = new QuickSearchWidget();

        private static bool Matches(Pawn pawn)
        {
            if (!Search.filter.Active)
                return true;

            // Both names, so a nickname and a surname find the same colonist.
            return Search.filter.Matches(pawn.LabelShortCap) || Search.filter.Matches(pawn.LabelCap);
        }

        /// <summary>
        /// The search field: our chrome, vanilla's widget inside it.
        ///
        /// The widget draws only its magnifier and its text, so a themed box behind it is all it takes to stop it
        /// looking like stock chrome dropped into the panel.
        /// </summary>
        private static void DrawSearchField(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);

            Color previousColor = GUI.color;

            GUI.color = Search.CurrentlyFocused() ? palette.BorderFocused : palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;

            // Scroll reset on change: filtering can leave the view scrolled past everything that still matches,
            // which reads as the search finding nothing.
            Search.OnGUI(rect.ContractedBy(1f), () => Grid.Scroll = Vector2.zero);
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
            Rect portrait = new Rect(cell.x + 8f, cell.y + (cell.height - PortraitSize) * 0.5f,
                PortraitSize, PortraitSize);

            Color previousPortraitColor = GUI.color;

            // A sunken disc behind the render. The render is transparent everywhere the pawn is not, so without
            // this the head sits on the bare card; with it, on something.
            GUI.color = palette.SurfaceSunken;
            GUI.DrawTexture(portrait, UIShapes.Disc);
            GUI.color = previousPortraitColor;

            // Framed on the face: the camera is lifted to head height and zoomed in, which is what the
            // cameraOffset and cameraZoom parameters exist for. A full-body render at 46px is a silhouette.
            RenderTexture face = PortraitsCache.Get(pawn, new Vector2(PortraitSize, PortraitSize),
                Rot4.South, FaceOffset, FaceZoom);

            if (face != null)
                GUI.DrawTexture(portrait, face);

            // Cropped to a circle, the only way IMGUI can: the square render is drawn, then everything outside
            // an inscribed circle is painted over in the color behind it. There is no masking in IMGUI and no
            // shader to clip a RenderTexture with, so the crop is done by covering rather than by clipping.
            //
            // Which means the tint has to be whatever the row would have shown there. Plain card color for most
            // rows; for a downed one the card is under a stripe wash, and painting flat card color over that
            // would leave four clean triangles around the head. Half the wash alpha is that pattern's average,
            // since the tile is half stripe and half gap -- and an average is indistinguishable from stripes
            // across the 7px this covers, where re-drawing the wash itself would put stripes over the face.
            GUI.color = pawn.Downed
                ? Color.Lerp(palette.PanelBackground, palette.Danger, DangerWashAlpha * 0.5f)
                : palette.PanelBackground;

            GUI.DrawTexture(portrait, UIShapes.DiscCutout);
            GUI.color = previousPortraitColor;

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
                TooltipHandler.TipRegion(cell, (TipSignal) pawn.LabelCap);
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
            Widgets.Label(new Rect(cell.x, box.yMax + SkillLabelGap, cell.width, 14f), skillLabel);

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
            Widgets.Label(new Rect(cell.x, box.yMax + SkillLabelGap, cell.width, 14f), skillLabel);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
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
