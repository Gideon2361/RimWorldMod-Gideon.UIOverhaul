using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Shapes IMGUI has no primitive for.
    ///
    /// Unity's GUI can fill a rectangle and nothing else, so anything round has to be a texture. These are
    /// generated rather than shipped as art files for two reasons: a generated one is tinted to any palette
    /// role at draw time, and there is no file to go missing or to be overridden by another mod's texture of
    /// the same path.
    ///
    /// Built in a static constructor under <see cref="StaticConstructorOnStartupAttribute"/>, because a
    /// Texture2D has to be created on the main thread and a lazy build is only main-thread by luck of who
    /// draws first.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class UIShapes
    {
        /// <summary>
        /// Authored size. Larger than anything draws at -- a radio button is 24px -- so downscaling is what
        /// smooths the edge, which bilinear filtering does better than any coverage math at final size.
        /// </summary>
        private const int Resolution = 128;

        /// <summary>
        /// A white filled circle, alpha only at the edge. Tint it with <c>GUI.color</c>.
        ///
        /// A filled disc rather than a ring, because a ring of one thickness is no use to the next caller
        /// that wants a different one. Concentric discs in different colors make any ring, which is how
        /// <see cref="UIElementPainter.PaintRadioButton"/> draws one.
        /// </summary>
        internal static readonly Texture2D Disc;

        /// <summary>
        /// The inverse of <see cref="Disc"/>: opaque everywhere except an inscribed circle, which is
        /// transparent.
        ///
        /// A stencil for making something square look round. IMGUI cannot mask one texture with another, so a
        /// circular crop is done the other way about: draw the square image, then draw this over it in the color
        /// of whatever is behind, and the corners are painted out. The work tab's pawn portraits are circular
        /// this way -- a RenderTexture from PortraitsCache is square and there is no shader to clip it with.
        ///
        /// Its alpha ramp is the complement of the disc's, so the two meet without a seam and the resulting edge
        /// is as smooth as the disc's own.
        /// </summary>
        internal static readonly Texture2D DiscCutout;

        /// <summary>
        /// Diagonal stripes, tileable, white where a stripe is and transparent between. Tint it with
        /// <c>GUI.color</c> and draw it with tex coords scaled to the rect, so the stripes keep one pitch
        /// whatever they are drawn across.
        ///
        /// The pitch is what makes this a tile rather than a stretched banner: a wash that stretches lands
        /// eight fat stripes on a row and one on a narrow cell, and the two stop looking like the same
        /// marking.
        /// </summary>
        internal static readonly Texture2D Stripes;

        /// <summary>Side of the stripe tile in pixels, which is also the distance it repeats over.</summary>
        internal const float StripePitch = 32f;

        static UIShapes()
        {
            Disc = BuildDisc(false);
            DiscCutout = BuildDisc(true);
            Stripes = BuildStripes();
        }

        /// <param name="inverted">
        /// True builds the stencil: the circle transparent and everything outside it opaque.
        /// </param>
        private static Texture2D BuildDisc(bool inverted)
        {
            Texture2D texture = new Texture2D(Resolution, Resolution, TextureFormat.ARGB32, false)
            {
                // Clamped so the edge pixels do not sample across to the far side of the texture and leave a
                // faint seam when the disc is drawn smaller than it was authored.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = inverted ? "Gideon.UIFramework.DiscCutout" : "Gideon.UIFramework.Disc"
            };

            Color32[] pixels = new Color32[Resolution * Resolution];

            const float center = Resolution * 0.5f;
            const float radius = center - 0.5f;

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;

                    // One pixel of falloff at the rim: coverage rather than a hard cut, so the circle has a
                    // clean edge at authored size as well as when scaled.
                    float coverage = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);

                    if (inverted)
                        coverage = 1f - coverage;

                    pixels[y * Resolution + x] = new Color32(255, 255, 255, (byte) (coverage * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return texture;
        }

        private static Texture2D BuildStripes()
        {
            int side = (int) StripePitch;

            Texture2D texture = new Texture2D(side, side, TextureFormat.ARGB32, false)
            {
                // Repeat, not clamp: this one is tiled rather than fitted, which is the whole point of it.
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "Gideon.UIFramework.Stripes"
            };

            Color32[] pixels = new Color32[side * side];

            // Half stripe, half gap, running down to the right. The diagonal is x + y so the pattern is
            // continuous across tile edges, which a shape drawn inside the tile would not be.
            const float band = StripePitch * 0.5f;

            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    float position = (x + y) % StripePitch;

                    // Distance to the nearest edge of the stripe, which gives a one pixel ramp at each of
                    // them. A hard edge on a diagonal is a staircase, and tiling magnifies it into a seam.
                    float coverage = position < band
                        ? Mathf.Clamp01(Mathf.Min(position, band - position))
                        : 0f;

                    pixels[y * side + x] = new Color32(255, 255, 255, (byte) (coverage * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return texture;
        }
    }
}
