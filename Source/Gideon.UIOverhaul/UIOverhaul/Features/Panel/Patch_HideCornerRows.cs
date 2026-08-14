using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Panel
{
    /// <summary>
    /// Hides the speed controls in the corner.
    ///
    /// <b>The seam is chosen for where the layout cursor moves.</b> <c>DoTimespeedControls</c> subtracts the row's
    /// height from <c>curBaseY</c> inside itself, so a prefix that skips the whole method skips the space as well
    /// and the column closes up. That is the difference between this and the rows drawn inline by
    /// <c>GlobalControlsOnGUI</c>, where the caller moves the cursor and skipping the draw would leave a hole.
    ///
    /// <b>Hiding the row is not the same as not calling it, and getting that wrong cost the pause key.</b>
    /// <c>DoTimeControlsGUI</c> draws the buttons and then, in the same method, handles
    /// <c>KeyBindingDefOf.TogglePause</c> and the three speed shortcuts. Skipping the call outright -- which is
    /// what this did at first -- took the keyboard with it: space stopped pausing, silently, for anyone who
    /// hid the buttons. <c>SpeedGlyphs</c> already said so in as many words, and this ignored it.
    ///
    /// So the call still happens; it is aimed off screen. Nothing is drawn where anyone can see it, no click
    /// can land on it, and the shortcut half at the end of the method runs exactly as it always did. The
    /// alternative -- reimplementing those four bindings here -- is the thing SpeedGlyphs refused to do, for
    /// the good reason that getting them subtly wrong reads as "the pause key sometimes does nothing".
    ///
    /// <c>curBaseY</c> is deliberately left alone, so the column closes up over the hidden row.
    /// </summary>
    [HarmonyPatch(typeof(GlobalControlsUtility), nameof(GlobalControlsUtility.DoTimespeedControls))]
    public static class Patch_GlobalControlsUtility_DoTimespeedControls
    {
        /// <summary>
        /// Far enough off screen that nothing lands on a visible pixel at any resolution or UI scale, and not so
        /// far that the coordinates stop being exactly representable.
        /// </summary>
        internal const float OffScreen = -4000f;

        public static bool Prefix()
        {
            bool show = UIGuard.Try("Panel.ReadSpeedControlsSetting",
                () => UIOverhaulSettingsFile.Current.showSpeedControlsWidget, true,
                "The corner's speed controls are shown, which is the vanilla behavior.");

            if (show)
                return true;

            UIGuard.Try("Panel.SpeedControlShortcuts", () =>
                {
                    Vector2 size = TimeControls.TimeButSize;

                    // Five buttons wide because that is what the method lays out; the width only has to be
                    // enough that nothing it draws wraps back onto the screen.
                    TimeControls.DoTimeControlsGUI(new Rect(OffScreen, OffScreen, size.x * 5f, size.y));
                },
                "The pause and speed keyboard shortcuts do not work while the speed controls are hidden.");

            return false;
        }
    }

    /// <summary>
    /// Hides the date block in the corner: the hour, the date and the season together.
    ///
    /// <b>One switch because it is one readout.</b> <c>DoDate</c> is a four line method that hands a rect to
    /// <c>DateReadout.DateOnGUI</c>, which draws all three lines and reports a single height for them
    /// (<c>48 + (SeasonLabelVisible ? 26 : 0)</c>). Splitting the season out would mean reimplementing that
    /// readout, including the longitude the hour and the season are both derived from, and a date readout that is
    /// subtly wrong about the day is a worse outcome than one that shows a line nobody asked for.
    /// </summary>
    [HarmonyPatch(typeof(GlobalControlsUtility), nameof(GlobalControlsUtility.DoDate))]
    public static class Patch_GlobalControlsUtility_DoDate
    {
        public static bool Prefix()
        {
            return UIGuard.Try("Panel.ReadDateSetting",
                () => UIOverhaulSettingsFile.Current.showDateWidget, true,
                "The corner's date is shown, which is the vanilla behavior.");
        }
    }

    /// <summary>
    /// Hides the real time clock in the corner.
    ///
    /// <b>The other half of one switch.</b> Vanilla only calls this when <c>Prefs.ShowRealtimeClock</c> is set,
    /// and <c>Patch_GlobalControls_PerformanceMeter</c> raises that flag for the duration of the corner's draw
    /// when the setting is ticked -- so that half covers "show it when vanilla would not". This half covers the
    /// other case, where the player has vanilla's preference on and has cleared our box, and without it the box
    /// would sit unticked above a clock that was still there.
    ///
    /// Skipping is clean here for the same reason it is for the date and the speed controls:
    /// <c>DoRealtimeClock</c> moves <c>curBaseY</c> itself, so not running it takes the space with it.
    /// </summary>
    [HarmonyPatch(typeof(GlobalControlsUtility), nameof(GlobalControlsUtility.DoRealtimeClock))]
    public static class Patch_GlobalControlsUtility_DoRealtimeClock
    {
        public static bool Prefix()
        {
            return UIGuard.Try("Panel.ReadRealtimeClockSetting",
                () => UIOverhaulSettingsFile.Current.showTimeWidget, true,
                "The corner's real time clock follows RimWorld's own preference, which is the vanilla behavior.");
        }
    }
}
