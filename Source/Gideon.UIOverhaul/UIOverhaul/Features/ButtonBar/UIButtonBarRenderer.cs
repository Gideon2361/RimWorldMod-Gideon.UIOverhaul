using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Patches.UIElements;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>
    /// Draws one slot on the bar and reports what it is, so the renderer and the menu popup lay buttons
    /// out the same way without sharing a code path they would both have to be careful about.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class UIButtonBarRenderer
    {
        /// <summary>Icon-only slot width. Square, matching the bar's height.</summary>
        public const float MinimizedWidth = 40f;

        private const float IconPad = 4f;
        private const float ProgressHeight = 3f;

        /// <summary>Air between a label and the unread badge to its right, so the two do not read as one word.</summary>
        private const float BadgeGap = 5f;

        /// <summary>Path to the icon on our own bar button.</summary>
        private const string OptionsIconPath = "UIOverhaul/UI/OptionsUIOverhaul";

        /// <summary>
        /// The icon for our own bar button.
        ///
        /// Loaded in a static constructor under StaticConstructorOnStartup, not lazily on first draw.
        /// RimWorld scans for static fields of asset types and warns about any type holding one without
        /// the attribute, because Unity only permits asset loading on the main thread and the attribute
        /// is what guarantees the field is filled there. Lazy loading from a draw call happens to satisfy
        /// that too, but the game cannot know it, so the warning was correct to fire.
        /// </summary>
        public static readonly Texture2D OptionsIcon;

        static UIButtonBarRenderer()
        {
            try
            {
                OptionsIcon = ContentFinder<Texture2D>.Get(OptionsIconPath, false);

                if (OptionsIcon == null)
                {
                    // Ours to ship, so a miss is a packaging fault rather than anything the player did. The
                    // button still works; it just draws empty.
                    Log.Error(UILogTag.Prefix + $"Missing '{OptionsIconPath}'. The bar's UI options button "
                              + "will have no icon.");
                }
            }
            catch (Exception ex)
            {
                UIGuard.Report("ButtonBar.LoadOptionsIcon", ex,
                    "The bar's UI options button draws without its icon. It still opens the settings.");
            }
        }

        /// <summary>
        /// Draws a bar button and returns true when it was clicked.
        /// </summary>
        /// <param name="label">Text to show. Null or empty draws an icon-only button.</param>
        /// <param name="selected">Whether the tab this represents is the open one.</param>
        /// <param name="disabled">Drawn dimmed and reports no clicks.</param>
        /// <param name="barPercent">
        /// 0 to 1. Drawn as a thin fill along the bottom edge -- the research tab uses it to show
        /// progress, and dropping it would lose information the vanilla bar carried.
        /// </param>
        /// <summary>Thickness of the accent rule along the top edge of each button.</summary>
        public const float AccentRuleHeight = 3f;

        /// <summary>Gap left between adjacent buttons, which the accent rule stops short of.</summary>
        public const float ButtonGap = 3f;

        /// <summary>
        /// The rule along the button's top edge, carrying its state.
        ///
        /// Same device as the accent stripe on the grow-zone and architect cards, turned horizontal: one
        /// colored edge saying what this thing is, rather than a border saying where it ends.
        ///
        /// AccentMuted at rest, Accent under the cursor, Success on the open tab. Muted is the right idle
        /// state because it is the same hue as the hover color rather than a different one, so pointing at a
        /// button brightens its rule instead of changing what the rule means.
        /// </summary>
        private static void DrawAccentRule(Rect rect, bool selected, bool over, bool disabled,
            UIColorPaletteDef palette)
        {
            Color color = disabled ? palette.TextDisabled
                : selected ? palette.Success
                : over ? palette.Accent
                : palette.AccentMuted;

            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, AccentRuleHeight), color);
        }

        public static bool Draw(Rect rect, string label, Texture2D icon, bool selected, bool disabled,
            float barPercent, UIColorPaletteDef palette, string badge = null)
        {
            bool over = !disabled && Mouse.IsOver(rect);
            bool held = over && Input.GetMouseButton(0);

            // Borderless: these buttons sit in a continuous strip, where each outline would double against
            // its neighbor's. The gap between them and the accent rule below do the separating instead.
            //
            // <b>And square. Nothing in this renderer rounds, deliberately.</b> A tab is not a floating control:
            // it abuts its neighbours and carries an accent rule along its top edge. Rounding pulled the corners
            // away from that rule and left a lit arc hanging over the tab, which is what it looked like in
            // practice. Every other fill in this method is DrawBoxSolid for the same reason; if a rounded one is
            // ever added here, that arc comes back.
            UIElementPainter.PaintButton(rect, palette, over, held, false, false);

            if (selected)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);

            DrawAccentRule(rect, selected, over, disabled, palette);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            if (barPercent > 0f)
            {
                Rect progress = new Rect(rect.x, rect.yMax - ProgressHeight,
                    rect.width * Mathf.Clamp01(barPercent), ProgressHeight);
                Widgets.DrawBoxSolid(progress, palette.AccentMuted);
            }

            bool hasLabel = !label.NullOrEmpty();

            // Measured before anything is laid out, because the badge takes its width out of the label's.
            // At Tiny: a bar button is 40 pixels tall at minimum and a Small badge on it touches both edges.
            bool hasBadge = !badge.NullOrEmpty();
            float badgeWidth = 0f;

            if (hasBadge)
            {
                Text.Font = GameFont.Tiny;
                badgeWidth = UITagControl.WidthFor(badge);
            }

            if (icon != null)
            {
                // Tinted from the palette on the same three-state ramp as the label below, so an icon and
                // the text beside it brighten together rather than the icon sitting at full white while
                // the label is grey. Bar icons are silhouettes -- ours and vanilla's both -- so tinting
                // is what they are for; art that must keep its own colors does not belong on a themed
                // bar in the first place.
                GUI.color = disabled ? palette.TextDisabled
                    : selected ? palette.TextPrimary : palette.TextSecondary;

                float size = Mathf.Min(rect.height - IconPad * 2f, 24f);
                Rect iconRect = hasLabel
                    ? new Rect(rect.x + IconPad, rect.center.y - size * 0.5f, size, size)
                    : new Rect(rect.center.x - size * 0.5f, rect.center.y - size * 0.5f, size, size);

                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                GUI.color = previousColor;
            }

            if (hasLabel)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = icon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                GUI.color = disabled ? palette.TextDisabled
                    : selected ? palette.TextPrimary : palette.TextSecondary;

                float textX = icon != null ? rect.x + IconPad * 2f + 24f : rect.x;
                float reserved = hasBadge ? badgeWidth + BadgeGap : 0f;
                Rect textRect = new Rect(textX, rect.y,
                    Mathf.Max(0f, rect.xMax - textX - IconPad - reserved), rect.height);

                // Ellipsized only when a badge is squeezing it. Without one the rect is the whole slot and
                // Widgets.Label is what every other tab has always used; switching unconditionally would put an
                // ellipsis on labels that fit today. LabelEllipses also refuses to draw below 13 pixels and
                // throws out of Substring under that, which a narrow slot with a three digit badge can reach.
                if (hasBadge && textRect.width >= 24f)
                    Widgets.LabelEllipses(textRect, label);
                else if (!hasBadge)
                    Widgets.Label(textRect, label);
            }

            // Drawn outside the label block on purpose: an icon-only tab, which is what a minimized slot is,
            // has no label to sit beside and still needs to say that something is waiting.
            if (hasBadge)
            {
                Text.Font = GameFont.Tiny;

                Rect badgeRect = new Rect(rect.xMax - IconPad - badgeWidth, rect.y, badgeWidth, rect.height);

                // Danger from the palette rather than a literal red, so a theme restating what danger looks
                // like carries here. Purely decorative: the whole slot stays one click target below, so the
                // badge cannot swallow the press that opens the tab.
                UITagControl.Draw(badgeRect, badge, palette.Danger, palette);
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (disabled)
                return false;

            if (!Widgets.ButtonInvisible(rect))
                return false;

            SoundDefOf.Mouseover_ButtonToggle.PlayOneShotOnCamera();
            return true;
        }

        /// <summary>
        /// The text on a button, or null for an icon-only one.
        ///
        /// LabelCap, not ShortenedLabelCap. Vanilla abbreviates because its bar divides the full screen
        /// width between every tab at a fixed height; ours can be arranged, so a truncated word like
        /// "Architec" is a worse trade than a slightly tighter fit. A label too wide for its slot is
        /// clipped by the button, which reads as a layout to fix rather than as the tab's name.
        ///
        /// Here rather than in the bar's patch because the menu popup draws buttons too, and the two have to
        /// agree about what a slot is called. A menu's own name is <c>entry.menu</c>: leaving that out of the
        /// fallback chain was why a named menu drew as a bare icon whatever display mode it was given, since
        /// a null label is exactly how this reports "icon only".
        /// </summary>
        public static string LabelFor(UIButtonBarEntry entry, MainButtonDef def)
        {
            switch (entry.mode)
            {
                case UIBarButtonMode.Minimize:
                    return null;

                case UIBarButtonMode.TextOnly:
                case UIBarButtonMode.Maximize:
                    break;

                default:
                    // minimized is a vanilla field, so a def that asked to be icon-only is honored
                    // without the player having to say so again. Maximize is what overrides it.
                    if (def != null && def.minimized)
                        return null;
                    break;
            }

            if (!entry.label.NullOrEmpty())
                return entry.label;

            if (entry.IsMenu)
                return entry.menu;

            return UIBarDefaultLabels.DefaultNameFor(entry, def);
        }

        /// <summary>
        /// The entry's own icon wins over the def's, which is how a tab that shipped without one gets an
        /// icon and how one that shipped with an unwanted icon gets a better one. Failing both, this mod's
        /// own art for the vanilla tabs that ship bare.
        ///
        /// The def's icon is checked before ours on purpose: most of the bar has no art, but the few tabs
        /// that do should keep the look their own mod chose.
        ///
        /// Text-only mode suppresses it entirely, which is the point of that mode.
        /// </summary>
        public static Texture2D IconFor(UIButtonBarEntry entry, MainButtonDef def)
        {
            if (entry.mode == UIBarButtonMode.TextOnly)
                return null;

            return UIBarDefaultIcons.Resolve(entry, def);
        }

        /// <summary>
        /// Whether a slot draws no text, and so should be sized to its icon rather than share the bar.
        ///
        /// Asked of <see cref="LabelFor"/> rather than tested against the mode directly, so the one place
        /// that decides whether a label is drawn is also the place that decides the width. Reading
        /// <c>mode == Minimize</c> here would miss a def carrying vanilla's own <c>minimized</c> flag, and
        /// would go wrong again the moment a new mode is added.
        /// </summary>
        public static bool IsIconOnly(UIButtonBarEntry entry)
        {
            return LabelFor(entry, entry.IsMenu ? null : entry.Def).NullOrEmpty();
        }

        /// <summary>
        /// Opens this mod's settings.
        ///
        /// Our own window rather than a page in the vanilla Options dialog. Dialog_Options only lists
        /// categories whose def came from an official mod -- anything else is skipped with an "Unofficial
        /// OptionCategoryDef ... ignoring" line in the log -- so a mod cannot add one.
        /// </summary>
        /// <param name="pauseGame">
        /// Passed to the window. Escape asks for a pause the way vanilla's menu did; this bar does not.
        /// </param>
        public static void OpenUIOptions(bool pauseGame = false)
        {
            if (Find.WindowStack.WindowOfType<Dialog_UIOptions>() != null)
                return;

            Find.WindowStack.Add(new Dialog_UIOptions(pauseGame));
        }
    }

    /// <summary>
    /// The column of tabs a menu slot reveals, floating just above the bar.
    ///
    /// A Window rather than something drawn inline in the bar's own pass. RimWorld's window stack already
    /// gets click-outside-to-close, input capture and draw order right, and a menu drawn inline would
    /// have to reimplement all three. It also keeps the bar's width fixed: expanding a menu in place
    /// would resize every other button each time one was opened.
    /// </summary>
    public class Window_BarMenu : Window
    {
        private const float RowHeight = 32f;
        private const float RowGap = 2f;

        private readonly List<UIButtonBarEntry> items;
        private readonly float slotX;
        private readonly float slotWidth;

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Max(slotWidth, 160f),
                items.Count * (RowHeight + RowGap) + RowGap * 2f);

        public Window_BarMenu(List<UIButtonBarEntry> items, float slotX, float slotWidth)
        {
            this.items = items;
            this.slotX = slotX;
            this.slotWidth = slotWidth;

            doCloseX = false;
            doCloseButton = false;
            drawShadow = false;
            closeOnClickedOutside = true;
            preventCameraMotion = false;
            layer = WindowLayer.GameUI;
        }

        protected override float Margin => RowGap;

        /// <summary>Sits directly on top of the bar, left edge aligned with the slot that opened it.</summary>
        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            float x = Mathf.Clamp(slotX, 0f, UI.screenWidth - size.x);
            float y = UI.screenHeight - MainButtonDef.ButtonHeight - size.y;

            windowRect = new Rect(x, Mathf.Max(0f, y), size.x, size.y);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("ButtonBar.Menu", inRect, () => DrawContents(inRect),
                "This bar menu shows a failure notice. The tabs inside it can still be reached by their "
                + "keyboard shortcuts.");
        }

        private void DrawContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            float y = inRect.y;

            foreach (UIButtonBarEntry entry in items)
            {
                MainButtonDef def = entry.Def;
                if (def == null)
                    continue;

                MainButtonWorker worker = def.Worker;
                Rect row = new Rect(inRect.x, y, inRect.width, RowHeight);

                // Through the same label and icon resolution the bar uses, so a tab renamed or given an icon
                // inside a menu shows that here. Reading def.LabelCap and def.Icon directly, as this used to,
                // meant edits to a child were stored and then ignored by the only thing that drew it.
                bool clicked = UIButtonBarRenderer.Draw(row,
                    UIButtonBarRenderer.LabelFor(entry, def), UIButtonBarRenderer.IconFor(entry, def),
                    Find.MainTabsRoot?.OpenTab == def, worker != null && worker.Disabled,
                    worker?.ButtonBarPercent ?? 0f, palette, UIBarBadges.For(def));

                if (clicked)
                {
                    // Closed before activating: the tab we are about to open may want the focus, and a
                    // menu left on top of it reads as the click not having worked.
                    Close(false);
                    worker?.InterfaceTryActivate();
                    return;
                }

                y += RowHeight + RowGap;
            }
        }
    }
}
