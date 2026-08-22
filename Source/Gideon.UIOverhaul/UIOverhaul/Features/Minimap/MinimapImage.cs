using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Minimap
{
    /// <summary>
    /// The baked picture of a map: ground, structures, and what the colony has not explored.
    ///
    /// <b>One texture pixel per map cell, and nothing smaller.</b> A 400 by 400 map is a 400 by 400 texture,
    /// drawn with point filtering so it scales up as blocks rather than as a blur. Smoothing it would cost the
    /// one thing the minimap is for: a wall is a single cell, and a filtered wall is indistinguishable from the
    /// dirt beside it.
    ///
    /// <b>Baked on a timer rather than kept live.</b> Ground and walls change rarely and cost 160,000 cells to
    /// read, so they are rebuilt every few seconds. The alternative is hooking every terrain change, every
    /// construction, every deconstruction and every roof collapse in the game, which is a great deal of
    /// plumbing to remove a staleness nobody can see. Pawns and the camera rectangle are not in here at all --
    /// they move constantly and are drawn over the top. See <see cref="MinimapWidget"/>.
    ///
    /// <b>Colour is resolved per terrain, not per cell.</b> A map has tens of thousands of cells and a few dozen
    /// distinct terrains, so the def to colour step is cached and the per-cell loop is an array read and an
    /// assignment.
    /// </summary>
    internal static class MinimapImage
    {
        /// <summary>How long a baked picture is allowed to stand before it is rebuilt. Aaron's number.</summary>
        private const float RebakeSeconds = 5f;

        /// <summary>
        /// Structures are one flat tone rather than their real material colour.
        ///
        /// At one pixel per cell the question a player asks is "where are my walls", not "what are they made
        /// of". A granite wall and a wooden one rendered in their own colours are two greys that both disappear
        /// into the ground; one deliberately light tone makes the colony's shape read at a glance.
        /// </summary>
        private static readonly Color32 Structure = new Color32(138, 132, 120, 255);

        /// <summary>Unexplored ground. Near black rather than black, so the panel does not look like a hole.</summary>
        private static readonly Color32 Unexplored = new Color32(11, 13, 16, 255);

        /// <summary>Anything with no terrain at all, which should not happen but must still have a colour.</summary>
        private static readonly Color32 Nothing = new Color32(20, 22, 26, 255);

        private sealed class Baked
        {
            internal Texture2D Texture;
            internal Color32[] Pixels;
            internal float BakedAt = float.NegativeInfinity;
            internal int Width;
            internal int Height;
        }

        private static readonly Dictionary<Map, Baked> Cache = new Dictionary<Map, Baked>();

        /// <summary>
        /// The last bake reading written to the log for each map, so the same one is not written again.
        ///
        /// Keyed by map beside the picture cache and pruned with it, because a map that has been left and
        /// returned to is worth a fresh line: its terrain sampling starts over.
        /// </summary>
        private static readonly Dictionary<Map, string> Reported = new Dictionary<Map, string>();

        /// <summary>
        /// The picture for this map, rebaked if it has gone stale. Null when there is nothing to draw.
        /// </summary>
        internal static Texture2D For(Map map)
        {
            if (map == null)
                return null;

            return UIGuard.Try("Minimap.Bake", () => Resolve(map), null,
                "The minimap is not drawing. Nothing else is affected.");
        }

        private static Texture2D Resolve(Map map)
        {
            Baked baked;

            if (!Cache.TryGetValue(map, out baked))
            {
                baked = new Baked();
                Cache[map] = baked;
            }

            // Real time rather than ticks, because the interface keeps running while the game is paused and a
            // minimap frozen at the moment somebody hit space would be a bug report.
            if (baked.Texture != null && Time.realtimeSinceStartup - baked.BakedAt < RebakeSeconds)
                return baked.Texture;

            Bake(map, baked);

            return baked.Texture;
        }

        private static void Bake(Map map, Baked baked)
        {
            IntVec3 size = map.Size;

            if (size.x <= 0 || size.z <= 0)
                return;

            if (baked.Texture == null || baked.Width != size.x || baked.Height != size.z)
            {
                Release(baked);

                baked.Texture = new Texture2D(size.x, size.z, TextureFormat.RGBA32, false)
                {
                    // The whole point of one pixel per cell: scaled up, a cell stays a block.
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };

                baked.Pixels = new Color32[size.x * size.z];
                baked.Width = size.x;
                baked.Height = size.z;
            }

            TerrainDef[] terrain = map.terrainGrid?.topGrid;
            Building[] edifices = map.edificeGrid?.InnerArray;
            FogGrid fog = map.fogGrid;

            if (terrain == null)
                return;

            Color32[] pixels = baked.Pixels;

            for (int z = 0; z < size.z; z++)
            {
                // Row offsets hoisted out of the inner loop. This runs 160,000 times on a large map and the
                // multiply is the only arithmetic in it worth removing.
                int rowStart = z * size.x;

                for (int x = 0; x < size.x; x++)
                {
                    int index = rowStart + x;

                    // Texture rows run bottom to top and so do map cells, so the index is the same in both and
                    // no flip is needed. Getting this wrong renders the colony upside down, which is subtle
                    // enough on a symmetrical map to survive a casual look.
                    pixels[index] = ColorAt(terrain, edifices, fog, index);
                }
            }

            PaintPlants(map, pixels, size, fog, edifices);

            baked.Texture.SetPixels32(pixels);

            // updateMipmaps false: the texture has none, and asking for them costs a pass over the whole thing.
            baked.Texture.Apply(false);

            baked.BakedAt = Time.realtimeSinceStartup;

            Report(map, terrain, edifices, fog, size);
        }

        /// <summary>
        /// Paints the plants over the ground.
        ///
        /// <b>This is where a RimWorld map gets its colour, and leaving it out was the whole problem.</b>
        /// Terrain is brown: soil, gravel, sand and rock sample to a set of muted browns between about 50 and
        /// 90, with almost no contrast between any of them. Grass is not terrain at all -- it is
        /// <c>Plant_Grass</c>, a Thing standing on ordinary soil -- and so are the bushes and trees. A picture
        /// of terrain alone is an accurate picture of dirt.
        ///
        /// <b>Walked as a list of plants rather than as a grid.</b> The lister already keeps plants grouped, so
        /// this visits the tens of thousands of plants on the map instead of testing all 160,000 cells for one.
        ///
        /// <b>Blended rather than painted flat.</b> Grass on the map does not hide the soil under it, and a
        /// minimap where one sparse plant turns a whole cell solid green reads as a lawn rather than as scrub.
        /// The weight is what a cell of grass covers, near enough.
        /// </summary>
        private static void PaintPlants(Map map, Color32[] pixels, IntVec3 size, FogGrid fog,
            Building[] edifices)
        {
            List<Thing> plants = map.listerThings?.ThingsInGroup(ThingRequestGroup.Plant);

            if (plants == null)
                return;

            for (int i = 0; i < plants.Count; i++)
            {
                Thing plant = plants[i];

                if (plant?.def == null)
                    continue;

                IntVec3 cell = plant.Position;

                if (cell.x < 0 || cell.z < 0 || cell.x >= size.x || cell.z >= size.z)
                    continue;

                int index = cell.z * size.x + cell.x;

                // The same two rules the ground pass follows: what the colony cannot see is not drawn, and a
                // structure is what matters about a cell it stands on.
                if (fog != null && fog.IsFogged(index))
                    continue;

                if (edifices != null && index < edifices.Length && edifices[index] != null)
                    continue;

                pixels[index] = Blend(pixels[index], MinimapTerrainColors.ForThing(plant.def), PlantCover);
            }
        }

        /// <summary>
        /// How much of a cell a plant is taken to cover.
        ///
        /// Not a physical quantity, a legibility one: high enough that a forest reads as forest, low enough
        /// that the ground still shows through scrub and the colony's floors are not tinted by the odd potato.
        /// </summary>
        private const float PlantCover = 0.75f;

        private static Color32 Blend(Color32 under, Color32 over, float amount)
        {
            float keep = 1f - amount;

            return new Color32(
                (byte) (under.r * keep + over.r * amount),
                (byte) (under.g * keep + over.g * amount),
                (byte) (under.b * keep + over.b * amount),
                255);
        }

        /// <summary>
        /// Says what the bake actually saw, when debug logging is on.
        ///
        /// <b>Here because a wrong minimap all looks the same.</b> Fogged, missing terrain and a terrain whose
        /// colour resolves to nothing all render as the same dark panel, and telling them apart by reasoning
        /// about the code is guesswork. One line naming which branch dominated, and the terrain actually under
        /// the middle of the map, turns the next report into a fact rather than another theory.
        ///
        /// Off unless <c>debugLogging</c> is set.
        ///
        /// <b>Once per map, and again only when the reading changes.</b> It used to write a line on every bake,
        /// which is one every five seconds for as long as the game runs: ninety five lines in eight minutes of
        /// Aaron's log on 2026-08-22, where they buried a real exception that had been reported in the middle of
        /// them. A diagnostic that drowns the thing it is meant to help you find is doing harm.
        ///
        /// <b>What counts as a change is deliberately narrow.</b> Fogged and structure counts move constantly as
        /// the colony explores and builds, so keying on those would print every time again. The signature is the
        /// three numbers that say whether the sampling is working: how many terrains were sampled, how many fell
        /// back to a guess, and how many distinct colours came out. Those settle once a map is loaded and only
        /// move when something new appears, which is exactly when another line is worth reading.
        ///
        /// <b>The cell walk happens after that decision, not before it.</b> Counting fogged, structure and
        /// missing cells is a pass over the whole map, and it exists only to fill in the line. Doing it first
        /// would leave the expensive half running every five seconds to produce nothing.
        /// </summary>
        private static void Report(Map map, TerrainDef[] terrain, Building[] edifices, FogGrid fog, IntVec3 size)
        {
            if (!UIDebug.Enabled || map == null)
                return;

            string signature = MinimapTerrainColors.Sampled + "/" + MinimapTerrainColors.Fallbacks + "/"
                               + MinimapTerrainColors.Distinct;

            string last;

            if (Reported.TryGetValue(map, out last) && last == signature)
                return;

            Reported[map] = signature;

            int cells = size.x * size.z;
            int fogged = 0;
            int structures = 0;
            int missing = 0;

            for (int i = 0; i < cells; i++)
            {
                if (fog != null && fog.IsFogged(i))
                    fogged++;
                else if (edifices != null && i < edifices.Length && edifices[i] != null)
                    structures++;
                else if (i >= terrain.Length || terrain[i] == null)
                    missing++;
            }

            TerrainDef middle = terrain.Length > cells / 2 ? terrain[cells / 2] : null;

            Log.Message(UILogTag.Prefix + "Minimap baked " + size.x + "x" + size.z + " for "
                        + map.ToStringSafe() + ": " + fogged + " fogged, " + structures + " structures, "
                        + missing + " with no terrain, terrainGrid length " + terrain.Length
                        + ", centre terrain " + (middle == null ? "null" : middle.defName)
                        + ", terrain sampled from texture " + MinimapTerrainColors.Sampled
                        + ", fell back " + MinimapTerrainColors.Fallbacks
                        + ". Colours: " + MinimapTerrainColors.Describe(10));
        }

        private static Color32 ColorAt(TerrainDef[] terrain, Building[] edifices, FogGrid fog, int index)
        {
            // Fog first, and it wins outright. This is the decision that keeps the minimap honest: what the
            // colony has not seen is not drawn, so a raid sitting in unexplored ground is not visible here
            // either. See MinimapMarkers, which applies the same test to pawns.
            if (fog != null && fog.IsFogged(index))
                return Unexplored;

            if (edifices != null && index < edifices.Length && edifices[index] != null)
                return Structure;

            if (index >= terrain.Length)
                return Nothing;

            TerrainDef def = terrain[index];

            return def == null ? Nothing : ColorOf(def);
        }

        // The natural ground palette. Ours by design rather than read from the game, for the reason in
        // ColorOf: RimWorld does not store a colour for the terrain that covers most of a map.
        private static readonly Color32 Water = new Color32(36, 56, 74, 255);
        private static readonly Color32 Stone = new Color32(58, 58, 61, 255);
        private static readonly Color32 Grass = new Color32(79, 90, 50, 255);
        private static readonly Color32 Soil = new Color32(74, 63, 50, 255);
        private static readonly Color32 Barren = new Color32(110, 98, 72, 255);
        private static readonly Color32 Floor = new Color32(85, 80, 74, 255);

        /// <summary>
        /// What colour a terrain draws as.
        ///
        /// Handed to <see cref="MinimapTerrainColors"/>, which reads the terrain's own texture so the panel
        /// matches what is on the map, and keeps its own cache. This used to classify by tag here; that is now
        /// only <see cref="FallbackColor"/>, for a terrain whose texture cannot be sampled.
        /// </summary>
        private static Color32 ColorOf(TerrainDef def)
        {
            return MinimapTerrainColors.For(def);
        }

        /// <summary>
        /// A reasonable colour for a terrain whose texture could not be read.
        ///
        /// <b>Not the normal path, and deliberately kept.</b> Sampling can fail on a modded terrain with an
        /// unusual texture or on a machine where the readback is refused, and a minimap with a hole in it is
        /// worse than one with an approximate colour in that spot.
        ///
        /// The tags are RimWorld's own, so a modded terrain carrying Water or Soil lands in the right band
        /// without knowing this exists. A declared colour still beats any guess: white is the default rather
        /// than a choice, so it does not count as declared.
        /// </summary>
        internal static Color32 FallbackColor(TerrainDef def)
        {
            if (def.IsWater)
                return Water;

            // A declared colour beats any guess, and is how floors, carpets and stone tiles get their real
            // appearance. White is the default rather than a choice, so it does not count as declared.
            Color declared = def.DrawColor;

            if (!IsWhite(declared))
                return Darkened(declared);

            // <b>categoryType, not tags, and this is where the old version went wrong.</b> Soil carries no
            // tags at all in Core -- it declares categoryType Soil -- so HasTag("Soil") was false for the
            // terrain covering most of the map. It also leaves "natural" unset, so the next test sent it to
            // the constructed floor colour, and the whole map came out one flat grey.
            //
            // Compared as text rather than against the enum's members, so a RimWorld that adds a category does
            // not stop this compiling and an unknown one simply falls through to the fertility test below.
            string category = def.categoryType.ToString();

            if (category == "Soil")
            {
                // Fertility is the closest thing RimWorld has to "is anything growing here", which is what
                // separates green ground from brown at a glance.
                return def.fertility >= 0.9f ? Grass : Soil;
            }

            if (category == "Sand")
                return Barren;

            // No category worth the name. Fertility still separates ground that grows things from bare rock,
            // and a constructed floor has neither.
            if (def.fertility > 0f)
                return Soil;

            return def.natural ? Stone : Floor;
        }

        private static bool IsWhite(Color color)
        {
            return color.r > 0.99f && color.g > 0.99f && color.b > 0.99f;
        }

        /// <summary>
        /// Takes a declared colour down a little.
        ///
        /// Terrain colours are chosen to sit under RimWorld's own lighting and textures. At full strength on a
        /// flat panel they are bright enough that the markers drawn over them stop standing out, which costs
        /// the panel the thing it is for.
        /// </summary>
        private static Color32 Darkened(Color source)
        {
            return new Color32(
                (byte) Mathf.Clamp(source.r * 255f * 0.82f, 0f, 255f),
                (byte) Mathf.Clamp(source.g * 255f * 0.82f, 0f, 255f),
                (byte) Mathf.Clamp(source.b * 255f * 0.82f, 0f, 255f),
                255);
        }

        /// <summary>
        /// Drops the picture for any map that no longer exists.
        ///
        /// <b>Not optional housekeeping.</b> A pocket dimension entered and left repeatedly is a new Map every
        /// time, and a texture per visit is a leak that shows up as the session getting slower rather than as
        /// anything that points here. Called once a frame from the widget, which is cheap: it is a walk of a
        /// dictionary that holds one entry per loaded map.
        /// </summary>
        internal static void Prune()
        {
            if (Cache.Count == 0)
                return;

            UIGuard.Try("Minimap.Prune", () =>
            {
                List<Map> gone = null;

                foreach (KeyValuePair<Map, Baked> pair in Cache)
                {
                    if (pair.Key != null && Find.Maps != null && Find.Maps.Contains(pair.Key))
                        continue;

                    gone = gone ?? new List<Map>();
                    gone.Add(pair.Key);
                }

                if (gone == null)
                    return;

                foreach (Map map in gone)
                {
                    Release(Cache[map]);
                    Cache.Remove(map);
                    Reported.Remove(map);
                }
            }, null);
        }

        /// <summary>Throws away every picture, for a game ending or a save being loaded over this one.</summary>
        internal static void Clear()
        {
            foreach (KeyValuePair<Map, Baked> pair in Cache)
                Release(pair.Value);

            Cache.Clear();

            // What has been logged goes with the pictures, so the first bake of the next game says what it saw
            // rather than being silenced by a reading from the last one.
            Reported.Clear();

            // Sampled terrain colours go too. They are keyed by TerrainDef, which survives a reload, but a mod
            // list change between games can retire a def and there is no reason to hold the old ones.
            MinimapTerrainColors.Clear();
        }

        private static void Release(Baked baked)
        {
            if (baked?.Texture == null)
                return;

            // Destroy rather than dropping the reference. A Texture2D is unmanaged memory behind a managed
            // handle, and letting the garbage collector find it leaves the video memory held until it does.
            Object.Destroy(baked.Texture);

            baked.Texture = null;
            baked.Pixels = null;
        }
    }
}
