using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Tabs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The pawns tab's window.
    ///
    /// A window of ours rather than a patch on a vanilla one, because there is no vanilla tab this replaces --
    /// which also means no <c>MainTabWindow_PawnTable</c> to inherit, and no vanilla table to fall back to if this
    /// fails. Drawing goes through <see cref="UIGuard.Content"/> for that reason: with no fallback available, the
    /// contained failure has to be something the player can see and read about rather than a blank window.
    /// </summary>
    public class MainTabWindow_Pawns : MainTabWindow, IUITabWidthReservation
    {
        public override Vector2 RequestedTabSize => new Vector2(PawnsPanel.WindowWidth, PawnsPanel.WindowHeight);

        /// <summary>Zero, because the panel does its own insetting -- the same arrangement the work tab uses.</summary>
        protected override float Margin => 0f;

        /// <summary>
        /// Room the work pane needs, held back on top of whatever width the tab has been dragged to.
        ///
        /// See <see cref="IUITabWidthReservation"/>. Without it, a player who had resized this tab got the pane
        /// drawn inside their chosen width instead of beside it: the columns were squeezed, the activity column
        /// fell off the right edge and a horizontal scrollbar appeared under a table that had been fitting.
        /// </summary>
        public float ReservedWidth => PawnsPanel.PaneReservation;

        /// <summary>
        /// The reservation this window was last sized for.
        ///
        /// <b>Remembered, where the earlier version compared the rect against the width the panel wanted.</b>
        /// That read better and was wrong once a stored size existed: the stored width legitimately differs
        /// from the panel's ideal, so the comparison was true on every frame forever and this called
        /// <c>SetInitialSizeAndPosition</c> sixty times a second to be handed the same rect back. Watching the
        /// thing that actually changes fires once per open and once per close.
        /// </summary>
        private float sizedFor = -1f;

        /// <summary>
        /// Resizes the window when the work pane opens or closes.
        ///
        /// <b>Why this is needed at all.</b> <c>RequestedTabSize</c> is only read when a window opens --
        /// <c>SetInitialSizeAndPosition</c> runs from <c>PreOpen</c> -- so a width that changes while the tab is
        /// already open changes nothing on its own. Without this the pane would have to be drawn inside the width
        /// the grid alone asked for, squeezing the table every time somebody expanded a row.
        ///
        /// In <c>WindowUpdate</c> rather than in <c>DoWindowContents</c> on purpose: this runs outside the GUI pass,
        /// so the rect is settled before anything lays out against it. Moving the window mid-draw would leave the
        /// frame's controls positioned against a rect that no longer exists.
        /// </summary>
        public override void WindowUpdate()
        {
            base.WindowUpdate();

            float reserved = ReservedWidth;

            if (Mathf.Abs(sizedFor - reserved) <= 0.5f)
                return;

            sizedFor = reserved;
            SetInitialSizeAndPosition();
        }

        /// <summary>
        /// Forgets what the window was sized for, so the next update re-applies it.
        ///
        /// The pane can be left open when the tab closes, and a stale value here would mean reopening the tab
        /// with the pane already open and nothing widening it.
        /// </summary>
        public override void PreOpen()
        {
            base.PreOpen();

            sizedFor = -1f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Pawns.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);
                PawnsPanel.Draw(inRect);
            }, "The pawns tab shows a failure notice. There is no vanilla equivalent to fall back to, so the "
               + "same information is in the Assign, Schedule and Health tabs until the game is restarted.");
        }
    }
}
