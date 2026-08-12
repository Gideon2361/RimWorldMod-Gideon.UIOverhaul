using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Display
{
    /// <summary>
    /// Puts the game back into fullscreen at the display's native resolution on every launch.
    ///
    /// <b>The problem.</b> Alt+Enter leaves fullscreen and RimWorld remembers it: the new windowed size and
    /// fullscreen flag are written to Prefs, so the next launch comes up windowed at whatever the window
    /// happened to be. There is nothing to undo it, so the resolution has to be set by hand every time.
    ///
    /// <b>Off by default, and it has to be.</b> This overrides a display preference the player set, which is
    /// close to the rudest thing a mod can do uninvited -- someone who plays windowed on purpose would find the
    /// game fighting them at every launch with no obvious culprit. It runs only when the player has asked for it
    /// in the UI options page.
    ///
    /// <b>Why a startup hook rather than a patch.</b> There is nothing to intercept. Alt+Enter is Unity's own
    /// handling, and the preference it writes is legitimate; what is wanted is a decision applied after prefs
    /// load, which is what StaticConstructorOnStartup is for. Blocking the toggle instead would break Alt+Enter
    /// during a session, which is not the ask -- leaving fullscreen should still work, it just should not stick.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class StartupFullscreen
    {
        static StartupFullscreen()
        {
            Apply();
        }

        /// <summary>
        /// Sets fullscreen at native resolution, if the player asked for it and it is not already so.
        ///
        /// Public so the options page can apply it the moment the box is ticked, rather than making the player
        /// restart to see whether it worked.
        /// </summary>
        public static void Apply()
        {
            if (!UIOverhaulSettingsFile.Current.fullscreenOnStartup)
                return;

            int width = NativeWidth;
            int height = NativeHeight;

            if (width <= 0 || height <= 0)
            {
                UIDebug.Warning("Could not read a native resolution, so fullscreen on startup did nothing.");
                return;
            }

            // Nothing to do is the common case after the first launch, and worth checking: assigning these
            // unconditionally would re-apply a resolution change on every startup, which flickers the display
            // for no reason.
            if (Prefs.FullScreen && Prefs.ScreenWidth == width && Prefs.ScreenHeight == height)
                return;

            UIDebug.Log($"Setting fullscreen {width}x{height} (was "
                        + $"{Prefs.ScreenWidth}x{Prefs.ScreenHeight}, fullscreen={Prefs.FullScreen}).");

            Prefs.ScreenWidth = width;
            Prefs.ScreenHeight = height;
            Prefs.FullScreen = true;

            // Apply drives the actual mode change; Save writes it back so the game's own options page agrees
            // with what is on screen rather than showing the pre-override values.
            Prefs.Apply();
            Prefs.Save();
        }

        /// <summary>
        /// The display's native width.
        ///
        /// <c>UnityEngine.Display.main.systemWidth</c> is the desktop's own resolution and does not change with
        /// the game's window, which is exactly what "native" has to mean here.
        /// <c>Screen.currentResolution</c> is the fallback rather than the first choice because in fullscreen it
        /// reports the resolution the game is currently using -- so reading it while already at a wrong
        /// fullscreen resolution would report that wrong value back as the target and change nothing.
        /// </summary>
        private static int NativeWidth
        {
            get
            {
                int system = UnityEngine.Display.main?.systemWidth ?? 0;
                return system > 0 ? system : Screen.currentResolution.width;
            }
        }

        private static int NativeHeight
        {
            get
            {
                int system = UnityEngine.Display.main?.systemHeight ?? 0;
                return system > 0 ? system : Screen.currentResolution.height;
            }
        }
    }
}
