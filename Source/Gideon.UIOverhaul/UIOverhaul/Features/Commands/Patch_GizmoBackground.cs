using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Commands
{
    /// <summary>
    /// Puts this mod's panel behind gizmos nothing else here draws -- which in practice means other mods'.
    ///
    /// <b>This replaces a texture, not a mod's drawing code.</b> That distinction is the whole design.
    /// <see cref="Patch_CommandGizmo"/> declines to reskin third party gizmos because reimplementing drawing we
    /// have never seen means breaking it the next time its author changes it, and that reasoning still holds. But
    /// every gizmo in the game, vanilla and modded alike, paints its background with one call --
    /// <c>GenUI.DrawTextureWithMaterial(rect, Command.BGTex, material)</c> -- and swapping what that one call
    /// paints touches nobody's logic. Their label, their icons, their portraits and their clicks all draw on top
    /// afterwards, exactly as they did, and keep working when the mod updates.
    ///
    /// <b>Found through One with Death's necromancer controls,</b> which sat in a beveled stone tablet among our
    /// flat panels the same way the mechanitor's did. Reported on 2026-08-25. It is fixed here rather than in a
    /// patch aimed at that mod, because a fix aimed at one mod would have to be written again for the next one.
    ///
    /// <b>It cannot double up with the two painters.</b> Both of those are prefixes that return false, so vanilla
    /// never reaches its own background call for a <c>Command</c> or for the mechanitor's group. This runs only
    /// where neither of them did.
    ///
    /// <b>The texture test is a reference compare against two statics,</b> which is as tight a guard as this
    /// method can be given.
    ///
    /// <b>It catches one thing beyond gizmos, and deliberately.</b> <c>ColonistBar.BGTex</c> is not a texture of
    /// its own -- it is assigned <c>Command.BGTex</c>, the same object -- so the pawn tiles in Ideology's ritual
    /// role selection come through here as well. Excluding them is not possible at this seam, since there is
    /// nothing left to tell them apart by, and it is not obviously desirable either: those tiles are the last
    /// place in the game still wearing RimWorld's beveled frame, and this mod exists to replace exactly that.
    /// Named here so it is a decision on the record rather than a surprise. If it turns out wrong, the fix is a
    /// separate patch on <c>PawnRoleSelectionWidgetBase</c>, not a guess about rect sizes here.
    /// </summary>
    [HarmonyPatch(typeof(GenUI), nameof(GenUI.DrawTextureWithMaterial))]
    internal static class Patch_GizmoBackground
    {
        [HarmonyPrefix]
        public static bool Prefix(Rect rect, Texture texture, Material material)
        {
            if (texture == null)
                return true;

            if (texture != Command.BGTex && texture != Command.BGTexShrunk)
                return true;

            if (!Wanted())
                return true;

            // Replaced rather than Try: a failure has to let RimWorld paint the background it was going to,
            // because a gizmo with no background at all is worse than one in the wrong style.
            return UIGuard.Replaced("Gizmos.Background", () => Draw(rect, material),
                "Other mods' command buttons keep RimWorld's own background.");
        }

        /// <summary>
        /// The same panel a command button gets, so a modded gizmo sits in the row rather than on it.
        ///
        /// <b>A material means the game wanted this drawn down.</b> Vanilla passes <c>TexUI.GrayscaleGUI</c> for a
        /// disabled or low light gizmo and null otherwise, so its presence is the only signal we have about state
        /// here -- there is no command object to ask. Sunken and unhovered is the honest reading of it.
        ///
        /// <b>The caller's <c>GUI.color</c> is carried into the fill.</b> Vanilla dims a low light gizmo by
        /// setting that before the call rather than by changing the texture, and a painter that sets its own
        /// colors would throw the dimming away and leave a modded gizmo at full strength behind a dark overlay.
        /// </summary>
        private static void Draw(Rect rect, Material material)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            bool down = material != null;
            bool over = !down && Mouse.IsOver(rect);

            Color fill = down
                ? palette.SurfaceSunken
                : over
                    ? palette.SurfaceRaised
                    : palette.PanelBackground;

            Color edge = over ? palette.Accent : palette.Border;

            float alpha = GUI.color.a;

            if (alpha < 1f)
            {
                fill = new Color(fill.r, fill.g, fill.b, fill.a * alpha);
                edge = new Color(edge.r, edge.g, edge.b, edge.a * alpha);
            }

            UIElementPainter.OutlineRounded(rect, edge, fill);
        }

        private static bool Wanted()
        {
            return UIGuard.Try("Gizmos.Background.ReadSetting",
                () => UIOverhaulSettingsFile.Current?.restyleCommandButtons ?? true, true, null);
        }
    }
}
