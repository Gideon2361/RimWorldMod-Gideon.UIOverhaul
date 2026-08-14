using System;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Panel
{
    /// <summary>
    /// Shows the frame and tick counters in the corner on this mod's say-so rather than only on the developer
    /// view settings'.
    ///
    /// <b>The problem.</b> <c>GlobalControls.GlobalControlsOnGUI</c> guards both counters behind
    /// <c>DebugViewSettings.showFpsCounter</c> and <c>showTpsCounter</c>, which are reachable only through the
    /// developer view settings window. <c>DrawFpsCounter</c> and <c>DrawTpsCounter</c> are public and could be
    /// patched all day without effect, because when the flags are false they are never called at all. Knowing
    /// how fast the game is running is an ordinary thing to want, and it should not cost turning on developer
    /// mode and the pile of overlays and menus that comes with it.
    ///
    /// <b>Forced for the duration of the draw, then put back.</b> The flags are raised in the prefix and
    /// lowered again afterwards, so the value the developer view settings window shows and writes is never the
    /// value this leaves behind. Setting them once and leaving them would make this mod's setting and that
    /// window fight over one variable, and whichever was touched last would win.
    ///
    /// <b>It raises the flags and never lowers them.</b> Switching this off restores whatever the developer
    /// settings say rather than hiding a counter someone deliberately turned on there. The two are separate
    /// requests and neither should overrule the other; this one only ever adds.
    ///
    /// <b>A finalizer rather than a postfix</b> for the restore. A postfix does not run when the original
    /// throws, and this is the one case where skipping the restore does lasting damage: the flags would stay
    /// raised for the rest of the session, so a single bad frame would silently turn on the counters for good
    /// and leave the developer window disagreeing with the screen. The finalizer returns the exception it was
    /// handed, so nothing about how the failure is reported changes.
    /// </summary>
    [HarmonyPatch(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI))]
    public static class Patch_GlobalControls_PerformanceMeter
    {
        private static bool raisedFps;
        private static bool raisedTps;
        private static bool raisedClock;

        public static void Prefix()
        {
            raisedFps = false;
            raisedTps = false;
            raisedClock = false;

            // Read through the guard, falling back to leaving the flags alone: a settings file that cannot be
            // read must not change what the corner shows, and vanilla's behavior is to follow the developer
            // view settings.
            UIOverhaulSettingsFile settings = UIGuard.Try("Panel.ReadCornerSettings",
                () => UIOverhaulSettingsFile.Current, null,
                "The corner follows RimWorld's own settings, which is the vanilla behavior.");

            if (settings == null)
                return;

            if (settings.showPerformanceWidget)
            {
                // Recorded per flag rather than as one boolean, because the developer window can have one of
                // the two on already. Lowering that one afterwards would take away a counter this never turned
                // on.
                raisedFps = !DebugViewSettings.showFpsCounter;
                raisedTps = !DebugViewSettings.showTpsCounter;

                DebugViewSettings.showFpsCounter = true;
                DebugViewSettings.showTpsCounter = true;
            }

            // The clock is the same bargain against a different flag. Prefs.ShowRealtimeClock's setter only
            // writes Prefs.data and does not save, so raising it for one draw costs nothing on disk -- and it is
            // lowered again below well before anything would write the preferences out.
            if (settings.showTimeWidget)
            {
                raisedClock = !Prefs.ShowRealtimeClock;
                Prefs.ShowRealtimeClock = true;
            }
        }

        public static Exception Finalizer(Exception __exception)
        {
            if (raisedFps)
                DebugViewSettings.showFpsCounter = false;

            if (raisedTps)
                DebugViewSettings.showTpsCounter = false;

            if (raisedClock)
                Prefs.ShowRealtimeClock = false;

            raisedFps = false;
            raisedTps = false;
            raisedClock = false;

            // Handed straight back so the exception is rethrown exactly as it would have been. Returning null
            // here would swallow it, which would hide a failure in a vanilla method behind this mod.
            return __exception;
        }
    }
}
