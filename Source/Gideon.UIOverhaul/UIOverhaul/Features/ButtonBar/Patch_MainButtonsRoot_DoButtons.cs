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

            bool clicked = UIButtonBarRenderer.Draw(slot,
                UIButtonBarRenderer.LabelFor(entry, def), UIButtonBarRenderer.IconFor(entry, def),
                Find.MainTabsRoot?.OpenTab == def, worker != null && worker.Disabled,
                worker?.ButtonBarPercent ?? 0f, palette);

            if (clicked)
                worker?.InterfaceTryActivate();
        }

        private static void DrawMenuSlot(Rect slot, UIButtonBarEntry entry, UIColorPaletteDef palette)
        {
            List<UIButtonBarEntry> children = new List<UIButtonBarEntry>();
            bool anyOpen = false;

            foreach (UIButtonBarEntry child in entry.children)
            {
                MainButtonDef def = child.Def;
                if (def == null || UIButtonBarConfig.Current.IsHidden(child.tab))
                    continue;

                MainButtonWorker worker = def.Worker;
                if (worker != null && !worker.Visible)
                    continue;

                children.Add(child);

                if (Find.MainTabsRoot?.OpenTab == def)
                    anyOpen = true;
            }

            if (children.Count == 0)
                return;

            // Highlighted when one of its children is the open tab, so a menu shows where you are rather
            // than looking untouched while the tab it contains is on screen.
            bool clicked = UIButtonBarRenderer.Draw(slot,
                UIButtonBarRenderer.LabelFor(entry, null), UIButtonBarRenderer.IconFor(entry, null),
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

            return UIButtonBarRenderer.IsIconOnly(entry) ? UIButtonBarRenderer.MinimizedWidth : -1f;
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
                    foreach (UIButtonBarEntry child in entry.children)
                    {
                        MainButtonDef childDef = child.Def;
                        MainButtonWorker childWorker = childDef?.Worker;

                        if (childDef != null && (childWorker == null || childWorker.Visible)
                                             && !UIButtonBarConfig.Current.IsHidden(child.tab))
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
