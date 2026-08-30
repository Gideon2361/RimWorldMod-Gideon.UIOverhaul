using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Quests
{
    /// <summary>
    /// The window the quests tab opens.
    ///
    /// Both axes are clamped to the screen, for the reason the ideoligions tab needed it: a tab that asks for
    /// more than the display has cannot be dragged back into view.
    /// </summary>
    public class MainTabWindow_Quests : MainTabWindow
    {
        public override Vector2 RequestedTabSize
        {
            get
            {
                return UIGuard.Try("Quests.RequestedSize",
                    () => new Vector2(
                        Mathf.Min(QuestPanel.WindowWidth, UI.screenWidth - 40f),
                        Mathf.Min(QuestPanel.WindowHeight, UI.screenHeight - 90f)),
                    new Vector2(980f, 620f), null);
            }
        }

        protected override float Margin
        {
            get { return 0f; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Quests.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                QuestPanel.Draw(inRect);
            }, "The quests tab shows a failure notice. No quest has been accepted, dismissed or changed, and "
               + "RimWorld's own quest handling is unaffected.");
        }
    }
}
