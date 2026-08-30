using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// Pushes the graphics settings this mod stores into the engine, at startup and whenever one changes.
    ///
    /// <b>This exists because Unity forgets.</b> <c>QualitySettings.vSyncCount</c> is set once from the quality
    /// level when the game starts and is never written back to disk, so a change made while playing is gone at
    /// the next launch. RimWorld has no preference of its own to hang it on -- there is no vsync field in
    /// <c>Prefs</c>, <c>PrefsData</c> or <c>ResolutionUtility</c> -- so the value lives in our settings file and
    /// something has to put it back. This is that something.
    ///
    /// <b><c>StaticConstructorOnStartup</c> rather than the mod constructor.</b> The constructor runs before defs
    /// load and is the wrong place to touch the renderer; this runs on the main thread once the game is up,
    /// which is early enough that the first frame drawn already obeys the setting.
    ///
    /// <b>Applying a setting that already matches costs nothing,</b> which is why this does not compare first.
    /// Assigning <c>vSyncCount</c> the value it already holds is not a mode change.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class GraphicsPreferences
    {
        static GraphicsPreferences()
        {
            Apply();
        }

        /// <summary>
        /// Writes the stored graphics settings into the engine.
        ///
        /// Called from the options window as well as at startup, so the switch takes effect while you are
        /// looking at it rather than at the next launch.
        /// </summary>
        internal static void Apply()
        {
            UIGuard.Try("Options.ApplyGraphics", () =>
                {
                    // One rather than two: every v-blank, not every other one. Two is a half-rate mode that reads
                    // as stuttering to anybody who did not ask for it, and this switch is on or off.
                    QualitySettings.vSyncCount = UIOverhaulSettingsFile.Current.vsync ? 1 : 0;
                },
                "The vertical sync setting could not be applied, so the game keeps whatever it started with.");
        }
    }
}
