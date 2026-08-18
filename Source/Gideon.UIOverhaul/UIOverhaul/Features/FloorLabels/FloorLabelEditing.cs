using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// The mode that makes labels clickable, and the click that opens the editor.
    ///
    /// <b>A mode is what makes intercepting the click safe.</b> A label sits on open floor, which is where a
    /// click places a building, starts a box selection, or picks something up -- so a label that is always
    /// clickable is a hit box fighting three existing ones, and the failure is a blueprint that silently refuses
    /// to go down. Off, nothing here touches input at all. On, the player has said what they are doing.
    ///
    /// <b>In Play Settings rather than in the options window,</b> because that row is where RimWorld already
    /// keeps "what am I looking at and what can I touch" toggles, and it is one click from the map instead of
    /// three from a menu. It also means the mode is visible: the icon is lit while it is on.
    /// </summary>
    internal static class FloorLabelEditing
    {
        /// <summary>
        /// Whether clicking a label opens its editor.
        ///
        /// <b>Deliberately not saved.</b> It is a mode somebody is in for a minute while naming rooms, not a
        /// preference -- and one that silently persisted across a restart would leave labels stealing clicks in
        /// a session where nobody had asked for it.
        /// </summary>
        internal static bool Active;

        /// <summary>Opens the labels window focused on whatever was clicked.</summary>
        private static void Open(FloorLabelHit hit)
        {
            UIGuard.Try("FloorLabels.OpenFromMap", () =>
            {
                Find.WindowStack.Add(new Dialog_FloorLabels(hit));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }, "The floor labels window did not open.");
        }

        /// <summary>
        /// Finds the label under the cursor, if editing is on and there is one.
        ///
        /// Searched newest first, so the label drawn last -- and therefore on top -- wins where two overlap.
        /// </summary>
        private static bool TryHit(out FloorLabelHit found)
        {
            found = default(FloorLabelHit);

            if (!Active || !GameComponent_FloorLabels.Enabled)
                return false;

            Vector3 mouse = UI.MouseMapPosition();

            for (int i = FloorLabelDrawer.Hits.Count - 1; i >= 0; i--)
            {
                FloorLabelHit hit = FloorLabelDrawer.Hits[i];

                if (!hit.Contains(mouse.x, mouse.z))
                    continue;

                found = hit;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds the toggle to RimWorld's own row of map controls.
        ///
        /// Hidden entirely when the feature is switched off, rather than shown disabled: an icon for something
        /// that cannot happen is worse than no icon, and the row is already crowded.
        /// </summary>
        [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
        internal static class Patch_PlaySettings_LabelEditing
        {
            public static void Postfix(WidgetRow row, bool worldView)
            {
                if (worldView || row == null || !GameComponent_FloorLabels.Enabled)
                    return;

                UIGuard.Try("FloorLabels.PlaySettingsToggle", () =>
                {
                    row.ToggleableIcon(ref Active, TexButton.Rename,
                        "Edit floor labels.\n\nWhile this is on, clicking a room name on the floor opens its "
                        + "editor. While it is off, the labels ignore clicks entirely.",
                        SoundDefOf.Mouseover_ButtonToggle);
                }, null);
            }
        }

        /// <summary>
        /// Takes the click before the map does, but only when there is a label under it.
        ///
        /// <b>A prefix that returns false only on a genuine hit.</b> Every other click -- including every click
        /// while the mode is off -- falls straight through to vanilla untouched, which is the property that makes
        /// this acceptable at all. The event is consumed as well as the method skipped, or the same press would
        /// be handled again by whatever runs next.
        /// </summary>
        [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
        internal static class Patch_MapInterface_HandleMapClicks
        {
            public static bool Prefix()
            {
                if (!Active)
                    return true;

                Event current = Event.current;

                if (current == null || current.type != EventType.MouseDown || current.button != 0)
                    return true;

                // A designator is somebody mid-placement, and taking that click would be the exact failure this
                // mode exists to avoid.
                if (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null)
                    return true;

                // Hand written rather than wrapped in UIGuard, because an out parameter cannot cross a lambda
                // and the alternative was testing twice. Returning true on any failure is the safe answer: the
                // click goes to the map, which is what would have happened anyway.
                try
                {
                    FloorLabelHit hit;

                    if (!TryHit(out hit))
                        return true;

                    Open(hit);
                    current.Use();

                    return false;
                }
                catch (System.Exception ex)
                {
                    UIGuard.Report("FloorLabels.HitTest", ex,
                        "Clicking a floor label does not open its editor. Nothing else is affected.");

                    return true;
                }
            }
        }
    }
}
