using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Commands
{
    /// <summary>
    /// Draws command buttons in this mod's theme instead of RimWorld's.
    ///
    /// <b>Every gizmo in the game goes through here,</b> which is the point and also the risk: these buttons are
    /// how a player does almost everything. So the replacement is guarded rather than trusted. If our drawing
    /// throws even once, <see cref="UIGuard.Replaced"/> reports it and the prefix lets RimWorld draw the button
    /// its own way, for that gizmo and every one after it. A theme is never worth losing the commands.
    ///
    /// <b>Only <c>Command</c> is covered.</b> A mod that writes a <c>Gizmo</c> from scratch rather than deriving
    /// from <c>Command</c> keeps its own appearance and sits beside ours. Reskinning those would mean guessing at
    /// drawing code we have never seen.
    /// </summary>
    [HarmonyPatch(typeof(Command), "GizmoOnGUIInt")]
    internal static class Patch_CommandGizmo
    {
        [HarmonyPrefix]
        public static bool Prefix(Command __instance, Rect butRect, GizmoRenderParms parms,
            ref GizmoResult __result)
        {
            if (!Wanted() || !CommandPainter.Available)
                return true;

            GizmoResult drawn = default;

            // Replaced rather than Try, because this stands in for RimWorld's own drawing: a failure has to fall
            // back to it rather than leave the player with no button at all.
            //
            // <b>Its return value is already what a prefix returns</b>, so it is passed straight through: false
            // when we drew, true to let RimWorld draw. This was written negated, which inverted the whole patch,
            // and it shipped that way in 14123. On success it ran vanilla's drawing on top of ours, and on
            // failure it suppressed vanilla and returned a default GizmoResult, which is the one outcome the
            // guard exists to prevent.
            if (UIGuard.Replaced("Gizmos.Draw", () => drawn = CommandPainter.Draw(__instance, butRect, parms),
                    "Command buttons are drawn RimWorld's own way."))
            {
                return true;
            }

            __result = drawn;

            return false;
        }

        private static bool Wanted()
        {
            return UIGuard.Try("Gizmos.ReadSetting",
                () => UIOverhaulSettingsFile.Current?.restyleCommandButtons ?? true, true, null);
        }
    }
}
