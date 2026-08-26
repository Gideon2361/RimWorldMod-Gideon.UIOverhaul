using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Commands
{
    /// <summary>
    /// Sends the mechanitor's control group gizmo through <see cref="MechGroupPainter"/>.
    ///
    /// <b>Same shape as <see cref="Patch_CommandGizmo"/> and for the same reasons.</b> Guarded with
    /// <see cref="UIGuard.Replaced"/> so a throw hands the drawing back to RimWorld rather than leaving a
    /// mechanitor with no way to command their mechs, and gated on the same setting, because one switch that
    /// restores every vanilla gizmo is more use than two that each restore some.
    ///
    /// <b>The type is declared in the base assembly, so patching it needs no Biotech check.</b> Without Biotech
    /// the gizmo is never constructed and this never runs; vanilla's own <c>CheckBiotech</c> call inside the
    /// method it replaces was a guard against a modded caller, and it is preserved by falling through to vanilla
    /// on any failure at all.
    /// </summary>
    [HarmonyPatch(typeof(MechanitorControlGroupGizmo), nameof(MechanitorControlGroupGizmo.GizmoOnGUI))]
    internal static class Patch_MechGroupGizmo
    {
        [HarmonyPrefix]
        public static bool Prefix(MechanitorControlGroupGizmo __instance, Vector2 topLeft, float maxWidth,
            GizmoRenderParms parms, ref GizmoResult __result)
        {
            if (!Wanted() || !MechGroupPainter.Available)
                return true;

            GizmoResult drawn = default;

            if (UIGuard.Replaced("Gizmos.MechGroup",
                    () => drawn = MechGroupPainter.Draw(__instance, topLeft, maxWidth, parms),
                    "Mech control groups are drawn RimWorld's own way."))
            {
                return true;
            }

            __result = drawn;

            return false;
        }

        private static bool Wanted()
        {
            return UIGuard.Try("Gizmos.MechGroup.ReadSetting",
                () => UIOverhaulSettingsFile.Current?.restyleCommandButtons ?? true, true, null);
        }
    }
}
