using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
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
    public class MainTabWindow_Pawns : MainTabWindow
    {
        public override Vector2 RequestedTabSize => new Vector2(PawnsPanel.WindowWidth, PawnsPanel.WindowHeight);

        /// <summary>Zero, because the panel does its own insetting -- the same arrangement the work tab uses.</summary>
        protected override float Margin => 0f;

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
        ///
        /// The comparison is against the actual rect rather than a remembered value, so the check answers "does the
        /// window match what it should be" instead of "did we notice the change" -- and it therefore also corrects
        /// itself after a resolution change or anything else that moves the window.
        /// </summary>
        public override void WindowUpdate()
        {
            base.WindowUpdate();

            if (Mathf.Abs(windowRect.width - PawnsPanel.WindowWidth) > 0.5f)
                SetInitialSizeAndPosition();
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
