using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>Which half of the tab is showing.</summary>
    internal enum CorpseView
    {
        Dead,

        Graves
    }

    /// <summary>
    /// The corpses tab: what became of the dead, and what you are going to do about each one.
    ///
    /// <b>RimWorld tells you somebody died and then loses them.</b> There is no list of the dead anywhere in the
    /// game. The body is on the map somewhere, rotting on a clock nothing displays, still wearing the gear you
    /// paid for, still upsetting everyone who walks past it, and still carrying the fourteen levels of Medicine
    /// you spent four years growing.
    ///
    /// <b>The sections are what you may do with a body, because that is the only thing that changes between
    /// them.</b> A colonist is buried and mourned. A raider is a pile of gear and, if you are that sort of
    /// colony, meat. An animal is a butchering job with a clock on it. A mechanoid is scrap. Every column below
    /// means something slightly different in each of those, which is why there is no heading row and each cell
    /// carries its own caption -- the arrangement the animals tab established.
    ///
    /// <b>The graves half is here rather than in a tab of its own because it is the answer to this one.</b>
    /// Every row on the left is a body with nowhere to go, and the reason it has nowhere to go is almost always
    /// a grave that will not take it. Two screens would have put the question and its answer a click apart.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class CorpsePanel
    {
        /// <summary>
        /// The tab's own mark, drawn beside the title the way the other tabs draw theirs.
        ///
        /// The same texture the button on the bar uses, so the glyph a player clicked to get here is the
        /// glyph waiting at the top of the screen. Loaded in a static constructor because the game warns
        /// about any type holding a static texture field without one, and the check reads the field's type
        /// rather than watching when the texture is fetched.
        /// </summary>
        private static readonly Texture2D Glyph;

        static CorpsePanel()
        {
            // Through a local, because a readonly field can only be assigned in the constructor itself and
            // the guard does its work in a closure.
            Texture2D glyph = null;

            UIGuard.Try("Corpses.Glyph",
                () => glyph = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Graveyard", false),
                "The header has no glyph this session. Everything on the tab still reads.");

            Glyph = glyph;
        }

        // ---------------------------------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------------------------------

        private const float WhoWidth = 216f;
        private const float ConditionWidth = 146f;
        private const float SkillsWidth = 164f;
        private const float TraitsWidth = 172f;
        private const float GearWidth = 104f;
        private const float WhereWidth = 152f;
        /// <summary>
        /// Room for the widest pair of action buttons this tab can produce.
        ///
        /// <b>Measured rather than written down, and that is the fix for a real fault.</b> It was a flat 152 split
        /// in half, and "Butcher all" does not fit in seventy-six pixels: the animals section shipped reading
        /// "Butche...". Half a row is the right width for exactly one pair of labels, and this tab has five pairs.
        ///
        /// Recomputed whenever the answer changes, since the font is a setting: a player who turns tiny text off
        /// needs a wider column and gets one.
        /// </summary>
        private static float ActionsWidth
        {
            get
            {
                float widest = 0f;

                for (int i = 0; i < Pairs.Length; i += 2)
                {
                    float pair = TabParts.ButtonWidth(Pairs[i]) + TabParts.ButtonWidth(Pairs[i + 1]);

                    widest = Mathf.Max(widest, pair);
                }

                // Four for the leading margin, six between the two, four after.
                return Mathf.Ceil(widest + 14f);
            }
        }

        /// <summary>
        /// Every pair of labels the actions column can hold, longest form first.
        ///
        /// The " all" suffix is the folded-group form and is what makes these long, so it is included: a column
        /// sized for the ungrouped labels would truncate the moment three raiders folded together.
        /// </summary>
        private static readonly string[] Pairs =
        {
            "Bury all", "Strip all",
            "Strip all", "Cremate all",
            "Butcher all", "Bury all",
            "Shred all", "Bury all",
            "Buried", "Cancel"
        };

        /// <summary>The width the columns were last built for, so a font change rebuilds them.</summary>
        private static float builtActionsWidth = -1f;

        private const float GraveWidth = 176f;
        private const float HoldsWidth = 204f;
        private const float RestingWidth = 150f;
        private const float AcceptsWidth = 272f;
        private const float KeptWidth = 152f;
        private const float GraveActionsWidth = 118f;

        private const float WindowChrome = 24f;
        private const float PaneGap = 8f;
        private const float ToolbarHeight = 34f;
        private const float ToolbarGap = 6f;

        /// <summary>The header block, sized as the ideoligion, quest and power tabs size theirs.</summary>
        private const float HeaderHeight = 66f;

        /// <summary>Side of the header glyph, and the air between it and the title.</summary>
        private const float GlyphSize = 34f;

        private const float GlyphGap = 10f;

        /// <summary>Width of the rail, and the gap between it and the list.</summary>
        private const float RailWidth = 190f;

        private const float RailGap = 10f;
        private const float PortraitSize = 40f;
        private const float ButtonHeight = 24f;

        private static float CaptionHeight
        {
            get { return TabParts.CaptionHeight; }
        }

        private static float ValueHeight
        {
            get { return TabParts.ValueHeight; }
        }

        /// <summary>
        /// Tall enough for a caption over three skill lines, whatever the font situation is.
        ///
        /// Derived rather than a constant: a player who has turned tiny text off gets Small for both, and a
        /// literal that suited the default would shave the top and bottom off every row on the tab for them.
        /// </summary>
        private static float RowHeight
        {
            get { return Mathf.Max(72f, CaptionHeight + 3f * UIFonts.LineHeightOf(GameFont.Tiny) + 10f); }
        }

        private static readonly UICardControl RowCard = new UICardControl { Padding = 0f, AccentWidth = 3f };

        private static readonly UIDesignatorTabControl Dead = new UIDesignatorTabControl
        {
            HasHeaderRow = false,
            RowGap = 2f,
            SectionHeaderHeight = 30f,
            AlternatingColumnBands = false
        };

        private static readonly UIDesignatorTabControl Graves = new UIDesignatorTabControl
        {
            HasHeaderRow = false,
            RowGap = 2f,
            SectionHeaderHeight = 30f,
            AlternatingColumnBands = false
        };

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search the dead",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private static readonly List<CorpseEntry> Filtered = new List<CorpseEntry>();

        private static readonly List<GraveRecord> FilteredGraves = new List<GraveRecord>();

        private static bool builtColumns;

        // ---------------------------------------------------------------------------------------
        // State
        // ---------------------------------------------------------------------------------------

        private static CorpseView view = CorpseView.Dead;

        /// <summary>The body the pane is drawing, held by corpse so a rebuild cannot swap it.</summary>
        private static Corpse paneFor;

        private static bool paneOpen;

        /// <summary>
        /// The rail entry showing, held by its own label rather than by index.
        ///
        /// <b>A label, because the sections come and go.</b> The last animal being butchered removes that
        /// section entirely, and an index would then quietly move the selection onto whatever section slid
        /// into its place. A label that no longer exists simply falls back to everything, which is the right
        /// answer for a group that has emptied.
        ///
        /// Null means every section, which is the state the tab opens in.
        /// </summary>
        private static string section;

        private static Vector2 railScroll;
        private static bool railDragging;
        private static float railOffset;

        private static readonly List<UIRailElement> RailItems = new List<UIRailElement>();

        /// <summary>Every section, which is what the rail's first entry selects.</summary>
        private const string AllDead = "*all";

        internal static float WindowWidth
        {
            get
            {
                EnsureColumns();

                // The wider of the two views, so switching between them does not resize the window under the
                // cursor. A tab that jumps width when you press a segment reads as a bug.
                float wanted = Mathf.Max(Dead.RequestedWidth, Graves.RequestedWidth) + WindowChrome;

                if (paneOpen)
                    wanted += CorpseBodyPane.PaneWidth + PaneGap;

                return Mathf.Min(wanted, UI.screenWidth - 16f);
            }
        }

        internal static float WindowHeight
        {
            get { return Mathf.Min(760f, UI.screenHeight * 0.8f); }
        }

        internal static float PaneReservation
        {
            get { return paneOpen ? CorpseBodyPane.PaneWidth + PaneGap : 0f; }
        }

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        internal static void Draw(Rect inRect)
        {
            EnsureColumns();

            Dead.RowHeight = RowHeight;
            Graves.RowHeight = RowHeight;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // Both rosters every frame, because the toolbar readouts show figures from each whichever view is
            // up: the number of free graves is exactly what somebody looking at the unburied is asking about.
            List<CorpseSection> sections = CorpseRoster.Sections;
            List<BurialSite> sites = GraveRoster.Sites;

            Rect content = inRect.ContractedBy(6f);

            Header(new Rect(content.x, content.y, content.width, HeaderHeight), sections, palette);

            content = new Rect(content.x, content.y + HeaderHeight + ToolbarGap, content.width,
                Mathf.Max(0f, content.height - HeaderHeight - ToolbarGap));

            // The rail runs the full height beside everything else, so switching group does not move the
            // toolbar under the cursor.
            Rail(new Rect(content.x, content.y, RailWidth, content.height), sections, palette);

            content = new Rect(content.x + RailWidth + RailGap, content.y,
                Mathf.Max(0f, content.width - RailWidth - RailGap), content.height);

            Rect toolbar = new Rect(content.x, content.y, content.width, ToolbarHeight);

            Toolbar(toolbar, palette);

            content = new Rect(content.x, toolbar.yMax + ToolbarGap, content.width,
                Mathf.Max(0f, content.height - ToolbarHeight - ToolbarGap));

            if (paneOpen)
            {
                CorpseEntry entry = CorpseRoster.EntryFor(paneFor);

                if (entry == null)
                {
                    ClosePane();
                }
                else
                {
                    Rect pane = new Rect(content.xMax - CorpseBodyPane.PaneWidth, content.y,
                        CorpseBodyPane.PaneWidth, content.height);

                    content = new Rect(content.x, content.y,
                        content.width - CorpseBodyPane.PaneWidth - PaneGap, content.height);

                    if (!CorpseBodyPane.Draw(pane, entry, palette, ClosePane))
                        ClosePane();
                }
            }

            if (view == CorpseView.Dead)
            {
                CollectDead(sections);

                Dead.Draw(content, palette);
            }
            else
            {
                CollectGraves(sites);

                Graves.Draw(content, palette);
            }

            // After the grid, so any scroll view a click happened inside has been closed out.
            PawnCameraJump.Resolve();
        }

        private static void ClosePane()
        {
            paneOpen = false;
            paneFor = null;
        }

        private static void Open(CorpseEntry entry)
        {
            if (entry == null || entry.Corpse == null)
                return;

            if (paneOpen && paneFor == entry.Corpse)
            {
                ClosePane();

                return;
            }

            paneOpen = true;
            paneFor = entry.Corpse;
        }

        // ---------------------------------------------------------------------------------------
        // Toolbar
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The two views, the search, the toggles, and the four figures that say how far behind you are.
        ///
        /// <b>The readouts are colony facts rather than row facts,</b> which is why they sit above the list. Each
        /// one is a number nothing else in the game adds up: what the yard can still hold, how many of our own
        /// are lying in the open, what the raid left on the ground in silver, and what the freezer would gain if
        /// somebody went and butchered what is still fresh.
        /// </summary>
        /// <summary>
        /// The block that names the screen, with the four readouts seated in it.
        ///
        /// <b>The same shape the ideoligion, quest and power tabs use,</b> which is the whole point: a glyph,
        /// a title in the display face, a line of context under it, and the figures on the right. The
        /// readouts were already these four and already drawn through <c>TabParts.Readout</c>; they have
        /// moved off a bare toolbar and into the place every other tab keeps them.
        ///
        /// <b>Titled in this tab's violet rather than the palette accent.</b> See
        /// <see cref="CorpseFaces.AccentOf"/> for where that color comes from.
        /// </summary>
        private static void Header(Rect rect, List<CorpseSection> sections, UIColorPaletteDef palette)
        {
            // SurfaceSunken, the same fill the rail beside it uses: header and rail are both chrome framing
            // the content, so they share a surface and the blocks between them sit above it.
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(10f);

            float text = inner.x;

            if (Glyph != null)
            {
                Rect mark = new Rect(inner.x, inner.y + (inner.height - GlyphSize) * 0.5f, GlyphSize,
                    GlyphSize);

                Color previous = GUI.color;

                GUI.color = CorpseFaces.AccentOf(palette);
                GUI.DrawTexture(mark, Glyph);
                GUI.color = previous;

                text = mark.xMax + GlyphGap;
            }

            TabParts.RowLabel(new Rect(text, inner.y + 2f, 320f, 26f),
                view == CorpseView.Dead ? "The Dead" : "Graves", CorpseFaces.AccentOf(palette),
                GameFont.Medium, CorpseFaces.Display, CorpseFaces.Size.Title);

            TabParts.RowLabel(new Rect(text, inner.y + 28f, 360f, 18f), Standing(sections),
                palette.TextSecondary, GameFont.Tiny, CorpseFaces.Condensed, CorpseFaces.Size.Subtitle);

            Readouts(inner, palette);
        }

        /// <summary>
        /// The line under the title: how much is here, and how much of it is still waiting.
        ///
        /// <b>Two facts, because either alone misleads.</b> Eight bodies is unalarming when they are buried
        /// and is the whole problem when they are not, and the tab exists to tell those apart.
        /// </summary>
        private static string Standing(List<CorpseSection> sections)
        {
            if (view == CorpseView.Graves)
                return GraveRoster.TotalGraves == 0
                    ? "No grave has been built yet"
                    : GraveRoster.TotalGraves + (GraveRoster.TotalGraves == 1 ? " grave" : " graves")
                      + "  -  " + GraveRoster.FreeGraves + " free";

            int bodies = Total(sections);

            if (bodies == 0)
                return "Nothing dead on this map";

            int waiting = CorpseRoster.UnburiedColonists;

            return bodies + (bodies == 1 ? " body" : " bodies") + " on this map  -  "
                   + (waiting == 0
                       ? "none of ours unburied"
                       : waiting + " of ours unburied");
        }

        /// <summary>
        /// The rail: the groups of the dead, then the ground they go into.
        ///
        /// <b>It replaces two segments and a switch.</b> "The dead" and "Graves" were buttons, and animals
        /// were a checkbox you had to find before you could learn there were eight of them. As rail entries
        /// with counts, both the choices and their sizes are readable without pressing anything, which is
        /// what the rail does on the ideoligion tab.
        ///
        /// The buried switch stays a switch: it widens the list rather than choosing what the list is of.
        /// </summary>
        private static void Rail(Rect rect, List<CorpseSection> sections, UIColorPaletteDef palette)
        {
            RailItems.Clear();

            RailItems.Add(new UIRailSectionHeaderControl
            {
                Label = "The dead",
                Uppercase = true,
                Face = CorpseFaces.Mono,
                Points = CorpseFaces.Size.RailHead,
                Color = palette.TextDisabled
            });

            RailItems.Add(Entry(AllDead, "Everything", Total(sections), palette));

            for (int i = 0; i < sections.Count; i++)
            {
                CorpseSection group = sections[i];

                RailItems.Add(Entry(group.Label, group.Label, Count(group), palette));
            }

            RailItems.Add(new UIRailDividerControl { Color = palette.Border });

            RailItems.Add(new UIRailSectionHeaderControl
            {
                Label = "Ground",
                Uppercase = true,
                Face = CorpseFaces.Mono,
                Points = CorpseFaces.Size.RailHead,
                Color = palette.TextDisabled
            });

            RailItems.Add(Entry(GraveKey, "Graves", GraveRoster.TotalGraves, palette));

            string picked = UIRailControl.Draw(rect, RailItems, Selected(), ref railScroll, ref railDragging,
                ref railOffset, palette);

            if (picked == null)
                return;

            if (picked == GraveKey)
            {
                view = CorpseView.Graves;

                return;
            }

            view = CorpseView.Dead;
            section = picked == AllDead ? null : picked;
        }

        /// <summary>The rail key for the graves view, which is not one of the sections.</summary>
        private const string GraveKey = "*graves";

        /// <summary>Which rail entry is lit, given the view and the chosen section.</summary>
        private static string Selected()
        {
            if (view == CorpseView.Graves)
                return GraveKey;

            return section ?? AllDead;
        }

        /// <summary>One rail entry, in this tab's faces and its violet.</summary>
        private static UIRailClickableEntry Entry(string key, string label, int count,
            UIColorPaletteDef palette)
        {
            return new UIRailClickableEntry(key, label)
            {
                Count = count,
                Face = CorpseFaces.Condensed,
                Points = CorpseFaces.Size.RailName,
                CountFace = CorpseFaces.Mono,
                CountPoints = CorpseFaces.Size.RailCount,

                // Lit in the tab's own color rather than the palette accent, so the rail agrees with the
                // title above it.
                TextColor = Selected() == key ? CorpseFaces.AccentOf(palette) : (Color?) null,
                CountColor = Selected() == key ? CorpseFaces.AccentOf(palette) : (Color?) null
            };
        }

        /// <summary>
        /// Bodies in a section, counting a folded group as the bodies it stands for.
        ///
        /// The same rule <see cref="Bodies"/> uses for the section suffix, and it has to be: a rail saying
        /// eight beside a list whose own heading says twelve is worse than either number alone.
        /// </summary>
        private static int Count(CorpseSection group)
        {
            int bodies = 0;

            for (int i = 0; i < group.Entries.Count; i++)
            {
                if (!group.Entries[i].InGroup)
                    bodies += group.Entries[i].Members.Count;
            }

            return bodies;
        }

        /// <summary>Every body on the map, across all sections.</summary>
        private static int Total(List<CorpseSection> sections)
        {
            int bodies = 0;

            for (int i = 0; i < sections.Count; i++)
                bodies += Count(sections[i]);

            return bodies;
        }

        private static void Toolbar(Rect bar, UIColorPaletteDef palette)
        {
            float x = bar.x;

            Search.Placeholder = view == CorpseView.Dead ? "Search the dead" : "Search graves";

            Search.Draw(new Rect(x, bar.y + 4f, 210f, ToolbarHeight - 10f), palette);

            x += 220f;

            if (view == CorpseView.Dead)
                Toggles(bar, x, palette);
            else
                BuildButtons(bar, x, palette);

        }

        /// <summary>
        /// The two switches that widen the list, and the filters that narrow it.
        ///
        /// <b>One word each, and each switch as wide as its own word.</b> They read "Include buried" and
        /// "Include animals" in a pair of 138 pixel rows until 2026-08-23, when Aaron reported the label wrapping
        /// onto a second line and being clipped. Both halves of that were wrong: 138 is fourteen pixels less than
        /// the longer label needed, and "Include" is the switch's own job to say -- a toggle that is on includes
        /// the thing next to it, which is what a toggle means. Removing the word made the labels fit twice over,
        /// and the widths are measured now rather than written down, so the next label cannot repeat it.
        /// </summary>
        private static void Toggles(Rect bar, float x, UIColorPaletteDef palette)
        {
            float height = ToolbarHeight - 8f;

            bool buried = CorpseRoster.ShowBuried;
            float width = UICheckboxControl.WidthFor("Buried");

            if (UICheckboxControl.Draw(new Rect(x, bar.y + 4f, width, height), ref buried, palette,
                    "Buried", "A grave is a decision already made, so the buried are left out until "
                              + "you ask for them."))
            {
                CorpseRoster.ShowBuried = buried;

                CorpseRoster.Invalidate();
            }

            x += width + ToggleGap;

            bool animals = CorpseRoster.ShowAnimals;
            width = UICheckboxControl.WidthFor("Animals");

            if (UICheckboxControl.Draw(new Rect(x, bar.y + 4f, width, height), ref animals, palette, "Animals",
                    "Dead animals are shown by default: most of them are meat and leather nobody has "
                    + "collected yet."))
            {
                CorpseRoster.ShowAnimals = animals;

                CorpseRoster.Invalidate();
            }

            x += width + ToggleGap;

            // Primary while something is filtered, so a list that is hiding bodies says so from the toolbar
            // rather than only from inside the window that set it.
            int filters = CorpseFilter.Count;

            string label = filters == 0 ? "Set filters" : "Filters (" + filters + ")";

            if (TabParts.Button(new Rect(x, bar.y + 4f, TabParts.ButtonWidth(label) + 16f, height), label,
                    palette, true, filters > 0,
                    filters == 0
                        ? "Narrow the list by xenotype, traits, skills, sex, age or faction."
                        : filters + (filters == 1 ? " filter is" : " filters are")
                          + " hiding bodies from this list."))
                Dialog_CorpseFilters.Open();
        }

        /// <summary>Between the switches, and after the last of them. Wide enough that they read as separate.</summary>
        private const float ToggleGap = 10f;

        /// <summary>
        /// The two build buttons, each the width its own label needs.
        ///
        /// <b>Measured, not halved or guessed.</b> These were literals -- 108 and 132 with the second at x plus
        /// 114 -- and "Build a sarcophagus" came out as "Build a sarcopha...". This is the same fault the two
        /// action buttons on the bodies side of this very tab already had, and the same fix: a button takes the
        /// width of the words in it. The Filters button four lines up was already doing it.
        ///
        /// Nothing downstream depends on where this ends: the readouts measure leftwards from the bar's right
        /// edge, so the pair can be as wide as its labels want.
        /// </summary>
        private static void BuildButtons(Rect bar, float x, UIColorPaletteDef palette)
        {
            const string grave = "Build a grave";
            const string sarcophagus = "Build a sarcophagus";
            const float gap = 6f;

            float height = ToolbarHeight - 8f;
            float width = TabParts.ButtonWidth(grave);

            if (TabParts.Button(new Rect(x, bar.y + 4f, width, height), grave, palette, true, false,
                    "Closes the tab with the grave tool in hand."))
                GraveActions.Build("Grave");

            x += width + gap;

            if (TabParts.Button(new Rect(x, bar.y + 4f, TabParts.ButtonWidth(sarcophagus), height), sarcophagus,
                    palette, true, false, "Closes the tab with the sarcophagus tool in hand."))
                GraveActions.Build("Sarcophagus");
        }

        private static void Readouts(Rect bar, UIColorPaletteDef palette)
        {
            UIColorPaletteDef p = palette;

            float x = bar.xMax;

            x = TabParts.Readout(bar, x, "meat if butchered", CorpseRoster.MeatIfButchered.ToString("N0"), p,
                "What a butcher would get out of every body still fresh enough to take.");

            x = TabParts.Readout(bar, x, "gear on the dead", CorpseRoster.GearOnTheDead.ToString("N0"), p,
                "The market value of everything still on a body nobody has stripped.");

            x = TabParts.Readout(bar, x, "unburied", Unburied(), p,
                "Our own dead lying in the open. Once one of them has been out a day and a half the whole "
                + "colony takes a mood penalty until it is buried.",
                CorpseRoster.UnburiedCosting > 0
                    ? palette.Mood
                    : CorpseRoster.UnburiedColonists > 0
                        ? palette.Warning
                        : palette.TextPrimary);

            TabParts.Readout(bar, x, "graves free",
                GraveRoster.FreeGraves + " / " + GraveRoster.TotalGraves, p,
                "Graves with nobody in them and nobody reserved for them.",
                GraveRoster.FreeGraves == 0 && CorpseRoster.UnburiedColonists > 0
                    ? palette.Warning
                    : palette.TextPrimary);
        }

        private static string Unburied()
        {
            if (CorpseRoster.UnburiedCosting > 0)
                return CorpseRoster.UnburiedColonists + " (" + CorpseRoster.UnburiedCosting + " costing mood)";

            return CorpseRoster.UnburiedColonists.ToString();
        }

        // ---------------------------------------------------------------------------------------
        // Rows: the dead
        // ---------------------------------------------------------------------------------------

        private static void CollectDead(List<CorpseSection> sections)
        {
            Dead.Rows.Clear();

            Dead.SuppressCollapse = !Search.IsEmpty;

            for (int s = 0; s < sections.Count; s++)
            {
                CorpseSection group = sections[s];

                // The rail's choice, unless a search is running: typing is a request to look everywhere, and
                // a search that silently skipped four of the five groups would read as a broken search.
                if (section != null && Search.IsEmpty && group.Label != section)
                    continue;

                Filtered.Clear();

                for (int i = 0; i < group.Entries.Count; i++)
                {
                    CorpseEntry entry = group.Entries[i];

                    if (!Search.IsEmpty && !Search.Matches(entry.Name) && !Search.Matches(entry.Subline))
                        continue;

                    Filtered.Add(entry);
                }

                if (Filtered.Count == 0)
                    continue;

                Dead.Rows.Add(new UIDesignatorTabRow
                {
                    SectionLabel = group.Label,
                    SectionSuffix = Bodies(Filtered)
                });

                for (int i = 0; i < Filtered.Count; i++)
                {
                    Dead.Rows.Add(new UIDesignatorTabRow
                    {
                        Payload = Filtered[i],
                        DrawBackground = DeadBackground
                    });
                }
            }
        }

        /// <summary>
        /// Bodies rather than rows, because a folded row stands for several of them.
        ///
        /// Members of an opened group are skipped: their head already counted them, and counting both is how a
        /// section reads "12" the moment you open a group of three.
        /// </summary>
        private static string Bodies(List<CorpseEntry> entries)
        {
            int bodies = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].InGroup)
                    bodies += entries[i].Members.Count;
            }

            return bodies.ToString();
        }

        /// <summary>
        /// The row's card, its stripe and the click that opens it.
        ///
        /// <b>The stripe is the mood colour on a body that is costing mood and the rot colour otherwise,</b>
        /// which puts the one row on this tab that charges you by the hour at the top of a glance rather than in
        /// the middle of a column of amber.
        ///
        /// The actions column is cut out of the hit target by geometry rather than by draw order: this background
        /// is painted before any cell, so its button would otherwise swallow every Bury and Strip on the tab.
        /// Measured from the columns and not from the row, because a row is as wide as the window.
        /// </summary>
        private static void DeadBackground(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            CorpseEntry entry = data.Payload as CorpseEntry;

            if (entry == null)
                return;

            RowCard.AccentColor = entry.UnburiedIn <= 0
                ? palette.Mood
                : CorpseFacts.StageColor(entry.Stage, palette);

            RowCard.BackgroundColor = palette.PanelBackground;
            RowCard.DrawChrome(row, palette);

            if (paneOpen && paneFor == entry.Corpse)
                Widgets.DrawBoxSolid(row, palette.SelectionOverlay);

            float right = Mathf.Min(row.xMax, row.x + Dead.ColumnsWidth - ActionsWidth);

            Rect click = new Rect(row.x, row.y, Mathf.Max(0f, right - row.x), row.height);

            if (!Widgets.ButtonInvisible(click))
                return;

            // A group's head is a container, not a body, so clicking it opens and closes the group rather than
            // a pane that could only ever describe one of the bodies inside it.
            if (entry.GroupHead)
            {
                if (!CorpseRoster.Opened.Add(entry.GroupKey))
                    CorpseRoster.Opened.Remove(entry.GroupKey);

                CorpseRoster.Invalidate();

                return;
            }

            Open(entry);
        }

        // ---------------------------------------------------------------------------------------
        // Rows: graves
        // ---------------------------------------------------------------------------------------

        private static void CollectGraves(List<BurialSite> sites)
        {
            Graves.Rows.Clear();

            Graves.SuppressCollapse = !Search.IsEmpty;

            for (int s = 0; s < sites.Count; s++)
            {
                BurialSite site = sites[s];

                FilteredGraves.Clear();

                for (int i = 0; i < site.Graves.Count; i++)
                {
                    GraveRecord record = site.Graves[i];

                    if (!Search.IsEmpty && !Search.Matches(record.Occupied ?? string.Empty)
                                        && !Search.Matches(record.Label)
                                        && !Search.Matches(site.Label))
                        continue;

                    FilteredGraves.Add(record);
                }

                if (FilteredGraves.Count == 0)
                    continue;

                Graves.Rows.Add(new UIDesignatorTabRow
                {
                    SectionLabel = SiteLabel(site),
                    SectionSuffix = site.Free + " of " + site.Total + " free"
                });

                for (int i = 0; i < FilteredGraves.Count; i++)
                {
                    Graves.Rows.Add(new UIDesignatorTabRow
                    {
                        Payload = FilteredGraves[i],
                        DrawBackground = GraveBackground
                    });
                }
            }
        }

        /// <summary>
        /// A burial site's name with the game's own adjective for it.
        ///
        /// <b>Impressiveness is not decoration here.</b> It is the input to the grave visiting recreation factor,
        /// so a tomb the game calls impressive is worth up to forty percent more to a colonist who goes and
        /// stands in it than the same graves in a corridor would be.
        /// </summary>
        private static string SiteLabel(BurialSite site)
        {
            if (site.Outdoors || site.Quality.NullOrEmpty())
                return site.Label;

            return site.Label + " - " + site.Quality;
        }

        private static void GraveBackground(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            GraveRecord record = data.Payload as GraveRecord;

            if (record == null)
                return;

            RowCard.AccentColor = record.Corpse != null
                ? CorpseFacts.StageColor(record.Stage, palette)
                : record.Reserved != null
                    ? palette.Info
                    : palette.Border;

            RowCard.BackgroundColor = palette.PanelBackground;
            RowCard.DrawChrome(row, palette);

            // Everything from the accepts pills rightwards is a control, so the row-wide jump stops before them.
            float cut = AcceptsWidth + KeptWidth + GraveActionsWidth;

            float right = Mathf.Min(row.xMax, row.x + Graves.ColumnsWidth - cut);

            Rect click = new Rect(row.x, row.y, Mathf.Max(0f, right - row.x), row.height);

            if (Widgets.ButtonInvisible(click))
                PawnCameraJump.Request(record.Grave);
        }

        // ---------------------------------------------------------------------------------------
        // Columns
        // ---------------------------------------------------------------------------------------

        private static void EnsureColumns()
        {
            float actions = ActionsWidth;

            // Watched rather than built once. The measurement depends on the font, which is a setting a player can
            // change with this tab open.
            if (builtColumns && Mathf.Abs(builtActionsWidth - actions) <= 0.5f)
                return;

            builtColumns = true;
            builtActionsWidth = actions;

            Dead.Columns.Clear();
            Dead.RowHeight = RowHeight;

            Column(Dead, WhoWidth, WhoCell, false);
            Column(Dead, ConditionWidth, ConditionCell, true);
            Column(Dead, SkillsWidth, SkillsCell, true);
            Column(Dead, TraitsWidth, TraitsCell, true);
            Column(Dead, GearWidth, GearCell, true);
            Column(Dead, WhereWidth, WhereCell, true);
            Column(Dead, actions, ActionsCell, false);

            Graves.Columns.Clear();
            Graves.RowHeight = RowHeight;

            Column(Graves, GraveWidth, GraveCell, false);
            Column(Graves, HoldsWidth, HoldsCell, true);
            Column(Graves, RestingWidth, RestingCell, true);
            Column(Graves, AcceptsWidth, AcceptsCell, true);
            Column(Graves, KeptWidth, KeptCell, true);
            Column(Graves, GraveActionsWidth, GraveActionsCell, false);
        }

        private static void Column(UIDesignatorTabControl grid, float width,
            System.Action<Rect, UIDesignatorTabRow, UIColorPaletteDef> draw, bool bandable)
        {
            grid.Columns.Add(new UIDesignatorTabColumn { Width = width, DrawCell = draw, Bandable = bandable });
        }

        private static CorpseEntry Of(UIDesignatorTabRow data)
        {
            return data == null ? null : data.Payload as CorpseEntry;
        }

        private static GraveRecord GraveOf(UIDesignatorTabRow data)
        {
            return data == null ? null : data.Payload as GraveRecord;
        }

        // ---------------------------------------------------------------------------------------
        // Cells: the dead
        // ---------------------------------------------------------------------------------------

        private static void WhoCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            CorpseEntry entry = Of(data);

            if (entry == null)
                return;

            // Members of an opened group are stepped in, so the head above them reads as the thing they belong
            // to rather than as another body in the same list.
            float indent = entry.InGroup ? 16f : 0f;

            Rect portrait = new Rect(cell.x + 4f + indent, cell.y + (cell.height - PortraitSize) * 0.5f,
                PortraitSize, PortraitSize);

            if (entry.GroupHead)
            {
                // A stack rather than a face: the group is several bodies and any one portrait would be a lie
                // about which of them you are looking at.
                UIElementPainter.OutlineRounded(portrait, palette.Border, palette.SurfaceSunken);

                TabParts.Line(portrait, portrait.y + (portrait.height - ValueHeight) * 0.5f,
                    "  x" + entry.Members.Count, palette.TextSecondary, GameFont.Tiny);
            }
            else
            {
                // The portrait requests its own camera jump, so nothing extra is hung on it here. A second
                // invisible button over the same rect would shift every control id after it on the row.
                PawnPortraitCell.Draw(portrait, entry.Pawn, palette, palette.SurfaceSunken);
            }

            Rect text = new Rect(portrait.xMax + 6f, cell.y + 6f, Mathf.Max(20f, cell.xMax - portrait.xMax - 10f),
                cell.height);

            float x = text.x;

            if (!entry.GroupHead && entry.Pawn != null && entry.Kind != CorpseKind.Mechanoids)
                x += GenderGlyphs.Draw(new Rect(text.x, text.y, text.width, ValueHeight), entry.Pawn, palette);

            TabParts.Line(new Rect(x, text.y, Mathf.Max(20f, text.xMax - x), 0f), text.y, entry.Name,
                palette.TextPrimary);

            TabParts.Line(text, text.y + ValueHeight, entry.Subline, palette.TextDisabled, GameFont.Tiny);

            if (entry.UnburiedIn == int.MaxValue || entry.Grave != null)
                return;

            TabParts.Line(text, text.y + ValueHeight + UIFonts.LineHeightOf(GameFont.Tiny),
                entry.UnburiedIn <= 0
                    ? "Everyone: -10 mood until buried"
                    : "Upsets everyone in " + entry.UnburiedIn.ToStringTicksToPeriod(),
                entry.UnburiedIn <= 0 ? palette.Mood : palette.Warning, GameFont.Tiny);
        }

        private static void ConditionCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            CorpseEntry entry = Of(data);

            if (entry == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 4f, cell.width - 8f, cell.height - 8f);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(band.x + 2f, band.y, band.width - 4f, CaptionHeight), "condition");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            float y = band.y + CaptionHeight;

            Rect pill = TabParts.Pill(band, band.x + 2f, y + 1f, CorpseFacts.StageTag(entry.Stage),
                CorpseFacts.StageColor(entry.Stage, palette), palette, band.width - 4f);

            string percent = entry.Spread
                             ?? (entry.Stage == RotStage.Rotting
                                 ? Mathf.RoundToInt(entry.Progress * 100f) + "%"
                                 : null);

            if (!percent.NullOrEmpty())
                TabParts.Line(new Rect(pill.xMax + 4f, y, Mathf.Max(10f, band.xMax - pill.xMax - 6f), 0f), y,
                    percent, palette.TextSecondary, GameFont.Tiny);

            TabParts.Line(new Rect(band.x + 2f, y, band.width - 4f, 0f), pill.yMax + 2f,
                entry.Frozen ? "Frozen" : entry.RotNote, palette.TextDisabled, GameFont.Tiny);

            TooltipHandler.TipRegion(band, (TipSignal) (entry.RotNote + "\n\n"
                                                        + entry.DaysRotted.ToString("0.0")
                                                        + " days of decay on the clock. That figure is what a "
                                                        + "resurrection's side effects are scaled from."));
        }

        /// <summary>
        /// The three best skills, or what a body yields instead.
        ///
        /// <b>The caption changes with the section because the number means a different thing in each.</b> On our
        /// own dead it is what the colony lost; on a raider it is whether the corpse is worth a resurrector
        /// serum; on an animal there are no skills at all and the useful reading is meat and leather.
        /// </summary>
        private static void SkillsCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            CorpseEntry entry = Of(data);

            if (entry == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 4f, cell.width - 8f, cell.height - 8f);

            if (entry.Kind == CorpseKind.Animals || entry.Kind == CorpseKind.Mechanoids)
            {
                Yield(band, entry, palette);

                return;
            }

            string caption = entry.Kind == CorpseKind.Ours ? "what we lost" : "skills";

            Caption(band, caption, palette);

            float y = band.y + CaptionHeight;

            if (entry.Folded)
            {
                TabParts.Line(band, y, "Nothing worth reading", palette.TextDisabled, GameFont.Tiny);

                return;
            }

            float line = UIFonts.LineHeightOf(GameFont.Tiny);

            for (int i = 0; i < entry.Skills.Count; i++)
            {
                CorpseSkill skill = entry.Skills[i];

                Rect row = new Rect(band.x + 2f, y, band.width - 4f, line);

                TabParts.Line(new Rect(row.x, row.y, row.width - 46f, 0f), row.y, skill.Label,
                    palette.TextSecondary, GameFont.Tiny);

                TabParts.Line(new Rect(row.xMax - 42f, row.y, 24f, 0f), row.y, skill.Level.ToString(),
                    skill.Level >= 10 ? palette.TextPrimary : palette.TextSecondary, GameFont.Tiny);

                PassionMark(new Rect(row.xMax - 18f, row.y, 18f, line), skill.Passion, palette);

                y += line;
            }

            if (entry.Skills.Count == 0)
                TabParts.Line(band, y, "None", palette.TextDisabled, GameFont.Tiny);
        }

        private static void Yield(Rect band, CorpseEntry entry, UIColorPaletteDef palette)
        {
            Caption(band, entry.Kind == CorpseKind.Mechanoids ? "salvage" : "yield", palette);

            float y = band.y + CaptionHeight;
            float line = UIFonts.LineHeightOf(GameFont.Tiny);

            if (entry.Kind == CorpseKind.Mechanoids)
            {
                TabParts.Line(band, y, "Shredding gives components and steel", palette.TextDisabled,
                    GameFont.Tiny);

                return;
            }

            bool spoiled = entry.Stage != RotStage.Fresh;

            Color color = spoiled ? palette.TextDisabled : palette.TextSecondary;

            if (entry.Meat > 0)
            {
                Pair(band, y, "Meat", entry.Meat.ToString(), color);

                y += line;
            }

            if (entry.Leather > 0)
            {
                Pair(band, y, entry.LeatherLabel ?? "Leather", entry.Leather.ToString(), color);

                y += line;
            }

            if (entry.Meat == 0 && entry.Leather == 0)
            {
                TabParts.Line(band, y, "Nothing to butcher", palette.TextDisabled, GameFont.Tiny);

                return;
            }

            if (spoiled)
                TabParts.Line(band, y, "Too far gone to take", palette.Warning, GameFont.Tiny);
        }

        private static void Pair(Rect band, float y, string left, string right, Color color)
        {
            TabParts.Line(new Rect(band.x + 2f, y, band.width - 50f, 0f), y, left, color, GameFont.Tiny);

            TabParts.Line(new Rect(band.xMax - 44f, y, 42f, 0f), y, right, color, GameFont.Tiny);
        }

        private static void Caption(Rect band, string caption, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                GUI.color = palette.TextDisabled;

                UIRichText.Label(new Rect(band.x + 2f, band.y, band.width - 4f, CaptionHeight), caption);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        /// <summary>The game's own flame, so a passion reads the same here as everywhere else.</summary>
        private static void PassionMark(Rect rect, Passion passion, UIColorPaletteDef palette)
        {
            if (passion == Passion.None)
                return;

            Texture2D icon = passion == Passion.Major
                ? SkillUI.PassionMajorIcon
                : SkillUI.PassionMinorIcon;

            if (icon == null)
                return;

            Color previous = GUI.color;

            GUI.color = passion == Passion.Major ? palette.Warning : palette.AccentMuted;

            GUI.DrawTexture(new Rect(rect.x, rect.center.y - 6f, 12f, 12f), icon);

            GUI.color = previous;
        }

        private static void TraitsCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            CorpseEntry entry = Of(data);

            if (entry == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 4f, cell.width - 8f, cell.height - 8f);

            Caption(band, entry.Kind == CorpseKind.Animals ? "was" : "traits", palette);

            Rect flow = new Rect(band.x + 2f, band.y + CaptionHeight, band.width - 4f,
                band.height - CaptionHeight);

            if (entry.Folded)
            {
                TabParts.Line(flow, flow.y, entry.TraitTotal + " between them", palette.TextDisabled,
                    GameFont.Tiny);

                return;
            }

            if (entry.Kind == CorpseKind.Animals)
            {
                Was(flow, entry, palette);

                return;
            }

            if (entry.Traits.Count == 0)
            {
                TabParts.Line(flow, flow.y, "None", palette.TextDisabled, GameFont.Tiny);

                return;
            }

            float x = flow.x;
            float y = flow.y;
            float rowHeight = 0f;

            for (int i = 0; i < entry.Traits.Count; i++)
            {
                // Measured before it is placed, which is the whole reason this is a shared control: the four
                // copies of this loop in the inspect pane each drew the chip and then asked whether it fitted.
                if (y + rowHeight > flow.yMax)
                    break;

                InspectPaneParts.Chip(flow, ref x, ref y, ref rowHeight, entry.Traits[i], palette.TextSecondary,
                    false, palette);
            }
        }

        /// <summary>What an animal was to the colony, which is the only thing worth saying about a dead one.</summary>
        private static void Was(Rect flow, CorpseEntry entry, UIColorPaletteDef palette)
        {
            float x = flow.x;
            float y = flow.y;
            float rowHeight = 0f;

            UIGuard.Try("Corpses.Was", () =>
            {
                Pawn pawn = entry.Pawn;

                if (pawn.training != null && pawn.training.HasLearned(TrainableDefOf.Obedience))
                    InspectPaneParts.Chip(flow, ref x, ref y, ref rowHeight, "Trained", palette.TextSecondary,
                        false, palette);

                if (pawn.relations == null)
                    return;

                Pawn bonded = pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Bond);

                if (bonded != null)
                    InspectPaneParts.Chip(flow, ref x, ref y, ref rowHeight, "Bonded to " + bonded.LabelShortCap,
                        palette.Mood, false, palette);
            }, null);

            if (rowHeight <= 0f)
                TabParts.Line(flow, flow.y, "Livestock", palette.TextDisabled, GameFont.Tiny);
        }

        private static void GearCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            CorpseEntry entry = Of(data);

            if (entry == null)
                return;

            Rect band = new Rect(cell.x, cell.y + 4f, cell.width, cell.height - 8f);

            string value = entry.GearCount == 0
                ? "None"
                : entry.GearCount + (entry.GearCount == 1 ? " item" : " items");

            Color color = entry.GearCount == 0
                ? palette.TextDisabled
                : entry.StripQueued
                    ? palette.Success
                    : entry.GearValue >= 500
                        ? palette.Warning
                        : palette.TextPrimary;

            TabParts.Labelled(band, "gear", value, color, palette,
                entry.GearCount == 0
                    ? "Nothing left on the body."
                    : "Worth " + entry.GearValue.ToString("N0") + " silver, and deteriorating where it lies.");

            if (entry.GearCount == 0)
                return;

            TabParts.Line(new Rect(band.x + 6f, band.y, band.width - 10f, 0f),
                band.y + 2f + CaptionHeight + ValueHeight,
                entry.StripQueued ? "Being stripped" : entry.GearValue.ToString("N0") + " silver",
                entry.StripQueued ? palette.Success : palette.TextDisabled, GameFont.Tiny);
        }

        private static void WhereCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            CorpseEntry entry = Of(data);

            if (entry == null)
                return;

            Rect band = new Rect(cell.x, cell.y + 4f, cell.width, cell.height - 8f);

            Color color = entry.Buried
                ? palette.Success
                : entry.UnburiedIn <= 0
                    ? palette.Mood
                    : entry.Where == "On the floor"
                        ? palette.Warning
                        : palette.TextPrimary;

            TabParts.Labelled(band, "where", entry.Where, color, palette);

            if (entry.WhereNote.NullOrEmpty())
                return;

            TabParts.Line(new Rect(band.x + 6f, band.y, band.width - 10f, 0f),
                band.y + 2f + CaptionHeight + ValueHeight, entry.WhereNote, palette.TextDisabled, GameFont.Tiny);
        }

        /// <summary>
        /// The two things worth doing to this kind of body.
        ///
        /// <b>Butchering is offered on animals and mechanoids and on nobody else.</b> It is the one action here
        /// that cannot be undone and that a misclick turns into a colony-wide mood spiral, and this tab is a list
        /// you scan rather than a body you have deliberately selected. A colony that butchers its own dead can
        /// still do it from the corpse itself, where the decision is made once with the body in front of you.
        ///
        /// A refused action is drawn disabled and says why on hover rather than being hidden, because "there is
        /// no crematorium" is a thing the player needs told and a missing button leaves nothing to hover.
        /// </summary>
        private static void ActionsCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            CorpseEntry entry = Of(data);

            if (entry == null)
                return;

            string all = entry.Folded ? " all" : string.Empty;

            // Each button gets the width its own label needs, and whatever the cell has left over is shared.
            // Splitting the cell in half is what turned "Butcher all" into "Butche...".
            string leftLabel;
            string rightLabel;

            Labels(entry, all, out leftLabel, out rightLabel);

            float wantedLeft = TabParts.ButtonWidth(leftLabel);
            float wantedRight = TabParts.ButtonWidth(rightLabel);

            float room = cell.width - 14f;
            float slack = room - wantedLeft - wantedRight;

            if (slack > 0f)
            {
                float share = Mathf.Floor(slack * 0.5f);

                wantedLeft += share;
                wantedRight += share;
            }
            else if (slack < 0f)
            {
                // Not enough room even measured, which means the column was squeezed rather than mis-sized.
                // Shrink both in proportion so neither is starved to make the other fit.
                float scale = room / Mathf.Max(1f, wantedLeft + wantedRight);

                wantedLeft *= scale;
                wantedRight *= scale;
            }

            Rect left = new Rect(cell.x + 4f, cell.y + (cell.height - ButtonHeight) * 0.5f,
                Mathf.Floor(wantedLeft), ButtonHeight);

            Rect right = new Rect(left.xMax + 6f, left.y, Mathf.Floor(wantedRight), ButtonHeight);

            switch (entry.Kind)
            {
                case CorpseKind.Ours:
                case CorpseKind.Guests:
                    Burial(left, entry, palette, all);
                    Stripping(right, entry, palette, all);

                    break;

                case CorpseKind.Hostiles:
                    Stripping(left, entry, palette, all);
                    Cremation(right, entry, palette, all);

                    break;

                default:
                    Butchering(left, entry, palette, all);
                    Burial(right, entry, palette, all);

                    break;
            }
        }

        /// <summary>
        /// What the two buttons on this row will say, so they can be measured before they are placed.
        ///
        /// <b>Separate from drawing rather than measured by drawing twice.</b> Calling the draw to find out how
        /// wide it wants to be paints a stray copy at the wrong position, which is the fault the hospital tab's
        /// pills had until <c>PillWidth</c> was split out.
        /// </summary>
        private static void Labels(CorpseEntry entry, string all, out string left, out string right)
        {
            switch (entry.Kind)
            {
                case CorpseKind.Ours:
                case CorpseKind.Guests:
                    left = entry.Buried ? "Buried" : "Bury" + all;
                    right = entry.StripQueued ? "Cancel" : "Strip" + all;

                    break;

                case CorpseKind.Hostiles:
                    left = entry.StripQueued ? "Cancel" : "Strip" + all;
                    right = "Cremate" + all;

                    break;

                default:
                    left = (entry.Kind == CorpseKind.Mechanoids ? "Shred" : "Butcher") + all;
                    right = entry.Buried ? "Buried" : "Bury" + all;

                    break;
            }
        }

        private static void Burial(Rect rect, CorpseEntry entry, UIColorPaletteDef palette, string all)
        {
            if (entry.Buried)
            {
                TabParts.Button(rect, "Buried", palette, false, false, "Already in a grave.");

                return;
            }

            if (TabParts.Button(rect, "Bury" + all, palette, entry.CanBury, false,
                    entry.CanBury
                        ? "Reserves a grave and sends somebody to carry the body to it."
                        : entry.BuryReason))
                CorpseActions.Bury(entry);
        }

        private static void Stripping(Rect rect, CorpseEntry entry, UIColorPaletteDef palette, string all)
        {
            if (entry.StripQueued)
            {
                if (TabParts.Button(rect, "Cancel", palette, true, false, "Calls off the stripping order."))
                    CorpseActions.CancelStrip(entry);

                return;
            }

            if (TabParts.Button(rect, "Strip" + all, palette, entry.Strippable, false,
                    entry.Strippable
                        ? "Marks the body to be stripped, the same as the strip tool would."
                        : "Nothing left to take."))
                CorpseActions.Strip(entry);
        }

        private static void Cremation(Rect rect, CorpseEntry entry, UIColorPaletteDef palette, string all)
        {
            if (TabParts.Button(rect, "Cremate" + all, palette, entry.CanCremate, false,
                    entry.CanCremate
                        ? "Adds a cremation bill for this kind of body to whichever crematorium has the shortest "
                          + "queue."
                        : entry.CremateReason))
                CorpseActions.Cremate(entry);
        }

        private static void Butchering(Rect rect, CorpseEntry entry, UIColorPaletteDef palette, string all)
        {
            string label = (entry.Kind == CorpseKind.Mechanoids ? "Shred" : "Butcher") + all;

            if (TabParts.Button(rect, label, palette, entry.CanButcher, false,
                    entry.CanButcher
                        ? "Adds a bill for this kind of body to the bench with the shortest queue."
                        : entry.ButcherReason))
                CorpseActions.Butcher(entry);
        }

        // ---------------------------------------------------------------------------------------
        // Cells: graves
        // ---------------------------------------------------------------------------------------

        private static void GraveCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            GraveRecord record = GraveOf(data);

            if (record == null)
                return;

            Rect band = new Rect(cell.x, cell.y + 6f, cell.width, cell.height - 12f);

            TabParts.Labelled(band, record.Sarcophagus ? "sarcophagus" : "grave", record.Label,
                palette.TextPrimary, palette);

            float y = band.y + 2f + CaptionHeight + ValueHeight;

            if (!record.Material.NullOrEmpty())
                y = TabParts.Line(new Rect(band.x + 6f, y, band.width - 10f, 0f), y, record.Material,
                    palette.TextDisabled, GameFont.Tiny);

            if (!record.Art.NullOrEmpty())
                TabParts.Line(new Rect(band.x + 6f, y, band.width - 10f, 0f), y, record.Art, palette.TextDisabled,
                    GameFont.Tiny);
        }

        private static void HoldsCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            GraveRecord record = GraveOf(data);

            if (record == null)
                return;

            Rect band = new Rect(cell.x, cell.y + 6f, cell.width, cell.height - 12f);

            string value = record.Corpse != null ? record.Occupied : "Empty";

            Color color = record.Corpse != null ? palette.TextPrimary : palette.TextDisabled;

            TabParts.Labelled(band, "holds", value, color, palette,
                record.Corpse != null && record.Occupant != null
                    ? record.Occupant.LabelCap + ", " + record.Died + "."
                    : null);

            if (record.Corpse == null)
                return;

            TabParts.Line(new Rect(band.x + 6f, band.y, band.width - 10f, 0f),
                band.y + 2f + CaptionHeight + ValueHeight, record.Died, palette.TextDisabled, GameFont.Tiny);
        }

        /// <summary>
        /// How the interred body is holding up.
        ///
        /// <b>A grave stops the weather getting at a body but it does not stop the clock.</b> Rot progresses
        /// inside a sarcophagus at the room's temperature exactly as it does outside; only the deterioration
        /// damage is prevented. So a colony keeping somebody for a resurrector serum is on the same countdown
        /// whether they buried them or not, and this is the only place that says so.
        /// </summary>
        private static void RestingCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            GraveRecord record = GraveOf(data);

            if (record == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 4f, cell.width - 8f, cell.height - 8f);

            if (record.Corpse == null)
                return;

            Caption(band, "condition", palette);

            float y = band.y + CaptionHeight;

            Rect pill = TabParts.Pill(band, band.x + 2f, y + 1f, CorpseFacts.StageTag(record.Stage),
                CorpseFacts.StageColor(record.Stage, palette), palette, band.width - 4f);

            TabParts.Line(new Rect(band.x + 2f, y, band.width - 4f, 0f), pill.yMax + 2f, record.RotNote,
                palette.TextDisabled, GameFont.Tiny);

            TooltipHandler.TipRegion(band, (TipSignal) (record.DaysRotted.ToString("0.0")
                                                        + " days of decay. A grave keeps the weather off a body; "
                                                        + "it does not slow the rot."));
        }

        /// <summary>
        /// Who this grave will take, as four toggles.
        ///
        /// <b>This is the whole of the management, and it is the answer to most empty graves.</b> A grave ships
        /// accepting humanlike corpses and a sarcophagus ships refusing strangers, so a colony that has never
        /// opened a grave's storage tab has a yard that will not take the dead muffalo or the dead raider and
        /// nothing anywhere saying why. The Bury button on the other view honours these, which is what makes them
        /// worth setting rather than a curiosity.
        /// </summary>
        private static void AcceptsCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            GraveRecord record = GraveOf(data);

            if (record == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 4f, cell.width - 8f, cell.height - 8f);

            Caption(band, "accepts", palette);

            if (record.Reserved != null)
            {
                TabParts.Line(band, band.y + CaptionHeight + 2f,
                    "Kept for " + record.Reserved.LabelShortCap + ", so nobody else can go in it",
                    palette.TextDisabled, GameFont.Tiny);

                return;
            }

            if (record.Corpse != null)
            {
                TabParts.Line(band, band.y + CaptionHeight + 2f, "Occupied", palette.TextDisabled,
                    GameFont.Tiny);

                return;
            }

            float x = band.x + 2f;
            float y = band.y + CaptionHeight + 2f;
            float height = UIFonts.LineHeightOf(GameFont.Tiny) + 4f;

            Audience(record, GraveAudience.Colonists, ref x, ref y, band, height, palette);
            Audience(record, GraveAudience.Strangers, ref x, ref y, band, height, palette);

            if (ModsConfig.IdeologyActive)
                Audience(record, GraveAudience.Slaves, ref x, ref y, band, height, palette);

            Audience(record, GraveAudience.Animals, ref x, ref y, band, height, palette);
        }

        /// <summary>One accepts toggle, wrapped in front of itself rather than behind it.</summary>
        private static void Audience(GraveRecord record, GraveAudience audience, ref float x, ref float y,
            Rect band, float height, UIColorPaletteDef palette)
        {
            string label = GraveActions.LabelOf(audience);

            float width = TabParts.PillWidth(label) + 8f;

            if (x > band.x + 2f && x + width > band.xMax)
            {
                x = band.x + 2f;
                y += height + 2f;
            }

            Rect pill = new Rect(x, y, width, height);

            x = pill.xMax + 4f;

            bool on = GraveActions.Accepts(record, audience);

            bool over = Mouse.IsOver(pill);

            if (on)
                UIElementPainter.FillRounded(pill, palette.AccentMuted);
            else
                UIElementPainter.OutlineRounded(pill, palette.Border,
                    over ? palette.SurfaceRaised : palette.SurfaceSunken);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = on ? palette.WindowBackground : palette.TextDisabled;

                UIRichText.Label(pill, label);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (Widgets.ButtonInvisible(pill))
                GraveActions.SetAccepts(record, audience, !on);
        }

        private static void KeptCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            GraveRecord record = GraveOf(data);

            if (record == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 4f, cell.width - 8f, cell.height - 8f);

            Caption(band, "kept for", palette);

            Rect button = new Rect(band.x + 2f, band.y + CaptionHeight + 2f, band.width - 4f, ButtonHeight);

            if (record.Corpse != null)
            {
                TabParts.Line(band, button.y + 3f, "-", palette.TextDisabled, GameFont.Tiny);

                return;
            }

            string label = record.Reserved != null ? record.Reserved.LabelShortCap.ToString() : "Anyone";

            if (!TabParts.Button(button, label, palette, true, false,
                    "Reserving a grave raises its priority and narrows it to one body, so haulers bring that "
                    + "one and no other."))
                return;

            GraveRecord captured = record;

            Dialog_PickBurial.For(record.Map, record.Reserved,
                pawn => GraveActions.Reserve(captured, pawn));
        }

        private static void GraveActionsCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            GraveRecord record = GraveOf(data);

            if (record == null)
                return;

            Rect button = new Rect(cell.x + 4f, cell.y + (cell.height - ButtonHeight) * 0.5f, cell.width - 8f,
                ButtonHeight);

            if (record.Corpse != null)
            {
                if (record.EmptyQueued)
                {
                    if (TabParts.Button(button, "Cancel", palette, true, false,
                            "Calls off opening the grave."))
                        GraveActions.SetEmptying(record, false);

                    return;
                }

                if (TabParts.Button(button, "Empty", palette, true, false,
                        "Sends somebody to open the grave and take the body out. The same job vanilla uses for "
                        + "any container, so it can be called off."))
                    GraveActions.SetEmptying(record, true);

                return;
            }

            if (record.Reserved != null)
            {
                if (TabParts.Button(button, "Release", palette, true, false,
                        "Stops keeping this grave for " + record.Reserved.LabelShortCap + "."))
                    GraveActions.Clear(record);

                return;
            }

            Corpse waiting = record.Waiting;

            bool can = waiting != null && waiting.InnerPawn != null;

            if (TabParts.Button(button, "Fill", palette, can, false,
                    can
                        ? "Gives this grave to " + waiting.InnerPawn.LabelShortCap
                          + ", who has waited longest of the bodies it will take."
                        : "No unburied body on this map would be accepted by this grave."))
                GraveActions.Reserve(record, waiting.InnerPawn);
        }
    }
}
