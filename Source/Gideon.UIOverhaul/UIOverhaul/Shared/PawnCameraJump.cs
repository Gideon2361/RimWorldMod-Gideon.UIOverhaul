using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// Centers the view on a pawn, at the end of the frame rather than where the click happened.
    ///
    /// Deferred for the reason the architect tab defers its close: the click is handled inside a scroll view, and
    /// both halves of what follows reach outside it. Closing the tab takes the window off the stack while it is
    /// still drawing, with a BeginScrollView left to be matched; and the jump itself calls TryHideWorld and can
    /// reassign Game.CurrentMap, neither of which belongs in the middle of drawing a row. Ending the frame first
    /// costs nothing.
    ///
    /// Shared by every panel that lists pawns, so they cannot disagree about it.
    /// </summary>
    internal static class PawnCameraJump
    {
        private static Pawn requested;

        /// <summary>Asks for a jump. Takes effect when the drawing panel calls <see cref="Resolve"/>.</summary>
        public static void Request(Pawn pawn)
        {
            requested = pawn;
        }

        /// <summary>
        /// Performs a requested jump, and closes the tab so there is something to see.
        ///
        /// The close is ours to do: nothing in CameraJumper touches the main tabs, and these tabs cover the map,
        /// so a jump on its own would center the camera behind a full-screen window.
        ///
        /// Jump rather than jump-and-select, which is the narrower of the two. Selecting as well is a one-word
        /// change to TryJumpAndSelect if the inspect pane turns out to be wanted too.
        ///
        /// Cross-map is already handled: CameraJumper hides the world view and reassigns the current map when the
        /// target is on another one, which these tabs need because they list colonists from every map -- and
        /// PositionHeld/MapHeld mean a pawn in a caravan or a container still resolves to somewhere real.
        ///
        /// Call it at the end of a panel's draw, after any scroll view has been closed out.
        /// </summary>
        public static void Resolve()
        {
            if (requested == null)
                return;

            Pawn pawn = requested;
            requested = null;

            // playSound: false -- CameraJumper plays its own sound on arrival, and vanilla's tab-close click on
            // top of that reads as two clicks for one action.
            Find.MainTabsRoot.EscapeCurrentTab(false);

            CameraJumper.TryJump(pawn);
        }
    }
}
