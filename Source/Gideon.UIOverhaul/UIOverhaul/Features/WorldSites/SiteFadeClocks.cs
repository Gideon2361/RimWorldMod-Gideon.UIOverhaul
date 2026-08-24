using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Gideon.UIOverhaul.Features.WorldSites
{
    /// <summary>
    /// Gives the three clockless marker defs the clock RimWorld already wrote.
    ///
    /// <b>Comps come from the def, which is why this is def injection rather than anything cleverer.</b>
    /// <c>WorldObject.ExposeData</c> calls <c>InitializeComps</c> from <c>def.comps</c> while loading vars, so a
    /// properties object added to a def before any save is read gives the comp to markers that were saved years
    /// ago as well as to ones made afterwards. There is no route to add a comp to an object that already exists,
    /// and a parallel table of our own end ticks would have needed its own scribing, its own removal and its own
    /// inspect line.
    ///
    /// <b>It runs whatever the settings say.</b> A <c>TimeoutComp</c> with no clock set does nothing at all: it
    /// ticks, sees -1, and returns; it prints nothing in the inspect pane; it saves one integer. Injecting only
    /// when the feature is on would mean a setting that needed a restart, and this mod does not ask for restarts.
    ///
    /// <b>The abandoned camp is not on the list</b> because its def declares the comp already. Adding a second
    /// one would give it two countdowns in the inspect pane and two things racing to remove it.
    ///
    /// <b>Startup, not the mod constructor.</b> Defs do not exist when the constructor runs, and a static
    /// constructor marked for startup runs after the database is built and long before a world object is loaded.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class SiteFadeClocks
    {
        static SiteFadeClocks()
        {
            UIGuard.Try("WorldSites.Clocks", Inject,
                "Abandoned colony and gravship launch markers cannot be given a lifespan this session. The "
                + "planet keeps them, which is what RimWorld does anyway.");
        }

        private static void Inject()
        {
            List<SiteFadeKind> kinds = SiteFadeKinds.All;

            for (int i = 0; i < kinds.Count; i++)
            {
                WorldObjectDef def = SiteFadeKinds.DefOf(kinds[i]);

                if (def == null || Clocked(def))
                    continue;

                def.comps.Add(new WorldObjectCompProperties_Timeout());
            }
        }

        /// <summary>
        /// Whether a def already carries a timeout comp, from vanilla or from another mod.
        ///
        /// Tested by comp class rather than by properties class, so a mod that subclasses the properties to reach
        /// the same comp still counts. Two comps of this class on one def is the fault worth avoiding, not two
        /// properties objects.
        /// </summary>
        private static bool Clocked(WorldObjectDef def)
        {
            for (int i = 0; i < def.comps.Count; i++)
            {
                WorldObjectCompProperties props = def.comps[i];

                if (props != null && props.compClass == typeof(TimeoutComp))
                    return true;
            }

            return false;
        }
    }
}
