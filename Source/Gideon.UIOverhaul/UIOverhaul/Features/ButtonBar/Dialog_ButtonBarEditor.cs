using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Patches.UIElements;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>
    /// Edits the button bar: what is on it, in what order, grouped into which menus, and with which icons.
    ///
    /// Works on a copy and writes only when Save is pressed, so backing out of a set of changes is
    /// possible. The bar reads the same config object, which is why the copy matters -- editing in place
    /// would restyle the bar underneath the window as each change was made.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Dialog_ButtonBarEditor : Window
    {
        /// <summary>
        /// Clear space kept around every piece of text in this window.
        ///
        /// Applied on all four sides of a label rather than just the leading edge, so nothing sits against
        /// the edge of its card or against the line above it. The bands below are sized to the text plus
        /// this on both sides; shaving it to make a row shorter is what produced the overlap it exists to
        /// prevent.
        /// </summary>
        private const float TextPad = 3f;

        /// <summary>
        /// Height of one line at a given font, asked of RimWorld rather than assumed.
        ///
        /// <b>Tiny is not always tiny.</b> <c>Text.Font</c>'s setter silently substitutes Small when
        /// <c>TinyFontSupported</c> is false, which it is when the player has turned tiny text off or the
        /// active language cannot render it. A rect sized for a tiny line then clips a small one, which is
        /// what happened to the source line under each name. Because this sets the font before measuring, it
        /// reports the height of whatever will actually be drawn, substitution included.
        ///
        /// The font is restored, so measuring during layout cannot disturb whatever was drawing.
        /// </summary>
        private static float LineHeightOf(GameFont font)
        {
            GameFont previous = Text.Font;
            Text.Font = font;
            float height = Text.LineHeight;
            Text.Font = previous;

            return height;
        }

        /// <summary>
        /// Identity band at the top of a row: icon, name and source mod.
        ///
        /// Two measured lines with <see cref="TextPad"/> above each and below the pair. Measured rather than a
        /// constant because the two fonts' heights depend on the player's language and text settings, so any
        /// number written here would be right on one machine and clip on another.
        /// </summary>
        private static float HeaderBandHeight =>
            TextPad + LineHeightOf(GameFont.Small) + TextPad + LineHeightOf(GameFont.Tiny) + TextPad;

        /// <summary>
        /// The controls under the identity band.
        ///
        /// Tall enough for a <see cref="ButtonSize"/> control with <see cref="TextPad"/> above and below,
        /// plus room for the name field's own border and inner padding.
        /// </summary>
        private const float SettingsBandHeight = 38f;

        private static float RowHeight => HeaderBandHeight + SettingsBandHeight;

        /// <summary>Height of the thin "add a tab to this menu" row under a menu's children.</summary>
        private const float AddRowHeight = 28f;

        private const float Gap = 4f;
        private const float ButtonSize = 26f;

        /// <summary>Vertical inset that centers a <see cref="ButtonSize"/> control in the settings band.</summary>
        private const float ButtonInset = (SettingsBandHeight - ButtonSize) * 0.5f;

        /// <summary>How far a row inside a menu is indented, which is what shows it is nested.</summary>
        private const float ChildIndent = 18f;

        private const float ColumnGap = 12f;
        private const float FooterHeight = 40f;

        /// <summary>
        /// The window's title strip: a Medium line, then a Small line of instructions.
        ///
        /// Measured for the same reason the rows are. At a fixed 56 a taller Medium line pushed the
        /// instructions down into the columns below, which start immediately under this.
        /// </summary>
        private static float HeaderHeight =>
            LineHeightOf(GameFont.Medium) + TextPad + LineHeightOf(GameFont.Small) + TextPad * 2f;

        /// <summary>The strip at the top that drags the window. Shorter than the header, which also holds a
        /// line of instructions that should not be a grab handle.</summary>
        private const float TitleBarHeight = 32f;

        /// <summary>Width of the grip glyph at the left of a draggable row.</summary>
        private const float GripWidth = 12f;

        private readonly UIButtonBarConfig working;

        private Vector2 barScroll;
        private Vector2 availableScroll;

        /// <summary>Index of the entry a child is being added to, or -1 when not choosing.</summary>
        private int assigningTo = -1;

        public override Vector2 InitialSize => new Vector2(880f, 620f);

        protected override float Margin => 0f;

        public Dialog_ButtonBarEditor()
        {
            working = Clone(UIButtonBarConfig.Current);

            // Every entry made explicit up front. The stored config may name only what the player has
            // touched, and an editor that showed a partial list would be lying about what is on the bar.
            List<UIButtonBarEntry> resolved = UIButtonBarConfig.Current.Resolve();
            if (working.entries.Count == 0)
                working.entries = resolved;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
        }

        private static UIButtonBarConfig Clone(UIButtonBarConfig source)
        {
            UIButtonBarConfig copy = new UIButtonBarConfig();
            copy.hidden.AddRange(source.hidden);

            foreach (UIButtonBarEntry entry in source.entries)
                copy.entries.Add(CloneEntry(entry));

            return copy;
        }

        /// <summary>
        /// A deep copy of one entry.
        ///
        /// Every field, including the ones this dialog has no control for. Save writes the working copy over
        /// the live config wholesale, so a field missed here is a field the player silently loses by opening
        /// the editor and pressing Save.
        ///
        /// Children are cloned rather than shared. They are entries now, and copying the list alone would
        /// hand the working copy the live config's own child objects -- editing a tab inside a menu would
        /// then change the bar underneath the window and survive a Cancel.
        /// </summary>
        private static UIButtonBarEntry CloneEntry(UIButtonBarEntry entry)
        {
            UIButtonBarEntry clone = new UIButtonBarEntry
            {
                tab = entry.tab,
                menu = entry.menu,
                widget = entry.widget,
                icon = entry.icon,
                label = entry.label,
                mode = entry.mode,
                last = entry.last
            };

            foreach (UIButtonBarEntry child in entry.children)
                clone.children.Add(CloneEntry(child));

            return clone;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // This window had the restriction before anything else did, because its draggable rows fought the
            // window drag for every press. It now shares the one definition of the rule.
            UIWindowDrag.TitleBarOnly(this, inRect.y + 10f + TitleBarHeight);

            UIGuardedPanel.Draw("ButtonBar.Editor", inRect, () => DrawContents(inRect),
                "The bar editor shows a failure notice. The saved layout is untouched, so the bar itself "
                + "keeps working.");
        }

        private void DrawContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            // Deliberately no fill: RimWorld has already painted this color and the window border across
            // inRect, and repainting it here covered the border. See Patch_Widgets_WindowChrome.

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Rect header = new Rect(inRect.x + ColumnGap, inRect.y + 10f, inRect.width - ColumnGap * 2f,
                HeaderHeight);

            DrawHeader(header, palette);

            Rect body = new Rect(inRect.x + ColumnGap, header.yMax,
                inRect.width - ColumnGap * 2f,
                inRect.height - HeaderHeight - FooterHeight - 20f);

            float columnWidth = (body.width - ColumnGap) * 0.5f;
            Rect left = new Rect(body.x, body.y, columnWidth, body.height);
            Rect right = new Rect(left.xMax + ColumnGap, body.y, columnWidth, body.height);

            // Captured here, outside every scroll view, because that is the only place these two are in the
            // same coordinate space. Inside the bar column's scroll view the mouse position is offset by the
            // scroll matrix, so testing it against this rect there would compare unrelated numbers.
            availableColumn = right;
            windowMouse = Event.current.mousePosition;

            // Before anything is drawn, so every row's stripe and the footer's message come from one pass over
            // the names rather than from each row deciding for itself.
            Validate();

            DrawBarColumn(left, palette);
            DrawAvailableColumn(right, palette);

            DrawFooter(new Rect(inRect.x + ColumnGap, inRect.yMax - FooterHeight - 8f,
                inRect.width - ColumnGap * 2f, FooterHeight), palette);

            GUI.color = previousColor;
            Text.Font = previousFont;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawHeader(Rect r, UIColorPaletteDef palette)
        {
            bool previousWrap = Text.WordWrap;

            // Both lines are single lines by construction. The instructions in particular are long enough to
            // wrap at a narrow UI scale, and a wrapped second line has nowhere to go but over the column
            // headings below.
            Text.WordWrap = false;

            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;
            float titleHeight = Text.LineHeight;
            Widgets.Label(new Rect(r.x, r.y, r.width - 200f, titleHeight), "Edit Designator Tabs");

            Text.Font = GameFont.Small;
            GUI.color = palette.TextSecondary;

            string instructions = assigningTo >= 0
                ? "Pick a tab on the right to put inside the menu."
                : "Drag a row to reorder it, onto a menu to put it inside, out of a menu to take it "
                  + "back out, or onto the right to hide it.";

            Rect instructionRect = new Rect(r.x, r.y + titleHeight + TextPad, r.width, Text.LineHeight);
            Widgets.Label(instructionRect, instructions.Truncate(instructionRect.width));

            GUI.color = palette.TextPrimary;
            Text.WordWrap = previousWrap;
        }

        // ---------------------------------------------------------------------------------------
        // On the bar
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// One drawn row, and where in the layout its entry actually lives.
        ///
        /// Built fresh each frame and used for drawing and for dragging both, so the two cannot disagree
        /// about which row is where. Before children were rows in their own right, the drag code walked
        /// top-level blocks and computed their heights a second time; a child could not be a drag source or a
        /// drop target because nothing knew where one was.
        /// </summary>
        private struct RowSlot
        {
            /// <summary>Index of the menu this row is inside, or -1 for a top-level slot.</summary>
            public int Parent;

            /// <summary>Position within whichever list <see cref="Parent"/> names.</summary>
            public int Index;

            public float Top;
        }

        private readonly List<RowSlot> rows = new List<RowSlot>();

        /// <summary>Height of everything in the bar column, set by <see cref="BuildRows"/>.</summary>
        private float contentHeight;

        /// <summary>
        /// Lays out every row: each top-level slot, then the children of any menu, then its add-a-tab row.
        /// </summary>
        private void BuildRows()
        {
            rows.Clear();

            float y = 0f;

            for (int i = 0; i < working.entries.Count; i++)
            {
                UIButtonBarEntry entry = working.entries[i];

                rows.Add(new RowSlot { Parent = -1, Index = i, Top = y });
                y += RowHeight + Gap;

                if (!entry.IsMenu)
                    continue;

                for (int c = 0; c < entry.children.Count; c++)
                {
                    rows.Add(new RowSlot { Parent = i, Index = c, Top = y });
                    y += RowHeight + Gap;
                }

                // The add row is not a RowSlot: it is a button, not something that can be dragged or
                // dropped onto, and giving it an address would make it a drop target for its own menu.
                y += AddRowHeight + Gap;
            }

            contentHeight = y;
        }

        private void DrawBarColumn(Rect r, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(r, palette.PanelBackground);
            Rect inner = r.ContractedBy(8f);

            BuildRows();
            layoutDirty = false;

            Rect view = new Rect(0f, 0f, inner.width - 18f, contentHeight + Gap);

            Widgets.BeginScrollView(inner, ref barScroll, view);

            foreach (RowSlot row in rows)
            {
                float x = row.Parent < 0 ? 0f : ChildIndent;
                DrawEntryRow(new Rect(x, row.Top, view.width - x, RowHeight), row, palette);

                // A button on the row just drawn may have rearranged the lists -- removed it, moved it, taken
                // it out of its menu -- which makes every address after this one point at the wrong entry.
                // Stop here and let the next frame lay the column out again.
                if (layoutDirty)
                    break;
            }

            if (layoutDirty)
            {
                // Whatever was being dragged is at a different address now, or gone. Carrying the old one into
                // the next frame would aim the drop at whichever row inherited its index.
                dragFrom = new RowSlot { Parent = -1, Index = -1 };
                dragActive = false;
            }
            else
            {
                DrawAddRows(view, palette);

                // After the rows, so the insertion line and the menu highlight draw over them rather than
                // under.
                HandleDrag(view, palette);
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// The "add a tab to this menu" row under each menu's children.
        ///
        /// A second pass rather than inline with the rows, because it is the one piece of the column that is
        /// not a <see cref="RowSlot"/> and threading it through that loop would mean giving it an address it
        /// has no use for.
        /// </summary>
        private void DrawAddRows(Rect view, UIColorPaletteDef palette)
        {
            float y = 0f;

            for (int i = 0; i < working.entries.Count; i++)
            {
                UIButtonBarEntry entry = working.entries[i];
                y += RowHeight + Gap;

                if (!entry.IsMenu)
                    continue;

                y += entry.children.Count * (RowHeight + Gap);

                Rect addRow = new Rect(ChildIndent, y, view.width - ChildIndent, AddRowHeight);
                if (SmallButton(addRow, "+ add a tab to this menu", palette))
                {
                    assigningTo = i;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                y += AddRowHeight + Gap;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Dragging
        //
        // No live reordering as the pointer moves. It reads well for a flat list, but a row here can also be
        // a drop target in its own right -- dropping a tab onto a menu puts it inside -- and a list that
        // rearranges under the cursor makes "which row am I over" ambiguous at the moment it matters most.
        // An insertion line plus a highlight on a menu keeps the two outcomes distinct.
        // ---------------------------------------------------------------------------------------

        /// <summary>Position of the row being dragged, or -1 in <c>Index</c> when nothing is.</summary>
        private RowSlot dragFrom = new RowSlot { Parent = -1, Index = -1 };

        /// <summary>Set by anything that rearranges the lists mid-frame. See <see cref="DrawBarColumn"/>.</summary>
        private bool layoutDirty;

        private Vector2 dragOrigin;

        /// <summary>The available column's rect and the pointer, both in window space. See DoWindowContents.</summary>
        private Rect availableColumn;

        private Vector2 windowMouse;

        /// <summary>
        /// Set once the pointer has moved far enough to mean a drag rather than a click.
        ///
        /// The press is deliberately not consumed, so a plain click still reaches the row's own buttons. This
        /// threshold is what keeps a click on the icon slot from also counting as a one-pixel drag.
        /// </summary>
        private bool dragActive;

        private const float DragThresholdSquared = 20f;

        /// <summary>
        /// The grab handle: three short vertical bars, centered in their slot.
        ///
        /// Drawn rather than loaded from a texture. At this size it is three rectangles, and a PNG would be
        /// one more file to ship and to tint for no gain.
        /// </summary>
        private static void DrawGrip(Rect slot, UIColorPaletteDef palette)
        {
            const float barWidth = 2f;
            const float barHeight = 12f;
            const float spacing = 4f;

            float totalWidth = barWidth * 3f + spacing * 2f;
            float x = slot.x + (slot.width - totalWidth) * 0.5f;
            float y = slot.y + (slot.height - barHeight) * 0.5f;

            for (int i = 0; i < 3; i++)
            {
                Widgets.DrawBoxSolid(new Rect(x, y, barWidth, barHeight), palette.TextDisabled);
                x += barWidth + spacing;
            }
        }

        /// <summary>The list a row address refers to: a menu's children, or the top level.</summary>
        private List<UIButtonBarEntry> ListOf(int parent)
        {
            return parent < 0 || parent >= working.entries.Count
                ? working.entries
                : working.entries[parent].children;
        }

        /// <summary>The entry at a row address, or null if the address no longer points at anything.</summary>
        private UIButtonBarEntry EntryAt(RowSlot slot)
        {
            List<UIButtonBarEntry> list = ListOf(slot.Parent);
            return slot.Index >= 0 && slot.Index < list.Count ? list[slot.Index] : null;
        }

        private bool Dragging => dragFrom.Index >= 0 && EntryAt(dragFrom) != null;

        /// <summary>
        /// Which row contains a view-space Y.
        ///
        /// Past the last row this reports one position off the end of the top level rather than the last row
        /// itself, so a row can be dropped at the end of the bar. Reporting the last row meant the final
        /// position was the only one unreachable by dragging.
        /// </summary>
        private RowSlot RowAt(float y)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (y < rows[i].Top + RowHeight + Gap)
                    return rows[i];
            }

            return new RowSlot { Parent = -1, Index = working.entries.Count, Top = contentHeight };
        }

        /// <summary>
        /// Whether dropping onto <paramref name="target"/> should nest rather than reorder.
        ///
        /// Only a plain tab goes into a menu, and only onto a menu's own row. Menus do not nest -- the bar
        /// draws one level of them, so a menu inside a menu would be a button with nowhere to open -- and a
        /// widget has no place in a column of tabs.
        ///
        /// Dropping a tab onto the menu it is already inside is not a nest. It would be a no-op that looked
        /// like one, so it falls through to the reorder path and moves the row within the menu instead.
        /// </summary>
        private bool WouldNest(RowSlot target)
        {
            UIButtonBarEntry dragged = EntryAt(dragFrom);

            if (dragged == null || target.Parent >= 0)
                return false;

            if (target.Parent == dragFrom.Parent && target.Index == dragFrom.Index)
                return false;

            UIButtonBarEntry over = EntryAt(target);

            return over != null
                   && over.IsMenu
                   && target.Index != dragFrom.Parent
                   && !dragged.IsMenu
                   && !dragged.IsWidget
                   && !dragged.tab.NullOrEmpty();
        }

        /// <summary>Draws drop feedback and completes the drag. Called inside the scroll view.</summary>
        private void HandleDrag(Rect view, UIColorPaletteDef palette)
        {
            if (!Dragging)
                return;

            Vector2 mouse = Event.current.mousePosition;

            if (!dragActive && (mouse - dragOrigin).sqrMagnitude > DragThresholdSquared)
                dragActive = true;

            RowSlot target = RowAt(mouse.y);

            if (dragActive)
            {
                if (WouldNest(target))
                {
                    Rect row = new Rect(0f, target.Top, view.width, RowHeight);
                    Widgets.DrawBoxSolid(row, palette.SelectionOverlay);

                    Color previous = GUI.color;
                    GUI.color = palette.Accent;
                    Widgets.DrawBox(row, 2);
                    GUI.color = previous;
                }
                else
                {
                    // Indented to where the row would land, so the line shows whether the drop goes into the
                    // menu above it or back out to the top level.
                    float x = target.Parent < 0 ? 0f : ChildIndent;
                    Widgets.DrawBoxSolid(new Rect(x, target.Top - 1f, view.width - x, 2f), palette.Accent);
                }
            }

            if (Event.current.type != EventType.MouseUp)
                return;

            if (dragActive)
            {
                if (availableColumn.Contains(windowMouse))
                    HideDragged();
                else
                    Drop(target);
            }

            dragFrom = new RowSlot { Parent = -1, Index = -1 };
            dragActive = false;
        }

        /// <summary>Dropped on the available column: off the bar, but recoverable from that list.</summary>
        private void HideDragged()
        {
            UIButtonBarEntry dragged = EntryAt(dragFrom);

            // A menu has nothing to go back to over there -- that list is tabs and widgets. Dropping one does
            // nothing rather than quietly destroying it along with its children.
            if (dragged == null || dragged.IsMenu)
                return;

            // Only a tab needs recording. `hidden` exists because an unlisted MainButtonDef is appended to the
            // bar, so taking one off has to be stated; a widget is drawn only where the layout names it, so
            // removing the entry is already the whole of it.
            if (!dragged.tab.NullOrEmpty())
                working.hidden.Add(dragged.tab);

            nameBuffers.Remove(dragged);
            ListOf(dragFrom.Parent).RemoveAt(dragFrom.Index);
            layoutDirty = true;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>True while a tab is being dragged and the pointer is over the available column.</summary>
        private bool DroppingToHide
        {
            get
            {
                if (!dragActive || !Dragging || !availableColumn.Contains(windowMouse))
                    return false;

                return !EntryAt(dragFrom).IsMenu;
            }
        }

        /// <summary>
        /// Completes a drop: into a menu, out of one, or to a new position in whichever list it lands in.
        /// </summary>
        private void Drop(RowSlot target)
        {
            UIButtonBarEntry dragged = EntryAt(dragFrom);
            if (dragged == null)
                return;

            if (WouldNest(target))
            {
                // The menu is taken as a reference before the removal. Indexing it afterwards would be wrong
                // whenever the dragged row sat above it: the gap closing moves the menu down one, so
                // working.entries[target.Index] would be a different entry, or off the end of the list.
                UIButtonBarEntry menu = EntryAt(target);

                ListOf(dragFrom.Parent).RemoveAt(dragFrom.Index);
                menu.children.Add(dragged);

                layoutDirty = true;
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            // Neither a menu nor a widget belongs inside a menu, so a drop aimed into one lands beside the
            // menu instead of vanishing into it.
            int parent = target.Parent;
            if (parent >= 0 && (dragged.IsMenu || dragged.IsWidget))
                parent = -1;

            if (parent == dragFrom.Parent && target.Index == dragFrom.Index)
                return;

            // Both lists are resolved before anything is removed, and held as references rather than as
            // parent indices. A menu's children list is the same object whatever happens to the top-level
            // list around it, which is what makes moving a row between the two need no index arithmetic
            // beyond the one case below.
            List<UIButtonBarEntry> from = ListOf(dragFrom.Parent);
            List<UIButtonBarEntry> to = ListOf(parent);
            int insert = target.Parent == parent ? target.Index : to.Count;

            from.RemoveAt(dragFrom.Index);

            // Within one list, an index past the old position is one too high once the gap closes.
            if (ReferenceEquals(from, to) && insert > dragFrom.Index)
                insert--;

            to.Insert(Mathf.Clamp(insert, 0, to.Count), dragged);

            layoutDirty = true;
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Chrome for the rows. One instance, reconfigured per row -- see UICardControl's own notes.
        ///
        /// DrawChrome rather than Draw throughout this dialog: these cards carry their own buttons, and Draw
        /// would consume the click before the buttons drawn on top of it could see it.
        /// </summary>
        private static readonly UICardControl RowCard = new UICardControl { Padding = 0f, AccentWidth = 3f };

        private void DrawEntryRow(Rect row, RowSlot slot, UIColorPaletteDef palette)
        {
            UIButtonBarEntry entry = EntryAt(slot);
            if (entry == null)
                return;

            List<UIButtonBarEntry> siblings = ListOf(slot.Parent);
            int index = slot.Index;
            bool nested = slot.Parent >= 0;

            // A menu carries the full accent, a widget the informational color, a plain tab the muted accent,
            // so the three kinds of row are distinguishable at a glance without reading their labels. A row
            // whose name is blank or a duplicate takes the danger color instead, which is what ties the error
            // by the Save button to the row that caused it.
            RowCard.AccentColor = IsNameInvalid(entry) ? palette.Danger
                : entry.IsMenu ? palette.Accent
                : entry.IsWidget ? palette.Info
                : palette.AccentMuted;

            // Sunken inside a menu, raised at the top level, so a nested row reads as belonging to the menu
            // above it rather than as a sibling of it. The indent alone was not enough once children became
            // full cards the same height as everything else.
            RowCard.BackgroundColor = nested ? palette.SurfaceSunken : palette.SurfaceRaised;
            RowCard.DrawChrome(row, palette);

            DrawGrip(new Rect(row.x + 6f, row.y, GripWidth, row.height), palette);

            // The grip glyph marks the row as draggable, but the whole left half is the grab area: a 12px
            // target is a fussy thing to hit, and there is nothing else over there to press by mistake. The
            // controls all sit on the right, and the event is left unconsumed either way so a plain click
            // still reaches whatever is under it.
            Rect grip = new Rect(row.x, row.y, row.width * 0.5f, row.height);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                                                          && grip.Contains(Event.current.mousePosition))
            {
                dragFrom = slot;
                dragOrigin = Event.current.mousePosition;
                dragActive = false;
            }

            if (dragActive && dragFrom.Parent == slot.Parent && dragFrom.Index == index)
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            // Identity on top, controls beneath. Every control below is positioned from this band rather than
            // from the row, so the two stay in step if either height changes.
            Rect settings = new Rect(row.x, row.yMax - SettingsBandHeight, row.width, SettingsBandHeight);
            DrawIdentityBand(new Rect(row.x, row.y, row.width, HeaderBandHeight), entry, palette);

            float x = row.xMax - TextPad;

            // Delete on a menu, take-out-of-menu on a nested tab, hide on a top-level one. The same removal
            // underneath, but they are not the same act and should not look alike: a menu is ours and ceases
            // to exist, a tab belongs to a mod and only goes back to the available list.
            x -= ButtonSize;
            Rect removeRect = new Rect(x, settings.y + ButtonInset, ButtonSize, ButtonSize);

            bool removed = entry.IsMenu
                ? IconAction(removeRect, BaseContent.BadTex, "X", palette,
                    "Delete this menu. The tabs inside it come off the bar, into the list on the right.")
                : IconAction(removeRect, "–", palette,
                    nested ? "Take this tab out of the menu and put it back on the bar"
                    : entry.IsWidget ? "Take this widget off the bar, returning it to the list on the right"
                    : "Hide this tab, returning it to the list on the right");

            if (removed)
            {
                RemoveRow(slot);
                return;
            }

            x -= ButtonSize + 2f;
            if (IconAction(new Rect(x, settings.y + ButtonInset, ButtonSize, ButtonSize), arrowDown, "v",
                    palette, nested ? "Move down in the menu" : "Move right")
                && index < siblings.Count - 1)
            {
                siblings[index] = siblings[index + 1];
                siblings[index + 1] = entry;
                layoutDirty = true;
                return;
            }

            x -= ButtonSize + 2f;
            if (IconAction(new Rect(x, settings.y + ButtonInset, ButtonSize, ButtonSize), arrowUp, "^",
                    palette, nested ? "Move up in the menu" : "Move left")
                && index > 0)
            {
                siblings[index] = siblings[index - 1];
                siblings[index - 1] = entry;
                layoutDirty = true;
                return;
            }

            if (entry.IsWidget)
            {
                // No icon slot, display mode or rename. A widget draws its own content, so all three would be
                // controls with nothing to act on. The space goes to its description instead, which is what is
                // worth reading when deciding whether to keep it on the bar.
                UIBarWidgetDef widgetDef = entry.WidgetDef;
                string description = widgetDef?.description ?? "This widget's mod is no longer installed.";

                Rect descriptionRect = new Rect(ContentLeft(row), settings.y,
                    Mathf.Max(0f, x - ContentLeft(row) - TextPad), settings.height);

                // One line, cut with an ellipsis, and the whole thing on hover. Descriptions run to a
                // sentence or two; wrapped into a band this short they were drawn as a centered block and
                // clipped at both ends, so the reader got the middle of the text and neither end of it. The
                // band cannot simply grow: every row is RowHeight tall and the drag arithmetic counts on it.
                //
                // Font set rather than inherited. This is the only text in the row not drawn by a control
                // that sets its own, so whatever the last thing drawn happened to leave behind would decide
                // how much of the description fit.
                bool previousWrap = Text.WordWrap;
                GameFont previousFont = Text.Font;

                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                Widgets.Label(descriptionRect, description.Truncate(descriptionRect.width));

                GUI.color = palette.TextPrimary;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = previousFont;
                Text.WordWrap = previousWrap;

                TooltipHandler.TipRegion(descriptionRect, (TipSignal) description);
                return;
            }

            // Icon slot. Clicking it opens the picker, which is the route for a tab whose mod never gave
            // it an icon.
            x -= ButtonSize + 6f;
            DrawIconSlot(new Rect(x, settings.y + ButtonInset, ButtonSize, ButtonSize), entry, palette);

            // Display mode, as a dropdown. Cycling through four states means clicking up to three times to
            // reach one and having to know the order; a list shows every choice and what each does.
            x -= 106f;
            Rect modeRect = new Rect(x, settings.y + ButtonInset, 102f, ButtonSize);
            if (IconAction(modeRect, ModeLabel(entry.mode) + "  v", palette, ModeTooltip(entry.mode)))
                OpenModeMenu(entry);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextPrimary;

            float labelLeft = ContentLeft(row);
            DrawNameField(new Rect(labelLeft, settings.y, Mathf.Max(0f, x - labelLeft - TextPad),
                settings.height), entry, palette);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextPrimary;
        }

        /// <summary>Left edge of a row's content: past the grip, with clear space either side of it.</summary>
        private static float ContentLeft(Rect row)
        {
            return row.x + TextPad + GripWidth + 6f;
        }

        /// <summary>
        /// Removes whatever a row's remove button removes.
        ///
        /// Four different things, which is why it is one method rather than inline: a nested tab goes back to
        /// the bar, a top-level tab goes off it, a widget just goes, and a menu takes its children off the bar
        /// with it.
        /// </summary>
        private void RemoveRow(RowSlot slot)
        {
            UIButtonBarEntry entry = EntryAt(slot);
            if (entry == null)
                return;

            layoutDirty = true;
            nameBuffers.Remove(entry);

            if (slot.Parent >= 0)
            {
                // Out of the menu and back onto the bar rather than off it: taking a tab out of a menu is not
                // the same act as hiding it, and it keeps whatever rename and icon it was given in there.
                ListOf(slot.Parent).RemoveAt(slot.Index);
                AppendSlot(entry);
                return;
            }

            if (entry.IsMenu)
            {
                // The children are hidden, not returned to the bar. They are off the available list only by
                // virtue of being in here, so deleting the menu has to say where they went -- and saying
                // nothing meant the bar appended every one of them as a top-level tab, while this editor
                // showed them as not on the bar. Hiding is what the two agree on, and the list on the right
                // is where they can be picked back up.
                foreach (UIButtonBarEntry child in entry.children)
                {
                    nameBuffers.Remove(child);

                    if (!child.tab.NullOrEmpty() && !working.hidden.Contains(child.tab))
                        working.hidden.Add(child.tab);
                }
            }
            else if (!entry.tab.NullOrEmpty())
            {
                working.hidden.Add(entry.tab);
            }

            working.entries.RemoveAt(slot.Index);
        }

        // ---------------------------------------------------------------------------------------
        // Names
        //
        // A name box holds text the entry itself cannot: an empty one. A tab stores a rename in `label`,
        // where null means "use the def's own name", so an empty string there would read as "not renamed"
        // and the box would refill itself the moment it lost focus. A menu is worse -- `menu` is what makes
        // the entry a menu at all, so clearing it would turn the row into something with no kind and orphan
        // its children.
        //
        // So a blank box is held here, next to the entry rather than in it, and the entry keeps its last
        // good name until a new one is typed. Save is blocked while any of these is blank, so a blank never
        // reaches the file.
        // ---------------------------------------------------------------------------------------

        private readonly Dictionary<UIButtonBarEntry, string> nameBuffers =
            new Dictionary<UIButtonBarEntry, string>();

        /// <summary>The name an entry has stored: a menu's own text, or a tab's rename falling back to its def.</summary>
        private static string StoredName(UIButtonBarEntry entry)
        {
            if (entry.IsMenu)
                return entry.menu;

            // No "(missing)" here. This is an editable field, and putting a status marker in it meant the
            // marker became the tab's name the moment anything was typed after it. The identity band above
            // still says the def is missing, which is where a status belongs.
            return entry.label ?? DefaultName(entry);
        }

        /// <summary>
        /// What a tab is called when it has not been renamed.
        ///
        /// Through <see cref="UIBarDefaultLabels"/>, so the box offers the same name the bar draws. Reading
        /// the def's label directly would show "Menu" in the field while the bar said "Pause Menu", and
        /// typing the name back would then be stored as a rename rather than recognized as the default.
        /// </summary>
        private static string DefaultName(UIButtonBarEntry entry)
        {
            return UIBarDefaultLabels.DefaultNameFor(entry, entry.Def);
        }

        /// <summary>What the box shows: the blank being typed if there is one, otherwise the stored name.</summary>
        private string ShownName(UIButtonBarEntry entry)
        {
            return nameBuffers.TryGetValue(entry, out string buffered) ? buffered : StoredName(entry);
        }

        private void DrawNameField(Rect rect, UIButtonBarEntry entry, UIColorPaletteDef palette)
        {
            string shown = ShownName(entry);
            string edited = ThemedTextField(rect, shown, palette, IsNameInvalid(entry));

            if (edited == shown)
                return;

            if (edited.NullOrEmpty())
            {
                // Remembered as blank; the entry keeps its name so nothing downstream sees an entry with none.
                nameBuffers[entry] = "";
                return;
            }

            nameBuffers.Remove(entry);

            if (entry.IsMenu)
            {
                entry.menu = edited;
                return;
            }

            // Stored only when it differs from the def's own name, which keeps the file free of entries that
            // just restate what the def already says.
            entry.label = edited == DefaultName(entry) ? null : edited;
        }

        /// <summary>Rows whose name is blank or shared with another row. Rebuilt every frame.</summary>
        private readonly HashSet<UIButtonBarEntry> invalidNames = new HashSet<UIButtonBarEntry>();

        /// <summary>What to say by the Save button, or null when there is nothing wrong.</summary>
        private string nameError;

        private bool IsNameInvalid(UIButtonBarEntry entry)
        {
            return invalidNames.Contains(entry);
        }

        /// <summary>
        /// Checks every name on the bar, before anything is drawn, so a row and the message by the Save
        /// button cannot disagree about whether it is a problem.
        ///
        /// Uniqueness is checked across the whole layout rather than within each menu. Two buttons with the
        /// same name are ambiguous wherever they sit, and one of them being inside a menu does not make the
        /// pair easier to tell apart.
        ///
        /// Widgets are skipped. A widget has no name box -- it draws its own content -- so there is nothing
        /// there to be blank or to collide.
        /// </summary>
        private void Validate()
        {
            invalidNames.Clear();
            nameError = null;

            Dictionary<string, UIButtonBarEntry> seen =
                new Dictionary<string, UIButtonBarEntry>(StringComparer.OrdinalIgnoreCase);

            bool blank = false;
            bool duplicate = false;

            foreach (UIButtonBarEntry entry in Named())
            {
                string name = ShownName(entry);

                if (name.NullOrEmpty() || name.Trim().Length == 0)
                {
                    invalidNames.Add(entry);
                    blank = true;
                    continue;
                }

                string key = name.Trim();

                if (seen.TryGetValue(key, out UIButtonBarEntry first))
                {
                    // Both ends of a collision are marked. Marking only the second would point at whichever
                    // row happened to be drawn later, which is not the one the player just typed into.
                    invalidNames.Add(entry);
                    invalidNames.Add(first);
                    duplicate = true;
                    continue;
                }

                seen[key] = entry;
            }

            if (blank && duplicate)
                nameError = "Every tab and menu needs a name, and no two may share one.";
            else if (blank)
                nameError = "Every tab and menu needs a name.";
            else if (duplicate)
                nameError = "Tabs and menus must have unique names.";
        }

        /// <summary>
        /// A name for a new menu that nothing else on the bar is using.
        ///
        /// "Menu" on its own will not do: that is the label of RimWorld's own pause tab, so a new menu taking
        /// it would come up already marked as a duplicate and with Save disabled, which reads as the button
        /// being broken rather than as a name to change.
        /// </summary>
        private string UniqueMenuName()
        {
            HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (UIButtonBarEntry entry in Named())
            {
                string name = ShownName(entry);
                if (!name.NullOrEmpty())
                    taken.Add(name.Trim());
            }

            if (!taken.Contains("Menu"))
                return "Menu";

            for (int i = 2; i < 100; i++)
            {
                string candidate = "Menu " + i;
                if (!taken.Contains(candidate))
                    return candidate;
            }

            // Ninety-nine menus in, the player is not being helped by another suffix. The duplicate warning
            // takes over from here.
            return "Menu";
        }

        /// <summary>Every row that carries a name box: each slot and each menu's children, widgets aside.</summary>
        private IEnumerable<UIButtonBarEntry> Named()
        {
            foreach (UIButtonBarEntry entry in working.entries)
            {
                if (!entry.IsWidget)
                    yield return entry;

                foreach (UIButtonBarEntry child in entry.children)
                {
                    if (!child.IsWidget)
                        yield return child;
                }
            }
        }

        /// <summary>
        /// What this row is: icon, name, and where it came from -- the three facts the plant cards lead with.
        ///
        /// The band below is for changing things; this one is for knowing what you are changing. The source
        /// matters most when two mods add tabs with similar names, which a bare label cannot disambiguate.
        /// </summary>
        private void DrawIdentityBand(Rect band, UIButtonBarEntry entry, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            float x = ContentLeft(band);

            Texture2D icon = UIBarDefaultIcons.Resolve(entry, entry.Def);
            if (icon != null)
            {
                GUI.color = palette.TextSecondary;
                GUI.DrawTexture(new Rect(x, band.y + (band.height - 24f) * 0.5f, 24f, 24f), icon,
                    ScaleMode.ScaleToFit);
            }

            x += 24f + 8f;

            MainButtonDef def = entry.Def;
            UIBarWidgetDef widgetDef = entry.WidgetDef;

            string name;
            string source;

            if (entry.IsMenu)
            {
                name = entry.menu.NullOrEmpty() ? "Menu" : entry.menu;

                // A menu has no mod behind it, so it says so rather than showing a blank or claiming ours.
                source = "Menu you created";
            }
            else if (entry.IsWidget)
            {
                name = widgetDef != null ? widgetDef.LabelCap.ToString() : entry.widget + " (missing)";

                // Named as a widget as well as by its mod, because the one thing a player needs to know about
                // this row is that it is not a tab and will not open anything.
                source = "Widget from " + (widgetDef?.modContentPack?.Name ?? "an uninstalled mod");
            }
            else
            {
                // "(missing)" stays here, on the status line, for a tab whose mod is gone. It is deliberately
                // not in the name box below, where it would become part of the tab's name once typed after.
                name = def != null
                    ? UIBarDefaultLabels.DefaultNameFor(entry, def)
                    : entry.tab + " (missing)";
                source = def?.modContentPack?.Name ?? "Unknown source";
            }

            float width = Mathf.Max(0f, band.xMax - x - TextPad);

            // One line each, cut with an ellipsis rather than wrapped. The band is sized for exactly two
            // lines, so a name long enough to wrap would push the source line out of the card; truncating
            // keeps the row the height the layout says it is.
            Text.WordWrap = false;

            // Each rect is exactly as tall as the line it holds, measured the same way HeaderBandHeight
            // measured it. That is the whole point: the band and the text inside it read their heights from
            // one place, so they cannot disagree about how much room a line needs.
            Text.Font = GameFont.Small;
            float nameHeight = Text.LineHeight;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(x, band.y + TextPad, width, nameHeight), name.Truncate(width));

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(x, band.y + TextPad + nameHeight + TextPad, width, Text.LineHeight),
                source.Truncate(width));

            Text.WordWrap = previousWrap;
            Text.Font = previousFont;
            GUI.color = previousColor;
        }

        // ---------------------------------------------------------------------------------------
        // Available
        // ---------------------------------------------------------------------------------------

        private void DrawAvailableColumn(Rect r, UIColorPaletteDef palette)
        {
            // Drop feedback for a tab being dragged over here. Drawn in this column rather than from
            // HandleDrag, which runs inside the other column's scroll view and cannot reach these coordinates.
            if (DroppingToHide)
            {
                Widgets.DrawBoxSolid(r, palette.SelectionOverlay);

                Color previousDrop = GUI.color;
                GUI.color = palette.Accent;
                Widgets.DrawBox(r, 2);
                GUI.color = previousDrop;
            }

            Widgets.DrawBoxSolid(r, palette.PanelBackground);
            Rect inner = r.ContractedBy(8f);

            Text.Font = GameFont.Small;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, Text.LineHeight),
                assigningTo >= 0 ? "Choose a tab for the menu" : "Not on the bar");
            GUI.color = palette.TextPrimary;

            Rect listRect = new Rect(inner.x, inner.y + 24f, inner.width, inner.height - 24f);

            List<MainButtonDef> candidates = Candidates();

            // Widgets are not offered while a menu is being filled. A menu reveals tabs, and a widget put
            // inside one would be a readout nothing could ever draw.
            List<UIBarWidgetDef> widgets = assigningTo >= 0
                ? new List<UIBarWidgetDef>()
                : WidgetCandidates();

            // Width first: widget rows are as tall as their description needs, and how much description fits
            // on a line depends on how wide the row is going to be.
            float viewWidth = listRect.width - 18f;

            float viewHeight = candidates.Count * (RowHeight + Gap) + Gap;
            if (widgets.Count > 0)
            {
                viewHeight += SectionLabelHeight;

                foreach (UIBarWidgetDef def in widgets)
                    viewHeight += AvailableWidgetRowHeight(def, viewWidth) + Gap;
            }

            Rect view = new Rect(0f, 0f, viewWidth, viewHeight);

            Widgets.BeginScrollView(listRect, ref availableScroll, view);

            float y = 0f;
            foreach (MainButtonDef def in candidates)
            {
                Rect row = new Rect(0f, y, view.width, RowHeight);
                Widgets.DrawBoxSolid(row, palette.SurfaceRaised);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                // This mod's name for the tab, not the def's, so a tab is listed here under the name it will
                // carry once it is on the bar.
                Rect nameRect = new Rect(row.x + TextPad + 3f, row.y,
                    Mathf.Max(0f, row.width - ButtonSize - 14f), row.height);
                Widgets.Label(nameRect, UIBarDefaultLabels.NameOf(def).Truncate(nameRect.width));

                Text.Anchor = TextAnchor.UpperLeft;

                Rect addRect = new Rect(row.xMax - ButtonSize - 4f, row.y + 4f, ButtonSize, ButtonSize);
                if (IconAction(addRect, "+", palette,
                        assigningTo >= 0 ? "Put in the menu" : "Put on the bar"))
                {
                    Add(def);
                    Widgets.EndScrollView();
                    return;
                }

                y += RowHeight + Gap;
            }

            if (widgets.Count > 0)
            {
                // Under their own heading rather than mixed in with the tabs. Both lists are things you can
                // put on the bar, but only one of them opens anything, and a player looking for a tab should
                // not have to read past four readouts to find it.
                Text.Font = GameFont.Small;
                GUI.color = palette.TextSecondary;
                Widgets.Label(new Rect(0f, y + 6f, view.width, Text.LineHeight), "Widgets");
                GUI.color = palette.TextPrimary;

                y += SectionLabelHeight;

                foreach (UIBarWidgetDef def in widgets)
                {
                    float widgetRowHeight = AvailableWidgetRowHeight(def, view.width);

                    if (DrawAvailableWidgetRow(new Rect(0f, y, view.width, widgetRowHeight), def, palette))
                    {
                        Widgets.EndScrollView();
                        return;
                    }

                    y += widgetRowHeight + Gap;
                }
            }

            Widgets.EndScrollView();
        }

        /// <summary>Height taken by a heading inside the available list, including its breathing room.</summary>
        private const float SectionLabelHeight = 30f;

        /// <summary>
        /// Where a widget's description starts in its row in the available list, under the label.
        ///
        /// Measured, for the same reason the identity band is: the label above it is a line of Small text
        /// whose height is not a number this mod gets to choose.
        /// </summary>
        private static float DescriptionTop => TextPad + LineHeightOf(GameFont.Small) + TextPad;

        /// <summary>Padding under that description, so the text does not sit on the bottom edge of the row.</summary>
        private const float DescriptionBottomPadding = 6f;

        /// <summary>Room for text in a row in the available list, inside the padding and clear of the button.</summary>
        private static float AvailableTextWidth(float rowWidth)
        {
            return Mathf.Max(0f, rowWidth - ButtonSize - 18f);
        }

        /// <summary>
        /// How tall a widget's row has to be for its whole description to show.
        ///
        /// Measured rather than fixed, and never shorter than the tab rows it sits under so the list keeps an
        /// even rhythm. Descriptions are a sentence or two and differ in length; at one fixed height the
        /// longer ones were cut off, and this list is the one place a description is read to decide whether
        /// the widget is wanted at all.
        ///
        /// The font is set before measuring, because <see cref="Text.CalcHeight"/> reports for whatever font
        /// is current and this runs during layout, where that is whatever was drawn last. It must also match
        /// the font <see cref="DrawAvailableWidgetRow"/> draws with, or the row and its text disagree.
        /// </summary>
        private static float AvailableWidgetRowHeight(UIBarWidgetDef def, float rowWidth)
        {
            if (def.description.NullOrEmpty())
                return RowHeight;

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float descriptionHeight = Text.CalcHeight(def.description, AvailableTextWidth(rowWidth));
            Text.Font = previousFont;

            return Mathf.Max(RowHeight, DescriptionTop + descriptionHeight + DescriptionBottomPadding);
        }

        /// <summary>
        /// One widget in the available list. Returns true when it was added, which ends the frame's pass over
        /// the list because the collection it was drawn from has changed.
        /// </summary>
        private bool DrawAvailableWidgetRow(Rect row, UIBarWidgetDef def, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(row, palette.SurfaceRaised);

            GameFont previousFont = Text.Font;

            float textWidth = AvailableTextWidth(row.width);

            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(row.x + 6f, row.y + TextPad, textWidth, Text.LineHeight), def.LabelCap);

            // The description sits in the row rather than in a tooltip: it is the only thing that says what
            // the widget will show, and a player choosing between four of them should not have to hover each.
            // The row was measured to fit it, so this rect takes whatever is left below the label.
            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(row.x + 6f, row.y + DescriptionTop, textWidth, row.height - DescriptionTop),
                def.description ?? "");

            Text.Font = previousFont;
            GUI.color = palette.TextPrimary;

            Rect addRect = new Rect(row.xMax - ButtonSize - 4f, row.y + 4f, ButtonSize, ButtonSize);
            if (!IconAction(addRect, "+", palette, "Put on the bar"))
                return false;

            AppendSlot(new UIButtonBarEntry { widget = def.defName });
            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        /// <summary>
        /// Widgets not already on the bar, ordered as their defs asked.
        ///
        /// Computed rather than stored, so a widget from a newly installed mod turns up here without the
        /// config having to know about it. One instance of each: a second copy of the clock would draw the
        /// same time twice.
        /// </summary>
        private List<UIBarWidgetDef> WidgetCandidates()
        {
            HashSet<string> placed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (UIButtonBarEntry entry in working.entries)
            {
                if (!entry.widget.NullOrEmpty())
                    placed.Add(entry.widget);
            }

            List<UIBarWidgetDef> result = new List<UIBarWidgetDef>();
            foreach (UIBarWidgetDef def in DefDatabase<UIBarWidgetDef>.AllDefsListForReading)
            {
                if (!placed.Contains(def.defName))
                    result.Add(def);
            }

            result.SortBy(def => def.order, def => def.LabelCap.ToString());
            return result;
        }

        /// <summary>
        /// Puts a slot on the bar, ahead of anything pinned to the end.
        ///
        /// Appending to the list outright would place it after the pause menu in this editor while the bar
        /// still drew the menu last, because <c>last</c> is honored when the layout resolves. The row would
        /// have looked like it was in a position it was never going to occupy.
        /// </summary>
        private void AppendSlot(UIButtonBarEntry entry)
        {
            int insert = working.entries.FindIndex(e => e.last);
            working.entries.Insert(insert < 0 ? working.entries.Count : insert, entry);
        }

        private void Add(MainButtonDef def)
        {
            working.hidden.RemoveAll(name =>
                string.Equals(name, def.defName, System.StringComparison.OrdinalIgnoreCase));

            if (assigningTo >= 0 && assigningTo < working.entries.Count)
            {
                UIButtonBarEntry menu = working.entries[assigningTo];

                // Taken off the top level first: a tab cannot be both a slot and inside a menu, and
                // leaving it in both places would draw it twice.
                working.entries.RemoveAll(e => !e.IsMenu
                                               && string.Equals(e.tab, def.defName,
                                                   StringComparison.OrdinalIgnoreCase));

                if (!menu.children.Exists(c => string.Equals(c.tab, def.defName,
                        StringComparison.OrdinalIgnoreCase)))
                    menu.children.Add(new UIButtonBarEntry { tab = def.defName });

                assigningTo = -1;
                layoutDirty = true;
            }
            else
            {
                AppendSlot(new UIButtonBarEntry { tab = def.defName });
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Tabs not currently placed anywhere. Computed rather than stored, so a tab added by a newly
        /// installed mod turns up here without the config having to know about it.
        /// </summary>
        private List<MainButtonDef> Candidates()
        {
            HashSet<string> placed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (UIButtonBarEntry entry in working.entries)
            {
                if (!entry.tab.NullOrEmpty())
                    placed.Add(entry.tab);

                foreach (UIButtonBarEntry child in entry.children)
                {
                    if (!child.tab.NullOrEmpty())
                        placed.Add(child.tab);
                }
            }

            List<MainButtonDef> result = new List<MainButtonDef>();
            foreach (MainButtonDef def in DefDatabase<MainButtonDef>.AllDefsListForReading)
            {
                if (!placed.Contains(def.defName))
                    result.Add(def);
            }

            return result;
        }

        // ---------------------------------------------------------------------------------------
        // Footer
        // ---------------------------------------------------------------------------------------

        private void DrawFooter(Rect r, UIColorPaletteDef palette)
        {
            float buttonWidth = 150f;
            float x = r.x;

            if (SmallButton(new Rect(x, r.y, buttonWidth, 32f), "New menu", palette))
            {
                AppendSlot(new UIButtonBarEntry { menu = UniqueMenuName() });
                layoutDirty = true;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            x += buttonWidth + 8f;

            if (SmallButton(new Rect(x, r.y, buttonWidth, 32f), "Reset to default", palette))
            {
                // The layout this mod ships, not an empty config resolved against the game's own button order.
                // Those were the same thing until a default shipped; now they are not, and resetting has to mean
                // "what a fresh install looks like" -- hidden tabs included, or reset would silently unhide the
                // three tabs the default keeps out of the way.
                UIButtonBarConfig shipped = UIButtonBarConfig.ShippedDefault();

                working.entries = shipped.Resolve();
                working.hidden.Clear();
                working.hidden.AddRange(shipped.hidden);

                // Half-typed names belong to rows that no longer exist.
                nameBuffers.Clear();
                layoutDirty = true;

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (assigningTo >= 0)
            {
                x += buttonWidth + 8f;
                if (SmallButton(new Rect(x, r.y, buttonWidth, 32f), "Cancel", palette))
                    assigningTo = -1;
            }

            Rect saveRect = new Rect(r.xMax - buttonWidth, r.y, buttonWidth, 32f);

            // The reason sits immediately left of the button it disables, so the two are read together. A
            // problem stated at the top of the window would be off screen by the time the player has scrolled
            // to the row causing it, and the row's own red stripe is what leads back here.
            if (nameError != null)
            {
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;
                bool previousWrap = Text.WordWrap;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Text.WordWrap = false;
                GUI.color = palette.Danger;

                float errorLeft = x + buttonWidth + 8f;
                Rect errorRect = new Rect(errorLeft, r.y, Mathf.Max(0f, saveRect.x - errorLeft - 8f), 32f);
                Widgets.Label(errorRect, nameError.Truncate(errorRect.width));

                GUI.color = previousColor;
                Text.WordWrap = previousWrap;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;

                TooltipHandler.TipRegion(errorRect, (TipSignal) nameError);
            }

            if (SmallButton(saveRect, "Save", palette, nameError == null) && nameError == null)
            {
                Commit();
                Close();
            }
        }

        private void Commit()
        {
            UIButtonBarConfig target = UIButtonBarConfig.Current;
            target.entries = working.entries;
            target.hidden = working.hidden;
            target.Save();
        }

        // ---------------------------------------------------------------------------------------
        // Small widgets
        // ---------------------------------------------------------------------------------------

        private void DrawIconSlot(Rect r, UIButtonBarEntry entry, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(r, palette.SurfaceSunken);

            // The same resolution the bar uses, so this slot previews what the button will actually draw
            // rather than showing "-" for a tab that will in fact get one of this mod's icons.
            Texture2D icon = UIBarDefaultIcons.Resolve(entry, entry.Def);

            if (icon != null)
            {
                // Tinted to match the bar, so this slot is a true preview of the button.
                Color previousIcon = GUI.color;
                GUI.color = palette.TextSecondary;
                GUI.DrawTexture(r.ContractedBy(3f), icon, ScaleMode.ScaleToFit);
                GUI.color = previousIcon;
            }
            else
            {
                Color previous = GUI.color;
                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, "-");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = previous;
            }

            TooltipHandler.TipRegion(r, (TipSignal) "Click to set this button's icon.");

            if (!Widgets.ButtonInvisible(r))
                return;

            SoundDefOf.Click.PlayOneShotOnCamera();
            Find.WindowStack.Add(new Dialog_PickBarIcon(entry));
        }

        /// <summary>
        /// The mode list. A FloatMenu rather than a window of our own: it is the dropdown RimWorld uses
        /// everywhere, so it behaves the way a player already expects, and each row carries the
        /// explanation of what that mode does.
        /// </summary>
        private static void OpenModeMenu(UIButtonBarEntry entry)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (UIBarButtonMode mode in System.Enum.GetValues(typeof(UIBarButtonMode)))
            {
                UIBarButtonMode captured = mode;
                string label = ModeLabel(captured) + " - " + ModeSummary(captured);

                options.Add(new FloatMenuOption(label, () => entry.mode = captured));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string ModeSummary(UIBarButtonMode mode)
        {
            switch (mode)
            {
                case UIBarButtonMode.Minimize: return "icon only";
                case UIBarButtonMode.TextOnly: return "text only";
                case UIBarButtonMode.Maximize: return "icon and text, always";
                default: return "icon and text, as the tab asked";
            }
        }

        private static string ModeLabel(UIBarButtonMode mode)
        {
            switch (mode)
            {
                case UIBarButtonMode.Minimize: return "Minimize";
                case UIBarButtonMode.TextOnly: return "Text only";
                case UIBarButtonMode.Maximize: return "Maximize";
                default: return "Default";
            }
        }

        private static string ModeTooltip(UIBarButtonMode mode)
        {
            switch (mode)
            {
                case UIBarButtonMode.Minimize:
                    return "Icon only. Click to change.";
                case UIBarButtonMode.TextOnly:
                    return "Text only, even where an icon exists. Click to change.";
                case UIBarButtonMode.Maximize:
                    return "Icon and full text, even for a tab that asked to be icon-only. Click to change.";
                default:
                    return "Icon and text, respecting a tab that asked to be icon-only. Click to change.";
            }
        }

        /// <param name="enabled">
        /// False draws the button dimmed and reports no clicks. Used for Save while a name is blank or
        /// duplicated: the button staying visible but inert, next to the reason, says more than removing it.
        /// </param>
        private static bool SmallButton(Rect r, string label, UIColorPaletteDef palette, bool enabled = true)
        {
            bool over = enabled && Mouse.IsOver(r);
            UIElementPainter.PaintButton(r, palette, over, over && Input.GetMouseButton(0));

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            GUI.color = enabled ? palette.TextPrimary : palette.TextDisabled;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;

            return enabled && Widgets.ButtonInvisible(r);
        }

        private static bool IconAction(Rect r, string glyph, UIColorPaletteDef palette, string tooltip)
        {
            TooltipHandler.TipRegion(r, (TipSignal) tooltip);
            return SmallButton(r, glyph, palette);
        }

        /// <summary>
        /// As the glyph version, but with a texture on the button.
        ///
        /// Falls through to <paramref name="fallbackGlyph"/> when the texture is missing, so a renamed or
        /// absent art file leaves a working button rather than an invisible one.
        /// </summary>
        private static bool IconAction(Rect r, Texture2D icon, string fallbackGlyph,
            UIColorPaletteDef palette, string tooltip)
        {
            if (icon == null)
                return IconAction(r, fallbackGlyph, palette, tooltip);

            TooltipHandler.TipRegion(r, (TipSignal) tooltip);

            bool over = Mouse.IsOver(r);
            UIElementPainter.PaintButton(r, palette, over, over && Input.GetMouseButton(0));

            Color previous = GUI.color;
            GUI.color = over ? palette.TextPrimary : palette.TextSecondary;
            GUI.DrawTexture(r.ContractedBy(3f), icon, ScaleMode.ScaleToFit);
            GUI.color = previous;

            return Widgets.ButtonInvisible(r);
        }

        // ---------------------------------------------------------------------------------------
        // Row action art
        //
        // Resolved once and kept, misses included. Paths are given rather than discovered because these are
        // specific images; a miss falls back to the text glyph the buttons used before.
        // ---------------------------------------------------------------------------------------

        // Loaded in the static constructor rather than lazily on first draw. A static Texture2D field has to
        // be filled on the main thread, and a lazy load is only main-thread by luck of who reads it first;
        // the attribute on this class is what makes it a guarantee. It runs after content loading, so
        // ContentFinder is ready.
        private static readonly Texture2D arrowUp;
        private static readonly Texture2D arrowDown;

        static Dialog_ButtonBarEditor()
        {
            try
            {
                arrowUp = ContentFinder<Texture2D>.Get("UI/Interface/UI.ArrowUp", false);
                arrowDown = ContentFinder<Texture2D>.Get("UI/Interface/UI.ArrowDown", false);
            }
            catch (Exception ex)
            {
                UIGuard.Report("ButtonBar.LoadEditorArrows", ex,
                    "The bar editor's move-up and move-down buttons draw without their arrows.");
            }
        }

        /// <summary>
        /// Flat text field matching the theme, for renaming a button.
        ///
        /// The text is inset by <see cref="TextPad"/> on every side rather than horizontally alone, so a
        /// descender does not touch the border. The field itself is inset from the band by the same amount.
        /// </summary>
        /// <param name="invalid">Draws the border in the danger color, matching the row's stripe.</param>
        private static string ThemedTextField(Rect r, string text, UIColorPaletteDef palette,
            bool invalid = false)
        {
            Rect field = new Rect(r.x, r.y + TextPad, Mathf.Min(220f, r.width),
                Mathf.Max(0f, r.height - TextPad * 2f));

            Widgets.DrawBoxSolid(field, palette.SurfaceSunken);

            Color previous = GUI.color;
            GUI.color = invalid ? palette.Danger : palette.AccentMuted;
            Widgets.DrawBox(field, 1);
            GUI.color = previous;

            return Widgets.TextField(field.ContractedBy(TextPad + 1f, TextPad), text ?? "");
        }
    }

}
