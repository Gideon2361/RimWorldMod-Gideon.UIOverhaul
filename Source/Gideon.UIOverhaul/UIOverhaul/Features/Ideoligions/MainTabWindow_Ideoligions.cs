using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>
    /// The ideoligions tab's window.
    ///
    /// <b>There is a vanilla screen behind this one and it is still reachable,</b> unlike the corpses or pawns
    /// tabs which have no equivalent. That does not make it a fallback: a contained failure draws a notice
    /// through <see cref="UIGuardedPanel"/> rather than quietly handing the player back to
    /// <c>MainTabWindow_Ideos</c>, because a silent hand-off hides the defect and this mod has a rule against it.
    /// The redirect can be undone by taking our button off the bar, which is the player's own lever.
    ///
    /// <b>Without Ideology this window is never opened,</b> because <see cref="IdeoTabs"/> answers false and the
    /// button is suppressed off the bar. The guard here is belt and braces for the case where something opens it
    /// directly.
    /// </summary>
    public class MainTabWindow_Ideoligions : MainTabWindow
    {
        public override Vector2 RequestedTabSize
        {
            get
            {
                return UIGuard.Try("Ideoligions.RequestedSize",
                    () => new Vector2(IdeoPanel.WindowWidth,
                        Mathf.Min(IdeoPanel.WindowHeight, UI.screenHeight - 90f)),
                    new Vector2(1000f, 640f), null);
            }
        }

        /// <summary>Zero, because the panel insets itself. The same arrangement as the other tabs.</summary>
        protected override float Margin
        {
            get { return 0f; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Ideoligions.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                if (!ModsConfig.IdeologyActive)
                    return;

                IdeoPanel.Draw(inRect);
            }, "The ideoligions tab shows a failure notice. Nothing about the colony's faiths has been changed, "
               + "and RimWorld's own ideoligion screen is unaffected.");
        }
    }
}
