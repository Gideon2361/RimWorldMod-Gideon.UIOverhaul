using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Saved bills: load one into the bill you have open, save that bill as a new one, or tidy the list.
    ///
    /// <b>One window for both halves of the job.</b> Saving and loading are the same list read two ways, and
    /// splitting them into a save dialog and a load dialog would mean naming a template in a window that cannot
    /// show you the names already taken.
    ///
    /// <b>Only templates of the kind that asked.</b> A hunting bill cannot use a taming template and there is
    /// nothing useful to do with one it is shown, so the list is filtered rather than greyed. The heading says
    /// which kind is on screen so an empty list does not read as a lost file.
    ///
    /// <b>Loading closes the window.</b> The bill underneath changes shape the moment a template lands on it, and
    /// leaving this open over a window that no longer matches what it shows invites a second click that undoes
    /// the first by accident.
    /// </summary>
    internal class Dialog_AnimalBillTemplates : Window
    {
        private static readonly UITextBoxControl NewName = new UITextBoxControl
        {
            Placeholder = "Name this template",
            MaxLength = 40
        };

        private readonly bool taming;
        private readonly Func<string, AnimalBillTemplate> capture;
        private readonly Action<AnimalBillTemplate> load;

        private Vector2 scroll;
        private string problem;

        /// <summary>
        /// <paramref name="capture"/> turns the open bill into a template under a given name, and
        /// <paramref name="load"/> pours a chosen one back into it. Both are supplied by the window that opened
        /// this, so this file never has to know which kind of bill it is serving beyond
        /// <paramref name="taming"/>.
        /// </summary>
        internal Dialog_AnimalBillTemplates(bool taming, Func<string, AnimalBillTemplate> capture,
            Action<AnimalBillTemplate> load)
        {
            this.taming = taming;
            this.capture = capture;
            this.load = load;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(560f, 520f);

        public override void PostOpen()
        {
            base.PostOpen();

            NewName.Clear();

            problem = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Animals.BillTemplates", inRect, () => Contents(inRect),
                "This window failed to draw. Your saved bills are unchanged.");
        }

        private const float RowHeight = 34f;

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, 32f),
                    taming ? "Saved taming bills" : "Saved hunting bills");

                Text.Font = GameFont.Small;

                float y = inRect.y + 40f;

                y = Saver(new Rect(inRect.x, y, inRect.width, 30f), palette);

                if (!problem.NullOrEmpty())
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = palette.Danger;

                    float height = Text.CalcHeight(problem, inRect.width);

                    Widgets.Label(new Rect(inRect.x, y, inRect.width, height), problem);

                    y += height + 4f;

                    Text.Font = GameFont.Small;
                    GUI.color = palette.TextPrimary;
                }

                List<AnimalBillTemplate> templates = AnimalBillTemplateStore.Of(taming);

                Rect list = new Rect(inRect.x, y, inRect.width,
                    Mathf.Max(0f, inRect.height - (y - inRect.y) - 40f));

                if (templates.Count == 0)
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = palette.TextSecondary;

                    Widgets.Label(list, "Nothing saved yet. Set a bill up the way you want it, then save it here "
                                        + "and it will be offered on every colony you play.");
                }
                else
                {
                    Rect view = new Rect(0f, 0f, list.width - 18f, templates.Count * RowHeight + 4f);

                    Widgets.BeginScrollView(list, ref scroll, view);

                    float at = 0f;

                    for (int i = 0; i < templates.Count; i++)
                    {
                        Row(new Rect(0f, at, view.width, RowHeight - 4f), templates[i], palette);

                        at += RowHeight;
                    }

                    Widgets.EndScrollView();
                }

                if (GzpPalette.GrayButton(new Rect(inRect.xMax - 110f, inRect.yMax - 32f, 110f, 32f), "Close",
                        true, true))
                    Close();
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>The name box and the button that saves the open bill under it.</summary>
        private float Saver(Rect rect, UIColorPaletteDef palette)
        {
            NewName.Draw(new Rect(rect.x, rect.y, Mathf.Max(80f, rect.width - 130f), 28f), palette);

            if (Button(new Rect(rect.xMax - 124f, rect.y, 124f, 28f), "Save this bill", palette))
                SaveCurrent();

            return rect.yMax + 8f;
        }

        private void SaveCurrent()
        {
            problem = null;

            string name = (NewName.Text ?? string.Empty).Trim();

            if (name.NullOrEmpty())
            {
                problem = "Give the template a name first.";

                return;
            }

            AnimalBillTemplate made = capture(name);

            if (made == null)
            {
                problem = "This bill could not be saved.";

                return;
            }

            // Add renames rather than refusing, so the name that ends up in the list is the one to report back:
            // saving twice under one name leaves two templates rather than silently replacing the first.
            AnimalBillTemplateStore.Add(made);

            NewName.Clear();

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void Row(Rect rect, AnimalBillTemplate template, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.OutlineRounded(rect, over ? palette.Accent : palette.Border,
                palette.PanelBackground);

            Rect inner = rect.ContractedBy(6f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                float buttons = 150f;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                Widgets.LabelEllipses(
                    new Rect(inner.x, inner.y, Mathf.Max(40f, inner.width - buttons - 140f), inner.height),
                    template.Name);

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextSecondary;

                Widgets.LabelEllipses(new Rect(inner.xMax - buttons - 136f, inner.y, 134f, inner.height),
                    template.Summary);

                if (Button(new Rect(inner.xMax - 62f, inner.y, 62f, inner.height), "Delete", palette))
                {
                    AnimalBillTemplateStore.Delete(template);

                    problem = null;
                }

                if (Button(new Rect(inner.xMax - 128f, inner.y, 62f, inner.height), "Load", palette))
                {
                    load(template);

                    Close();
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private bool Button(Rect rect, string label, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextPrimary;

                Widgets.Label(rect, label);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return Widgets.ButtonInvisible(rect);
        }
    }
}
