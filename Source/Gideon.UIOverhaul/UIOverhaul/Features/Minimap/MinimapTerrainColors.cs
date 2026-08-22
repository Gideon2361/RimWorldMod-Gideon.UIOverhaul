using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Minimap
{
    /// <summary>
    /// What colour a terrain actually appears as on the map.
    ///
    /// <b>Read from the terrain's own texture, because RimWorld stores no colour for most ground.</b>
    /// <c>TerrainDef.color</c> is white for soil, grass, sand and rock -- the terrain covering almost every
    /// cell of a map -- and only the floors and stone tiles declare one. Ground gets its appearance from a
    /// texture, so the only honest way to match what is on screen is to look at that texture.
    ///
    /// <b>Sampled on the GPU rather than read directly.</b> Textures loaded from a mod or the game are not
    /// readable from script, so <c>GetPixels</c> on one throws. Blitting into a small RenderTexture and
    /// reading that back is the standard way round it, and it has a second benefit: blitting into a 4 by 4
    /// target makes the GPU do the averaging with its own filtering, so the readback is sixteen pixels rather
    /// than the whole texture. This mod already does the same thing to shrink a save's screenshot; see
    /// <c>SaveThumbnails</c>.
    ///
    /// <b>Then tinted by the def's own colour.</b> That is how RimWorld draws it: a greyscale stone tile
    /// texture times a colour is what makes granite look different from sandstone. Undeclared colours are
    /// white, so the multiply changes nothing for ordinary ground.
    ///
    /// Once per terrain, cached for the session. A map has a few dozen distinct terrains and 160,000 cells.
    /// </summary>
    internal static class MinimapTerrainColors
    {
        /// <summary>
        /// How small the texture is blitted down to before reading it back.
        ///
        /// Four by four rather than one by one: a single pixel asks the GPU for one mip level and some drivers
        /// give back the corner rather than the average, whereas sixteen samples averaged in managed code is
        /// stable everywhere and still trivially cheap.
        /// </summary>
        private const int SampleSize = 4;

        private static readonly Dictionary<BuildableDef, Color32> Cache = new Dictionary<BuildableDef, Color32>();

        /// <summary>
        /// Darkens what comes back.
        ///
        /// Terrain textures are painted to sit under RimWorld's lighting, which is never at full brightness.
        /// Shown flat on a panel they come out lighter than the map does, and the markers drawn over them stop
        /// standing out.
        /// </summary>
        private const float Dim = 0.85f;

        internal static Color32 For(TerrainDef def)
        {
            Color32 color;

            if (Cache.TryGetValue(def, out color))
                return color;

            color = Resolve(def);

            Cache[def] = color;

            return color;
        }

        /// <summary>
        /// The colour of a thing that is worth painting onto the map, which today means plants.
        ///
        /// <b>Plants are why a RimWorld map looks green and terrain does not.</b> There is no grass terrain:
        /// grass is <c>Plant_Grass</c>, a ThingDef standing on ordinary brown soil, and the same goes for
        /// bushes and trees. A minimap that draws only terrain is drawing dirt, accurately, and looks nothing
        /// like the map it describes.
        ///
        /// Sampled exactly as terrain is, and cached in the same table -- both are BuildableDefs with a
        /// graphic, so nothing here needs to know which it was handed.
        /// </summary>
        internal static Color32 ForThing(ThingDef def)
        {
            Color32 color;

            if (Cache.TryGetValue(def, out color))
                return color;

            Color average;

            if (TrySample(TextureOf(def), out average))
            {
                sampledCount++;

                // graphicData's colour, not DrawColor: a plant's tint lives there, and it is what separates a
                // dead brown bush from a living green one.
                Color tint = def.graphicData?.color ?? Color.white;

                color = Pack(average * tint);
            }
            else
            {
                fallbackCount++;

                // A plant nobody could sample still reads better as green than as the ground it stands on.
                color = new Color32(72, 92, 54, 255);
            }

            Cache[def] = color;

            return color;
        }

        private static Color32 Resolve(TerrainDef def)
        {
            Color average;

            if (TrySample(TextureOf(def), out average))
            {
                sampledCount++;

                // Multiplied by the def's own colour, which is how RimWorld draws it: a greyscale stone tile
                // texture times a colour is what makes granite look different from sandstone. Undeclared
                // colours are white, so ordinary ground is unaffected.
                return Pack(average * def.DrawColor);
            }

            fallbackCount++;

            // No usable texture. MinimapImage owns what the fallback palette looks like.
            return MinimapImage.FallbackColor(def);
        }

        /// <summary>
        /// The texture RimWorld actually draws this terrain with.
        ///
        /// <b>The graphic's material, not the def's uiIcon.</b> An earlier version asked for <c>uiIcon</c>,
        /// which is <c>BadTex</c> for terrain -- terrain defs carry a <c>texturePath</c> and RimWorld builds a
        /// <c>Graphic_Terrain</c> from it during load. Sampling the icon therefore failed for every terrain on
        /// the map and fell through to the approximate palette without saying so, which is exactly the flat
        /// panel this was meant to fix.
        ///
        /// uiIcon is still tried second: a modded terrain that supplies an icon and no graphic is unusual, and
        /// an approximate colour from the right family beats the fallback.
        /// </summary>
        private static Texture TextureOf(BuildableDef def)
        {
            Material material = def.graphic?.MatSingle;

            if (material != null && material.mainTexture != null)
                return material.mainTexture;

            return def.uiIcon == BaseContent.BadTex ? null : def.uiIcon;
        }

        /// <summary>
        /// The average colour of a texture, whether or not it is readable.
        ///
        /// Guarded and reported as a failure rather than a throw: a modded terrain with an odd texture should
        /// cost that terrain its colour, not the minimap.
        /// </summary>
        private static bool TrySample(Texture source, out Color average)
        {
            average = Color.white;

            if (source == null)
                return false;

            // <b>Sentinel rather than white.</b> The lambda below returns early when a texture is entirely
            // transparent, and an earlier version left the result at white in that case -- which was then
            // reported as a successful sample of a white terrain. A failure has to be distinguishable from a
            // legitimately white result, or the diagnostic lies again.
            Color result = new Color(-1f, -1f, -1f, -1f);

            bool sampled = UIGuard.Try("Minimap.SampleTerrain", () =>
            {
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture small = null;
                Texture2D readback = null;

                try
                {
                    small = RenderTexture.GetTemporary(SampleSize, SampleSize, 0);

                    Graphics.Blit(source, small);

                    RenderTexture.active = small;

                    readback = new Texture2D(SampleSize, SampleSize, TextureFormat.RGBA32, false);
                    readback.ReadPixels(new Rect(0f, 0f, SampleSize, SampleSize), 0, 0);
                    readback.Apply(false);

                    Color[] pixels = readback.GetPixels();

                    float r = 0f, g = 0f, b = 0f, weight = 0f;

                    foreach (Color pixel in pixels)
                    {
                        // Weighted by alpha. Terrain textures have transparent margins on their edge variants,
                        // and counting those as black would drag every average toward it.
                        r += pixel.r * pixel.a;
                        g += pixel.g * pixel.a;
                        b += pixel.b * pixel.a;
                        weight += pixel.a;
                    }

                    if (weight <= 0.001f)
                        return;

                    result = new Color(r / weight, g / weight, b / weight, 1f);
                }
                finally
                {
                    // Restored whatever happened. Leaving RenderTexture.active pointing at a released
                    // temporary is how the next thing to draw ends up rendering into nothing.
                    RenderTexture.active = previousActive;

                    if (small != null)
                        RenderTexture.ReleaseTemporary(small);

                    if (readback != null)
                        Object.Destroy(readback);
                }
            }, null);

            if (!sampled || result.r < 0f)
                return false;

            average = result;

            return true;
        }

        /// <summary>
        /// Every terrain colour resolved so far, as text, for the diagnostic line.
        ///
        /// <b>Because "37 sampled, 0 fell back" still did not say whether the answers were any good.</b> A
        /// sampler that succeeds and returns the same flat tone for every terrain looks identical in the counts
        /// to one that works. Printing the actual values is the only thing that separates "the reads failed"
        /// from "the reads worked and the drawing is wrong", and the distinct count answers it at a glance.
        /// </summary>
        internal static string Describe(int limit)
        {
            HashSet<int> distinct = new HashSet<int>();
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            int shown = 0;

            foreach (KeyValuePair<BuildableDef, Color32> pair in Cache)
            {
                Color32 c = pair.Value;

                distinct.Add((c.r << 16) | (c.g << 8) | c.b);

                if (shown >= limit)
                    continue;

                if (shown > 0)
                    builder.Append(", ");

                builder.Append(pair.Key.defName).Append('=')
                    .Append(c.r).Append('/').Append(c.g).Append('/').Append(c.b);

                shown++;
            }

            return distinct.Count + " distinct [" + builder + "]";
        }

        private static Color32 Pack(Color color)
        {
            return new Color32(
                (byte) Mathf.Clamp(color.r * 255f * Dim, 0f, 255f),
                (byte) Mathf.Clamp(color.g * 255f * Dim, 0f, 255f),
                (byte) Mathf.Clamp(color.b * 255f * Dim, 0f, 255f),
                255);
        }

        private static int sampledCount;
        private static int fallbackCount;

        /// <summary>
        /// How many terrains got their colour from their own texture, and how many fell back.
        ///
        /// <b>Two numbers, because one was actively misleading.</b> This used to report the size of the cache,
        /// which counts a fallback exactly the same as a real sample -- so a run where every single sample
        /// failed reported a healthy looking "37 terrains sampled" and sent the investigation somewhere else
        /// entirely. A diagnostic that cannot distinguish success from failure is worse than none.
        /// </summary>
        internal static int Sampled => sampledCount;

        internal static int Fallbacks => fallbackCount;

        /// <summary>
        /// How many distinct colours the cache holds, which is the third number that says whether a bake worked.
        ///
        /// <b>Separated out of <see cref="Describe"/> so it can be asked cheaply.</b> The bake's report is now
        /// only written when the reading has actually changed, and deciding that must not cost the string it is
        /// deciding whether to build. This walks a dictionary of a few dozen entries; Describe builds a sentence.
        /// </summary>
        internal static int Distinct
        {
            get
            {
                HashSet<int> distinct = new HashSet<int>();

                foreach (KeyValuePair<BuildableDef, Color32> pair in Cache)
                {
                    Color32 c = pair.Value;

                    distinct.Add((c.r << 16) | (c.g << 8) | c.b);
                }

                return distinct.Count;
            }
        }

        internal static void Clear()
        {
            Cache.Clear();

            sampledCount = 0;
            fallbackCount = 0;
        }
    }
}
