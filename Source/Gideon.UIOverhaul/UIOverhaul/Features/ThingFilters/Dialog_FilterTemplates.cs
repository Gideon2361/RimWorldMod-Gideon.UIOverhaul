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

namespace Gideon.UIOverhaul.Features.ThingFilters
{
    /// <summary>
    /// The saved filters, to pick one from or to save the current one into.
    ///
    /// <b>One window for both halves.</b> Saving and loading are the same list seen from two sides, and the thing
    /// somebody wants most often after opening it, overwriting the template they made last time by saving over its
    /// name, needs both on screen at once. The panel's two buttons open this at the same place; which one was
    /// pressed only decides whether the name box starts focused.
    ///
    /// <b>A click applies and closes.</b> A filter is not a decision anybody reviews in a confirm step, and the
    /// panel behind is redrawn the moment this closes, so the result is visible immediately.
    ///
    /// <b>Deleting asks nothing but is one click away from an accident,</b> so it is a small X at the end of the
    /// row rather than a button next to Apply, and it never removes the row the cursor is applying.
    /// </summary>
    internal class Dialog_FilterTemplates : Window
    {
        private const float HeaderHeight = 34f;
        private const float RowHeight = 30f;
        private const float FooterHeight = 62f;
        private const float Pad = 10f;

        private readonly ThingFilter filter;
        private readonly ThingFilter parent;
        private readonly string origin;
        private readonly Action changed;

        private readonly UITextBoxControl name = new UITextBoxControl
        {
            Placeholder = "Template name",
            MaxLength = 64
        };

        private Vector2 scroll;

        private string note;

        private Dialog_FilterTemplates(ThingFilter target, ThingFilter parentFilter, string what, Action onChanged)
        {
            filter = target;
            parent = parentFilter;
            origin = what;
            changed = onChanged;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
        }

        internal static void Open(ThingFilter target, ThingFilter parentFilter, string what, Action onChanged,
            bool saving)
        {
            UIGuard.Try("Filters.Templates.Open", () =>
            {
                Dialog_FilterTemplates window = new Dialog_FilterTemplates(target, parentFilter, what, onChanged);

                window.name.Text = saving ? what ?? string.Empty : string.Empty;

                Find.WindowStack.Add(window);
            }, "The template list could not be opened.");
        }

        public override Vector2 InitialSize => new Vector2(420f, 460f);

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Filters.Templates", inRect, () => Contents(inRect),
                "The template list failed to draw. Nothing has been changed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, HeaderHeight), "Filter templates");

                Text.Font = GameFont.Small;

                Rect list = new Rect(inRect.x, inRect.y + HeaderHeight, inRect.width,
                    Mathf.Max(RowHeight, inRect.height - HeaderHeight - FooterHeight - Pad));

                Rows(list, palette);
                Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private void Rows(Rect rect, UIColorPaletteDef palette)
        {
            List<FilterTemplate> all = FilterTemplateStore.All;
            Rect view = new Rect(0f, 0f, rect.width - 18f, all.Count * RowHeight + 4f);

            Widgets.BeginScrollView(rect, ref scroll, view);

            if (all.Count == 0)
            {
                GUI.color = palette.TextDisabled;
                Text.Font = GameFont.Tiny;

                Widgets.Label(new Rect(4f, 4f, view.width - 8f, 40f),
                    "No templates yet. Set a filter up the way you want it, then save it here and it is available "
                    + "in every colony.");

                Text.Font = GameFont.Small;
            }

            for (int i = 0; i < all.Count; i++)
                Row(new Rect(0f, i * RowHeight, view.width, RowHeight - 2f), all[i], palette);

            Widgets.EndScrollView();
        }

        private void Row(Rect row, FilterTemplate template, UIColorPaletteDef palette)
        {
            Rect remove = new Rect(row.xMax - 22f, row.center.y - 9f, 18f, 18f);
            Rect body = new Rect(row.x, row.y, row.width - 26f, row.height);
            bool over = Mouse.IsOver(body);

            if (over)
                UIElementPainter.FillRounded(row, palette.SurfaceRaised);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = over ? palette.TextPrimary : palette.TextSecondary;

            Widgets.LabelEllipses(new Rect(body.x + 8f, body.y, body.width * 0.6f, body.height), template.Name);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;

            bool previousWrap = Text.WordWrap;
            Text.WordWrap = false;

            Widgets.Label(new Rect(body.x + body.width * 0.6f, body.y, body.width * 0.4f - 4f, body.height),
                template.Count + (template.Count == 1 ? " thing" : " things"));

            Text.WordWrap = previousWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            if (!template.Origin.NullOrEmpty() || !template.Saved.NullOrEmpty())
            {
                TooltipHandler.TipRegion(body, (TipSignal) ("Saved from " + (template.Origin.NullOrEmpty()
                    ? "a filter"
                    : template.Origin) + (template.Saved.NullOrEmpty() ? string.Empty : " on " + template.Saved)
                    + ".\n\nClick to apply it to this filter."));
            }

            if (Widgets.ButtonInvisible(body))
            {
                template.ApplyTo(filter, parent);

                changed?.Invoke();

                SoundDefOf.Click.PlayOneShotOnCamera();

                Close();
            }

            if (Widgets.ButtonImage(remove, TexButton.Delete, palette.TextDisabled, palette.Danger))
            {
                FilterTemplateStore.Delete(template);

                note = "Deleted " + template.Name + ".";
            }
        }

        /// <summary>
        /// The name box and the save button.
        ///
        /// <b>Saving with a name already in the list makes a second one rather than replacing it,</b> which is the
        /// store's own rule and the safe half of the ambiguity: nobody loses a template they made because somebody
        /// typed a name twice. The note says which name it actually got.
        /// </summary>
        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            Rect box = new Rect(rect.x, rect.y, rect.width - 110f, 28f);

            name.Draw(box, palette);

            if (UIActionButtonControl.Draw(new Rect(box.xMax + 8f, rect.y, 102f, 28f), "Save", true, true))
                SaveCurrent();

            if (note.NullOrEmpty())
                return;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.x, rect.y + 30f, rect.width, 28f), note);

            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        private void SaveCurrent()
        {
            UIGuard.Try("Filters.Templates.Save", () =>
            {
                FilterTemplate template = FilterTemplate.Capture(filter, origin);

                if (template == null)
                    return;

                template.Name = name.Text;

                FilterTemplateStore.Add(template);

                note = "Saved as \"" + template.Name + "\", " + template.Count + " things.";

                name.Text = template.Name;

                SoundDefOf.Click.PlayOneShotOnCamera();
            }, "The template was not saved.");
        }
    }
}
