using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Every bill in the colony, in one window.
    ///
    /// <b>The question vanilla cannot answer is "where is that bill?".</b> Its tab is a fixed 420 by 480 panel
    /// bolted to one bench, so finding the bill making too much chemfuel means opening benches one at a time. That
    /// got worse when we raised the cap from fifteen to a hundred and twenty ourselves, which is what made a
    /// colony wide list the fix rather than a restyle.
    ///
    /// <b>A bench's own tab is a different interface, not this one filtered.</b> It was this one filtered, and
    /// that was wrong: clicking a workbench pointed at one bench and got the whole colony, with the main tab bar
    /// changing under the player. A bench now has its own card list, which is the growing zone's shape applied to
    /// recipes. This window keeps the question only it can answer, which is "where is that bill?" across every
    /// bench on every map. See <c>WorkBenchBillsTab</c> for the other one.
    ///
    /// <b>The bill is edited here rather than behind another window.</b> Vanilla puts every setting in
    /// <c>Dialog_BillConfig</c>, stacked over the tab, so comparing two bills is open, read, close, open. Selecting
    /// a row fills the pane on the right instead, and that pane is complete: repeat mode and count, who is allowed
    /// to work it, the skill range, the search radius and the ingredient filter.
    ///
    /// <b>Reordering stays inside a bench.</b> Bill order is priority within one bench and means nothing across
    /// them, so a drag that would cross a bench heading is refused rather than being quietly read as a move.
    /// Moving a bill elsewhere is the explicit Copy to bench action, decided with Aaron on 2026-08-18.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MainTabWindow_Bills : MainTabWindow
    {
        private const float TitleHeight = 30f;

        /// <summary>The header block, sized as every other tab sizes its own.</summary>
        private const float HeaderHeight = 66f;

        /// <summary>Side of the header glyph, and the air between it and the title.</summary>
        private const float GlyphSize = 34f;

        private const float GlyphGap = 10f;

        private const float ToolbarHeight = 34f;
        private const float FooterHeight = 44f;
        private const float BenchHeight = 30f;

        /// <summary>The word on the save-as-template button, kept beside the code that measures it.</summary>
        private const string SaveLabel = "Save";
        private const float RowHeight = 50f;
        private const float Pad = 12f;
        private const float EditorWidth = 330f;

        /// <summary>
        /// The ingredient column.
        ///
        /// <b>A column of its own rather than the bottom of the editor.</b> It started as the last section of the
        /// editor pane, taking whatever height was left, and that was too little of the wrong dimension: the tree
        /// is a hierarchy of long names with a toggle at each right edge, so squeezing it narrow costs far more
        /// than squeezing it short. Aaron asked for it moved on 2026-08-19 after seeing it working at 400 wide.
        ///
        /// Wide enough that a nested corpse name and its switch sit on one line, which is the case that was
        /// wrapping.
        /// </summary>
        private const float FilterWidth = 380f;

        /// <summary>
        /// Smallest the ingredient tree is allowed to be squeezed to.
        ///
        /// Below about this the tree is a scrollbar with a word beside it and the player is better served by the
        /// window simply being taller. It is a floor rather than a fixed height so the tree grows into whatever a
        /// large screen gives it.
        /// </summary>
        private const float FilterMinimumHeight = 140f;

        /// <summary>Width of the rail, and the gap between it and the rest.</summary>
        private const float RailWidth = 200f;

        /// <summary>The tab's own mark, the same texture its button on the bar uses.</summary>
        private static readonly Texture2D Glyph;

        static MainTabWindow_Bills()
        {
            // Through a local, because a readonly field can only be assigned in the constructor itself and
            // the guard does its work in a closure.
            Texture2D glyph = null;

            UIGuard.Try("Bills.Glyph",
                () => glyph = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Bills", false),
                "The header has no glyph this session. Everything on the tab still reads.");

            Glyph = glyph;
        }

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search recipe, bill name or bench",
            MaxLength = 64
        };

        private List<BillGroup> groups = new List<BillGroup>();

        /// <summary>
        /// The value of <see cref="BillCatalog.Stamp"/> the groups above were gathered at.
        ///
        /// Starts at zero, which is also where the stamp starts, so a window opened before anything has happened
        /// does not gather twice on its first frame -- <c>PostOpen</c> has already read the colony by then.
        /// </summary>
        private int stamp;
        private Bill_Production selected;
        private Vector2 scroll = Vector2.zero;
        private bool suspendedOnly;
        private bool troubledOnly;

        /// <summary>
        /// Benches whose bills are folded away, by thing id.
        ///
        /// <b>Ids rather than references,</b> so a bench that is deconstructed while folded leaves an integer
        /// behind instead of keeping a destroyed building alive for as long as the window exists.
        /// </summary>
        private readonly HashSet<int> collapsed = new HashSet<int>();

        private readonly BillNumberBox counterBox = new BillNumberBox();
        private readonly BillNumberBox radiusBox = new BillNumberBox();
        private readonly BillNumberBox skillLowBox = new BillNumberBox();
        private readonly BillNumberBox skillHighBox = new BillNumberBox();

        /// <summary>
        /// The ingredient tree's own scroll position and search, which RimWorld keeps outside the filter.
        ///
        /// One state for the pane rather than one per bill: the pane shows a single bill, and a state per bill
        /// would accumulate one for every bill ever selected.
        /// </summary>
        private readonly ThingFilterUI.UIState filterState = new ThingFilterUI.UIState();

        /// <summary>The bill being dragged, and the bench it started on. Null when nothing is being dragged.</summary>
        private Bill_Production dragging;

        private Building_WorkTable dragBench;

        /// <summary>
        /// The row the cursor is currently over, which is where the dragged bill would land.
        ///
        /// Recorded while drawing rather than computed from the cursor's height, because the list is filtered: the
        /// row at a given height is not the bill at that index in the stack. Dropping onto a row means taking that
        /// row's place, which stays true whatever is hidden.
        /// </summary>
        private BillEntry over;

        /// <summary>What the footer says after a refused drag or a completed one.</summary>
        private string note;

        /// <summary>
        /// Sized for three columns: the list, the bill's settings, and the ingredient tree.
        ///
        /// Taller and wider than the first version on purpose. The tree needs both dimensions before it is worth
        /// having, and a filter squeezed into what was left of a 640 pixel window would have been the sort of
        /// technically present control nobody can use.
        ///
        /// <b>Clamped to the screen rather than asked for flat.</b> A main tab is positioned from the bottom of
        /// the screen upwards, so a height the display cannot give runs the title and the toolbar off the top
        /// where nothing can reach them. The room left below is for the button bar this mod puts there.
        ///
        /// <b>The list is what gives way on a narrow screen,</b> since it is the only column that stays useful
        /// when it is squeezed: a name and a state still read at half the width, while a tree of toggles or a row
        /// of numbered fields does not.
        ///
        /// The default margin is kept rather than zeroed, because every rectangle below is measured from the one
        /// the game hands in and expects it to be already inset.
        /// </summary>
        public override Vector2 RequestedTabSize =>
            new Vector2(Mathf.Min(1400f, UI.screenWidth - 40f), Mathf.Min(760f, UI.screenHeight - 120f));

        public override void PostOpen()
        {
            base.PostOpen();

            Search.Text = string.Empty;

            Reread();
        }

        /// <summary>
        /// Re-reads the colony.
        ///
        /// Called when the window opens and after anything that could change the list, never per frame: asking
        /// every bill on every map whether anybody can work it is far too much to do while drawing.
        /// </summary>
        internal void Reread()
        {
            groups = BillCatalog.Collect();
            stamp = BillCatalog.Stamp;

            if (selected == null)
                return;

            // A bill deleted behind the window must not stay selected and editable.
            foreach (BillGroup group in groups)
            {
                foreach (BillEntry entry in group.Bills)
                {
                    if (entry.Bill == selected)
                        return;
                }
            }

            selected = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // A comparison rather than a gather: the stamp moves only when a bill is added, deleted or cleared
            // anywhere in the game, so the expensive walk happens on the frame after a real change and on no
            // other. This is what makes importing a bench template show up without the importer knowing that
            // this window exists.
            if (stamp != BillCatalog.Stamp)
                Reread();

            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("Bills.Window", inRect, () => Contents(inRect),
                "The bills window failed to draw. Your bills are unchanged.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.UpperLeft;

                float y = Header(inRect, palette);

                y = Toolbar(inRect, y, palette);

                Rect body = new Rect(inRect.x, y + 6f, inRect.width, inRect.yMax - FooterHeight - y - 12f);

                Rail(new Rect(body.x, body.y, RailWidth, body.height), palette);

                Rect rest = new Rect(body.x + RailWidth + Pad, body.y,
                    Mathf.Max(0f, body.width - RailWidth - Pad), body.height);

                // <b>The editor and the ingredient tree are only reserved when a bill is open.</b> They used
                // to hold their width whatever was selected, so a tab with nothing chosen spent seven hundred
                // pixels on one sentence and one empty rectangle. Now the list has the room until it is
                // actually needed, and gets it back the moment the bill is closed.
                if (selected == null)
                {
                    DrawList(rest, palette);
                }
                else
                {
                    // Floored so a narrow screen produces a cramped list rather than a negative width.
                    float listWidth = Mathf.Max(240f, rest.width - EditorWidth - FilterWidth - Pad * 2f);

                    Rect list = new Rect(rest.x, rest.y, listWidth, rest.height);
                    Rect editor = new Rect(list.xMax + Pad, rest.y, EditorWidth, rest.height);
                    Rect filter = new Rect(editor.xMax + Pad, rest.y, FilterWidth, rest.height);

                    DrawList(list, palette);
                    DrawEditor(editor, palette);
                    DrawFilter(filter, palette);
                }

                Footer(inRect, palette);
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }

        /// <summary>
        /// The block that names the screen, with its figures seated in it.
        ///
        /// <b>The same shape every restyled tab uses.</b> The title and the line under it were a pair of bare
        /// <c>Widgets.Label</c> calls in the game's own font; the counts they carried are now readouts, which
        /// is where the rest of the mod puts a figure.
        /// </summary>
        private float Header(Rect inRect, UIColorPaletteDef palette)
        {
            Rect rect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);

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

                GUI.color = BillFaces.AccentOf(palette);
                GUI.DrawTexture(mark, Glyph);
                GUI.color = previous;

                text = mark.xMax + GlyphGap;
            }

            TabParts.RowLabel(new Rect(text, inner.y + 2f, 320f, 26f), "Bills",
                BillFaces.AccentOf(palette), GameFont.Medium, BillFaces.Display, BillFaces.Size.Title);

            TabParts.RowLabel(new Rect(text, inner.y + 28f, 380f, 18f),
                "Every standing order in the colony, by bench", palette.TextSecondary, GameFont.Tiny,
                BillFaces.Condensed, BillFaces.Size.Subtitle);

            Readouts(inner, palette);

            return rect.yMax + 6f;
        }

        /// <summary>
        /// The four figures, right to left.
        ///
        /// Suspended and needing attention are drawn whatever their count, because a zero there is worth
        /// reading: it is the difference between "nothing is wrong" and "I have not looked".
        /// </summary>
        private void Readouts(Rect area, UIColorPaletteDef palette)
        {
            int total = BillCatalog.Total(groups);
            int troubled = BillCatalog.Troubled(groups);
            int suspended = Suspended();

            float x = area.xMax;

            x = TabParts.Readout(area, x, "suspended", suspended.ToString(), palette,
                "Bills switched off by hand. They keep their settings and their place in the order.");

            x = TabParts.Readout(area, x, "need attention", troubled.ToString(), palette,
                "Bills that cannot run: no ingredients, nobody able to work them, or no bench to work at.",
                troubled > 0 ? palette.Warning : palette.TextPrimary);

            x = TabParts.Readout(area, x, "benches", groups.Count.ToString(), palette,
                "Benches with at least one bill on them.");

            TabParts.Readout(area, x, "bills", total.ToString(), palette,
                "Every bill in the colony, on every bench.");
        }

        /// <summary>
        /// The rail: the benches, then the two filters that cut across all of them.
        ///
        /// <b>It replaces the bench headings as the way to get somewhere.</b> Eight benches with forty-four
        /// bills between them is a lot of scrolling to reach the one you came for; as entries with counts,
        /// and a warning-colored count on any bench holding a bill in trouble, the trip is one click. The
        /// headings stay in the list, because they are still what a bill belongs to.
        ///
        /// Needs attention and suspended sit under the divider and stay toggles rather than selections: they
        /// narrow whatever is showing instead of choosing what is shown.
        /// </summary>
        private void Rail(Rect rect, UIColorPaletteDef palette)
        {
            RailItems.Clear();

            RailItems.Add(Head("Benches", palette));
            RailItems.Add(Entry(0, "All benches", BillCatalog.Total(groups), 0, palette));

            for (int i = 0; i < groups.Count; i++)
            {
                BillGroup group = groups[i];

                RailItems.Add(Entry(BenchId(group), group.Label, group.Bills.Count, Troubled(group), palette));
            }

            RailItems.Add(new UIRailDividerControl { Color = palette.Border });
            RailItems.Add(Head("Across all benches", palette));

            RailItems.Add(new UIRailClickableEntry(TroubleKey, "Needs attention")
            {
                Count = BillCatalog.Troubled(groups),
                Face = BillFaces.Condensed,
                Points = BillFaces.Size.RailName,
                CountFace = BillFaces.Mono,
                CountPoints = BillFaces.Size.RailCount,
                TextColor = troubledOnly ? BillFaces.AccentOf(palette) : (Color?) null,
                CountColor = troubledOnly ? BillFaces.AccentOf(palette) : (Color?) null
            });

            RailItems.Add(new UIRailClickableEntry(SuspendKey, "Suspended")
            {
                Count = Suspended(),
                Face = BillFaces.Condensed,
                Points = BillFaces.Size.RailName,
                CountFace = BillFaces.Mono,
                CountPoints = BillFaces.Size.RailCount,
                TextColor = suspendedOnly ? BillFaces.AccentOf(palette) : (Color?) null,
                CountColor = suspendedOnly ? BillFaces.AccentOf(palette) : (Color?) null
            });

            string picked = UIRailControl.Draw(rect, RailItems, railBench.ToString(), ref railScroll,
                ref railDragging, ref railOffset, palette);

            if (picked == null)
                return;

            // The two filters toggle rather than select, so pressing the one that is already on turns it off.
            if (picked == TroubleKey)
            {
                troubledOnly = !troubledOnly;

                return;
            }

            if (picked == SuspendKey)
            {
                suspendedOnly = !suspendedOnly;

                return;
            }

            int bench;

            if (!int.TryParse(picked, out bench))
                return;

            railBench = bench;
            scroll = Vector2.zero;
        }

        private const string TroubleKey = "*trouble";
        private const string SuspendKey = "*suspended";

        private UIRailSectionHeaderControl Head(string label, UIColorPaletteDef palette)
        {
            return new UIRailSectionHeaderControl
            {
                Label = label,
                Uppercase = true,
                Face = BillFaces.Mono,
                Points = BillFaces.Size.RailHead,
                Color = palette.TextDisabled
            };
        }

        /// <summary>
        /// One bench in the rail. <paramref name="trouble"/> colors the count, which is the whole reason a
        /// bench in difficulty can be spotted without opening it.
        /// </summary>
        private UIRailClickableEntry Entry(int id, string label, int count, int trouble,
            UIColorPaletteDef palette)
        {
            bool on = railBench == id;

            return new UIRailClickableEntry(id.ToString(), label)
            {
                Count = count,
                Face = BillFaces.Condensed,
                Points = BillFaces.Size.RailName,
                CountFace = BillFaces.Mono,
                CountPoints = BillFaces.Size.RailCount,
                TextColor = on ? BillFaces.AccentOf(palette) : (Color?) null,
                CountColor = trouble > 0
                    ? palette.Warning
                    : on ? BillFaces.AccentOf(palette) : (Color?) null
            };
        }

        /// <summary>How many bills on one bench cannot run.</summary>
        private static int Troubled(BillGroup group)
        {
            int count = 0;

            for (int i = 0; group.Bills != null && i < group.Bills.Count; i++)
            {
                if (group.Bills[i].Trouble != BillTrouble.None)
                    count++;
            }

            return count;
        }

        private int railBench;
        private Vector2 railScroll;
        private bool railDragging;
        private float railOffset;

        private readonly List<UIRailElement> RailItems = new List<UIRailElement>();

        /// <summary>How many bills are switched off by hand.</summary>
        private int Suspended()
        {
            int count = 0;

            for (int g = 0; g < groups.Count; g++)
            {
                List<BillEntry> entries = groups[g].Bills;

                for (int i = 0; entries != null && i < entries.Count; i++)
                {
                    if (entries[i].Suspended)
                        count++;
                }
            }

            return count;
        }

        private float Toolbar(Rect inRect, float y, UIColorPaletteDef palette)
        {
            Rect bar = new Rect(inRect.x, y + 4f, inRect.width, ToolbarHeight);

            Search.Draw(new Rect(bar.x, bar.y, 260f, ToolbarHeight - 2f), palette);

            float x = bar.x + 268f;

            x += Chip(new Rect(x, bar.y + 2f, 140f, 26f), "Needs attention", ref troubledOnly, palette) + 8f;
            Chip(new Rect(x, bar.y + 2f, 110f, 26f), "Suspended", ref suspendedOnly, palette);

            Rect templates = new Rect(bar.xMax - 110f, bar.y + 2f, 110f, 26f);

            if (Button(templates, "Templates", palette))
                Find.WindowStack.Add(new Dialog_BillTemplates(selected));

            // Between Templates and Add bill, because it belongs to the same question as Add bill and is
            // usually the step before it: you look a thing up, learn which bench makes it, and then go and add
            // the bill. It asks for nothing, so it sits in the plain style rather than the primary one.
            Rect lookup = new Rect(templates.x - 108f, bar.y + 2f, 100f, 26f);

            if (Button(lookup, "Lookup", palette))
                Find.WindowStack.Add(new Dialog_RecipeLookup());

            Rect add = new Rect(lookup.x - 118f, bar.y + 2f, 110f, 26f);

            // Into the wizard, which asks for the bench itself as its first step. This window is the colony, so
            // there is no bench in view to assume, and the two float menus this replaced were sixty bare names
            // followed by a few hundred more.
            if (Button(add, "Add bill", palette, true))
                Find.WindowStack.Add(new Dialog_AddWorkBill(Reread));

            return bar.yMax;
        }

        /// <summary>
        /// A filter toggle. Returns its width so the next one can sit beside it.
        ///
        /// <b>It is the mod's button with the toggled state set, not a shape of its own.</b> Drawn by hand it
        /// answered the pointer in no way at all -- no fill, no border, no sound -- so Needs attention and
        /// Suspended read as two captions somebody had drawn a box around. Reported on 2026-08-25. Nothing about
        /// its appearance changed except that it now reacts.
        /// </summary>
        private static float Chip(Rect rect, string label, ref bool on, UIColorPaletteDef palette)
        {
            if (UIActionButtonControl.Draw(rect, label, palette, false, true, GameFont.Tiny, null, on))
                on = !on;

            return rect.width;
        }

        private static bool Button(Rect rect, string label, UIColorPaletteDef palette, bool primary = false)
        {
            return BillButtons.Button(rect, label, palette, primary);
        }

        /// <summary>
        /// How wide the save button has to be for the word on it.
        ///
        /// <b>Measured at the font it is drawn in, which is Small rather than the Tiny the heading uses.</b>
        /// <c>BillButtons.Button</c> draws with a centred <c>Widgets.Label</c>, and a centred IMGUI label clips
        /// at <i>both</i> ends rather than ellipsing -- so a rect a few pixels short would silently show "av"
        /// with no sign that anything was wrong. <c>Text.CalcSize</c> is the right measurer here precisely
        /// because that label is a plain one and reserves nothing.
        ///
        /// Measured every frame rather than cached: it depends on the UI scale and the language, and this is one
        /// call per visible bench heading.
        /// </summary>
        private static float SaveWidth()
        {
            GameFont font = Text.Font;

            try
            {
                Text.Font = GameFont.Small;

                return Mathf.Round(Text.CalcSize(SaveLabel).x) + 16f;
            }
            finally
            {
                Text.Font = font;
            }
        }

        // ------------------------------------------------------------------ list

        private void DrawList(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(1f);
            float height = 0f;

            foreach (BillGroup group in groups)
            {
                if (Shown(group) == 0)
                    continue;

                height += BenchHeight + (Folded(group) ? 0f : Shown(group) * RowHeight);
            }

            Rect view = new Rect(0f, 0f, inner.width - 18f, Mathf.Max(height, inner.height));

            Widgets.BeginScrollView(inner, ref scroll, view);

            try
            {
                // Recomputed every frame from what the cursor is actually over, so a stale target cannot survive
                // the list being refiltered mid drag.
                over = null;

                float y = 0f;

                foreach (BillGroup group in groups)
                {
                    if (Shown(group) == 0)
                        continue;

                    y = Bench(new Rect(0f, y, view.width, BenchHeight), group, palette);

                    if (Folded(group))
                        continue;

                    foreach (BillEntry entry in group.Bills)
                    {
                        if (!Matches(entry, group))
                            continue;

                        y = Row(new Rect(0f, y, view.width, RowHeight), entry, palette);
                    }
                }

                if (height <= 0f)
                {
                    Text.Font = GameFont.Small;
                    GUI.color = palette.TextDisabled;
                    Widgets.Label(new Rect(10f, 10f, view.width - 20f, 40f),
                        groups.Count == 0 ? "No bench in the colony has a bill." : "Nothing matches those filters.");
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            // Settled outside the scroll view so a drag that ends anywhere, including off the list entirely,
            // still finishes rather than leaving a row stuck to the cursor.
            if (dragging != null && Event.current != null && Event.current.type == EventType.MouseUp)
            {
                Drop();

                Event.current.Use();

                return;
            }

            // A button released outside the window never sends us a MouseUp, and a drag with nothing holding it
            // would follow the cursor forever. Asking the button directly is the only thing that catches that.
            if (dragging != null && !Input.GetMouseButton(0))
                Drop();
        }

        /// <summary>
        /// Whether this bench's bills are folded away.
        ///
        /// <b>A search opens everything.</b> Hiding a match inside a fold would make the search look broken while
        /// it was working perfectly, and there is no way for the player to tell the difference.
        /// </summary>
        private bool Folded(BillGroup group)
        {
            if (group.Bench == null || (Search.Text ?? string.Empty).Trim().Length > 0)
                return false;

            return collapsed.Contains(group.Bench.thingIDNumber);
        }

        private int Shown(BillGroup group)
        {
            // The rail's choice, unless a search is running: typing is a request to look everywhere, and a
            // search that silently skipped seven of the eight benches would read as a broken search.
            if (railBench != 0 && Search.Text.NullOrEmpty() && BenchId(group) != railBench)
                return 0;

            int count = 0;

            foreach (BillEntry entry in group.Bills)
            {
                if (Matches(entry, group))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// A bench's identity for the rail, by thing id.
        ///
        /// <b>An id rather than the group's index or its label.</b> The list is rebuilt whenever the colony
        /// changes, so an index would move the selection onto a different bench the moment one was built, and
        /// two stoves in two rooms share a label. Zero means every bench, which is how the tab opens.
        /// </summary>
        private static int BenchId(BillGroup group)
        {
            return group == null || group.Bench == null ? 0 : group.Bench.thingIDNumber;
        }

        /// <summary>Whether a bill survives the search box and the filter chips.</summary>
        private bool Matches(BillEntry entry, BillGroup group)
        {
            if (entry?.Bill == null)
                return false;

            if (troubledOnly && entry.Trouble == BillTrouble.None)
                return false;

            if (suspendedOnly && !entry.Suspended)
                return false;

            string term = (Search.Text ?? string.Empty).Trim();

            if (term.Length == 0)
                return true;

            return Has(entry.Label, term) || Has(group.Label, term) || Has(entry.Bill.recipe?.label, term);
        }

        private static bool Has(string text, string term)
        {
            return text != null && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private float Bench(Rect rect, BillGroup group, UIColorPaletteDef palette)
        {
            UIElementPainter.FillRounded(rect, palette.PanelBackground);

            bool folded = Folded(group);

            Rect caret = new Rect(rect.x + 6f, rect.y, 18f, rect.height);

            Caret(caret, !folded, Mouse.IsOver(rect) ? palette.TextPrimary : palette.TextSecondary);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextPrimary;
            Text.Anchor = TextAnchor.MiddleLeft;

            Widgets.Label(new Rect(rect.x + 28f, rect.y, rect.width * 0.5f, rect.height), group.Label);

            Rect add = new Rect(rect.xMax - 26f, rect.y + 4f, 22f, rect.height - 8f);

            // <b>The whole bench, from the row that names it.</b> Asked for on 2026-08-23. The bench's own tab
            // has had a Save bench button since 14123, but reaching it means leaving this window and clicking the
            // bench on the map -- and this window exists precisely so a colony's benches can be worked on without
            // going to find each one.
            float saveWidth = SaveWidth();

            Rect save = new Rect(add.x - 4f - saveWidth, rect.y + 4f, saveWidth, rect.height - 8f);

            GUI.color = palette.TextDisabled;
            Text.Anchor = TextAnchor.MiddleRight;

            Widgets.Label(new Rect(rect.x, rect.y, save.x - rect.x - 10f, rect.height),
                group.Where.NullOrEmpty() ? Shown(group) + " shown" : group.Where);

            Text.Anchor = TextAnchor.UpperLeft;

            // Drawn after the label so the buttons are above it, and hit tested before the heading so a click on
            // either adds or saves rather than folding the bench under the cursor.
            bool saving = Button(save, "Save", palette);

            // The scope is worth saying because the list above it may be filtered: a search showing one bill of
            // six does not mean five are about to be left out.
            TooltipHandler.TipRegion(save,
                (TipSignal)("Saves every bill on this bench as a template, including any the search is hiding."));

            bool adding = Button(add, "+", palette);

            if (adding)
            {
                // Straight to the recipe step: this heading already names the bench, so the wizard's first
                // question would have one answer.
                Find.WindowStack.Add(new Dialog_AddWorkBill(group.Bench, Reread));
            }
            else if (saving)
            {
                // Guarded on the map for the same reason the bench tab's button is: the dialog writes a file
                // named after where the bench is, and a bench in a caravan or mid-teleport has no map to name.
                if (group.Bench != null && group.Bench.Map != null)
                    Find.WindowStack.Add(new Dialog_SaveBenchTemplate(group.Bench));
            }
            else if (group.Bench != null && Widgets.ButtonInvisible(rect))
            {
                if (!collapsed.Remove(group.Bench.thingIDNumber))
                    collapsed.Add(group.Bench.thingIDNumber);
            }

            return rect.yMax;
        }

        /// <summary>
        /// The fold indicator: a solid triangle pointing down when open and right when closed.
        ///
        /// <b>Drawn from three bars rather than typed as a glyph.</b> A triangle character would be the obvious
        /// way and is the wrong one: this codebase is kept to plain ASCII in source so that an encoding accident
        /// in an editor or a diff cannot turn a control into a box with a question mark in it.
        /// </summary>
        private static void Caret(Rect rect, bool open, Color color)
        {
            float cx = Mathf.Round(rect.center.x);
            float cy = Mathf.Round(rect.center.y);

            for (int i = 0; i < 3; i++)
            {
                float span = 9f - i * 3f;

                Rect bar = open
                    ? new Rect(cx - span * 0.5f, cy - 4f + i * 3f, span, 3f)
                    : new Rect(cx - 4f + i * 3f, cy - span * 0.5f, 3f, span);

                UIElementPainter.FillRounded(bar, color);
            }
        }

        private float Row(Rect rect, BillEntry entry, UIColorPaletteDef palette)
        {
            bool chosen = entry.Bill == selected;

            if (chosen)
                UIElementPainter.FillRounded(rect, palette.AccentMuted);
            else if (Mouse.IsOver(rect))
                UIElementPainter.FillRounded(rect, palette.HoverOverlay);

            Drag(rect, entry, palette);

            // The state dot, which is the fastest thing to read in a long list.
            Color dot = entry.Suspended
                ? palette.TextDisabled
                : entry.Trouble != BillTrouble.None
                    ? palette.Danger
                    : palette.Success;

            UIElementPainter.FillRounded(new Rect(rect.x + 20f, rect.y + rect.height * 0.5f - 3f, 6f, 6f), dot);

            Product(new Rect(rect.x + 32f, rect.y + 8f, 34f, 34f), entry);

            Text.Font = GameFont.Small;
            GUI.color = entry.Suspended ? palette.TextDisabled : palette.TextPrimary;

            // Counted once and handed to both the detail line and the bar. It walks the map's resource
            // counter, so asking twice per row is a map wide count per row per frame for one number.
            int held = Held(entry.Bill);

            float textWidth = rect.width - 306f;

            Widgets.Label(new Rect(rect.x + 72f, rect.y + 6f, textWidth, 20f), entry.Label);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(rect.x + 72f, rect.y + 26f, textWidth, 18f), Detail(entry, held));

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = entry.Trouble != BillTrouble.None ? palette.Danger : palette.TextSecondary;

            Widgets.Label(new Rect(rect.xMax - 226f, rect.y, 160f, rect.height), State(entry));

            Text.Anchor = TextAnchor.UpperLeft;

            Progress(rect, entry, held, palette);

            bool acted = Actions(rect, entry, palette);

            // Not while dragging, or releasing the button over a row would select it as well as move something.
            // Not after an action either: a click that suspended or deleted a bill is answered, and letting the
            // row take it as well would select a bill the player was in the middle of removing.
            if (!acted && dragging == null && Widgets.ButtonInvisible(rect))
                selected = entry.Bill;

            return rect.yMax;
        }

        /// <summary>
        /// How close the bill is to its target, as a bar along the bottom of the row.
        ///
        /// <b>The growing zone's bar, applied here,</b> asked for on 2026-08-20. It answers the question the
        /// subtitle only half answers: "until you have 5000" says what the target is and nothing about whether
        /// the colony is near it.
        ///
        /// <b>Only for a target count bill, because only that mode has a denominator.</b> Do X times counts down
        /// what is left with nothing recording where it started, and Forever has no end. A bar drawn for those
        /// would have to invent a scale, which is the placeholder the bench row still carries and which is worse
        /// than no bar: a bar is read as a proportion whether or not it is one.
        ///
        /// <b>Amber once the target is met,</b> matching the stripe and the badge, so a satisfied bill reads the
        /// same in all three places.
        /// </summary>
        private static void Progress(Rect rect, BillEntry entry, int held, UIColorPaletteDef palette)
        {
            Bill_Production bill = entry.Bill;

            if (bill == null || bill.repeatMode != BillRepeatModeDefOf.TargetCount || bill.targetCount <= 0)
                return;

            // held is passed in: the row already counted it for the line above the bar.
            float fill = Mathf.Clamp01(held / (float) bill.targetCount);

            Rect bar = new Rect(rect.x + 72f, rect.yMax - 9f, rect.width - 300f, 4f);

            if (bar.width <= 8f)
                return;

            UIElementPainter.FillRounded(bar, palette.SurfaceSunken);

            if (fill > 0f)
            {
                // <b>Green at the target rather than amber.</b> A bill that has made what it was asked for is
                // finished, and finished is the one thing amber does not mean anywhere else in the mod: it is
                // the color this very tab uses for a bill that cannot run. Reading "done" and "in trouble" in
                // the same color on the same screen is what the bar was doing until Aaron caught it.
                UIElementPainter.FillRounded(new Rect(bar.x, bar.y, Mathf.Max(2f, bar.width * fill), bar.height),
                    fill >= 1f ? palette.Success : palette.Accent);
            }

            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(bar, (TipSignal)(held + " of " + bill.targetCount + " in the colony."));
        }

        /// <summary>
        /// How many of the bill's product the colony already has.
        ///
        /// Asked of the recipe's own worker counter, which is what the game uses to decide whether the bill should
        /// run at all, so the bar and the bill agree about when it is satisfied. Guarded and defaulting to zero:
        /// it reaches the map's resource counter and a recipe whose product cannot be counted says so rather than
        /// returning a number that means nothing.
        /// </summary>
        private static int Held(Bill_Production bill)
        {
            return UIGuard.Try("Bills.RowTargetCount", () =>
            {
                RecipeWorkerCounter counter = bill.recipe?.WorkerCounter;

                return counter == null || !counter.CanCountProducts(bill) ? 0 : counter.CountProducts(bill);
            }, 0, null);
        }

        /// <summary>
        /// Suspend and delete, on the row itself.
        ///
        /// <b>Here rather than only in the footer, which is what Aaron asked for on 2026-08-19.</b> Suspending one
        /// bill used to be click the row, travel to the bottom of a 760 pixel window, click Suspend, and travel
        /// back for the next one. The footer pair went with this change: with the action on every row they were a
        /// second way to do the same thing, one of which needed a selection first.
        ///
        /// <b>Glyphs rather than words,</b> also asked for. A pause symbol and a bin are read without reading,
        /// which is what a column repeated down a list of thirty six bills needs; two words each are not.
        ///
        /// <b>Delete is armed by colour rather than by a confirmation.</b> A bill is cheap to recreate and the
        /// picker is two clicks away, so a modal per deletion would cost more than the mistake does. The bin is
        /// drawn in the danger colour so it never reads as the neutral half of the pair.
        /// </summary>
        private bool Actions(Rect rect, BillEntry entry, UIColorPaletteDef palette)
        {
            Rect bin = new Rect(rect.xMax - 32f, rect.y + rect.height * 0.5f - 11f, 22f, 22f);
            Rect pause = new Rect(bin.x - 26f, bin.y, 22f, 22f);

            if (BillGlyphs.Pause != null && GzpPalette.IconButton(pause, BillGlyphs.Pause,
                    entry.Suspended ? "Resume this bill" : "Suspend this bill",
                    entry.Suspended ? palette.Warning : palette.TextSecondary))
            {
                entry.Bill.suspended = !entry.Bill.suspended;

                Reread();

                return true;
            }

            if (BillGlyphs.Trash == null || !GzpPalette.IconButton(bin, BillGlyphs.Trash, "Delete this bill",
                    palette.Danger))
                return false;

            entry.Bill.billStack?.Delete(entry.Bill);

            if (selected == entry.Bill)
                selected = null;

            Reread();

            return true;
        }

        /// <summary>
        /// What the bill makes, drawn beside its name.
        ///
        /// <b>The recipe's own icon thing, which is not always what it produces.</b> A recipe can name an icon
        /// explicitly, and the ones that do are the ones that need it: surgery, disassembly and anything else
        /// whose product is nothing or is a corpse. Reading <c>ProducedThingDef</c> directly would give those
        /// rows a blank square.
        ///
        /// A recipe with neither is left blank rather than given a placeholder, because a question mark in a row
        /// reads as something being wrong with the bill.
        /// </summary>
        private static void Product(Rect rect, BillEntry entry)
        {
            ThingDef icon = entry.Bill?.recipe?.UIIconThing;

            if (icon == null)
                return;

            // A suspended bill is faded the same way its text is, so the row reads as one thing switched off
            // rather than a live picture with dead words beside it. Passed as the icon's own alpha rather than
            // set on GUI.color, because the icon draws through a material that does not read it.
            Widgets.DefIcon(rect, icon, alpha: entry.Suspended ? 0.4f : 1f);
        }

        /// <summary>
        /// The grip, and everything that happens while a row is being dragged.
        ///
        /// <b>Order is priority within one bench and means nothing between them,</b> so a drag that would cross a
        /// bench heading is refused rather than quietly read as a move to that bench. The row under the cursor
        /// shows which it will be before the button is released: an accent line where the bill would land, a red
        /// one where it would not.
        ///
        /// <b>The grip is drawn as dots.</b> It was a colon, which is what a grip looks like when nobody has
        /// drawn one: too faint to notice and too small to aim at, so the only way to discover that rows could be
        /// reordered was to be told. Six dots in two columns is the shape every list in every program uses for
        /// this, and it is now wide enough and dark enough to read as a handle.
        /// </summary>
        private void Drag(Rect rect, BillEntry entry, UIColorPaletteDef palette)
        {
            Rect grip = new Rect(rect.x + 2f, rect.y + 4f, 16f, rect.height - 8f);

            if (dragging == null)
            {
                bool hovering = Mouse.IsOver(grip);

                if (hovering)
                    UIElementPainter.FillRounded(grip, palette.HoverOverlay);

                Grip(grip, hovering ? palette.TextPrimary : palette.TextSecondary);

                if (hovering && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    dragging = entry.Bill;
                    dragBench = entry.Bench;
                    note = null;

                    Event.current.Use();
                }

                return;
            }

            if (entry.Bill == dragging)
            {
                // The row being carried, dimmed so the gap it leaves is visible.
                UIElementPainter.FillRounded(rect, palette.SurfaceSunken);

                return;
            }

            if (!Mouse.IsOver(rect))
                return;

            this.over = entry;

            bool same = entry.Bench == dragBench;

            UIElementPainter.FillRounded(new Rect(rect.x, rect.y, rect.width, 2f),
                same ? palette.Accent : palette.Danger);
        }

        /// <summary>Two columns of three dots, centered in the grip.</summary>
        private static void Grip(Rect rect, Color color)
        {
            float cx = Mathf.Round(rect.center.x);
            float cy = Mathf.Round(rect.center.y);

            for (int column = 0; column < 2; column++)
            {
                for (int row = 0; row < 3; row++)
                {
                    UIElementPainter.FillRounded(
                        new Rect(cx - 3f + column * 4f, cy - 6f + row * 5f, 2f, 2f), color);
                }
            }
        }

        /// <summary>
        /// Settles a drag when the button comes up.
        ///
        /// A drop onto another bench does nothing at all and says why. Nothing is moved, copied or created, because
        /// the two readings of that gesture are too different to pick one on the player's behalf.
        /// </summary>
        private void Drop()
        {
            Bill_Production carried = dragging;
            BillEntry onto = over;

            dragging = null;
            dragBench = null;
            over = null;

            if (carried == null || onto == null || onto.Bill == carried)
                return;

            if (onto.Bench != carried.billStack?.billGiver)
            {
                note = "Bill order is its priority on its own bench, so a bill cannot be dragged to another one.";

                return;
            }

            BillStack stack = onto.Bench?.billStack;

            if (stack == null)
                return;

            int from = stack.IndexOf(carried);
            int to = stack.IndexOf(onto.Bill);

            if (from < 0 || to < 0 || from == to)
                return;

            // Reorder guards its lower bound only, and inserting past the end of the shortened list would throw,
            // so the target is clamped here rather than trusted.
            stack.Reorder(carried, Mathf.Clamp(to, 0, stack.Count - 1) - from);

            note = null;

            Reread();
        }

        private static string Detail(BillEntry entry, int held)
        {
            Bill_Production bill = entry.Bill;

            // <b>The count against the target, not just the target.</b> "Until you have 500" says a target
            // exists and nothing else; the bar underneath then carries the only part worth reading, and it
            // carried it as a tooltip nobody hovers. With both numbers on the line the bar is confirming what
            // the row already says rather than being the only place it is said.
            if (bill.repeatMode == BillRepeatModeDefOf.TargetCount)
                return "Until you have " + held.ToString("N0") + " / " + bill.targetCount.ToString("N0");

            if (bill.repeatMode == BillRepeatModeDefOf.Forever)
                return "Forever";

            return "Do " + bill.repeatCount + "x";
        }

        private static string State(BillEntry entry)
        {
            if (entry.Suspended)
                return "suspended";

            switch (entry.Trouble)
            {
                case BillTrouble.NoWorker: return "nobody can work it";
                default: return entry.Bill.paused ? "paused" : "running";
            }
        }

        // ------------------------------------------------------------------ editor

        private void DrawEditor(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.WindowBackground);

            Rect inner = rect.ContractedBy(12f);

            if (selected == null)
            {
                Text.Font = GameFont.Small;
                GUI.color = palette.TextDisabled;

                Widgets.Label(inner, "Choose a bill to edit it here.");

                return;
            }

            float y = inner.y;

            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;

            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), selected.LabelCap);

            y += 24f;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(inner.x, y, inner.width, 18f), selected.recipe?.LabelCap);

            y += 26f;

            y = Group(inner, y, palette, "Repeat");
            y = Modes(inner, y, palette);
            y = Counter(inner, y, palette);

            y = Group(inner, y, palette, "Worker");
            y = Worker(inner, y, palette);

            Skill(inner, y, palette);

            Rect save = new Rect(inner.x, inner.yMax - 30f, inner.width, 28f);

            if (Button(save, "Save as template", palette))
                Find.WindowStack.Add(new Dialog_BillTemplates(selected, true));
        }

        /// <summary>
        /// The ingredient column: how far to look, and what counts.
        ///
        /// <b>Its own column because the tree is the widest thing in the window,</b> not because ingredients are
        /// more important than the rest. Every row is a name at some depth of indent with a switch pinned to the
        /// right, so width is what decides whether a row reads at all; height only decides how many of them you
        /// see at once. Sharing the settings column meant it had neither.
        ///
        /// The radius comes with it rather than staying beside the repeat count, since it is the other half of
        /// the same question. What may be used, and how far away it may be.
        /// </summary>
        private void DrawFilter(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.WindowBackground);

            // Left blank rather than repeating the editor's invitation, which is already on screen beside it.
            if (selected == null)
                return;

            Rect inner = rect.ContractedBy(12f);

            float y = Group(inner, inner.y, palette, "Ingredients");

            y = Radius(inner, y, palette);

            Filter(new Rect(inner.x, y + 4f, inner.width, inner.yMax - y - 8f), palette);
        }

        private static float Group(Rect inner, float y, UIColorPaletteDef palette, string title)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(inner.x, y + 6f, inner.width, 18f), title.ToUpperInvariant());

            UIElementPainter.FillRounded(new Rect(inner.x, y + 24f, inner.width, 1f), palette.Border);

            return y + 28f;
        }

        private float Modes(Rect inner, float y, UIColorPaletteDef palette)
        {
            Rect row = new Rect(inner.x, y + 4f, inner.width, 26f);
            float third = row.width / 3f;

            Mode(new Rect(row.x, row.y, third, row.height), BillRepeatModeDefOf.RepeatCount, "Do X times", palette);
            Mode(new Rect(row.x + third, row.y, third, row.height), BillRepeatModeDefOf.TargetCount,
                "Until you have", palette);
            Mode(new Rect(row.x + third * 2f, row.y, third, row.height), BillRepeatModeDefOf.Forever, "Forever",
                palette);

            return row.yMax + 4f;
        }

        /// <summary>
        /// One segment of a radio row: the mod's button, with the selected one toggled on.
        ///
        /// <b>Hand drawn until 2026-08-25, and it showed.</b> It painted its own outline and never looked at the
        /// mouse, so a row of these sat in a window full of buttons that all lit up when the pointer crossed them
        /// and stayed dark. The control has a toggled state for exactly this -- accent border, accent text, muted
        /// accent fill -- so the look is unchanged and the behaviour is now everyone else's.
        /// </summary>
        private void Mode(Rect rect, BillRepeatModeDef mode, string label, UIColorPaletteDef palette)
        {
            bool on = selected.repeatMode == mode;

            if (UIActionButtonControl.Draw(rect, label, palette, false, true, GameFont.Tiny, null, on) && !on)
                selected.repeatMode = mode;
        }

        private float Counter(Rect inner, float y, UIColorPaletteDef palette)
        {
            if (selected.repeatMode == BillRepeatModeDefOf.Forever)
                return y;

            bool target = selected.repeatMode == BillRepeatModeDefOf.TargetCount;
            int value = target ? selected.targetCount : selected.repeatCount;

            int changed = counterBox.Draw(new Rect(inner.x, y + 4f, inner.width, 26f), palette,
                target ? "Target" : "Count", selected, value, 1, 99999);

            if (changed != value)
            {
                if (target)
                    selected.targetCount = changed;
                else
                    selected.repeatCount = changed;
            }

            return y + 34f;
        }

        private float Radius(Rect inner, float y, UIColorPaletteDef palette)
        {
            int value = Mathf.Clamp(Mathf.RoundToInt(selected.ingredientSearchRadius), 3, 999);

            int changed = radiusBox.Draw(new Rect(inner.x, y + 4f, inner.width, 26f), palette, "Radius",
                selected, value, 3, 999);

            if (changed != value)
                selected.ingredientSearchRadius = changed;

            return y + 34f;
        }

        /// <summary>
        /// Who is allowed to work this bill.
        ///
        /// A button rather than a list, because the answer is nearly always "anyone" and the menu behind it is as
        /// long as the colony.
        /// </summary>
        private float Worker(Rect inner, float y, UIColorPaletteDef palette)
        {
            Rect row = new Rect(inner.x, y + 4f, inner.width, 26f);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(row.x, row.y, 90f, row.height), "Assigned to");

            Text.Anchor = TextAnchor.UpperLeft;

            Rect button = new Rect(row.x + 96f, row.y, row.width - 96f, row.height);

            if (Button(button, BillActions.WorkerLabel(selected), palette))
                BillActions.ChooseWorker(selected, Reread);

            return y + 34f;
        }

        /// <summary>
        /// The skill range a worker has to fall inside.
        ///
        /// <b>Hidden when it cannot apply,</b> which is exactly when RimWorld hides it: a bill given to one named
        /// pawn is already answered, a recipe with no work skill has nothing to measure, and a mech does not have
        /// skills at all. Showing a control that the game will ignore is worse than showing nothing, because the
        /// player sets it and then watches it do nothing.
        ///
        /// <b>The two ends are clamped against each other rather than independently,</b> so pushing the minimum
        /// past the maximum carries the maximum along instead of producing a range that can never be satisfied.
        ///
        /// <b>Stacked rather than side by side.</b> Two of these on one row leaves each about 150 pixels, and a
        /// label, a minus, a number and a plus do not fit in 150. The first attempt put them side by side and the
        /// number fields came out a pixel wide.
        /// </summary>
        private float Skill(Rect inner, float y, UIColorPaletteDef palette)
        {
            if (selected.PawnRestriction != null || selected.recipe?.workSkill == null || selected.MechsOnly)
                return y;

            IntRange range = selected.allowedSkillRange;

            int low = skillLowBox.Draw(new Rect(inner.x, y + 4f, inner.width, 26f), palette, "Min skill",
                selected, range.min, 0, 20);

            int high = skillHighBox.Draw(new Rect(inner.x, y + 34f, inner.width, 26f), palette, "Max skill",
                selected, range.max, 0, 20);

            if (low != range.min)
                high = Mathf.Max(high, low);
            else if (high != range.max)
                low = Mathf.Min(low, high);

            if (low != range.min || high != range.max)
                selected.allowedSkillRange = new IntRange(Mathf.Min(low, high), Mathf.Max(low, high));

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(inner.x, y + 62f, inner.width, 16f),
                selected.recipe.workSkill.LabelCap + " between " + selected.allowedSkillRange.min + " and "
                + selected.allowedSkillRange.max);

            return y + 80f;
        }

        /// <summary>
        /// The ingredient filter, drawn by the game's own panel so ours reskins it.
        ///
        /// <b>Handed to <c>ThingFilterUI</c> rather than drawn here.</b> That call is already patched by this mod
        /// into our own tree, so routing through it means the filter in this window and the filter in every other
        /// window in the game are one implementation. Drawing a second tree would be a second thing to keep
        /// correct as categories, special filters and hit point ranges change between versions.
        ///
        /// <b>A recipe whose ingredients are all fixed gets a sentence instead.</b> There is nothing to choose:
        /// the recipe names exactly what it consumes. Vanilla omits the panel entirely in that case, which leaves
        /// a player wondering where it went, so this says so.
        /// </summary>
        private void Filter(Rect rect, UIColorPaletteDef palette)
        {
            if (rect.height < FilterMinimumHeight)
                return;

            RecipeDef recipe = selected.recipe;

            if (recipe == null)
                return;

            if (!Choosable(recipe))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(rect.x, rect.y + 4f, rect.width, 36f),
                    "This recipe uses fixed ingredients, so there is nothing to choose.");

                return;
            }

            ThingFilterUI.DoThingFilterConfigWindow(rect, filterState, selected.ingredientFilter,
                recipe.fixedIngredientFilter, 4, null, Hidden(recipe), false, false, false,
                recipe.GetPremultipliedSmallIngredients(), selected.Map);
        }

        /// <summary>
        /// The special filters this recipe's tree must not offer.
        ///
        /// <b>The four diet toggles are hidden when Ideology is active,</b> which reads backwards until you see
        /// why: with Ideology installed, whether a colony eats meat or people is a precept rather than a per bill
        /// switch, so the toggles would be four controls that argue with the ideoligion. This is RimWorld's own
        /// rule, reproduced rather than referenced because the list it keeps is private to its bill dialog.
        /// </summary>
        private static IEnumerable<SpecialThingFilterDef> Hidden(RecipeDef recipe)
        {
            if (ModsConfig.IdeologyActive)
            {
                yield return SpecialThingFilterDefOf.AllowCarnivore;
                yield return SpecialThingFilterDefOf.AllowVegetarian;
                yield return SpecialThingFilterDefOf.AllowCannibal;
                yield return SpecialThingFilterDefOf.AllowInsectMeat;
            }

            List<SpecialThingFilterDef> forced = recipe.forceHiddenSpecialFilters;

            if (forced == null)
                yield break;

            foreach (SpecialThingFilterDef filter in forced)
                yield return filter;
        }

        /// <summary>Whether any of the recipe's ingredients leave the player a choice.</summary>
        private static bool Choosable(RecipeDef recipe)
        {
            List<IngredientCount> ingredients = recipe.ingredients;

            if (ingredients == null)
                return false;

            foreach (IngredientCount ingredient in ingredients)
            {
                if (!ingredient.IsFixedIngredient)
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ footer

        private void Footer(Rect inRect, UIColorPaletteDef palette)
        {
            Rect rect = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);

            Rect close = new Rect(rect.xMax - 90f, rect.y + 6f, 90f, 30f);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            int troubled = BillCatalog.Troubled(groups);

            // <b>No Suspend and Delete buttons here any more.</b> Both actions are on every row, so a pair down
            // here was a second route to the same thing that additionally required selecting a bill first, which
            // is the round trip Aaron asked to be rid of. The line they used to sit beside now starts at the left
            // edge and has the room to say something.
            //
            // A refused drag takes the line, because it is the thing that just happened and the player needs to
            // know their gesture did nothing rather than assume it worked.
            GUI.color = note == null ? palette.TextSecondary : palette.Warning;

            Widgets.Label(new Rect(rect.x, rect.y, close.x - rect.x - 14f, FooterHeight),
                note ?? (troubled == 0
                    ? "Every bill has somebody who can work it."
                    : troubled + (troubled == 1 ? " bill has" : " bills have") + " nobody who can work them."));

            Text.Anchor = TextAnchor.UpperLeft;

            if (Button(close, "Close", palette))
                Close();
        }
    }
}
