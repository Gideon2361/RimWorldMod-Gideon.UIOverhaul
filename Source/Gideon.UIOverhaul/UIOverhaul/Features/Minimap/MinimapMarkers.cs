using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Minimap
{
    /// <summary>What a marker is, which decides its colour and nothing else.</summary>
    internal enum MinimapMarkerKind
    {
        Colonist,
        Downed,
        Hostile,
        Animal
    }

    internal struct MinimapMarker
    {
        internal int X;
        internal int Z;
        internal MinimapMarkerKind Kind;

        /// <summary>
        /// Whether this animal hunts.
        ///
        /// <b>A flag beside the kind rather than a kind of its own,</b> because the two answer different questions
        /// and a predator can be either. A warg on the far ridge is wildlife; the same warg in a manhunter pack is
        /// a hostile, and trading the red away for a paw colour would lose the more urgent of the two facts. So the
        /// kind keeps deciding the colour and this decides the shape.
        /// </summary>
        internal bool Predator;
    }

    /// <summary>
    /// Where the people are, refreshed four times a second.
    ///
    /// <b>Separate from the baked picture because it changes at a completely different rate.</b> Ground and
    /// walls are rebaked every few seconds; pawns move constantly. Baking them into the texture would force a
    /// full 160,000 cell rebuild every time anybody took a step, and drawing them from a live scan every frame
    /// would walk the whole colony sixty times a second to move a handful of dots three pixels. Neither is
    /// necessary: the list is rebuilt on a short timer and drawn from cache in between.
    ///
    /// <b>Fog is applied here, and it is a deliberate limit on what this feature is allowed to tell you.</b> A
    /// pawn standing in unexplored ground is not listed, so a raid crossing the far edge of the map does not
    /// appear until the colony can actually see it. Drawing them would turn the minimap into an intelligence
    /// source the base game does not give you, which is a different feature from the one that was asked for.
    /// </summary>
    internal static class MinimapMarkers
    {
        /// <summary>How long a marker list stands before it is rebuilt. Aaron's number.</summary>
        private const float RefreshSeconds = 0.25f;

        private static readonly List<MinimapMarker> Markers = new List<MinimapMarker>();

        private static Map builtFor;
        private static float builtAt = float.NegativeInfinity;
        private static int hostileCount;

        /// <summary>How many hostiles the colony can currently see, for the panel's footer.</summary>
        internal static int VisibleHostiles => hostileCount;

        /// <summary>
        /// The markers for this map, rebuilt if they have gone stale.
        ///
        /// The returned list is the live one rather than a copy: it is read and drawn immediately by the only
        /// caller, and handing out a fresh list four times a second to avoid a theoretical aliasing problem
        /// would be the more wasteful of the two mistakes.
        /// </summary>
        internal static List<MinimapMarker> For(Map map)
        {
            if (map == null)
            {
                Markers.Clear();
                hostileCount = 0;

                return Markers;
            }

            // Real time, not ticks, so the dots keep up while the game is paused and somebody is panning around
            // looking at things.
            bool stale = map != builtFor || Time.realtimeSinceStartup - builtAt >= RefreshSeconds;

            if (stale)
            {
                UIGuard.Try("Minimap.Markers", () => Rebuild(map), null);

                builtFor = map;
                builtAt = Time.realtimeSinceStartup;
            }

            return Markers;
        }

        private static void Rebuild(Map map)
        {
            Markers.Clear();
            hostileCount = 0;

            IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
            FogGrid fog = map.fogGrid;

            if (pawns == null)
                return;

            Faction player = Faction.OfPlayerSilentFail;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];

                if (pawn == null || pawn.Dead)
                    continue;

                IntVec3 cell = pawn.Position;

                if (!cell.InBounds(map))
                    continue;

                // The honesty rule, applied to every pawn rather than only to hostiles. A tame animal wandering
                // through unexplored ground is equally unknown to the colony, and a rule with an exception in it
                // is a rule somebody will read wrongly later.
                if (fog != null && fog.IsFogged(cell))
                    continue;

                MinimapMarkerKind kind = KindOf(pawn, player);

                if (kind == MinimapMarkerKind.Hostile)
                    hostileCount++;

                Markers.Add(new MinimapMarker
                {
                    X = cell.x,
                    Z = cell.z,
                    Kind = kind,
                    Predator = pawn.RaceProps != null && pawn.RaceProps.predator
                });
            }
        }

        /// <summary>
        /// What this pawn counts as.
        ///
        /// Order matters. Hostility is asked first because a hostile animal is a threat rather than wildlife,
        /// and a manhunter pack drawn in the same colour as a passing squirrel is the one case where getting
        /// this wrong costs somebody a colony.
        /// </summary>
        private static MinimapMarkerKind KindOf(Pawn pawn, Faction player)
        {
            if (player != null && pawn.HostileTo(player))
                return MinimapMarkerKind.Hostile;

            // A tamed animal counts as ours, from 2026-08-23 on Aaron's instruction. Orange means wildlife --
            // something the colony does not own and mostly does not care about the position of -- and a muffalo
            // the player paid for and can order about is not that. The faction is the whole test: taming is what
            // sets it, and it is what every other part of the game reads to answer the same question.
            bool ours = pawn.IsColonist || pawn.IsColonyMech
                        || (pawn.RaceProps != null && pawn.RaceProps.Animal && player != null
                            && pawn.Faction == player);

            // Downed applies to them too rather than only to colonists. A cow bleeding out reads the same as a
            // colonist bleeding out, which is what the colour is for, and leaving tamed animals blue while downed
            // would have invented an inconsistency this did not have before.
            if (ours)
                return pawn.Downed ? MinimapMarkerKind.Downed : MinimapMarkerKind.Colonist;

            if (pawn.RaceProps != null && pawn.RaceProps.Animal)
                return MinimapMarkerKind.Animal;

            // Everything else that is neither ours nor hostile: visitors, traders, prisoners being escorted.
            // Drawn as colonists rather than given a colour of their own, because at one pixel a fifth colour
            // is a legend nobody reads rather than information anybody uses.
            return MinimapMarkerKind.Colonist;
        }

        /// <summary>Drops the cache, for a game ending or a map being left.</summary>
        internal static void Clear()
        {
            Markers.Clear();
            builtFor = null;
            builtAt = float.NegativeInfinity;
            hostileCount = 0;
        }
    }
}
