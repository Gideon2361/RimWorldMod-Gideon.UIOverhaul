using System;
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

        /// <summary>
        /// The corner radius every rounded surface in this mod uses, in pixels at 1x UI scale.
        ///
        /// One number rather than a per-control choice. A window at one radius and a button at another do not
        /// read as two deliberate decisions, they read as a mistake -- and the whole reason this is shared is so
        /// a change here moves the window frame, the buttons, the dropdowns and the checkboxes together.
        ///
        /// <b>Not everything rounds.</b> The main button bar along the bottom stays square: its tabs sit in a
        /// continuous strip under an accent rule, and a curve there pulls each tab away from the rule above it
        /// and leaves a visible arc between them. See the rounding switch on
        /// <see cref="UIElementPainter.PaintButton"/>.
        /// </summary>
        internal const int CornerRadius = 4;

        /// <summary>
        /// A white rounded rectangle, built to be drawn through <c>Widgets.DrawAtlas</c>.
        ///
        /// <b>Sized for vanilla's own nine-slice rule, and the size is load bearing.</b> <c>DrawAtlas</c> takes
        /// the corner as <c>atlas.width * 0.25f</c>, so this texture must be exactly four radii across -- its
        /// dimensions are the radius. Reusing that method rather than slicing by hand also means the rounding
        /// lands on the same pixel grid as everything else the game draws, including under UI scaling.
        ///
        /// Tint it with <c>GUI.color</c>: the texture is white and carries its shape in the alpha.
        /// </summary>
        internal static readonly Texture2D RoundedRect;

        /// <summary>Side of the stripe tile in pixels, which is also the distance it repeats over.</summary>
        internal const float StripePitch = 32f;

        /// <summary>
        /// <b>Guarded, and for a reason particular to static constructors.</b> RimWorld already catches these, so a
        /// throw does not stop the game loading -- but the CLR then marks the type as failed, and every later read
        /// of one of these fields throws <c>TypeInitializationException</c> instead of returning a texture. Since
        /// these are read while drawing, that converts one startup fault into a fault on every frame thereafter.
        ///
        /// Catching it here leaves the fields null instead, which the drawing code treats as "no texture" and
        /// survives.
        ///
        /// Written out rather than through <c>UIGuard.Try</c> because these fields are <c>static readonly</c>, and
        /// C# only allows those to be assigned from the static constructor itself -- a lambda is a different method
        /// and would not compile. Every static constructor in this mod is written this way for that reason.
        /// </summary>
        static UIShapes()
        {
            try
            {
                Disc = BuildDisc(false);
                DiscCutout = BuildDisc(true);
                Stripes = BuildStripes();
                RoundedRect = BuildRoundedRect();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Framework.BuildShapes", ex,
                    "Rounded corners, circles and the striped fill are drawn as plain rectangles.");
            }
        }

        /// <summary>
        /// The rounded rectangle atlas.
        ///
        /// <b>Exactly four radii across, and that is not a detail to tune.</b> <c>Widgets.DrawAtlas</c> takes the
        /// corner size from <c>atlas.width * 0.25f</c> -- the texture's pixel width, with no reference to what
        /// the author intended. So the texture's size <i>is</i> the radius, and authoring it larger for a
        /// smoother curve silently multiplies the rounding by the same factor.
        ///
        /// That is exactly what went wrong the first time: built at eight times scale for a nicer edge, it drew
        /// a thirty-two pixel radius instead of four. Every button became a capsule, and worse, the radius then
        /// clamped to half the control's height -- which collapses the nine-slice's middle band to nothing and
        /// leaves the top and bottom corner rows meeting in a visible seam across the control.
        ///
        /// <b>Smoothness comes from supersampled coverage instead.</b> Each pixel's alpha is the fraction of it
        /// actually inside the shape, measured over a grid of subsamples, so a four pixel curve is as clean as
        /// the format allows without the texture being any bigger than the radius it encodes.
        ///
        /// Only the corners are shaped: the edges and the middle stay solid, which is what lets the nine-slice
        /// stretch it to any size without distorting the curve.
        /// </summary>
        private static Texture2D BuildRoundedRect()
        {
            // DrawAtlas reads the corner as a quarter of the width, so this is the radius and nothing else.
            const int size = CornerRadius * 4;
            const float radius = CornerRadius;

            // Subsamples per axis when measuring how much of a pixel is inside the shape.
            const int samples = 8;

            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                name = "Gideon.Shape.RoundedRect",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int inside = 0;

                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    float px = x + (sx + 0.5f) / samples;
                    float py = y + (sy + 0.5f) / samples;

                    // How far past the corner's centre this sample sits, on each axis independently. Zero
                    // anywhere along an edge or in the middle, which is what leaves those regions solid.
                    float dx = Mathf.Max(0f, Mathf.Max(radius - px, px - (size - radius)));
                    float dy = Mathf.Max(0f, Mathf.Max(radius - py, py - (size - radius)));

                    if (dx * dx + dy * dy <= radius * radius)
                        inside++;
                }

                pixels[y * size + x] = new Color(1f, 1f, 1f, inside / (float) (samples * samples));
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
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
