using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIOverhaul.Features.Tabs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The animals tab's window, replacing both of vanilla's.
    ///
    /// <b>One window for two tabs, which is the whole point of the feature.</b> Vanilla's Animals and Wildlife
    /// are the same table with different columns over populations that the player compares against each other
    /// constantly: how much meat is walking about out there, and how many mouths are already inside the fence.
    /// Splitting that across two screens is what makes the question hard to answer.
    ///
    /// <b>No vanilla equivalent to fall back to, so failures are shown rather than swapped.</b> Drawing goes
    /// through <see cref="UIGuardedPanel"/> for the same reason the pawns tab does: a contained failure has to be
    /// something the player can read, not a blank window and not a quiet switch to a screen we suppressed.
    /// </summary>
    public class MainTabWindow_Animals : MainTabWindow, IUITabWidthReservation
    {
        public override Vector2 RequestedTabSize =>
            new Vector2(AnimalsPanel.WindowWidth, AnimalsPanel.WindowHeight);

        /// <summary>Zero, because the panel insets itself. The same arrangement as the work and pawns tabs.</summary>
        protected override float Margin => 0f;

        /// <summary>Room the species pane needs on top of whatever width the tab has been dragged to.</summary>
        public float ReservedWidth => AnimalsPanel.PaneReservation;

        /// <summary>
        /// The reservation this window was last sized for.
        ///
        /// Watched rather than compared against the panel's ideal width, because a stored size legitimately
        /// differs from the ideal and comparing them would call <c>SetInitialSizeAndPosition</c> on every frame
        /// forever. See the pawns tab, where that fault was found.
        /// </summary>
        private float sizedFor = -1f;

        /// <summary>
        /// Resizes the window when the pane opens or closes.
        ///
        /// <c>RequestedTabSize</c> is only read when a window opens, so a width that changes while the tab is open
        /// changes nothing on its own. In <c>WindowUpdate</c> rather than during the GUI pass, so the rect is
        /// settled before anything lays out against it.
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

        public override void PreOpen()
        {
            base.PreOpen();

            // The pane can be left open when the tab closes, and a stale value here would mean reopening with the
            // pane open and nothing widening the window for it.
            sizedFor = -1f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Animals.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                AnimalsPanel.Draw(inRect);
            }, "The animals tab shows a failure notice. Your animals are unaffected, and hunting and taming can "
               + "still be ordered by clicking an animal on the map.");
        }
    }
}
