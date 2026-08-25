using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Explosives
{
    /// <summary>
    /// Rings the blast radius of any selected thing that can explode.
    ///
    /// <b>What it answers.</b> An IED, a shell rack, a chemfuel pile and a boomalope all carry a number that
    /// decides how much of the colony goes with them, and the game states it nowhere on the map. Deciding how
    /// far from the wall to stack the chemfuel currently means reading the info card, converting a number of
    /// tiles into a distance by eye, and being wrong.
    ///
    /// <b>Hung off the comp rather than off a list of defs.</b> Every explosive in the game reaches its blast
    /// through <c>CompExplosive</c>, so one postfix on the comp's own overlay hook covers vanilla, both DLCs
    /// and every modded explosive at once, with nothing to maintain as things are added. The hook is called
    /// once per comp per selected thing, which is also the exact moment the ring should be drawn.
    ///
    /// <b>The radius is asked of the comp, not of the def.</b> <c>ExplosiveRadius()</c> folds in the stack
    /// count and the contents of a refuelable tank, so a single chemfuel says one thing and a pile of two
    /// hundred says another. The def's <c>explosiveRadius</c> is only the first of those.
    ///
    /// <b><c>ThingDef.specialDisplayRadius</c> is deliberately left alone,</b> which is where the reference
    /// mod for this feature starts. Writing the def's own radius field would have RimWorld draw a second ring,
    /// in its own white, at the base radius -- so a fuel tank or a stack would show two rings disagreeing about
    /// how big the blast is. It also buys nothing at placement time, which is the reason to want it: the ghost
    /// under the cursor is drawn by the def's PlaceWorkers, and none of them consult that field.
    /// </summary>
    [HarmonyPatch(typeof(ThingComp), nameof(ThingComp.PostDrawExtraSelectionOverlays))]
    internal static class Patch_BlastRadius
    {
        /// <summary>
        /// Below this the ring is not worth drawing, and it is RimWorld's own cutoff: <c>Thing</c> applies the
        /// same 0.1 before drawing a special display radius.
        /// </summary>
        private const float Smallest = 0.1f;

        public static void Postfix(ThingComp __instance)
        {
            UIGuard.Try("Explosives.Ring", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null || !settings.showBlastRadius)
                    return;

                if (!(__instance is CompExplosive explosive) || explosive.parent == null)
                    return;

                if (!explosive.parent.Spawned)
                    return;

                float radius = explosive.ExplosiveRadius();

                if (radius < Smallest)
                    return;

                // Past this the ring cannot be built: GenRadial keeps a precalculated pattern of 20,000 cells
                // and both NumCellsInRadius and DrawRadiusRing log an error rather than clamping. A large
                // enough stack of something with explosiveExpandPerStackcount reaches it, so the test is ours
                // to make -- the whole point of this mod's guards is that we never hand RimWorld an error.
                if (radius >= GenRadial.MaxRadialPatternRadius)
                    return;

                UIColorPaletteDef palette = UIColorPaletteDef.Active;

                GenDraw.DrawRadiusRing(explosive.parent.Position, radius,
                    palette == null ? Color.red : palette.Danger);
            }, "Explosives are drawn without a blast ring.");
        }
    }
}
