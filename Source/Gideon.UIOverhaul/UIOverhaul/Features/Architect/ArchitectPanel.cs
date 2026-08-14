using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.Architect
{
    /// <summary>
    /// The architect tab, redrawn as a two-pane window in the theme.
    ///
    /// Left pane: the categories, one card each, with an icon and the category's name. The active one
    /// carries the palette's accent stripe. Right pane: that category's designators as an icon grid,
    /// with a filter above it and a readout below showing whichever designator the cursor is over --
    /// or, when it is over none, the one currently selected.
    ///
    /// Vanilla draws the categories inside its window and the designators outside it, in a grid across
    /// the bottom of the screen laid out by GizmoGridDrawer. That grid takes a start X and works out the
    /// rest from the screen, so it cannot be aimed at a rect; the icons here are drawn directly instead.
    /// What is not reimplemented is each designator's own rendering: Command.DrawIcon draws the icon, so
    /// a dropdown keeps its corner badge and a build designator keeps its stuff color, and ProcessInput
    /// handles activation, so selecting, dropdown menus and placement all behave as they always did.
    /// </summary>
    public static class ArchitectPanel
    {
        // ---------------------------------------------------------------------------------------
        // Sizing
        //
        // Derived from the screen rather than fixed, because this window is much larger than vanilla's
        // and a fixed 1040 would run off the edge of a small display.
        // ---------------------------------------------------------------------------------------

        public static float WindowWidth => Mathf.Min(1040f, UI.screenWidth - 16f);

        public static float WindowHeight => Mathf.Min(470f, UI.screenHeight * 0.55f);

        private const float CategoryPaneWidth = 190f;
        private const float CategoryRowHeight = 44f;
        private const float CategoryRowGap = 2f;
        private const float CategoryIconSize = 28f;

        // Sized for an icon with its name under it. Vanilla's grid is icon-only and relies on a tooltip to
        // say what anything is, which means hunting by memory; a name on the card is the whole point of the
        // extra width.
        private const float DesignatorCardWidth = 96f;
        private const float DesignatorCardHeight = 108f;
        private const float DesignatorCardGap = 4f;
        private const float DesignatorIconSize = 46f;
        private const float DesignatorLabelHeight = 30f;
        private const float HotKeyStripHeight = 12f;

        private const float FilterHeight = 28f;
        private const float ReadoutHeight = 112f;
        private const float ScrollBarWidth = 20f;
        private const float Pad = 8f;
        private const float PaneGap = 8f;

        // ---------------------------------------------------------------------------------------
        // Cards
        //
        // One instance each, reused for every row and every icon. A card is an object so that its
        // configuration survives between frames and only what changed has to be assigned.
        // ---------------------------------------------------------------------------------------

        private static readonly UICardControl CategoryCard = new UICardControl { Padding = 6f, AccentWidth = 3f };

        private static readonly UICardImage CategoryIcon =
            CategoryCard.Add(new UICardImage { Fit = Gideon.UIFramework.Components.Images.UIImageFit.Contain });

        private static readonly UICardLabel CategoryLabel =
            CategoryCard.Add(new UICardLabel { Anchor = TextAnchor.MiddleLeft, Font = GameFont.Small });

        private static readonly UICardControl DesignatorCard = new UICardControl { Padding = 6f };

        private static readonly UICardLabel DesignatorHotKey =
            DesignatorCard.Add(new UICardLabel { Anchor = TextAnchor.UpperLeft, Font = GameFont.Tiny });

        /// <summary>
        /// The name under the icon. Wrapped and Tiny, because build names run long -- "Sterile tile",
        /// "Hydroponics basin" -- and clipping them to one line loses exactly the word that distinguishes
        /// two similar entries.
        /// </summary>
        private static readonly UICardLabel DesignatorName =
            DesignatorCard.Add(new UICardLabel
            {
                Anchor = TextAnchor.UpperCenter,
                Font = GameFont.Tiny,
                WrapText = true
            });

        // ---------------------------------------------------------------------------------------
        // State
        // ---------------------------------------------------------------------------------------

        private static Vector2 categoryScroll;
        private static Vector2 designatorScroll;
        private static Vector2 optionScroll;
        private static string filter = "";

        /// <summary>
        /// The designator whose options are showing in the third pane, or null when that pane is closed.
        ///
        /// This is what replaces the float menu. Vanilla pops a menu over the map for a dropdown's variants
        /// or a stuffable building's materials, which covers the thing you are looking at and shows nothing
        /// but names; holding the choice here instead lets the options be cards with stats on them.
        /// </summary>
        private static Designator optionsFor;

        /// <summary>
        /// True when the grid is showing every category at once.
        ///
        /// Held as a flag rather than as a synthetic DesignationCategoryDef. A fake def would have to be
        /// registered in the database to be selectable, where every other mod and every save would then see
        /// a category that does not exist -- and vanilla's selectedDesPanel would have to point at a tab
        /// built from it. A flag stays entirely inside this window.
        /// </summary>
        private static bool allStuffOpen;

        private const string AllStuffLabel = "All stuff";

        private const float OptionsPaneWidth = 268f;
        // Sized to fit exactly the four stat rows a building or a floor shows, which is the most either can
        // produce. 78 with a 13px line was too tight on both counts: Tiny renders taller than 13px so rows
        // crowded each other, and the card only had room for three of the four.
        //
        // No headroom by design, but none is needed: the row sets are fixed in ArchitectStatBlock, so a fifth
        // row cannot appear without a code change here as well.
        private const float OptionCardHeight = 100f;
        private const float OptionIconSize = 34f;
        private const float OptionStatLineHeight = 16f;

        private static readonly UICardControl OptionCard = new UICardControl { Padding = 6f };

        private static readonly UICardLabel OptionName =
            OptionCard.Add(new UICardLabel { Anchor = TextAnchor.UpperLeft, Font = GameFont.Small });

        /// <summary>Reused so building the visible list does not allocate once per frame.</summary>
        private static readonly List<Designator> Shown = new List<Designator>();

        /// <summary>
        /// The window's category list. Private in vanilla, and worth reading anyway rather than building
        /// our own: these are the instances vanilla's own selectedDesPanel, tutorial hooks and search
        /// state refer to, so a second set would drift from them.
        /// </summary>
        private static readonly AccessTools.FieldRef<MainTabWindow_Architect, List<ArchitectCategoryTab>> PanelsOf =
            AccessTools.FieldRefAccess<MainTabWindow_Architect, List<ArchitectCategoryTab>>("desPanelsCached");

        /// <summary>Ideology's style-selector icon, so that button looks like it does everywhere else.</summary>
        private static readonly FieldInfo ChangeStyleIconField =
            AccessTools.Field(typeof(MainTabWindow_Architect), "ChangeStyleIcon");

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        public static void Draw(MainTabWindow_Architect window, Rect inRect)
        {
            List<ArchitectCategoryTab> panels = PanelsOf(window);
            if (panels == null || panels.Count == 0)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // The window's own background shows through as a frame between and around the panes, which is
            // what separates them now that nothing is drawing borders.
            Rect content = inRect.ContractedBy(6f);

            ArchitectCategoryTab open = EnsureSelection(window, panels);

            Rect left = new Rect(content.x, content.y, CategoryPaneWidth, content.height);
            Rect right = new Rect(left.xMax + PaneGap, content.y,
                content.width - CategoryPaneWidth - PaneGap, content.height);

            DrawCategoryPane(window, panels, open, left, palette);
            DrawDesignatorPane(open, right, palette);

            CloseIfChosen();
        }

        /// <summary>
        /// Set when something has been put on the cursor, so the tab closes at the end of the frame.
        ///
        /// Deferred rather than closed where the choice is made. The choice happens inside a scroll view, and
        /// taking the window off the stack from in there leaves this method drawing into a window that is no
        /// longer on it -- with a BeginScrollView still to be matched. Ending the frame first costs nothing and
        /// keeps the pairing intact.
        /// </summary>
        private static bool closeRequested;

        /// <summary>
        /// Closes the tab once something has been chosen to place.
        ///
        /// A change from vanilla, which leaves the architect tab open while you place. That suits vanilla's
        /// architect: a strip of small icons that does not cover much. This one is a full window of cards, and
        /// leaving it up means placing walls you cannot see.
        ///
        /// Only a completed choice closes it. Opening a category, or opening the option pane for something that
        /// has variants or materials, is not a choice yet -- the pane exists to be read before choosing.
        /// </summary>
        private static void CloseIfChosen()
        {
            if (!closeRequested)
                return;

            closeRequested = false;

            // playSound: false -- the designator's own activate sound already played, and vanilla's tab-close
            // click on top of it reads as two clicks for one action.
            Find.MainTabsRoot.EscapeCurrentTab(false);
        }

        /// <summary>
        /// The open category, choosing the first available one when nothing is open.
        ///
        /// Vanilla can sit with no category open, because its designator grid is a full-width overlay
        /// worth being able to dismiss. Here the categories and their contents share one window, so no
        /// selection just means half the window is blank.
        /// </summary>
        private static ArchitectCategoryTab EnsureSelection(MainTabWindow_Architect window,
            List<ArchitectCategoryTab> panels)
        {
            if (window.selectedDesPanel != null && panels.Contains(window.selectedDesPanel))
                return window.selectedDesPanel;

            foreach (ArchitectCategoryTab panel in panels)
            {
                if (!panel.Visible)
                    continue;

                window.selectedDesPanel = panel;
                return panel;
            }

            return null;
        }

        private static void DrawCategoryPane(MainTabWindow_Architect window, List<ArchitectCategoryTab> panels,
            ArchitectCategoryTab open, Rect pane, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(pane, palette.PanelBackground);

            Rect inner = pane.ContractedBy(Pad);
            Rect view = new Rect(0f, 0f, inner.width - ScrollBarWidth,
                (panels.Count + 1) * (CategoryRowHeight + CategoryRowGap));

            Widgets.BeginScrollView(inner, ref categoryScroll, view);

            ArchitectCategoryTab clicked = null;
            bool allClicked = false;
            float y = 0f;

            // First, above the categories: everything at once. It belongs at the top because it is not one
            // of them -- it is the way out of having to know which one a thing lives in.
            if (DrawAllStuffCard(new Rect(0f, y, view.width, CategoryRowHeight), palette))
                allClicked = true;

            y += CategoryRowHeight + CategoryRowGap;

            foreach (ArchitectCategoryTab panel in panels)
            {
                Rect row = new Rect(0f, y, view.width, CategoryRowHeight);
                y += CategoryRowHeight + CategoryRowGap;

                // Nothing in the category list reads as active while the combined view is open.
                if (DrawCategoryCard(row, panel, !allStuffOpen && panel == open, palette))
                    clicked = panel;
            }

            Widgets.EndScrollView();

            // Acted on outside the scroll view: selecting resets the designator scroll position, and a
            // rejected category shows a message, neither of which should happen mid-layout.
            if (allClicked)
                SelectAllStuff();
            else if (clicked != null)
                Select(window, clicked);
        }

        private static bool DrawCategoryCard(Rect row, ArchitectCategoryTab panel, bool active,
            UIColorPaletteDef palette)
        {
            bool available = panel.Visible;

            // Always a stripe, transparent on the rows that are not open. AccentColor widens the card's
            // content inset when it appears, so leaving it unset would shift every icon and label
            // sideways as the selection moved down the list.
            CategoryCard.AccentColor = active ? palette.Accent : Color.clear;
            CategoryCard.BackgroundColor = active ? palette.SurfaceRaised : palette.PanelBackground;
            CategoryCard.Selected = active;

            Rect content = CategoryCard.ContentRect(row);

            Texture icon = ArchitectCategoryIcons.For(panel.def);
            Rect slot = new Rect(0f, (content.height - CategoryIconSize) * 0.5f,
                CategoryIconSize, CategoryIconSize);

            CategoryIcon.Texture = icon;
            CategoryIcon.Bounds = slot;
            CategoryIcon.Tint = available ? (Color?) null : palette.TextDisabled;

            // The slot is reserved whether or not it is filled. Collapsing it for a category with no art
            // pulled that label flush left while every other row stayed indented, and it was the ragged
            // left edge -- not the missing icon -- that made the list look broken.
            float textX = CategoryIconSize + 8f;
            CategoryLabel.Text = panel.def.LabelCap;
            CategoryLabel.Bounds = new Rect(textX, 0f, Mathf.Max(0f, content.width - textX), content.height);
            CategoryLabel.Color = !available ? palette.TextDisabled
                : active ? palette.TextPrimary
                : palette.TextSecondary;

            bool clicked = CategoryCard.Draw(row, palette);

            // A quiet recess where art would have gone, so the reserved slot reads as part of a column
            // rather than as a hole. Deliberately not a glyph: the category's name sits right beside it,
            // so any placeholder symbol would only repeat what the label already says.
            if (icon == null)
            {
                Color sunken = palette.SurfaceSunken;
                Widgets.DrawBoxSolid(
                    new Rect(content.x + slot.x, content.y + slot.y, slot.width, slot.height),
                    new Color(sunken.r, sunken.g, sunken.b, 0.45f));
            }

            return clicked;
        }

        /// <summary>
        /// The combined view: every category's contents in one grid, with the filter searching all of it.
        ///
        /// The filter is deliberately not cleared here, unlike when picking a category. Typing a search and
        /// then widening it to everything is the normal way this gets used, and wiping the search at that
        /// moment would undo the thing the player was in the middle of.
        /// </summary>
        private static void SelectAllStuff()
        {
            if (allStuffOpen)
                return;

            allStuffOpen = true;
            optionsFor = null;
            designatorScroll = Vector2.zero;
            SoundDefOf.ArchitectCategorySelect.PlayOneShotOnCamera();
        }

        private static bool DrawAllStuffCard(Rect row, UIColorPaletteDef palette)
        {
            CategoryCard.AccentColor = allStuffOpen ? palette.Accent : Color.clear;
            CategoryCard.BackgroundColor = allStuffOpen ? palette.SurfaceRaised : palette.PanelBackground;
            CategoryCard.Selected = allStuffOpen;

            Rect content = CategoryCard.ContentRect(row);

            CategoryIcon.Texture = null;
            CategoryLabel.Text = AllStuffLabel;
            CategoryLabel.Bounds = new Rect(0f, 0f, content.width, content.height);
            CategoryLabel.Color = allStuffOpen ? palette.TextPrimary : palette.TextSecondary;

            bool clicked = CategoryCard.Draw(row, palette);
            CategoryCard.Tooltip = null;

            return clicked;
        }

        private static void Select(MainTabWindow_Architect window, ArchitectCategoryTab panel)
        {
            if (!panel.Visible)
            {
                Messages.Message("NothingAvailableInCategory".Translate() + ": " + panel.def.LabelCap,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (window.selectedDesPanel == panel)
                return;

            // No toggle-off, unlike vanilla's ClickedCategory. Closing the open category would leave the
            // right pane empty, and the next frame would reselect it anyway.
            window.selectedDesPanel = panel;
            allStuffOpen = false;
            filter = "";
            designatorScroll = Vector2.zero;

            // Its owner is about to leave the grid, so the option pane would be showing choices for something
            // no longer on screen.
            optionsFor = null;
            SoundDefOf.ArchitectCategorySelect.PlayOneShotOnCamera();
        }

        private static void DrawDesignatorPane(ArchitectCategoryTab open, Rect pane, UIColorPaletteDef palette)
        {
            // The combined view has no category behind it, so a null panel is only empty when it is also off.
            if (open == null && !allStuffOpen)
            {
                Widgets.DrawBoxSolid(pane, palette.PanelBackground);
                return;
            }

            // The option pane takes its width off the right before anything else is laid out, so the grid
            // reflows into what is left rather than being covered by it.
            if (optionsFor != null)
            {
                Rect options = new Rect(pane.xMax - OptionsPaneWidth, pane.y, OptionsPaneWidth, pane.height);
                pane = new Rect(pane.x, pane.y, pane.width - OptionsPaneWidth - PaneGap, pane.height);
                DrawOptionsPane(options, palette);
            }

            Widgets.DrawBoxSolid(pane, palette.PanelBackground);

            Rect inner = pane.ContractedBy(Pad);

            Rect filterRect = new Rect(inner.x, inner.y, Mathf.Min(300f, inner.width - 40f), FilterHeight);
            DrawFilterField(filterRect, palette);

            DrawStyleButton(new Rect(inner.xMax - FilterHeight, inner.y, FilterHeight, FilterHeight));

            Rect grid = new Rect(inner.x, filterRect.yMax + Pad, inner.width,
                inner.height - FilterHeight - Pad * 2f - ReadoutHeight);

            Designator hovered = DrawGrid(grid, open, palette);

            Rect readout = new Rect(inner.x, grid.yMax + Pad, inner.width, ReadoutHeight);
            DrawReadout(readout, hovered ?? Find.DesignatorManager.SelectedDesignator, palette);
        }

        /// <summary>Draws the icon grid and reports the designator under the cursor, if any.</summary>
        private static Designator DrawGrid(Rect grid, ArchitectCategoryTab open, UIColorPaletteDef palette)
        {
            Collect(open);

            int columns = Mathf.Max(1, Mathf.FloorToInt(
                (grid.width - ScrollBarWidth + DesignatorCardGap) / (DesignatorCardWidth + DesignatorCardGap)));
            int rows = Mathf.CeilToInt(Shown.Count / (float) columns);

            Rect view = new Rect(0f, 0f, grid.width - ScrollBarWidth,
                rows * (DesignatorCardHeight + DesignatorCardGap));

            Designator hovered = null;
            Designator activated = null;
            Designator rightClicked = null;

            Widgets.BeginScrollView(grid, ref designatorScroll, view);

            for (int i = 0; i < Shown.Count; i++)
            {
                Designator designator = Shown[i];

                Rect card = new Rect(
                    i % columns * (DesignatorCardWidth + DesignatorCardGap),
                    i / columns * (DesignatorCardHeight + DesignatorCardGap),
                    DesignatorCardWidth, DesignatorCardHeight);

                bool over = Mouse.IsOver(card);
                if (over)
                    hovered = designator;

                if (DrawDesignatorCard(card, designator, over, palette))
                    activated = designator;

                // ButtonInvisible, which the card uses, only reports the left button, so the right-click
                // menu vanilla gizmos offer has to be picked up separately.
                if (over && Event.current.type == EventType.MouseDown && Event.current.button == 1)
                {
                    rightClicked = designator;
                    Event.current.Use();
                }
            }

            Widgets.EndScrollView();

            // Everything that opens a window waits until the scroll view has ended. Inside it the GUI
            // matrix is offset, so a float menu built from Event.current.mousePosition would appear in
            // the scroll view's coordinate space rather than on screen.
            if (activated != null)
                Activate(activated);
            else if (rightClicked != null)
                ShowRightClickMenu(rightClicked);

            HandleHotKeys();

            return hovered;
        }

        private static bool DrawDesignatorCard(Rect card, Designator designator, bool over,
            UIColorPaletteDef palette)
        {
            bool disabled = designator.Disabled;
            bool selected = Find.DesignatorManager.SelectedDesignator == designator;

            DesignatorCard.BackgroundColor = palette.SurfaceRaised;
            DesignatorCard.Selected = selected;
            DesignatorCard.BorderColor = selected ? palette.Accent : (Color?) null;

            // Built only for the card under the cursor. It is the only one whose tip can be shown, and
            // composing a string for all of them every frame is pure garbage.
            DesignatorCard.Tooltip = over ? TooltipFor(designator) : null;

            Rect content = DesignatorCard.ContentRect(card);

            DesignatorHotKey.Text = designator.hotKey != null ? designator.hotKey.MainKeyLabel : null;
            DesignatorHotKey.Bounds = new Rect(0f, 0f, content.width, HotKeyStripHeight);
            DesignatorHotKey.Color = palette.TextSecondary;

            DesignatorName.Text = designator.LabelCap;
            DesignatorName.Bounds = new Rect(0f, content.height - DesignatorLabelHeight,
                content.width, DesignatorLabelHeight);
            DesignatorName.Color = disabled ? palette.TextDisabled
                : selected ? palette.TextPrimary
                : palette.TextSecondary;

            bool clicked = DesignatorCard.Draw(card, palette);

            // The icon is drawn by the designator itself rather than as a card image: DrawIcon is what
            // gives a dropdown its corner badge, a build designator its stuff color and a rotated
            // designator its angle, and reproducing that here would quietly lose all three.
            //
            // Centered in the band between the hotkey strip and the name rather than filling the content
            // rect, so a wide icon cannot grow into the label's space.
            Rect iconRect = new Rect(
                content.x + (content.width - DesignatorIconSize) * 0.5f,
                content.y + HotKeyStripHeight,
                DesignatorIconSize, DesignatorIconSize);
            designator.DrawIcon(iconRect, null, default(GizmoRenderParms));

            // A scrim rather than a tinted icon: DrawIcon assigns GUI.color itself, so anything set
            // beforehand is discarded.
            if (disabled)
            {
                Color background = palette.WindowBackground;
                Widgets.DrawBoxSolid(card, new Color(background.r, background.g, background.b, 0.6f));
            }

            return clicked;
        }

        private static string TooltipFor(Designator designator)
        {
            StringBuilder builder = new StringBuilder(designator.LabelCap);

            string description = designator.Desc;
            if (!description.NullOrEmpty())
                builder.Append("\n\n").Append(description);

            string postfix = designator.DescPostfix;
            if (!postfix.NullOrEmpty())
                builder.Append("\n\n").Append(postfix);

            if (designator.Disabled && !designator.disabledReason.NullOrEmpty())
                builder.Append("\n\n").Append(designator.disabledReason);

            return builder.ToString();
        }

        /// <summary>
        /// Selects a designator, or reports why it cannot be. ProcessInput rather than
        /// DesignatorManager.Select: a dropdown's input opens its menu instead of selecting it, and
        /// calling Select directly would put the dropdown itself on the cursor.
        /// </summary>
        private static void Activate(Designator designator)
        {
            if (designator.Disabled)
            {
                if (!designator.disabledReason.NullOrEmpty())
                    Messages.Message(designator.disabledReason, MessageTypeDefOf.RejectInput, false);

                return;
            }

            SoundDef sound = designator.CurActivateSound;
            if (sound != null)
                sound.PlayOneShotOnCamera();

            // Anything that would have opened a float menu opens the option pane instead. ProcessInput is
            // deliberately not called for those: it is the method that builds and shows the menu, so calling
            // it would put the menu on screen alongside our pane.
            if (HasOptions(designator))
            {
                optionsFor = optionsFor == designator ? null : designator;
                optionScroll = Vector2.zero;
                return;
            }

            optionsFor = null;
            designator.ProcessInput(Event.current);

            // Nothing left to choose, so this was the choice.
            closeRequested = true;
        }

        /// <summary>
        /// Whether this designator's click would have opened a float menu.
        ///
        /// Two unrelated sources, which is why this is not one check. A dropdown offers its grouped variants
        /// -- the stone floors, the bed sizes. A build designator for a stuffable thing offers materials, and
        /// that menu is built inside Designator_Build.ProcessInput rather than coming from a shared list.
        /// </summary>
        private static bool HasOptions(Designator designator)
        {
            if (designator is Designator_Dropdown dropdown)
                return dropdown.Elements != null && dropdown.Elements.Count > 1;

            return designator is Designator_Build build
                   && build.PlacingDef != null
                   && build.PlacingDef.MadeFromStuff;
        }

        private static void ShowRightClickMenu(Designator designator)
        {
            IEnumerable<FloatMenuOption> options = designator.RightClickFloatMenuOptions;
            if (options == null)
                return;

            List<FloatMenuOption> list = new List<FloatMenuOption>(options);
            if (list.Count > 0)
                Find.WindowStack.Add(new FloatMenu(list));
        }

        /// <summary>
        /// Designator hotkeys, which vanilla handles inside GizmoGridDrawer. Only the first designator
        /// claiming a key responds, matching vanilla's own one-gizmo-per-key rule.
        /// </summary>
        private static void HandleHotKeys()
        {
            if (Event.current.type != EventType.KeyDown)
                return;

            for (int i = 0; i < Shown.Count; i++)
            {
                Designator designator = Shown[i];
                KeyBindingDef key = designator.hotKey;

                if (key == null || !key.KeyDownEvent || ClaimedEarlier(key, i))
                    continue;

                Activate(designator);
                Event.current.Use();
                return;
            }
        }

        private static bool ClaimedEarlier(KeyBindingDef key, int index)
        {
            for (int i = 0; i < index; i++)
            {
                if (Shown[i].hotKey == key)
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------
        // Option pane
        // ---------------------------------------------------------------------------------------

        /// <summary>Reused, for the same reason as <see cref="Shown"/>.</summary>
        private static readonly List<Designator> OptionDesignators = new List<Designator>();

        private static readonly List<ThingDef> OptionStuffs = new List<ThingDef>();

        private static void DrawOptionsPane(Rect pane, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(pane, palette.PanelBackground);

            Designator owner = optionsFor;
            if (owner == null)
                return;

            Rect inner = pane.ContractedBy(Pad);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 24f, 24f), owner.LabelCap);

            GUI.color = previousColor;
            Text.Font = previousFont;

            if (Widgets.ButtonImage(new Rect(inner.xMax - 18f, inner.y + 4f, 16f, 16f), TexButton.CloseXSmall))
            {
                optionsFor = null;
                return;
            }

            Rect list = new Rect(inner.x, inner.y + 30f, inner.width, inner.height - 30f);

            if (owner is Designator_Dropdown dropdown)
                DrawVariantOptions(list, dropdown, palette);
            else if (owner is Designator_Build build)
                DrawStuffOptions(list, build, palette);
        }

        /// <summary>A dropdown's grouped variants, each its own designator.</summary>
        private static void DrawVariantOptions(Rect list, Designator_Dropdown dropdown, UIColorPaletteDef palette)
        {
            OptionDesignators.Clear();
            foreach (Designator element in dropdown.Elements)
            {
                if (element != null && element.Visible)
                    OptionDesignators.Add(element);
            }

            Rect view = new Rect(0f, 0f, list.width - ScrollBarWidth,
                OptionDesignators.Count * (OptionCardHeight + DesignatorCardGap));

            Designator chosen = null;
            Widgets.BeginScrollView(list, ref optionScroll, view);

            for (int i = 0; i < OptionDesignators.Count; i++)
            {
                Designator element = OptionDesignators[i];
                Rect card = new Rect(0f, i * (OptionCardHeight + DesignatorCardGap),
                    view.width, OptionCardHeight);

                Designator_Build asBuild = element as Designator_Build;

                if (DrawOptionCard(card, element, null, element.LabelCap,
                        asBuild?.PlacingDef, asBuild?.StuffDef,
                        Find.DesignatorManager.SelectedDesignator == element, palette))
                    chosen = element;
            }

            Widgets.EndScrollView();

            if (chosen == null)
                return;

            // SetActiveDesignator is what vanilla's own menu calls, so the dropdown remembers the choice and
            // its grid button shows the chosen variant's icon afterwards.
            dropdown.SetActiveDesignator(chosen, true);
            Find.DesignatorManager.Select(chosen);
            SoundDefOf.Click.PlayOneShotOnCamera();

            // A variant was picked, which completes the choice the option pane was opened to offer.
            closeRequested = true;
        }

        /// <summary>The materials a stuffable building can be made from.</summary>
        private static void DrawStuffOptions(Rect list, Designator_Build build, UIColorPaletteDef palette)
        {
            BuildableDef placing = build.PlacingDef;

            OptionStuffs.Clear();
            foreach (ThingDef candidate in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                // The same test vanilla's stuff menu applies, so the list matches what the game would offer.
                if (candidate.IsStuff && candidate.stuffProps != null && candidate.stuffProps.CanMake(placing))
                    OptionStuffs.Add(candidate);
            }

            Rect view = new Rect(0f, 0f, list.width - ScrollBarWidth,
                OptionStuffs.Count * (OptionCardHeight + DesignatorCardGap));

            ThingDef chosen = null;
            Widgets.BeginScrollView(list, ref optionScroll, view);

            for (int i = 0; i < OptionStuffs.Count; i++)
            {
                ThingDef stuff = OptionStuffs[i];
                Rect card = new Rect(0f, i * (OptionCardHeight + DesignatorCardGap),
                    view.width, OptionCardHeight);

                if (DrawOptionCard(card, null, stuff, stuff.LabelCap, placing, stuff,
                        build.StuffDef == stuff, palette))
                    chosen = stuff;
            }

            Widgets.EndScrollView();

            if (chosen == null)
                return;

            build.SetStuffDef(chosen);
            Find.DesignatorManager.Select(build);
            SoundDefOf.Click.PlayOneShotOnCamera();

            // A material was picked, which completes the choice the option pane was opened to offer.
            closeRequested = true;
        }

        /// <summary>
        /// One option: icon, name, and the stats that separate it from its siblings.
        ///
        /// The stats are what this pane exists for. A float menu could only ever list names, so choosing
        /// granite over sandstone meant knowing the difference already or leaving the menu to look it up.
        /// </summary>
        /// <param name="variant">A dropdown entry, drawn through its own DrawIcon. Null on a material card.</param>
        /// <param name="iconThing">
        /// A material, drawn through Widgets.ThingIcon. Null on a variant card.
        ///
        /// ThingIcon rather than the raw uiIcon texture: it applies the def's own uiIconColor and scale, which
        /// is what makes sandstone blocks sandstone-colored. Drawing the texture directly left every stone,
        /// wood and metal the same grey, so the list gave no visual clue which material was which.
        /// </param>
        private static bool DrawOptionCard(Rect card, Designator variant, ThingDef iconThing, string label,
            BuildableDef placing, ThingDef stuff, bool selected, UIColorPaletteDef palette)
        {
            OptionCard.BackgroundColor = palette.SurfaceRaised;
            OptionCard.Selected = selected;
            OptionCard.BorderColor = selected ? palette.Accent : (Color?) null;

            Rect content = OptionCard.ContentRect(card);
            float textX = OptionIconSize + 8f;

            OptionName.Text = label;
            OptionName.Bounds = new Rect(textX, 0f, Mathf.Max(0f, content.width - textX), 20f);
            OptionName.Color = selected ? palette.TextPrimary : palette.TextSecondary;

            bool clicked = OptionCard.Draw(card, palette);

            Rect iconRect = new Rect(content.x, content.y + 2f, OptionIconSize, OptionIconSize);

            if (iconThing != null)
                Widgets.ThingIcon(iconRect, iconThing);
            else if (variant != null)
                variant.DrawIcon(iconRect, null, default(GizmoRenderParms));

            DrawOptionStats(new Rect(content.x + textX, content.y + 22f,
                content.width - textX, content.height - 22f), placing, stuff, palette);

            return clicked;
        }

        private static void DrawOptionStats(Rect rect, BuildableDef placing, ThingDef stuff,
            UIColorPaletteDef palette)
        {
            if (placing == null)
                return;

            List<ArchitectStatRow> rows = ArchitectStatBlock.For(placing, stuff);
            if (rows.Count == 0)
                return;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;

            float y = rect.y;
            int shown = Mathf.Min(rows.Count, Mathf.FloorToInt(rect.height / OptionStatLineHeight));

            for (int i = 0; i < shown; i++)
            {
                Rect line = new Rect(rect.x, y, rect.width, OptionStatLineHeight);

                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextDisabled;
                Widgets.Label(line, rows[i].Label);

                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = palette.TextSecondary;
                Widgets.Label(line, rows[i].Value);

                y += OptionStatLineHeight;
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>Fills <see cref="Shown"/> with the designators the grid should draw.</summary>
        private static void Collect(ArchitectCategoryTab open)
        {
            Shown.Clear();

            if (allStuffOpen)
            {
                CollectEverything();
                return;
            }

            if (open == null)
                return;

            foreach (Designator designator in open.def.ResolvedAllowedDesignators)
            {
                if (designator == null || !designator.Visible || !Matches(designator))
                    continue;

                Shown.Add(designator);
            }
        }

        /// <summary>
        /// Every category's contents in one list.
        ///
        /// Deduplicated by instance. The same designator object can be reached through more than one category
        /// -- a mod placing its buildings in both its own tab and a vanilla one is the usual way -- and a
        /// combined view is exactly where those duplicates would show up side by side.
        ///
        /// Categories are walked in their own order rather than sorted, so a thing sits roughly where the
        /// player already expects to find it.
        /// </summary>
        private static void CollectEverything()
        {
            seenDesignators.Clear();

            foreach (DesignationCategoryDef category in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
            {
                if (!category.Visible)
                    continue;

                foreach (Designator designator in category.ResolvedAllowedDesignators)
                {
                    if (designator == null || !designator.Visible || !Matches(designator))
                        continue;

                    if (!seenDesignators.Add(designator))
                        continue;

                    Shown.Add(designator);
                }
            }
        }

        private static readonly HashSet<Designator> seenDesignators = new HashSet<Designator>();

        private static bool Matches(Designator designator)
        {
            if (filter.NullOrEmpty())
                return true;

            string label = designator.Label;
            return !label.NullOrEmpty()
                   && label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// A label filter over the grid.
        ///
        /// Vanilla's QuickSearchWidget is not reused. It is a private field, and its filter drives
        /// private state on every ArchitectCategoryTab -- highlight and lowlight predicates, the unique
        /// match that Enter selects -- all of which exists to serve the gizmo grid this window no longer
        /// draws. Filtering the list we build ourselves is the whole of what it was doing here.
        /// </summary>
        private static void DrawFilterField(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);

            Color previous = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);

            Rect field = new Rect(rect.x + 6f, rect.y, rect.width - 6f - 22f, rect.height);

            GUI.color = palette.TextPrimary;
            filter = Widgets.TextField(field, filter);
            GUI.color = previous;

            if (filter.NullOrEmpty())
            {
                TextAnchor anchor = Text.Anchor;
                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(field, "Search");
                Text.Anchor = anchor;
                GUI.color = previous;
                return;
            }

            Rect clear = new Rect(rect.xMax - 20f, rect.y + (rect.height - 16f) * 0.5f, 16f, 16f);
            if (Widgets.ButtonImage(clear, TexButton.CloseXSmall))
            {
                filter = "";
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Ideology's style selector, kept because vanilla's DoWindowContents drew it and nothing else
        /// offers it. Silently absent without the DLC, or in classic mode, exactly as in vanilla.
        /// </summary>
        private static void DrawStyleButton(Rect rect)
        {
            if (!ModsConfig.IdeologyActive || Find.IdeoManager.classicMode)
                return;

            Texture2D icon = (ChangeStyleIconField?.GetValue(null) as CachedTexture)?.Texture;
            if (icon == null)
                return;

            TooltipHandler.TipRegion(rect, (TipSignal) "ChangeStyle".Translate());

            if (!Widgets.ButtonImage(rect.ContractedBy(2f), icon))
                return;

            if (Find.WindowStack.IsOpen<Dialog_StyleSelection>())
                Find.WindowStack.TryRemove(typeof(Dialog_StyleSelection));
            else
                Find.WindowStack.Add(new Dialog_StyleSelection());
        }

        /// <summary>
        /// The detail strip under the grid: what vanilla put in its info box, moved inside the window
        /// where the two-pane layout has room for it.
        /// </summary>
        private static void DrawReadout(Rect rect, Designator designator, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);

            if (designator == null)
                return;

            Rect inner = rect.ContractedBy(Pad);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;
            Rect title = new Rect(inner.x, inner.y, inner.width, 24f);
            Widgets.Label(title, designator.LabelCap);

            float bodyHeight = inner.height - title.height;
            float split = inner.width * 0.6f;

            string description = designator.Desc;
            if (!description.NullOrEmpty())
            {
                GUI.color = palette.TextSecondary;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(inner.x, title.yMax, split - Pad, bodyHeight), description);
            }

            DrawPanelReadout(designator,
                new Rect(inner.x + split, title.yMax, inner.width - split, bodyHeight), palette);

            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// The designator's own readout -- a building's cost list and stats, a dropdown's contents.
        ///
        /// Inside a group because vanilla calls this from an immediate window whose rect is the info
        /// box, so every implementation positions from x = 0 and would otherwise draw at the left edge
        /// of the screen. Guarded because a modded designator throwing here should cost its readout, not
        /// the whole architect.
        /// </summary>
        private static void DrawPanelReadout(Designator designator, Rect rect, UIColorPaletteDef palette)
        {
            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            GUI.BeginGroup(rect);
            float curY = 0f;

            try
            {
                designator.DrawPanelReadout(ref curY, rect.width);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(UILogTag.Prefix + "Designator " + designator.GetType().Name
                              + " failed to draw its architect readout.\n" + ex,
                    0x17C0_10C0 ^ designator.GetType().GetHashCode());
            }
            finally
            {
                GUI.EndGroup();
            }
        }
    }
}
