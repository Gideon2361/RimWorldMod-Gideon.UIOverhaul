using System.Collections.Generic;
using Gideon.UIFramework.Defs;
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
            OptionsIcon = ContentFinder<Texture2D>.Get(OptionsIconPath, false);

            if (OptionsIcon == null)
            {
                // Ours to ship, so a miss is a packaging fault rather than anything the player did. The
                // button still works; it just draws empty.
                Log.Error($"[Gideon.UIOverhaul] Missing '{OptionsIconPath}'. The bar's UI options button "
                          + "will have no icon.");
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
            float barPercent, UIColorPaletteDef palette)
        {
            bool over = !disabled && Mouse.IsOver(rect);
            bool held = over && Input.GetMouseButton(0);

            // Borderless: these buttons sit in a continuous strip, where each outline would double against
            // its neighbor's. The gap between them and the accent rule below do the separating instead.
            UIElementPainter.PaintButton(rect, palette, over, held, false);

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
                Rect textRect = new Rect(textX, rect.y, rect.xMax - textX - IconPad, rect.height);
                Widgets.Label(textRect, label);
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
        /// Opens this mod's settings.
        ///
        /// Our own window rather than a page in the vanilla Options dialog. Dialog_Options only lists
        /// categories whose def came from an official mod -- anything else is skipped with an "Unofficial
        /// OptionCategoryDef ... ignoring" line in the log -- so a mod cannot add one.
        /// </summary>
        public static void OpenUIOptions()
        {
            if (Find.WindowStack.WindowOfType<Dialog_UIOptions>() != null)
                return;

            Find.WindowStack.Add(new Dialog_UIOptions());
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

        private readonly List<MainButtonDef> items;
        private readonly float slotX;
        private readonly float slotWidth;

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Max(slotWidth, 160f),
                items.Count * (RowHeight + RowGap) + RowGap * 2f);

        public Window_BarMenu(List<MainButtonDef> items, float slotX, float slotWidth)
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
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            float y = inRect.y;

            foreach (MainButtonDef def in items)
            {
                MainButtonWorker worker = def.Worker;
                Rect row = new Rect(inRect.x, y, inRect.width, RowHeight);

                // Full label, not the abbreviation: a menu column is as wide as it needs to be, so there
                // is nothing to be gained by truncating.
                bool clicked = UIButtonBarRenderer.Draw(row, def.LabelCap, def.Icon,
                    Find.MainTabsRoot?.OpenTab == def, worker != null && worker.Disabled,
                    worker?.ButtonBarPercent ?? 0f, palette);

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
