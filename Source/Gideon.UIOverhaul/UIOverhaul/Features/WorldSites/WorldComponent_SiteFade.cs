using Gideon.UIFramework.Helpers;
using RimWorld.Planet;
using Verse;

namespace Gideon.UIOverhaul.Features.WorldSites
{
    /// <summary>
    /// The hourly pass that keeps the marker clocks agreeing with the settings.
    ///
    /// <b>It holds no state, which is the point.</b> Every end tick is computed from the marker's own creation
    /// tick, so there is nothing to save, nothing to load, and nothing that can be left behind pointing at a
    /// marker that has gone. Removing this mod leaves the clocks it set sitting on vanilla's own comp, and
    /// removing the comp with it leaves an unread integer in the save.
    ///
    /// <b>Once an in-game hour.</b> The only thing a sweep catches that the settings window does not is a marker
    /// created since the last one, and the shortest lifespan on offer is fifteen days. An hour is already far
    /// finer than the thing it measures, and the work is a walk over the world object list looking at a def name.
    ///
    /// <b>A world component rather than a game component</b> because these are world objects: on the planet, not
    /// on a map, and outliving every map in the save.
    /// </summary>
    public class WorldComponent_SiteFade : WorldComponent
    {
        private const int IntervalTicks = 2500;

        private int sinceSweep;

        /// <summary>Required by RimWorld: every world component is constructed with the world it belongs to.</summary>
        public WorldComponent_SiteFade(World world)
            : base(world)
        {
        }

        /// <summary>
        /// Once when the world is ready, so a save loaded with the feature already on has its clocks correct
        /// before the first hour has passed.
        ///
        /// Guarded because this is called by RimWorld during load, where a throw is a failed load rather than a
        /// missing feature.
        /// </summary>
        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);

            UIGuard.Try("WorldSites.FinalizeInit", SiteFade.ReconcileAll,
                "Abandoned marker lifespans are not applied to this save until a setting is changed.");
        }

        public override void WorldComponentTick()
        {
            if (++sinceSweep < IntervalTicks)
                return;

            sinceSweep = 0;

            SiteFade.ReconcileAll();
        }
    }
}
