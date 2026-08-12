using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Work
{
    /// <summary>
    /// Manages saved work priority templates: view them, rename them, edit what they assign, delete them, and
    /// put one on a pawn.
    ///
    /// One window for all of that rather than a picker and a separate editor. A template is only ever chosen by
    /// its name and its contents, and a picker that shows nothing but names makes the player apply one to find
    /// out what it does -- so the list and the contents of the selected entry are on screen together.
    ///
    /// Opened two ways. From the apply button on a pawn's row it carries that pawn, and each entry offers to
    /// apply to them; from the save button it carries nobody and is purely a manager. The difference is one
    /// field, so the same window covers both.
    /// </summary>
    public class Dialog_WorkTemplates : Window
    {
        private const float ListWidth = 300f;
        private const float RowHeight = 62f;
        private const float RowGap = 4f;
        private const float ButtonSize = 26f;
        private const float DetailRowHeight = 28f;
        private const float PriorityBoxSize = 24f;
        private const float FooterHeight = 40f;
        private const float Pad = 10f;

        /// <summary>The pawn to apply to, or null when the window was opened to manage rather than to apply.</summary>
        private readonly Pawn target;

        private WorkPriorityTemplate selected;

        private Vector2 listScroll;
        private Vector2 detailScroll;

        public Dialog_WorkTemplates(Pawn target = null, WorkPriorityTemplate select = null)
        {
            this.target = target;

            selected = select ?? (WorkTemplateStore.Templates.Count > 0 ? WorkTemplateStore.Templates[0] : null);

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(920f, 640f);

        /// <summary>
        /// Saved on close rather than on every keystroke.
        ///
        /// Renaming happens in a text field, and writing the file per character would mean a file write per
        /// letter typed. Everything that is not typing -- creating, deleting, applying -- saves as it happens,
        /// so the only thing riding on this is the names.
        /// </summary>
        public override void PostClose()
        {
            base.PostClose();

            foreach (WorkPriorityTemplate template in WorkTemplateStore.Templates)
                template.name = WorkTemplateStore.UniqueName(template.name, template);

            WorkTemplateStore.Save();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect title = new Rect(inRect.x, inRect.y, inRect.width - 36f, 32f);

            // Only the title strip drags the window. Window calls GUI.DragWindow() across its whole area when
            // draggable is set, which would fight the name fields for the press -- the same problem the bar
            // editor's rows had. Assigning it from the pointer position here runs before Window reaches that
            // call, so the answer already reflects where the cursor is.
            draggable = title.Contains(Event.current.mousePosition);

            DrawTitle(title, palette);

            Rect body = new Rect(inRect.x, title.yMax + 6f, inRect.width,
                inRect.height - title.height - 6f - FooterHeight);

            DrawList(new Rect(body.x, body.y, ListWidth, body.height), palette);

            Rect detail = new Rect(body.x + ListWidth + Pad, body.y,
                body.width - ListWidth - Pad, body.height);

            DrawDetail(detail, palette);

            Rect footer = new Rect(inRect.x, body.yMax + 6f, inRect.width, FooterHeight - 6f);
            DrawFooter(footer, palette);
        }

        private void DrawTitle(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;
            Widgets.Label(rect, target != null
                ? "Apply a work template to " + target.LabelShortCap
                : "Work priority templates");

            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        // ---------------------------------------------------------------------------------------
        // The list
        // ---------------------------------------------------------------------------------------

        private static readonly UICardControl RowCard = new UICardControl { Padding = 0f, AccentWidth = 3f };

        private void DrawList(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            List<WorkPriorityTemplate> templates = WorkTemplateStore.Templates;

            if (templates.Count == 0)
            {
                DrawEmptyNotice(rect.ContractedBy(16f), palette);
                return;
            }

            Rect inner = rect.ContractedBy(6f);
            Rect view = new Rect(0f, 0f, inner.width - 18f, templates.Count * (RowHeight + RowGap));

            Widgets.BeginScrollView(inner, ref listScroll, view);

            float y = 0f;
            for (int i = 0; i < templates.Count; i++)
            {
                if (DrawListRow(new Rect(0f, y, view.width, RowHeight), templates[i], palette))
                {
                    // A row acted on itself, so the list this loop is walking may no longer be the list to
                    // draw. Drawing stops here and the next frame starts again from whatever the store now
                    // holds, rather than finishing a pass over entries that have moved or gone.
                    Widgets.EndScrollView();
                    return;
                }

                y += RowHeight + RowGap;
            }

            Widgets.EndScrollView();
        }

        private static void DrawEmptyNotice(Rect rect, UIColorPaletteDef palette)
        {
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            GUI.color = palette.TextSecondary;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(rect, "No templates saved yet.\n\nUse the disk button on a colonist's row in the "
                                + "work tab to save their priorities as one.");

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
        }

        /// <returns>True when the list was changed, which means this frame must stop drawing it.</returns>
        private bool DrawListRow(Rect row, WorkPriorityTemplate template, UIColorPaletteDef palette)
        {
            bool isSelected = template == selected;

            RowCard.AccentColor = isSelected ? palette.Accent : palette.AccentMuted;
            RowCard.BackgroundColor = isSelected ? palette.SurfaceRaised : palette.SurfaceSunken;
            RowCard.DrawChrome(row, palette);

            float x = row.xMax - 4f;

            // Delete first, so its position is the same whether or not the apply button is present -- a
            // destructive button that moves under the pointer is how a wrong one gets pressed.
            x -= ButtonSize;
            if (IconAction(new Rect(x, row.y + (row.height - ButtonSize) * 0.5f, ButtonSize, ButtonSize),
                    WorkToolIcons.Clear, "X", palette, "Delete this template"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Delete the template \"" + template.name + "\"?", () =>
                    {
                        WorkTemplateStore.Remove(template);

                        if (selected == template)
                        {
                            selected = WorkTemplateStore.Templates.Count > 0
                                ? WorkTemplateStore.Templates[0]
                                : null;
                        }
                    }, true));

                return true;
            }

            if (target != null)
            {
                x -= ButtonSize + 4f;
                if (IconAction(new Rect(x, row.y + (row.height - ButtonSize) * 0.5f, ButtonSize, ButtonSize),
                        WorkToolIcons.Apply, ">", palette, "Apply to " + target.LabelShortCap))
                {
                    Apply(template);
                    return true;
                }
            }

            // The name is a text field, so renaming is where the name already is. No rename mode, no dialog.
            Rect nameRect = new Rect(row.x + 12f, row.y + 8f, x - row.x - 18f, 24f);
            template.name = ThemedTextField(nameRect, template.name, palette);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            int assigned = template.AssignedCount;
            Widgets.Label(new Rect(row.x + 12f, row.y + 36f, row.width - 24f, 18f),
                assigned == 1 ? "1 work type assigned" : assigned + " work types assigned");

            GUI.color = previousColor;
            Text.Font = previousFont;

            // Selecting is the leftover click: the buttons and the field claim theirs first, and this only
            // sees what none of them wanted.
            if (Widgets.ButtonInvisible(new Rect(row.x, row.y + 34f, row.width, row.height - 34f)))
            {
                selected = template;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return false;
        }

        private void Apply(WorkPriorityTemplate template)
        {
            int skipped = template.ApplyTo(target);

            string message = "Applied \"" + template.name + "\" to " + target.LabelShortCap + ".";
            if (skipped > 0)
            {
                message += " " + skipped + (skipped == 1 ? " work type was" : " work types were")
                                         + " left off; " + target.LabelShortCap + " cannot do "
                                         + (skipped == 1 ? "it." : "them.");
            }

            Messages.Message(message, MessageTypeDefOf.TaskCompletion, false);
            Close();
        }

        // ---------------------------------------------------------------------------------------
        // The selected template's contents
        // ---------------------------------------------------------------------------------------

        private void DrawDetail(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            if (selected == null)
                return;

            Rect header = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 24f);

            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;

            GUI.color = palette.TextPrimary;
            Widgets.Label(header, selected.name);
            GUI.color = palette.TextSecondary;

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(header.x, header.yMax, header.width, 18f),
                "Left click a number to raise the priority, right click to lower it. 0 means the work is not "
                + "assigned.");

            Text.Font = previousFont;
            GUI.color = previousColor;

            Rect inner = new Rect(rect.x + 6f, header.yMax + 22f, rect.width - 12f,
                rect.yMax - header.yMax - 28f);

            List<WorkTypeDef> works = WorkPanel.VisibleWorkTypes;
            Rect view = new Rect(0f, 0f, inner.width - 18f, works.Count * DetailRowHeight);

            Widgets.BeginScrollView(inner, ref detailScroll, view);

            float y = 0f;
            foreach (WorkTypeDef work in works)
            {
                DrawDetailRow(new Rect(0f, y, view.width, DetailRowHeight), work, palette);
                y += DetailRowHeight;
            }

            Widgets.EndScrollView();
        }

        private void DrawDetailRow(Rect row, WorkTypeDef work, UIColorPaletteDef palette)
        {
            if (Mouse.IsOver(row))
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            int priority = selected.PriorityFor(work);

            Rect box = new Rect(row.x + 4f, row.y + (row.height - PriorityBoxSize) * 0.5f,
                PriorityBoxSize, PriorityBoxSize);

            Widgets.DrawBoxSolid(box, priority == 0 ? palette.SurfaceSunken : palette.SurfaceRaised);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = WorkPanel.ColorOfPriority(priority, palette);
            Widgets.Label(box, priority.ToString());

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = priority > 0 ? palette.TextPrimary : palette.TextSecondary;
            Widgets.Label(new Rect(box.xMax + 8f, row.y, row.width - box.width - 16f, row.height),
                work.gerundLabel.CapitalizeFirst());

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;

            if (Event.current.type != EventType.MouseDown || !Mouse.IsOver(row))
                return;

            int step = Event.current.button == 1 ? -1 : 1;
            int next = priority + step;

            if (next > WorkPriorityRange.Lowest)
                next = 0;
            else if (next < 0)
                next = WorkPriorityRange.Lowest;

            selected.Set(work, next);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            Event.current.Use();
        }

        // ---------------------------------------------------------------------------------------
        // Footer
        // ---------------------------------------------------------------------------------------

        private void DrawFooter(Rect rect, UIColorPaletteDef palette)
        {
            if (selected != null)
            {
                Rect clear = new Rect(rect.x, rect.y, 150f, rect.height);
                if (SmallButton(clear, "Clear template", palette))
                {
                    foreach (WorkTypeDef work in WorkPanel.VisibleWorkTypes)
                        selected.Set(work, 0);
                }

                if (target != null)
                {
                    Rect apply = new Rect(clear.xMax + 8f, rect.y, 220f, rect.height);
                    if (SmallButton(apply, "Apply to " + target.LabelShortCap, palette))
                        Apply(selected);
                }
            }

            Rect close = new Rect(rect.xMax - 120f, rect.y, 120f, rect.height);
            if (SmallButton(close, "Close", palette))
                Close();
        }

        // ---------------------------------------------------------------------------------------
        // Small themed widgets
        //
        // Deliberately local copies of the button and field the bar editor uses. Promoting them to the
        // framework is the right move once a third window wants them, but a control has to earn its public
        // surface -- see the Help folder's note on what belongs in UIFramework.
        // ---------------------------------------------------------------------------------------

        private static bool SmallButton(Rect r, string label, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(r);
            UIElementPainter.PaintButton(r, palette, over, over && Input.GetMouseButton(0));

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            GUI.color = palette.TextPrimary;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;

            return Widgets.ButtonInvisible(r);
        }

        private static bool IconAction(Rect r, Texture2D icon, string fallbackGlyph,
            UIColorPaletteDef palette, string tooltip)
        {
            TooltipHandler.TipRegion(r, (TipSignal) tooltip);

            if (icon == null)
                return SmallButton(r, fallbackGlyph, palette);

            bool over = Mouse.IsOver(r);
            UIElementPainter.PaintButton(r, palette, over, over && Input.GetMouseButton(0));

            Color previous = GUI.color;
            GUI.color = over ? palette.TextPrimary : palette.TextSecondary;
            GUI.DrawTexture(r.ContractedBy(3f), icon, ScaleMode.ScaleToFit);
            GUI.color = previous;

            return Widgets.ButtonInvisible(r);
        }

        private static string ThemedTextField(Rect r, string text, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(r, palette.SurfaceSunken);

            Color previousColor = GUI.color;
            GUI.color = palette.TextPrimary;

            string edited = Widgets.TextField(r.ContractedBy(2f), text);

            GUI.color = previousColor;
            return edited;
        }
    }
}
