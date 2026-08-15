using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Panel
{
    /// <summary>
    /// Skips vanilla's row of play settings toggles in the corner when the player has switched it off.
    ///
    /// <b>The seam is the row, not the column.</b> <c>GlobalControls.GlobalControlsOnGUI</c> draws the whole bottom
    /// right in one method, and taking it over to hide one row would mean owning the date, the weather, the
    /// temperature, the conditions and the counters as well. <c>GlobalControlsUtility.DoPlaySettings</c> is the one
    /// call that draws exactly the row in question, so a prefix on it changes one thing and leaves the rest of the
    /// column entirely vanilla.
    ///
    /// <b>Skipping does not advance the layout cursor, and that is correct rather than an oversight.</b>
    /// <c>curBaseY</c> is passed by reference and moved up by whatever each row consumes; not moving it means every
    /// readout below simply occupies the space the toggles would have taken. Advancing it anyway would leave a row
    /// shaped hole in the middle of the column, which is what hiding a row should never look like.
    ///
    /// <b>No stand-down against another mod here.</b> This suppresses a call rather than replacing a drawing, so a
    /// mod that restyles the same row still gets to decide how the row looks; it just is not asked to draw one.
    ///
    /// <b>This is the fallback path now.</b> <see cref="GlobalControlsPanel"/> replaced
    /// <c>GlobalControlsOnGUI</c> and calls <c>DoPlaySettingsGlobalControls</c> itself, so
    /// <c>GlobalControlsUtility.DoPlaySettings</c> is not reached while the panel is drawing and this prefix does
    /// not fire. It is kept for the case where the panel stands down and vanilla's corner comes back, which is
    /// the one path where this setting would otherwise stop working. The panel applies the same rule, including
    /// running the row off screen so the three keyboard shortcuts survive being hidden.
    /// </summary>
    [HarmonyPatch(typeof(GlobalControlsUtility), nameof(GlobalControlsUtility.DoPlaySettings))]
    public static class Patch_GlobalControlsUtility_DoPlaySettings
    {
        public static bool Prefix(WidgetRow rowVisibility, bool worldView, ref float curBaseY)
        {
            // Read through the guard because a settings file that cannot be read must not stop the corner
            // drawing -- the fallback is the vanilla behavior of showing it.
            bool show = UIGuard.Try("Panel.ReadGlobalControlsSetting",
                () => UIOverhaulSettingsFile.Current.showGlobalControlsWidget, true,
                "The corner's toggle row is shown, which is the vanilla behavior.");

            if (show)
                return true;

            // Aimed off screen rather than skipped, for the same reason as the speed controls: DoMapControls
            // draws the toggles and then handles three keyboard shortcuts in the same pass -- beauty display,
            // room stats, and map search. Not calling it took all three away from anyone who hid the row, which
            // is not what hiding a row of buttons should mean.
            UIGuard.Try("Panel.PlaySettingShortcuts", () =>
                {
                    // No wrapping: one long row nobody sees is cheaper than reasoning about where it wraps.
                    rowVisibility.Init(Patch_GlobalControlsUtility_DoTimespeedControls.OffScreen,
                        Patch_GlobalControlsUtility_DoTimespeedControls.OffScreen, UIDirection.RightThenDown);

                    Find.PlaySettings.DoPlaySettingsGlobalControls(rowVisibility, worldView);
                },
                "The beauty, room stats and map search keyboard shortcuts do not work while the corner's "
                + "toggle row is hidden.");

            // curBaseY untouched, so the readouts above reclaim the row's space.
            return false;
        }
    }
}
