using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// An animal held by an allowed area can reach its area, and heads for it.
    ///
    /// <b>Two gaps, both found on 2026-08-23 from one chicken that would not go home.</b> The feature let the
    /// player give livestock an area and stopped the animal roaming, but vanilla's containment for roamers is
    /// built entirely around pens, and neither half of that knew about areas.
    ///
    /// <b>It could not get there.</b> <c>Building_Door.PawnCanOpen</c> refuses any pawn whose
    /// <c>FenceBlocked</c> is set unless the door is an animal flap, and <c>Pawn.FenceBlocked</c> is
    /// <c>Roamer</c> with a job exemption. So a chicken cannot open an ordinary door and a crow can, which is
    /// exactly what Aaron noticed: the crow is not a roamer. An area on the far side of a door was unreachable
    /// for the animal however reachable it looked to the player.
    ///
    /// <b>And it never tried.</b> <c>JobGiver_WanderInPen.GetWanderRoot</c> is where a loose roamer decides to
    /// head home: if its district touches the map edge it wanders towards <c>ClosestSuitablePen</c>. With no pen
    /// that returns null, the root falls back to the animal's own position, and the chicken wanders in circles
    /// wherever it happens to be standing -- forever, with its food in an area it never walks to.
    ///
    /// <b>The consequence to be clear about: a fence stops containing an area-held animal.</b>
    /// <c>Pawn.CanPassFences</c> is defined as <c>!FenceBlocked</c>, the same flag the doors read, so there is no
    /// way to open doors for this animal without also letting it step over a fence. That is coherent with what
    /// the setting says -- the area is the containment now, not the fence -- and the area is respected by job
    /// targeting, so the animal has no reason to leave it. But an area drawn larger than the fence line will let
    /// the animal out, and that is the player's drawing rather than a bug.
    ///
    /// Only ever for an animal an <em>area</em> is holding. An animal with a pen is left entirely to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.FenceBlocked), MethodType.Getter)]
    internal static class Patch_AreaOpensDoors
    {
        /// <summary>
        /// Clears the flag for an area-held animal, which lets it through doors and fences alike.
        ///
        /// A postfix on the cheap side of the test: vanilla says false for everything that is not a roamer, which
        /// is almost every pawn in the game, and that answer is returned untouched before anything of ours runs.
        /// </summary>
        private static void Postfix(Pawn __instance, ref bool __result)
        {
            if (!__result)
                return;

            if (UIGuard.Try("Animals.AreaOpensDoors",
                    () => LivestockRoaming.HeldByArea(__instance), false, null))
                __result = false;
        }
    }

    /// <summary>
    /// Sends a loose area-held animal towards its area, the way vanilla sends a loose penned one towards its pen.
    ///
    /// <b>A postfix rather than a replacement,</b> so vanilla still gets to prefer a pen. An animal that has both
    /// an area and a suitable pen keeps the pen: that is the arrangement the game was built around and the one
    /// its own alerts talk about.
    ///
    /// <b>The nearest cell of the area, not its middle.</b> A pen marker sits in its pen, so vanilla's root is
    /// inside the destination. An area has no marker and can be any shape at all, including two halves either
    /// side of the map, so the closest active cell is the only choice that always lies in the area and never
    /// sends the animal further away than it needs to go. <c>wanderRadius</c> then does the rest once it arrives.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_WanderInPen), "GetWanderRoot")]
    internal static class Patch_AreaIsTheWanderRoot
    {
        private static void Postfix(Pawn pawn, ref IntVec3 __result)
        {
            // Copied out first: a ref parameter cannot be captured by the lambda the guard takes.
            IntVec3 vanilla = __result;

            IntVec3 found = UIGuard.Try("Animals.AreaWanderRoot", () => Root(pawn, vanilla), IntVec3.Invalid,
                null);

            if (found.IsValid)
                __result = found;
        }

        private static IntVec3 Root(Pawn pawn, IntVec3 vanilla)
        {
            if (pawn == null || !pawn.Spawned || !LivestockRoaming.HeldByArea(pawn))
                return IntVec3.Invalid;

            // Vanilla found a pen to head for. It wins: a pen is a better home than an area, and the player has
            // one of each only when they meant to.
            if (vanilla != pawn.Position)
                return IntVec3.Invalid;

            Area area = pawn.playerSettings?.EffectiveAreaRestrictionInPawnCurrentMap;

            if (area == null || area.TrueCount == 0)
                return IntVec3.Invalid;

            // Already inside it. Nothing to head towards, and returning a cell would drag the animal to the edge
            // of its own area every time it picked a wander job.
            if (area[pawn.Position])
                return IntVec3.Invalid;

            IntVec3 closest = IntVec3.Invalid;
            int best = int.MaxValue;

            foreach (IntVec3 cell in area.ActiveCells)
            {
                int distance = (cell - pawn.Position).LengthHorizontalSquared;

                if (distance >= best)
                    continue;

                // Standable and reachable, or the animal is sent at a wall and gives up. Checked only on cells
                // that are already closer than the best so far, so a large area does not pay for it per cell.
                if (!cell.Standable(pawn.Map)
                    || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    continue;

                best = distance;
                closest = cell;
            }

            return closest;
        }
    }
}
