using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Tabs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>
    /// The corpses tab's window.
    ///
    /// <b>No vanilla equivalent to fall back to, so failures are shown rather than swapped.</b> Drawing goes
    /// through <see cref="UIGuardedPanel"/> the way the animals, pawns and hospital tabs do: a contained failure
    /// has to be something the player can read, not a blank window.
    /// </summary>
    public class MainTabWindow_Corpses : MainTabWindow, IUITabWidthReservation
    {
        /// <summary>
        /// The size RimWorld should open the tab at.
        ///
        /// <b>Guarded, because RimWorld reads this rather than calling it.</b> A property the game asks for
        /// during window layout is as much a boundary as an override is, and this one reaches into the panel's
        /// column widths. The fallback is a shape that fits any screen the game runs on.
        /// </summary>
        public override Vector2 RequestedTabSize
        {
            get
            {
                return UIGuard.Try("Corpses.RequestedSize",
                    () => new Vector2(CorpsePanel.WindowWidth, CorpsePanel.WindowHeight),
                    new Vector2(1000f, 640f), null);
            }
        }

        /// <summary>Zero, because the panel insets itself. The same arrangement as the other tabs.</summary>
        protected override float Margin
        {
            get { return 0f; }
        }

        /// <summary>Room the body pane needs on top of whatever width the tab has been dragged to.</summary>
        public float ReservedWidth
        {
            get { return UIGuard.Try("Corpses.Reserved", () => CorpsePanel.PaneReservation, 0f, null); }
        }

        /// <summary>
        /// The reservation this window was last sized for.
        ///
        /// Watched rather than compared against the panel's ideal width, because a stored size legitimately
        /// differs from the ideal and comparing them would call <c>SetInitialSizeAndPosition</c> every frame
        /// forever. The fault was found on the pawns tab.
        /// </summary>
        private float sizedFor = -1f;

        /// <summary>
        /// Widens the window when the pane opens or closes.
        ///
        /// Guarded: RimWorld wraps <c>DoWindowContents</c> and nothing else, so a lifecycle override that threw
        /// would take the whole window stack down with it.
        /// </summary>
        public override void WindowUpdate()
        {
            base.WindowUpdate();

            UIGuard.Try("Corpses.WindowUpdate", () =>
            {
                float reserved = ReservedWidth;

                if (Mathf.Abs(sizedFor - reserved) <= 0.5f)
                    return;

                sizedFor = reserved;

                SetInitialSizeAndPosition();
            }, null);
        }

        public override void PreOpen()
        {
            base.PreOpen();

            // The pane can be left open when the tab closes, and a stale value here would mean reopening with
            // the pane open and nothing widening the window for it.
            sizedFor = -1f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Corpses.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                CorpsePanel.Draw(inRect);
            }, "The corpses tab shows a failure notice. Nothing on the map has been changed, and any burial, "
               + "stripping or bench order already given still stands.");
        }
    }
}
