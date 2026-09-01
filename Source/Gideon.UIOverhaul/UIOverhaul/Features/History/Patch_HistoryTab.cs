using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.History
{
    /// <summary>
    /// Whether this mod draws the history tab.
    ///
    /// Asked in one place because four patches ask it, and a setting read from four call sites is a setting
    /// that ends up half applied: the contents replaced but the window sized for vanilla's three pages.
    /// </summary>
    internal static class HistoryTabFeature
    {
        internal static bool Enabled
        {
            get
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings == null || settings.historyTab;
            }
        }
    }

    /// <summary>
    /// Hands the history tab over to <see cref="HistoryPanel"/>.
    ///
    /// <b>The vanilla window is kept and its contents replaced, rather than a tab of our own being added.</b>
    /// The same reasoning as the research tab: History already has a button on the bar, an icon and a place in
    /// the order, and <c>MainButtonDefOf.History</c> is what everything in the game that opens this screen goes
    /// through. Replacing the contents means none of those callers notice.
    ///
    /// <b>The prefix returns false whether or not our drawing worked.</b> Per the rule this mod has had since
    /// 2026-08-17: a window we replace never quietly hands back to RimWorld's, because a silent swap hides the
    /// defect that caused it. <see cref="UIGuardedPanel"/> shows a failure notice in our own window instead.
    ///
    /// <b>Nothing here writes to the game.</b> Every source this tab reads is a record the colony keeps anyway,
    /// so a failure costs a screen and never a save.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_History), nameof(MainTabWindow_History.DoWindowContents))]
    internal static class Patch_MainTabWindow_History_DoWindowContents
    {
        public static bool Prefix(Rect fillRect)
        {
            if (!HistoryTabFeature.Enabled)
                return true;

            UIGuardedPanel.Draw("History.Tab", fillRect, () =>
            {
                Widgets.DrawBoxSolid(fillRect, UIColorPaletteDef.Active.WindowBackground);

                HistoryPanel.Draw(fillRect);
            }, "The history tab shows a failure notice. Nothing about your colony has changed: this screen "
               + "only reads records the game already keeps.");

            return false;
        }
    }

    /// <summary>
    /// Sizes the window for our layout instead of for vanilla's three pages.
    ///
    /// Patched on <c>RequestedTabSize</c>, which is the property the history window actually overrides;
    /// vanilla returns a flat 1010 by 640 there and this screen needs the width for a rail beside a plot.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_History), "get_RequestedTabSize")]
    internal static class Patch_MainTabWindow_History_RequestedTabSize
    {
        public static void Postfix(ref Vector2 __result)
        {
            if (!HistoryTabFeature.Enabled)
                return;

            __result = UIGuard.Try("History.TabSize",
                () => new Vector2(HistoryPanel.WindowWidth, HistoryPanel.WindowHeight), __result, null);
        }
    }

    /// <summary>
    /// Takes the window's margin away, so the panel insets itself the way this mod's other tabs do.
    ///
    /// <b>Patched on the base class with an instance test, because the history window does not override it.</b>
    /// The test is on the exact window type rather than on assignability, so a mod subclassing the history
    /// window to add its own drawing keeps the margin its layout was written against.
    /// </summary>
    [HarmonyPatch(typeof(Window), "get_Margin")]
    internal static class Patch_Window_Margin_History
    {
        public static void Postfix(Window __instance, ref float __result)
        {
            if (__instance != null && __instance.GetType() == typeof(MainTabWindow_History)
                                   && HistoryTabFeature.Enabled)
                __result = 0f;
        }
    }

    /// <summary>
    /// Clears what should not survive a close, and recounts wealth, when the tab opens.
    ///
    /// A postfix rather than a prefix: vanilla's own <c>PreOpen</c> builds its three <c>TabRecord</c>s, picks a
    /// group and forces the same wealth recount, none of which we use but all of which are cheap, and letting
    /// it run keeps the window sane for the frame where this mod's setting is switched off mid-session.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_History), nameof(MainTabWindow_History.PreOpen))]
    internal static class Patch_MainTabWindow_History_PreOpen
    {
        public static void Postfix()
        {
            if (!HistoryTabFeature.Enabled)
                return;

            UIGuard.Try("History.PreOpen", HistoryPanel.Notify_Opened, null);
        }
    }
}
