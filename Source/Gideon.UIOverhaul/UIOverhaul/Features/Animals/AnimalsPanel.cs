using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Pawns;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>Which of the two populations the list is showing.</summary>
    internal enum AnimalScope
    {
        All,
        Colony,
        Wild,

        /// <summary>The standing hunting orders, on their own.</summary>
        Bills
    }

    /// <summary>
    /// The animals tab: every animal the colony owns and every animal standing outside it, one row per species.
    ///
    /// <b>The species is the row and the individual is what you open.</b> Vanilla lists creatures, which means
    /// sixty rows across two tabs, each with twenty checkboxes, and a player who never once wanted to decide
    /// something about the fourth hare. Grouping turns that into ten rows and puts the decision where it is
    /// actually made: hunt four of those deer, put the muffalo in the north pasture, this species is over its cap.
    ///
    /// <b>There is no heading row, and that is what makes one list work for two populations.</b> A colony row and
    /// a wildlife row answer different questions in the same columns, so a pinned heading would have to be either
    /// wrong for one of them or so generic it said nothing. Each cell carries its own small caption instead, which
    /// is what the approved mockup showed and is the reason it could show both kinds together.
    ///
    /// <b>Rows are rebuilt every frame from a roster that is not.</b> The list here is layout, so it is cheap and
    /// disposable; the summarising behind it costs real work and happens twice a game second. See
    /// <see cref="AnimalRoster"/>.
    ///
    /// <b>What is open is remembered by identity, not by reference.</b> The roster recycles its group objects, so
    /// holding one across frames would eventually mean the opened row was a different species than the one the
    /// player clicked. <see cref="GroupKey"/> is the stable name for a species in a place.
    /// </summary>
    internal static class AnimalsPanel
    {
        // ---------------------------------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------------------------------

        private const float AnimalColumnWidth = 196f;
        private const float StateColumnWidth = 152f;
        private const float YieldColumnWidth = 116f;
        private const float WhereColumnWidth = 116f;
        // Wide enough for seven training pills and their caption: four is the usual number a species can take, and
        // Odyssey's special trainables push the ceiling up. Past seven the row draws what fits and the card carries
        // the rest, which is where every skill is listed with its name anyway. Widened from 232 when the switches
        // became pills, which cost three pixels each: at 232 the seventh fell off the end.
        private const float HandlingColumnWidth = 248f;
        private const float LimitColumnWidth = 158f;
        private const float ActColumnWidth = 136f;

        private const float WindowChrome = 24f;
        private const float PaneGap = 8f;
        private const float ToolbarHeight = 30f;
        private const float ToolbarGap = 6f;

        /// <summary>How many individuals an opened species lists before it stops and says how many are left.</summary>
        private const int MaxOpenedMembers = 14;

        private const float MemberRowHeight = 34f;

        /// <summary>
        /// The one line of state on an individual's row.
        ///
        /// Narrowed from 190 when the master moved out of it and onto its own chip: what is left is short by
        /// nature, since the longest thing it says is a pregnancy countdown.
        /// </summary>
        private const float StateLaneWidth = 150f;

        /// <summary>Room for "master" and a colonist's short name at Tiny, plus the caret.</summary>
        private const float MasterChipWidth = 118f;

        /// <summary>The caption above a value in a cell.</summary>
        private static float CaptionHeight => UIFonts.LineHeightOf(GameFont.Tiny);

        private static float ValueHeight => UIFonts.LineHeightOf(GameFont.Small);

        /// <summary>
        /// A row tall enough for a caption over a value, whatever the font situation is.
        ///
        /// Derived rather than a constant because Tiny is not always Tiny: a player who has turned tiny text off,
        /// or is playing in a language that cannot render it, gets Small for both lines and needs the extra
        /// height. A fixed 42 would have shaved the tops and bottoms off every caption on this tab for them.
        /// </summary>
        private static float RowHeight =>
            Mathf.Max(42f, CaptionHeight + Mathf.Max(ValueHeight, AnimalTrainingBoxes.PillHeight) + 6f);

        private static readonly UICardControl RowCard = new UICardControl { Padding = 0f, AccentWidth = 3f };

        private static readonly UIDesignatorTabControl Grid = new UIDesignatorTabControl
        {
            HasHeaderRow = false,
            RowGap = 2f,
            SectionHeaderHeight = 30f,
            AlternatingColumnBands = false
        };

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search animals",
            Icon = TexButton.Search,
            MaxLength = 30
        };

        internal static float WindowWidth
        {
            get
            {
                EnsureColumns();

                float wanted = Grid.RequestedWidth + WindowChrome;

                if (paneOpen)
                    wanted += AnimalSpeciesPane.PaneWidth + PaneGap;

                return Mathf.Min(wanted, UI.screenWidth - 16f);
            }
        }

        internal static float WindowHeight => Mathf.Min(760f, UI.screenHeight * 0.8f);

        /// <summary>Width held back for the pane, so a resized tab keeps its columns. See the pawns tab.</summary>
        internal static float PaneReservation => paneOpen ? AnimalSpeciesPane.PaneWidth + PaneGap : 0f;

        // ---------------------------------------------------------------------------------------
        // State
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// A species in a place, by identity rather than by reference.
        ///
        /// The roster reuses its group objects between rebuilds, so this is what "the row I opened" has to mean.
        /// A map and a caravan cannot share an id space, so both are carried.
        /// </summary>
        private struct GroupKey : IEquatable<GroupKey>
        {
            private readonly ThingDef def;
            private readonly AnimalKind kind;
            private readonly int map;
            private readonly int caravan;

            internal GroupKey(AnimalGroup group)
            {
                def = group?.Def;
                kind = group == null ? AnimalKind.Colony : group.Kind;
                map = group?.Map?.uniqueID ?? -1;
                caravan = group?.Caravan?.ID ?? -1;
            }

            internal bool Set => def != null;

            internal bool Matches(AnimalGroup group)
            {
                return Set && Equals(new GroupKey(group));
            }

            public bool Equals(GroupKey other)
            {
                return def == other.def && kind == other.kind && map == other.map && caravan == other.caravan;
            }

            public override bool Equals(object obj)
            {
                return obj is GroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                int hash = def == null ? 0 : def.shortHash;

                return (hash * 397 ^ (int) kind) * 397 ^ (map * 31 + caravan);
            }
        }

        private static AnimalScope scope = AnimalScope.All;

        private static GroupKey opened;
        private static GroupKey paneFor;
        private static bool paneOpen;

        /// <summary>The group the pane is drawing, found again each frame from <see cref="paneFor"/>.</summary>
        private static AnimalGroup paneGroup;

        /// <summary>
        /// The individual the pane is drawing, or null for the species.
        ///
        /// <b>Held by reference rather than by an identity key, unlike the group.</b> A group is rebuilt by the
        /// roster every thirty ticks and a stale one would silently start describing a different species, which is
        /// why those are matched by <see cref="GroupKey"/>. A pawn is not rebuilt: it is the same object for as long
        /// as it exists, and when it stops existing the card says so and closes itself.
        /// </summary>
        private static Pawn paneAnimal;

        /// <summary>
        /// Opens the tab on one of the two populations.
        ///
        /// Called by the redirect when somebody presses the key or the button for one of vanilla's animal tabs, so
        /// F4 still means the colony's animals and F5 still means what is outside. The scope then stays where they
        /// left it, because it is a view setting rather than a mode.
        /// </summary>
        internal static void ShowScope(AnimalScope which)
        {
            scope = which;
            Grid.Scroll = Vector2.zero;
        }

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        internal static void Draw(Rect inRect)
        {
            EnsureColumns();

            // Reapplied every frame rather than set once with the columns, because the row height is derived from
            // the font and the font can change while the tab is open: turning the tiny text accessibility setting
            // on would otherwise leave every closed row at the height the smaller font needed, with its captions
            // shaved off top and bottom.
            Grid.RowHeight = RowHeight;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            List<AnimalSection> sections = AnimalRoster.Sections;

            Collect(sections);

            Rect content = inRect.ContractedBy(6f);

            Rect toolbar = new Rect(content.x, content.y, content.width, ToolbarHeight);

            DrawToolbar(toolbar, sections, palette);

            content = new Rect(content.x, toolbar.yMax + ToolbarGap, content.width,
                Mathf.Max(0f, content.height - ToolbarHeight - ToolbarGap));

            // The pane takes its width off the right before the grid lays out, so the grid draws into what is left
            // rather than under it. The same order the pawns tab uses.
            if (paneOpen && paneGroup != null)
            {
                Rect pane = new Rect(content.xMax - AnimalSpeciesPane.PaneWidth, content.y,
                    AnimalSpeciesPane.PaneWidth, content.height);

                content = new Rect(content.x, content.y,
                    content.width - AnimalSpeciesPane.PaneWidth - PaneGap, content.height);

                // An individual has been picked out of the opened species, so the card takes the pane. Falling back
                // to the species when the card says it is finished is what happens when that animal is slaughtered,
                // tamed, or dies while its card is open: the herd it belonged to is the honest thing to show next.
                if (paneAnimal != null)
                {
                    if (!AnimalCardPane.Draw(pane, paneAnimal, paneGroup, palette, Changed, ShowSpecies))
                        ShowSpecies();
                }
                else if (!AnimalSpeciesPane.Draw(pane, paneGroup, palette, Changed))
                {
                    ClosePane();
                }
            }
            else if (paneOpen)
            {
                // The species the pane was open for is gone: tamed, hunted, or the last of them died. Closing is
                // the honest response, since there is nothing left to describe.
                ClosePane();
            }

            Grid.Draw(content, palette);

            // After the grid, so any scroll view a click happened inside has been closed out.
            PawnCameraJump.Resolve();
        }

        /// <summary>Drops cached readings after the player changes something through this tab.</summary>
        private static void Changed()
        {
            AnimalRoster.Invalidate();
        }

        private static void ClosePane()
        {
            paneOpen = false;
            paneFor = new GroupKey(null);
            paneGroup = null;
            paneAnimal = null;
        }

        /// <summary>Back from one animal to the species it belongs to, which is what the card's chip asks for.</summary>
        private static void ShowSpecies()
        {
            paneAnimal = null;
        }

        /// <summary>
        /// Opens the pane on a species, which is where its settings are.
        ///
        /// Clears any individual being shown: the button says Settings and the species settings are what it has to
        /// produce, not one animal's card that happens to be open from earlier.
        /// </summary>
        private static void ShowSpeciesSettings(AnimalGroup group)
        {
            paneFor = new GroupKey(group);
            paneAnimal = null;
            paneOpen = true;

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Opens the pane on one individual.
        ///
        /// The species is set too, so the card's way back always leads somewhere: an animal clicked in a row that
        /// is later closed still has a herd to return to.
        /// </summary>
        private static void ShowAnimal(AnimalGroup group, Pawn animal)
        {
            paneFor = new GroupKey(group);
            paneAnimal = animal;
            paneOpen = true;

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        // ---------------------------------------------------------------------------------------
        // Toolbar
        // ---------------------------------------------------------------------------------------

        private static readonly AnimalScope[] Scopes =
        {
            AnimalScope.All, AnimalScope.Colony, AnimalScope.Wild, AnimalScope.Bills
        };

        private static void DrawToolbar(Rect bar, List<AnimalSection> sections, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                if (Search.Draw(new Rect(bar.x, bar.y + 2f, 190f, 26f), palette))
                    Grid.Scroll = Vector2.zero;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;

                float x = bar.x + 198f;

                for (int i = 0; i < Scopes.Length; i++)
                {
                    AnimalScope which = Scopes[i];
                    string label = Caption(which);
                    float width = Text.CalcSize(label).x + 22f;
                    Rect button = new Rect(x, bar.y + 2f, width, 26f);

                    if (button.xMax > bar.xMax)
                        break;

                    bool on = scope == which;

                    // The mod's button with the chosen one toggled, rather than filled at full accent. A filled
                    // button is the one thing a window exists to do, and a scope switch is not that -- it decides
                    // what the list below shows. The control plays the click, so the one that was here is gone
                    // rather than doubled.
                    if (UIActionButtonControl.Draw(button, label, palette, false, true, GameFont.Small, null, on)
                        && !on)
                    {
                        scope = which;
                        Grid.Scroll = Vector2.zero;
                    }

                    x += width + 4f;
                }

                DrawReadouts(new Rect(x + 8f, bar.y, Mathf.Max(0f, bar.xMax - x - 8f), bar.height), sections,
                    palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static string Caption(AnimalScope which)
        {
            switch (which)
            {
                case AnimalScope.Colony: return "Colony";
                case AnimalScope.Wild: return "Wild";
                case AnimalScope.Bills: return "Bills";
                default: return "All";
            }
        }

        /// <summary>
        /// The four things worth knowing before reading a single row: how many animals there are, whether the
        /// pasture covers them, whether anything out there is dangerous, and what the standing hunts will yield.
        ///
        /// Drawn right to left, because the rightmost chip is the one that must never be pushed off the bar. On a
        /// narrow window the counts drop before the warnings do.
        /// </summary>
        private static void DrawReadouts(Rect area, List<AnimalSection> sections, UIColorPaletteDef palette)
        {
            int tame = 0;
            int wild = 0;
            int predators = 0;
            int huntOrdered = 0;
            float huntMeat = 0f;

            for (int s = 0; s < sections.Count; s++)
            {
                AnimalSection section = sections[s];

                for (int g = 0; g < section.Groups.Count; g++)
                {
                    AnimalGroup group = section.Groups[g];

                    if (section.Kind == AnimalKind.Colony)
                    {
                        tame += group.Count;

                        continue;
                    }

                    wild += group.Count;

                    if (group.Predator || group.Manhunters > 0)
                        predators += group.Count;

                    if (group.HuntOrdered <= 0)
                        continue;

                    huntOrdered += group.HuntOrdered;

                    // Meat per animal from the group total rather than per member, since the designated ones are
                    // whichever the picker chose and the average is the honest figure for a summary.
                    huntMeat += group.Meat / group.Count * group.HuntOrdered;
                }
            }

            PastureReading pasture = AnimalPasture.Worst(sections);

            float x = area.xMax;

            if (huntOrdered > 0)
                x = Chip(area, x, huntOrdered + " hunts, " + Mathf.RoundToInt(huntMeat) + " meat",
                    palette.Warning, palette);

            if (predators > 0)
                x = Chip(area, x, predators == 1 ? "1 predator nearby" : predators + " predators nearby",
                    palette.Danger, palette);

            if (pasture.Short)
                x = Chip(area, x, "Pasture short in " + pasture.WorstQuadrum.Label(), palette.Warning, palette);

            Chip(area, x, tame + " tame, " + wild + " wild", palette.TextSecondary, palette);
        }

        /// <summary>Draws one readout right aligned at <paramref name="right"/> and returns the next edge.</summary>
        private static float Chip(Rect area, float right, string text, Color color, UIColorPaletteDef palette)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            float width = Text.CalcSize(text).x + 16f;
            Rect chip = new Rect(right - width, area.y + 3f, width, area.height - 6f);

            if (chip.x < area.x)
                return right;

            UIElementPainter.OutlineRounded(chip, color, palette.PanelBackground);

            GUI.color = color;

            Widgets.Label(chip, text);

            GUI.color = Color.white;

            return chip.x - 4f;
        }

        // ---------------------------------------------------------------------------------------
        // Rows
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// A bill of either kind, or the row that makes one, as a grid row payload.
        ///
        /// One payload type for both because the row chrome, the buttons and the add row are identical and only
        /// the middle of the row differs. ForTaming is what an add row is asked, since it has neither bill to be
        /// read from.
        /// </summary>
        private sealed class BillRow
        {
            internal HuntingBill Bill;
            internal TamingBill Tame;
            internal Map Map;
            internal bool IsNew;
            internal bool ForTaming;
        }

        private static readonly List<BillRow> BillRows = new List<BillRow>();

        /// <summary>
        /// The colony or wildlife heading inside a place's heading.
        ///
        /// <b>A data row rather than a second level of section in the grid control.</b> That control gives one
        /// level of collapsible section and is shared with the work, pawns and architect tabs, so teaching it
        /// nesting to serve this tab would put a change under three shipped screens. A row this panel draws itself
        /// costs one payload type and keeps the fold state here, where the rule about what folds is already
        /// written.
        /// </summary>
        private sealed class SubHeader
        {
            /// <summary>What it is called. Derived from the section when there is one.</summary>
            internal string Label;

            internal string Suffix;

            /// <summary>Stable across rebuilds, so the fold survives the roster recycling its sections.</summary>
            internal string Key;

            /// <summary>The population this heading covers, or null for the hunting bills heading.</summary>
            internal AnimalSection Section;

            internal int Shown;
        }

        private const float SubHeaderHeight = 26f;

        /// <summary>Which population headings the player has folded away, by <see cref="SubHeader.Key"/>.</summary>
        private static readonly HashSet<string> Folded = new HashSet<string>();

        /// <summary>
        /// A name for one population in one place that survives a rebuild.
        ///
        /// Ids rather than the label, because two pocket maps can share a name and a caravan can be renamed while
        /// the tab is open, and either would move somebody's fold to a different section.
        /// </summary>
        private static string FoldKey(AnimalSection section)
        {
            int map = section.Map?.uniqueID ?? -1;
            int caravan = section.Caravan?.ID ?? -1;

            return map + "/" + caravan + "/" + (int) section.Kind;
        }

        private static bool SamePlace(AnimalSection a, AnimalSection b)
        {
            return a.Map == b.Map && a.Caravan == b.Caravan;
        }

        /// <summary>Everything in this place that the current scope shows, for the place heading.</summary>
        private static string PlaceSuffix(List<AnimalSection> sections, AnimalSection place)
        {
            int tame = 0;
            int wild = 0;

            for (int s = 0; s < sections.Count; s++)
            {
                AnimalSection section = sections[s];

                if (!SamePlace(place, section) || !InScope(section.Kind))
                    continue;

                if (section.Kind == AnimalKind.Colony)
                    tame += section.Animals;
                else
                    wild += section.Animals;
            }

            if (wild == 0)
                return tame == 1 ? "1 animal" : tame + " animals";

            return tame == 0 ? wild + " wild" : tame + " tame, " + wild + " wild";
        }

        private static void Collect(List<AnimalSection> sections)
        {
            Grid.Rows.Clear();
            BillRows.Clear();

            // A match inside a folded section would not be shown, which reads as the search failing. The folds
            // themselves are remembered and come back when the box is cleared.
            Grid.SuppressCollapse = !Search.IsEmpty;

            paneGroup = null;

            if (scope == AnimalScope.Bills)
            {
                CollectBills(sections);

                return;
            }

            AnimalSection place = null;

            for (int s = 0; s < sections.Count; s++)
            {
                AnimalSection section = sections[s];

                if (!InScope(section.Kind))
                    continue;

                List<AnimalGroup> shown = Matching(section);

                if (shown.Count == 0)
                    continue;

                // A heading per place, then one per population inside it. Asked for on 2026-08-22, replacing a
                // single level that read "Mountainhome wildlife": a colony with three maps wants the map to be the
                // thing it folds, and the two populations to be what it folds inside that.
                if (place == null || !SamePlace(place, section))
                {
                    place = section;

                    Grid.Rows.Add(new UIDesignatorTabRow
                    {
                        SectionLabel = section.Label,
                        SectionSuffix = PlaceSuffix(sections, section)
                    });

                    // Bills belong to a place rather than to a population, so they are listed once under the place
                    // heading rather than inside either half. Only where they are worth offering, or every quest
                    // map still loaded would carry an empty one.
                    if (scope == AnimalScope.All && WorthBills(section.Map))
                        Bills(section.Map, false);
                }

                string key = FoldKey(section);
                bool folded = Folded.Contains(key) && Search.IsEmpty;

                Grid.Rows.Add(new UIDesignatorTabRow
                {
                    Payload = new SubHeader
                    {
                        Label = section.Kind == AnimalKind.Colony ? "Colony animals" : "Wildlife",
                        Suffix = Suffix(section, shown),
                        Key = key,
                        Section = section,
                        Shown = shown.Count
                    },
                    Height = SubHeaderHeight,
                    DrawBackground = DrawSubHeader
                });

                if (folded)
                    continue;

                for (int g = 0; g < shown.Count; g++)
                {
                    AnimalGroup group = shown[g];

                    if (paneFor.Matches(group))
                        paneGroup = group;

                    bool open = opened.Matches(group);

                    Grid.Rows.Add(new UIDesignatorTabRow
                    {
                        Payload = group,
                        Height = open ? RowHeight + OpenedHeight(group) : (float?) null,
                        DrawBackground = DrawRowBackground,
                        DrawOverlay = open
                            ? (Action<Rect, UIDesignatorTabRow, UIColorPaletteDef>) DrawOpened
                            : null
                    });
                }
            }
        }

        /// <summary>
        /// Whether a map is worth offering hunting bills on.
        ///
        /// A home map always is, since that is where a larder is kept. Anywhere else earns a heading only once it
        /// has a bill of its own, because a colony that has visited a dozen quest maps should not be reading a
        /// dozen empty headings to find the one that matters.
        /// </summary>
        private static bool WorthBills(Map map)
        {
            if (map == null)
                return false;

            if (map.IsPlayerHome)
                return true;

            MapComponent_HuntingBills hunting = MapComponent_HuntingBills.For(map);

            if (hunting != null && hunting.Bills.Count > 0)
                return true;

            // Taming counts too, or a map that is not a player home but carries a taming bill would hide the
            // bill that is running on it.
            MapComponent_TamingBills taming = MapComponent_TamingBills.For(map);

            return taming != null && taming.Bills.Count > 0;
        }

        /// <summary>How many standing orders of both kinds a map has, for the place heading's count.</summary>
        private static int Total(Map map)
        {
            MapComponent_HuntingBills hunting = MapComponent_HuntingBills.For(map);
            MapComponent_TamingBills taming = MapComponent_TamingBills.For(map);

            return (hunting == null ? 0 : hunting.Bills.Count) + (taming == null ? 0 : taming.Bills.Count);
        }

        /// <summary>The bills scope: the standing orders, and nothing else.</summary>
        private static void CollectBills(List<AnimalSection> sections)
        {
            List<Map> maps = Find.Maps;

            if (maps == null)
                return;

            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];

                if (!WorthBills(map))
                    continue;

                Bills(map, true);
            }
        }

        /// <summary>
        /// Adds a map's hunting bills, then the row that adds another.
        ///
        /// <b>A population heading like the other two.</b> Bills sit under the place they belong to, so folding a
        /// map folds its bills with its animals, and the bills fold on their own inside that. In the bills scope
        /// there is no place heading yet, so this draws one first.
        /// </summary>
        private static void Bills(Map map, bool ownPlaceHeading)
        {
            MapComponent_HuntingBills component = MapComponent_HuntingBills.For(map);

            if (component == null)
                return;

            List<HuntingBill> bills = component.Bills;

            if (ownPlaceHeading)
            {
                Grid.Rows.Add(new UIDesignatorTabRow
                {
                    SectionLabel = MapLabels.NameOf(map),
                    // Both kinds, because both hang under this heading. Counting only the hunting ones would
                    // have a place read "no bills" with a taming bill visibly sitting inside it.
                    SectionSuffix = Total(map) == 1 ? "1 bill" : Total(map) + " bills"
                });
            }

            string key = (map?.uniqueID ?? -1) + "/bills";
            bool folded = Folded.Contains(key) && Search.IsEmpty;

            Grid.Rows.Add(new UIDesignatorTabRow
            {
                Payload = new SubHeader
                {
                    Label = "Hunting bills",
                    Suffix = bills.Count == 0
                        ? "none yet"
                        : bills.Count == 1 ? "1 bill" : bills.Count + " bills",
                    Key = key
                },
                Height = SubHeaderHeight,
                DrawBackground = DrawSubHeader
            });

            if (folded)
                return;

            for (int i = 0; i < bills.Count; i++)
            {
                BillRow row = new BillRow { Bill = bills[i], Map = map };

                BillRows.Add(row);

                Grid.Rows.Add(new UIDesignatorTabRow
                {
                    Payload = row,
                    Height = RowHeight,
                    DrawBackground = DrawBillRow
                });
            }

            BillRow add = new BillRow { Map = map, IsNew = true };

            BillRows.Add(add);

            Grid.Rows.Add(new UIDesignatorTabRow
            {
                Payload = add,
                Height = Mathf.Max(28f, ValueHeight + 8f),
                DrawBackground = DrawBillRow
            });

            TamingBills(map);
        }

        /// <summary>
        /// A map's taming bills, under their own foldable sub-heading beside the hunting ones.
        ///
        /// <b>A second sub-heading rather than one list of both.</b> They are different orders that happen to
        /// live in the same place: one sends somebody out with a rifle and the other with a handful of kibble,
        /// and a player looking for one does not want to read past the other. Separate headings also means
        /// separate folding, so a colony that has settled its herd can put taming away and keep hunting open.
        /// </summary>
        private static void TamingBills(Map map)
        {
            MapComponent_TamingBills component = MapComponent_TamingBills.For(map);

            if (component == null)
                return;

            List<TamingBill> bills = component.Bills;

            string key = (map?.uniqueID ?? -1) + "/taming";
            bool folded = Folded.Contains(key) && Search.IsEmpty;

            Grid.Rows.Add(new UIDesignatorTabRow
            {
                Payload = new SubHeader
                {
                    Label = "Taming bills",
                    Suffix = bills.Count == 0
                        ? "none yet"
                        : bills.Count == 1 ? "1 bill" : bills.Count + " bills",
                    Key = key
                },
                Height = SubHeaderHeight,
                DrawBackground = DrawSubHeader
            });

            if (folded)
                return;

            for (int i = 0; i < bills.Count; i++)
            {
                BillRow row = new BillRow { Tame = bills[i], Map = map, ForTaming = true };

                BillRows.Add(row);

                Grid.Rows.Add(new UIDesignatorTabRow
                {
                    Payload = row,
                    Height = RowHeight,
                    DrawBackground = DrawBillRow
                });
            }

            BillRow add = new BillRow { Map = map, IsNew = true, ForTaming = true };

            BillRows.Add(add);

            Grid.Rows.Add(new UIDesignatorTabRow
            {
                Payload = add,
                Height = Mathf.Max(28f, ValueHeight + 8f),
                DrawBackground = DrawBillRow
            });
        }

        private static bool InScope(AnimalKind kind)
        {
            switch (scope)
            {
                case AnimalScope.Colony: return kind == AnimalKind.Colony;
                case AnimalScope.Wild: return kind == AnimalKind.Wild;
                case AnimalScope.Bills: return false;
                default: return true;
            }
        }

        private static readonly List<AnimalGroup> Shown = new List<AnimalGroup>();

        /// <summary>
        /// The groups in a section that survive the search box.
        ///
        /// A species matches on its own name or on any of its members' names, so searching a muffalo's name finds
        /// the muffalo row rather than nothing. The group is kept whole either way: filtering a species down to
        /// the one animal whose name matched would misreport every figure on the row.
        /// </summary>
        private static List<AnimalGroup> Matching(AnimalSection section)
        {
            Shown.Clear();

            for (int g = 0; g < section.Groups.Count; g++)
            {
                AnimalGroup group = section.Groups[g];

                if (Search.IsEmpty || Search.Matches(group.Def.label) || AnyName(group))
                    Shown.Add(group);
            }

            return Shown;
        }

        private static bool AnyName(AnimalGroup group)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                if (PawnSearch.Matches(Search, group.Members[i]))
                    return true;
            }

            return false;
        }

        private static string Suffix(AnimalSection section, List<AnimalGroup> shown)
        {
            int animals = 0;

            for (int i = 0; i < shown.Count; i++)
                animals += shown[i].Count;

            if (section.Kind == AnimalKind.Colony)
                return animals == 1 ? "1 animal" : animals + " animals";

            float meat = 0f;

            for (int i = 0; i < shown.Count; i++)
                meat += shown[i].Meat;

            return animals + " wild, " + Mathf.RoundToInt(meat) + " meat";
        }

        private static float OpenedHeight(AnimalGroup group)
        {
            int rows = Mathf.Min(group.Count, MaxOpenedMembers);

            if (group.Count > MaxOpenedMembers)
                rows++;

            return rows * MemberRowHeight + 8f;
        }

        // ---------------------------------------------------------------------------------------
        // Columns
        // ---------------------------------------------------------------------------------------

        private static void EnsureColumns()
        {
            if (Grid.Columns.Count == 7)
                return;

            Grid.Columns.Clear();
            Grid.RowHeight = RowHeight;

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Width = AnimalColumnWidth, Bandable = false, DrawCell = DrawAnimalCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn { Width = StateColumnWidth, DrawCell = DrawStateCell });
            Grid.Columns.Add(new UIDesignatorTabColumn { Width = YieldColumnWidth, DrawCell = DrawYieldCell });
            Grid.Columns.Add(new UIDesignatorTabColumn { Width = WhereColumnWidth, DrawCell = DrawWhereCell });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Width = HandlingColumnWidth, DrawCell = DrawHandlingCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn { Width = LimitColumnWidth, DrawCell = DrawLimitCell });
            Grid.Columns.Add(new UIDesignatorTabColumn { Width = ActColumnWidth, DrawCell = DrawActCell });
        }

        /// <summary>
        /// The top slice of a cell, which is where its content goes.
        ///
        /// An opened row is taller than a closed one and every cell is handed the whole row, so trimming keeps the
        /// values where they were before it opened. The same arrangement the pawns tab uses.
        /// </summary>
        private static Rect TopBand(Rect cell)
        {
            return new Rect(cell.x, cell.y, cell.width, Mathf.Min(RowHeight, cell.height));
        }

        private static AnimalGroup GroupOf(UIDesignatorTabRow data)
        {
            return data?.Payload as AnimalGroup;
        }

        /// <summary>
        /// A caption over a value, which is this tab's whole cell vocabulary.
        ///
        /// The caption is what a heading row would have said, said per cell instead, because the two kinds of row
        /// need different words in the same column. It is drawn in the dimmest text colour the palette has, so a
        /// column of them reads as furniture rather than as content.
        /// </summary>
        private static void DrawLabelled(Rect cell, string caption, string value, Color color,
            UIColorPaletteDef palette, string tip = null)
        {
            Rect band = TopBand(cell);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Rect top = new Rect(band.x + 6f, band.y + 2f, Mathf.Max(0f, band.width - 10f), CaptionHeight);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextDisabled;

                if (!caption.NullOrEmpty())
                    Widgets.LabelEllipses(top, caption);

                Rect bottom = new Rect(top.x, top.yMax, top.width, ValueHeight);

                Text.Font = GameFont.Small;
                GUI.color = color;

                if (!value.NullOrEmpty())
                    Widgets.LabelEllipses(bottom, value);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(band, (TipSignal) tip);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        // ---------------------------------------------------------------------------------------
        // The row itself
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The row's card and the click that opens it.
        ///
        /// <b>The stripe says what kind of row this is.</b> Green for the colony's own, amber for wildlife, red
        /// for anything with a predator or a manhunter in it, which is the one distinction worth having at a
        /// glance across a long list.
        /// </summary>
        private static void DrawRowBackground(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            AnimalGroup group = GroupOf(data);

            if (group == null)
                return;

            RowCard.AccentColor = Accent(group, palette);
            RowCard.BackgroundColor = palette.PanelBackground;
            RowCard.DrawChrome(row, palette);

            Rect band = new Rect(row.x, row.y, row.width, Mathf.Min(RowHeight, row.height));

            if (opened.Matches(group) || paneFor.Matches(group))
                Widgets.DrawBoxSolid(band, palette.SelectionOverlay);

            // The act column is cut out of the row's hit target by geometry rather than by draw order: this
            // background is painted before any cell, so its button would otherwise swallow every stepper click.
            // The same fault the pawns tab's area column had.
            //
            // Measured from the columns rather than from the band, because the band is as wide as the window: the
            // control lays rows out across Mathf.Max(ColumnsWidth, available), so on a tab dragged wider than its
            // columns, band.width - ActColumnWidth would cut a strip of empty space and leave the steppers dead.
            float right = Mathf.Min(band.xMax, band.x + Grid.ColumnsWidth - ActColumnWidth);
            Rect click = new Rect(band.x, band.y, Mathf.Max(0f, right - band.x), band.height);

            if (Widgets.ButtonInvisible(click))
                Toggle(group);
        }

        /// <summary>
        /// A population heading inside a place: an arrow, a name, and what is in it.
        ///
        /// Indented and quieter than the place heading above it, so the two levels read as a hierarchy rather than
        /// as two headings that happen to be adjacent. The whole row folds it, because a 26px row with a 16px
        /// arrow in it is not a target anybody should have to aim at.
        /// </summary>
        private static void DrawSubHeader(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            SubHeader header = data?.Payload as SubHeader;

            if (header == null)
                return;

            bool folded = Folded.Contains(header.Key);
            bool over = Mouse.IsOver(row);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Rect arrow = new Rect(row.x + 14f, row.center.y - 8f, 16f, 16f);
                Texture2D texture = folded ? TexButton.Reveal : TexButton.Collapse;

                if (texture != null)
                {
                    GUI.color = over ? palette.TextPrimary : palette.TextSecondary;

                    GUI.DrawTexture(arrow, texture);
                }

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = over ? palette.TextPrimary : palette.TextSecondary;

                Widgets.LabelEllipses(new Rect(arrow.xMax + 6f, row.y, 220f, row.height), header.Label);

                if (!header.Suffix.NullOrEmpty())
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = palette.TextDisabled;

                    float right = Mathf.Min(row.xMax, row.x + Grid.ColumnsWidth);

                    Widgets.Label(new Rect(right - 220f, row.y, 214f, row.height), header.Suffix);
                }

                GUI.color = palette.Border;

                Widgets.DrawLineHorizontal(arrow.xMax + 6f, row.yMax - 1f,
                    Mathf.Max(0f, Mathf.Min(row.xMax, row.x + Grid.ColumnsWidth) - arrow.xMax - 6f));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!Widgets.ButtonInvisible(row))
                return;

            if (folded)
                Folded.Remove(header.Key);
            else
                Folded.Add(header.Key);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static Color Accent(AnimalGroup group, UIColorPaletteDef palette)
        {
            if (group.Manhunters > 0 || group.Predator)
                return palette.Danger;

            return group.Kind == AnimalKind.Colony ? palette.Success : palette.Warning;
        }

        private static void Toggle(AnimalGroup group)
        {
            if (opened.Matches(group))
            {
                opened = new GroupKey(null);

                // The individual's card came from inside this row, so folding the row puts the pane back on the
                // species rather than leaving a card open for an animal that is no longer listed.
                paneAnimal = null;

                return;
            }

            opened = new GroupKey(group);
            paneFor = opened;
            paneOpen = true;
            paneAnimal = null;

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// The species, its count, and the shape of the group.
        ///
        /// The icon is the first member drawn through vanilla's own thing icon, so a muffalo looks like the
        /// muffalo it is, coat colour included, rather than like a generic silhouette.
        /// </summary>
        private static void DrawAnimalCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            AnimalGroup group = GroupOf(data);

            if (group == null)
                return;

            Rect band = TopBand(cell);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Rect caret = new Rect(band.x + 6f, band.center.y - 8f, 16f, 16f);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;

                // RimWorld's own fold textures rather than a triangle glyph. The mockup drew a caret, and a caret
                // character is a gamble: anything outside the game's bitmap font atlas renders as a hollow box,
                // which is how a tidy looking string becomes a rendering fault in one language and not another.
                // These are the same two textures the pawns tab folds with.
                Texture2D arrow = opened.Matches(group) ? TexButton.Collapse : TexButton.Reveal;

                if (arrow != null)
                {
                    GUI.color = palette.TextSecondary;

                    GUI.DrawTexture(caret, arrow);
                }

                Rect icon = new Rect(caret.xMax + 2f, band.center.y - 13f, 26f, 26f);

                if (group.Members.Count > 0)
                    Widgets.ThingIcon(icon, group.Members[0]);

                Rect name = new Rect(icon.xMax + 6f, band.y + 2f,
                    Mathf.Max(0f, band.xMax - icon.xMax - 10f), CaptionHeight + 2f);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                Widgets.LabelEllipses(name, group.Def.LabelCap + "  " + group.Count);

                Rect mix = new Rect(name.x, name.yMax, name.width, CaptionHeight);

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextSecondary;

                Widgets.LabelEllipses(mix, Breakdown(group));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>Females, males and juveniles, which is the breakdown breeding and slaughter both turn on.</summary>
        private static string Breakdown(AnimalGroup group)
        {
            string text = group.Females + "f  " + group.Males + "m";

            if (group.Young > 0)
                text += "  " + group.Young + " young";

            return text;
        }

        /// <summary>
        /// The worst thing true of anybody in the group, or nothing at all when the answer is that they are fine.
        ///
        /// <b>Ordered by what would make somebody act now.</b> A manhunter outranks a wound, a wound outranks
        /// hunger, and a pregnancy is news rather than a problem. Only one line fits, so the order is the whole
        /// design of this cell.
        /// </summary>
        private static void DrawStateCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            AnimalGroup group = GroupOf(data);

            if (group == null)
                return;

            string caption = "State";
            string value;
            Color color = palette.TextSecondary;

            if (group.Manhunters > 0)
            {
                value = group.Manhunters == 1 ? "1 manhunter" : group.Manhunters + " manhunters";
                color = palette.Danger;
            }
            else if (group.Downed > 0)
            {
                value = group.Downed + " down";
                color = palette.Danger;
            }
            else if (group.NeedsTending > 0)
            {
                value = group.NeedsTending + " need tending";
                color = palette.Danger;
            }
            else if (group.Starving > 0)
            {
                value = group.Starving + " starving";
                color = palette.Warning;
            }
            else if (group.InMentalBreak > 0)
            {
                value = group.InMentalBreak + " breaking";
                color = palette.Warning;
            }
            else if (group.Hunting > 0)
            {
                value = group.Hunting == 1 ? "hunting" : group.Hunting + " hunting";
                color = palette.Warning;
            }
            else if (group.Pregnant > 0)
            {
                value = group.Pregnant + " pregnant";
                color = palette.TextPrimary;
            }
            else if (group.Kind == AnimalKind.Wild)
            {
                value = "quiet";
            }
            else
            {
                value = "fine";
                color = palette.Success;
            }

            DrawLabelled(cell, caption, value, color, palette);
        }

        /// <summary>
        /// What the group is worth: production for the colony's own, meat and hide for wildlife.
        ///
        /// Wildlife shows the whole group's meat, because the question is whether hunting them is worth the trip.
        /// Colony animals show what is next off them and when.
        /// </summary>
        private static void DrawYieldCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            AnimalGroup group = GroupOf(data);

            if (group == null)
                return;

            if (group.Kind == AnimalKind.Wild)
            {
                string hide = group.LeatherLabel.NullOrEmpty()
                    ? "no hide"
                    : Mathf.RoundToInt(group.Leather) + " " + group.LeatherLabel;

                DrawLabelled(cell, "Meat, group", Mathf.RoundToInt(group.Meat).ToString(), palette.TextPrimary,
                    palette, "Butchering all " + group.Count + " would yield about "
                             + Mathf.RoundToInt(group.Meat) + " meat and " + hide + ".");

                return;
            }

            if (!group.Produce.Any)
            {
                DrawLabelled(cell, "Yield", "none", palette.TextDisabled, palette);

                return;
            }

            if (group.ReadyToGather > 0)
            {
                DrawLabelled(cell, group.Produce.ResourceLabel, group.ReadyToGather + " ready", palette.Success,
                    palette, group.ReadyToGather + " of " + group.Count + " are ready to be gathered.");

                return;
            }

            if (group.Produce.PerDay > 0f && group.Produce.DaysLeft <= 0f)
            {
                DrawLabelled(cell, group.Produce.ResourceLabel,
                    group.ProducePerDay.ToString("0.#") + " a day", palette.TextPrimary, palette);

                return;
            }

            DrawLabelled(cell, group.Produce.ResourceLabel, "in " + Days(group.Produce.DaysLeft),
                palette.TextPrimary, palette,
                "The whole group produces about " + group.ProducePerDay.ToString("0.#") + " a day.");
        }

        private static string Days(float days)
        {
            if (days < 1f)
                return Mathf.Max(1, Mathf.RoundToInt(days * 24f)) + "h";

            return days.ToString("0.#") + "d";
        }

        /// <summary>Pen or area for the colony's own, distance from home for wildlife.</summary>
        private static void DrawWhereCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            AnimalGroup group = GroupOf(data);

            if (group == null)
                return;

            if (group.Kind == AnimalKind.Wild)
            {
                if (group.NearestDistance < 0)
                {
                    DrawLabelled(cell, "Distance", "unknown", palette.TextDisabled, palette);

                    return;
                }

                bool close = group.NearestDistance <= 15;

                DrawLabelled(cell, "Nearest", group.NearestDistance + " tiles",
                    close && (group.Predator || group.Manhunters > 0) ? palette.Danger : palette.TextPrimary,
                    palette, "Measured from the middle of your home area.");

                return;
            }

            if (group.Caravan != null)
            {
                DrawLabelled(cell, "Where", "travelling", palette.TextSecondary, palette);

                return;
            }

            if (group.PenMixed)
            {
                DrawLabelled(cell, "Pen", "mixed", palette.Warning, palette,
                    "These are in different pens. Use the menu to put them all in one.");

                return;
            }

            if (group.Pen != null)
            {
                DrawLabelled(cell, "Pen", group.Pen.RenamableLabel, palette.TextPrimary, palette);

                return;
            }

            if (group.Unpenned > 0)
            {
                string note = group.Unpenned + " of these need a pen and are not in one.";

                if (group.AreaHeld > 0)
                    note += "\n\n" + group.AreaHeld + " of them are held by an allowed area instead.";
                else if (group.Area != null || group.AreaMixed)
                {
                    // An area is set on them and is doing nothing, which is the confusing state worth naming
                    // rather than leaving somebody to work out. RimWorld ignores an area on a roamer unless the
                    // setting is on, so the row would otherwise read as the assignment having been lost.
                    note += "\n\nThese have an allowed area set, but RimWorld does not hold roaming livestock to "
                            + "an area. Switch on \"Let an allowed area keep livestock\" under Additional "
                            + "Features, Animals, in this mod's options to make it count.";
                }

                DrawLabelled(cell, "Pen", "none", palette.Warning, palette, note);

                return;
            }

            string area = group.AreaMixed
                ? "mixed"
                : group.Area != null ? group.Area.Label : "unrestricted";

            // Livestock kept by an area rather than a pen says so, because "Area: Barnyard" on a row that would
            // otherwise read "Pen: none" is the difference between a solved problem and an unsolved one. Only
            // reachable with the setting on, since nothing else lets an area hold a roamer.
            // Livestock kept by an area reads as an area and nothing else. This said "Area, no pen" for one build
            // and Aaron read it as the pen warning it had replaced, which is fair: half the caption was the thing
            // that had just been fixed. The pen is still worth knowing about, so it is in the tooltip.
            if (group.AreaHeld > 0)
            {
                DrawLabelled(cell, "Area", area, palette.TextPrimary, palette,
                    group.AreaHeld + (group.AreaHeld == 1 ? " of these is" : " of these are")
                    + " a roamer held by an allowed area rather than by a pen, which keeps them on the map. A pen "
                    + "is still where they graze, so a species you want fed from a pasture wants one anyway.");

                return;
            }

            DrawLabelled(cell, "Area", area, group.AreaMixed ? palette.Warning : palette.TextPrimary, palette);
        }

        /// <summary>
        /// Training for the colony's own, tameability for wildlife.
        ///
        /// <b>The training reading counts what is at risk, not what is trained.</b> Five filled bars on an
        /// obedient husky is decoration; a husky that drops a step in a day and a half is the reason to have
        /// opened the tab.
        /// </summary>
        private static void DrawHandlingCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            AnimalGroup group = GroupOf(data);

            if (group == null)
                return;

            if (group.Kind == AnimalKind.Wild)
            {
                string caption = "Wildness " + group.Wildness.ToStringPercent();

                if (!group.TameOdds.Known)
                {
                    DrawLabelled(cell, caption, group.Trainability ?? "untrainable", palette.TextSecondary,
                        palette);

                    return;
                }

                bool able = group.TameOdds.AnyoneSkilledEnough;

                DrawLabelled(cell, caption, group.TameOdds.Chance.ToStringPercent() + " to tame",
                    able ? palette.TextPrimary : palette.TextDisabled, palette,
                    able
                        ? "Best handler: " + group.TameOdds.Handler.LabelShortCap + ", animals "
                          + group.TameOdds.HandlerSkill + "."
                        : "Nobody has the animals skill of " + group.TameOdds.MinSkill
                          + " this species needs, so no attempt will be made.");

                return;
            }

            // The caption carries the news and the boxes carry the controls. Asked for on 2026-08-22: training
            // requests are now set from the row rather than through a menu, one click per skill for the whole
            // species, so the only thing left for the caption is what the player did not already know.
            Rect band = TopBand(cell);

            string note;
            Color color;

            if (group.TrainingAtRisk > 0)
            {
                note = group.TrainingAtRisk + " at risk in " + Days(Mathf.Max(0f, group.SoonestDecayDays));
                color = palette.Danger;
            }
            else if (group.FullyTrained > 0)
            {
                note = group.FullyTrained + " of " + group.Count + " trained";
                color = palette.TextSecondary;
            }
            else
            {
                note = group.Trainability.NullOrEmpty() ? "Training" : group.Trainability;
                color = palette.TextDisabled;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = color;

                Widgets.LabelEllipses(new Rect(band.x + 6f, band.y + 2f, Mathf.Max(0f, band.width - 10f),
                    CaptionHeight), note);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (group.TrainingAtRisk > 0)
            {
                TooltipHandler.TipRegion(band, (TipSignal)
                    ("One training step from forgetting something. Which skill goes is decided at the moment it "
                     + "happens, so it cannot be named in advance."));
            }

            Rect boxes = new Rect(band.x + 6f, band.y + CaptionHeight, Mathf.Max(0f, band.width - 10f),
                Mathf.Max(AnimalTrainingBoxes.PillHeight, ValueHeight));

            AnimalTrainingBoxes.DrawForGroup(boxes, group, palette, Changed);
        }

        /// <summary>The auto slaughter limit for the colony's own, the threat for wildlife.</summary>
        private static void DrawLimitCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            AnimalGroup group = GroupOf(data);

            if (group == null)
                return;

            if (group.Kind == AnimalKind.Wild)
            {
                if (group.Predator)
                {
                    DrawLabelled(cell, "Threat", "predator", palette.Danger, palette,
                        "Hunts your animals and colonists. Retaliates when shot "
                        + group.ManhunterOnDamage.ToStringPercent() + " of the time.");

                    return;
                }

                if (group.ManhunterOnDamage >= 0.015f)
                {
                    DrawLabelled(cell, "If shot", group.ManhunterOnDamage.ToStringPercent() + " manhunter",
                        palette.Warning, palette,
                        "Failing a taming attempt turns them manhunter "
                        + group.ManhunterOnTameFail.ToStringPercent() + " of the time.");

                    return;
                }

                DrawLabelled(cell, "Threat", "harmless", palette.TextSecondary, palette);

                return;
            }

            PastureReading pasture = AnimalPasture.ForGroup(group);

            if (pasture.Short)
            {
                int over = AnimalPasture.ShortBy(pasture, group);

                DrawLabelled(cell, pasture.WorstQuadrum.Label() + " pasture",
                    over > 0 ? over + " too many" : "short", palette.Warning, palette,
                    "The pen grows " + pasture.WorstGrown.ToString("0.##")
                    + " nutrition a day in " + pasture.WorstQuadrum.Label() + " and this pen eats "
                    + pasture.ConsumptionPerDay.ToString("0.##") + ".");

                return;
            }

            if (group.Limits == null)
            {
                DrawLabelled(cell, "Cap", "no limit", palette.TextDisabled, palette);

                return;
            }

            if (group.Limits.maxTotal < 0)
            {
                DrawLabelled(cell, "Cap", "no limit", palette.TextDisabled, palette,
                    "Set a limit in the pane to have surplus animals slaughtered automatically.");

                return;
            }

            DrawLabelled(cell, "Cap", group.Count + " of " + group.Limits.maxTotal,
                group.OverLimit ? palette.Warning : palette.TextPrimary, palette,
                group.OverLimit
                    ? "Over the limit, so the surplus will be slaughtered."
                    : "The auto slaughter limit for this species.");
        }

        // ---------------------------------------------------------------------------------------
        // The act column
        // ---------------------------------------------------------------------------------------

        private static void DrawActCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            AnimalGroup group = GroupOf(data);

            if (group == null)
                return;

            Rect band = TopBand(cell);

            if (group.Kind == AnimalKind.Colony)
            {
                Rect button = new Rect(band.x + 4f, band.center.y - 11f, Mathf.Max(0f, band.width - 8f), 22f);

                // Opens the pane on this species rather than a menu. The settings moved into the pane on
                // 2026-08-22, so this button now goes where they went: a menu here and a settings block there
                // would have been two ways to set one species, which is the thing the move was for.
                if (UIActionButtonControl.Draw(button, "Settings", palette, false, true, GameFont.Tiny))
                    ShowSpeciesSettings(group);

                return;
            }

            // Two steppers, stacked, because a wildlife row's whole purpose is these two numbers and there is not
            // room for them side by side at a width the other columns can afford.
            float height = Mathf.Min(20f, (band.height - 6f) / 2f);

            Stepper(new Rect(band.x + 4f, band.y + 3f, Mathf.Max(0f, band.width - 8f), height), group, palette,
                true);

            Stepper(new Rect(band.x + 4f, band.y + 5f + height, Mathf.Max(0f, band.width - 8f), height), group,
                palette, false);
        }

        /// <summary>
        /// One designation stepper: a caption, a count out of the group, and the two arrows.
        ///
        /// <b>The number is what the player is choosing.</b> Which animals carry it out is
        /// <see cref="AnimalDesignations"/>'s business, and the pane names the ones it picked so the choice is
        /// visible rather than magic.
        /// </summary>
        private static void Stepper(Rect rect, AnimalGroup group, UIColorPaletteDef palette, bool hunt)
        {
            int ordered = hunt ? group.HuntOrdered : group.TameOrdered;
            Color tint = ordered > 0 ? hunt ? palette.Warning : palette.Success : palette.TextSecondary;

            UIElementPainter.OutlineRounded(rect, ordered > 0 ? tint : palette.Border, palette.SurfaceSunken);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;

                Rect minus = new Rect(rect.x + 1f, rect.y, 16f, rect.height);
                Rect plus = new Rect(rect.xMax - 17f, rect.y, 16f, rect.height);
                Rect middle = new Rect(minus.xMax, rect.y, Mathf.Max(0f, plus.x - minus.xMax), rect.height);

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextSecondary;

                Widgets.Label(minus, "-");
                Widgets.Label(plus, "+");

                GUI.color = tint;

                Widgets.Label(middle, (hunt ? "Hunt " : "Tame ") + ordered + " of " + group.Count);

                if (Widgets.ButtonInvisible(minus))
                    Set(group, hunt, ordered - 1);

                if (Widgets.ButtonInvisible(plus))
                    Set(group, hunt, ordered + 1);

                if (Widgets.ButtonInvisible(middle))
                {
                    paneFor = new GroupKey(group);
                    paneOpen = true;
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static void Set(AnimalGroup group, bool hunt, int wanted)
        {
            if (hunt)
                AnimalDesignations.SetHuntCount(group, wanted);
            else
                AnimalDesignations.SetTameCount(group, wanted);

            paneFor = new GroupKey(group);
            paneOpen = true;

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        // ---------------------------------------------------------------------------------------
        // The opened species
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The individuals inside an opened species.
        ///
        /// <b>Capped, and it says so.</b> A herd of forty hares would otherwise make a row twelve hundred pixels
        /// tall, which is a scroll bar pretending to be a list. Fourteen is enough to see who is who in any group
        /// worth naming, and the last line says how many were left out and selects them on the map when clicked,
        /// which is the thing somebody wanting all forty actually wants.
        /// </summary>
        private static void DrawOpened(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            AnimalGroup group = GroupOf(data);

            if (group == null)
                return;

            Rect area = new Rect(row.x + RowCard.AccentWidth + 8f, row.y + RowHeight + 4f,
                row.width - RowCard.AccentWidth - 16f, Mathf.Max(0f, row.height - RowHeight - 4f));

            int shown = Mathf.Min(group.Count, MaxOpenedMembers);
            float y = area.y;

            for (int i = 0; i < shown; i++)
            {
                DrawMember(new Rect(area.x, y, area.width, MemberRowHeight), group, group.Members[i], palette);

                y += MemberRowHeight;
            }

            if (group.Count <= MaxOpenedMembers)
                return;

            Rect more = new Rect(area.x, y, area.width, MemberRowHeight);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Mouse.IsOver(more) ? palette.Accent : palette.TextSecondary;

                Widgets.Label(new Rect(more.x + 36f, more.y, more.width - 40f, more.height),
                    "and " + (group.Count - shown) + " more. Click to select all of them.");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (Widgets.ButtonInvisible(more))
                UIGuard.Try("Animals.SelectRest", () => ColonyBar.GroupActions.Select(group.Members), null);
        }

        /// <summary>
        /// One animal inside an opened species: who it is, how it is, and the orders on it.
        ///
        /// Clicking the name jumps the camera to it and selects it, the same gesture as a portrait on the pawns
        /// tab. The checkboxes on the right are the per animal version of the row's steppers, so a player who
        /// wants a specific deer can have that one.
        /// </summary>
        private static void DrawMember(Rect rect, AnimalGroup group, Pawn animal, UIColorPaletteDef palette)
        {
            if (animal == null || animal.Destroyed)
                return;

            bool showing = paneAnimal == animal;

            if (showing)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
            else if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                Rect icon = new Rect(rect.x + 2f, rect.center.y - 11f, 22f, 22f);

                Widgets.ThingIcon(icon, animal);

                if (showing)
                {
                    GUI.color = palette.Accent;

                    Widgets.DrawBox(icon, 1);

                    GUI.color = previousColor;
                }

                GUI.color = palette.TextPrimary;

                Rect name = new Rect(icon.xMax + 6f, rect.y, 140f, rect.height);

                Widgets.LabelEllipses(name, animal.LabelShortCap + "  " + Descriptor(animal));

                GUI.color = palette.TextSecondary;

                Rect state = new Rect(name.xMax + 4f, rect.y, StateLaneWidth, rect.height);

                Widgets.LabelEllipses(state, MemberState(animal, group, palette));

                float from = state.xMax + 4f;

                // The colony's own get the master chip; wildlife has no master to set, so the orders take the
                // room instead.
                if (group.Kind != AnimalKind.Wild)
                {
                    DrawMemberMaster(new Rect(from, rect.y, MasterChipWidth, rect.height), animal, palette);

                    from += MasterChipWidth + 6f;
                }

                Rect right = new Rect(from, rect.y, Mathf.Max(0f, rect.xMax - from - 8f), rect.height);

                if (group.Kind == AnimalKind.Wild)
                    DrawMemberOrders(right, animal, palette);
                else
                    DrawMemberTraining(right, animal, palette);

                // <b>The icon jumps the camera, the name opens the card.</b> Both were the same click until
                // 2026-08-22, when the card arrived and needed a gesture: jumping to the map is the one that has
                // somewhere else to be, so it keeps the portrait, which is the same division the pawns tab uses.
                // Neither reaches past the name, so the controls on the right keep their own clicks.
                //
                // Both are drawn every frame rather than one being skipped when the other fires: an invisible
                // button that comes and goes is exactly what shifts the control ids of everything after it, which
                // is a fault this mod has already paid for once in the text boxes.
                bool jump = Widgets.ButtonInvisible(icon);
                bool open = Widgets.ButtonInvisible(new Rect(icon.xMax, rect.y, name.xMax - icon.xMax,
                    rect.height));

                if (jump)
                    PawnCameraJump.Request(animal);
                else if (open)
                    ShowAnimal(group, animal);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static string Descriptor(Pawn animal)
        {
            string gender = animal.gender == Gender.Female ? "f" : animal.gender == Gender.Male ? "m" : "";

            if (AnimalFacts.Juvenile(animal))
                return gender + " juvenile";

            float years = animal.ageTracker?.AgeBiologicalYearsFloat ?? 0f;

            return gender + " " + years.ToString("0.#") + "y";
        }

        /// <summary>One line about one animal, chosen the same way the group's State cell chooses.</summary>
        private static string MemberState(Pawn animal, AnimalGroup group, UIColorPaletteDef palette)
        {
            if (animal.InAggroMentalState)
                return "manhunter";

            if (animal.Downed)
                return "down";

            if (HealthAIUtility.ShouldBeTendedNowByPlayer(animal))
                return "needs tending";

            if (animal.needs?.food != null && animal.needs.food.Starving)
                return "starving";

            AnimalPregnancy pregnancy = AnimalFacts.Pregnancy(animal);

            if (pregnancy.Pregnant)
                return "pregnant, " + Days(pregnancy.DaysLeft) + " left";

            if (group.Kind == AnimalKind.Wild)
            {
                if (!animal.Spawned || group.Map == null)
                    return string.Empty;

                return animal.Position.DistanceTo(AnimalRoster.ColonyCentre(group.Map)).ToString("0") + " tiles";
            }

            AnimalProduce produce = AnimalFacts.Produce(animal);

            if (produce.Any && produce.Ready)
                return produce.ResourceLabel + " ready";

            // The master used to be reported here, and it is not any more: it has its own control on the row now,
            // so saying it twice would leave two places showing one setting, one of which cannot be clicked.
            return "healthy";
        }

        /// <summary>
        /// Who is master to this animal, and the way to change it.
        ///
        /// <b>A control rather than a readout,</b> which is what Aaron asked for on 2026-08-22: the row said
        /// "master Jeff" and there was no way to make it say anybody else without going through the species menu,
        /// which sets the whole herd at once. One animal is exactly the case that menu cannot serve.
        ///
        /// Dim when there is no master, so a row full of unassigned animals does not read as a row full of
        /// controls demanding attention. The chip is clickable either way, including on an animal that has no
        /// obedience training yet, because the window is also where that gets explained.
        /// </summary>
        private static void DrawMemberMaster(Rect rect, Pawn animal, UIColorPaletteDef palette)
        {
            if (animal?.playerSettings == null)
                return;

            Rect chip = new Rect(rect.x, rect.center.y - 11f, rect.width, 22f);
            Pawn master = animal.playerSettings.Master;
            bool over = Mouse.IsOver(chip);

            UIElementPainter.OutlineRounded(chip, over ? palette.Accent : palette.Border,
                over ? palette.SurfaceRaised : palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;

                float caret = 14f;
                Rect text = new Rect(chip.x + 7f, chip.y, Mathf.Max(0f, chip.width - caret - 10f), chip.height);

                GUI.color = palette.TextDisabled;

                float captionWidth = Mathf.Min(text.width, Text.CalcSize("master ").x);

                Widgets.Label(new Rect(text.x, text.y, captionWidth, text.height), "master ");

                GUI.color = master == null ? palette.TextDisabled : palette.TextPrimary;

                Widgets.LabelEllipses(
                    new Rect(text.x + captionWidth, text.y, Mathf.Max(0f, text.width - captionWidth), text.height),
                    master == null ? "none" : master.LabelShortCap);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (TexButton.Reveal != null)
            {
                GUI.color = over ? palette.Accent : palette.TextDisabled;

                GUI.DrawTexture(new Rect(chip.xMax - 16f, chip.center.y - 5f, 10f, 10f), TexButton.Reveal);

                GUI.color = previousColor;
            }

            TooltipHandler.TipRegion(chip, (TipSignal) (master == null
                ? "No master. Click to pick one."
                : "Master is " + master.LabelShortCap + ". Click to change."));

            if (Widgets.ButtonInvisible(chip))
                Dialog_PickMaster.For(animal, Changed);
        }

        /// <summary>The hunt and tame checkboxes for one wild animal.</summary>
        private static void DrawMemberOrders(Rect rect, Pawn animal, UIColorPaletteDef palette)
        {
            float half = rect.width / 2f;

            Checkbox(new Rect(rect.x, rect.y, half - 4f, rect.height), "Hunt", animal, DesignationDefOf.Hunt,
                AnimalDesignations.CanHunt(animal), palette.Warning, palette);

            Checkbox(new Rect(rect.x + half, rect.y, half - 4f, rect.height), "Tame", animal,
                DesignationDefOf.Tame, AnimalDesignations.CanTame(animal), palette.Success, palette);
        }

        /// <summary>
        /// One animal's training boxes, its decay countdown, and its slaughter checkbox.
        ///
        /// <b>Boxes rather than the pips this drew until 2026-08-22.</b> Pips reported and could not be clicked,
        /// which meant the only way to train one specific animal was the species menu, which trains all of them.
        /// The boxes are the same control the species row carries, so the two levels read and behave alike.
        /// </summary>
        private static void DrawMemberTraining(Rect rect, Pawn animal, UIColorPaletteDef palette)
        {
            AnimalTrainingState training = AnimalTraining.Of(animal);

            Rect boxes = new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 88f), rect.height);
            float used = AnimalTrainingBoxes.DrawForAnimal(boxes, animal, palette, Changed);

            if (training.Decaying && training.AnythingAtRisk)
            {
                float x = rect.x + used + 4f;

                GUI.color = palette.Danger;

                Widgets.LabelEllipses(new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - x - 90f), rect.height),
                    "decays in " + Days(training.DecayDaysLeft));

                GUI.color = palette.TextSecondary;
            }

            Checkbox(new Rect(rect.xMax - 84f, rect.y, 84f, rect.height), "Slaughter", animal,
                DesignationDefOf.Slaughter, animal.Faction == Faction.OfPlayer, palette.Danger, palette);
        }

        /// <summary>A designation as a checkbox, drawn small enough for a line inside an opened row.</summary>
        private static void Checkbox(Rect rect, string label, Pawn animal, DesignationDef what, bool allowed,
            Color on, UIColorPaletteDef palette)
        {
            if (rect.width < 40f)
                return;

            bool ordered = AnimalDesignations.Ordered(animal, what);
            Rect box = new Rect(rect.x, rect.center.y - 6f, 12f, 12f);

            if (!allowed)
            {
                GUI.color = palette.TextDisabled;

                Widgets.DrawBox(box, 1);
                Widgets.LabelEllipses(new Rect(box.xMax + 4f, rect.y, rect.width - 18f, rect.height), label);

                return;
            }

            if (ordered)
            {
                Widgets.DrawBoxSolid(box, on);
            }
            else
            {
                GUI.color = palette.Border;

                Widgets.DrawBox(box, 1);
            }

            GUI.color = ordered ? on : palette.TextSecondary;

            Widgets.LabelEllipses(new Rect(box.xMax + 4f, rect.y, rect.width - 18f, rect.height), label);

            if (Widgets.ButtonInvisible(rect))
            {
                AnimalDesignations.Toggle(animal, what, !ordered);

                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        // ---------------------------------------------------------------------------------------
        // Hunting bills
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// One hunting bill: what it keeps, how full that is, and what it is doing about it.
        ///
        /// <b>Drawn as a whole row rather than through the columns.</b> A bill is not a species: it has a target,
        /// a progress bar and a species list, none of which belong in a column sized for a pen name. The cell
        /// drawers all return early for this payload, so the row owns its whole width.
        /// </summary>
        private static void DrawBillRow(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            BillRow bill = data?.Payload as BillRow;

            if (bill == null)
                return;

            if (bill.IsNew)
            {
                DrawNewBillRow(row, bill, palette);

                return;
            }

            if (bill.Tame != null)
            {
                DrawTameRow(row, bill, palette);

                return;
            }

            if (bill.Bill == null)
                return;

            RowCard.AccentColor = bill.Bill.suspended ? palette.TextDisabled : palette.Accent;
            RowCard.BackgroundColor = palette.PanelBackground;
            RowCard.DrawChrome(row, palette);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Rect inner = new Rect(row.x + RowCard.AccentWidth + 8f, row.y,
                    Mathf.Max(0f, row.width - RowCard.AccentWidth - 16f), row.height);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextDisabled;

                Widgets.LabelEllipses(new Rect(inner.x, inner.y + 2f, 260f, CaptionHeight),
                    bill.Bill.suspended ? "Suspended" : Mode(bill.Bill));

                Text.Font = GameFont.Small;
                GUI.color = bill.Bill.suspended ? palette.TextDisabled : palette.TextPrimary;

                Widgets.LabelEllipses(new Rect(inner.x, inner.y + CaptionHeight, 260f, ValueHeight),
                    bill.Bill.Label);

                Rect bar = new Rect(inner.x + 270f, inner.center.y - 8f, 200f, 16f);

                // Only a stocked bill has a target to be a fraction of. The other two say what they are watching
                // instead of drawing a bar that could only ever be full or empty and would mean neither.
                if (!bill.Bill.Stocked)
                {
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = bill.Bill.suspended ? palette.TextDisabled : palette.Warning;

                    Widgets.LabelEllipses(bar, bill.Bill.Forever
                        ? "No stock target"
                        : "Over " + bill.Bill.maxPopulation + " of each");
                }
                else
                {
                    int stock = bill.Bill.Stock(bill.Map);
                    float fraction = bill.Bill.targetCount <= 0
                        ? 1f
                        : Mathf.Clamp01(stock / (float) bill.Bill.targetCount);

                    UIProgressBarControl.Draw(bar, fraction, palette,
                        fraction >= 1f ? palette.Success : palette.Accent);

                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = palette.TextPrimary;

                    Widgets.Label(bar, stock + " / " + bill.Bill.targetCount);
                }

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                Rect note = new Rect(bar.xMax + 10f, inner.y, Mathf.Max(0f, inner.xMax - bar.xMax - 200f),
                    inner.height);

                Widgets.LabelEllipses(note, BillNote(bill.Bill));

                float x = inner.xMax;

                x = BillButton(new Rect(x - 74f, inner.center.y - 11f, 74f, 22f), "Remove", palette, () =>
                {
                    MapComponent_HuntingBills.For(bill.Map)?.Remove(bill.Bill);
                });

                x = BillButton(new Rect(x - 84f, inner.center.y - 11f, 80f, 22f),
                    bill.Bill.suspended ? "Resume" : "Suspend", palette,
                    () => bill.Bill.suspended = !bill.Bill.suspended);

                BillButton(new Rect(x - 64f, inner.center.y - 11f, 60f, 22f), "Edit", palette,
                    () => Find.WindowStack.Add(new Dialog_HuntingBill(bill.Bill, bill.Map)));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>What kind of order this is, in the caption above its name.</summary>
        private static string Mode(HuntingBill bill)
        {
            switch (bill.mode)
            {
                case HuntingBillMode.Forever: return "Culling, forever";
                case HuntingBillMode.MaxPopulation: return "Culling over a count";
                default: return "Keeping stocked";
            }
        }

        /// <summary>What the bill has to say for itself: what it may take, and what it last did.</summary>
        private static string BillNote(HuntingBill bill)
        {
            string species = bill.species == null || bill.species.Count == 0
                ? "any wildlife"
                : bill.species.Count == 1 ? bill.species[0].label : bill.species.Count + " species";

            if (bill.Stocked)
                species = bill.Counted + " from " + species;

            if (bill.lastActedTick < 0)
                return species + ", nothing ordered yet";

            int ago = Find.TickManager.TicksGame - bill.lastActedTick;

            return species + ", last ordered " + bill.lastOrderedCount + " about "
                   + Days(ago / (float) GenDate.TicksPerDay) + " ago";
        }

        private static float BillButton(Rect rect, string label, UIColorPaletteDef palette, Action act)
        {
            // The control plays the click itself, so the one this used to play here is gone rather than
            // doubled up.
            if (UIActionButtonControl.Draw(rect, label, palette, false, true, GameFont.Tiny))
                UIGuard.Try("Animals.BillButton", act, "The bill was not changed.");

            return rect.x;
        }

        /// <summary>
        /// One taming bill: what it wants, how close it is, and what it is doing about it.
        ///
        /// <b>The bar counts animals rather than items,</b> which is the one real difference from the hunting row
        /// beside it. Wanted is every male and female asked for across the species; held is what the colony has
        /// of exactly those, so a bill asking for two of each muffalo reads 3 / 4 rather than pretending a herd
        /// of males satisfies it.
        /// </summary>
        private static void DrawTameRow(Rect row, BillRow bill, UIColorPaletteDef palette)
        {
            RowCard.AccentColor = bill.Tame.suspended ? palette.TextDisabled : palette.Accent;
            RowCard.BackgroundColor = palette.PanelBackground;
            RowCard.DrawChrome(row, palette);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Rect inner = new Rect(row.x + RowCard.AccentWidth + 8f, row.y,
                    Mathf.Max(0f, row.width - RowCard.AccentWidth - 16f), row.height);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextDisabled;

                Widgets.LabelEllipses(new Rect(inner.x, inner.y + 2f, 260f, CaptionHeight),
                    bill.Tame.suspended ? "Suspended" : "Taming");

                Text.Font = GameFont.Small;
                GUI.color = bill.Tame.suspended ? palette.TextDisabled : palette.TextPrimary;

                Widgets.LabelEllipses(new Rect(inner.x, inner.y + CaptionHeight, 260f, ValueHeight),
                    bill.Tame.Label);

                Rect bar = new Rect(inner.x + 270f, inner.center.y - 8f, 200f, 16f);

                int wanted;
                int held;

                TameProgress(bill.Tame, bill.Map, out wanted, out held);

                if (wanted <= 0)
                {
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = bill.Tame.suspended ? palette.TextDisabled : palette.Warning;

                    Widgets.LabelEllipses(bar, "Nothing wanted yet");
                }
                else
                {
                    float fraction = Mathf.Clamp01(held / (float) wanted);

                    UIProgressBarControl.Draw(bar, fraction, palette,
                        fraction >= 1f ? palette.Success : palette.Accent);

                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = palette.TextPrimary;

                    Widgets.Label(bar, held + " / " + wanted);
                }

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                Rect note = new Rect(bar.xMax + 10f, inner.y, Mathf.Max(0f, inner.xMax - bar.xMax - 200f),
                    inner.height);

                Widgets.LabelEllipses(note, TameNote(bill.Tame));

                float x = inner.xMax;

                x = BillButton(new Rect(x - 74f, inner.center.y - 11f, 74f, 22f), "Remove", palette, () =>
                {
                    MapComponent_TamingBills.For(bill.Map)?.Remove(bill.Tame);
                });

                x = BillButton(new Rect(x - 84f, inner.center.y - 11f, 80f, 22f),
                    bill.Tame.suspended ? "Resume" : "Suspend", palette,
                    () => bill.Tame.suspended = !bill.Tame.suspended);

                BillButton(new Rect(x - 64f, inner.center.y - 11f, 60f, 22f), "Edit", palette,
                    () => Find.WindowStack.Add(new Dialog_TamingBill(bill.Tame, bill.Map)));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// How many animals a taming bill wants and how many of those it has.
        ///
        /// Held is capped per species and sex at what was asked for, so a colony with nine males against a want
        /// of two does not report itself over target and hide that the females are missing.
        /// </summary>
        private static void TameProgress(TamingBill bill, Map map, out int wanted, out int held)
        {
            wanted = 0;
            held = 0;

            if (bill.targets == null)
                return;

            for (int i = 0; i < bill.targets.Count; i++)
            {
                TamingTarget target = bill.targets[i];

                if (target == null || target.species == null)
                    continue;

                wanted += target.males + target.females;

                held += Mathf.Min(target.males, TamingBill.Held(map, target.species, Gender.Male));
                held += Mathf.Min(target.females, TamingBill.Held(map, target.species, Gender.Female));
            }
        }

        /// <summary>What the bill has to say for itself: what it wants, and what it last did.</summary>
        private static string TameNote(TamingBill bill)
        {
            int species = 0;

            for (int i = 0; bill.targets != null && i < bill.targets.Count; i++)
            {
                if (bill.targets[i] != null && bill.targets[i].species != null && !bill.targets[i].Empty)
                    species++;
            }

            string what = species == 0
                ? "no species chosen"
                : species == 1 ? "1 species" : species + " species";

            if (bill.tamer != null)
                what += ", planned for " + bill.tamer.LabelShortCap;

            if (bill.lastActedTick < 0)
                return what + ", nothing ordered yet";

            int ago = Find.TickManager.TicksGame - bill.lastActedTick;

            return what + ", last ordered " + bill.lastOrderedCount + " about "
                   + ago.ToStringTicksToPeriodVague() + " ago";
        }

        private static void DrawNewBillRow(Rect row, BillRow bill, UIColorPaletteDef palette)
        {
            Rect button = new Rect(row.x + 8f, row.y, Mathf.Min(220f, row.width - 16f), row.height);
            bool over = Mouse.IsOver(button);

            UIElementPainter.OutlineRounded(button, over ? palette.Accent : palette.Border,
                palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = over ? palette.Accent : palette.TextSecondary;

                Widgets.Label(button, bill.ForTaming ? "New taming bill" : "New hunting bill");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!Widgets.ButtonInvisible(button))
                return;

            if (bill.ForTaming)
            {
                UIGuard.Try("Animals.NewTamingBill", () =>
                {
                    MapComponent_TamingBills component = MapComponent_TamingBills.For(bill.Map);

                    if (component == null)
                        return;

                    TamingBill made = TamingBill.NewBill();

                    component.Add(made);

                    Find.WindowStack.Add(new Dialog_TamingBill(made, bill.Map));
                }, "The taming bill was not created.");
            }
            else
            {
                UIGuard.Try("Animals.NewBill", () =>
                {
                    MapComponent_HuntingBills component = MapComponent_HuntingBills.For(bill.Map);

                    if (component == null)
                        return;

                    HuntingBill made = HuntingBill.NewMeatBill();

                    component.Add(made);

                    Find.WindowStack.Add(new Dialog_HuntingBill(made, bill.Map));
                }, "The hunting bill was not created.");
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
        }
    }
}
