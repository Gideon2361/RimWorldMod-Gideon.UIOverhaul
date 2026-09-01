using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.TilePreview
{
    /// <summary>One tile's picture and its figures, held until the cache runs out of room.</summary>
    internal sealed class TilePreviewEntry
    {
        internal Texture2D Texture;

        internal TilePreviewReading Reading;

        /// <summary>Whether the field could be read at all. A tile that failed is remembered as failed.</summary>
        internal bool Valid;

        /// <summary>
        /// The biome's name, read once here rather than off the grid every frame.
        ///
        /// The panel draws inside a guard that retires on its first failure, so a null biome reached from the
        /// draw would cost the whole preview for the session rather than one label.
        /// </summary>
        internal string Biome;
    }

    /// <summary>
    /// Previews already drawn, so moving the cursor back over a tile is free.
    ///
    /// <b>A preview is expensive once and free afterwards, which is what makes hovering affordable.</b> Reading
    /// the field is two noise passes over sixty thousand cells and the picture is a texture upload; doing that
    /// every frame the cursor rests on a tile would be absurd, and doing it once per tile is nothing.
    ///
    /// <b>Bounded, and the textures are destroyed rather than dropped.</b> Every entry holds a
    /// <c>Texture2D</c>, which is unmanaged memory the garbage collector will not reclaim on its own: a player
    /// sweeping the cursor across a continent would leak a quarter of a megabyte per tile until the session
    /// ended. The whole cache is emptied when it fills rather than evicted one at a time, because the access
    /// pattern here is a sweep rather than a working set and tracking recency would cost more than it saves.
    ///
    /// <b>Emptied when the world changes,</b> which is what the stored seed is for: a new game on the same
    /// session has different tiles behind the same numbers.
    /// </summary>
    internal static class TilePreviewCache
    {
        /// <summary>
        /// How many previews are held.
        ///
        /// A 250 by 250 texture at four bytes a pixel is 250 kilobytes, so this is about six megabytes at the
        /// default map size. Enough to compare a cluster of candidate tiles without re-reading any of them.
        /// </summary>
        private const int Limit = 24;

        private static readonly Dictionary<PlanetTile, TilePreviewEntry> Entries =
            new Dictionary<PlanetTile, TilePreviewEntry>();

        private static int seed;

        private static bool seeded;

        internal static TilePreviewEntry For(PlanetTile tile)
        {
            World world = Find.World;

            if (world == null)
                return null;

            Check(world.info.Seed);

            TilePreviewEntry entry;

            if (Entries.TryGetValue(tile, out entry))
                return entry;

            if (Entries.Count >= Limit)
                Clear();

            entry = Make(tile);

            Entries[tile] = entry;

            return entry;
        }

        private static TilePreviewEntry Make(PlanetTile tile)
        {
            TilePreviewField field = TilePreviewGenerator.For(tile);

            if (field == null)
                return new TilePreviewEntry { Valid = false };

            TilePreviewReading reading;

            Texture2D texture = TilePreviewImage.Render(field, out reading);

            return new TilePreviewEntry
            {
                Texture = texture,
                Reading = reading,
                Valid = texture != null,
                Biome = UIGuard.Try<string>("TilePreview.Biome",
                    () => field.Tile.PrimaryBiome != null ? field.Tile.PrimaryBiome.LabelCap.ToString() : null,
                    null, null)
            };
        }

        private static void Check(int current)
        {
            if (seeded && seed == current)
                return;

            seeded = true;
            seed = current;

            Clear();
        }

        /// <summary>Drops every preview, destroying the textures rather than leaving them to Unity.</summary>
        internal static void Clear()
        {
            UIGuard.Try("TilePreview.Clear", () =>
            {
                foreach (KeyValuePair<PlanetTile, TilePreviewEntry> pair in Entries)
                {
                    if (pair.Value != null && pair.Value.Texture != null)
                        Object.Destroy(pair.Value.Texture);
                }
            }, null);

            Entries.Clear();
        }
    }
}
