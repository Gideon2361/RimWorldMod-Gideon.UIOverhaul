using System.Collections.Generic;
using System.Linq;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.TilePreview
{
    /// <summary>Where a true analysis has got to.</summary>
    internal enum TilePreviewJobPhase
    {
        Idle,
        Preparing,
        Stepping,
        Reading,
        Done,
        Failed
    }

    /// <summary>
    /// The real map generator, run against a map the game never sees, one step per frame.
    ///
    /// <b>Why this exists.</b> <see cref="TilePreviewGenerator"/> reproduces one generation step in C# and
    /// therefore knows one step's worth of the truth. It cannot see a landmark carving a lake, a mutator
    /// dropping a chasm, or any of the generation a mod adds, and it does not fail when it is wrong: the
    /// arithmetic succeeds and returns a plausible map of a world that will not be generated. That estimate is
    /// the right thing to draw while a cursor sweeps a continent. It is the wrong thing to settle on.
    ///
    /// <b>So this runs Ludeon's own steps instead of imitating them.</b> Everything a mod adds, whether a
    /// generation step or a tile mutator, is picked up because the step list is assembled the way
    /// <c>MapGenerator.GenerateMap</c> assembles it, from the generator, the biome and the mutators, minus what
    /// any of them prevent. Nothing here knows what a lake is.
    ///
    /// <b>It stops at <see cref="MaxOrder"/>.</b> Ludeon documents the order bands in
    /// <c>CommonMapGenerator.xml</c>: grids below 100, natural terrain to 300, critical structures to 500,
    /// non-critical structures to 800, and from 850 on it is the player start spot, plants, geysers, animals
    /// and fog. A cut at 800 is therefore the sentence "terrain and structures, nothing that grows or moves"
    /// expressed as data rather than as a list of step names, so a mod's structure step is included and a mod's
    /// plant step is excluded without either being named here.
    ///
    /// <b>One step per frame, on the main thread, because it cannot be anywhere else.</b> <c>Rand</c> keeps its
    /// seed, its iteration count and its state stack in plain statics rather than thread statics, so generating
    /// on a worker would corrupt the stream the colony rolls against; and structure steps spawn things, which
    /// construct Unity objects. A step is atomic, so a step is the unit. The seed each one gets is computed the
    /// way <c>MapGenerator</c> computes it, from the map seed and the step's own <c>SeedPart</c> plus a
    /// disambiguator for repeats -- get that wrong and this is an expensive picture of a different map.
    ///
    /// <b>Nothing here is added to the game.</b> <c>GenerateMap</c> calls <c>Current.Game.AddMap</c>; this does
    /// not, so the map is unreachable from <c>Find.Maps</c>, is never drawn, and is dropped when the analysis
    /// ends. It is ordinary managed memory and the collector takes it back.
    /// </summary>
    internal static class TilePreviewJob
    {
        /// <summary>
        /// The last generation order this runs. See the class summary for why it is a number and not a list.
        /// </summary>
        internal const int MaxOrder = 800;

        /// <summary>How many frames without being advanced before an abandoned job releases its map.</summary>
        private const int StaleFrames = 5;

        private static readonly List<GenStepDef> Steps = new List<GenStepDef>();

        /// <summary>Writes <c>WorldObject.tile</c> without the setter, which dirties the world's tile finder.</summary>
        private static readonly AccessTools.FieldRef<WorldObject, PlanetTile> TileField =
            UIGuard.Try("TilePreview.TileField",
                () => AccessTools.FieldRefAccess<WorldObject, PlanetTile>("tile"), null,
                "A tile cannot be analyzed in full; the estimate is still shown.");

        private static Map map;

        private static MapParent parent;

        private static int seed;

        private static int index;

        private static int lastFrame;

        internal static TilePreviewJobPhase Phase { get; private set; }

        /// <summary>The tile being analyzed, or invalid when nothing is.</summary>
        internal static PlanetTile Tile { get; private set; }

        /// <summary>Steps finished over steps to run, for the readout and the zoom.</summary>
        internal static float Progress
        {
            get { return Steps.Count <= 0 ? 0f : Mathf.Clamp01(index / (float) Steps.Count); }
        }

        internal static int StepsDone
        {
            get { return index; }
        }

        internal static int StepsTotal
        {
            get { return Steps.Count; }
        }

        /// <summary>The step about to run, for the caption. Empty once there is none.</summary>
        internal static string CurrentLabel { get; private set; }

        internal static bool Running
        {
            get { return Phase == TilePreviewJobPhase.Preparing || Phase == TilePreviewJobPhase.Stepping; }
        }

        /// <summary>Begins an analysis of <paramref name="planetTile"/>, replacing whatever was running.</summary>
        internal static void Start(PlanetTile planetTile)
        {
            Cancel();

            if (!planetTile.Valid || TileField == null)
            {
                Phase = TilePreviewJobPhase.Failed;

                return;
            }

            Tile = planetTile;
            Phase = TilePreviewJobPhase.Preparing;
            index = 0;
            lastFrame = Time.frameCount;
            CurrentLabel = null;
        }

        /// <summary>
        /// Moves the analysis on by one step. Called once a frame while the panel is drawing.
        ///
        /// <b>A job nobody is advancing is a job nobody wants.</b> The world map stops drawing the moment the
        /// player settles or opens a screen over it, and an abandoned map here is tens of megabytes. Rather
        /// than hunting for every way out, this notices it has not been called and lets go.
        /// </summary>
        internal static void Advance()
        {
            if (!Running)
                return;

            lastFrame = Time.frameCount;

            // Something else has started generating a real map. Ours shares MapGenerator's static working data
            // with it, so there is no version of continuing that is correct.
            if (MapGenerator.mapBeingGenerated != null && MapGenerator.mapBeingGenerated != map)
            {
                Cancel();

                return;
            }

            bool ok = UIGuard.Try("TilePreview.Advance", () =>
            {
                if (Phase == TilePreviewJobPhase.Preparing)
                    return Prepare();

                return Step();
            }, false, null);

            if (!ok)
            {
                Cancel();

                Phase = TilePreviewJobPhase.Failed;
            }
        }

        /// <summary>Releases the map without finishing. Safe to call at any point, including from Idle.</summary>
        internal static void Cancel()
        {
            if (MapGenerator.mapBeingGenerated == map)
                MapGenerator.mapBeingGenerated = null;

            if (map != null)
                UIGuard.Try("TilePreview.Release", RockNoises.Reset, null);

            map = null;
            parent = null;
            Steps.Clear();
            index = 0;
            CurrentLabel = null;
            Tile = PlanetTile.Invalid;

            if (Running)
                Phase = TilePreviewJobPhase.Idle;
        }

        /// <summary>Drops an analysis that stopped being drawn, so its map is not held for the session.</summary>
        internal static void Sweep()
        {
            if (Running && Time.frameCount - lastFrame > StaleFrames)
                Cancel();
        }

        /// <summary>
        /// Builds the map and the step list, without adding either to the game.
        ///
        /// <b>The parent exists only because <c>MapInfo.Tile</c> reads through it.</b> It is never registered
        /// with <c>Find.WorldObjects</c>, and its tile is written straight to the field: the property's setter
        /// dirties the planet layer's tile finder, which is real world state and none of this feature's
        /// business.
        /// </summary>
        private static bool Prepare()
        {
            World world = Find.World;

            if (world == null || world.grid == null || !Tile.Valid)
                return false;

            MapGeneratorDef generator = MapGeneratorDefOf.Base_Player;

            if (generator == null)
                return false;

            parent = (MapParent) WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);

            if (parent == null)
                return false;

            TileField(parent) = Tile;

            map = new Map
            {
                // A sentinel rather than Find.UniqueIDsManager.GetNextMapID(), which would spend a real id from
                // the save on a map the save will never contain.
                uniqueID = -1,
                generationTick = GenTicks.TicksGame
            };

            map.events = new MapEvents(map);
            map.info.Size = world.info.initialMapSize;
            map.info.parent = parent;
            map.generatorDef = generator;

            map.ConstructComponents();

            seed = Gen.HashCombineInt(world.info.Seed, Tile.GetHashCode());

            MapGenerator.mapBeingGenerated = map;

            // Both of these draw from the seeded stream before the first step does, exactly as
            // GenerateContentsIntoMap has them, so the stream is where the first step expects it.
            Rand.PushState();

            try
            {
                Rand.Seed = seed;

                foreach (TileMutatorDef mutator in map.TileInfo.Mutators)
                {
                    if (mutator != null && mutator.Worker != null)
                        mutator.Worker.Init(map);
                }

                RockNoises.Init(map);
            }
            finally
            {
                Rand.PopState();
            }

            Collect(generator);

            Phase = Steps.Count > 0 ? TilePreviewJobPhase.Stepping : TilePreviewJobPhase.Reading;

            return true;
        }

        /// <summary>
        /// The steps this tile would really generate with, up to <see cref="MaxOrder"/>.
        ///
        /// Assembled the way <c>MapGenerator.GenerateMap</c> assembles it and then cut by order. The ordering
        /// and the <c>preventsGenSteps</c> pass are <c>GenerateContentsIntoMap</c>'s, kept because a step that
        /// suppresses another has to be able to do so here too.
        /// </summary>
        private static void Collect(MapGeneratorDef generator)
        {
            Steps.Clear();

            IEnumerable<GenStepDef> chosen = generator.genSteps.Where(Allowed);

            foreach (TileMutatorDef mutator in map.TileInfo.Mutators)
            {
                if (mutator != null && mutator.extraGenSteps != null && mutator.extraGenSteps.Any())
                    chosen = chosen.Concat(mutator.extraGenSteps);
            }

            BiomeDef biome = map.Biome;

            if (biome != null && biome.extraGenSteps != null && biome.extraGenSteps.Any())
                chosen = chosen.Concat(biome.extraGenSteps.Where(Allowed));

            if (biome != null && biome.preventGenSteps != null && biome.preventGenSteps.Any())
                chosen = chosen.Where(step => !biome.preventGenSteps.Contains(step));

            foreach (TileMutatorDef mutator in map.TileInfo.Mutators)
            {
                if (mutator == null || mutator.preventGenSteps == null || !mutator.preventGenSteps.Any())
                    continue;

                TileMutatorDef captured = mutator;

                chosen = chosen.Where(step => !captured.preventGenSteps.Contains(step));
            }

            List<GenStepDef> ordered = chosen
                .Where(step => step != null && step.genStep != null && step.order <= MaxOrder)
                .Distinct()
                .OrderBy(step => step.order)
                .ThenBy(step => step.index)
                .ToList();

            ordered.RemoveAll(a => ordered.Any(b =>
                b.preventsGenSteps != null && b.preventsGenSteps.Contains(a)));

            Steps.AddRange(ordered);
        }

        /// <summary>Whether the scenario has switched this step off, which is vanilla's own test.</summary>
        private static bool Allowed(GenStepDef step)
        {
            Scenario scenario = Find.Scenario;

            if (scenario == null)
                return true;

            // AllParts rather than the parts list itself, which is internal to RimWorld.
            return !scenario.AllParts.Any(part =>
                part != null && part.def != null && part.def.genStep == step
                && typeof(ScenPart_DisableMapGen).IsAssignableFrom(part.def.scenPartClass));
        }

        /// <summary>
        /// Runs one step, seeded the way the real generator seeds it.
        ///
        /// <b>The random state is pushed and popped inside the frame rather than held across frames.</b> Every
        /// step re-seeds from the map seed and its own part, so nothing is carried between them and the
        /// colony's stream is never left sitting under ours while the world map draws.
        ///
        /// <b>A step that throws costs its own contribution and not the analysis.</b> That is vanilla's
        /// behaviour too, which logs and carries on, and it matters more here: a modded step meeting a map that
        /// was never added to the game is the likeliest failure in this whole feature.
        /// </summary>
        private static bool Step()
        {
            if (index >= Steps.Count)
            {
                Phase = TilePreviewJobPhase.Reading;

                return true;
            }

            GenStepDef step = Steps[index];

            CurrentLabel = step.defName;

            ProgramState previous = Current.ProgramState;

            MapGenerator.mapBeingGenerated = map;

            Rand.PushState();

            try
            {
                Rand.Seed = Gen.HashCombineInt(seed, SeedPartFor(index));
                Current.ProgramState = ProgramState.MapInitializing;

                UIGuard.Try("TilePreview.Step." + step.defName,
                    () => step.genStep.Generate(map, default(GenStepParams)), null);
            }
            finally
            {
                Current.ProgramState = previous;

                Rand.PopState();
            }

            index++;

            if (index >= Steps.Count)
            {
                Phase = TilePreviewJobPhase.Reading;
                CurrentLabel = null;
            }

            return true;
        }

        /// <summary>
        /// <c>MapGenerator.GetSeedPart</c>, which is private and is pure logic over the step list.
        ///
        /// The disambiguator is the point: two steps sharing a <c>SeedPart</c> would otherwise generate
        /// identically, so the second and later ones are offset by how many came before them.
        /// </summary>
        private static int SeedPartFor(int at)
        {
            int part = Steps[at].genStep.SeedPart;
            int repeats = 0;

            for (int i = 0; i < at; i++)
            {
                if (Steps[i].genStep.SeedPart == part)
                    repeats++;
            }

            return part + repeats;
        }

        /// <summary>The finished map, for the one caller that reads it. Null unless the phase is Reading.</summary>
        internal static Map Finished
        {
            get { return Phase == TilePreviewJobPhase.Reading ? map : null; }
        }

        /// <summary>Called once the reading has been taken, to let the map go.</summary>
        internal static void Complete()
        {
            PlanetTile analyzed = Tile;

            Cancel();

            Tile = analyzed;
            Phase = TilePreviewJobPhase.Done;
        }
    }
}
