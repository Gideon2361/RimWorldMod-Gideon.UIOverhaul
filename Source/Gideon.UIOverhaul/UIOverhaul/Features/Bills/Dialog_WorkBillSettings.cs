using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Everything about one production bill that will not fit on its row, opened from that row.
    ///
    /// <b>This window exists because a production bill is not a growing bill.</b> The growing zone row says
    /// everything there is to say about a growing bill, so that feature needs no equivalent. Replacing the
    /// workbench tab with the same card list would otherwise have quietly dropped four settings vanilla put on
    /// the bench, and sending the player to the colony wide tab for them is exactly what Aaron asked not to
    /// happen. So they moved here rather than being lost.
    ///
    /// <b>The panel itself is <see cref="WorkBillSettingsPane"/>,</b> shared with the last step of the Add bill
    /// wizard. This class is the window around it: a title, a footer, and a bill to point it at.
    ///
    /// <b>It edits the bill in place and has no cancel.</b> That is how every other bill control in the game
    /// behaves, including vanilla's own dialog: there is no draft of a bill to commit or discard, and offering a
    /// cancel button would promise an undo that nothing behind it implements.
    /// </summary>
    public class Dialog_WorkBillSettings : Window
    {
        private const float HeaderHeight = 46f;
        private const float FooterHeight = 52f;
        private const float Pad = 12f;
        private const float EdgeInset = 8f;

        private readonly Bill_Production bill;

        private readonly WorkBillSettingsPane pane = new WorkBillSettingsPane();

        public Dialog_WorkBillSettings(Bill_Production subject)
        {
            bill = subject;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(920f, 620f);

        protected override float Margin => 0f;

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + HeaderHeight);

            UIGuardedPanel.Draw("Bills.BenchSettings", inRect, () => Contents(inRect),
                "The bill settings window shows a failure notice. The bill itself is unchanged.");
        }

        private void Contents(Rect inRect)
        {
            if (bill == null)
            {
                Close();

                return;
            }

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Widgets.DrawBoxSolid(inRect, GzpPalette.BGD);
            Text.Font = GameFont.Small;

            Header(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight));

            Rect body = new Rect(inRect.x + EdgeInset, inRect.y + HeaderHeight, inRect.width - EdgeInset * 2f,
                inRect.height - HeaderHeight - FooterHeight);

            pane.Draw(body, bill, palette);

            Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight));

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void Header(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            Color previous = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = GzpPalette.Stat;

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 8f, rect.width - 80f, 30f), bill.LabelCap);

            Text.Font = GameFont.Small;
            GUI.color = previous;

            if (GzpPalette.IconButton(new Rect(rect.xMax - Pad - 24f, rect.y + 11f, 24f, 24f), GzpTex.Close,
                    "Close"))
                Close();
        }

        private void Footer(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            // No "Changes apply as you make them." Every control in this window has always applied on the spot,
            // which is what a window with no Cancel button means; the sentence was reassurance about behaviour
            // nobody had reason to doubt. Removed 2026-08-23 on Aaron's instruction.

            if (UIActionButtonControl.Draw(new Rect(rect.xMax - Pad - 120f, rect.y + 10f, 120f, 32f), "Done", true, true))
                Close();
        }
    }
}
