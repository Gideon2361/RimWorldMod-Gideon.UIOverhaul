using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Power
{
    /// <summary>
    /// The window the power tab opens.
    ///
    /// Both axes are clamped to the screen, for the reason the ideoligions tab needed it: a tab that asks for
    /// more than the display has cannot be dragged back into view.
    /// </summary>
    public class MainTabWindow_Power : MainTabWindow
    {
        public override Vector2 RequestedTabSize
        {
            get
            {
                return UIGuard.Try("Power.RequestedSize",
                    () => new Vector2(
                        Mathf.Min(PowerPanel.WindowWidth, UI.screenWidth - 40f),
                        Mathf.Min(PowerPanel.WindowHeight, UI.screenHeight - 90f)),
                    new Vector2(960f, 600f), null);
            }
        }

        protected override float Margin
        {
            get { return 0f; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Power.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                PowerPanel.Draw(inRect);
            }, "The power tab shows a failure notice. Nothing about the colony's grids has been changed.");
        }
    }
}
