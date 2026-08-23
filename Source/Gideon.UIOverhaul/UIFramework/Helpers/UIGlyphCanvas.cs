using UnityEngine;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// A small coverage rasterizer for building icon textures in code.
    ///
    /// The same bargain <see cref="UIShapes"/> makes, one step further up. UIShapes generates the three
    /// shapes IMGUI has no primitive for; this generates whole glyphs out of those shapes, so an icon can be
    /// tinted to any palette role at draw time and there is no art file to go missing or to be shadowed by
    /// another mod's texture of the same path.
    ///
    /// <b>Coverage, not color.</b> Every glyph is white with an alpha ramp, exactly like UIShapes' output,
    /// because the caller supplies the color through <c>GUI.color</c>. Shapes combine by taking the greater
    /// coverage -- a union -- which is what makes a cloud out of overlapping discs without a seam where they
    /// meet. <see cref="Erase"/> is the inverse, for a bite taken out of a shape.
    ///
    /// <b>Authored top-down.</b> Coordinates are 0 to 1 with y increasing downward, which is how a glyph is
    /// natural to describe and how every other rect in the UI is laid out. The flip to Unity's bottom-up
    /// texture rows happens once, in <see cref="ToTexture"/>.
    ///
    /// <b>Authored large.</b> Glyphs are built at <see cref="Resolution"/> and drawn at 16 to 20 pixels, so
    /// the edge quality comes from bilinear downscaling rather than from coverage math at final size -- the
    /// same reason UIShapes authors its disc at 128.
    ///
    /// Build these from a static constructor under <c>StaticConstructorOnStartup</c>. A Texture2D has to be
    /// created on the main thread, and a lazy build is only main-thread by luck of who draws first.
    /// </summary>
    internal sealed class UIGlyphCanvas
    {
        /// <summary>Authored size, matching <see cref="UIShapes"/>.</summary>
        internal const int Resolution = 128;

        /// <summary>Coverage per pixel, row 0 at the top.</summary>
        private readonly float[] coverage = new float[Resolution * Resolution];

        /// <summary>
        /// Distance in pixels over which an edge fades from covered to not.
        ///
        /// One pixel, as UIShapes uses. Wider would blur a glyph that is already going to be downscaled by a
        /// factor of six or more; narrower would alias along the diagonals that bolts and rays are made of.
        /// </summary>
        private const float EdgeSoftness = 1f;

        /// <summary>Filled disc. Radii are in the same 0-1 units as the center.</summary>
        internal UIGlyphCanvas Disc(float cx, float cy, float radius)
        {
            return Ellipse(cx, cy, radius, radius);
        }

        /// <summary>Filled ellipse, for the shapes a circle cannot make -- an orbit seen at an angle.</summary>
        internal UIGlyphCanvas Ellipse(float cx, float cy, float radiusX, float radiusY)
        {
            return Paint((px, py) => EllipseCoverage(px, py, cx, cy, radiusX, radiusY, 0f), false);
        }

        /// <summary>
        /// Ellipse outline of a given thickness. A ring rather than a disc because the two cannot be made
        /// from each other here: erasing a smaller disc from a larger one would also erase whatever the
        /// glyph had already drawn inside it.
        /// </summary>
        internal UIGlyphCanvas Ring(float cx, float cy, float radiusX, float radiusY, float thickness)
        {
            return Paint((px, py) => EllipseCoverage(px, py, cx, cy, radiusX, radiusY, thickness), false);
        }

        /// <summary>
        /// A line with round caps and a thickness: the workhorse. Rays, rain, fog bars, wind and the
        /// segments of a lightning bolt are all this shape at different angles.
        /// </summary>
        internal UIGlyphCanvas Capsule(float x0, float y0, float x1, float y1, float thickness)
        {
            return Paint((px, py) => CapsuleCoverage(px, py, x0, y0, x1, y1, thickness), false);
        }

        /// <summary>A run of capsules through the given points, for a polyline such as a bolt.</summary>
        internal UIGlyphCanvas Polyline(float thickness, params float[] xy)
        {
            for (int i = 0; i + 3 < xy.Length; i += 2)
                Capsule(xy[i], xy[i + 1], xy[i + 2], xy[i + 3], thickness);

            return this;
        }

        /// <summary>Removes a disc from what has been drawn so far.</summary>
        internal UIGlyphCanvas Erase(float cx, float cy, float radius)
        {
            return Paint((px, py) => EllipseCoverage(px, py, cx, cy, radius, radius, 0f), true);
        }

        /// <summary>
        /// A filled convex polygon, from points given in order. Three of them is a triangle.
        ///
        /// <b>Why this exists.</b> The obvious way to fill a triangle here was three thick capsules along its
        /// edges, and it does not work: at the thickness needed to close the middle, the round caps bulge past
        /// every corner and a play arrow comes out as a blob. A real half-plane test gives sharp corners and a
        /// one-pixel edge like every other primitive, and it is what the transport icons -- play, skip, the
        /// speaker cone -- are all made of.
        ///
        /// <b>Convex only, and points in order.</b> The coverage is the furthest-outside edge, which is the
        /// signed distance to a convex polygon and is meaningless for a concave one. Winding does not matter:
        /// the sign is taken from the polygon's own area so clockwise and counter-clockwise both fill.
        /// </summary>
        internal UIGlyphCanvas Polygon(params float[] xy)
        {
            if (xy == null || xy.Length < 6 || xy.Length % 2 != 0)
                return this;

            int count = xy.Length / 2;
            float[] points = new float[xy.Length];

            for (int i = 0; i < xy.Length; i++)
                points[i] = xy[i] * Resolution;

            // Twice the signed area. Its sign says which way the points wind, which is what lets the edge test
            // below treat both directions as inside.
            float area = 0f;

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;

                area += points[i * 2] * points[next * 2 + 1] - points[next * 2] * points[i * 2 + 1];
            }

            float winding = area >= 0f ? 1f : -1f;

            return Paint((px, py) => PolygonCoverage(px, py, points, count, winding), false);
        }

        /// <summary>
        /// A six-spoke snowflake: three capsules crossing at a point.
        ///
        /// Six rather than the four a plus sign would give, because four reads as a cross and this has to be
        /// recognizable at sixteen pixels where the spokes are barely two pixels long.
        /// </summary>
        internal UIGlyphCanvas Snowflake(float cx, float cy, float radius, float thickness)
        {
            for (int i = 0; i < 3; i++)
            {
                float angle = Mathf.PI * i / 3f;
                float dx = Mathf.Cos(angle) * radius;
                float dy = Mathf.Sin(angle) * radius;

                Capsule(cx - dx, cy - dy, cx + dx, cy + dy, thickness);
            }

            return this;
        }

        /// <summary>
        /// The cloud every overcast, rain and snow glyph is built on: three discs and a flat base.
        ///
        /// One definition rather than one per glyph, so the whole weather set shares a silhouette and a
        /// player reads "cloud, plus something" instead of a dozen unrelated pictures.
        /// </summary>
        internal UIGlyphCanvas Cloud(float cx, float cy, float scale)
        {
            Disc(cx - 0.15f * scale, cy + 0.05f * scale, 0.15f * scale);
            Disc(cx + 0.02f * scale, cy - 0.05f * scale, 0.20f * scale);
            Disc(cx + 0.17f * scale, cy + 0.06f * scale, 0.14f * scale);
            Capsule(cx - 0.15f * scale, cy + 0.13f * scale,
                cx + 0.17f * scale, cy + 0.13f * scale, 0.14f * scale);

            return this;
        }

        /// <summary>Bakes the coverage into a texture. The canvas is reusable afterwards but is not reset.</summary>
        internal Texture2D ToTexture(string name)
        {
            Texture2D texture = new Texture2D(Resolution, Resolution, TextureFormat.ARGB32, false)
            {
                // Clamped, as UIShapes is: an edge pixel must not sample across to the far side when the
                // glyph is drawn at a fraction of its authored size.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };

            Color32[] pixels = new Color32[Resolution * Resolution];

            for (int row = 0; row < Resolution; row++)
            {
                // Row 0 is authored at the top; Unity's row 0 is the bottom.
                int textureRow = Resolution - 1 - row;

                for (int column = 0; column < Resolution; column++)
                {
                    byte alpha = (byte) (Mathf.Clamp01(coverage[row * Resolution + column]) * 255f);
                    pixels[textureRow * Resolution + column] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return texture;
        }

        private delegate float Sampler(float px, float py);

        /// <summary>
        /// Walks every pixel, sampling the shape in pixel units.
        ///
        /// No bounding box. A glyph is 128 by 128 and there are a couple of dozen of them, so the whole set
        /// is a few million samples taken once at startup; the arithmetic to skip pixels would cost more to
        /// read than it saves to run.
        /// </summary>
        private UIGlyphCanvas Paint(Sampler sampler, bool erase)
        {
            for (int row = 0; row < Resolution; row++)
            {
                float py = row + 0.5f;

                for (int column = 0; column < Resolution; column++)
                {
                    float sample = sampler(column + 0.5f, py);
                    if (sample <= 0f)
                        continue;

                    int index = row * Resolution + column;

                    coverage[index] = erase
                        ? Mathf.Min(coverage[index], 1f - sample)
                        : Mathf.Max(coverage[index], sample);
                }
            }

            return this;
        }

        /// <summary>
        /// Coverage of an ellipse at a pixel, filled when <paramref name="thickness"/> is zero and an
        /// outline of that thickness otherwise.
        ///
        /// The distance is the ellipse's implicit function scaled back to something close to pixels. It is
        /// exact for a circle and slightly off for an eccentric ellipse, which only affects the width of the
        /// one-pixel edge ramp on a shape that is about to be downscaled anyway.
        /// </summary>
        private static float EllipseCoverage(float px, float py, float cx, float cy,
            float radiusX, float radiusY, float thickness)
        {
            float rx = radiusX * Resolution;
            float ry = radiusY * Resolution;

            if (rx <= 0f || ry <= 0f)
                return 0f;

            float dx = px - cx * Resolution;
            float dy = py - cy * Resolution;

            float normalized = Mathf.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
            float distance = (normalized - 1f) * Mathf.Min(rx, ry);

            if (thickness > 0f)
                distance = Mathf.Abs(distance) - thickness * Resolution * 0.5f;

            return Mathf.Clamp01(0.5f - distance / EdgeSoftness);
        }

        /// <summary>
        /// Coverage of a convex polygon at a pixel, in the same units the other two work in.
        ///
        /// The distance to a convex polygon is the largest of the perpendicular distances to its edge lines,
        /// negative inside. Each edge's normal is scaled by the winding so both point outward whichever way the
        /// points were given.
        /// </summary>
        private static float PolygonCoverage(float px, float py, float[] points, int count, float winding)
        {
            float distance = float.NegativeInfinity;

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;

                float ax = points[i * 2];
                float ay = points[i * 2 + 1];
                float ex = points[next * 2] - ax;
                float ey = points[next * 2 + 1] - ay;

                float length = Mathf.Sqrt(ex * ex + ey * ey);

                // A repeated point contributes no edge. Skipped rather than dividing by zero, which would make
                // the whole polygon vanish.
                if (length <= 0f)
                    continue;

                // The outward normal of this edge, unit length.
                float nx = ey / length * winding;
                float ny = -ex / length * winding;

                float edge = (px - ax) * nx + (py - ay) * ny;

                if (edge > distance)
                    distance = edge;
            }

            if (float.IsNegativeInfinity(distance))
                return 0f;

            return Mathf.Clamp01(0.5f - distance / EdgeSoftness);
        }

        private static float CapsuleCoverage(float px, float py, float x0, float y0, float x1, float y1,
            float thickness)
        {
            float ax = x0 * Resolution;
            float ay = y0 * Resolution;
            float bx = x1 * Resolution;
            float by = y1 * Resolution;

            float abx = bx - ax;
            float aby = by - ay;
            float lengthSquared = abx * abx + aby * aby;

            float t = lengthSquared <= 0f
                ? 0f
                : Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) / lengthSquared);

            float dx = px - (ax + abx * t);
            float dy = py - (ay + aby * t);

            float distance = Mathf.Sqrt(dx * dx + dy * dy) - thickness * Resolution * 0.5f;

            return Mathf.Clamp01(0.5f - distance / EdgeSoftness);
        }
    }
}
