using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.ThingFilters
{
    /// <summary>
    /// The thing filter panel, redrawn in the theme.
    ///
    /// This is the control behind every "what may go in here" list in the game: storage zones and shelves, bill
    /// ingredients, caravan and trade requests, outfit and drug policies. It is the densest list RimWorld puts in
    /// front of a player, and vanilla's version is a plain indented tree with a checkbox column, a pair of range
    /// sliders that scroll away with the list, and no indication of how much of a category is allowed beyond a
    /// tri-state box.
    ///
    /// <b>What is different here.</b>
    ///
    /// <i>The header stays put.</i> Search, the three whole-list actions and the hit points and quality ranges are
    /// pinned above the scroll view rather than scrolling with the tree. Those are the controls a player reaches for
    /// after scrolling somewhere, and scrolling back to find them is the panel's most common annoyance.
    ///
    /// <i>The whole row toggles.</i> Vanilla's label is decoration -- only the checkbox itself takes a click, which
    /// in a 24px row is a small target repeated hundreds of times. Here the row is the target, dragging still paints
    /// down a column, and the chevron is excluded so opening a category is never mistaken for allowing it.
    ///
    /// <i>Rows are virtualized.</i> Only the rows inside the scroll view are laid out and drawn. Vanilla walks the
    /// whole open tree and tests each row against the visible rect, which means a fully expanded tree costs the same
    /// whether one row is on screen or forty.
    ///
    /// <b>What is deliberately unchanged: every mutation.</b> Allowing and disallowing runs through
    /// <c>ThingFilter.SetAllow</c>, <c>SetAllowAll</c> and <c>SetDisallowAll</c> with the same arguments vanilla
    /// passes, and nothing here reaches inside a filter. That is not laziness; a category toggle has to propagate to
    /// every descendant def and to the special filters, and a subtly wrong copy of that would corrupt a bill or a
    /// storage setting silently, showing the right thing in this panel while the game acted on something else. The
    /// tri-state arithmetic in <see cref="ThingFilterView"/> is a faster way to reach vanilla's answer, and it is
    /// read-only.
    /// </summary>
    internal static class ThingFilterPanel
    {
        private const float Pad = 6f;
        private const float Gap = 4f;

        private const float SearchHeight = UITextBoxControl.DefaultHeight;
        private const float ToolbarHeight = 24f;

        private const float TemplateBarHeight = 24f;

        /// <summary>
        /// The shortest panel that still gets the template buttons.
        ///
        /// Below this the tree is short enough that four rows matter more than the buttons do, and the same
        /// templates are reachable from any full sized filter anyway. 260 is the height the hunting bill's item
        /// filter is drawn at, which is the case this threshold exists to leave alone.
        /// </summary>
        private const float TemplateBarMinPanelHeight = 300f;
        private const float ReadoutWidth = 80f;

        /// <summary>Vanilla's own range slider geometry, so a pinned slider is the size players know.</summary>
        private const float RangeHeight = 32f;

        private const float RangeGap = 5f;

        private const float RowHeight = 24f;
        private const float IndentStep = 12f;
        private const float ChevronSize = 16f;
        private const float IconSize = 20f;
        private const float BadgeGap = 6f;

        private const float SwitchWidth = UICheckboxControl.BoxWidth;
        private const float SwitchHeight = UICheckboxControl.BoxSize;

        /// <summary>The switch's lane, wide enough that a 40px switch is not against the panel edge.</summary>
        private const float SwitchColumn = SwitchWidth + 8f;

        private const float ScrollBarWidth = 16f;

        /// <summary>
        /// How much of a stuff's stack a "small volume" item takes, which is what the /10 tag on a row means.
        /// Vanilla writes the literal 10 in four places; this is the same number, named.
        /// </summary>
        private const int SmallVolumeFactor = 10;

        /// <summary>
        /// The ids vanilla gives its three range sliders.
        ///
        /// Reused rather than invented. <c>Widgets.FloatRange</c> keys which handle is being dragged on this id, so
        /// a panel that made up its own would drop the drag whenever anything else in the game drew a range with
        /// vanilla's -- and the quality slider's id is shared with the one in the bill dialog on purpose.
        /// </summary>
        private const int HitPointsSliderId = 1;

        private const int QualitySliderId = 876813230;
        private const int MentalBreakSliderId = 968573221;

        /// <summary>
        /// Per-window state, hung off the <c>UIState</c> the caller already owns.
        ///
        /// A weak table rather than a dictionary, so a closed window's search box and evaluation arrays are
        /// collected with it. A strong-keyed dictionary here would hold every filter window the player had ever
        /// opened alive for the session, and the arrays are one entry per def in the game.
        /// </summary>
        private sealed class PanelState
        {
            internal readonly UITextBoxControl Search = new UITextBoxControl
            {
                Placeholder = "Search",
                Icon = TexButton.Search,
                MaxLength = 30
            };

            internal readonly ThingFilterView View = new ThingFilterView();
        }

        private static readonly ConditionalWeakTable<ThingFilterUI.UIState, PanelState> states =
            new ConditionalWeakTable<ThingFilterUI.UIState, PanelState>();

        private static bool reportedMissingHelper;

        internal static void Draw(Rect rect, ThingFilterUI.UIState state, ThingFilter filter,
            ThingFilter parentFilter, int openMask, IEnumerable<ThingDef> forceHiddenDefs,
            IEnumerable<SpecialThingFilterDef> forceHiddenFilters, bool forceHideHitPointsConfig,
            bool forceHideQualityConfig, bool showMentalBreakChanceRange,
            List<ThingDef> suppressSmallVolumeTags, Map map)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            PanelState panel = states.GetValue(state, _ => new PanelState());

            if (ThingFilterView.HiddenSpecialsUnavailable && !reportedMissingHelper)
            {
                reportedMissingHelper = true;

                UIDebug.Warning("Could not reach Listing_TreeThingFilter.GetCachedHiddenSpecialFilters, so the "
                                + "filter panel cannot tell which special filters a category can never match. A "
                                + "few extra toggles such as \"allow rotten\" may appear where they have nothing "
                                + "to apply to. Harmless, but it means that method was renamed.");
            }

            // Vanilla's own resolution of what tree to show and which ranges the parent permits. A bill's
            // ingredient filter is rooted wherever its parent lands; a storage filter is rooted at everything.
            TreeNode_ThingCategory root = filter.RootNode;
            bool hitPointsConfigurable = true;
            bool qualitiesConfigurable = true;

            if (parentFilter != null)
            {
                root = parentFilter.DisplayRootCategory;
                hitPointsConfigurable = parentFilter.allowedHitPointsConfigurable;
                qualitiesConfigurable = parentFilter.allowedQualitiesConfigurable;
            }

            // Assigned only when it differs, and that is a performance rule rather than tidiness: the setter on
            // QuickSearchFilter clears a 5000-entry match cache, so writing the same text every frame would empty
            // the cache every frame and make every match a fresh string search.
            if (panel.Search.Text != state.quickSearch.filter.Text)
                state.quickSearch.filter.Text = panel.Search.Text;

            ThingFilterView view = panel.View;
            view.Refresh(root, filter, parentFilter, state.quickSearch.filter, forceHiddenDefs, forceHiddenFilters,
                openMask);

            state.quickSearch.noResultsMatched = state.quickSearch.filter.Active && view.MatchCount == 0;

            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Color previousColor = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;

            Rect inner = rect.ContractedBy(Pad);
            float y = inner.y;

            DrawSearchRow(new Rect(inner.x, y, inner.width, SearchHeight), panel, view, palette,
                state.quickSearch.filter);
            y += SearchHeight + Gap;

            DrawToolbar(new Rect(inner.x, y, inner.width, ToolbarHeight), filter, parentFilter, view,
                forceHiddenDefs, forceHiddenFilters);
            y += ToolbarHeight + Gap;

            bool showHitPoints = hitPointsConfigurable && !forceHideHitPointsConfig;
            bool showQuality = qualitiesConfigurable && !forceHideQualityConfig;

            if (showHitPoints || showQuality)
            {
                if (showHitPoints)
                {
                    FloatRange range = filter.AllowedHitPointsPercents;
                    Widgets.FloatRange(new Rect(inner.x, y, inner.width, RangeHeight), HitPointsSliderId,
                        ref range, 0f, 1f, "HitPoints", ToStringStyle.PercentZero, 0f, GameFont.Small, null, 0.01f);
                    filter.AllowedHitPointsPercents = range;

                    Text.Font = GameFont.Small;
                    y += RangeHeight + RangeGap;
                }

                if (showQuality)
                {
                    QualityRange range = filter.AllowedQualityLevels;
                    Widgets.QualityRange(new Rect(inner.x, y, inner.width, RangeHeight), QualitySliderId,
                        ref range);
                    filter.AllowedQualityLevels = range;

                    Text.Font = GameFont.Small;
                    y += RangeHeight + RangeGap;
                }

                GUI.color = palette.Border;
                Widgets.DrawLineHorizontal(inner.x, y, inner.width);
                GUI.color = previousColor;

                y += Gap;
            }

            // The template bar sits under the tree, and the tree gives up the room for it. Only where there is
            // room to give: this panel is drawn at everything from a full storage tab to a 260 pixel column inside
            // the hunting bill, and two buttons are not worth four rows of a short tree.
            float templates = rect.height >= TemplateBarMinPanelHeight ? TemplateBarHeight + Gap : 0f;

            DrawTree(new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y - templates)), state, view,
                filter, palette, forceHiddenDefs, showMentalBreakChanceRange, suppressSmallVolumeTags, map);

            if (templates > 0f)
            {
                DrawTemplateBar(new Rect(inner.x, inner.yMax - TemplateBarHeight, inner.width, TemplateBarHeight),
                    filter, parentFilter, palette);
            }
        }

        /// <summary>
        /// Saving the filter as a template and loading one back.
        ///
        /// <b>Asked for on 2026-08-22 for the storage tab,</b> where a colony's third shelf wants the filter its
        /// first two already have and there was no way to say so. It lands in every filter in the game because
        /// this panel replaces every filter in the game, which is a bonus rather than the aim: a bill's ingredient
        /// filter is worth templating for the same reason.
        ///
        /// <b>Two buttons rather than one.</b> They open the same window, since saving and loading are one list
        /// seen from two sides, but a player looking to save should not have to learn that. Which one was pressed
        /// only decides whether the name box arrives filled in.
        /// </summary>
        private static void DrawTemplateBar(Rect rect, ThingFilter filter, ThingFilter parentFilter,
            UIColorPaletteDef palette)
        {
            float half = (rect.width - Gap) * 0.5f;
            string origin = Origin(filter);

            if (Button(new Rect(rect.x, rect.y, half, rect.height), "Save as template", palette))
                Dialog_FilterTemplates.Open(filter, parentFilter, origin, null, true);

            if (Button(new Rect(rect.x + half + Gap, rect.y, half, rect.height), "Load template", palette))
                Dialog_FilterTemplates.Open(filter, parentFilter, origin, null, false);
        }

        /// <summary>
        /// What to call the filter being saved, which becomes the template's suggested name.
        ///
        /// A filter does not know what owns it, so this is the best honest answer: its own summary when it has one,
        /// which is what RimWorld shows for a stockpile, and "Filter" when it does not. Nothing is inferred from
        /// the caller, because the same panel is drawn for a dozen different owners.
        /// </summary>
        private static string Origin(ThingFilter filter)
        {
            return UIGuard.Try("Filters.TemplateOrigin", () =>
            {
                string summary = filter?.customSummary;

                return summary.NullOrEmpty() ? "Filter" : summary;
            }, "Filter", null);
        }

        private static bool Button(Rect rect, string label, UIColorPaletteDef palette)
        {
            return UIActionButtonControl.Draw(rect, label, palette, false, true,
                GameFont.Tiny);
        }

        /// <summary>
        /// The search box, with a count of what it found.
        ///
        /// <see cref="UITextBoxControl"/> rather than the <c>QuickSearchWidget</c> the caller handed us, because
        /// that one loses keyboard focus whenever Unity's control ids shift under it -- and this panel is one long
        /// list of controls that appear and disappear as it scrolls, which is precisely what shifts them. The text
        /// is still pushed into the caller's search filter, so the matching, its cache and anything else reading
        /// the state are unaffected.
        /// </summary>
        private static void DrawSearchRow(Rect rect, PanelState panel, ThingFilterView view,
            UIColorPaletteDef palette, QuickSearchFilter search)
        {
            bool active = search.Active;
            Rect box = rect;

            if (active)
                box.width -= ReadoutWidth + Gap;

            panel.Search.Draw(box, palette);

            if (!active)
                return;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = view.MatchCount == 0 ? palette.Danger : palette.TextSecondary;

            Widgets.Label(new Rect(box.xMax + Gap, rect.y, ReadoutWidth, rect.height),
                view.MatchCount + " / " + view.ShownCount);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// Allow all, clear all, invert.
        ///
        /// The first two are vanilla's, calling vanilla's methods with vanilla's arguments. Invert is ours, and it
        /// is here because the two vanilla buttons only ever go to an extreme: the common job of "everything except
        /// these few" means clicking clear, then hunting for the few. Inverting is that job in one click.
        ///
        /// It flips things only, never the special filters. Those are constraints on what is allowed rather than
        /// entries in the list -- inverting "allow rotten" alongside a few hundred foodstuffs is not a thing anyone
        /// means by inverting a selection.
        /// </summary>
        private static void DrawToolbar(Rect rect, ThingFilter filter, ThingFilter parentFilter,
            ThingFilterView view, IEnumerable<ThingDef> forceHiddenDefs,
            IEnumerable<SpecialThingFilterDef> forceHiddenFilters)
        {
            float width = (rect.width - Gap * 2f) / 3f;

            if (Widgets.ButtonText(new Rect(rect.x, rect.y, width, rect.height), "AllowAll".Translate()))
            {
                filter.SetAllowAll(parentFilter);
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            if (Widgets.ButtonText(new Rect(rect.x + width + Gap, rect.y, width, rect.height),
                    "ClearAll".Translate()))
            {
                filter.SetDisallowAll(forceHiddenDefs, forceHiddenFilters);
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            Rect invert = new Rect(rect.x + (width + Gap) * 2f, rect.y, width, rect.height);

            TooltipHandler.TipRegion(invert, (TipSignal) ("Allow everything currently disallowed, and disallow "
                                                         + "everything currently allowed. Items only; the filters "
                                                         + "above and the range constraints are left alone."));

            if (!Widgets.ButtonText(invert, "Invert"))
                return;

            // Collected before mutating. SetAllow rebuilds the filter's display root, and the sequence being
            // walked is derived from the evaluation of the tree that root produced.
            List<ThingDef> toggle = new List<ThingDef>(view.VisibleThings());

            foreach (ThingDef thing in toggle)
                filter.SetAllow(thing, !filter.Allows(thing));

            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
        }

        /// <summary>
        /// The scrolling tree.
        ///
        /// <b>Only the rows on screen are drawn,</b> which is what the flattened array buys at the drawing end as
        /// well as the evaluation end: a fixed row height turns "which rows are visible" into two divisions rather
        /// than a walk. Vanilla lays out every row of the open tree and asks each one whether it overlaps the
        /// visible rect, so a tree expanded to a few thousand rows pays for all of them on every event pass.
        ///
        /// The mental break range is the one control inside the scroll view rather than pinned above it. It applies
        /// to a narrow slice of filters -- Anomaly's, and only when the caller asks for it -- and a constraint that
        /// is absent from almost every filter window does not earn permanent space at the top of all of them.
        /// </summary>
        private static void DrawTree(Rect rect, ThingFilterUI.UIState state, ThingFilterView view,
            ThingFilter filter, UIColorPaletteDef palette, IEnumerable<ThingDef> forceHiddenDefs,
            bool showMentalBreakChanceRange, List<ThingDef> suppressSmallVolumeTags, Map map)
        {
            bool mentalBreak = ModsConfig.AnomalyActive && showMentalBreakChanceRange;
            float header = mentalBreak ? RangeHeight + RangeGap : 0f;

            float viewWidth = rect.width - ScrollBarWidth;
            Rect viewRect = new Rect(0f, 0f, viewWidth, header + view.Count * RowHeight);

            Widgets.BeginScrollView(rect, ref state.scrollPosition, viewRect);

            try
            {
                if (mentalBreak)
                {
                    FloatRange range = filter.AllowedMentalBreakChance;
                    Widgets.FloatRange(new Rect(0f, 0f, viewWidth, RangeHeight), MentalBreakSliderId, ref range,
                        0f, 1f, "MaxMentalBreakChance", ToStringStyle.PercentZero);
                    filter.AllowedMentalBreakChance = range;

                    Text.Font = GameFont.Small;
                }

                // Clamped rather than trusted: scrollPosition belongs to the caller and survives the tree
                // changing size underneath it, so it can point past the end after a search narrows the list.
                float top = Mathf.Max(0f, state.scrollPosition.y - header);
                int first = Mathf.Clamp(Mathf.FloorToInt(top / RowHeight), 0, Mathf.Max(0, view.Count - 1));
                int last = Mathf.Min(view.Count - 1,
                    first + Mathf.CeilToInt(rect.height / RowHeight) + 1);

                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                bool previousWrap = Text.WordWrap;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;

                try
                {
                    for (int slot = first; slot <= last && slot < view.Count; slot++)
                        DrawRow(new Rect(0f, header + slot * RowHeight, viewWidth, RowHeight), slot, view, filter,
                            palette, forceHiddenDefs, suppressSmallVolumeTags, map);
                }
                finally
                {
                    Text.WordWrap = previousWrap;
                    Text.Anchor = previousAnchor;
                    Text.Font = previousFont;
                }
            }
            finally
            {
                // In a finally because an unbalanced scroll view is not confined to this panel: the clip group
                // stays on Unity's stack and disturbs whatever draws next, anywhere on screen.
                Widgets.EndScrollView();
            }
        }

        private static void DrawRow(Rect rect, int slot, ThingFilterView view, ThingFilter filter,
            UIColorPaletteDef palette, IEnumerable<ThingDef> forceHiddenDefs,
            List<ThingDef> suppressSmallVolumeTags, Map map)
        {
            ThingFilterRow row = view.RowAt(slot);
            bool category = row.Kind == ThingFilterRowKind.Category;
            bool matches = view.MatchesAt(slot);

            Color previousColor = GUI.color;

            // Categories sit on the raised surface so the tree has a visible skeleton at a glance, rather than
            // relying on indentation alone to say where one group ends and the next begins.
            if (category)
                Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);

            if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            float indent = row.Depth * IndentStep;

            DrawIndentGuides(rect, row.Depth, palette);

            Rect switchRect = new Rect(rect.xMax - SwitchColumn + (SwitchColumn - SwitchWidth) * 0.5f,
                rect.y + (rect.height - SwitchHeight) * 0.5f, SwitchWidth, SwitchHeight);

            Rect hit = rect;
            hit.xMin += indent;
            float labelX = rect.x + indent;

            if (category && view.OpenableAt(slot))
            {
                Rect chevron = new Rect(rect.x + indent, rect.y + (rect.height - ChevronSize) * 0.5f,
                    ChevronSize, ChevronSize);

                bool open = view.IsOpenAt(slot);

                // Vanilla's own arrows, tinted. Drawing a triangle procedurally was the alternative; these are
                // already the shape the player reads as "expand" everywhere else in the game.
                if (Widgets.ButtonImage(chevron, open ? TexButton.Collapse : TexButton.Reveal,
                        palette.TextSecondary, palette.TextPrimary))
                {
                    bool nowOpen;

                    if (view.Toggle(slot, out nowOpen))
                        (nowOpen ? SoundDefOf.TabOpen : SoundDefOf.TabClose).PlayOneShotOnCamera();
                }

                // Excluded from the toggle's hit area, so opening a category is never also allowing it.
                hit.xMin = chevron.xMax;
                labelX = chevron.xMax + Gap;
            }
            else if (row.Kind == ThingFilterRowKind.Thing)
            {
                // Indented one further than its category's other rows, as vanilla does, so the icons form a
                // column of their own instead of sitting under the chevrons.
                labelX = rect.x + indent + IndentStep;

                if (row.Thing.uiIcon != null && row.Thing.uiIcon != BaseContent.BadTex)
                {
                    Rect icon = new Rect(labelX, rect.y + (rect.height - IconSize) * 0.5f, IconSize, IconSize);

                    Widgets.DefIcon(icon, row.Thing, null, 1f, null, true,
                        matches ? (Color?) null : palette.TextDisabled);

                    labelX = icon.xMax + Gap;
                }
            }
            else if (row.Kind == ThingFilterRowKind.Undiscovered)
            {
                labelX = rect.x + indent + IndentStep;
            }

            hit.xMax = switchRect.xMax;

            float labelMax = rect.xMax - SwitchColumn - BadgeGap;

            labelMax = DrawBadges(rect, row, labelMax, palette, suppressSmallVolumeTags, map);

            GUI.color = LabelColor(row, matches, palette);
            Widgets.LabelEllipses(new Rect(labelX, rect.y, Mathf.Max(0f, labelMax - labelX), rect.height),
                LabelOf(row));
            GUI.color = previousColor;

            string tip = TooltipOf(row, suppressSmallVolumeTags);

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(hit, (TipSignal) tip);

            Toggle(hit, switchRect, slot, row, view, filter, palette, forceHiddenDefs);
        }

        /// <summary>
        /// The toggle, and the only place this panel changes anything.
        ///
        /// <b>One mechanism for all four row kinds,</b> which is what keeps a drag started on a category painting
        /// through the things below it. <c>ToggleInvisibleDraggable</c> owns the press, the drag, the paint state
        /// carried between rows and the sounds; the switch is painted after it from the state on the way in, so a
        /// click shows its result on the following frame exactly as vanilla's checkboxes do.
        ///
        /// <b>A category's tri-state maps onto that bool without losing anything.</b> Vanilla's
        /// <c>CheckboxMulti</c> goes Off to On and both On and Partial to Off, which is the same table as flipping
        /// <c>state != Off</c>: Partial reads as on, so flipping it clears the category. The switch is still
        /// painted from the real tri-state, so Partial keeps its knob mid-travel.
        /// </summary>
        private static void Toggle(Rect hit, Rect switchRect, int slot, ThingFilterRow row, ThingFilterView view,
            ThingFilter filter, UIColorPaletteDef palette, IEnumerable<ThingDef> forceHiddenDefs)
        {
            switch (row.Kind)
            {
                case ThingFilterRowKind.Category:
                {
                    MultiCheckboxState state = view.StateAt(slot);
                    bool on = state != MultiCheckboxState.Off;
                    bool before = on;

                    Widgets.ToggleInvisibleDraggable(hit, ref on, true, true);

                    if (on != before)
                        filter.SetAllow(row.Node.catDef, on, forceHiddenDefs, view.HiddenSpecials);

                    UIElementPainter.PaintCheckbox(switchRect, state, palette, false);
                    break;
                }

                case ThingFilterRowKind.Thing:
                {
                    bool on = filter.Allows(row.Thing);
                    bool before = on;

                    Widgets.ToggleInvisibleDraggable(hit, ref on, true, true);

                    if (on != before)
                        filter.SetAllow(row.Thing, on);

                    Paint(switchRect, before, palette);
                    break;
                }

                case ThingFilterRowKind.Special:
                {
                    bool on = filter.Allows(row.Special);
                    bool before = on;

                    Widgets.ToggleInvisibleDraggable(hit, ref on, true, true);

                    if (on != before)
                        filter.SetAllow(row.Special, on);

                    Paint(switchRect, before, palette);
                    break;
                }

                case ThingFilterRowKind.Undiscovered:
                {
                    ThingDef first = view.FirstUndiscoveredAt(slot);

                    if (first == null)
                        return;

                    bool on = filter.Allows(first);
                    bool before = on;

                    Widgets.ToggleInvisibleDraggable(hit, ref on, true, true);

                    // One row standing for several defs, so the click applies to all of them rather than to the
                    // one whose state the switch happens to be showing.
                    if (on != before)
                        foreach (ThingDef thing in view.UndiscoveredDefsAt(slot))
                            filter.SetAllow(thing, on);

                    Paint(switchRect, before, palette);
                    break;
                }
            }
        }

        private static void Paint(Rect switchRect, bool on, UIColorPaletteDef palette)
        {
            UIElementPainter.PaintCheckbox(switchRect,
                on ? MultiCheckboxState.On : MultiCheckboxState.Off, palette, false);
        }

        /// <summary>
        /// Faint verticals showing which category a nested row belongs to.
        ///
        /// The tree is deep enough that 12px of indentation alone stops answering "whose child is this" once a row
        /// has scrolled away from its parent.
        /// </summary>
        private static void DrawIndentGuides(Rect rect, int depth, UIColorPaletteDef palette)
        {
            if (depth <= 0)
                return;

            Color previous = GUI.color;
            Color guide = palette.Border;
            guide.a *= 0.35f;
            GUI.color = guide;

            for (int level = 0; level < depth; level++)
                Widgets.DrawLineVertical(rect.x + level * IndentStep + ChevronSize * 0.5f, rect.y, rect.height);

            GUI.color = previous;
        }

        /// <summary>
        /// The right-aligned annotations: how many are on the map, and the small-volume marker.
        ///
        /// Returns where the label has to stop, since both of these eat into it from the right.
        /// </summary>
        private static float DrawBadges(Rect rect, ThingFilterRow row, float labelMax, UIColorPaletteDef palette,
            List<ThingDef> suppressSmallVolumeTags, Map map)
        {
            if (row.Kind != ThingFilterRowKind.Thing)
                return labelMax;

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleRight;

            if (IsSmallVolume(row.Thing, suppressSmallVolumeTags))
            {
                string tag = "/" + SmallVolumeFactor.ToStringCached();

                GUI.color = palette.TextDisabled;
                Widgets.Label(new Rect(labelMax - 24f, rect.y, 24f, rect.height), tag);
                GUI.color = previousColor;

                labelMax -= 24f + BadgeGap;
            }

            if (map != null)
            {
                int count = map.resourceCounter.GetCount(row.Thing);

                if (count > 0)
                {
                    string text = count.ToStringCached();
                    float width = Text.CalcSize(text).x + BadgeGap;

                    GUI.color = palette.Info;
                    Widgets.Label(new Rect(labelMax - width, rect.y, width, rect.height), text);
                    GUI.color = previousColor;

                    labelMax -= width + BadgeGap;
                }
            }

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;

            return labelMax;
        }

        /// <summary>
        /// Whether a row earns the "/10" marker: stuff that stacks ten to a unit, unless the caller suppressed it.
        /// </summary>
        private static bool IsSmallVolume(ThingDef thing, List<ThingDef> suppressSmallVolumeTags)
        {
            if (!thing.IsStuff || !thing.smallVolume)
                return false;

            return suppressSmallVolumeTags == null || !suppressSmallVolumeTags.Contains(thing);
        }

        private static Color LabelColor(ThingFilterRow row, bool matches, UIColorPaletteDef palette)
        {
            if (!matches)
                return palette.TextDisabled;

            return row.Kind == ThingFilterRowKind.Category ? palette.TextPrimary : palette.TextSecondary;
        }

        private static string LabelOf(ThingFilterRow row)
        {
            switch (row.Kind)
            {
                case ThingFilterRowKind.Category:
                    return row.Node.LabelCap;

                case ThingFilterRowKind.Thing:
                    return row.Thing.LabelCap;

                // The asterisk is vanilla's mark for a special filter, kept because it is the only thing
                // distinguishing "allow rotten" from an item named the same way.
                case ThingFilterRowKind.Special:
                    return "*" + row.Special.LabelCap;

                case ThingFilterRowKind.Undiscovered:
                    return "UndiscoveredItemLabel".Translate();

                default:
                    return string.Empty;
            }
        }

        private static string TooltipOf(ThingFilterRow row, List<ThingDef> suppressSmallVolumeTags)
        {
            switch (row.Kind)
            {
                case ThingFilterRowKind.Category:
                    return row.Node.catDef.description;

                case ThingFilterRowKind.Thing:
                {
                    string text = row.Thing.DescriptionDetailed;

                    if (IsSmallVolume(row.Thing, suppressSmallVolumeTags))
                        text += "\n\n" + "ThisIsSmallVolume".Translate(SmallVolumeFactor.ToStringCached());

                    return text;
                }

                case ThingFilterRowKind.Special:
                    return row.Special.description;

                case ThingFilterRowKind.Undiscovered:
                    return "UndiscoveredItemDesc".Translate().Resolve();

                default:
                    return null;
            }
        }
    }
}
