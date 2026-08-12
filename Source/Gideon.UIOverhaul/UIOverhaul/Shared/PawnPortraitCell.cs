using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// A colonist's face in a round frame, clickable to center the view on them.
    ///
    /// Shared by the work tab and the pawns tab. Not a framework control: it knows what a Pawn is, what the
    /// camera is, and that clicking should close the tab -- none of which belongs in a reusable UI control, and
    /// none of which another mod would want decided for it.
    /// </summary>
    internal static class PawnPortraitCell
    {
        /// <summary>Side of the round frame. Both tabs draw it at the same size, so rows line up between them.</summary>
        public const float Size = 46f;

        /// <summary>
        /// How far up the body the portrait camera looks, and how far in.
        ///
        /// PortraitsCache takes both, which is what makes a face crop possible without any patching. The offset
        /// lifts the camera to head height; the zoom then fills the frame with it. Values are in world units, so
        /// they hold for any pawn size.
        /// </summary>
        private static readonly Vector3 FaceOffset = new Vector3(0f, 0f, 0.34f);

        private const float FaceZoom = 2.1f;

        /// <summary>
        /// Draws the portrait and requests a camera jump if it was clicked.
        /// </summary>
        /// <param name="behind">
        /// The color the row shows behind the portrait, which the circular crop has to paint with. A caller
        /// whose row carries a wash has to pass the average of it rather than the flat card color -- see the
        /// crop note below.
        /// </param>
        public static void Draw(Rect frame, Pawn pawn, UIColorPaletteDef palette, Color behind)
        {
            Color previous = GUI.color;

            bool over = Mouse.IsOver(frame);

            // A sunken disc behind the render. The render is transparent everywhere the pawn is not, so without
            // this the head sits on the bare card; with it, on something.
            //
            // It is also the hover feedback, because it is the only part of the portrait that can carry any: the
            // face itself is a RenderTexture that must be drawn untinted, and the ring of disc left visible
            // around it is what is left to change. Lerped rather than set to Accent outright -- this is a hint
            // that something is clickable, not a selection.
            GUI.color = over ? Color.Lerp(palette.SurfaceSunken, palette.Accent, 0.45f) : palette.SurfaceSunken;
            GUI.DrawTexture(frame, UIShapes.Disc);
            GUI.color = previous;

            // Framed on the face: the camera is lifted to head height and zoomed in, which is what the
            // cameraOffset and cameraZoom parameters exist for. A full-body render at 46px is a silhouette.
            RenderTexture face = PortraitsCache.Get(pawn, new Vector2(Size, Size), Rot4.South, FaceOffset,
                FaceZoom);

            if (face != null)
                GUI.DrawTexture(frame, face);

            // Cropped to a circle, the only way IMGUI can: the square render is drawn, then everything outside
            // an inscribed circle is painted over in the color behind it. There is no masking in IMGUI and no
            // shader to clip a RenderTexture with, so the crop is done by covering rather than by clipping --
            // which is why the caller has to say what color the row shows there.
            GUI.color = behind;
            GUI.DrawTexture(frame, UIShapes.DiscCutout);
            GUI.color = previous;

            // Unconditional, so this control's id cannot come and go between frames. A conditional control is
            // how a neighbor's id gets shifted, which is a fault worth not reintroducing anywhere.
            if (Widgets.ButtonInvisible(frame))
                PawnCameraJump.Request(pawn);
        }

        /// <summary>Whether the cursor is over the portrait, for a caller composing one tooltip for a whole row.</summary>
        public static bool IsOver(Rect frame)
        {
            return Mouse.IsOver(frame);
        }
    }
}
