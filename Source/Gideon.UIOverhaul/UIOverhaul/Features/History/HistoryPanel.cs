using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.History
{
    /// <summary>
    /// The history tab: one screen, one time axis, four things to look at.
    ///
    /// <b>What vanilla has is three pages that each know a third of the same story.</b> The Graph page knows
    /// when things happened, because it already turns every permanent tale into a <c>CurveMark</c> on a day
    /// axis. The Messages page knows what happened, because every archived letter carries
    /// <c>CreatedTicksGame</c> -- the same axis. The Statistics page knows the totals. They are separated by a
    /// <c>TabDrawer</c> row, so the wealth cliff and the siege that caused it can never be seen together.
    ///
    /// <b>The rail replaces both the tab row and the float menu.</b> Choosing a graph was a
    /// <c>Widgets.ButtonText</c> labelled "Select graph" in every state, opening a menu of three; the rail shows
    /// the three, says which one is current, and carries each one's latest value so the choice is informative
    /// before it is made.
    ///
    /// <b>A fourth view the tab never had.</b> <c>Find.BattleLog</c> keeps a blow by blow record of the last
    /// twenty fights, reachable today only through one colonist's character card at a time. This screen is where
    /// a player goes to ask what happened to their colony, so it is where that belongs.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class HistoryPanel
    {
        internal const float WindowWidth = 1280f;

        internal const float WindowHeight = 720f;

        private const float HeaderHeight = 62f;

        private const float GlyphSize = 30f;

        private const float GlyphGap = 10f;

        private const float RailWidth = 190f;

        private const float Gap = 6f;

        private const float ControlHeight = 26f;

        private const float DetailWidth = 380f;

        private const float RowHeight = 30f;

        private const float BattleRowHeight = 38f;

        private const string ArchiveKey = "archive";

        private const string BattlesKey = "battles";

        private const string StatisticsKey = "stats";

        // -------------------------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------------------------

        private static string selected = null;

        /// <summary>How many days back the plot shows. Zero means the whole run.</summary>
        private static float span = 100f;

        private static Vector2 railScroll;

        private static bool railDragging;

        private static float railDragOffset;

        private static Vector2 listScroll;

        private static Vector2 detailScroll;

        private static IArchivable opened;

        private static Battle openedBattle;

        private static bool showLetters = true;

        private static bool showMessages;

        private static bool pinnedOnly;

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search the archive",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private static readonly List<UIRailElement> Rail = new List<UIRailElement>();

        /// <summary>The tab's own mark, the same texture its button on the bar uses.</summary>
        private static readonly Texture2D Glyph;

        /// <summary>
        /// Vanilla's own pin pair, loaded by path because <c>MainTabWindow_History</c> keeps them private.
        ///
        /// The same two textures the vanilla row draws, so a player who has learned what a pin looks like in
        /// this game does not have to learn a second shape. What changes is the color they are drawn in, not
        /// the drawing: vanilla tints the unpinned one <c>(0.25, 0.25, 0.25, 0.5)</c>, which is invisible.
        /// </summary>
        private static readonly Texture2D PinTex;

        private static readonly Texture2D PinOutlineTex;

        static HistoryPanel()
        {
            Texture2D glyph = null;
            Texture2D pin = null;
            Texture2D outline = null;

            UIGuard.Try("History.Glyph",
                () => glyph = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/History", false),
                "The header has no glyph this session. Everything on the tab still reads.");

            UIGuard.Try("History.PinTex", () =>
            {
                pin = ContentFinder<Texture2D>.Get("UI/Icons/Pin", false);
                outline = ContentFinder<Texture2D>.Get("UI/Icons/Pin-Outline", false);
            }, "The archive's pins are drawn as plain squares this session. Pinning still works.");

            Glyph = glyph;
            PinTex = pin;
            PinOutlineTex = outline;
        }

        /// <summary>
        /// Resets what should not survive a close, and forces a wealth recount the way vanilla's own
        /// <c>PreOpen</c> does, so the statistics card is not a season out of date.
        /// </summary>
        internal static void Notify_Opened()
        {
            listScroll = Vector2.zero;
            detailScroll = Vector2.zero;
            opened = null;
            openedBattle = null;

            Search.Clear();

            List<Map> maps = Find.Maps;

            for (int i = 0; maps != null && i < maps.Count; i++)
                maps[i]?.wealthWatcher?.ForceRecount();
        }

        // -------------------------------------------------------------------------------------------
        // Layout
        // -------------------------------------------------------------------------------------------

        internal static void Draw(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            List<HistoryAutoRecorderGroup> groups = HistoryFacts.Groups();

            EnsureSelection(groups);

            Rect content = inRect.ContractedBy(6f);
            Rect header = new Rect(content.x, content.y, content.width, HeaderHeight);

            Header(header, palette);

            float top = header.yMax + Gap;
            float height = content.yMax - top;

            BuildRail(groups, palette);

            Rect railRect = new Rect(content.x, top, RailWidth, height);
            string picked = UIRailControl.Draw(railRect, Rail, selected, ref railScroll, ref railDragging,
                ref railDragOffset, palette);

            if (!picked.NullOrEmpty() && picked != selected)
            {
                selected = picked;
                listScroll = Vector2.zero;
            }

            Rect view = new Rect(railRect.xMax + Gap, top, content.xMax - railRect.xMax - Gap, height);

            if (selected == ArchiveKey)
                ArchiveView(view, palette);
            else if (selected == BattlesKey)
                BattlesView(view, palette);
            else if (selected == StatisticsKey)
                StatisticsView(view, palette);
            else
                GraphView(view, groups, palette);
        }

        /// <summary>
        /// Keeps the selection on something that exists.
        ///
        /// A save whose mods changed can come back with the selected group gone, and a rail with nothing
        /// selected draws a view for a group that is null. Falling back to the first group is what vanilla's own
        /// <c>PreOpen</c> does with <c>Groups().FirstOrDefault()</c>.
        /// </summary>
        private static void EnsureSelection(List<HistoryAutoRecorderGroup> groups)
        {
            if (selected == ArchiveKey || selected == BattlesKey || selected == StatisticsKey)
                return;

            for (int i = 0; i < groups.Count; i++)
            {
                if (KeyOf(groups[i]) == selected)
                    return;
            }

            selected = groups.Count > 0 ? KeyOf(groups[0]) : ArchiveKey;
        }

        private static string KeyOf(HistoryAutoRecorderGroup group)
        {
            return group?.def == null ? null : "group:" + group.def.defName;
        }

        private static HistoryAutoRecorderGroup Current(List<HistoryAutoRecorderGroup> groups)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (KeyOf(groups[i]) == selected)
                    return groups[i];
            }

            return groups.Count > 0 ? groups[0] : null;
        }

        // -------------------------------------------------------------------------------------------
        // Header
        // -------------------------------------------------------------------------------------------

        private static void Header(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(10f);
            float text = inner.x;

            if (Glyph != null)
            {
                Rect mark = new Rect(inner.x, inner.y + (inner.height - GlyphSize) * 0.5f, GlyphSize, GlyphSize);
                Color previous = GUI.color;

                GUI.color = HistoryFaces.AccentOf(palette);
                GUI.DrawTexture(mark, Glyph);
                GUI.color = previous;

                text = mark.xMax + GlyphGap;
            }

            float wall = Readouts(inner, palette) - 12f;
            float width = Mathf.Max(0f, wall - text);

            TabParts.RowLabel(new Rect(text, inner.y, Mathf.Min(300f, width), 24f), "History",
                HistoryFaces.AccentOf(palette), GameFont.Medium, HistoryFaces.Display,
                HistoryFaces.Size.Title);

            TabParts.RowLabel(new Rect(text, inner.y + 23f, Mathf.Min(520f, width), 18f), Subtitle(),
                palette.TextSecondary, GameFont.Tiny, HistoryFaces.Condensed, HistoryFaces.Size.Subtitle);
        }

        /// <summary>The line under the title: who is telling this story, and how long it has been running.</summary>
        private static string Subtitle()
        {
            return UIGuard.Try("History.Subtitle", () =>
            {
                string where = Find.CurrentMap != null && Find.CurrentMap.Parent != null
                    ? Find.CurrentMap.Parent.LabelCap + "  -  "
                    : string.Empty;

                Storyteller teller = Find.Storyteller;

                return teller?.def == null
                    ? where + "day " + Mathf.FloorToInt(HistoryFacts.Today)
                    : where + "day " + Mathf.FloorToInt(HistoryFacts.Today) + "  -  " + teller.def.LabelCap
                      + ", " + teller.difficultyDef.LabelCap;
            }, "The colony so far", null);
        }

        /// <summary>The four figures, right to left. Returns the left edge of the leftmost.</summary>
        private static float Readouts(Rect area, UIColorPaletteDef palette)
        {
            float x = area.xMax;

            x = Readout(area, x, "mood", MoodFigure(), palette,
                "The colony's average mood, as the mood graph records it.");

            x = Readout(area, x, "colonists", ColonistFigure(), palette,
                "Free colonists on all maps.");

            x = Readout(area, x, "wealth", WealthFigure(), palette,
                "This map's total wealth, recounted when the tab opened.");

            return Readout(area, x, "day", Mathf.FloorToInt(HistoryFacts.Today).ToString(), palette,
                "Days since the colony started.");
        }

        private static string WealthFigure()
        {
            return UIGuard.Try("History.Wealth", () => Find.CurrentMap?.wealthWatcher == null
                ? "-"
                : HistoryFacts.ShortSilver(Find.CurrentMap.wealthWatcher.WealthTotal), "-", null);
        }

        private static string ColonistFigure()
        {
            return UIGuard.Try("History.Colonists", () =>
            {
                List<Pawn> colonists = PawnsFinder.AllMaps_FreeColonists;

                return colonists == null ? "-" : colonists.Count.ToString();
            }, "-", null);
        }

        /// <summary>
        /// Average mood, read from the recorder rather than recomputed.
        ///
        /// The mood group already samples exactly this every half day, so asking it costs one array lookup and
        /// cannot disagree with the graph two hundred pixels below.
        /// </summary>
        private static string MoodFigure()
        {
            return UIGuard.Try("History.Mood", () =>
            {
                List<HistoryAutoRecorderGroup> groups = HistoryFacts.Groups();

                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i].def == null || groups[i].def.defName != "ColonistMood")
                        continue;

                    List<HistorySeries> series = HistoryFacts.SeriesOf(groups[i]);

                    if (series.Count > 0)
                        return Mathf.RoundToInt(series[0].Latest) + "%";
                }

                return "-";
            }, "-", null);
        }

        /// <summary>One right-aligned caption under a figure, in the mono, returning the x the next ends at.</summary>
        private static float Readout(Rect bar, float right, string caption, string value,
            UIColorPaletteDef palette, string tip = null, Color? valueColor = null)
        {
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            bool wrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;

                float width = Mathf.Max(
                    UITextControl.Width(caption ?? string.Empty, HistoryFaces.Mono, HistoryFaces.Size.Caption),
                    UITextControl.Width(value ?? string.Empty, HistoryFaces.Mono, HistoryFaces.Size.Readout))
                    + 20f;

                Rect cell = new Rect(right - width, bar.y, width, bar.height);
                float valueHeight = UITextControl.LineHeight(HistoryFaces.Mono, HistoryFaces.Size.Readout);

                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = valueColor ?? palette.TextPrimary;

                UITextControl.Label(new Rect(cell.x, cell.y, cell.width - 6f, valueHeight + 2f), value,
                    HistoryFaces.Mono, HistoryFaces.Size.Readout);

                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = palette.TextDisabled;

                UITextControl.Label(new Rect(cell.x, cell.y + valueHeight + 3f, cell.width - 6f, 14f),
                    caption.ToUpperInvariant(), HistoryFaces.Mono, HistoryFaces.Size.Caption);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(cell, (TipSignal) tip);

                return cell.x;
            }
            finally
            {
                Text.WordWrap = wrap;
                GUI.color = color;
                Text.Anchor = anchor;
            }
        }

        // -------------------------------------------------------------------------------------------
        // Rail
        // -------------------------------------------------------------------------------------------

        private static void BuildRail(List<HistoryAutoRecorderGroup> groups, UIColorPaletteDef palette)
        {
            Rail.Clear();

            Color accent = HistoryFaces.AccentOf(palette);

            Rail.Add(new UIRailSectionHeaderControl("Plot")
            {
                Uppercase = true,
                Face = HistoryFaces.Mono,
                Points = HistoryFaces.Size.Caption
            });

            for (int i = 0; i < groups.Count; i++)
            {
                HistoryAutoRecorderGroup group = groups[i];

                Rail.Add(new UIRailClickableEntry(KeyOf(group), group.def.LabelCap)
                {
                    Trailing = LatestOf(group),
                    Face = HistoryFaces.Condensed,
                    Points = HistoryFaces.Size.RailName,
                    CountFace = HistoryFaces.Mono,
                    CountPoints = HistoryFaces.Size.RailCount,
                    SelectionBar = accent,
                    TextColor = KeyOf(group) == selected ? accent : (Color?) null,
                    Tooltip = "Plot " + group.def.label + " over time."
                });
            }

            Rail.Add(new UIRailDividerControl());

            HistoryFacts.ArchiveCounts(out int held, out int pinned, out _, out _);

            Rail.Add(new UIRailClickableEntry(ArchiveKey, "Archive")
            {
                Count = held,
                Face = HistoryFaces.Condensed,
                Points = HistoryFaces.Size.RailName,
                CountFace = HistoryFaces.Mono,
                CountPoints = HistoryFaces.Size.RailCount,
                SelectionBar = accent,
                TextColor = selected == ArchiveKey ? accent : (Color?) null,
                Tooltip = "Every letter and message the colony still holds. " + pinned + " pinned."
            });

            Rail.Add(new UIRailClickableEntry(BattlesKey, "Battles")
            {
                Count = HistoryFacts.Battles().Count,
                Face = HistoryFaces.Condensed,
                Points = HistoryFaces.Size.RailName,
                CountFace = HistoryFaces.Mono,
                CountPoints = HistoryFaces.Size.RailCount,
                SelectionBar = accent,
                TextColor = selected == BattlesKey ? accent : (Color?) null,
                Tooltip = "The fights the game still has a record of."
            });

            Rail.Add(new UIRailClickableEntry(StatisticsKey, "Statistics")
            {
                Face = HistoryFaces.Condensed,
                Points = HistoryFaces.Size.RailName,
                SelectionBar = accent,
                TextColor = selected == StatisticsKey ? accent : (Color?) null,
                Tooltip = "What this run has come to so far."
            });
        }

        /// <summary>A group's headline figure for the rail, formatted the way its own recorder asks.</summary>
        private static string LatestOf(HistoryAutoRecorderGroup group)
        {
            return UIGuard.Try("History.Latest", () =>
            {
                List<HistorySeries> series = HistoryFacts.SeriesOf(group);

                if (series.Count == 0)
                    return null;

                int total = HistoryFacts.TotalIndex(series);
                HistorySeries headline = series[total >= 0 ? total : 0];
                float value = headline.Latest;

                if (!headline.ValueFormat.NullOrEmpty() && headline.ValueFormat.Contains("$"))
                    return HistoryFacts.ShortSilver(value);

                string figure = Mathf.RoundToInt(value).ToString("N0");

                return !headline.ValueFormat.NullOrEmpty() && headline.ValueFormat.Contains("%")
                    ? figure + "%"
                    : figure;
            }, null, null);
        }

        // -------------------------------------------------------------------------------------------
        // The graph
        // -------------------------------------------------------------------------------------------

        private static void GraphView(Rect rect, List<HistoryAutoRecorderGroup> groups,
            UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(9f);
            HistoryAutoRecorderGroup group = Current(groups);

            if (group == null)
            {
                TabParts.RowLabel(inner, "Nothing has been recorded yet.", palette.TextDisabled,
                    GameFont.Small, HistoryFaces.Condensed, HistoryFaces.Size.Row);

                return;
            }

            List<HistorySeries> series = HistoryFacts.SeriesOf(group);

            Rect head = new Rect(inner.x, inner.y, inner.width, ControlHeight);

            TabParts.RowLabel(new Rect(head.x, head.y, head.width - 380f, head.height), group.def.LabelCap,
                palette.TextPrimary, GameFont.Small, HistoryFaces.Condensed, HistoryFaces.Size.Row);

            RangeControl(new Rect(head.xMax - 356f, head.y, 356f, head.height), palette);

            Widgets.DrawLineHorizontal(inner.x, head.yMax + 5f, inner.width);
            GUI.color = Color.white;

            float legendHeight = 34f;
            Rect plot = new Rect(inner.x, head.yMax + 12f, inner.width,
                inner.height - head.height - 12f - legendHeight - 8f);

            float today = HistoryFacts.Today;
            float from = span <= 0f ? 0f : Mathf.Max(0f, today - span);

            List<HistoryMoment> moments = HistoryFacts.Moments(from, today);

            HistoryMoment clicked = HistoryChart.Draw(plot, series, group.def, from, today, moments,
                HistoryFacts.ArchiveHorizonDay, palette);

            if (clicked != null)
                Open(clicked);

            Legend(new Rect(inner.x, inner.yMax - legendHeight, inner.width, legendHeight), series, palette);
        }

        /// <summary>
        /// The four spans, as segments rather than as buttons.
        ///
        /// <b>The whole reason this is not four <c>ButtonText</c> calls.</b> Vanilla's are momentary: pressing
        /// one sets <c>graphSection</c> and nothing on screen changes to say which is current, so the only way
        /// to find out what you are looking at is to count gridlines.
        /// </summary>
        private static void RangeControl(Rect rect, UIColorPaletteDef palette)
        {
            float today = HistoryFacts.Today;
            float width = (rect.width - 3f * 4f) / 4f;

            Segment(new Rect(rect.x, rect.y, width, rect.height), "30 days", span == 30f, palette,
                () => span = 30f);

            Segment(new Rect(rect.x + width + 4f, rect.y, width, rect.height), "100 days", span == 100f,
                palette, () => span = 100f);

            Segment(new Rect(rect.x + (width + 4f) * 2f, rect.y, width, rect.height), "300 days", span == 300f,
                palette, () => span = 300f);

            Segment(new Rect(rect.x + (width + 4f) * 3f, rect.y, width, rect.height),
                "All " + Mathf.FloorToInt(today), span <= 0f, palette, () => span = 0f);
        }

        private static void Segment(Rect rect, string label, bool on, UIColorPaletteDef palette,
            System.Action chosen)
        {
            TabParts.Segment(rect, label, on, palette, () =>
            {
                chosen();
                SoundDefOf.Click.PlayOneShotOnCamera();
            });
        }

        private static void Legend(Rect rect, List<HistorySeries> series, UIColorPaletteDef palette)
        {
            float x = rect.x;

            for (int i = 0; i < series.Count && x < rect.xMax - 60f; i++)
            {
                HistorySeries entry = series[i];
                string value = Formatted(entry);

                float width = Mathf.Max(
                    UITextControl.Width(entry.Label ?? string.Empty, HistoryFaces.Condensed,
                        HistoryFaces.Size.Chip),
                    UITextControl.Width(value, HistoryFaces.Mono, HistoryFaces.Size.Figure)) + 26f;

                Widgets.DrawBoxSolid(new Rect(x, rect.y + 6f, 3f, 22f), HistoryFaces.Series(palette, i));

                TabParts.RowLabel(new Rect(x + 9f, rect.y + 2f, width - 12f, 15f), value,
                    palette.TextPrimary, GameFont.Tiny, HistoryFaces.Mono, HistoryFaces.Size.Figure);

                TabParts.RowLabel(new Rect(x + 9f, rect.y + 17f, width - 12f, 14f), entry.Label,
                    palette.TextSecondary, GameFont.Tiny, HistoryFaces.Condensed, HistoryFaces.Size.Chip);

                x += width;
            }
        }

        private static string Formatted(HistorySeries series)
        {
            float value = series.Latest;

            if (!series.ValueFormat.NullOrEmpty() && series.ValueFormat.Contains("$"))
                return HistoryFacts.Silver(value);

            string figure = Mathf.RoundToInt(value).ToString("N0");

            return !series.ValueFormat.NullOrEmpty() && series.ValueFormat.Contains("%")
                ? figure + "%"
                : figure;
        }

        // -------------------------------------------------------------------------------------------
        // The archive
        // -------------------------------------------------------------------------------------------

        private static void ArchiveView(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(9f);

            HistoryFacts.ArchiveCounts(out int held, out int pinned, out int letters, out int messages);

            Rect head = new Rect(inner.x, inner.y, inner.width, ControlHeight);

            Chips(head, letters, messages, pinned, palette);

            Search.Draw(new Rect(head.xMax - 220f, head.y, 220f, ControlHeight), palette);

            Widgets.DrawLineHorizontal(inner.x, head.yMax + 5f, inner.width);
            GUI.color = Color.white;

            float top = head.yMax + 12f;
            Rect detail = new Rect(inner.xMax - DetailWidth, top, DetailWidth, inner.yMax - top);
            Rect list = new Rect(inner.x, top, inner.width - DetailWidth - Gap, inner.yMax - top);

            List<HistoryMoment> rows = HistoryFacts.Archive(showLetters, showMessages, pinnedOnly,
                Search.Text);

            ArchiveList(list, rows, held, pinned, palette);
            Detail(detail, palette);
        }

        private static void Chips(Rect rect, int letters, int messages, int pinned, UIColorPaletteDef palette)
        {
            float x = rect.x;

            x += Chip(new Rect(x, rect.y, ChipWidth("Letters", letters), rect.height), "Letters", letters,
                showLetters, palette, () => showLetters = !showLetters) + 4f;

            x += Chip(new Rect(x, rect.y, ChipWidth("Messages", messages), rect.height), "Messages", messages,
                showMessages, palette, () => showMessages = !showMessages) + 4f;

            Chip(new Rect(x, rect.y, ChipWidth("Pinned only", pinned), rect.height), "Pinned only", pinned,
                pinnedOnly, palette, () => pinnedOnly = !pinnedOnly);
        }

        private static float ChipWidth(string label, int count)
        {
            return TabParts.FilterChipWidth(label, count.ToString("N0"), HistoryFaces.Condensed,
                HistoryFaces.Size.Chip, HistoryFaces.Mono, HistoryFaces.Size.RailCount);
        }

        private static float Chip(Rect rect, string label, int count, bool on, UIColorPaletteDef palette,
            System.Action toggled)
        {
            if (TabParts.FilterChip(rect, label, count.ToString("N0"), on,
                    on ? HistoryFaces.AccentOf(palette) : (Color?) null, palette, HistoryFaces.Condensed,
                    HistoryFaces.Size.Chip, HistoryFaces.Mono, HistoryFaces.Size.RailCount))
            {
                toggled();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return rect.width;
        }

        private static void ArchiveList(Rect rect, List<HistoryMoment> rows, int held, int pinned,
            UIColorPaletteDef palette)
        {
            float footer = 34f;
            Rect body = new Rect(rect.x, rect.y, rect.width, rect.height - footer - 6f);

            Rect header = new Rect(body.x, body.y, body.width, 20f);

            Columns(header, palette);

            Rect outer = new Rect(body.x, header.yMax, body.width, body.height - header.height);
            Rect view = new Rect(0f, 0f, UIScrollBarControl.ContentWidth(outer), rows.Count * RowHeight + 2f);

            Widgets.BeginScrollView(outer, ref listScroll, view, false);

            for (int i = 0; i < rows.Count; i++)
            {
                float y = i * RowHeight;

                // Culled to what is on screen. Two hundred rows is not many, but every one of them measures a
                // date and an ellipsed label, and this list is redrawn every frame the tab is open.
                if (y + RowHeight < listScroll.y || y > listScroll.y + outer.height)
                    continue;

                ArchiveRow(new Rect(0f, y, view.width, RowHeight), rows[i], palette);
            }

            Widgets.EndScrollView();

            UIScrollBarControl.Draw(outer, view.height, ref listScroll, ref railDragging, ref railDragOffset,
                palette);

            CullNote(new Rect(rect.x, rect.yMax - footer, rect.width, footer), held, pinned, palette);
        }

        private static void Columns(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);
            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
            GUI.color = Color.white;

            TabParts.RowLabel(new Rect(rect.x + 62f, rect.y, 120f, rect.height), "DATE",
                palette.TextDisabled, GameFont.Tiny, HistoryFaces.Mono, HistoryFaces.Size.Caption);

            TabParts.RowLabel(new Rect(rect.x + 190f, rect.y, 200f, rect.height), "EVENT",
                palette.TextDisabled, GameFont.Tiny, HistoryFaces.Mono, HistoryFaces.Size.Caption);
        }

        /// <summary>
        /// One archived letter.
        ///
        /// <b>The label is at full strength.</b> Vanilla sets <c>GUI.color = Color.gray</c> and only leaves it
        /// white when the row matches the quick search, so the resting state of the entire archive is dim.
        ///
        /// <b>The pin is visible.</b> Vanilla draws the unpinned one in <c>PinOutlineColor</c>, an alpha 0.5
        /// near-black on a near-black panel, which hides the only control standing between a letter and
        /// deletion.
        /// </summary>
        private static void ArchiveRow(Rect rect, HistoryMoment row, UIColorPaletteDef palette)
        {
            bool isOpen = row.Archived != null && row.Archived == opened;

            if (isOpen)
            {
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 2f, 3f, rect.height - 4f),
                    HistoryFaces.AccentOf(palette));
            }
            else if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);
            }

            Archive archive = Find.Archive;
            bool pinned = archive != null && row.Archived != null && archive.IsPinned(row.Archived);

            Rect pin = new Rect(rect.x + 8f, rect.y + (rect.height - 18f) / 2f, 18f, 18f);
            Color previous = GUI.color;

            Texture2D mark = pinned ? PinTex : PinOutlineTex;

            GUI.color = pinned ? HistoryFaces.AccentOf(palette) : palette.TextDisabled;

            if (mark != null)
                GUI.DrawTexture(pin, mark);
            else
                Widgets.DrawBoxSolid(pin.ContractedBy(5f), GUI.color);

            GUI.color = previous;

            TooltipHandler.TipRegion(pin, (TipSignal) (pinned
                ? "Pinned. This one is kept when the archive drops its oldest entries."
                : "Pin this, and the archive keeps it past its " + HistoryFacts.ArchiveCap + " entry limit."));

            if (Widgets.ButtonInvisible(pin) && archive != null && row.Archived != null)
            {
                if (pinned)
                {
                    archive.Unpin(row.Archived);
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                }
                else
                {
                    archive.Pin(row.Archived);
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }

                return;
            }

            if (row.Icon != null)
            {
                Rect icon = new Rect(rect.x + 32f, rect.y + (rect.height - 18f) / 2f, 18f, 18f);

                GUI.color = row.Tint.a < 0.05f ? palette.TextSecondary : row.Tint;
                GUI.DrawTexture(icon, row.Icon, ScaleMode.ScaleToFit);
                GUI.color = previous;
            }

            TabParts.RowLabel(new Rect(rect.x + 62f, rect.y, 122f, rect.height),
                HistoryFacts.DateOf(row.TicksGame), palette.TextDisabled, GameFont.Tiny, HistoryFaces.Mono,
                HistoryFaces.Size.Figure);

            TabParts.RowLabel(new Rect(rect.x + 190f, rect.y, Mathf.Max(0f, rect.width - 196f), rect.height),
                row.Label, isOpen ? HistoryFaces.AccentOf(palette) : palette.TextPrimary, GameFont.Small,
                HistoryFaces.Condensed, HistoryFaces.Size.Row);

            if (Widgets.ButtonInvisible(rect))
            {
                opened = row.Archived;
                openedBattle = null;
                detailScroll = Vector2.zero;
            }
        }

        /// <summary>
        /// The line at the bottom that says the archive is full and what that means.
        ///
        /// <b>The whole purpose of the pin, stated once, where it can be read.</b> <c>Archive.Add</c> calls
        /// <c>CheckCullArchivables</c> on every letter and message, and the cap is 200. Vanilla says this
        /// nowhere except in a tooltip key on a 30 pixel box.
        /// </summary>
        private static void CullNote(Rect rect, int held, int pinned, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            bool full = held - pinned >= HistoryFacts.ArchiveCap;

            string text = full
                ? "Full: " + HistoryFacts.ArchiveCap + " kept, plus " + pinned
                  + " pinned. Older entries are dropped as new ones arrive. Pin anything worth keeping."
                : held + " kept of " + HistoryFacts.ArchiveCap + ", plus " + pinned
                  + " pinned. Once it is full, the oldest unpinned entry goes when the next one arrives.";

            TabParts.RowLabel(rect.ContractedBy(9f), text,
                full ? palette.TextSecondary : palette.TextDisabled, GameFont.Tiny, HistoryFaces.Condensed,
                HistoryFaces.Size.Chip);
        }

        /// <summary>
        /// The letter that is open, kept open.
        ///
        /// <b>Vanilla's pane follows the mouse.</b> <c>DoArchivableRow</c> sets <c>displayedMessageIndex</c>
        /// from <c>Mouse.IsOver</c>, so the pane flickers through every row the pointer crosses and there is no
        /// way to read one while looking at another.
        /// </summary>
        private static void Detail(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(12f);

            if (opened == null)
            {
                TabParts.RowLabel(inner, "Pick something on the left.", palette.TextDisabled, GameFont.Small,
                    HistoryFaces.Condensed, HistoryFaces.Size.Row);

                return;
            }

            TabParts.RowLabel(new Rect(inner.x, inner.y, inner.width, 13f), "SELECTED",
                palette.TextDisabled, GameFont.Tiny, HistoryFaces.Mono, HistoryFaces.Size.Caption);

            string label = UIGuard.Try("History.DetailLabel", () => opened.ArchivedLabel, "-", null);
            string body = UIGuard.Try("History.DetailBody", () => opened.ArchivedTooltip, string.Empty, null);

            TabParts.RowLabel(new Rect(inner.x, inner.y + 15f, inner.width, 22f), label,
                HistoryFaces.AccentOf(palette), GameFont.Small, HistoryFaces.Condensed,
                HistoryFaces.Size.Row);

            TabParts.RowLabel(new Rect(inner.x, inner.y + 37f, inner.width, 15f),
                HistoryFacts.DateOf(opened.CreatedTicksGame), palette.TextDisabled, GameFont.Tiny,
                HistoryFaces.Mono, HistoryFaces.Size.Figure);

            float actions = 30f;
            Rect text = new Rect(inner.x, inner.y + 58f, inner.width, inner.height - 58f - actions - 6f);
            Rect view = new Rect(0f, 0f, UIScrollBarControl.ContentWidth(text),
                UITextControl.Height(body, HistoryFaces.Body, HistoryFaces.Size.Prose,
                    UIScrollBarControl.ContentWidth(text)) + 4f);

            Widgets.BeginScrollView(text, ref detailScroll, view, false);

            Color previous = GUI.color;

            GUI.color = palette.TextSecondary;

            UITextControl.Paragraph(view, body, HistoryFaces.Body, HistoryFaces.Size.Prose);

            GUI.color = previous;

            Widgets.EndScrollView();

            Rect open = new Rect(inner.x, inner.yMax - actions, 110f, actions);

            if (TabParts.Button(open, "Open letter", palette))
                UIGuard.Try("History.OpenArchived", () => opened.OpenArchived(), null);

            Rect jump = new Rect(open.xMax + 6f, open.y, 96f, actions);
            LookTargets targets = UIGuard.Try<LookTargets>("History.Targets", () => opened.LookTargets, null,
                null);

            bool canJump = targets != null && CameraJumper.CanJump(targets.TryGetPrimaryTarget());

            if (TabParts.Button(jump, "Jump to", palette, canJump) && canJump)
            {
                CameraJumper.TryJumpAndSelect(targets.TryGetPrimaryTarget());
                Find.MainTabsRoot.EscapeCurrentTab();
            }
        }

        private static void Open(HistoryMoment moment)
        {
            if (moment.Battle != null)
            {
                selected = BattlesKey;
                openedBattle = moment.Battle;
                opened = null;

                return;
            }

            if (moment.Archived == null)
                return;

            selected = ArchiveKey;
            opened = moment.Archived;
            openedBattle = null;
            detailScroll = Vector2.zero;
        }

        // -------------------------------------------------------------------------------------------
        // Battles
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The battle log, which the history tab has never shown.
        ///
        /// Reachable today only through a pawn's character card, filtered to the entries that
        /// <c>Concerns</c> them, so a fight involving six colonists has to be read six times to be read once.
        /// </summary>
        private static void BattlesView(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(9f);
            List<Battle> battles = HistoryFacts.Battles();

            Rect head = new Rect(inner.x, inner.y, inner.width, ControlHeight);

            TabParts.RowLabel(new Rect(head.x, head.y, head.width, head.height), "Battles",
                palette.TextPrimary, GameFont.Small, HistoryFaces.Condensed, HistoryFaces.Size.Row);

            Widgets.DrawLineHorizontal(inner.x, head.yMax + 5f, inner.width);
            GUI.color = Color.white;

            float footer = 34f;
            Rect columns = new Rect(inner.x, head.yMax + 12f, inner.width, 20f);

            BattleColumns(columns, palette);

            Rect outer = new Rect(inner.x, columns.yMax, inner.width,
                inner.yMax - columns.yMax - footer - 6f);

            Rect view = new Rect(0f, 0f, UIScrollBarControl.ContentWidth(outer),
                battles.Count * BattleRowHeight + 2f);

            Widgets.BeginScrollView(outer, ref listScroll, view, false);

            for (int i = 0; i < battles.Count; i++)
                BattleRow(new Rect(0f, i * BattleRowHeight, view.width, BattleRowHeight), battles[i], palette);

            Widgets.EndScrollView();

            UIScrollBarControl.Draw(outer, view.height, ref listScroll, ref railDragging, ref railDragOffset,
                palette);

            Rect note = new Rect(inner.x, inner.yMax - footer, inner.width, footer);

            UIElementPainter.OutlineRounded(note, palette.Border, palette.SurfaceSunken);

            TabParts.RowLabel(note.ContractedBy(9f),
                "The last 20 battles. The game keeps no more than that, and drops the oldest once it has been "
                + "quiet for seven days. Nothing here can be pinned.", palette.TextDisabled, GameFont.Tiny,
                HistoryFaces.Condensed, HistoryFaces.Size.Chip);
        }

        private static void BattleColumns(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);
            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
            GUI.color = Color.white;

            float right = rect.xMax;

            Caption(new Rect(right - 90f, rect.y, 84f, rect.height), "ENTRIES", palette);
            Caption(new Rect(right - 260f, rect.y, 164f, rect.height), "COLONISTS IN IT", palette);
            Caption(new Rect(right - 350f, rect.y, 84f, rect.height), "LASTED", palette);
            Caption(new Rect(right - 490f, rect.y, 134f, rect.height), "STARTED", palette);
            Caption(new Rect(rect.x + 10f, rect.y, 200f, rect.height), "BATTLE", palette);
        }

        private static void Caption(Rect rect, string text, UIColorPaletteDef palette)
        {
            TabParts.RowLabel(rect, text, palette.TextDisabled, GameFont.Tiny, HistoryFaces.Mono,
                HistoryFaces.Size.Caption);
        }

        private static void BattleRow(Rect rect, Battle battle, UIColorPaletteDef palette)
        {
            bool isOpen = battle == openedBattle;

            if (isOpen)
            {
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 2f, 3f, rect.height - 4f),
                    HistoryFaces.AccentOf(palette));
            }
            else if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);
            }

            float right = rect.xMax;

            TabParts.RowLabel(new Rect(rect.x + 10f, rect.y, Mathf.Max(0f, rect.width - 510f), rect.height),
                UIGuard.Try("History.BattleName", battle.GetName, "Battle", null),
                isOpen ? HistoryFaces.AccentOf(palette) : palette.TextPrimary, GameFont.Small,
                HistoryFaces.Condensed, HistoryFaces.Size.Row);

            TabParts.RowLabel(new Rect(right - 490f, rect.y, 134f, rect.height),
                HistoryFacts.DateOf(battle.CreationTimestamp), palette.TextSecondary, GameFont.Tiny,
                HistoryFaces.Mono, HistoryFaces.Size.Figure);

            TabParts.RowLabel(new Rect(right - 350f, rect.y, 84f, rect.height), HistoryFacts.Lasted(battle),
                palette.TextSecondary, GameFont.Tiny, HistoryFaces.Mono, HistoryFaces.Size.Figure);

            TabParts.RowLabel(new Rect(right - 260f, rect.y, 164f, rect.height),
                HistoryFacts.WhoWasIn(battle), palette.TextDisabled, GameFont.Tiny, HistoryFaces.Condensed,
                HistoryFaces.Size.Chip);

            TabParts.RowLabel(new Rect(right - 90f, rect.y, 84f, rect.height),
                battle.Importance.ToString("N0"), palette.TextDisabled, GameFont.Tiny, HistoryFaces.Mono,
                HistoryFaces.Size.Figure);

            if (Widgets.ButtonInvisible(rect))
                openedBattle = isOpen ? null : battle;

            if (isOpen)
                TooltipHandler.TipRegion(rect, (TipSignal) Entries(battle));
        }

        /// <summary>
        /// A battle's log entries as one block of text.
        ///
        /// <b>Capped.</b> A siege runs to two hundred entries and a tooltip that long is one nobody reads and
        /// one that does not fit on the screen it is drawn over.
        /// </summary>
        private static string Entries(Battle battle)
        {
            return UIGuard.Try("History.BattleEntries", () =>
            {
                List<LogEntry> entries = battle.Entries;

                if (entries == null || entries.Count == 0)
                    return "No entries.";

                int shown = Mathf.Min(entries.Count, 12);
                System.Text.StringBuilder text = new System.Text.StringBuilder();

                for (int i = 0; i < shown; i++)
                    text.AppendLine(entries[i].ToGameStringFromPOV(null));

                if (entries.Count > shown)
                    text.AppendLine("... and " + (entries.Count - shown) + " more.");

                return text.ToString();
            }, "No entries.", null);
        }

        // -------------------------------------------------------------------------------------------
        // Statistics
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Three cards where vanilla draws one <c>StringBuilder</c>.
        ///
        /// <b>The old page is <c>Widgets.Label(new Rect(0f, 0f, 400f, 400f), text)</c> in a 1010 by 640
        /// window.</b> Everything past 400 pixels in either direction is empty, permanently, and the wealth
        /// figures inside it are <c>ToString("F0")</c>, so a colony worth two hundred thousand silver reports
        /// <c>238412</c>.
        /// </summary>
        private static void StatisticsView(Rect rect, UIColorPaletteDef palette)
        {
            float width = (rect.width - Gap * 2f) / 3f;

            Run(new Rect(rect.x, rect.y, width, rect.height), palette);
            Worth(new Rect(rect.x + width + Gap, rect.y, width, rect.height), palette);
            Cost(new Rect(rect.x + (width + Gap) * 2f, rect.y, width, rect.height), palette);
        }

        private static Rect Card(Rect rect, string heading, string headline, string caption,
            UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(12f);

            TabParts.RowLabel(new Rect(inner.x, inner.y, inner.width, 14f), heading.ToUpperInvariant(),
                palette.TextDisabled, GameFont.Tiny, HistoryFaces.Mono, HistoryFaces.Size.Caption);

            float headlineHeight = UITextControl.LineHeight(HistoryFaces.Mono, HistoryFaces.Size.Headline);

            TabParts.RowLabel(new Rect(inner.x, inner.y + 18f, inner.width, headlineHeight + 2f), headline,
                palette.TextPrimary, GameFont.Medium, HistoryFaces.Mono, HistoryFaces.Size.Headline);

            TabParts.RowLabel(new Rect(inner.x, inner.y + 20f + headlineHeight, inner.width, 14f),
                caption.ToUpperInvariant(), palette.TextDisabled, GameFont.Tiny, HistoryFaces.Mono,
                HistoryFaces.Size.Caption);

            return new Rect(inner.x, inner.y + 40f + headlineHeight, inner.width,
                inner.yMax - (inner.y + 40f + headlineHeight));
        }

        private static float Row(Rect area, float y, string label, string value, UIColorPaletteDef palette)
        {
            TabParts.RowLabel(new Rect(area.x, y, area.width * 0.62f, 22f), label, palette.TextSecondary,
                GameFont.Small, HistoryFaces.Condensed, HistoryFaces.Size.Row);

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextPrimary;

                UITextControl.LabelEllipses(new Rect(area.x + area.width * 0.62f, y, area.width * 0.38f, 22f),
                    value, HistoryFaces.Mono, HistoryFaces.Size.Figure);
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
            }

            return y + 22f;
        }

        private static void Run(Rect rect, UIColorPaletteDef palette)
        {
            Rect body = Card(rect, "The run", "Day " + Mathf.FloorToInt(HistoryFacts.Today),
                Quadrum(), palette);

            float y = body.y;

            y = Row(body, y, "Played", Playtime(), palette);
            y = Row(body, y, "Storyteller", UIGuard.Try("History.Teller",
                () => Find.Storyteller?.def == null ? "-" : Find.Storyteller.def.LabelCap.ToString(), "-",
                null), palette);
            y = Row(body, y, "Difficulty", UIGuard.Try("History.Difficulty",
                () => Find.Storyteller?.difficultyDef == null
                    ? "-"
                    : Find.Storyteller.difficultyDef.LabelCap.ToString(), "-", null), palette);
            y = Row(body, y, "Colonists", ColonistFigure(), palette);

            // Recorded by StatsRecord.UpdateGreatestPopulation and displayed nowhere in the game. On a run
            // that has buried six colonists it is the difference between what the colony is and what it was.
            Row(body, y, "Peak population", UIGuard.Try("History.Peak",
                () => Find.StoryWatcher?.statsRecord == null
                    ? "-"
                    : Find.StoryWatcher.statsRecord.greatestPopulation.ToString("N0"), "-", null), palette);
        }

        private static string Quadrum()
        {
            return UIGuard.Try("History.Quadrum", () =>
            {
                Vector2 location = Find.CurrentMap != null
                    ? Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile)
                    : Vector2.zero;

                long abs = GenDate.TickGameToAbs(Find.TickManager.TicksGame);

                return "Year " + GenDate.Year(abs, location.x) + ", "
                       + GenDate.Quadrum(abs, location.x).Label();
            }, "so far", null);
        }

        private static string Playtime()
        {
            return UIGuard.Try("History.Playtime", () =>
            {
                System.TimeSpan span = new System.TimeSpan(0, 0,
                    (int) Find.GameInfo.RealPlayTimeInteracting);

                return span.Days > 0
                    ? span.Days + "d " + span.Hours + "h " + span.Minutes + "m"
                    : span.Hours + "h " + span.Minutes + "m";
            }, "-", null);
        }

        private static void Worth(Rect rect, UIColorPaletteDef palette)
        {
            WealthWatcher wealth = Find.CurrentMap?.wealthWatcher;

            Rect body = Card(rect, "What it is worth",
                wealth == null ? "-" : HistoryFacts.Silver(wealth.WealthTotal), "On this map", palette);

            if (wealth == null)
                return;

            float items = wealth.WealthItems;
            float buildings = wealth.WealthBuildings;
            float pawns = wealth.WealthPawns;
            float total = Mathf.Max(1f, items + buildings + pawns);

            // The split as a bar, because three of these four numbers are parts of the fourth and four stacked
            // lines never said so.
            Rect bar = new Rect(body.x, body.y, body.width, 8f);

            UIElementPainter.Outline(bar, palette.Border, palette.SurfaceSunken);

            float x = bar.x + 1f;
            float inner = bar.width - 2f;

            x += Slice(new Rect(x, bar.y + 1f, inner * (items / total), 6f), palette, 1);
            x += Slice(new Rect(x, bar.y + 1f, inner * (buildings / total), 6f), palette, 2);
            Slice(new Rect(x, bar.y + 1f, inner * (pawns / total), 6f), palette, 3);

            float y = bar.yMax + 8f;

            y = Row(body, y, "Items", HistoryFacts.Silver(items), palette);
            y = Row(body, y, "Buildings", HistoryFacts.Silver(buildings), palette);
            Row(body, y, "Colonists and animals", HistoryFacts.Silver(pawns), palette);
        }

        private static float Slice(Rect rect, UIColorPaletteDef palette, int index)
        {
            if (rect.width > 0.5f)
                Widgets.DrawBoxSolid(rect, HistoryFaces.Series(palette, index));

            return rect.width;
        }

        private static void Cost(Rect rect, UIColorPaletteDef palette)
        {
            StatsRecord stats = Find.StoryWatcher?.statsRecord;

            Rect body = Card(rect, "What it cost",
                stats == null ? "-" : stats.colonistsKilled.ToString("N0"), "Colonists killed", palette);

            if (stats == null)
                return;

            float y = body.y;

            y = Row(body, y, "Major threats", stats.numThreatBigs.ToString("N0"), palette);
            y = Row(body, y, "Enemy raids", stats.numRaidsEnemy.ToString("N0"), palette);

            y = Row(body, y, "Damage taken", UIGuard.Try("History.Damage",
                () => Find.CurrentMap?.damageWatcher == null
                    ? "-"
                    : Find.CurrentMap.damageWatcher.DamageTakenEver.ToString("N0"), "-", null), palette);

            Row(body, y, "Colonists launched", stats.colonistsLaunched.ToString("N0"), palette);
        }
    }
}
