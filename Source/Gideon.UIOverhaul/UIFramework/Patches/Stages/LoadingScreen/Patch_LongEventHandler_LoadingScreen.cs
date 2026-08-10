using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Stages;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Replaces the vanilla loading screen.
    ///
    /// LongEventsOnGUI does not merely delegate to a window: it draws the event text itself, inline,
    /// after an early-out for a null currentEvent. So this prefix *replaces* the method rather than
    /// running ahead of it. Drawing in a prefix and letting the original continue puts our backdrop
    /// underneath vanilla's text, which is a background change, not a replacement.
    ///
    /// The null-event path is preserved and handed back to vanilla, because that branch resets the
    /// gameplay tip timer and has nothing to do with drawing.
    ///
    /// DrawLongEventWindow is suppressed as well, for the cases where the window route is taken.
    /// </summary>
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
    public static class Patch_LongEventHandler_LongEventsOnGUI
    {
        /// <summary>Returns false to skip vanilla's drawing entirely.</summary>
        public static bool Prefix()
        {
            if (Failed || !LongEventHandler.AnyEventNowOrWaiting)
                return true;

            try
            {
                Draw();
                return false;
            }
            catch (Exception ex)
            {
                // A throwing loading screen must not become a game that will not start. Report once
                // and let vanilla's own drawing continue for the rest of the load.
                Log.ErrorOnce("[Gideon.UIOverhaul] Loading screen failed to draw; "
                              + "falling back to the vanilla screen.\n" + ex, 0x17C0_10AD);
                Failed = true;
                return true;
            }
        }

        /// <summary>
        /// Set when drawing has thrown. Both patches check it, so one failure reverts the whole screen
        /// to vanilla rather than leaving a half-replaced one.
        /// </summary>
        public static bool Failed { get; private set; }

        private static void Draw()
        {
            UILoadingScreenConfig config = UILoadingScreenConfig.Active;
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            UILoadingSnapshot progress = UIFramework.Stages.UILoadingScreen.Snapshot();

            Rect screen = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
            config.Drawer.Draw(screen, config, progress, palette);
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), "DrawLongEventWindow")]
    public static class Patch_LongEventHandler_DrawLongEventWindow
    {
        /// <summary>
        /// Skips vanilla's window. Returning true here restores the stock screen, which is what
        /// happens if our own drawing has thrown.
        /// </summary>
        public static bool Prefix()
        {
            return Patch_LongEventHandler_LongEventsOnGUI.Failed;
        }
    }

    /// <summary>
    /// Clears the progress state at the start of a load. Matters on the second and later loads --
    /// changing the mod list reloads all play data -- where stale figures from the previous run would
    /// otherwise leave the bar starting at full.
    /// </summary>
    [HarmonyPatch(typeof(PlayDataLoader), nameof(PlayDataLoader.LoadAllPlayData))]
    public static class Patch_PlayDataLoader_LoadAllPlayData
    {
        public static void Prefix()
        {
            UIFramework.Stages.UILoadingScreen.Reset();
        }
    }
}
