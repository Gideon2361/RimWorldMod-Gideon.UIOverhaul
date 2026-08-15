using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Stages;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>Which lines the console is showing.</summary>
    internal enum LoadingConsoleFilter
    {
        /// <summary>Everything, definitions included. Thousands of lines on a modded install.</summary>
        All,

        /// <summary>The shape of the load: sections, phases and their detail, without the definitions.</summary>
        Phases,

        /// <summary>Errors and warnings only.</summary>
        Problems
    }

    /// <summary>
    /// The loading screen's output, kept and shown on the main menu so it can actually be read.
    ///
    /// <b>What this is for.</b> A loading screen says a great deal and keeps none of it. Every phase, every mod
    /// being read, every definition and every error goes past at whatever speed the load runs and is then gone, so
    /// the answer to "which part of my load takes ninety seconds" or "which file is that def error in" has been a
    /// matter of staring at the screen and trying to catch it. <see cref="UILoadingLog"/> keeps the sequence with
    /// a timestamp on each line, and this draws it.
    ///
    /// <b>Three filters, because the full list is too long to read and too useful to shorten.</b> Every definition
    /// is logged with the file it came from, which on a heavy mod list is tens of thousands of lines. That is the
    /// right thing to keep -- it is exactly what you need when one specific def is wrong -- and the wrong thing to
    /// open on. <see cref="LoadingConsoleFilter.Phases"/> is the overview, <see cref="LoadingConsoleFilter.All"/>
    /// is the detail, and <see cref="LoadingConsoleFilter.Problems"/> is what somebody with a broken load actually
    /// came for. The search box narrows any of them.
    ///
    /// <b>Off by default, and the recording behind it is not.</b> This is a diagnostic panel over the main menu;
    /// somebody who has not gone looking for it does not want a wall of profiler labels across their title screen.
    /// But turning it on shows the load that already happened rather than demanding a restart and a reproduction,
    /// which is only possible because the recording never waited for the switch.
    ///
    /// <b>It exists at the main menu and nowhere else.</b> Once a game starts the log is released and recording
    /// stops, so none of this is carried through a colony. See <c>Patch_Game_FinalizeInit_StopConsole</c>.
    ///
    /// <b>Placed where the main menu is empty.</b> The menu's own controls sit on the right, the title above them,
    /// and the expansion icons along the bottom left, which leaves the left column free. The one thing that does
    /// claim it is the translation credits box, and only for a player not running the game in English, so this
    /// steps aside when that is drawn rather than sitting on top of it.
    /// </summary>
    internal static class LoadingConsole
    {
        private const float Inset = 8f;

        /// <summary>
        /// How wide the panel wants to be.
        ///
        /// Roughly double what it started at. The first version was sized for a stage name and a timestamp, and
        /// then definitions arrived carrying a file path each -- a path is well over a hundred characters, so the
        /// old width left the two columns fighting and ellipsized both. Clamped to the screen below, so a small
        /// display gets whatever is there rather than a panel running off the edge.
        /// </summary>
        private const float Width = 1120f;

        /// <summary>Height of the detail pane under the list, when a line is selected.</summary>
        private const float DetailHeight = 170f;

        /// <summary>Height of the pane while a file search is showing. Taller: it is a list, not a paragraph.</summary>
        private const float SearchHeight = 260f;

        /// <summary>Where vanilla starts drawing down the left edge, and the height it reserves at the bottom.</summary>
        private const float TopInset = 100f;

        private const float ExpansionIconBand = 96f + 8f + 8f;

        /// <summary>The translation credits box, from <c>MainMenuDrawer.MainMenuOnGUI</c>: 300 wide at x 8.</summary>
        private const float TranslationBoxWidth = 300f;

        private const float HeaderHeight = 28f;
        private const float ToolbarHeight = 28f;
        private const float FooterHeight = 28f;
        private const float RowPad = 2f;

        /// <summary>Width of the elapsed-time column. Wide enough for three digits of seconds.</summary>
        private const float TimeColumn = 60f;

        /// <summary>How far a step or definition is indented under the phase it belongs to.</summary>
        private const float StepIndent = 12f;

        /// <summary>Arbitrary and only has to be stable and unlike anything vanilla uses.</summary>
        private const int WindowId = 0x4C_4F_41_44;

        private static Vector2 scroll;
        private static Vector2 detailScroll;
        private static Vector2 searchScroll;

        /// <summary>Whether plain mentions are shown alongside declarations and patches.</summary>
        private static bool showReferences;

        /// <summary>Whether the last draw was scrolled to the bottom, so new content keeps following.</summary>
        private static bool pinnedToEnd = true;

        private static LoadingConsoleFilter filter = LoadingConsoleFilter.Phases;

        /// <summary>
        /// The line whose full detail is showing, or null.
        ///
        /// A copy of the entry rather than an index into the list, and that is not incidental: the visible list is
        /// rebuilt whenever the filter or the search changes, so an index would point at a different line the
        /// moment somebody typed. Entries are small structs, so keeping one costs nothing and keeps the detail
        /// pane showing what was actually clicked.
        /// </summary>
        private static UILoadingLogEntry? selected;

        /// <summary>
        /// The search box.
        ///
        /// <c>UITextBoxControl</c> rather than <c>Widgets.TextField</c>, and that is not a style choice: anything
        /// else lets the camera and the shortcut keys take the keypresses while you are typing into it.
        /// </summary>
        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search text or file path"
        };

        /// <summary>
        /// The lines currently shown, and what they were derived from.
        ///
        /// Rebuilt only when something that decides it has changed. Filtering tens of thousands of entries by a
        /// substring is not free, and doing it every frame to draw forty visible rows would be most of what the
        /// panel costs.
        /// </summary>
        private static List<UILoadingLogEntry> visible = new List<UILoadingLogEntry>();

        private static int cachedSourceCount = -1;
        private static string cachedSearch = null;
        private static LoadingConsoleFilter cachedFilter = LoadingConsoleFilter.All;

        internal static bool Enabled =>
            UIGuard.Try("Diagnostics.ReadLoadingConsole",
                () => UIOverhaulSettingsFile.Current?.showLoadingConsole ?? false, false,
                "The loading console is hidden.");

        /// <summary>
        /// Draws the panel, if it is switched on and there is anything in it.
        ///
        /// In an ImmediateWindow rather than straight onto the menu, for the reason the message cards use one: a
        /// window gets its clicks seen and its scroll wheel handled by the window stack, where drawing inline
        /// would put a scroll view underneath anything the menu opens and leave its input to be swallowed.
        /// </summary>
        internal static void Draw()
        {
            if (!Enabled || UILoadingLog.Count == 0)
                return;

            Rect rect = Bounds();

            Find.WindowStack.ImmediateWindow(WindowId, rect, WindowLayer.GameUI,
                () => UIGuard.Try("Diagnostics.LoadingConsole", () => Contents(rect.AtZero()),
                    "The loading console is blank. Nothing else on the menu is affected."),
                false, false, 0f);
        }

        /// <summary>
        /// Drops everything the console is holding: the selection, the scroll positions and any running search.
        ///
        /// Called when a game starts, alongside the framework releasing the log itself.
        /// </summary>
        internal static void Release()
        {
            XmlFileSearch.Reset();

            selected = null;
            visible = new List<UILoadingLogEntry>();
            cachedSourceCount = -1;
            scroll = Vector2.zero;
            detailScroll = Vector2.zero;
            searchScroll = Vector2.zero;
            pinnedToEnd = true;
        }

        /// <summary>
        /// The panel's rect: down the left edge, clear of what the menu already draws there.
        ///
        /// The translation test is vanilla's own, from <c>DoTranslationInfoRect</c>, rather than a guess at when
        /// that box appears. Getting it wrong in either direction is visible: too eager and the panel is shoved
        /// aside for nothing, too lax and it sits on top of the credits.
        /// </summary>
        private static Rect Bounds()
        {
            bool translationShown = UIGuard.Try("Diagnostics.TranslationBoxCheck",
                () => LanguageDatabase.activeLanguage != LanguageDatabase.defaultLanguage
                      || DebugSettings.enableTranslationWindowInEnglish,
                false, "The loading console may overlap the translation credits.");

            float x = Inset + (translationShown ? TranslationBoxWidth + Inset : 0f);
            float bottom = UI.screenHeight - ExpansionIconBand;

            return new Rect(x, TopInset, Mathf.Min(Width, UI.screenWidth - x - Inset),
                Mathf.Max(160f, bottom - TopInset));
        }

        private static void Contents(Rect rect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // <b>Opaque, unlike the corner panel.</b> HudBackground carries an alpha because it is drawn over a
            // live map somebody is watching, and that is exactly wrong here: this is a wall of small text being
            // read against the menu's hero art, and the artwork showing through it makes every line harder to
            // pick out. A reading surface gets the panel fill.
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Color previousColor = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;

            Rebuild();

            Rect header = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, HeaderHeight);
            Rect toolbar = new Rect(rect.x + 8f, header.yMax + 2f, rect.width - 16f, ToolbarHeight);
            Rect footer = new Rect(rect.x + 8f, rect.yMax - FooterHeight, rect.width - 16f, FooterHeight - 4f);

            // The detail pane only takes room when there is something in it, so an unselected console is the same
            // list it was before selection existed rather than one permanently shorter by a blank panel. A file
            // search needs more room than one entry's text, so it gets more.
            bool searching = XmlFileSearch.State != XmlSearchState.Idle;
            float detail = searching ? SearchHeight : selected.HasValue ? DetailHeight : 0f;

            Rect body = new Rect(rect.x + 4f, toolbar.yMax + 4f, rect.width - 8f,
                Mathf.Max(0f, footer.y - toolbar.yMax - 12f - detail));

            DrawHeader(header, palette);
            DrawToolbar(toolbar, palette);
            DrawRows(body, palette);

            Rect pane = new Rect(rect.x + 4f, body.yMax + 4f, rect.width - 8f, detail);

            if (searching)
                DrawSearchResults(pane, palette);
            else if (selected.HasValue)
                DrawDetail(pane, selected.Value, palette);

            DrawFooter(footer, palette);
        }

        /// <summary>
        /// Recomputes the visible list, if anything that decides it has moved.
        ///
        /// The source count stands in for the log's contents: it only ever grows while recording, and it is reset
        /// when the log is cleared, so a change in it is a change in what there is to show. Comparing that is a
        /// single integer read where comparing the entries themselves would be the work this cache exists to
        /// avoid.
        /// </summary>
        private static void Rebuild()
        {
            int count = UILoadingLog.Count;
            string search = Search.Text ?? string.Empty;

            if (count == cachedSourceCount && search == cachedSearch && filter == cachedFilter)
                return;

            cachedSourceCount = count;
            cachedSearch = search;
            cachedFilter = filter;

            List<UILoadingLogEntry> all = UILoadingLog.Snapshot();
            List<UILoadingLogEntry> shown = new List<UILoadingLogEntry>(Mathf.Min(all.Count, 512));

            foreach (UILoadingLogEntry entry in all)
            {
                if (!PassesFilter(entry))
                    continue;

                // The path is searched as well as the text, which is most of why the path is worth keeping: the
                // useful question is usually "what came out of this file", not "what is this def called".
                if (!Search.IsEmpty && !Search.Matches(entry.Text) && !Search.Matches(entry.Path))
                    continue;

                shown.Add(entry);
            }

            visible = shown;
        }

        private static bool PassesFilter(UILoadingLogEntry entry)
        {
            switch (filter)
            {
                case LoadingConsoleFilter.Problems:
                    return entry.IsProblem;

                case LoadingConsoleFilter.Phases:
                    // Problems stay visible in the overview. They are the reason somebody is reading it, and a
                    // view that hid them would be an overview of a load that looked fine.
                    return entry.Kind != UILoadingLogKind.Def;

                default:
                    return true;
            }
        }

        private static void DrawHeader(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                Widgets.Label(rect, "Loading console");

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextSecondary;

                string counts = visible.Count == UILoadingLog.Count
                    ? UILoadingLog.Count + " lines"
                    : visible.Count + " of " + UILoadingLog.Count;

                Widgets.Label(rect, counts + ", " + UILoadingLog.Duration(UILoadingLog.TotalSeconds));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>The three filters and the search box, on one row.</summary>
        private static void DrawToolbar(Rect rect, UIColorPaletteDef palette)
        {
            const float tabWidth = 74f;

            float x = rect.x;

            foreach (LoadingConsoleFilter option in
                     new[]
                     {
                         LoadingConsoleFilter.Phases, LoadingConsoleFilter.All, LoadingConsoleFilter.Problems
                     })
            {
                Rect tab = new Rect(x, rect.y, tabWidth, rect.height);

                if (Tab(tab, Label(option), option == filter, palette, Count(option)))
                {
                    filter = option;
                    scroll = Vector2.zero;
                    pinnedToEnd = false;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                x += tabWidth + 4f;
            }

            Search.Draw(new Rect(x + 4f, rect.y, Mathf.Max(60f, rect.xMax - x - 4f), rect.height), palette);
        }

        private static string Label(LoadingConsoleFilter option)
        {
            switch (option)
            {
                case LoadingConsoleFilter.Phases:
                    return "Phases";

                case LoadingConsoleFilter.Problems:
                    return "Problems";

                default:
                    return "All";
            }
        }

        /// <summary>
        /// How many lines a filter would show, for the count on its tab.
        ///
        /// Only the problem count is worth computing: it is the one a reader wants to know before clicking, since
        /// "Problems 0" and "Problems 14" are entirely different situations and the difference should not require
        /// a click to discover. The other two would just restate the header.
        /// </summary>
        private static int Count(LoadingConsoleFilter option)
        {
            if (option != LoadingConsoleFilter.Problems)
                return -1;

            return UIGuard.Try("Diagnostics.CountProblems", () =>
            {
                int problems = 0;

                foreach (UILoadingLogEntry entry in UILoadingLog.Snapshot())
                {
                    if (entry.IsProblem)
                        problems++;
                }

                return problems;
            }, -1, null);
        }

        private static bool Tab(Rect rect, string label, bool chosen, UIColorPaletteDef palette, int count)
        {
            bool over = Mouse.IsOver(rect);

            Widgets.DrawBoxSolid(rect, chosen ? palette.AccentMuted
                : over ? palette.HoverOverlay : palette.ControlBackgroundFaded);

            Color previousColor = GUI.color;
            GUI.color = chosen ? palette.Accent : palette.Border;
            Widgets.DrawBox(rect, 1);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            // A tab that is counting something with nothing in it is drawn quiet rather than absent, so "no
            // problems" reads as an answer instead of a missing feature.
            GUI.color = chosen ? palette.TextPrimary : palette.TextSecondary;

            if (count > 0 && !chosen)
                GUI.color = palette.Warning;

            Widgets.Label(rect, count >= 0 ? label + " " + count : label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>
        /// The lines, one row each, in a scroll view.
        ///
        /// <b>Follows the end unless the reader has scrolled away from it.</b> The log is still being written
        /// while a load runs, and a panel that jumps to the bottom every time a line arrives is one nobody can
        /// read the middle of. Whether to follow is decided from where the view was left, which is the behavior
        /// every console has and nobody notices until it is missing.
        ///
        /// <b>Only the rows on screen are laid out.</b> The full list is tens of thousands of lines and forty of
        /// them fit; drawing all of them to show those forty is the difference between a panel and a stutter.
        /// </summary>
        private static void DrawRows(Rect rect, UIColorPaletteDef palette)
        {
            float rowHeight = UIFonts.LineHeightOf(GameFont.Tiny) + RowPad;
            float contentHeight = visible.Count * rowHeight;

            Rect view = new Rect(0f, 0f, rect.width - 18f, contentHeight);

            if (pinnedToEnd)
                scroll.y = Mathf.Max(0f, contentHeight - rect.height);

            Widgets.BeginScrollView(rect, ref scroll, view);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                // <b>Wrapping off, and this is not a detail.</b> Every row here is one line tall, and a good many
                // of the lines are long -- an exception message runs to several hundred characters. With wrapping
                // on, Widgets.Label hands that to GUI.Label as a multi-line block, and a MiddleLeft anchor
                // centres the block on the rect, so the extra lines are drawn above and below it, straight
                // through the neighbouring rows. The list rendered as overlapping text until this was set.
                Text.WordWrap = false;

                int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / rowHeight) - 1);
                int last = Mathf.Min(visible.Count, first + Mathf.CeilToInt(rect.height / rowHeight) + 2);

                for (int i = first; i < last; i++)
                    DrawRow(new Rect(0f, i * rowHeight, view.width, rowHeight), visible[i], palette);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Widgets.EndScrollView();

            // Measured after the view, so it reflects wherever the wheel or the bar just left it. The tolerance
            // is a row, so landing a pixel short of the bottom still counts as being at it.
            pinnedToEnd = scroll.y >= contentHeight - rect.height - rowHeight;
        }

        private static void DrawRow(Rect rect, UILoadingLogEntry entry, UIColorPaletteDef palette)
        {
            bool chosen = selected.HasValue && selected.Value.Seconds == entry.Seconds
                                            && selected.Value.Text == entry.Text;

            if (chosen)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
            else if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(rect.x, rect.y, TimeColumn, rect.height), entry.Seconds.ToString("F2"));

            Text.Anchor = TextAnchor.MiddleLeft;

            float indent = entry.Kind == UILoadingLogKind.Step || entry.Kind == UILoadingLogKind.Def
                ? StepIndent
                : 0f;

            float x = rect.x + TimeColumn + 6f + indent;

            GUI.color = ColorOf(entry, palette);

            // Only the first line of a logged message. An error carries its whole stack trace, which is what
            // makes it useful in the copied text and unusable in a row.
            string label = entry.IsProblem ? UILoadingLog.FirstLine(entry.Text) : entry.Text;

            if (entry.Repeats > 1)
                label += "  x" + entry.Repeats;

            // The right-hand column carries whichever of the two matters for this kind of line: how long a phase
            // took, or which file a definition came from. Neither is ever wanted at the same time as the other.
            bool slow = entry.Duration >= 0.25f && !entry.IsProblem && entry.Kind != UILoadingLogKind.Def;
            bool hasPath = !entry.Path.NullOrEmpty();

            // The path column gets a generous share now the panel is wide enough for one. A path is the longer of
            // the two things on the row and the one that is useless truncated to a stub.
            float rightWidth = slow ? 60f : hasPath ? Mathf.Min(420f, rect.width * 0.4f) : 0f;

            Widgets.LabelEllipses(new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - x - rightWidth - 4f),
                rect.height), label);

            if (slow)
            {
                Text.Anchor = TextAnchor.MiddleRight;

                // Anything over a second is worth pointing at rather than merely stating.
                GUI.color = entry.Duration >= 1f ? palette.Warning : palette.TextDisabled;

                Widgets.Label(new Rect(rect.xMax - rightWidth, rect.y, rightWidth, rect.height),
                    UILoadingLog.Duration(entry.Duration));
            }
            else if (hasPath)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                // The file name in the row and the full path on hover. A full path is two hundred characters and
                // would leave no room for the definition it belongs to, which is the thing being looked up.
                Widgets.LabelEllipses(new Rect(rect.xMax - rightWidth, rect.y, rightWidth, rect.height),
                    FileNameOf(entry.Path));
            }

            // Clicking opens the line in the detail pane; clicking the open one closes it again, so the list can
            // be given its full height back without hunting for a close button.
            if (Widgets.ButtonInvisible(rect))
            {
                selected = chosen ? (UILoadingLogEntry?) null : entry;
                detailScroll = Vector2.zero;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (!Mouse.IsOver(rect))
                return;

            // A short tooltip only. The full text has a pane of its own now, and a tooltip carrying a whole stack
            // trace covers the list it is describing.
            string tip = entry.IsProblem ? "Click for the full message." : entry.Text;

            if (hasPath)
                tip += "\n\n" + entry.Path;

            TooltipHandler.TipRegion(rect, (TipSignal) tip);
        }

        /// <summary>
        /// The full detail of one line, under the list.
        ///
        /// <b>This is what the list cannot do.</b> Every row is one line high, because a console with thousands
        /// of them has to be scannable; but an error is a message and a stack trace, and a definition has a path
        /// that is longer than the row it sits in. Rather than compromise the list, the whole of one entry gets
        /// its own space here when it is asked for.
        ///
        /// The path is shown in full and on its own line, since it is usually the thing being read and it is
        /// almost always too long to sit beside anything else.
        /// </summary>
        private static void DrawDetail(Rect rect, UILoadingLogEntry entry, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);

            Color previousColor = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;

            Rect inner = rect.ContractedBy(6f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                float headerHeight = UIFonts.LineHeightOf(GameFont.Tiny) + 2f;
                Rect line = new Rect(inner.x, inner.y, inner.width, headerHeight);

                GUI.color = ColorOf(entry, palette);
                Widgets.Label(line, KindLabel(entry.Kind) + "  at " + entry.Seconds.ToString("F2") + "s"
                                    + (entry.Duration >= 0.05f
                                        ? "  for " + UILoadingLog.Duration(entry.Duration)
                                        : string.Empty)
                                    + (entry.Repeats > 1 ? "  x" + entry.Repeats : string.Empty));

                Rect copyOne = new Rect(inner.xMax - 96f, inner.y, 96f, headerHeight);

                if (Button(copyOne, "Copy line", palette))
                {
                    UIGuard.Try("Diagnostics.CopyLoadingLine",
                        () => { GUIUtility.systemCopyBuffer = DetailText(entry); },
                        "That line could not be copied to the clipboard.");

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                // The name worth going and looking for, if this line has one. Offered only when there is
                // something specific to search for: a button that would scan every mod on disk for the word
                // "Processing" is a button that wastes ten seconds and answers nothing.
                string subject = SubjectOf(entry);

                if (!subject.NullOrEmpty())
                {
                    Rect find = new Rect(copyOne.x - 132f, inner.y, 128f, headerHeight);

                    if (Button(find, "Find in mod files", palette))
                    {
                        UIGuard.Try("Diagnostics.StartFileSearch", () => XmlFileSearch.Start(subject),
                            "The file search could not be started.");

                        searchScroll = Vector2.zero;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }

                    if (Mouse.IsOver(find))
                        TooltipHandler.TipRegion(find, (TipSignal)
                            ("Read every XML file in every loaded mod, looking for \"" + subject + "\".\n\n"
                             + "This is how you find which mod references something that failed to load, in the "
                             + "cases where the game itself no longer knows. It runs in the background and takes "
                             + "a few seconds on a large mod list."));
                }

                float y = line.yMax + 2f;

                if (!entry.Path.NullOrEmpty())
                {
                    GUI.color = palette.Accent;
                    Widgets.LabelEllipses(new Rect(inner.x, y, inner.width, headerHeight), entry.Path);
                    y += headerHeight + 2f;
                }

                Rect textRect = new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y));

                GUI.color = palette.TextPrimary;
                Text.Anchor = TextAnchor.UpperLeft;

                // Wrapping on, explicitly. This is the one place in the console that wants a paragraph rather
                // than a row, and it is drawn right after a list that deliberately turns wrapping off -- so
                // relying on whatever the flag happens to hold is how this would silently become one long line.
                Text.WordWrap = true;

                // Measured at the wrapped width so a stack trace scrolls rather than being cut off at the pane's
                // height, which is the whole reason this is a scroll view and not a label.
                float height = Text.CalcHeight(entry.Text, textRect.width - 18f);
                Rect view = new Rect(0f, 0f, textRect.width - 18f, Mathf.Max(height, textRect.height));

                Widgets.BeginScrollView(textRect, ref detailScroll, view);
                Widgets.Label(view, entry.Text);
                Widgets.EndScrollView();
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The identifier this line is about, or null if it has none worth searching for.
        ///
        /// A definition is about itself. A cross-reference failure is about the def it could not find, which is
        /// the whole point: that name is in somebody's XML and finding whose is the question the console cannot
        /// otherwise answer. Everything else is prose and would turn a file search into a word count.
        /// </summary>
        private static string SubjectOf(UILoadingLogEntry entry)
        {
            if (entry.Kind == UILoadingLogKind.Def)
                return entry.Text;

            if (!entry.IsProblem)
                return null;

            const string marker = " named ";

            int start = entry.Text.IndexOf(marker, System.StringComparison.Ordinal);

            if (start < 0)
                return null;

            start += marker.Length;

            int end = entry.Text.IndexOf(" (", start, System.StringComparison.Ordinal);

            if (end < 0)
                end = entry.Text.IndexOf('\n', start);

            if (end < 0)
                end = entry.Text.Length;

            string name = entry.Text.Substring(start, end - start).Trim();

            // A name with a space in it is a sentence fragment rather than a defName, which means the message did
            // not have the shape this was expecting and the result would be a search for prose.
            return name.Length > 1 && name.IndexOf(' ') < 0 ? name : null;
        }

        /// <summary>
        /// The file search: its progress while it runs, and the lines it found when it is done.
        ///
        /// <b>Grouped by mod, because that is the answer.</b> Somebody looking at a missing def wants to know
        /// which mod referenced it, and a flat list of two hundred absolute paths makes them read the same folder
        /// prefix two hundred times to work that out.
        /// </summary>
        private static void DrawSearchResults(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);

            Color previousColor = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;

            Rect inner = rect.ContractedBy(6f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                // Same reason as the log rows: a matched XML line is arbitrarily long and each row is one line
                // tall, so wrapping would draw the overflow through the rows either side of it.
                Text.WordWrap = false;

                float lineHeight = UIFonts.LineHeightOf(GameFont.Tiny) + 2f;
                Rect head = new Rect(inner.x, inner.y, inner.width, lineHeight);

                XmlSearchState state = XmlFileSearch.State;

                int done;
                int all;
                int files;

                XmlFileSearch.Progress(out done, out all, out files);

                GUI.color = palette.TextPrimary;

                string status;

                switch (state)
                {
                    case XmlSearchState.Running:
                        status = all > 0
                            ? "Searching for \"" + XmlFileSearch.Term + "\" ... " + done + " of " + all + " files"
                            : "Searching for \"" + XmlFileSearch.Term + "\" ... listing files";
                        break;

                    case XmlSearchState.Failed:
                        GUI.color = palette.Danger;
                        status = "The search failed: " + XmlFileSearch.Failure;
                        break;

                    default:
                        status = "\"" + XmlFileSearch.Term + "\" declared or patched in " + files + " file(s)";
                        break;
                }

                int declared;
                int referenced;

                XmlFileSearch.Counts(out declared, out referenced);

                Widgets.Label(new Rect(head.x, head.y, head.width - 290f, head.height), status);

                Rect close = new Rect(inner.xMax - 68f, inner.y, 68f, lineHeight);
                Rect copyAll = new Rect(close.x - 76f, inner.y, 72f, lineHeight);
                Rect refs = new Rect(copyAll.x - 128f, inner.y, 124f, lineHeight);
                Rect stop = new Rect(refs.x - 62f, inner.y, 58f, lineHeight);

                if (state == XmlSearchState.Running && Button(stop, "Stop", palette))
                {
                    UIGuard.Try("Diagnostics.CancelFileSearch", XmlFileSearch.Cancel, null);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                // The other half of the investigation, one click away rather than mixed into the answer. A
                // declaration says where a def comes from; a reference says who is asking for one that is
                // missing, and those are different questions asked on different days.
                if (referenced > 0 && Button(refs,
                        showReferences ? "Hide " + referenced + " refs" : "Show " + referenced + " refs", palette))
                {
                    showReferences = !showReferences;
                    searchScroll = Vector2.zero;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                if (Mouse.IsOver(refs) && referenced > 0)
                    TooltipHandler.TipRegion(refs, (TipSignal)
                        ("Lines that merely name it: list entries, attributes, comments.\n\nThese are hidden by "
                         + "default because they answer a different question. Show them to find which mod is "
                         + "asking for something that is missing."));

                if (declared + referenced > 0 && Button(copyAll, "Copy", palette))
                {
                    UIGuard.Try("Diagnostics.CopyFileSearch",
                        () => { GUIUtility.systemCopyBuffer = SearchText(); },
                        "The search results could not be copied.");

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                if (Button(close, "Close", palette))
                {
                    UIGuard.Try("Diagnostics.ResetFileSearch", XmlFileSearch.Reset, null);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                List<XmlSearchHit> results = XmlFileSearch.Hits(showReferences);

                Rect list = new Rect(inner.x, head.yMax + 2f, inner.width,
                    Mathf.Max(0f, inner.yMax - head.yMax - 2f));

                float rowHeight = lineHeight * 2f;
                Rect view = new Rect(0f, 0f, list.width - 18f, results.Count * rowHeight);

                Widgets.BeginScrollView(list, ref searchScroll, view);

                int first = Mathf.Max(0, Mathf.FloorToInt(searchScroll.y / rowHeight) - 1);
                int last = Mathf.Min(results.Count, first + Mathf.CeilToInt(list.height / rowHeight) + 2);

                for (int i = first; i < last; i++)
                    DrawHit(new Rect(0f, i * rowHeight, view.width, rowHeight), results[i], palette, lineHeight);

                Widgets.EndScrollView();
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>One matching line: where it is on the first row, what it says on the second.</summary>
        private static void DrawHit(Rect rect, XmlSearchHit hit, UIColorPaletteDef palette, float lineHeight)
        {
            if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Text.Anchor = TextAnchor.MiddleLeft;

            // A declaration is the answer, a patch is a strong lead, a reference is context. Colored as such, so
            // a list holding all three can be read without checking each one.
            GUI.color = hit.Kind == XmlSearchHitKind.Definition ? palette.Success
                : hit.Kind == XmlSearchHitKind.Patch ? palette.Accent
                : palette.TextDisabled;

            Widgets.LabelEllipses(new Rect(rect.x + 2f, rect.y, rect.width - 4f, lineHeight),
                KindTag(hit.Kind) + hit.Mod + "  -  " + hit.Path + ":" + hit.Line);

            GUI.color = palette.TextSecondary;

            Widgets.LabelEllipses(new Rect(rect.x + 14f, rect.y + lineHeight, rect.width - 16f, lineHeight),
                hit.Text);

            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, (TipSignal)
                    (hit.Path + "\nLine " + hit.Line + "\n\n" + hit.Text + "\n\nClick to copy the path."));

            if (!Widgets.ButtonInvisible(rect))
                return;

            // Copying the path is the most useful thing a click can do here: nothing in RimWorld can open a text
            // editor, and the path is what somebody needs in order to go and look at the file themselves.
            UIGuard.Try("Diagnostics.CopyHitPath", () => { GUIUtility.systemCopyBuffer = hit.Path; },
                "That path could not be copied.");

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static string KindTag(XmlSearchHitKind kind)
        {
            switch (kind)
            {
                case XmlSearchHitKind.Definition:
                    return "[defined]  ";

                case XmlSearchHitKind.Patch:
                    return "[patch]  ";

                default:
                    return "[mentions]  ";
            }
        }

        private static string SearchText()
        {
            List<XmlSearchHit> results = XmlFileSearch.Hits(showReferences);
            System.Text.StringBuilder text = new System.Text.StringBuilder(results.Count * 96);

            int done;
            int all;
            int files;

            XmlFileSearch.Progress(out done, out all, out files);

            text.Append("Search for \"").Append(XmlFileSearch.Term).Append("\" across loaded mod XML: ")
                .Append(files).Append(" file(s), ").Append(results.Count).Append(" line(s).\n\n");

            foreach (XmlSearchHit hit in results)
                text.Append(KindTag(hit.Kind)).Append(hit.Mod).Append("\n  ").Append(hit.Path).Append(':')
                    .Append(hit.Line).Append("\n    ").Append(hit.Text).Append('\n');

            return text.ToString();
        }

        private static string DetailText(UILoadingLogEntry entry)
        {
            string text = KindLabel(entry.Kind) + " at " + entry.Seconds.ToString("F2") + "s\n";

            if (!entry.Path.NullOrEmpty())
                text += entry.Path + "\n";

            return text + "\n" + entry.Text;
        }

        private static string KindLabel(UILoadingLogKind kind)
        {
            switch (kind)
            {
                case UILoadingLogKind.Error:
                    return "Error";

                case UILoadingLogKind.Warning:
                    return "Warning";

                case UILoadingLogKind.Def:
                    return "Definition";

                case UILoadingLogKind.Stage:
                    return "Phase";

                case UILoadingLogKind.Section:
                    return "Load";

                default:
                    return "Step";
            }
        }

        private static Color ColorOf(UILoadingLogEntry entry, UIColorPaletteDef palette)
        {
            switch (entry.Kind)
            {
                case UILoadingLogKind.Error:
                    return palette.Danger;

                case UILoadingLogKind.Warning:
                    return palette.Warning;

                case UILoadingLogKind.Section:
                    return palette.Accent;

                case UILoadingLogKind.Stage:
                    return palette.TextPrimary;

                default:
                    return palette.TextSecondary;
            }
        }

        /// <summary>The file name out of a full path, without paying for a Path.GetFileName exception contract.</summary>
        private static string FileNameOf(string path)
        {
            if (path.NullOrEmpty())
                return string.Empty;

            int slash = path.LastIndexOfAny(new[] { '\\', '/' });

            return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
        }

        private static void DrawFooter(Rect rect, UIColorPaletteDef palette)
        {
            Rect copy = new Rect(rect.x, rect.y, 96f, rect.height);
            Rect clear = new Rect(copy.xMax + 6f, rect.y, 72f, rect.height);

            if (Button(copy, "Copy", palette))
            {
                // What is on screen, not the whole log. Somebody who has narrowed this to four errors and then
                // pastes forty thousand definitions into a bug report has been answered a question they did not
                // ask. A statement block rather than an expression lambda: an assignment written as an expression
                // has the assigned value as its result, which makes it a Func<string> and binds the generic Try
                // overload -- quietly taking the consequence text below as a fallback value instead of reporting.
                if (UIGuard.Try("Diagnostics.CopyLoadingLog",
                        () => { GUIUtility.systemCopyBuffer = UILoadingLog.AsText(visible, Describe()); },
                        "The loading console could not be copied to the clipboard."))
                {
                    // Guarded on its own. This runs at the entry screen, where there is no game and the message
                    // system is further from its usual footing than anywhere else; failing to confirm a copy that
                    // worked should cost the confirmation, not retire the panel for the session.
                    UIGuard.Try("Diagnostics.CopyLoadingLogNotice",
                        () => Messages.Message(visible.Count + " lines copied to the clipboard.",
                            MessageTypeDefOf.SilentInput, false),
                        "The copy worked but said nothing about it.");
                }

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (Button(clear, "Clear", palette))
            {
                UIGuard.Try("Diagnostics.ClearLoadingLog", UILoadingLog.Clear,
                    "The loading console was not cleared.");

                scroll = Vector2.zero;
                detailScroll = Vector2.zero;
                pinnedToEnd = true;

                // The detail pane holds a copy of its entry, so without this it would keep showing a line that is
                // no longer in the log it came from.
                selected = null;

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            int dropped = UILoadingLog.Dropped;

            if (dropped <= 0)
                return;

            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.Warning;

            Widgets.Label(new Rect(clear.xMax + 6f, rect.y, Mathf.Max(0f, rect.xMax - clear.xMax - 6f),
                rect.height), dropped + " lines not kept");

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>What the copied text says it is, so a pasted excerpt is not mistaken for the whole load.</summary>
        private static string Describe()
        {
            string description = Label(filter).ToLowerInvariant();

            return Search.IsEmpty ? description : description + " matching \"" + Search.Text + "\"";
        }

        private static bool Button(Rect rect, string label, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);
            UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextPrimary;

            Widgets.Label(rect, label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return Widgets.ButtonInvisible(rect);
        }
    }

    /// <summary>
    /// Puts the loading console on the main menu.
    ///
    /// <c>MainMenuOnGUI</c> rather than <c>DoMainMenuControls</c>, and the difference matters: the controls are
    /// drawn on the entry screen and again by the in-game Esc menu, while this method belongs to the entry screen
    /// alone. Patching the other one would put a loading console over the pause menu mid-colony.
    ///
    /// A postfix, so the panel is registered after the menu has laid itself out and cannot disturb it.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    public static class Patch_MainMenuDrawer_MainMenuOnGUI
    {
        public static void Postfix()
        {
            UIGuard.Try("Diagnostics.MainMenuConsole", LoadingConsole.Draw,
                "The loading console is not shown. The main menu is otherwise unaffected.");
        }
    }

    /// <summary>
    /// Shuts the console's own state down when a game starts.
    ///
    /// <b>Separate from the patch that releases the log, and it has to be.</b> That one lives in the framework,
    /// which cannot see this feature -- the dependency runs the other way. This is the mod-side half: the file
    /// search is a background thread reading thousands of files, and a colony is the last place it should still
    /// be doing that. Two postfixes on one method is ordinary; they are independent and neither needs the other.
    /// </summary>
    [HarmonyPatch(typeof(Verse.Game), nameof(Verse.Game.FinalizeInit))]
    public static class Patch_Game_FinalizeInit_StopSearch
    {
        public static void Postfix()
        {
            UIGuard.Try("Diagnostics.StopFileSearch", LoadingConsole.Release,
                "A background file search may keep running until the game is restarted.");
        }
    }
}
