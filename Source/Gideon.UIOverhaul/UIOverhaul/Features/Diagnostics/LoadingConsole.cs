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

        /// <summary>The verdict strip: a label, a figure and a line of context, stacked.</summary>
        private const float VerdictHeight = 54f;

        /// <summary>
        /// How long a step must have taken to appear in the overview.
        ///
        /// Fifty milliseconds: below this a row cannot draw a bar anybody can see, and a row without a bar in a
        /// view built around them is a row saying nothing. Deliberately the same figure the bars use, so the
        /// overview holds exactly the entries the overview can express.
        /// </summary>
        private const float OverviewThreshold = 0.05f;
        private const float ToolbarHeight = 28f;
        private const float FooterHeight = 28f;
        private const float RowPad = 2f;

        /// <summary>Width of the elapsed-time column. Wide enough for three digits of seconds.</summary>
        private const float TimeColumn = 60f;

        /// <summary>How far a step or definition is indented under the phase it belongs to.</summary>
        private const float StepIndent = 12f;

        /// <summary>Arbitrary and only has to be stable and unlike anything vanilla uses.</summary>
        private const int WindowId = 0x4C_4F_41_44;

        /// <summary>Width of the timings column when it is showing.</summary>
        private const float TimingsWidth = 300f;

        private static Vector2 scroll;
        private static Vector2 detailScroll;
        private static Vector2 searchScroll;
        private static Vector2 timingsScroll;

        /// <summary>
        /// One phase of the load and how long it lasted.
        ///
        /// <b>Measured across to the next heading rather than taken from the entry.</b> An entry's own
        /// <c>Duration</c> is how long that line stayed on the loading screen before another replaced it, which
        /// for a heading is the time until its first child rather than the time the whole phase took. The number
        /// worth reading is the span from one heading to the next, which is what this holds.
        /// </summary>
        private struct CategorySpan
        {
            public string Key;
            public string Name;
            public UILoadingLogKind Kind;
            public float Seconds;
            public float Duration;

            /// <summary>Lines underneath it, which is what collapsing hides.</summary>
            public int Children;
        }

        private static List<CategorySpan> categories = new List<CategorySpan>();

        /// <summary>
        /// One line of the overview, with its part already decided.
        ///
        /// <b>The panel used to render the log's raw sequence and hope structure emerged from it.</b> It did not:
        /// every row was the same kind of thing, so a phase looked like a step, a phase that vanilla runs three
        /// times appeared three times, and the mods inside a phase were indistinguishable from the profiler
        /// noise around them. Deciding what each row <i>is</i> here, once, is what lets the drawing code give a
        /// heading its weight and a child its place without guessing from the entry's kind.
        /// </summary>
        private struct ConsoleRow
        {
            /// <summary>Fold identity. Headers only.</summary>
            public string Key;

            public string Text;

            /// <summary>When it first ran.</summary>
            public float Seconds;

            /// <summary>Total across every occurrence.</summary>
            public float Duration;

            /// <summary>How many times this phase ran during the load. One for most.</summary>
            public int Occurrences;

            /// <summary>Lines underneath it, whether or not they are showing.</summary>
            public int Children;

            public bool Header;

            public UILoadingLogKind Kind;

            /// <summary>The entry behind this row, for the detail pane.</summary>
            public UILoadingLogEntry Entry;
        }

        private static List<ConsoleRow> rows = new List<ConsoleRow>();

        /// <summary>
        /// Headings the reader has folded away, by <see cref="KeyOf"/>.
        ///
        /// Keyed by content rather than by index, because the visible list is rebuilt whenever the filter or the
        /// search changes and an index would fold a different heading the moment somebody typed.
        /// </summary>
        private static readonly HashSet<string> collapsed = new HashSet<string>();

        /// <summary>Bumped on every fold, so the cached visible list knows it is stale.</summary>
        private static int collapsedVersion;

        /// <summary>
        /// On by default: where the time went is the question the console is usually opened to answer, and a
        /// panel that has to be switched on first is one most readers never find.
        /// </summary>
        private static bool showTimings = true;

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
        private static int cachedCollapsed = -1;

        /// <summary>Timestamp of the last line recorded, which is when the load actually ended.</summary>
        private static float logSeconds;

        /// <summary>
        /// Headings by key, so a row can find its own duration and child count while drawing.
        ///
        /// A row knows what it says and when it happened; how long its phase lasted is measured across to the
        /// next heading and therefore lives with the spans. Looking it up here beats threading it onto every
        /// entry, which would cost the whole log a field that only headings use.
        /// </summary>
        private static Dictionary<string, CategorySpan> spanByKey = new Dictionary<string, CategorySpan>();

        /// <summary>The longest phase, which is both the verdict and the scale every bar is drawn against.</summary>
        private static CategorySpan slowest;

        private static int errorCount;
        private static int warningCount;

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
            categories = new List<CategorySpan>();
            collapsed.Clear();
            collapsedVersion++;
            cachedSourceCount = -1;
            scroll = Vector2.zero;
            detailScroll = Vector2.zero;
            searchScroll = Vector2.zero;
            timingsScroll = Vector2.zero;
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
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rebuild();

            Rect header = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, HeaderHeight);
            Rect verdict = new Rect(rect.x + 6f, header.yMax + 2f, rect.width - 12f, VerdictHeight);
            Rect toolbar = new Rect(rect.x + 8f, verdict.yMax + 6f, rect.width - 16f, ToolbarHeight);
            Rect footer = new Rect(rect.x + 8f, rect.yMax - FooterHeight, rect.width - 16f, FooterHeight - 4f);

            // The detail pane only takes room when there is something in it, so an unselected console is the same
            // list it was before selection existed rather than one permanently shorter by a blank panel. A file
            // search needs more room than one entry's text, so it gets more.
            bool searching = XmlFileSearch.State != XmlSearchState.Idle;
            float detail = searching ? SearchHeight : selected.HasValue ? DetailHeight : 0f;

            Rect body = new Rect(rect.x + 4f, toolbar.yMax + 4f, rect.width - 8f,
                Mathf.Max(0f, footer.y - toolbar.yMax - 12f - detail));

            DrawHeader(header, palette);
            DrawVerdict(verdict, palette);
            DrawToolbar(toolbar, palette);

            // The timings take a column off the right of the list rather than a strip off the bottom, because
            // both are lists and reading two side by side is how you match a slow phase to what happened in it.
            if (showTimings && body.width > TimingsWidth + 200f)
            {
                Rect times = new Rect(body.xMax - TimingsWidth, body.y, TimingsWidth, body.height);

                DrawRows(new Rect(body.x, body.y, body.width - TimingsWidth - 6f, body.height), palette);
                DrawTimings(times, palette);
            }
            else
            {
                DrawRows(body, palette);
            }

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

            if (count == cachedSourceCount && search == cachedSearch && filter == cachedFilter
                && collapsedVersion == cachedCollapsed)
                return;

            cachedSourceCount = count;
            cachedSearch = search;
            cachedFilter = filter;
            cachedCollapsed = collapsedVersion;

            List<UILoadingLogEntry> all = UILoadingLog.Snapshot();
            List<UILoadingLogEntry> shown = new List<UILoadingLogEntry>(Mathf.Min(all.Count, 512));

            // When the load stopped, as opposed to what time it is now. Recording stays live until a game
            // starts, so the stopwatch keeps counting while somebody reads this panel on the main menu.
            logSeconds = all.Count > 0 ? all[all.Count - 1].Seconds : 0f;

            BuildCategories(all);

            errorCount = 0;
            warningCount = 0;

            foreach (UILoadingLogEntry entry in all)
            {
                // Counted from the whole log rather than from what is on screen, since the verdict has to say
                // how many problems the load had, not how many the current filter admits.
                if (entry.Kind == UILoadingLogKind.Error)
                    errorCount++;
                else if (entry.Kind == UILoadingLogKind.Warning)
                    warningCount++;
            }

            BuildRows(all);

            foreach (ConsoleRow row in rows)
                shown.Add(row.Entry);

            visible = shown;
        }

        /// <summary>
        /// One phase of the load while the model is being assembled.
        ///
        /// A class rather than a struct because it is looked up by name and added to repeatedly; a struct would
        /// have to be written back into its list on every touch.
        /// </summary>
        private class PhaseGroup
        {
            public string Name;
            public float Seconds;
            public float Duration;
            public int Occurrences;
            public int TotalChildren;
            public UILoadingLogKind Kind;
            public UILoadingLogEntry Entry;

            public readonly List<ConsoleRow> Children = new List<ConsoleRow>();
            public readonly Dictionary<string, int> ChildIndex = new Dictionary<string, int>();
        }

        /// <summary>
        /// Turns the log into the overview: phases merged by identity, their contents merged underneath them.
        ///
        /// <b>Merged by name, and that is the point of the exercise.</b> RimWorld resolves references three
        /// times, binds DefOfs twice and drains deferred work four times, so a panel listing occurrences shows
        /// the same six phase names over and over and leaves the reader to add them up. One row per phase with
        /// its total and how many times it ran is the same information, organized.
        ///
        /// Children are merged the same way, so a hundred separate "loading X" lines under one phase collapse to
        /// the handful of distinct things that actually happened, each carrying its own total.
        ///
        /// The search box narrows the children rather than the phases, so searching keeps the shape of the load
        /// instead of returning a flat list of hits with no context.
        /// </summary>
        private static void BuildRows(List<UILoadingLogEntry> all)
        {
            List<PhaseGroup> groups = new List<PhaseGroup>();
            Dictionary<string, int> byName = new Dictionary<string, int>();

            PhaseGroup current = null;

            foreach (UILoadingLogEntry entry in all)
            {
                if (IsHeading(entry))
                {
                    int index;

                    if (!byName.TryGetValue(entry.Text ?? string.Empty, out index))
                    {
                        index = groups.Count;
                        byName[entry.Text ?? string.Empty] = index;

                        groups.Add(new PhaseGroup
                        {
                            Name = entry.Text,
                            Seconds = entry.Seconds,
                            Kind = entry.Kind,
                            Entry = entry
                        });
                    }

                    current = groups[index];
                    current.Occurrences++;

                    CategorySpan span;

                    if (spanByKey.TryGetValue(KeyOf(entry), out span))
                        current.Duration += span.Duration;

                    continue;
                }

                if (current == null)
                    continue;

                current.TotalChildren++;

                if (!PassesFilter(entry))
                    continue;

                // The path is searched as well as the text, which is most of why the path is worth keeping: the
                // useful question is usually "what came out of this file", not "what is this def called".
                if (!Search.IsEmpty && !Search.Matches(entry.Text) && !Search.Matches(entry.Path))
                    continue;

                MergeChild(current, entry);
            }

            Flatten(groups);
        }

        private static void MergeChild(PhaseGroup group, UILoadingLogEntry entry)
        {
            string text = entry.Text ?? string.Empty;
            int index;

            if (group.ChildIndex.TryGetValue(text, out index))
            {
                ConsoleRow existing = group.Children[index];
                existing.Duration += entry.Duration;
                existing.Occurrences++;
                group.Children[index] = existing;

                return;
            }

            group.ChildIndex[text] = group.Children.Count;

            group.Children.Add(new ConsoleRow
            {
                Text = text,
                Seconds = entry.Seconds,
                Duration = entry.Duration,
                Occurrences = 1,
                Kind = entry.Kind,
                Entry = entry
            });
        }

        /// <summary>Lays the groups out as rows, honoring which of them are folded.</summary>
        private static void Flatten(List<PhaseGroup> groups)
        {
            List<ConsoleRow> built = new List<ConsoleRow>(groups.Count * 4);

            // <b>Measured on the merged totals, not on single occurrences.</b> The verdict and the bars used to
            // take their scale from the largest individual run of a phase, while the timings panel showed the
            // merged total -- so the same phase read 30.04s at the top of the panel and 39.02s down the side.
            // Both figures were true and the panel was still telling the reader two different things.
            CategorySpan longest = default(CategorySpan);

            foreach (PhaseGroup group in groups)
            {
                string key = group.Name ?? string.Empty;

                if (group.Duration > longest.Duration)
                {
                    longest = new CategorySpan
                    {
                        Key = key,
                        Name = group.Name,
                        Kind = group.Kind,
                        Seconds = group.Seconds,
                        Duration = group.Duration,
                        Children = group.TotalChildren
                    };
                }

                built.Add(new ConsoleRow
                {
                    Key = key,
                    Text = group.Name,
                    Seconds = group.Seconds,
                    Duration = group.Duration,
                    Occurrences = group.Occurrences,
                    Children = group.TotalChildren,
                    Header = true,
                    Kind = group.Kind,
                    Entry = group.Entry
                });

                if (collapsed.Contains(key))
                    continue;

                foreach (ConsoleRow child in group.Children)
                    built.Add(child);
            }

            rows = built;
            slowest = longest;
        }

        /// <summary>
        /// A stable identity for a heading.
        ///
        /// The timestamp is in it because a phase name repeats -- a second load runs the same phases -- and
        /// folding one occurrence should not fold the other.
        /// </summary>
        /// <summary>
        /// Joins the parts of a key. A control character, so it cannot occur in a phase name and accidentally
        /// make two different headings share an identity.
        /// </summary>
        private const string KeySeparator = "\u0001";

        private static string KeyOf(UILoadingLogEntry entry)
        {
            return (int) entry.Kind + KeySeparator + entry.Seconds.ToString("F3") + KeySeparator + entry.Text;
        }

        private static bool IsHeading(UILoadingLogEntry entry)
        {
            return entry.Kind == UILoadingLogKind.Section || entry.Kind == UILoadingLogKind.Stage;
        }

        /// <summary>
        /// Every heading with the time from it to the next one, for the timings panel.
        ///
        /// Built from the whole log rather than the visible list, so folding a phase away does not remove it from
        /// the panel that says how long it took -- which would defeat the point of having both.
        /// </summary>
        private static void BuildCategories(List<UILoadingLogEntry> all)
        {
            List<CategorySpan> spans = new List<CategorySpan>();

            for (int i = 0; i < all.Count; i++)
            {
                UILoadingLogEntry entry = all[i];

                if (!IsHeading(entry))
                {
                    if (spans.Count > 0)
                    {
                        CategorySpan open = spans[spans.Count - 1];
                        open.Children++;
                        spans[spans.Count - 1] = open;
                    }

                    continue;
                }

                if (spans.Count > 0)
                {
                    // Closed against this heading's timestamp, which is the moment the previous one stopped.
                    CategorySpan previous = spans[spans.Count - 1];
                    previous.Duration = Mathf.Max(0f, entry.Seconds - previous.Seconds);
                    spans[spans.Count - 1] = previous;
                }

                spans.Add(new CategorySpan
                {
                    Key = KeyOf(entry),
                    Name = entry.Text,
                    Kind = entry.Kind,
                    Seconds = entry.Seconds
                });
            }

            // Indexed here so a row can find its own span while drawing. Ranking happens after merging, in
            // Flatten, since the figure worth ranking is a phase's total rather than its longest single run.
            Dictionary<string, CategorySpan> index = new Dictionary<string, CategorySpan>(spans.Count);

            if (spans.Count > 0)
            {
                // <b>Closed against the last thing recorded, not against the clock.</b> The stopwatch keeps
                // running until a game starts, so measuring the final phase against it made that one row grow
                // for as long as the menu was open -- and since the panel is rebuilt whenever a heading is
                // folded, the number visibly jumped on every click. The load ended when the last line was
                // written; that is the honest end of the last phase.
                CategorySpan last = spans[spans.Count - 1];
                last.Duration = Mathf.Max(0f, logSeconds - last.Seconds);
                spans[spans.Count - 1] = last;
            }

            foreach (CategorySpan span in spans)
                index[span.Key] = span;

            categories = spans;
            spanByKey = index;
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
                    if (entry.IsProblem)
                        return true;

                    // Headings are the skeleton of the overview and are always in it, however fast they were.
                    if (IsHeading(entry))
                        return true;

                    // <b>Everything else has to have cost something.</b> This is the rule the panel never had.
                    // RimWorld profiles its own work in great detail -- ResolveAllCrossReferences,
                    // DoAllPostLoadInits, Parse loaded defs -- and each of those labels becomes a step, so an
                    // overview that admitted all of them ran to a hundred and seventy thousand rows describing a
                    // load whose interesting parts number about thirty. A step earns its place here by having
                    // taken measurable time; the rest are still in All, which is what All is for.
                    return entry.Kind == UILoadingLogKind.Step && entry.Duration >= OverviewThreshold;

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

                // Same reason as the last phase span: the clock runs until a game starts, so reporting it here
                // would tell the reader their load took as long as they have been sitting on the main menu.
                Widgets.Label(rect, counts + ", " + UILoadingLog.Duration(logSeconds));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The four figures worth having before reading anything: what was slowest, how long the whole thing
        /// took, how many problems there were, and how much was recorded.
        ///
        /// <b>Every one of these already existed and none of them were readable.</b> The slowest phase was the
        /// top row of a panel that had to be switched on and sorted; the total sat in the corner of the header;
        /// the problem count was a badge on a tab. Assembling the headline finding took three glances and a
        /// click, for a panel whose entire purpose is answering one question quickly.
        ///
        /// The slowest phase leads and is sized largest because it is the answer. On the load this was built
        /// against it reads 140.96s against a 191s total, and the point of the strip is that this is the first
        /// thing seen rather than something arrived at.
        /// </summary>
        private static void DrawVerdict(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            float lead = Mathf.Max(180f, rect.width * 0.36f);
            float rest = Mathf.Max(90f, (rect.width - lead) / 3f);

            float share = logSeconds > 0.01f ? slowest.Duration / logSeconds : 0f;

            Cell(new Rect(rect.x, rect.y, lead, rect.height), "Slowest phase",
                UILoadingLog.Duration(slowest.Duration),
                slowest.Name.NullOrEmpty()
                    ? "nothing recorded yet"
                    : slowest.Name + "  -  " + Mathf.RoundToInt(share * 100f) + "% of the load",
                share >= 0.4f ? palette.Danger : share >= 0.2f ? palette.Warning : palette.TextPrimary,
                palette);

            Cell(new Rect(rect.x + lead, rect.y, rest, rect.height), "Total",
                UILoadingLog.Duration(logSeconds), categories.Count + " phases", palette.TextPrimary, palette);

            int problems = errorCount + warningCount;

            Cell(new Rect(rect.x + lead + rest, rect.y, rest, rect.height), "Problems",
                problems.ToString(),
                errorCount + " errors, " + warningCount + " warnings",
                errorCount > 0 ? palette.Danger : problems > 0 ? palette.Warning : palette.TextPrimary,
                palette);

            Cell(new Rect(rect.x + lead + rest * 2f, rect.y, rest, rect.height), "Recorded",
                Compact(UILoadingLog.Count), "lines kept", palette.TextPrimary, palette);

            // Dividers drawn after the cells so they sit on top of the fill rather than under the next one.
            Color previousColor = GUI.color;
            GUI.color = palette.Border;

            for (int i = 1; i <= 3; i++)
            {
                float x = rect.x + lead + rest * (i - 1);
                Widgets.DrawLineVertical(x, rect.y + 6f, rect.height - 12f);
            }

            GUI.color = previousColor;
        }

        private static void Cell(Rect rect, string label, string value, string sub, Color tint,
            UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleLeft;

                Rect inner = new Rect(rect.x + 10f, rect.y + 5f, Mathf.Max(0f, rect.width - 16f),
                    rect.height - 10f);

                float line = inner.height / 3f;

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, line), label.ToUpperInvariant());

                Text.Font = GameFont.Small;
                GUI.color = tint;
                Widgets.Label(new Rect(inner.x, inner.y + line - 2f, inner.width, line + 4f), value);

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextSecondary;
                Widgets.LabelEllipses(new Rect(inner.x, inner.y + line * 2f + 2f, inner.width, line), sub);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>198069 as "198k". A six digit count in a figure this size is unreadable and unhelpful.</summary>
        private static string Compact(int count)
        {
            if (count >= 1000000)
                return (count / 1000000f).ToString("F1") + "m";

            return count >= 1000 ? (count / 1000) + "k" : count.ToString();
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

            Rect times = new Rect(x + 4f, rect.y, 72f, rect.height);

            if (Tab(times, "Timings", showTimings, palette, -1))
            {
                showTimings = !showTimings;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Rect fold = new Rect(times.xMax + 4f, rect.y, 72f, rect.height);

            // One button rather than two, because the useful action changes with the state: with everything open
            // you want it shut, and once it is shut the only thing you want is it open again.
            if (Tab(fold, collapsed.Count > 0 ? "Expand" : "Collapse", false, palette, -1))
            {
                ToggleAll(collapsed.Count == 0);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            x = fold.xMax;

            Search.Draw(new Rect(x + 4f, rect.y, Mathf.Max(60f, rect.xMax - x - 4f), rect.height), palette);
        }

        /// <summary>Folds or unfolds every heading at once.</summary>
        private static void ToggleAll(bool fold)
        {
            collapsed.Clear();

            if (fold)
            {
                foreach (ConsoleRow row in rows)
                {
                    // Keyed by phase name, matching what the fold marker on each header uses. Folding is now a
                    // property of the phase rather than of one occurrence of it, so folding "Resolving
                    // references" folds all three runs of it at once.
                    if (row.Header && row.Kind == UILoadingLogKind.Stage)
                        collapsed.Add(row.Key);
                }
            }

            collapsedVersion++;
            scroll = Vector2.zero;
            pinnedToEnd = false;
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
            // Counted during the rebuild rather than by walking the whole log here. This is called once per tab
            // per frame, and on a two hundred thousand line load that was a full scan sixty times a second.
            return option == LoadingConsoleFilter.Problems ? errorCount + warningCount : -1;
        }

        /// <summary>
        /// One segment of a filter control.
        ///
        /// <b>The selected segment is filled with the accent, not tinted by it.</b> The previous version drew
        /// every tab as an outlined box and marked the current one with a muted wash and a brighter border,
        /// which at this size is two nearly identical grey rectangles: which filter is active took a moment to
        /// work out on a panel where it should be immediate. A solid fill with dark text is unmistakable at a
        /// glance and is what the mockup called for.
        /// </summary>
        private static bool Tab(Rect rect, string label, bool chosen, UIColorPaletteDef palette, int count)
        {
            bool over = Mouse.IsOver(rect);

            // Unselected sits on the panel surface with a border, not on ControlBackgroundFaded. That colour is
            // the palette's word for "cannot be used", so a segmented control drawn in it reads as disabled
            // rather than as the other half of a choice.
            if (chosen)
                UIElementPainter.FillRounded(rect, palette.Accent);
            else
                UIElementPainter.OutlineRounded(rect, palette.Border,
                    over ? palette.SurfaceRaised : palette.PanelBackground);

            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Dark text on the accent, because the accent is a light blue and light-on-light is the one
            // combination this palette cannot carry. Full strength text when unselected, since dimmed text is
            // the other half of what makes a control look unavailable.
            GUI.color = chosen ? palette.WindowBackground : palette.TextPrimary;

            // A tab counting something with nothing in it is drawn quiet rather than absent, so "no problems"
            // reads as an answer instead of a missing feature.
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
            float contentHeight = rows.Count * rowHeight;

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
                int last = Mathf.Min(rows.Count, first + Mathf.CeilToInt(rect.height / rowHeight) + 2);

                for (int i = first; i < last; i++)
                    DrawModelRow(new Rect(0f, i * rowHeight, view.width, rowHeight), rows[i], palette);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;

                // <b>Inside the finally, with the state restoration.</b> A throw between Begin and End leaves
                // Unity's clip and mouse-position stacks unbalanced, and the game then reports "more calls to
                // BeginScrollView than EndScrollView" and repairs them -- but the damage lands on everything
                // drawn afterwards, not only the panel that threw. One bad row must not be able to break the
                // rest of the frame.
                Widgets.EndScrollView();
            }

            // Measured after the view, so it reflects wherever the wheel or the bar just left it. The tolerance
            // is a row, so landing a pixel short of the bottom still counts as being at it.
            pinnedToEnd = scroll.y >= contentHeight - rect.height - rowHeight;
        }

        /// <summary>
        /// One row of the model.
        ///
        /// <b>A header and a child are drawn as different things,</b> which the old version could not do because
        /// every row was simply an entry and the code had to infer a role from its kind. A header is white and
        /// heavy with its total on the right; a child is dimmer, indented against a rail, and carries its own
        /// count when it happened more than once. That difference is what makes the panel scannable.
        /// </summary>
        private static void DrawModelRow(Rect rect, ConsoleRow row, UIColorPaletteDef palette)
        {
            bool chosen = selected.HasValue && selected.Value.Seconds == row.Entry.Seconds
                                            && selected.Value.Text == row.Entry.Text;

            if (row.Duration > OverviewThreshold)
            {
                float scale = Mathf.Max(0.01f, slowest.Duration);
                float width = Mathf.Clamp01(row.Duration / scale) * rect.width;

                Color fill = row.Duration >= slowest.Duration * 0.25f ? palette.Danger : palette.Accent;
                fill.a = row.Header ? 0.20f : 0.12f;

                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, Mathf.Max(2f, width), rect.height), fill);
            }

            if (chosen)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
            else if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;
            Widgets.Label(new Rect(rect.x, rect.y, TimeColumn, rect.height), row.Seconds.ToString("F2"));

            Text.Anchor = TextAnchor.MiddleLeft;

            float x = rect.x + TimeColumn + 6f;
            Rect twist = Rect.zero;

            if (row.Header)
            {
                twist = new Rect(x, rect.y, 14f, rect.height);
                x += 16f;

                GUI.color = Mouse.IsOver(twist) ? palette.TextPrimary : palette.TextSecondary;
                Widgets.DrawTextureFitted(twist.ContractedBy(2f),
                    collapsed.Contains(row.Key) ? TexButton.Reveal : TexButton.Collapse, 1f);
            }
            else
            {
                GUI.color = palette.Border;
                Widgets.DrawLineVertical(rect.x + TimeColumn + 12f, rect.y, rect.height);
                x += StepIndent + 10f;
            }

            bool problem = row.Kind == UILoadingLogKind.Error || row.Kind == UILoadingLogKind.Warning;

            if (problem)
            {
                // The shared badge. This panel is where the look was worked out, and it moved to
                // UITagControl once a third caller wanted it; the reasoning lives there now.
                string severityLabel = row.Kind == UILoadingLogKind.Error ? "ERROR" : "WARN";
                Color severity = row.Kind == UILoadingLogKind.Error ? palette.Danger : palette.Warning;

                x = UITagControl.DrawLeading(new Rect(x, rect.y, rect.width, rect.height), severityLabel,
                    severity, palette);

                Text.Anchor = TextAnchor.MiddleLeft;
            }

            string label = row.Header ? row.Text : UILoadingLog.FirstLine(row.Text);

            // How many times it happened, when that is more than once. A phase RimWorld runs three times is one
            // row saying so, rather than three rows the reader has to notice are the same.
            if (row.Occurrences > 1)
                label += "  x" + row.Occurrences;

            GUI.color = problem
                ? (row.Kind == UILoadingLogKind.Error ? palette.Danger : palette.Warning)
                : row.Header
                    ? palette.TextPrimary
                    : palette.TextSecondary;

            float rightWidth = row.Duration >= OverviewThreshold ? 62f : 0f;
            float pill = row.Header && collapsed.Contains(row.Key) && row.Children > 0
                ? 20f + row.Children.ToString().Length * 6f
                : 0f;

            Widgets.LabelEllipses(new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - x - rightWidth - pill - 8f),
                rect.height), label);

            if (pill > 0f)
            {
                float labelWidth = Mathf.Min(Text.CalcSize(label).x,
                    Mathf.Max(0f, rect.xMax - x - rightWidth - pill - 8f));

                Rect chip = new Rect(x + labelWidth + 6f, rect.y + 1f, pill, rect.height - 2f);

                UIElementPainter.OutlineRounded(chip, palette.Border, palette.SurfaceSunken);

                GUI.color = palette.TextSecondary;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(chip, row.Children.ToString());
                Text.Anchor = TextAnchor.MiddleLeft;
            }

            if (rightWidth > 0f)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = row.Duration >= 10f ? palette.Danger
                    : row.Duration >= 1f ? palette.Warning
                    : palette.TextDisabled;

                Widgets.Label(new Rect(rect.xMax - rightWidth, rect.y, rightWidth, rect.height),
                    UILoadingLog.Duration(row.Duration));

                Text.Anchor = TextAnchor.MiddleLeft;
            }

            if (row.Header && Widgets.ButtonInvisible(twist))
            {
                Fold(row.Key);
                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            if (Widgets.ButtonInvisible(rect))
            {
                selected = chosen ? (UILoadingLogEntry?) null : row.Entry;
                detailScroll = Vector2.zero;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (!Mouse.IsOver(rect))
                return;

            string tip = row.Text;

            if (row.Occurrences > 1)
                tip += "\n\nRan " + row.Occurrences + " times, " + UILoadingLog.Duration(row.Duration) + " total.";

            if (!row.Entry.Path.NullOrEmpty())
                tip += "\n\n" + row.Entry.Path;

            TooltipHandler.TipRegion(rect, (TipSignal) tip);
        }

        private static void Fold(string key)
        {
            if (!collapsed.Remove(key))
                collapsed.Add(key);

            collapsedVersion++;
            pinnedToEnd = false;
        }

        /// <summary>
        /// Every phase of the load and what it cost, beside the list rather than inside it.
        ///
        /// <b>This answers a question the list structurally cannot.</b> The list is in load order, one line per
        /// event, which is right for "what happened" and useless for "where did the time go" -- the phase that
        /// took ninety seconds looks exactly like the one that took nothing. Here each heading carries its span
        /// to the next, sorted longest first, so the answer is the top row.
        ///
        /// Clicking a row scrolls the list to that phase, which is the natural next question once the panel has
        /// said which one to look at.
        /// </summary>
        private static void DrawTimings(Rect rect, UIColorPaletteDef palette)
        {
            // Raised, not sunken. This is content sitting on the panel, and in this palette elevation reads as
            // lighter: SurfaceSunken is two steps below the window and belongs to empty sockets and input wells
            // -- text boxes, vacant bar slots, the tab behind the panel. Painting a reading surface with it put
            // the inside of the console darker than the console.
            Color previousColor = GUI.color;
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(6f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;

                // Taller than a list row, because each entry is now a line of text with a bar under it.
                float lineHeight = UIFonts.LineHeightOf(GameFont.Tiny) + RowPad + 6f;

                // An uppercase mono label and a line saying what the order means, matching the verdict strip
                // above it. "Time by phase" alone did not say it was sorted, so the first row being the worst
                // offender read as a coincidence.
                GUI.color = palette.TextDisabled;
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, lineHeight), "WHERE THE TIME WENT");

                GUI.color = palette.TextSecondary;
                Widgets.Label(new Rect(inner.x, inner.y + lineHeight - 6f, inner.width, lineHeight),
                    "Longest first. Click to jump.");

                // <b>Built from the same merged model the list uses,</b> so a phase RimWorld runs three times is
                // one entry with its total here as well. The old version listed every occurrence separately,
                // which put "Resolving references" in the panel three times with three partial figures and no
                // way to see what the phase actually cost.
                List<CategorySpan> sorted = new List<CategorySpan>(rows.Count);

                foreach (ConsoleRow row in rows)
                {
                    if (!row.Header)
                        continue;

                    sorted.Add(new CategorySpan
                    {
                        Key = row.Key,
                        Name = row.Text,
                        Kind = row.Kind,
                        Seconds = row.Seconds,
                        Duration = row.Duration,
                        Children = row.Children
                    });
                }

                sorted.Sort((a, b) => b.Duration.CompareTo(a.Duration));

                Rect list = new Rect(inner.x, inner.y + lineHeight * 2f - 2f, inner.width,
                    Mathf.Max(0f, inner.height - lineHeight * 2f + 2f));

                Rect view = new Rect(0f, 0f, list.width - 18f, sorted.Count * lineHeight);

                Widgets.BeginScrollView(list, ref timingsScroll, view);

                try
                {
                    float longest = sorted.Count > 0 ? Mathf.Max(0.001f, sorted[0].Duration) : 1f;

                    for (int i = 0; i < sorted.Count; i++)
                        DrawTiming(new Rect(0f, i * lineHeight, view.width, lineHeight), sorted[i], palette,
                            longest);
                }
                finally
                {
                    Widgets.EndScrollView();
                }
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static void DrawTiming(Rect rect, CategorySpan span, UIColorPaletteDef palette, float longest)
        {
            float share = Mathf.Clamp01(span.Duration / longest);

            if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            const float timeWidth = 54f;

            // <b>Name and figure on the first line, a real bar under them.</b> The first version washed the row
            // in AccentMuted at the bar's width, which on this palette is a couple of levels off the surface it
            // sits on: technically present and invisible in practice. A dark track with a bright fill is legible
            // at a glance, which is the only reason to draw a bar rather than print a number.
            float textHeight = rect.height - 5f;

            GUI.color = span.Kind == UILoadingLogKind.Section ? palette.Accent : palette.TextPrimary;

            Widgets.LabelEllipses(new Rect(rect.x + 2f, rect.y, rect.width - timeWidth - 6f, textHeight),
                span.Name);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = span.Duration >= 10f ? palette.Danger
                : span.Duration >= 1f ? palette.Warning
                : palette.TextSecondary;

            Widgets.Label(new Rect(rect.xMax - timeWidth, rect.y, timeWidth, textHeight),
                UILoadingLog.Duration(span.Duration));

            Text.Anchor = TextAnchor.MiddleLeft;

            Rect track = new Rect(rect.x + 2f, rect.y + textHeight, rect.width - 4f, 3f);

            Widgets.DrawBoxSolid(track, palette.SurfaceSunken);
            Widgets.DrawBoxSolid(new Rect(track.x, track.y, Mathf.Max(1f, track.width * share), track.height),
                span.Duration >= longest * 0.5f ? palette.Danger : palette.Accent);

            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, (TipSignal)
                    (span.Name + "\n\nStarted at " + span.Seconds.ToString("F2") + "s, took "
                     + UILoadingLog.Duration(span.Duration) + "\n" + span.Children
                     + " line(s)\n\nClick to jump to it in the list."));

            if (!Widgets.ButtonInvisible(rect))
                return;

            Jump(span);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>Scrolls the list to a phase, unfolding it first if it is folded away.</summary>
        private static void Jump(CategorySpan span)
        {
            // Keyed by name, like everything else about folding now.
            if (collapsed.Remove(span.Name ?? string.Empty))
            {
                collapsedVersion++;
                Rebuild();
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].Header || rows[i].Key != (span.Name ?? string.Empty))
                    continue;

                scroll.y = i * (UIFonts.LineHeightOf(GameFont.Tiny) + RowPad);
                pinnedToEnd = false;

                return;
            }
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
            // Raised, not sunken. This is content sitting on the panel, and in this palette elevation reads as
            // lighter: SurfaceSunken is two steps below the window and belongs to empty sockets and input wells
            // -- text boxes, vacant bar slots, the tab behind the panel. Painting a reading surface with it put
            // the inside of the console darker than the console.
            Color previousColor = GUI.color;
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

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

                try
                {
                    Widgets.Label(view, entry.Text);
                }
                finally
                {
                    Widgets.EndScrollView();
                }
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
            // Raised, not sunken. This is content sitting on the panel, and in this palette elevation reads as
            // lighter: SurfaceSunken is two steps below the window and belongs to empty sockets and input wells
            // -- text boxes, vacant bar slots, the tab behind the panel. Painting a reading surface with it put
            // the inside of the console darker than the console.
            Color previousColor = GUI.color;
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

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

                try
                {
                    int first = Mathf.Max(0, Mathf.FloorToInt(searchScroll.y / rowHeight) - 1);
                    int last = Mathf.Min(results.Count, first + Mathf.CeilToInt(list.height / rowHeight) + 2);

                    for (int i = first; i < last; i++)
                        DrawHit(new Rect(0f, i * rowHeight, view.width, rowHeight), results[i], palette,
                            lineHeight);
                }
                finally
                {
                    Widgets.EndScrollView();
                }
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

            // Through FirstLine rather than drawn raw, because this is a line lifted out of somebody's XML and
            // nothing bounds how long that is: a minified def file is one enormous line. LabelEllipses shortens
            // by removing a character at a time and remeasuring, so an unbounded string here freezes the game
            // rather than merely overflowing the row. See UILoadingLog.MaxRowChars.
            Widgets.LabelEllipses(new Rect(rect.x + 14f, rect.y + lineHeight, rect.width - 16f, lineHeight),
                UILoadingLog.FirstLine(hit.Text));

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

            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;

            // How much of the log is on screen, which is the question the filters and the search box raise and
            // nothing else answered. The dropped-lines warning takes precedence when there is one, since that
            // says the log is incomplete and outranks saying how much of it is showing.
            GUI.color = dropped > 0 ? palette.Warning : palette.TextDisabled;

            string status = dropped > 0
                ? dropped + " lines not kept"
                : visible.Count + " of " + UILoadingLog.Count + " shown";

            Widgets.Label(new Rect(clear.xMax + 6f, rect.y, Mathf.Max(0f, rect.xMax - clear.xMax - 6f),
                rect.height), status);

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
            return UIActionButtonControl.Draw(rect, label, palette, false, true,
                GameFont.Tiny);
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
