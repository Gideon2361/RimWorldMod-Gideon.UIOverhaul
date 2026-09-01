using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Minimap;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.TilePreview
{
    /// <summary>
    /// What the grid says, once it has been banded.
    ///
    /// Percentages rather than cell counts, because the map size is a setting and a player comparing two tiles
    /// is comparing shares of a map rather than areas.
    /// </summary>
    internal struct TilePreviewReading
    {
        /// <summary>Cells that are neither stone nor water. The figure the decision turns on.</summary>
        internal int Buildable;

        /// <summary>The largest single connected piece of that, as a share of the whole map.</summary>
        internal int LargestRun;

        /// <summary>Cells under overhead rock: no drop pods, no sun, and infestations.</summary>
        internal int Mountain;

        /// <summary>Stone that can be cut into, roofed or open.</summary>
        internal int Rock;

        /// <summary>Share of the buildable ground whose terrain is soil or better.</summary>
        internal int Fertile;

        internal int Water;
    }

    /// <summary>
    /// Turns a field into a picture and six figures.
    ///
    /// <b>Banded at the game's own thresholds rather than at chosen ones.</b>
    /// <c>MapGenTuning.ThresholdElevationRock</c> is where stone begins, and
    /// <c>GenStep_RocksFromGrid</c> puts a thin roof at 1.04 times it and overhead mountain at 1.14. Those three
    /// numbers are the difference between ground you can build on, stone you can mine and mountain you cannot
    /// see the sky through, so they are what the picture is drawn from and what the figures are counted against.
    ///
    /// <b>Ground terrain comes from the biome, through the game's own lookup.</b>
    /// <c>TerrainThreshold.TerrainAtValue</c> against <c>BiomeDef.terrainsByFertility</c> is exactly how the real
    /// terrain step picks soil against sand against gravel, so a modded biome with its own terrain list is drawn
    /// correctly without knowing anything about it.
    ///
    /// <b>Colour comes from the minimap's sampler,</b> which reads a terrain's true colour off its own texture
    /// through a render target and caches it per def. Modded terrain and modded stone are handled for free, which
    /// a fixed palette could never do.
    /// </summary>
    internal static class TilePreviewImage
    {
        /// <summary>Where stone begins.</summary>
        private static float RockAt
        {
            get { return MapGenTuning.ThresholdElevationRock; }
        }

        /// <summary>Where a thin rock roof begins.</summary>
        private static float ThinAt
        {
            get { return RockAt * 1.04f; }
        }

        /// <summary>Where overhead mountain begins.</summary>
        private static float ThickAt
        {
            get { return RockAt * 1.14f; }
        }

        /// <summary>
        /// Soil and better. <c>TerrainDef.fertility</c> is 1 for soil, 1.4 for rich soil and well under 1 for
        /// sand and gravel, so this is the line between ground worth sowing and ground that merely holds a wall.
        /// </summary>
        private const float FertileAt = 1f;

        /// <summary>Scratch for the flood fill, so a hover does not allocate two map-sized arrays.</summary>
        private static bool[] open;

        private static bool[] seen;

        private static int[] stack;

        internal static Texture2D Render(TilePreviewField field, out TilePreviewReading reading)
        {
            TilePreviewReading found = default(TilePreviewReading);

            Texture2D texture = UIGuard.Try<Texture2D>("TilePreview.Render", () => Build(field, out found),
                null, "A world tile preview could not be drawn.");

            reading = found;

            return texture;
        }

        private static Texture2D Build(TilePreviewField field, out TilePreviewReading reading)
        {
            reading = default(TilePreviewReading);

            if (field == null || field.Tile == null || field.Cells <= 0)
                return null;

            int cells = field.Cells;

            Prepare(cells);

            BiomeDef biome = field.Tile.PrimaryBiome;

            Color32 stone = StoneColor(field.Tile);
            Color32 mountain = Darken(stone, 0.55f);
            Color32 roofed = Darken(stone, 0.78f);

            Color32[] pixels = new Color32[cells];

            int buildable = 0;
            int mountainCells = 0;
            int rockCells = 0;
            int waterCells = 0;
            int fertileCells = 0;

            bool drowned = field.Tile.WaterCovered;

            Color32 water = WaterColor(biome);

            for (int i = 0; i < cells; i++)
            {
                open[i] = false;

                if (drowned)
                {
                    pixels[i] = water;
                    waterCells++;

                    continue;
                }

                float elevation = field.Elevation[i];

                if (elevation > ThickAt)
                {
                    pixels[i] = mountain;
                    mountainCells++;
                    rockCells++;

                    continue;
                }

                if (elevation > ThinAt)
                {
                    pixels[i] = roofed;
                    rockCells++;

                    continue;
                }

                if (elevation > RockAt)
                {
                    pixels[i] = stone;
                    rockCells++;

                    continue;
                }

                TerrainDef terrain = GroundAt(biome, field.Fertility[i]);

                pixels[i] = terrain == null ? stone : MinimapTerrainColors.For(terrain);

                buildable++;
                open[i] = true;

                if (terrain != null && terrain.fertility >= FertileAt)
                    fertileCells++;
            }

            reading = new TilePreviewReading
            {
                Buildable = Share(buildable, cells),
                Mountain = Share(mountainCells, cells),
                Rock = Share(rockCells, cells),
                Water = Share(waterCells, cells),
                Fertile = buildable > 0 ? Share(fertileCells, buildable) : 0,
                LargestRun = Share(LargestRun(field.Width, field.Height), cells)
            };

            // Rows are written top down here and Unity reads its textures bottom up, which would mirror the
            // preview against the map it is a picture of. Flipped once, on the way in.
            Texture2D texture = new Texture2D(field.Width, field.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] flipped = new Color32[cells];

            for (int z = 0; z < field.Height; z++)
            {
                int from = z * field.Width;
                int to = (field.Height - 1 - z) * field.Width;

                for (int x = 0; x < field.Width; x++)
                    flipped[to + x] = pixels[from + x];
            }

            texture.SetPixels32(flipped);
            texture.Apply(false);

            return texture;
        }

        private static int Share(int part, int whole)
        {
            return whole <= 0 ? 0 : Mathf.RoundToInt(part / (float) whole * 100f);
        }

        /// <summary>The ground this biome puts at this fertility, which is what the terrain step asks.</summary>
        private static TerrainDef GroundAt(BiomeDef biome, float fertility)
        {
            if (biome == null || biome.terrainsByFertility == null)
                return TerrainDefOf.Soil;

            TerrainDef found = TerrainThreshold.TerrainAtValue(biome.terrainsByFertility, fertility);

            return found ?? TerrainDefOf.Soil;
        }

        /// <summary>
        /// The tile's own stone, so a marble tile does not read as granite.
        ///
        /// The first of the natural rock types, which is the same set the generator draws from. A tile with none
        /// falls back to the terrain sampler's answer for gravel rather than to a literal grey.
        /// </summary>
        private static Color32 StoneColor(Tile tile)
        {
            return UIGuard.Try("TilePreview.Stone", () =>
            {
                World world = Find.World;

                if (world != null)
                {
                    foreach (ThingDef rock in world.NaturalRockTypesIn(tile.tile))
                    {
                        if (rock != null)
                            return MinimapTerrainColors.ForThing(rock);
                    }
                }

                return MinimapTerrainColors.For(TerrainDefOf.Gravel);
            }, new Color32(90, 86, 79, 255), null);
        }

        private static Color32 WaterColor(BiomeDef biome)
        {
            return UIGuard.Try("TilePreview.Water", () =>
            {
                TerrainDef deep = biome != null ? biome.oceanDeepTerrain ?? biome.waterDeepTerrain : null;

                return MinimapTerrainColors.For(deep ?? TerrainDefOf.WaterDeep);
            }, new Color32(46, 74, 99, 255), null);
        }

        private static Color32 Darken(Color32 color, float by)
        {
            return new Color32((byte) (color.r * by), (byte) (color.g * by), (byte) (color.b * by), 255);
        }

        private static void Prepare(int cells)
        {
            if (open == null || open.Length < cells)
            {
                open = new bool[cells];
                seen = new bool[cells];
                stack = new int[cells];
            }
        }

        /// <summary>
        /// The biggest connected run of buildable ground, four-connected.
        ///
        /// <b>This is the figure that separates two tiles the buildable share calls equal.</b> Sixty percent
        /// open, cut into nine pockets by rock spurs, is a worse site than forty percent in one piece, and the
        /// two look alike at a glance -- which is exactly the misreading a preview picture invites.
        ///
        /// Iterative, because a recursive fill over sixty thousand cells overflows the stack.
        /// </summary>
        private static int LargestRun(int width, int height)
        {
            int cells = width * height;

            for (int i = 0; i < cells; i++)
                seen[i] = false;

            int best = 0;

            for (int start = 0; start < cells; start++)
            {
                if (!open[start] || seen[start])
                    continue;

                int top = 0;
                int size = 0;

                stack[top++] = start;
                seen[start] = true;

                while (top > 0)
                {
                    int index = stack[--top];

                    size++;

                    int x = index % width;
                    int z = index / width;

                    if (x > 0)
                        Push(index - 1, ref top);

                    if (x < width - 1)
                        Push(index + 1, ref top);

                    if (z > 0)
                        Push(index - width, ref top);

                    if (z < height - 1)
                        Push(index + width, ref top);
                }

                if (size > best)
                    best = size;
            }

            return best;
        }

        private static void Push(int index, ref int top)
        {
            if (!open[index] || seen[index])
                return;

            seen[index] = true;
            stack[top++] = index;
        }
    }
}
