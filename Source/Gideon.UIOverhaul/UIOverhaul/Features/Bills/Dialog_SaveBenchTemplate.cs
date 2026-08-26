using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Names a bench template and saves it.
    ///
    /// <b>A small window rather than a name box wedged into the tab.</b> The bench tab is 520 by 480 and already
    /// carries a button, a count and a list; a text field appearing in it would either displace the list or sit in
    /// the corner unexplained. This says what is being saved, how many bills that is, and what a name clash will
    /// do about it, which is more than a field could.
    ///
    /// <b>It reports the count before saving, not after.</b> Only production bills travel, so a bench holding
    /// something a mod added will save fewer bills than it appears to have. Saying so up front is the difference
    /// between a template that is smaller than expected and one that looks broken on import.
    /// </summary>
    public class Dialog_SaveBenchTemplate : Window
    {
        private static readonly UITextBoxControl Name = new UITextBoxControl
        {
            Placeholder = "Template name",
            MaxLength = 64
        };

        private readonly Building_WorkTable bench;

        private string note;

        public Dialog_SaveBenchTemplate(Building_WorkTable table)
        {
            bench = table;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(460f, 260f);

        public override void PostOpen()
        {
            base.PostOpen();

            // Seeded with the bench's own name, since that is what somebody would type. Cleared of any leftover
            // text from the last time this window was open, because the box is static.
            Name.Text = bench?.LabelCap ?? string.Empty;
            note = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Bills.SaveBench", inRect, () => Contents(inRect),
                "This window failed to draw. Nothing has been saved.");
        }

        private void Contents(Rect inRect)
        {
            if (bench == null)
            {
                Close();

                return;
            }

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont font = Text.Font;
            Color color = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "Save bench as template");

                Text.Font = GameFont.Small;
                GUI.color = palette.TextSecondary;

                int count = Countable();

                Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 44f),
                    count == 1
                        ? "One bill from " + bench.LabelCap + "."
                        : count + " bills from " + bench.LabelCap + ".");

                GUI.color = palette.TextPrimary;

                Name.Draw(new Rect(inRect.x, inRect.y + 82f, inRect.width, 30f), palette);

                GUI.color = palette.TextSecondary;
                Text.Font = GameFont.Tiny;

                Widgets.Label(new Rect(inRect.x, inRect.y + 118f, inRect.width, 44f),
                    note
                    ?? "A name already in use gets a number added rather than replacing what is there. "
                    + "The order of the bills is kept, since that is their priority.");

                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;

                Rect save = new Rect(inRect.xMax - 130f, inRect.yMax - 34f, 130f, 30f);
                Rect cancel = new Rect(save.x - 108f, save.y, 100f, 30f);

                if (BillButtons.Button(cancel, "Cancel", palette))
                    Close();

                if (Name.IsEmpty)
                {
                    // Drawn by the control as a refusing primary, which is what it is: the button this window
                    // exists to press, saying it will not go yet. It used to be drawn by hand here, and set no
                    // font at all -- so its label took whatever size the last thing drawn had left behind.
                    UIActionButtonControl.Draw(save, "Save", palette, true, false, GameFont.Small,
                        "Give the template a name first.");

                    return;
                }

                if (BillButtons.Button(save, "Save", palette, true))
                    Commit();
            }
            finally
            {
                GUI.color = color;
                Text.Font = font;
            }
        }

        /// <summary>How many of the bench's bills a template can actually hold.</summary>
        private int Countable()
        {
            return UIGuard.Try("Bills.CountBenchBills", () =>
            {
                int count = 0;

                if (bench.billStack?.Bills == null)
                    return 0;

                foreach (Bill bill in bench.billStack.Bills)
                {
                    if (bill is Bill_Production)
                        count++;
                }

                return count;
            }, 0, null);
        }

        private void Commit()
        {
            BillTemplate made = BillTemplateApply.CaptureBench(bench, Name.Text);

            if (made == null)
            {
                note = "That bench could not be read.";

                return;
            }

            if (made.Bills.Count == 0)
            {
                note = "Nothing on this bench can be saved as a template.";

                return;
            }

            BillTemplateStore.Add(made);

            Close();
        }
    }
}
