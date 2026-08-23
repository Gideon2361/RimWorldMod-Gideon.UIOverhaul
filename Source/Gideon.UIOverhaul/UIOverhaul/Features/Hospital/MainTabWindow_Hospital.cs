using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Tabs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// The hospital tab's window.
    ///
    /// <b>No vanilla equivalent to fall back to, so failures are shown rather than swapped.</b> Drawing goes
    /// through <see cref="UIGuardedPanel"/> the way the animals and pawns tabs do: a contained failure has to be
    /// something the player can read, not a blank window and not a quiet switch to a screen we suppressed.
    /// </summary>
    public class MainTabWindow_Hospital : MainTabWindow, IUITabWidthReservation
    {
        public override Vector2 RequestedTabSize
        {
            get { return new Vector2(HospitalPanel.WindowWidth, HospitalPanel.WindowHeight); }
        }

        /// <summary>Zero, because the panel insets itself. The same arrangement as the animals tab.</summary>
        protected override float Margin
        {
            get { return 0f; }
        }

        /// <summary>Room the patient pane needs on top of whatever width the tab has been dragged to.</summary>
        public float ReservedWidth
        {
            get { return HospitalPanel.PaneReservation; }
        }

        /// <summary>
        /// The reservation this window was last sized for.
        ///
        /// Watched rather than compared against the panel's ideal width, because a stored size legitimately
        /// differs from the ideal and comparing them would call <c>SetInitialSizeAndPosition</c> every frame
        /// forever. The fault was found on the pawns tab.
        /// </summary>
        private float sizedFor = -1f;

        public override void WindowUpdate()
        {
            base.WindowUpdate();

            float reserved = ReservedWidth;

            if (Mathf.Abs(sizedFor - reserved) <= 0.5f)
                return;

            sizedFor = reserved;

            SetInitialSizeAndPosition();
        }

        public override void PreOpen()
        {
            base.PreOpen();

            // The pane can be left open when the tab closes, and a stale value here would mean reopening with the
            // pane open and nothing widening the window for it.
            sizedFor = -1f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Hospital.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                HospitalPanel.Draw(inRect);
            }, "The hospital tab shows a failure notice. Your patients are unaffected, and operations can still "
               + "be queued from a pawn's own health tab.");
        }
    }
}
