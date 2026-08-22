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

        // No width reservation and no resize watching since 2026-08-22. Both existed for the work priorities
        // pane, which reserved 330 pixels beside the table and made the window re-place itself whenever a row
        // opened or closed. The priorities are drawn inside the opened row now, so the tab is simply the width
        // its columns need. See PawnWorkGrid.

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
