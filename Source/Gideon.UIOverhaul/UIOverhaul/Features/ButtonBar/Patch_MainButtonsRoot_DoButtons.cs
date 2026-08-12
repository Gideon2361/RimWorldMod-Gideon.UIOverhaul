using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>
    /// Replaces the bottom button bar, so it follows the theme and honors the player's layout.
    ///
    /// DoButtons is the layout half of the bar and nothing else: MainButtonsOnGUI calls it, then handles
    /// hotkeys separately by walking allButtonsInOrder itself. Replacing only DoButtons therefore leaves
    /// every keyboard shortcut working, including for tabs the player has hidden or tucked inside a menu.
    /// Patching MainButtonsOnGUI instead would have meant reimplementing that.
    ///
    /// Per-button state still comes from vanilla. Visible, Disabled and ButtonBarPercent are read off each
    /// MainButtonWorker and activation goes through InterfaceTryActivate, so a tab that hides itself
    /// outside its DLC, greys out at the wrong time, or shows research progress behaves as it always did.
    /// </summary>
    [HarmonyPatch(typeof(MainButtonsRoot), "DoButtons")]
    public static class Patch_MainButtonsRoot_DoButtons
    {
        /// <summary>
        /// Set once drawing has thrown, after which the vanilla bar comes back for the rest of the
        /// session. An unusable bar is a game that cannot be played.
        /// </summary>
        public static bool Failed { get; private set; }

        public static bool Prefix()
        {
            if (Failed)
                return true;

            try
            {
                Draw();
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Gideon.UIOverhaul] The button bar failed to draw; falling back to the "
                              + "vanilla bar.\n" + ex, 0x17C0_10B4);
                Failed = true;
                return true;
            }
        }

        private static void Draw()
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            float height = MainButtonDef.ButtonHeight;
            Rect bar = new Rect(0f, UI.screenHeight - height, UI.screenWidth, height);

            // The strip behind the buttons. Without it, a gap left by a hidden tab shows the map through
            // the bar rather than reading as part of the chrome.
            Widgets.DrawBoxSolid(bar, palette.WindowBackground);

            List<UIButtonBarEntry> entries = Visible(UIButtonBarConfig.Current.Resolve());

            // Always drawn. This button is the only route to the mod's settings, so it is not optional --
            // hiding it would leave no way to get them back.
            float optionsWidth = UIButtonBarRenderer.MinimizedWidth;
            float gap = UIButtonBarRenderer.ButtonGap;

            // Fixed-width slots take what they need and only the ones showing text share out what is left.
            // Giving every button an equal share made minimizing pointless: the button got narrower content
            // but kept the same footprint, so nothing was reclaimed. Widgets are fixed-width for a different
            // reason -- a readout sized to a share of the bar would have its text cut off on a crowded bar
            // and swim in space on an empty one.
            int slots = entries.Count;
            int labelled = 0;
            float narrowTotal = 0f;

            foreach (UIButtonBarEntry entry in entries)
            {
                float fixedWidth = FixedWidthOf(entry);

                if (fixedWidth >= 0f)
                    narrowTotal += fixedWidth;
                else
                    labelled++;
            }

            // Every button is followed by a gap, the options button included, so the widths are what is left
            // after the gaps are taken out rather than being trimmed afterwards -- trimming would let rounding
            // push the last button past the edge of the screen.
            float available = bar.width - optionsWidth - gap * (slots + 1) - narrowTotal;
            float slotWidth = labelled > 0 ? Mathf.Max(0f, available / labelled) : 0f;

            float x = bar.x;

            foreach (UIButtonBarEntry entry in entries)
            {
                float fixedWidth = FixedWidthOf(entry);
                float width = fixedWidth >= 0f ? fixedWidth : slotWidth;
                Rect slot = new Rect(x, bar.y, width, bar.height);

                if (entry.IsWidget)
                    DrawWidgetSlot(slot, entry, palette);
                else if (entry.IsMenu)
                    DrawMenuSlot(slot, entry, palette);
                else
                    DrawTabSlot(slot, entry, palette);

                x += width + gap;
            }

            Rect optionsSlot = new Rect(bar.xMax - optionsWidth, bar.y, optionsWidth, bar.height);
            TooltipHandler.TipRegion(optionsSlot, (TipSignal) "UI options");

            if (UIButtonBarRenderer.Draw(optionsSlot, null, UIButtonBarRenderer.OptionsIcon,
                    false, false, 0f, palette))
                UIButtonBarRenderer.OpenUIOptions();
        }

        private static void DrawTabSlot(Rect slot, UIButtonBarEntry entry, UIColorPaletteDef palette)
        {
            MainButtonDef def = entry.Def;
            if (def == null)
                return;

            MainButtonWorker worker = def.Worker;

            bool clicked = UIButtonBarRenderer.Draw(slot, LabelFor(entry, def), IconFor(entry, def),
                Find.MainTabsRoot?.OpenTab == def, worker != null && worker.Disabled,
                worker?.ButtonBarPercent ?? 0f, palette);

            if (clicked)
                worker?.InterfaceTryActivate();
        }

        private static void DrawMenuSlot(Rect slot, UIButtonBarEntry entry, UIColorPaletteDef palette)
        {
            List<MainButtonDef> children = new List<MainButtonDef>();
            bool anyOpen = false;

            foreach (string childName in entry.children)
            {
                MainButtonDef child = DefDatabase<MainButtonDef>.GetNamedSilentFail(childName);
                if (child == null || UIButtonBarConfig.Current.IsHidden(childName))
                    continue;

                MainButtonWorker worker = child.Worker;
                if (worker != null && !worker.Visible)
                    continue;

                children.Add(child);

                if (Find.MainTabsRoot?.OpenTab == child)
                    anyOpen = true;
            }

            if (children.Count == 0)
                return;

            // Highlighted when one of its children is the open tab, so a menu shows where you are rather
            // than looking untouched while the tab it contains is on screen.
            bool clicked = UIButtonBarRenderer.Draw(slot, LabelFor(entry, null), IconFor(entry, null),
                anyOpen, false, 0f, palette);

            if (!clicked)
                return;

            // Toggle: clicking the open menu closes it rather than reopening it underneath itself.
            Window_BarMenu existing = Find.WindowStack.WindowOfType<Window_BarMenu>();
            existing?.Close(false);

            if (existing == null)
                Find.WindowStack.Add(new Window_BarMenu(children, slot.x, slot.width));
        }

        /// <summary>
        /// One widget: a sunken tray with the widget's own content in it.
        ///
        /// The tray is painted here rather than by each worker, so every widget -- ours and any a mod adds --
        /// sits in the same frame. Sunken and without the accent rule the tab buttons carry, because a
        /// readout is not something you press and should not look like one. The controls a widget draws
        /// inside its tray are raised in the usual way, so within one slot the distinction still reads.
        /// </summary>
        private static void DrawWidgetSlot(Rect slot, UIButtonBarEntry entry, UIColorPaletteDef palette)
        {
            UIBarWidgetWorker worker = entry.WidgetDef?.Worker;
            if (worker == null)
                return;

            Widgets.DrawBoxSolid(slot, palette.SurfaceSunken);

            worker.DrawSafely(slot, palette);

            // A readout has no button underneath it, and the bar is drawn over the map. Without this, clicking
            // the date issues an order at whatever tile is behind the bar.
            GenUI.AbsorbClicksInRect(slot);
        }

        /// <summary>
        /// The width this slot needs, or a negative number when it should share out what the fixed-width
        /// slots leave behind.
        /// </summary>
        private static float FixedWidthOf(UIButtonBarEntry entry)
        {
            if (entry.IsWidget)
            {
                UIBarWidgetWorker worker = entry.WidgetDef?.Worker;

                // A widget that cannot be built is filtered out by Visible before this is asked, so the
                // fallback is only reached if a def loses its worker between the two calls.
                return worker?.Width ?? UIButtonBarRenderer.MinimizedWidth;
            }

            return IsIconOnly(entry) ? UIButtonBarRenderer.MinimizedWidth : -1f;
        }

        /// <summary>
        /// Whether this slot draws no text, and so should be sized to its icon rather than share the bar.
        ///
        /// Asked of <see cref="LabelFor"/> rather than tested against the mode directly, so the one place
        /// that decides whether a label is drawn is also the place that decides the width. Reading
        /// <c>mode == Minimize</c> here would miss a def carrying vanilla's own <c>minimized</c> flag, and
        /// would go wrong again the moment a new mode is added.
        /// </summary>
        private static bool IsIconOnly(UIButtonBarEntry entry)
        {
            return LabelFor(entry, entry.IsMenu ? null : entry.Def).NullOrEmpty();
        }

        /// <summary>
        /// The text on a button, or null for an icon-only one.
        ///
        /// LabelCap, not ShortenedLabelCap. Vanilla abbreviates because its bar divides the full screen
        /// width between every tab at a fixed height; ours can be arranged, so a truncated word like
        /// "Architec" is a worse trade than a slightly tighter fit. A label too wide for its slot is
        /// clipped by the button, which reads as a layout to fix rather than as the tab's name.
        /// </summary>
        private static string LabelFor(UIButtonBarEntry entry, MainButtonDef def)
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

            return def != null ? def.LabelCap.ToString() : entry.tab;
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
        private static Texture2D IconFor(UIButtonBarEntry entry, MainButtonDef def)
        {
            if (entry.mode == UIBarButtonMode.TextOnly)
                return null;

            return UIBarDefaultIcons.Resolve(entry, def);
        }

        /// <summary>
        /// Drops slots nothing would be drawn in. A worker reporting Visible false is how vanilla hides a
        /// tab that needs a DLC or a particular game state, and a menu whose children are all in that
        /// position should disappear with them rather than leave a button that opens an empty column.
        /// </summary>
        private static List<UIButtonBarEntry> Visible(List<UIButtonBarEntry> entries)
        {
            List<UIButtonBarEntry> result = new List<UIButtonBarEntry>();

            foreach (UIButtonBarEntry entry in entries)
            {
                if (entry.IsWidget)
                {
                    // A widget hides itself when it has nothing to report -- the weather on a pocket map, the
                    // date before a world exists -- and the slot goes with it rather than leaving a gap.
                    UIBarWidgetWorker widgetWorker = entry.WidgetDef?.Worker;

                    if (widgetWorker != null && widgetWorker.Visible)
                        result.Add(entry);

                    continue;
                }

                if (entry.IsMenu)
                {
                    foreach (string childName in entry.children)
                    {
                        MainButtonDef child = DefDatabase<MainButtonDef>.GetNamedSilentFail(childName);
                        MainButtonWorker childWorker = child?.Worker;

                        if (child != null && (childWorker == null || childWorker.Visible)
                                          && !UIButtonBarConfig.Current.IsHidden(childName))
                        {
                            result.Add(entry);
                            break;
                        }
                    }

                    continue;
                }

                MainButtonDef def = entry.Def;
                MainButtonWorker worker = def?.Worker;

                if (def != null && (worker == null || worker.Visible))
                    result.Add(entry);
            }

            return result;
        }
    }
}
