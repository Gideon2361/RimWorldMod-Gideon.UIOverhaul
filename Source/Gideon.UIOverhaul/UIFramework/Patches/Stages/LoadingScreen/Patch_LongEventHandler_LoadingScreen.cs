using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
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
    /// <b>Only the full-screen kind of long event is taken over.</b> LongEventsOnGUI has two branches: a
    /// standalone one that paints the whole screen, and one that puts a small status window on the window
    /// stack over a game that keeps running. This replaces the first and must leave the second alone, and
    /// the reason is not cosmetic:
    ///
    /// <c>DrawLongEventWindowContents</c> is the only thing that ever sets <c>alreadyDisplayed</c> on the
    /// current event. <c>ShouldWaitUntilDisplayed</c> stays true until it does, and
    /// <c>UpdateCurrentSynchronousEvent</c> returns without running the event's action while that is true.
    /// Suppressing the window therefore did not merely hide it -- it stopped the event ever executing, so it
    /// never completed and never cleared. The game was left in a long event forever: our screen painted over
    /// the terrain every frame, and since <c>ShouldWaitForEvent</c> is false for a standard-window event,
    /// <c>Root_Play.Update</c> went on calling <c>Game.UpdatePlay</c> underneath it -- which is also where the
    /// "Called DrawWorldLayers() but already regenerating" errors came from.
    ///
    /// <c>ShouldWaitForEvent</c> is the gate because it is exactly vanilla's own condition for the standalone
    /// branch: an event is up, and either it does not use the standard window or there is no UIRoot to put one
    /// on. Asynchronous events, map generation included, satisfy it, so the loading screen still covers
    /// everything it was written for.
    /// </summary>
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
    public static class Patch_LongEventHandler_LongEventsOnGUI
    {
        /// <summary>
        /// True while a long event was running as of the previous frame, so the start of a new one can be
        /// detected.
        /// </summary>
        private static bool wasRunning;

        /// <summary>Returns false to skip vanilla's drawing entirely.</summary>
        public static bool Prefix()
        {
            // Guarded separately from the drawing below, and before the Failed check, because this is bookkeeping
            // rather than drawing: it has to keep happening so that a later event still starts from zero, even
            // after the screen itself has been handed back to vanilla.
            UIGuard.Try("LoadingScreen.TrackEventStart", () =>
            {
                bool running = LongEventHandler.AnyEventNowOrWaiting;

                // Clear the previous event's figures the moment a new one starts. Resetting only in
                // PlayDataLoader.LoadAllPlayData covered the initial load and nothing else, so map generation
                // -- a separate long event -- opened with the bar still full from the end of startup.
                if (running && !wasRunning)
                    UIFramework.Stages.UILoadingScreen.Reset();

                wasRunning = running;
            }, "A loading screen may open showing the previous load's progress.");

            // ShouldWaitForEvent, not AnyEventNowOrWaiting. The difference is the standard-window events,
            // which must be left to vanilla or they never execute at all -- see the note on this class.
            if (Failed || !LongEventHandler.ShouldWaitForEvent)
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
                UIGuard.Report("LoadingScreen.Draw", ex,
                    "Vanilla's loading screen is used for the rest of the session. Loading itself is "
                    + "unaffected.");
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

    // There is deliberately no patch on DrawLongEventWindow, and one must not be added back.
    //
    // It used to be suppressed, on the reasoning that our screen replaces vanilla's and the window route
    // would otherwise draw on top of it. That was wrong twice over. DrawLongEventWindow is only ever reached
    // from the standard-window branch of LongEventsOnGUI, which we no longer take over, so there is nothing
    // left to collide with -- and suppressing it was what hung the game, because the contents it draws are
    // the only thing that marks the event as displayed and so lets it run at all.

    /// <summary>
    /// Clears the progress state at the start of a load. Matters on the second and later loads --
    /// changing the mod list reloads all play data -- where stale figures from the previous run would
    /// otherwise leave the bar starting at full.
    /// </summary>
    [HarmonyPatch(typeof(PlayDataLoader), nameof(PlayDataLoader.LoadAllPlayData))]
    public static class Patch_PlayDataLoader_LoadAllPlayData
    {
        /// <summary>
        /// Guarded because of what it prefixes. An escape from here would stop LoadAllPlayData before it began, and a
        /// game that cannot load its play data does not reach the main menu -- for the sake of resetting a progress
        /// bar.
        /// </summary>
        public static void Prefix()
        {
            UIGuard.Try("LoadingScreen.ResetForLoad", UIFramework.Stages.UILoadingScreen.Reset,
                "The loading screen's bar may start part-full.");
        }
    }
}
