using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Draws the shadow behind a window as flat layered rings instead of vanilla's soft atlas.
    ///
    /// <b>This seam is the only place a shadow can be drawn at all.</b> A window's own contents are inside a
    /// clip group the size of its rect, so nothing drawn from <c>DoWindowContents</c> can reach outside it --
    /// which is where a shadow has to go. <c>WindowStack.WindowStackOnGUI</c> calls
    /// <c>Widgets.DrawShadowAround</c> for each window just before drawing it, outside that group and already
    /// in the right z-order, so replacing that call gets the position and the layering for free.
    ///
    /// <b>Rings rather than a blurred image.</b> Vanilla draws a nine-sliced <c>UI/Widgets/DropShadow</c>
    /// texture, which is soft and light and disappears against dark terrain. Everything else this mod draws is
    /// flat with hard edges, and a stack of one-pixel boxes at falling opacity reads as depth without bringing
    /// a gradient into a theme that has none. It also needs no texture, so it cannot be affected by whatever a
    /// palette does or does not ship.
    ///
    /// <b>Which windows get one is left exactly as it was.</b> This changes how the shadow looks, never
    /// whether it is drawn -- vanilla's own gate on <c>shadowAlpha</c> still decides that, and the ambient
    /// <c>GUI.color</c> it sets for that alpha is multiplied in below. A window that had no shadow still has
    /// none, and tabs keep the one they have; unlike the border, a shadow around a tab is separation from the
    /// map rather than a box drawn around something that is not one.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawShadowAround))]
    public static class Patch_Widgets_DrawShadowAround
    {
        /// <summary>How far the shadow reaches. Vanilla's atlas spreads 9; this is a little wider and softer.</summary>
        private const int Rings = 10;

        /// <summary>Opacity of the innermost ring. Every ring outward falls away from this.</summary>
        private const float NearAlpha = 0.30f;

        // There is deliberately no downward offset, and that is a correction rather than a preference.
        //
        // Vanilla shifts its shadow two pixels down and right to imply a light source, which works because its
        // shadow is one continuous nine-sliced image: shifting it leaves the whole band intact, just off-center.
        // These are discrete one-pixel rings, and shifting the rect they expand from moves the innermost ring
        // two pixels clear of the window on the offset sides while burying it under the window on the other
        // two. The result was a shadow visibly detached from the right and bottom edges.
        //
        // Concentric rings touch the window on all four sides by construction. A directional shadow would need
        // the four sides drawn separately with their own thicknesses, each still starting at the edge -- worth
        // doing if the flat look ever wants it, but not by moving the rect.

        public static bool Prefix(Rect rect)
        {
            return !UIGuard.Try("Framework.WindowShadow", () => Paint(rect),
                "Windows are drawn with RimWorld's own drop shadow.");
        }

        private static void Paint(Rect rect)
        {
            // Repaint only. The rings are pure decoration and hit nothing, so drawing them during layout or
            // an input event is wasted work several times a frame.
            if (Event.current.type != EventType.Repaint)
                return;

            Color previous = GUI.color;

            // Multiplied in rather than replaced: WindowStackOnGUI sets GUI.color to the window's shadowAlpha
            // before calling this, and that is how a window asks for a fainter shadow or none.
            float ambient = previous.a;

            if (ambient <= 0f)
                return;

            for (int i = 0; i < Rings; i++)
            {
                // Squared falloff, so the ring against the window is distinctly darker than the ones past it.
                // A linear ramp over this many rings reads as a grey outline rather than as a shadow.
                float t = 1f - i / (float) Rings;
                float alpha = NearAlpha * t * t * ambient;

                GUI.color = new Color(0f, 0f, 0f, alpha);

                // Expanded by i + 1 rather than i: DrawBox strokes inside the rect it is given, so this puts
                // the first ring's edge on the pixel immediately outside the window, with none of it hidden
                // underneath.
                Widgets.DrawBox(rect.ContractedBy(-(i + 1)), 1);
            }

            GUI.color = previous;
        }
    }
}
