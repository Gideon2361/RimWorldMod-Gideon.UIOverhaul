using System.Collections.Generic;
using System.Linq;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.WorldTile
{
    /// <summary>How a reading should be coloured, which is the thing the vanilla tab never says.</summary>
    internal enum WorldTileTone
    {
        Plain,
        Good,
        Warning,
        Bad,
        Quiet
    }

    /// <summary>One reading: what it is, what it says, and whether that is good news.</summary>
    internal struct WorldTileFact
    {
        internal string Name;

        internal string Value;

        internal WorldTileTone Tone;

        internal string Tip;

        internal WorldTileFact(string name, string value, WorldTileTone tone = WorldTileTone.Plain,
            string tip = null)
        {
            Name = name;
            Value = value;
            Tone = tone;
            Tip = tip;
        }

        internal Color ColorIn(UIColorPaletteDef palette)
        {
            switch (Tone)
            {
                case WorldTileTone.Good:
                    return palette.Success;

                case WorldTileTone.Warning:
                    return palette.Warning;

                case WorldTileTone.Bad:
                    return palette.Danger;

                case WorldTileTone.Quiet:
                    return palette.TextDisabled;

                default:
                    return palette.TextPrimary;
            }
        }
    }

    /// <summary>
    /// Everything the terrain tab knows about a tile, read once and grouped by the question it answers.
    ///
    /// <b>The readings are vanilla's, taken one at a time.</b> Every figure here is the same call
    /// <c>WITab_Terrain</c> makes; what changes is that they arrive as data rather than as rows already pushed
    /// through a <c>Listing_Standard</c>, so the panel can group them, colour them and put three of them in a
    /// header.
    ///
    /// <b>Grouped by decision, not by data type.</b> Movement difficulty sits with the terrain that causes it;
    /// pollution sits with the disease it belongs beside. The vanilla tab's three hairlines separate elevation
    /// from the hilliness it describes and give pollution a section of its own.
    /// </summary>
    internal static class WorldTileFacts
    {
        /// <summary>Growing period, average temperature and disease frequency: the three that decide it.</summary>
        internal static void Header(Tile tile, PlanetTile planetTile, List<WorldTileFact> into)
        {
            into.Clear();

            into.Add(new WorldTileFact("growing", Growing(planetTile), GrowingTone(planetTile)));
            into.Add(new WorldTileFact("avg temp", TemperatureText.Of(tile.temperature)));

            float disease = Disease(tile);

            into.Add(new WorldTileFact("disease / yr", disease.ToString("F1"),
                disease >= 1f ? WorldTileTone.Warning : WorldTileTone.Plain));
        }

        internal static void Living(Tile tile, PlanetTile planetTile, List<WorldTileFact> into)
        {
            into.Clear();

            into.Add(new WorldTileFact("Growing period", Growing(planetTile), GrowingTone(planetTile)));

            into.Add(new WorldTileFact("Average temperature",
                TemperatureText.Of(tile.temperature) + "  " + TemperatureText.Of(tile.MinTemperature) + " to "
                + TemperatureText.Of(tile.MaxTemperature)));

            into.Add(new WorldTileFact("Rainfall", tile.rainfall.ToString("N0") + " mm"));

            BiomeDef biome = tile.PrimaryBiome;

            if (biome != null && biome.foragedFood != null && biome.forageability > 0f)
            {
                into.Add(new WorldTileFact("Forageability",
                    biome.forageability.ToStringPercent() + "  " + biome.foragedFood.label,
                    biome.forageability >= 1f ? WorldTileTone.Good : WorldTileTone.Plain));
            }
            else
            {
                into.Add(new WorldTileFact("Forageability", "None", WorldTileTone.Quiet));
            }

            bool grazing = UIGuard.Try("WorldTile.Grazing",
                () => VirtualPlantsUtility.EnvironmentAllowsEatingVirtualPlantsNowAt(planetTile), false, null);

            into.Add(new WorldTileFact("Animals can graze now", grazing ? "Yes" : "No",
                grazing ? WorldTileTone.Good : WorldTileTone.Quiet));
        }

        internal static void Ground(Tile tile, PlanetTile planetTile, List<WorldTileFact> into)
        {
            into.Clear();

            if (tile.HillinessLabel != Hilliness.Undefined)
                into.Add(new WorldTileFact("Terrain", tile.HillinessLabel.GetLabelCap()));

            UIGuard.Try("WorldTile.Difficulty", () =>
            {
                if (Find.World.Impassable(planetTile))
                {
                    into.Add(new WorldTileFact("Movement difficulty", "Impassable", WorldTileTone.Bad));

                    return;
                }

                float move = WorldPathGrid.CalculatedMovementDifficultyAt(planetTile, false)
                             * Find.WorldGrid.GetRoadMovementDifficultyMultiplier(planetTile,
                                 PlanetTile.Invalid);

                into.Add(new WorldTileFact("Movement difficulty", move.ToString("0.#"),
                    move <= 1.01f ? WorldTileTone.Good : move >= 3f ? WorldTileTone.Warning
                        : WorldTileTone.Plain));
            }, null);

            into.Add(new WorldTileFact("Elevation", Elevation(tile)));

            if (tile.PrimaryBiome != null && tile.PrimaryBiome.canBuildBase)
            {
                string stone = UIGuard.Try<string>("WorldTile.Stone",
                    () => Find.World.NaturalRockTypesIn(planetTile).Select(rock => rock.label)
                        .ToCommaList(true).CapitalizeFirst(), null, null);

                if (!stone.NullOrEmpty())
                    into.Add(new WorldTileFact("Stone types", stone));
            }

            SurfaceTile surface = tile as SurfaceTile;

            if (surface == null)
                return;

            UIGuard.Try("WorldTile.Ways", () =>
            {
                if (surface.Roads != null)
                {
                    into.Add(new WorldTileFact("Road",
                        surface.Roads.Select(link => link.road.label).Distinct().ToCommaList(true)
                            .CapitalizeFirst(), WorldTileTone.Good));
                }

                if (surface.Rivers != null)
                {
                    into.Add(new WorldTileFact("River",
                        surface.Rivers.MaxBy(link => link.river.degradeThreshold).river.LabelCap,
                        WorldTileTone.Good));
                }
            }, null);
        }

        /// <summary>
        /// What would make this a bad place to live, and the landmark's own words about it.
        ///
        /// Pollution sits here rather than in a section of its own: it is a reason not to settle, which is the
        /// same thing the disease rate and a hostile mutator are.
        /// </summary>
        internal static void Hazards(Tile tile, PlanetTile planetTile, List<WorldTileFact> into)
        {
            into.Clear();

            if (tile.Mutators != null && tile.Mutators.Any())
            {
                string names = UIGuard.Try<string>("WorldTile.Mutators",
                    () => tile.Mutators.OrderBy(m => -m.displayPriority).Select(m => m.Label(planetTile))
                        .ToCommaList().CapitalizeFirst(), null, null);

                if (!names.NullOrEmpty())
                    into.Add(new WorldTileFact("Tile mutators", names, WorldTileTone.Warning));
            }

            if (!ModsConfig.BiotechActive)
                return;

            float pollution = tile.pollution;

            into.Add(new WorldTileFact("Pollution",
                pollution <= 0f ? "None" : pollution.ToStringPercent(),
                pollution <= 0f ? WorldTileTone.Quiet
                    : pollution >= 0.5f ? WorldTileTone.Bad : WorldTileTone.Warning));

            float nearby = UIGuard.Try("WorldTile.NearbyPollution",
                () => WorldPollutionUtility.CalculateNearbyPollutionScore(planetTile), 0f, null);

            into.Add(new WorldTileFact("Nearby pollution", nearby <= 0f ? "None" : nearby.ToString("F2"),
                nearby <= 0f ? WorldTileTone.Quiet : WorldTileTone.Warning));
        }

        /// <summary>The landmark's description, or null. Shown under the hazards it usually explains.</summary>
        internal static string Lore(Tile tile)
        {
            return UIGuard.Try<string>("WorldTile.Lore", () =>
            {
                if (ModsConfig.OdysseyActive && tile.Landmark != null && tile.Landmark.def != null)
                    return tile.Landmark.def.description;

                return tile.PrimaryBiome != null ? tile.PrimaryBiome.description : null;
            }, null, null);
        }

        /// <summary>The tile's name: its landmark if it has one, otherwise its biome.</summary>
        internal static string Name(Tile tile)
        {
            return UIGuard.Try<string>("WorldTile.Name", () =>
            {
                if (ModsConfig.OdysseyActive && tile.Landmark != null && !tile.Landmark.name.NullOrEmpty())
                    return tile.Landmark.name;

                return tile.PrimaryBiome != null ? tile.PrimaryBiome.LabelCap.ToString() : "Tile";
            }, "Tile", null);
        }

        /// <summary>The biome and the coordinates, which is the line under the name.</summary>
        internal static string Where(Tile tile, PlanetTile planetTile)
        {
            return UIGuard.Try<string>("WorldTile.Where", () =>
            {
                Vector2 longLat = Find.WorldGrid.LongLatOf(planetTile);

                string biome = tile.PrimaryBiome != null ? tile.PrimaryBiome.LabelCap.ToString() : null;
                string place = longLat.y.ToStringLatitude() + " " + longLat.x.ToStringLongitude();

                return biome.NullOrEmpty() ? place : biome.ToUpperInvariant() + "  " + place;
            }, null, null);
        }

        private static string Growing(PlanetTile planetTile)
        {
            return UIGuard.Try<string>("WorldTile.Growing",
                () => Zone_Growing.GrowingQuadrumsDescription(planetTile), "?", null);
        }

        /// <summary>
        /// Whether the growing period is good news.
        ///
        /// Read off the quadrum count rather than off the sentence, because the sentence is translated and a
        /// panel that only colours English is worse than one that colours nothing.
        /// </summary>
        private static WorldTileTone GrowingTone(PlanetTile planetTile)
        {
            return UIGuard.Try("WorldTile.GrowingTone", () =>
            {
                // The same two values GrowingQuadrumsDescription falls back to when no plant is named,
                // so the colour agrees with the sentence it is colouring.
                List<Twelfth> twelfths = GenTemperature.TwelfthsInAverageTemperatureRange(planetTile,
                    Plant.DefaultMinOptimalGrowthTemperature,
                    Plant.DefaultMaxOptimalGrowthTemperature);

                if (twelfths == null || twelfths.Count == 0)
                    return WorldTileTone.Bad;

                if (twelfths.Count >= 12)
                    return WorldTileTone.Good;

                return twelfths.Count <= 3 ? WorldTileTone.Warning : WorldTileTone.Plain;
            }, WorldTileTone.Plain, null);
        }

        private static float Disease(Tile tile)
        {
            return UIGuard.Try("WorldTile.Disease",
                () => tile.PrimaryBiome == null ? 0f : 60f / tile.PrimaryBiome.diseaseMtbDays, 0f, null);
        }

        private static string Elevation(Tile tile)
        {
            return UIGuard.Try<string>("WorldTile.Elevation",
                () => tile.Layer.Def.elevationString.Formatted(tile.elevation.ToString("F0")),
                tile.elevation.ToString("F0") + "m", null);
        }
    }
}
