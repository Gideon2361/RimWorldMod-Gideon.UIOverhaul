using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// The mech tab's window.
    ///
    /// <b>Failures are shown rather than swapped.</b> Drawing goes through <see cref="UIGuardedPanel"/> the
    /// way the hospital, animals and pawns tabs do: a contained failure has to be something the player can
    /// read, not a blank window and not a quiet switch to the screen we suppressed.
    /// </summary>
    public class MainTabWindow_Mechs : MainTabWindow
    {
        public override Vector2 RequestedTabSize
        {
            get { return new Vector2(MechsPanel.WindowWidth, MechsPanel.WindowHeight); }
        }

        /// <summary>Zero, because the panel insets itself. The same arrangement as the hospital tab.</summary>
        protected override float Margin
        {
            get { return 0f; }
        }

        public override void PreOpen()
        {
            base.PreOpen();

            // The roster is a per-frame snapshot keyed on the frame counter, and a tab reopened in the same
            // frame it closed would otherwise draw the state it had when it closed.
            MechRoster.Invalidate();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Mechs.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                MechsPanel.Draw(inRect);
            }, "The mech tab shows a failure notice. Your mechs are unaffected, and every order on this "
               + "screen is also on the mechanitor's own command row at the bottom of the map.");
        }
    }
}
