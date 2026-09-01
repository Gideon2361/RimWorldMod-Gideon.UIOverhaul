using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace Gideon.UIOverhaul.Features.TilePreview
{
    /// <summary>
    /// The elevation and fertility of a map that has not been generated, read straight off the noise.
    ///
    /// Two grids the width and height of the map the tile would produce, in the same order RimWorld's own cells
    /// are walked: index is <c>z * Width + x</c>.
    /// </summary>
    internal sealed class TilePreviewField
    {
        internal int Width;

        internal int Height;

        /// <summary>Elevation per cell. Above <c>MapGenTuning.ThresholdElevationRock</c> is stone.</summary>
        internal float[] Elevation;

        /// <summary>Fertility per cell, which is what picks the ground terrain inside a biome.</summary>
        internal float[] Fertility;

        /// <summary>The tile this was read from, kept so the reading beside it can ask about the biome.</summary>
        internal Tile Tile;

        internal int Cells
        {
            get { return Width * Height; }
        }
    }

    /// <summary>
    /// Rebuilds <c>GenStep_ElevationFertility</c> against a world tile, without a map.
    ///
    /// <b>That step is a pure function of the tile and the world seed, and it only needs a map because it writes
    /// its answer into one.</b> Everything it reads is on the tile: the hilliness factor, whether the tile is
    /// water covered, and whether a mutator suppresses natural elevation. Everything it computes is a chain of
    /// <c>Verse.Noise</c> modules, which are ordinary objects that evaluate at a coordinate. So the shape of a map
    /// can be known before the map exists, which is the whole of this feature.
    ///
    /// <b>The random stream is the fragile part, not the arithmetic.</b> Every value in the chain comes from
    /// <c>Rand</c> in a fixed order, so the calls here have to happen in exactly the order the real step makes
    /// them, from exactly the same seed. Reorder two of them and the preview is a plausible map of nowhere. That
    /// is why the displacement noise goes through <c>MapNoiseUtility.AddDisplacementNoise</c> rather than being
    /// written out: it draws from the stream itself, and matching it by hand would mean matching how many numbers
    /// it takes.
    ///
    /// <b>Pushed and popped around the whole thing.</b> The colony's own random stream is shared, and a preview
    /// drawn while the player hovers a tile must not move it -- a UI that changes what the game rolls is a far
    /// worse bug than a wrong picture.
    ///
    /// <b>The tuning is read from the step itself where it is reachable.</b> Five of the ranges are private, so
    /// those are reflected with the shipped values as a fallback; the rest are public constants and are used
    /// directly. If Ludeon retunes them, the reflected ones follow and the preview stays honest.
    /// </summary>
    internal static class TilePreviewGenerator
    {
        /// <summary>Fertility noise frequency. A private const on the step, so it is restated here.</summary>
        private const float FertilityFreq = 0.021f;

        private static bool resolved;

        private static FloatRange warpFreq = new FloatRange(0.01f, 0.02f);
        private static FloatRange warpStrength = new FloatRange(0f, 15f);
        private static IntRange warpOctaves = new IntRange(3, 4);
        private static FloatRange preStretch = new FloatRange(1f, 1.15f);
        private static FloatRange postStretch = new FloatRange(1f, 1.15f);

        /// <summary>
        /// The step's own seed part, read off an instance rather than written down.
        ///
        /// <c>MapGenerator</c> adds a disambiguator when two steps in one generator share a part; elevation and
        /// fertility is the only step with this one, so the part is the whole answer.
        /// </summary>
        private static int seedPart;

        private static bool seedPartRead;

        internal static TilePreviewField For(PlanetTile planetTile)
        {
            return UIGuard.Try<TilePreviewField>("TilePreview.Field", () => Build(planetTile), null,
                "A world tile could not be previewed. The world map is otherwise unaffected.");
        }

        private static TilePreviewField Build(PlanetTile planetTile)
        {
            World world = Find.World;

            if (world == null || world.grid == null)
                return null;

            Tile tile = world.grid[planetTile];

            if (tile == null)
                return null;

            Resolve();

            IntVec3 size = world.info.initialMapSize;

            int width = Mathf.Max(1, size.x);
            int height = Mathf.Max(1, size.z);

            TilePreviewField field = new TilePreviewField
            {
                Width = width,
                Height = height,
                Tile = tile,
                Elevation = new float[width * height],
                Fertility = new float[width * height]
            };

            int mapSeed = Gen.HashCombineInt(world.info.Seed, planetTile.GetHashCode());

            Rand.PushState(Gen.HashCombineInt(mapSeed, SeedPart()));

            try
            {
                Fill(field, tile);
            }
            finally
            {
                Rand.PopState();
            }

            return field;
        }

        /// <summary>
        /// The chain, in the order the step builds it. Every line here draws from the seeded stream.
        /// </summary>
        private static void Fill(TilePreviewField field, Tile tile)
        {
            bool natural = !SuppressesElevation(tile);

            ModuleBase elevation = null;

            if (natural)
            {
                elevation = new Perlin(GenStep_ElevationFertility.ElevationFreqRange.RandomInRange, 2.0, 0.5,
                    GenStep_ElevationFertility.ElevationOctaves, Rand.Range(0, int.MaxValue),
                    QualityMode.High);

                elevation = MapNoiseUtility.AddDisplacementNoise(elevation,
                    GenStep_ElevationFertility.DetailFreqRange.RandomInRange,
                    GenStep_ElevationFertility.DetailStrengthRange.RandomInRange,
                    GenStep_ElevationFertility.DetailOctavesRange.RandomInRange);

                elevation = new Scale(preStretch.RandomInRange, 1.0, 1.0, elevation);
                elevation = new Rotate(0.0, Rand.Range(0f, 180f), 0.0, elevation);

                elevation = MapNoiseUtility.AddDisplacementNoise(elevation, warpFreq.RandomInRange,
                    warpStrength.RandomInRange, warpOctaves.RandomInRange);

                elevation = new Scale(postStretch.RandomInRange, 1.0, 1.0, elevation);
                elevation = new Rotate(0.0, Rand.Range(0f, 180f), 0.0, elevation);
                elevation = new ScaleBias(0.5, 0.5, elevation);
                elevation = new Multiply(elevation, new Const(FactorFor(tile.HillinessForElevationGen)));
            }

            // A water covered tile is clamped flat rather than shaped. The step does this with a Min against
            // zero, and it is what makes an ocean tile produce no stone at all.
            float ceiling = tile.WaterCovered ? 0f : float.MaxValue;

            if (elevation != null)
            {
                for (int z = 0; z < field.Height; z++)
                {
                    for (int x = 0; x < field.Width; x++)
                    {
                        field.Elevation[z * field.Width + x] =
                            Mathf.Min(elevation.GetValue(new IntVec3(x, 0, z)), ceiling);
                    }
                }
            }

            // Built after the elevation modules whether or not they were, because the step does: its seed comes
            // out of the stream at whatever point the branch above left it.
            ModuleBase fertility = new Perlin(FertilityFreq, 2.0, 0.5, 6, Rand.Range(0, int.MaxValue),
                QualityMode.High);

            fertility = new ScaleBias(0.5, 0.5, fertility);

            for (int z = 0; z < field.Height; z++)
            {
                for (int x = 0; x < field.Width; x++)
                    field.Fertility[z * field.Width + x] = fertility.GetValue(new IntVec3(x, 0, z));
            }
        }

        /// <summary>Whether any mutator on this tile turns natural elevation off, as the step asks.</summary>
        private static bool SuppressesElevation(Tile tile)
        {
            IList<TileMutatorDef> mutators = tile.Mutators;

            for (int i = 0; mutators != null && i < mutators.Count; i++)
            {
                if (mutators[i] != null && mutators[i].preventNaturalElevation)
                    return true;
            }

            return false;
        }

        /// <summary>The hilliness multiplier, which is the one thing that makes two tiles differ this much.</summary>
        private static float FactorFor(Hilliness hilliness)
        {
            switch (hilliness)
            {
                case Hilliness.Flat:
                    return MapGenTuning.ElevationFactorFlat;

                case Hilliness.SmallHills:
                    return MapGenTuning.ElevationFactorSmallHills;

                case Hilliness.LargeHills:
                    return MapGenTuning.ElevationFactorLargeHills;

                case Hilliness.Mountainous:
                    return MapGenTuning.ElevationFactorMountains;

                case Hilliness.Impassable:
                    return MapGenTuning.ElevationFactorImpassableMountains;

                default:
                    return 1f;
            }
        }

        private static int SeedPart()
        {
            if (seedPartRead)
                return seedPart;

            seedPartRead = true;

            seedPart = UIGuard.Try("TilePreview.SeedPart", () => new GenStep_ElevationFertility().SeedPart,
                826504671, null);

            return seedPart;
        }

        /// <summary>
        /// Reads the five private tuning ranges once, each independently: one renamed field costs its own
        /// value rather than the whole preview.
        /// </summary>
        private static void Resolve()
        {
            if (resolved)
                return;

            resolved = true;

            UIGuard.Try("TilePreview.Tuning", () =>
            {
                warpFreq = Float("WarpFreqRange", warpFreq);
                warpStrength = Float("WarpStrengthRange", warpStrength);
                preStretch = Float("PreTurbulenceStretchRange", preStretch);
                postStretch = Float("PostTurbulenceStretchRange", postStretch);
                warpOctaves = Int("WarpOctavesRange", warpOctaves);
            }, null);
        }

        private static FieldInfo Field(string name)
        {
            return typeof(GenStep_ElevationFertility).GetField(name,
                BindingFlags.NonPublic | BindingFlags.Static);
        }

        private static FloatRange Float(string name, FloatRange fallback)
        {
            FieldInfo field = Field(name);

            return field != null && field.FieldType == typeof(FloatRange)
                ? (FloatRange) field.GetValue(null)
                : fallback;
        }

        private static IntRange Int(string name, IntRange fallback)
        {
            FieldInfo field = Field(name);

            return field != null && field.FieldType == typeof(IntRange)
                ? (IntRange) field.GetValue(null)
                : fallback;
        }
    }
}
