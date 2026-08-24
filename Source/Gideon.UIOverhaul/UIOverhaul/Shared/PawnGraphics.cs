using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// Makes sure a pawn's render tree exists before this mod asks for a picture of them.
    ///
    /// <b>The precondition RimWorld states in its own error message,</b> and one this mod was breaking in four
    /// places: <c>PawnRenderTree.ParallelPreDraw</c> logs "you must called EnsureGraphicsInitialized() on the
    /// drawn dynamic thing X before drawing it" and then dereferences a null node. Reported by Aaron on
    /// 2026-08-23, from a fresh load, for two colonists.
    ///
    /// <b>Vanilla does not need this and we do, for a reason that is the same in every case.</b> The game
    /// initialises a pawn's graphics on the way into view and then draws them; every picture this mod asks for is
    /// of a pawn who may never have been in view at all -- a colonist on another map in the bar, a body in the
    /// corpses tab, whoever the editor was opened on, a tile of somebody the camera has never pointed at. The
    /// portrait cache renders on a miss, and a miss for a pawn nobody has looked at is exactly the case vanilla
    /// never reaches.
    ///
    /// <b>Cheap enough to call before every request.</b> <c>EnsureInitialized</c> is a set-up-if-needed check
    /// followed by a null-conditional call on the root node, so on all but the first call for a pawn it does
    /// nothing. That is what makes a call site guard correct here rather than a warm-up list somebody has to
    /// remember to add to.
    /// </summary>
    internal static class PawnGraphics
    {
        /// <summary>
        /// Prepares one pawn, or does nothing if there is nothing to prepare.
        ///
        /// Unspawned is allowed and is the common case for half the callers: the starting characters have no map,
        /// and a pawn in a caravan or a pod is not spawned either. What is refused is a destroyed pawn and one
        /// with no draw tracker, neither of which has a render tree to build.
        /// </summary>
        internal static void Ensure(Pawn pawn)
        {
            UIGuard.Try("Shared.PawnGraphics", () =>
            {
                if (pawn == null || pawn.Destroyed)
                    return;

                Pawn_DrawTracker drawer = pawn.Drawer;

                if (drawer == null || drawer.renderer == null)
                    return;

                drawer.renderer.EnsureGraphicsInitialized();
            }, null);
        }
    }
}
