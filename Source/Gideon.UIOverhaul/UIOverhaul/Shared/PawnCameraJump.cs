using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// Centers the view on a pawn and selects them, at the end of the frame rather than where the click happened.
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
        private static Thing requested;

        /// <summary>Asks for a jump. Takes effect when the drawing panel calls <see cref="Resolve"/>.</summary>
        public static void Request(Pawn pawn)
        {
            requested = pawn;
        }

        /// <summary>
        /// The same, for anything that is not a pawn: a grave, a bench, a body's own container.
        ///
        /// <b>A dead pawn is worth passing as the pawn rather than as its corpse.</b> CameraJumper walks up the
        /// parent holders of anything that is not spawned, so a colonist inside a corpse inside a sarcophagus
        /// resolves to the sarcophagus without the caller having to know which of the three is on the map.
        /// </summary>
        public static void Request(Thing thing)
        {
            requested = thing;
        }

        /// <summary>
        /// Performs a requested jump, and closes the tab so there is something to see.
        ///
        /// The close is ours to do: nothing in CameraJumper touches the main tabs, and these tabs cover the map,
        /// so a jump on its own would center the camera behind a full-screen window.
        ///
        /// Jump *and* select, which is what vanilla's colonist bar does when you click a portrait. Centering
        /// alone left the pawn under the cursor but not acted upon, and the next thing anyone wants after
        /// finding a colonist is to give them an order -- which needs them selected and their inspect pane up.
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

            Thing target = requested;
            requested = null;

            // playSound: false -- CameraJumper plays its own sound on arrival, and vanilla's tab-close click on
            // top of that reads as two clicks for one action.
            Find.MainTabsRoot.EscapeCurrentTab(false);

            CameraJumper.TryJumpAndSelect(target);
        }
    }
}
