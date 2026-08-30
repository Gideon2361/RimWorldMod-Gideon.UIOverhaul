using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// Which world tile a map sits on, for the things that need a climate or a longitude.
    ///
    /// <b>Because a pocket map does not sit on one, and asking throws.</b> A gravship interior, an undercave or
    /// an anomaly pocket has a <c>Tile</c> that indexes nothing, so the world grid answers with an
    /// <c>ArgumentOutOfRangeException</c> rather than a null or an invalid tile. Reported 2026-08-28 from a
    /// pocket map, where it cost the whole calendar widget: five places across this mod read
    /// <c>LongLatOf(map.Tile)</c> and none of them expected a map without a tile.
    ///
    /// <b>It borrows the map the pocket hangs off rather than answering nothing.</b> An undercave is under a
    /// real tile and a gravship departed from one, so the surface climate is what a player down there is
    /// actually asking about -- and a calendar that goes blank whenever you step inside is worse than one that
    /// keeps describing the colony you came from.
    ///
    /// <b>A pocket map is not the only map off the grid, which is why the real test asks the grid.</b> The first
    /// version of this checked <c>IsPocketMap</c> and stopped there, and the error came straight back from
    /// underground maps -- which are not pocket maps at all but ordinary tiles on another planet layer, and can
    /// still fail to resolve. Enumerating the kinds of map that have no tile is a list that grows with every
    /// expansion. <see cref="Resolves"/> asks the only question that matters instead: does the world grid
    /// actually have a record for this.
    ///
    /// <b>A map with no growing season is a normal state, not a fault.</b> That is the whole reason this returns
    /// an invalid tile rather than letting the exception reach <c>UIGuard</c>: an error in the log every time
    /// the player steps underground describes the game working as intended.
    /// </summary>
    internal static class MapTile
    {
        /// <summary>The tile to read a climate or a longitude from, or an invalid one when there is none.</summary>
        internal static PlanetTile Of(Map map)
        {
            if (map == null)
                return PlanetTile.Invalid;

            if (!map.IsPocketMap)
                return Resolves(map.Tile) ? map.Tile : PlanetTile.Invalid;

            PocketMapParent parent = map.Parent as PocketMapParent;
            Map source = parent == null ? null : parent.sourceMap;

            // One step out only. A pocket inside a pocket is a chain this has no reason to walk, and a loop here
            // would hang the interface rather than lose a row of it.
            return source != null && !source.IsPocketMap && Resolves(source.Tile)
                ? source.Tile
                : PlanetTile.Invalid;
        }

        /// <summary>
        /// Whether the world grid can actually produce a record for this tile.
        ///
        /// <b><c>Valid</c> is not the same question and does not answer this one.</b> It asks only whether the id
        /// is non-negative. The grid's indexer is <c>tile.Layer.Tiles[tile.tileId]</c>, which fails two further
        /// ways: an id past the end of that layer's list, and a layer id the world does not have registered.
        /// Neither is reachable through a property that returns false instead of throwing.
        ///
        /// <b>So the two shapes of "no such tile" are caught, and nothing else is.</b>
        /// <c>ArgumentOutOfRangeException</c> is the index; <c>KeyNotFoundException</c> is the layer lookup. Any
        /// other exception is a real fault and is left to reach <c>UIGuard</c> and be reported, because the point
        /// of swallowing these two is that they are not faults -- a map off the world grid is an ordinary thing
        /// to be, and reporting it once per map switch is noise about a state the game is entitled to be in.
        ///
        /// <b>Asked here rather than at each call site,</b> so the growing season, the longitude and the date all
        /// get the same answer. The cost is a dictionary lookup and a list index on a path that runs once a
        /// frame at worst.
        /// </summary>
        private static bool Resolves(PlanetTile tile)
        {
            if (!tile.Valid || Find.WorldGrid == null)
                return false;

            try
            {
                return Find.WorldGrid[tile] != null;
            }
            catch (System.ArgumentOutOfRangeException)
            {
                return false;
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                return false;
            }
        }

        /// <summary>
        /// Longitude for date arithmetic, falling back to zero.
        ///
        /// Zero is a real answer rather than a refusal: longitude only decides where a day falls, so a date read
        /// against it is right to within a fraction of one. Every caller here is drawing a date or a season, and
        /// none of them would rather draw nothing.
        /// </summary>
        internal static float LongitudeOf(Map map)
        {
            PlanetTile tile = Of(map);

            return tile.Valid && Find.WorldGrid != null ? Find.WorldGrid.LongLatOf(tile).x : 0f;
        }

        /// <summary>The full longitude and latitude pair, for the readouts that show both.</summary>
        internal static Vector2 LongLatOf(Map map)
        {
            PlanetTile tile = Of(map);

            return tile.Valid && Find.WorldGrid != null ? Find.WorldGrid.LongLatOf(tile) : Vector2.zero;
        }
    }
}
