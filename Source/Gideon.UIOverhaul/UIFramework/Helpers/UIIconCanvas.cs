using UnityEngine;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// A small pixel buffer that icons are drawn into with filled primitives, then baked once into a texture.
    ///
    /// <b>Why the mod draws icons instead of shipping them.</b> A generated mask is a white shape in an alpha
    /// channel, so it takes whatever color it is drawn with -- which means one icon is the danger red on the dark
    /// theme and the correct red on a light one, where a PNG would need a variant per theme or would simply be
    /// wrong on one of them. It also costs no files, and the shapes stay editable as code rather than as art
    /// somebody has to open a paint program to change.
    ///
    /// <b>Filled shapes only, and that is the medium rather than a limitation.</b> There is no stroke here because
    /// there is no stroking: a line is a filled capsule, an outline is a shape with a smaller shape cut out of it.
    /// That is what a rasterizer does naturally, and it avoids the sub-pixel stroke problems that make hand-drawn
    /// icons look soft at the sizes these draw at.
    ///
    /// <b>Coverage, not sampling.</b> Every primitive is expressed as a signed distance -- positive inside, negative
    /// outside -- and a pixel's alpha is how much of it the shape covers, taken as <c>distance + 0.5</c> clamped.
    /// One evaluation per pixel gives a clean edge at any size, where supersampling would cost several evaluations
    /// for a worse result. It is also why every shape antialiases the same way without each one having to say so.
    ///
    /// <b>Authored in a fixed 32 unit square, baked at whatever resolution is asked for.</b> Icon code reads in
    /// round numbers and does not have to change if the baked size does. Bake larger than the icon draws: these are
    /// bilinear, so a 64px mask drawn at 16px is smooth, while a 16px mask drawn at 20px is not.
    ///
    /// <code>
    /// Texture2D bell = new UIIconCanvas(64)
    ///     .Disc(16f, 13f, 9f)
    ///     .Rect(7f, 13f, 18f, 7f)
    ///     .Rect(4f, 20f, 24f, 3f)
    ///     .Disc(16f, 27f, 3f)
    ///     .ToTexture("Gideon.Icon.Bell");
    /// </code>
    /// </summary>
    internal sealed class UIIconCanvas
    {
        /// <summary>The side of the square icons are authored in. Coordinates below are all in these units.</summary>
        internal const float Units = 32f;

        private readonly int width;
        private readonly int height;
        private readonly float scale;
        private readonly float[] coverage;

        /// <summary>A square icon, authored in a 32 by 32 space.</summary>
        internal UIIconCanvas(int size) : this(size, size)
        {
        }

        /// <summary>
        /// A rectangular icon, for a slot that is not square.
        ///
        /// <b>The scale stays uniform and the authoring space gets shorter instead.</b> Stretching the units to fit
        /// would bake a distorted mask, which is the bug this exists to avoid: a 32 by 32 glyph baked square and then
        /// drawn into a 32 by 24 button is squashed by a quarter, and circles come out as ellipses. Here the width is
        /// always 32 units and the height is whatever that scale makes it -- so a 64 by 48 texture is authored in 32
        /// by 24 and drawn into a 32 by 24 button at its true proportions.
        ///
        /// <see cref="HeightUnits"/> is the vertical extent to author within, rather than a number to work out.
        /// </summary>
        internal UIIconCanvas(int width, int height)
        {
            this.width = Mathf.Max(1, width);
            this.height = Mathf.Max(1, height);

            scale = this.width / Units;
            coverage = new float[this.width * this.height];
        }

        /// <summary>How tall this canvas is in authored units. 32 for a square one.</summary>
        internal float HeightUnits => height / scale;

        // ---------------------------------------------------------------------------------------
        // Primitives
        //
        // Each returns the canvas so shapes chain, and each is one call to Paint with a distance function. Adding a
        // shape means adding a distance function, not another rasterizer.
        // ---------------------------------------------------------------------------------------

        internal UIIconCanvas Disc(float cx, float cy, float radius)
        {
            return Paint((x, y) => radius - Distance(x - cx, y - cy), false);
        }

        internal UIIconCanvas CutDisc(float cx, float cy, float radius)
        {
            return Paint((x, y) => radius - Distance(x - cx, y - cy), true);
        }

        /// <summary>A ring of the given thickness, centered on the radius.</summary>
        internal UIIconCanvas Ring(float cx, float cy, float radius, float thickness)
        {
            float half = thickness * 0.5f;

            return Paint((x, y) => half - Mathf.Abs(Distance(x - cx, y - cy) - radius), false);
        }

        internal UIIconCanvas Rect(float x0, float y0, float width, float height)
        {
            return Paint((x, y) => RectDistance(x, y, x0, y0, width, height), false);
        }

        internal UIIconCanvas CutRect(float x0, float y0, float width, float height)
        {
            return Paint((x, y) => RectDistance(x, y, x0, y0, width, height), true);
        }

        /// <summary>
        /// A line as a capsule: the set of points within half its thickness of the segment.
        ///
        /// Rounded ends rather than square, because at these sizes a butt end on a diagonal reads as a notch. Where
        /// a square end is wanted, a rect is the shape to use.
        /// </summary>
        internal UIIconCanvas Line(float x0, float y0, float x1, float y1, float thickness)
        {
            float half = thickness * 0.5f;

            return Paint((x, y) => half - SegmentDistance(x, y, x0, y0, x1, y1), false);
        }

        internal UIIconCanvas Triangle(float ax, float ay, float bx, float by, float cx, float cy)
        {
            return Paint((x, y) => TriangleDistance(x, y, ax, ay, bx, by, cx, cy), false);
        }

        internal UIIconCanvas CutTriangle(float ax, float ay, float bx, float by, float cx, float cy)
        {
            return Paint((x, y) => TriangleDistance(x, y, ax, ay, bx, by, cx, cy), true);
        }

        // ---------------------------------------------------------------------------------------
        // Rasterizing
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Evaluates a distance function over every pixel and combines it into the buffer.
        ///
        /// Adding takes the greater coverage rather than summing, so overlapping shapes join into one silhouette
        /// instead of building up a brighter seam where they cross. Cutting takes the lesser of what is there and
        /// what is outside the shape, which is the same idea inverted.
        /// </summary>
        private UIIconCanvas Paint(System.Func<float, float, float> distance, bool cut)
        {
            for (int py = 0; py < height; py++)
            {
                // Authored top down, like every other coordinate system in this UI. Unity's textures are bottom up,
                // so the row is flipped here rather than in every icon.
                float y = (height - 1 - py + 0.5f) / scale;

                for (int px = 0; px < width; px++)
                {
                    float x = (px + 0.5f) / scale;

                    // Distances are in authored units; the half pixel of falloff has to be in them too, or icons
                    // baked at a higher resolution would get a proportionally softer edge.
                    float shape = Mathf.Clamp01(distance(x, y) * scale + 0.5f);

                    int i = py * width + px;

                    coverage[i] = cut
                        ? Mathf.Min(coverage[i], 1f - shape)
                        : Mathf.Max(coverage[i], shape);
                }
            }

            return this;
        }

        /// <summary>
        /// Bakes the buffer into a white texture whose alpha is the coverage.
        ///
        /// White rather than colored for the reason this class exists: the icon is a mask, and the color comes from
        /// whatever palette role it is drawn with at the call site.
        /// </summary>
        internal Texture2D ToTexture(string name)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false)
            {
                // The same two settings the shared shapes use, for the same reasons: clamped so an edge pixel does
                // not sample across to the far side, bilinear so the mask stays clean when drawn smaller than baked.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };

            Color32[] pixels = new Color32[coverage.Length];

            for (int i = 0; i < coverage.Length; i++)
                pixels[i] = new Color32(255, 255, 255, (byte) (Mathf.Clamp01(coverage[i]) * 255f));

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return texture;
        }

        // ---------------------------------------------------------------------------------------
        // Distance functions
        //
        // Positive inside the shape, negative outside, measured in authored units.
        // ---------------------------------------------------------------------------------------

        private static float Distance(float dx, float dy) => Mathf.Sqrt(dx * dx + dy * dy);

        private static float RectDistance(float x, float y, float x0, float y0, float width, float height)
        {
            // The nearest edge decides. Inside, every term is positive and the smallest is the distance to the wall
            // you would hit first; outside, at least one is negative and the most negative is how far out you are.
            return Mathf.Min(Mathf.Min(x - x0, x0 + width - x), Mathf.Min(y - y0, y0 + height - y));
        }

        private static float SegmentDistance(float x, float y, float x0, float y0, float x1, float y1)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;

            float lengthSquared = dx * dx + dy * dy;

            // A zero length segment is a point, and projecting onto it would divide by zero.
            float t = lengthSquared <= 0f
                ? 0f
                : Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / lengthSquared);

            return Distance(x - (x0 + t * dx), y - (y0 + t * dy));
        }

        /// <summary>
        /// Distance to a triangle, treating it as the intersection of three half planes.
        ///
        /// Correct for any triangle regardless of which way round its points were given: the winding is measured
        /// from the points themselves and the edge tests are flipped to match, so an icon author does not have to
        /// remember a convention that would fail silently by rendering nothing.
        /// </summary>
        private static float TriangleDistance(float x, float y, float ax, float ay, float bx, float by,
            float cx, float cy)
        {
            float winding = Cross(ax, ay, bx, by, cx, cy) < 0f ? -1f : 1f;

            return Mathf.Min(Mathf.Min(
                    EdgeDistance(x, y, ax, ay, bx, by) * winding,
                    EdgeDistance(x, y, bx, by, cx, cy) * winding),
                EdgeDistance(x, y, cx, cy, ax, ay) * winding);
        }

        private static float Cross(float ax, float ay, float bx, float by, float cx, float cy)
        {
            return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        }

        /// <summary>Perpendicular distance from a point to the infinite line through two points.</summary>
        private static float EdgeDistance(float x, float y, float x0, float y0, float x1, float y1)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;

            float length = Distance(dx, dy);

            return length <= 0f ? 0f : ((x - x0) * dy - (y - y0) * dx) / -length;
        }
    }
}
