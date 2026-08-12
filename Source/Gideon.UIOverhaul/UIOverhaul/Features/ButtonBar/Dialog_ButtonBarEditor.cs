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
        /// <summary>Identity band at the top of a row: icon, name and source mod.</summary>
        private const float HeaderBandHeight = 34f;

        /// <summary>The controls under the identity band.</summary>
        private const float SettingsBandHeight = 34f;

        private const float RowHeight = HeaderBandHeight + SettingsBandHeight;
        private const float ChildRowHeight = 28f;
        private const float Gap = 4f;
        private const float ButtonSize = 26f;
        private const float ColumnGap = 12f;
        private const float FooterHeight = 40f;
        private const float HeaderHeight = 56f;

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
            {
                UIButtonBarEntry clone = new UIButtonBarEntry
                {
                    tab = entry.tab,
                    menu = entry.menu,
                    icon = entry.icon,
                    label = entry.label,
                    mode = entry.mode
                };

                clone.children.AddRange(entry.children);
                copy.entries.Add(clone);
            }

            return copy;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            Widgets.DrawBoxSolid(inRect, palette.WindowBackground);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Rect header = new Rect(inRect.x + ColumnGap, inRect.y + 10f, inRect.width - ColumnGap * 2f,
                HeaderHeight);

            // Only the title strip drags the window.
            //
            // Window calls GUI.DragWindow() across its whole area when draggable is set, so a press meant for
            // a row started a window drag as well and the two fought each other. draggable is assigned from
            // the pointer position here, which runs before Window reaches that call, so by the time it asks,
            // the answer already reflects where the cursor actually is.
            draggable = new Rect(header.x, header.y, header.width, TitleBarHeight)
                .Contains(Event.current.mousePosition);

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
            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(r.x, r.y, r.width - 200f, 32f), "Edit Designator Tabs");

            Text.Font = GameFont.Small;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(r.x, r.y + 30f, r.width, 22f),
                assigningTo >= 0
                    ? "Pick a tab on the right to put inside the menu."
                    : "Drag a row to reorder it, onto a menu to put it inside, or onto the right to hide it.");
            GUI.color = palette.TextPrimary;
        }

        // ---------------------------------------------------------------------------------------
        // On the bar
        // ---------------------------------------------------------------------------------------

        private void DrawBarColumn(Rect r, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(r, palette.PanelBackground);
            Rect inner = r.ContractedBy(8f);

            float viewHeight = 0f;
            foreach (UIButtonBarEntry entry in working.entries)
                viewHeight += HeightOf(entry);

            Rect view = new Rect(0f, 0f, inner.width - 18f, viewHeight + Gap);

            Widgets.BeginScrollView(inner, ref barScroll, view);

            float y = 0f;
            for (int i = 0; i < working.entries.Count; i++)
            {
                UIButtonBarEntry entry = working.entries[i];
                DrawEntryRow(new Rect(0f, y, view.width, RowHeight), entry, i, palette);
                y += RowHeight + Gap;

                if (!entry.IsMenu)
                    continue;

                for (int c = 0; c < entry.children.Count; c++)
                {
                    Rect childRow = new Rect(18f, y, view.width - 18f, ChildRowHeight);
                    DrawChildRow(childRow, entry, c, palette);
                    y += ChildRowHeight + Gap;
                }

                Rect addRow = new Rect(18f, y, view.width - 18f, ChildRowHeight);
                if (SmallButton(addRow, "+ add a tab to this menu", palette))
                {
                    assigningTo = i;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                y += ChildRowHeight + Gap;
            }

            // After the rows, so the insertion line and the menu highlight draw over them rather than under.
            HandleDrag(view, palette);

            Widgets.EndScrollView();
        }

        // ---------------------------------------------------------------------------------------
        // Dragging
        //
        // No live reordering as the pointer moves. It reads well for a flat list, but a row here can also be
        // a drop target in its own right -- dropping a tab onto a menu puts it inside -- and a list that
        // rearranges under the cursor makes "which row am I over" ambiguous at the moment it matters most.
        // An insertion line plus a highlight on a menu keeps the two outcomes distinct.
        // ---------------------------------------------------------------------------------------

        /// <summary>Index in <c>working.entries</c> being dragged, or -1.</summary>
        private int dragIndex = -1;

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

        /// <summary>Which entry's block contains a view-space Y.</summary>
        private int IndexAt(float y)
        {
            float top = 0f;

            for (int i = 0; i < working.entries.Count; i++)
            {
                float height = HeightOf(working.entries[i]);
                if (y < top + height)
                    return i;

                top += height;
            }

            return Mathf.Max(0, working.entries.Count - 1);
        }

        private float TopOf(int index)
        {
            float top = 0f;
            for (int i = 0; i < index && i < working.entries.Count; i++)
                top += HeightOf(working.entries[i]);

            return top;
        }

        /// <summary>
        /// Whether dropping onto <paramref name="target"/> should nest rather than reorder.
        ///
        /// Only a plain tab goes into a menu. Menus do not nest -- the bar draws one level of them -- and a
        /// menu dropped into a menu would silently lose its children.
        /// </summary>
        private bool WouldNest(int target)
        {
            if (dragIndex < 0 || target == dragIndex || target < 0 || target >= working.entries.Count)
                return false;

            return working.entries[target].IsMenu
                   && !working.entries[dragIndex].IsMenu
                   && !working.entries[dragIndex].tab.NullOrEmpty();
        }

        /// <summary>Draws drop feedback and completes the drag. Called inside the scroll view.</summary>
        private void HandleDrag(Rect view, UIColorPaletteDef palette)
        {
            if (dragIndex < 0 || dragIndex >= working.entries.Count)
                return;

            Vector2 mouse = Event.current.mousePosition;

            if (!dragActive && (mouse - dragOrigin).sqrMagnitude > DragThresholdSquared)
                dragActive = true;

            int target = IndexAt(mouse.y);

            if (dragActive)
            {
                if (WouldNest(target))
                {
                    Rect row = new Rect(0f, TopOf(target), view.width, RowHeight);
                    Widgets.DrawBoxSolid(row, palette.SelectionOverlay);

                    Color previous = GUI.color;
                    GUI.color = palette.Accent;
                    Widgets.DrawBox(row, 2);
                    GUI.color = previous;
                }
                else
                {
                    Widgets.DrawBoxSolid(new Rect(0f, TopOf(target) - 1f, view.width, 2f), palette.Accent);
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

            dragIndex = -1;
            dragActive = false;
        }

        /// <summary>Dropped on the available column: off the bar, but recoverable from that list.</summary>
        private void HideDragged()
        {
            UIButtonBarEntry dragged = working.entries[dragIndex];

            // A menu has nothing to go back to over there -- that list is tabs. Dropping one does nothing
            // rather than quietly destroying it along with its children.
            if (dragged.IsMenu)
                return;

            if (!dragged.tab.NullOrEmpty())
                working.hidden.Add(dragged.tab);

            working.entries.RemoveAt(dragIndex);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>True while a tab is being dragged and the pointer is over the available column.</summary>
        private bool DroppingToHide =>
            dragActive && dragIndex >= 0 && dragIndex < working.entries.Count
            && !working.entries[dragIndex].IsMenu && availableColumn.Contains(windowMouse);

        private void Drop(int target)
        {
            UIButtonBarEntry dragged = working.entries[dragIndex];

            if (WouldNest(target))
            {
                working.entries[target].children.Add(dragged.tab);
                working.entries.RemoveAt(dragIndex);
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            if (target == dragIndex)
                return;

            working.entries.RemoveAt(dragIndex);

            // Computed against the list before the removal, so an index past the old position is one too high
            // once the gap closes.
            int insert = Mathf.Clamp(target > dragIndex ? target - 1 : target, 0, working.entries.Count);
            working.entries.Insert(insert, dragged);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private float HeightOf(UIButtonBarEntry entry)
        {
            float height = RowHeight + Gap;

            if (entry.IsMenu)
                height += (entry.children.Count + 1) * (ChildRowHeight + Gap);

            return height;
        }

        /// <summary>
        /// Chrome for the rows. One instance, reconfigured per row -- see UICardControl's own notes.
        ///
        /// DrawChrome rather than Draw throughout this dialog: these cards carry their own buttons, and Draw
        /// would consume the click before the buttons drawn on top of it could see it.
        /// </summary>
        private static readonly UICardControl RowCard = new UICardControl { Padding = 0f, AccentWidth = 3f };

        private void DrawEntryRow(Rect row, UIButtonBarEntry entry, int index, UIColorPaletteDef palette)
        {
            // A menu carries the full accent, a plain tab the muted one, so the two kinds of row are
            // distinguishable at a glance without reading their labels.
            RowCard.AccentColor = entry.IsMenu ? palette.Accent : palette.AccentMuted;
            RowCard.BackgroundColor = palette.SurfaceRaised;
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
                dragIndex = index;
                dragOrigin = Event.current.mousePosition;
                dragActive = false;
            }

            if (dragActive && dragIndex == index)
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);



            // Identity on top, controls beneath. Every control below is positioned from this band rather than
            // from the row, so the two stay in step if either height changes.
            Rect settings = new Rect(row.x, row.yMax - SettingsBandHeight, row.width, SettingsBandHeight);
            DrawIdentityBand(new Rect(row.x, row.y, row.width, HeaderBandHeight), entry, palette);

            float x = row.xMax - 4f;

            // Delete on a menu, hide on a tab -- the same removal underneath, but they are not the same act
            // and should not look alike. A menu is ours and ceases to exist; a tab belongs to a mod and only
            // goes back to the available list, where it can be brought back. Deleting a menu also returns its
            // children, since they are off that list only by virtue of being in here.
            x -= ButtonSize;
            bool removed = entry.IsMenu
                ? IconAction(new Rect(x, settings.y + 4f, ButtonSize, ButtonSize), BaseContent.BadTex, "X",
                    palette, "Delete this menu")
                : IconAction(new Rect(x, settings.y + 4f, ButtonSize, ButtonSize), "–", palette,
                    "Hide this tab, returning it to the list on the right");

            if (removed)
            {
                if (!entry.IsMenu && !entry.tab.NullOrEmpty())
                    working.hidden.Add(entry.tab);

                working.entries.RemoveAt(index);
                return;
            }

            x -= ButtonSize + 2f;
            if (IconAction(new Rect(x, settings.y + 4f, ButtonSize, ButtonSize), arrowDown, "v", palette,
                    "Move right")
                && index < working.entries.Count - 1)
            {
                working.entries[index] = working.entries[index + 1];
                working.entries[index + 1] = entry;
                return;
            }

            x -= ButtonSize + 2f;
            if (IconAction(new Rect(x, settings.y + 4f, ButtonSize, ButtonSize), arrowUp, "^", palette, "Move left")
                && index > 0)
            {
                working.entries[index] = working.entries[index - 1];
                working.entries[index - 1] = entry;
                return;
            }

            // Icon slot. Clicking it opens the picker, which is the route for a tab whose mod never gave
            // it an icon.
            x -= ButtonSize + 6f;
            DrawIconSlot(new Rect(x, settings.y + 4f, ButtonSize, ButtonSize), entry, palette);

            // Display mode, as a dropdown. Cycling through four states means clicking up to three times to
            // reach one and having to know the order; a list shows every choice and what each does.
            x -= 106f;
            Rect modeRect = new Rect(x, settings.y + 4f, 102f, ButtonSize);
            if (IconAction(modeRect, ModeLabel(entry.mode) + "  v", palette, ModeTooltip(entry.mode)))
                OpenModeMenu(entry);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextPrimary;

            Rect labelRect = new Rect(row.x + 6f + GripWidth + 6f, settings.y,
                x - row.x - 10f - GripWidth - 6f, settings.height);

            if (entry.IsMenu)
            {
                // A menu has no def to fall back on, so its label *is* the entry's own text.
                entry.menu = ThemedTextField(labelRect, entry.menu, palette);
            }
            else
            {
                MainButtonDef def = entry.Def;
                string original = def != null ? def.LabelCap.ToString() : entry.tab + " (missing)";

                // Shows the def's name until renamed, and clearing the field puts it back. Storing the
                // rename only when it differs keeps the config free of entries that just restate the def.
                string shown = entry.label.NullOrEmpty() ? original : entry.label;
                string edited = ThemedTextField(labelRect, shown, palette);

                if (edited != shown)
                    entry.label = edited.NullOrEmpty() || edited == original ? null : edited;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextPrimary;
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

            float x = band.x + 6f + GripWidth + 6f;

            Texture2D icon = UIBarDefaultIcons.Resolve(entry, entry.Def);
            if (icon != null)
            {
                GUI.color = palette.TextSecondary;
                GUI.DrawTexture(new Rect(x, band.y + 5f, 24f, 24f), icon, ScaleMode.ScaleToFit);
            }

            x += 24f + 8f;

            MainButtonDef def = entry.Def;

            string name = entry.IsMenu
                ? entry.menu.NullOrEmpty() ? "Menu" : entry.menu
                : def != null ? def.LabelCap.ToString() : entry.tab + " (missing)";

            // A menu has no mod behind it, so it says so rather than showing a blank or claiming ours.
            string source = entry.IsMenu
                ? "Menu you created"
                : def?.modContentPack?.Name ?? "Unknown source";

            float width = Mathf.Max(0f, band.xMax - x - 6f);

            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(x, band.y + 1f, width, 20f), name);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(x, band.y + 18f, width, 14f), source);

            Text.Font = previousFont;
            GUI.color = previousColor;
        }

        private void DrawChildRow(Rect row, UIButtonBarEntry parent, int childIndex,
            UIColorPaletteDef palette)
        {
            // Sunken and stripeless, so a tab inside a menu reads as nested under its parent rather than as a
            // sibling of the top-level rows.
            RowCard.AccentColor = null;
            RowCard.BackgroundColor = palette.SurfaceSunken;
            RowCard.DrawChrome(row, palette);

            string childName = parent.children[childIndex];
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail(childName);

            Rect removeRect = new Rect(row.xMax - ButtonSize - 4f, row.y + 1f, ButtonSize, ButtonSize);
            if (IconAction(removeRect, "X", palette, "Take out of this menu"))
            {
                parent.children.RemoveAt(childIndex);

                // Back onto the bar as a top-level slot rather than vanishing: taking a tab out of a menu
                // is not the same as removing it.
                working.entries.Add(new UIButtonBarEntry { tab = childName });
                return;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(row.x + 8f, row.y, removeRect.x - row.x - 12f, row.height),
                def != null ? def.LabelCap.ToString() : childName + " (missing)");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextPrimary;
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
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f),
                assigningTo >= 0 ? "Choose a tab for the menu" : "Not on the bar");
            GUI.color = palette.TextPrimary;

            Rect listRect = new Rect(inner.x, inner.y + 24f, inner.width, inner.height - 24f);

            List<MainButtonDef> candidates = Candidates();
            Rect view = new Rect(0f, 0f, listRect.width - 18f,
                candidates.Count * (RowHeight + Gap) + Gap);

            Widgets.BeginScrollView(listRect, ref availableScroll, view);

            float y = 0f;
            foreach (MainButtonDef def in candidates)
            {
                Rect row = new Rect(0f, y, view.width, RowHeight);
                Widgets.DrawBoxSolid(row, palette.SurfaceRaised);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;
                Widgets.Label(new Rect(row.x + 6f, row.y, row.width - ButtonSize - 14f, row.height),
                    def.LabelCap);
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

            Widgets.EndScrollView();
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
                                                   System.StringComparison.OrdinalIgnoreCase));

                if (!menu.children.Contains(def.defName))
                    menu.children.Add(def.defName);

                assigningTo = -1;
            }
            else
            {
                working.entries.Add(new UIButtonBarEntry { tab = def.defName });
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

                foreach (string child in entry.children)
                    placed.Add(child);
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
                working.entries.Add(new UIButtonBarEntry { menu = "Menu" });
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

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (assigningTo >= 0)
            {
                x += buttonWidth + 8f;
                if (SmallButton(new Rect(x, r.y, buttonWidth, 32f), "Cancel", palette))
                    assigningTo = -1;
            }

            if (SmallButton(new Rect(r.xMax - buttonWidth, r.y, buttonWidth, 32f), "Save", palette))
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

        private static bool SmallButton(Rect r, string label, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(r);
            UIElementPainter.PaintButton(r, palette, over, over && Input.GetMouseButton(0));

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            GUI.color = palette.TextPrimary;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;

            return Widgets.ButtonInvisible(r);
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
            arrowUp = ContentFinder<Texture2D>.Get("UI/Interface/UI.ArrowUp", false);
            arrowDown = ContentFinder<Texture2D>.Get("UI/Interface/UI.ArrowDown", false);
        }

        /// <summary>Flat text field matching the theme, for renaming a button.</summary>
        private static string ThemedTextField(Rect r, string text, UIColorPaletteDef palette)
        {
            Rect field = new Rect(r.x, r.y + 4f, Mathf.Min(220f, r.width), r.height - 8f);
            Widgets.DrawBoxSolid(field, palette.SurfaceSunken);

            Color previous = GUI.color;
            GUI.color = palette.AccentMuted;
            Widgets.DrawBox(field, 1);
            GUI.color = previous;

            return Widgets.TextField(field.ContractedBy(4f, 0f), text ?? "");
        }
    }

}
