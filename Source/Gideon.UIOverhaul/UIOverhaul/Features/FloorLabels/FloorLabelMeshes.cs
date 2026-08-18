using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>One string turned into geometry, with the size it came out at.</summary>
    internal sealed class FloorLabelMesh
    {
        internal Mesh Mesh;

        /// <summary>Width in glyph units, so the drawer can work out the scale that makes it fit a room.</summary>
        internal float Width;

        /// <summary>Height of the tallest glyph, in the same units.</summary>
        internal float Height;
    }

    /// <summary>
    /// Builds a mesh per label and keeps it.
    ///
    /// <b>Cached because building one is not free and a label does not change between frames.</b> A colony has
    /// tens of labels and they are rebuilt only when the text changes or the atlas moves underneath them, so the
    /// steady state is a dictionary lookup per label per frame.
    ///
    /// <b>Cleared wholesale when the font atlas is rebuilt.</b> Every UV in every mesh here points into that
    /// texture, so after a rebuild they all address the wrong glyphs -- the classic symptom being labels that
    /// turn into other people's letters. See <see cref="FloorLabelFont"/>.
    ///
    /// <b>Built in glyph units, positioned by the caller's matrix.</b> The mesh is centered on the origin and
    /// one unit tall per pixel of the atlas; scaling it to a room is a matrix, not a rebuild. That is what lets
    /// the same mesh serve every zoom level.
    /// </summary>
    internal static class FloorLabelMeshes
    {
        private static readonly Dictionary<string, FloorLabelMesh> Cache =
            new Dictionary<string, FloorLabelMesh>();

        private static bool listening;

        /// <summary>
        /// The mesh for this text, built if it is new.
        ///
        /// Null when there is no font, or when the text has nothing drawable in it.
        /// </summary>
        internal static FloorLabelMesh For(string text)
        {
            if (text.NullOrEmpty() || !FloorLabelFont.Available)
                return null;

            Listen();

            FloorLabelMesh existing;

            // A destroyed mesh is treated as absent. Unity can collect one out from under the dictionary if
            // anything else destroys it, and a null check here is cheaper than being wrong about it.
            if (Cache.TryGetValue(text, out existing) && existing != null && existing.Mesh != null)
                return existing;

            FloorLabelMesh built = UIGuard.Try("FloorLabels.BuildMesh", () => Build(text), null, null);

            if (built != null)
                Cache[text] = built;

            return built;
        }

        private static void Listen()
        {
            if (listening)
                return;

            listening = true;
            FloorLabelFont.Invalidated += Clear;
        }

        /// <summary>Drops every mesh. Called when the atlas moves, and safe to call at any time.</summary>
        internal static void Clear()
        {
            UIGuard.Try("FloorLabels.ClearMeshes", () =>
            {
                foreach (KeyValuePair<string, FloorLabelMesh> pair in Cache)
                {
                    // A Mesh is unmanaged and the collector will not reclaim it. Tens of labels rebuilt on
                    // every atlas change would leak steadily over a long session.
                    if (pair.Value != null && pair.Value.Mesh != null)
                        Object.Destroy(pair.Value.Mesh);
                }

                Cache.Clear();
            }, null);
        }

        private static FloorLabelMesh Build(string text)
        {
            FloorLabelFont.Request(text);

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            float pen = 0f;
            float top = 0f;
            float bottom = 0f;

            foreach (char c in text)
            {
                FloorGlyph glyph;

                if (!FloorLabelFont.TryGlyph(c, out glyph))
                    continue;

                // A space has no quad but still advances, so it is handled before anything is emitted.
                if (glyph.Drawable)
                {
                    int at = vertices.Count;

                    float left = pen + glyph.MinX;
                    float right = pen + glyph.MaxX;
                    float up = glyph.MaxY;
                    float down = glyph.MinY;

                    // Flat on the ground: x across, z up the screen, y left for the altitude the caller sets.
                    vertices.Add(new Vector3(left, 0f, down));
                    vertices.Add(new Vector3(left, 0f, up));
                    vertices.Add(new Vector3(right, 0f, up));
                    vertices.Add(new Vector3(right, 0f, down));

                    uvs.Add(glyph.UvBottomLeft);
                    uvs.Add(glyph.UvTopLeft);
                    uvs.Add(glyph.UvTopRight);
                    uvs.Add(glyph.UvBottomRight);

                    triangles.Add(at);
                    triangles.Add(at + 1);
                    triangles.Add(at + 2);
                    triangles.Add(at);
                    triangles.Add(at + 2);
                    triangles.Add(at + 3);

                    if (up > top)
                        top = up;

                    if (down < bottom)
                        bottom = down;
                }

                pen += glyph.Advance;
            }

            if (vertices.Count == 0)
                return null;

            // Centered on the origin, so the caller positions by the label's middle rather than its corner and
            // the same matrix works whatever the text is.
            float halfWidth = pen * 0.5f;
            float middle = (top + bottom) * 0.5f;

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 v = vertices[i];

                vertices[i] = new Vector3(v.x - halfWidth, v.y, v.z - middle);
            }

            Mesh mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);

            // The label is flat and unlit, so normals and tangents would be data nothing reads. Bounds are
            // recalculated because culling uses them.
            mesh.RecalculateBounds();

            return new FloorLabelMesh
            {
                Mesh = mesh,
                Width = pen,
                Height = top - bottom
            };
        }
    }
}
