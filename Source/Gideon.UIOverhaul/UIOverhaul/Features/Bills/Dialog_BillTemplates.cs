using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Saved bill configurations, and applying one to a bill.
    ///
    /// <b>Every template is checked against this game before it is offered.</b> A template made in a colony running
    /// other mods can name a recipe or an ingredient that is not installed here, so each row says whether it is
    /// ready, how much of it would be skipped, or why it cannot be used at all. Finding that out when Apply is
    /// pressed would be too late.
    ///
    /// <b>Applying never guesses.</b> Anything that cannot be resolved leaves the bill's own value alone and is
    /// named in the panel, which is <see cref="BillTemplateApply"/>'s rule rather than this window's.
    /// </summary>
    public class Dialog_BillTemplates : Window
    {
        private const float TitleHeight = 28f;
        private const float RowHeight = 46f;
        private const float FooterHeight = 42f;

        private static readonly UITextBoxControl Name = new UITextBoxControl
        {
            Placeholder = "Template name",
            MaxLength = 64
        };

        /// <summary>The bill a template would be applied to, or captured from. May be null.</summary>
        private readonly Bill_Production target;

        private readonly bool capturing;

        private BillTemplate chosen;
        private Vector2 scroll = Vector2.zero;
        private string note;

        public Dialog_BillTemplates(Bill_Production bill = null, bool capture = false)
        {
            target = bill;
            capturing = capture && bill != null;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(720f, 560f);

        public override void PostOpen()
        {
            base.PostOpen();

            Name.Text = capturing && target != null ? target.LabelCap : string.Empty;
            note = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("Bills.Templates.Window", inRect, () => Contents(inRect),
                "The templates window failed to draw. Nothing was changed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, TitleHeight), "Bill templates");

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(inRect.x, inRect.y + TitleHeight, inRect.width - 30f, 18f),
                    "Kept outside every save, so a template made here is there for the next colony.");

                float y = inRect.y + TitleHeight + 24f;

                if (capturing)
                    y = Capture(inRect, y, palette);

                Rect list = new Rect(inRect.x, y + 4f, inRect.width, inRect.yMax - FooterHeight - y - 12f);

                DrawList(list, palette);

                Footer(inRect, palette);
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }

        /// <summary>The name box and Save button shown when the window was opened to capture a bill.</summary>
        private float Capture(Rect inRect, float y, UIColorPaletteDef palette)
        {
            Rect row = new Rect(inRect.x, y + 2f, inRect.width, 30f);

            Name.Draw(new Rect(row.x, row.y, row.width - 180f, 28f), palette);

            Rect save = new Rect(row.xMax - 172f, row.y, 172f, 28f);

            if (BillButtons.Button(save, "Save this bill", palette, true))
            {
                BillTemplate made = BillTemplateApply.Capture(target, Name.Text);

                if (made == null)
                {
                    note = "That bill could not be saved as a template.";
                }
                else
                {
                    BillTemplateStore.Add(made);

                    note = "Saved as " + made.Name + ".";
                    chosen = made;
                }
            }

            return row.yMax + 4f;
        }

        private void DrawList(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            List<BillTemplate> all = BillTemplateStore.All;
            Rect inner = rect.ContractedBy(1f);
            Rect view = new Rect(0f, 0f, inner.width - 18f, Mathf.Max(all.Count * RowHeight, inner.height));

            Widgets.BeginScrollView(inner, ref scroll, view);

            try
            {
                if (all.Count == 0)
                {
                    Text.Font = GameFont.Small;
                    GUI.color = palette.TextDisabled;

                    Widgets.Label(new Rect(10f, 10f, view.width - 20f, 40f),
                        "No templates yet. Save one from a bill to start.");

                    return;
                }

                float y = 0f;

                foreach (BillTemplate template in all)
                    y = Row(new Rect(0f, y, view.width, RowHeight), template, palette);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private float Row(Rect rect, BillTemplate template, UIColorPaletteDef palette)
        {
            bool on = template == chosen;

            if (on)
                UIElementPainter.FillRounded(rect, palette.AccentMuted);
            else if (Mouse.IsOver(rect))
                UIElementPainter.FillRounded(rect, palette.HoverOverlay);

            BillTemplateOutcome outcome = BillTemplateApply.Preview(template, target?.Map ?? Find.CurrentMap);

            Color dot = !outcome.Usable
                ? palette.Danger
                : outcome.Skipped.Count > 0
                    ? palette.Warning
                    : palette.Success;

            UIElementPainter.FillRounded(new Rect(rect.x + 8f, rect.y + rect.height * 0.5f - 3f, 6f, 6f), dot);

            Text.Font = GameFont.Small;
            GUI.color = outcome.Usable ? palette.TextPrimary : palette.TextDisabled;

            Widgets.Label(new Rect(rect.x + 22f, rect.y + 4f, rect.width - 190f, 20f), template.Name);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(rect.x + 22f, rect.y + 23f, rect.width - 190f, 18f), template.Summary());

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = outcome.Usable ? palette.TextSecondary : palette.Danger;

            Widgets.Label(new Rect(rect.xMax - 170f, rect.y, 160f, rect.height),
                !outcome.Usable
                    ? "unavailable"
                    : outcome.Skipped.Count == 0
                        ? "ready"
                        : outcome.Skipped.Count + " skipped");

            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(rect))
                chosen = template;

            return rect.yMax;
        }

        private void Footer(Rect inRect, UIColorPaletteDef palette)
        {
            Rect rect = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);

            Rect apply = new Rect(rect.xMax - 150f, rect.y + 6f, 150f, 28f);
            Rect remove = new Rect(rect.x, rect.y + 6f, 90f, 28f);

            if (chosen != null && BillButtons.Button(remove, "Delete", palette))
            {
                BillTemplateStore.Delete(chosen);

                note = "Deleted.";
                chosen = null;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;
            Text.Anchor = TextAnchor.MiddleLeft;

            string line = note;

            if (line == null && chosen != null)
            {
                BillTemplateOutcome outcome = BillTemplateApply.Preview(chosen, target?.Map ?? Find.CurrentMap);

                line = outcome.Line();

                if (outcome.Skipped.Count > 0)
                    line += "  " + outcome.Skipped[0];
            }

            Widgets.Label(new Rect(remove.xMax + 12f, rect.y, apply.x - remove.xMax - 22f, FooterHeight),
                line ?? "Choose a template.");

            Text.Anchor = TextAnchor.UpperLeft;

            bool ready = chosen != null && target != null
                                        && BillTemplateApply.Preview(chosen, target.Map).Usable;

            if (!ready)
            {
                UIElementPainter.OutlineRounded(apply, palette.Border, palette.ControlBackgroundFaded);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                Widgets.Label(apply, "Apply to bill");

                Text.Anchor = TextAnchor.UpperLeft;

                return;
            }

            if (BillButtons.Button(apply, "Apply to bill", palette, true))
            {
                BillTemplateOutcome done = BillTemplateApply.Apply(chosen, target, target.Map);

                note = done.Line();
            }
        }
    }

    /// <summary>
    /// The one button both bill windows draw.
    ///
    /// Shared rather than copied so the two windows cannot drift apart, and kept out of the save windows' chrome
    /// because a bills window reaching into a save feature for a button would be a worse dependency than a small
    /// helper of its own.
    /// </summary>
    internal static class BillButtons
    {
        internal static bool Button(Rect rect, string label, UIColorPaletteDef palette, bool primary = false)
        {
            bool over = Mouse.IsOver(rect);

            if (primary)
            {
                UIElementPainter.FillRounded(rect, palette.Accent);

                if (over)
                    UIElementPainter.FillRounded(rect, palette.HoverOverlay);
            }
            else
            {
                UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));
            }

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = primary ? palette.WindowBackground : palette.TextPrimary;

            Widgets.Label(rect, label);

            GUI.color = color;
            Text.Anchor = anchor;
            Text.Font = font;

            return Widgets.ButtonInvisible(rect);
        }
    }
}
