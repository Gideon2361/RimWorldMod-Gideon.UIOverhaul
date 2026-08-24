using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// An animal kept by an allowed area does not need pen management.
    ///
    /// <b>The gap this closes, found by Aaron on 2026-08-23.</b> A hen with an allowed area assigned kept
    /// producing "Cannot rope Hen 1: No allowed reachable pens" -- a handler would pick it up as pen work every
    /// time it looked for a job, fail, and report it. The feature let the player give livestock an area and stopped
    /// the animal roaming, but never told the pen system that the area had taken over, so half the game still
    /// thought the animal was homeless.
    ///
    /// <b><c>NeedsToBeManagedByRope</c> is the single question everything asks,</b> and it answers
    /// <c>pawn.Roamer</c> and nothing else. Every roping work giver, the pen marker's own accounting and
    /// <c>GetPenAnimalShouldBeTakenTo</c> all funnel through it, so one prefix covers the lot and cannot fall out
    /// of step with them.
    ///
    /// <b>Not patched at <c>Pawn.Roamer</c>, which was the obvious place and the wrong one.</b> <c>Roamer</c> also
    /// feeds <c>Pawn.FenceBlocked</c>: clearing it would tell the pathfinder that fences do not apply to this
    /// animal, and an animal held by a pen would then walk straight out through one. The rope question is the only
    /// one we mean to change.
    ///
    /// <b>Only an area counts, never a pen.</b> <see cref="LivestockRoaming.HeldByArea"/> and not <c>Held</c>: an
    /// animal standing in a pen still wants pen management, because that is vanilla working correctly. This is
    /// solely about the case where the player has said "keep it here instead".
    /// </summary>
    [HarmonyPatch(typeof(AnimalPenUtility), nameof(AnimalPenUtility.NeedsToBeManagedByRope))]
    internal static class Patch_AreaInsteadOfPen
    {
        /// <summary>
        /// Answers false for an animal an area is keeping, and otherwise lets vanilla answer.
        ///
        /// Guarded, because this runs from work giver scans: an exception here would be thrown once per animal per
        /// scan, which is the shape of fault that fills a log in a minute.
        /// </summary>
        private static bool Prefix(Pawn pawn, ref bool __result)
        {
            bool held = UIGuard.Try("Animals.AreaInsteadOfPen",
                () => pawn != null && LivestockRoaming.HeldByArea(pawn), false, null);

            // True lets vanilla answer, false means we already have. No UIGuard.Replaced here: that helper is for
            // a prefix that draws or does something in the original's place, and its return is the answer to
            // "did we handle it" -- there is nothing to hand it when the whole body is one assignment.
            if (!held)
                return true;

            __result = false;

            return false;
        }
    }
}
