using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Work;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Pawns.Templates
{
    /// <summary>
    /// Manages saved templates: view them, rename them, edit what they assign, delete them, and put one on a pawn.
    ///
    /// One window for all of that rather than a picker and a separate editor. A template is only ever chosen by its
    /// name and its contents, and a picker that shows nothing but names makes the player apply one to find out what
    /// it does -- so the list and the contents of the selected entry are on screen together.
    ///
    /// <b>The scope it is opened with decides what it shows and what applying writes.</b> The same window serves the
    /// three sets of tools: from a pawn's card it manages whole-pawn templates, from the work priorities pane
    /// priorities alone, from the schedule strip schedules alone. It lists every template that <i>covers</i> that
    /// scope -- so a whole-pawn template appears in the schedule tools, since it does hold a schedule -- and passes
    /// the scope as a limit when applying, so pressing apply there writes the schedule and nothing else. Filtering
    /// on an exact match instead would hide the player's most complete template from most of the places they would
    /// want part of it.
    ///
    /// Opened two ways. From the apply button on a row it carries that pawn, and each entry offers to apply to them;
    /// from the save button it carries nobody and is purely a manager. The difference is one field, so the same
    /// window covers both.
    /// </summary>
    public class Dialog_PawnTemplates : Window
    {
        private const float ListWidth = 300f;
        private const float RowHeight = 62f;
        private const float RowGap = 4f;
        private const float ButtonSize = 26f;
        private const float DetailRowHeight = 28f;
        private const float PriorityBoxSize = 24f;
        private const float FooterHeight = 40f;
        private const float SectionHeaderHeight = 22f;
        private const float PolicyRowHeight = 22f;
        private const float Pad = 10f;

        /// <summary>The title row, which is also the only part of this window that drags it.</summary>
        private const float TitleHeight = 32f;

        /// <summary>The pawn to apply to, or null when the window was opened to manage rather than to apply.</summary>
        private readonly Pawn target;

        /// <summary>
        /// Which part of a pawn this window is about. Never <see cref="PawnTemplateScope.None"/>: a window scoped to
        /// nothing would list nothing and apply nothing, so an empty scope is read as the whole pawn.
        /// </summary>
        private readonly PawnTemplateScope scope;

        private PawnTemplate selected;

        private Vector2 listScroll;
        private Vector2 detailScroll;

        public Dialog_PawnTemplates(Pawn target = null,
            PawnTemplateScope scope = PawnTemplateScope.Everything, PawnTemplate select = null)
        {
            this.target = target;
            this.scope = scope == PawnTemplateScope.None ? PawnTemplateScope.Everything : scope;

            List<PawnTemplate> offered = PawnTemplateStore.ForScope(this.scope);

            // The template just saved is selected even when it is not the first in the list, which is the whole
            // reason the caller passes one: saving then having to find your new template is a step for nothing.
            selected = select ?? (offered.Count > 0 ? offered[0] : null);

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
        /// Renaming happens in a text field, and writing the file per character would mean a file write per letter
        /// typed. Everything that is not typing -- creating, deleting, editing, applying -- saves as it happens, so
        /// the only thing riding on this is the names.
        ///
        /// Guarded because this is where the templates are written to disk, and because RimWorld does not wrap
        /// window lifecycle methods the way it wraps DoWindowContents. An escape from here would leave
        /// WindowStack.TryRemove partway through closing the window -- and would lose the renames it was called to
        /// save without saying so.
        /// </summary>
        public override void PostClose()
        {
            base.PostClose();

            UIGuard.Try("Pawns.SaveTemplates", () =>
            {
                foreach (PawnTemplate template in PawnTemplateStore.Templates)
                    template.name = PawnTemplateStore.UniqueName(template.name, template);

                PawnTemplateStore.Save();
            }, "Template names renamed in this session were not saved. Everything else about the templates was "
               + "saved as it happened.");
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("Pawns.TemplatesWindow", inRect, () => DrawContents(inRect),
                "The templates window shows a failure notice; saved templates are unaffected.");
        }

        private void DrawContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect title = new Rect(inRect.x, inRect.y, inRect.width - 36f, TitleHeight);

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
                ? "Apply " + ScopeNoun + " to " + target.LabelShortCap
                : ScopeNoun.CapitalizeFirst() + "s");

            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// What this window calls a template, given what it is scoped to.
        ///
        /// Worth saying rather than calling everything "a template": the player pressed a button on a schedule
        /// strip, and a window that then talks about templates in general leaves them wondering whether applying one
        /// is about to rewrite their work priorities.
        /// </summary>
        private string ScopeNoun
        {
            get
            {
                if (scope == PawnTemplateScope.Priorities)
                    return "work priority template";

                if (scope == PawnTemplateScope.Schedule)
                    return "schedule template";

                if (scope == PawnTemplateScope.Policies)
                    return "policy template";

                return "pawn template";
            }
        }

        // ---------------------------------------------------------------------------------------
        // The list
        // ---------------------------------------------------------------------------------------

        private static readonly UICardControl RowCard = new UICardControl { Padding = 0f, AccentWidth = 3f };

        private void DrawList(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            List<PawnTemplate> templates = PawnTemplateStore.ForScope(scope);

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
                    // A row acted on itself, so the list this loop is walking may no longer be the list to draw.
                    // Drawing stops here and the next frame starts again from whatever the store now holds, rather
                    // than finishing a pass over entries that have moved or gone.
                    Widgets.EndScrollView();
                    return;
                }

                y += RowHeight + RowGap;
            }

            Widgets.EndScrollView();
        }

        private void DrawEmptyNotice(Rect rect, UIColorPaletteDef palette)
        {
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            GUI.color = palette.TextSecondary;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(rect, "No " + ScopeNoun + "s saved yet.\n\nUse the disk button on a colonist's row to "
                                + "save theirs as one.");

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
        }

        /// <returns>True when the list was changed, which means this frame must stop drawing it.</returns>
        private bool DrawListRow(Rect row, PawnTemplate template, UIColorPaletteDef palette)
        {
            bool isSelected = template == selected;

            RowCard.AccentColor = isSelected ? palette.Accent : palette.AccentMuted;
            RowCard.BackgroundColor = isSelected ? palette.SurfaceRaised : palette.SurfaceSunken;
            RowCard.DrawChrome(row, palette);

            float x = row.xMax - 4f;

            // Delete first, so its position is the same whether or not the apply button is present -- a destructive
            // button that moves under the pointer is how a wrong one gets pressed.
            x -= ButtonSize;
            if (IconAction(new Rect(x, row.y + (row.height - ButtonSize) * 0.5f, ButtonSize, ButtonSize),
                    WorkToolIcons.Clear, "X", palette, "Delete this template"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Delete the template \"" + template.name + "\"?", () =>
                    {
                        PawnTemplateStore.Remove(template);

                        if (selected == template)
                        {
                            List<PawnTemplate> remaining = PawnTemplateStore.ForScope(scope);
                            selected = remaining.Count > 0 ? remaining[0] : null;
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

            // What the template holds, not what this window is scoped to: a whole-pawn template offered to the
            // schedule tools should still say it is a whole-pawn template, so applying part of it is no surprise.
            Widgets.Label(new Rect(row.x + 12f, row.y + 36f, row.width - 24f, 18f), template.Describe());

            GUI.color = previousColor;
            Text.Font = previousFont;

            // Selecting is the leftover click: the buttons and the field claim theirs first, and this only sees
            // what none of them wanted.
            if (Widgets.ButtonInvisible(new Rect(row.x, row.y + 34f, row.width, row.height - 34f)))
            {
                selected = template;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return false;
        }

        private void Apply(PawnTemplate template)
        {
            // The window's scope is the limit, so the schedule strip's apply button writes a schedule even when the
            // template it was given describes a whole pawn.
            PawnTemplateApplyResult result = template.ApplyTo(target, scope);

            string message = "Applied \"" + template.name + "\" to " + target.LabelShortCap + ".";
            string trouble = result.Describe(target.LabelShortCap);

            if (!trouble.NullOrEmpty())
                message += " " + trouble;

            // The remembered snapshot describes the old numbers, and would put them back the next time manual
            // priorities were switched off and on again.
            if ((result.applied & PawnTemplateScope.Priorities) == PawnTemplateScope.Priorities)
                WorkPanel.ForgetRemembered(target);

            PawnAttributes.Invalidate(target);

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

            GUI.color = palette.TextPrimary;
            Widgets.Label(header, selected.name);
            GUI.color = previousColor;

            float y = header.yMax + 4f;

            // Only the parts this window is scoped to. From the schedule strip, a whole-pawn template shows its
            // schedule and not its priorities, because its priorities are not what pressing apply here would write.
            if (Shows(PawnTemplateScope.Schedule))
                y = DrawScheduleSection(rect, y, palette);

            if (Shows(PawnTemplateScope.Policies))
                y = DrawPoliciesSection(rect, y, palette);

            if (Shows(PawnTemplateScope.Priorities))
                DrawPrioritiesSection(new Rect(rect.x, y, rect.width, rect.yMax - y - 6f), palette);
        }

        /// <summary>Whether a part is both in this window's scope and something the selected template speaks for.</summary>
        private bool Shows(PawnTemplateScope part)
        {
            return (scope & part) == part && selected != null && selected.Covers(part);
        }

        private static float DrawSectionHeader(Rect rect, float y, string label, string hint,
            UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(rect.x + 10f, y, rect.width - 20f, SectionHeaderHeight),
                hint.NullOrEmpty() ? label : label + "  --  " + hint);

            GUI.color = previousColor;
            Text.Font = previousFont;

            return y + SectionHeaderHeight;
        }

        // ---------------------------------------------------------------------------------------
        // Schedule
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The template's day, painted with the same brush and the same strip a pawn's row uses.
        ///
        /// Editable, unlike the policies below, because a schedule is a shape rather than a list of choices: the
        /// reason to open a schedule template is usually to move the sleep block, and re-capturing it from a pawn
        /// to do that would mean editing the pawn you were trying to leave alone.
        /// </summary>
        private float DrawScheduleSection(Rect rect, float y, UIColorPaletteDef palette)
        {
            y = DrawSectionHeader(rect, y, "Schedule", "click or drag to paint; an unset hour is left as the pawn "
                                                      + "had it", palette);

            Rect picker = new Rect(rect.x + 10f, y + 2f, ScheduleStrip.BrushWidth, ScheduleStrip.CellHeight);
            ScheduleStrip.DrawBrushPicker(picker, palette);

            Rect hours = new Rect(picker.xMax + 8f, y + 2f, rect.xMax - picker.xMax - 18f,
                ScheduleStrip.CellHeight);

            ScheduleStrip.DrawHours(hours, palette,
                hour => selected.AssignmentAt(hour),
                (hour, assignment) => SetHour(hour, assignment),

                // No hour is outlined. A template describes any day rather than today, so marking the current hour
                // would be pointing at something the template has no relationship to.
                -1);

            return hours.yMax + 8f;
        }

        /// <summary>
        /// Writes one hour and saves.
        ///
        /// Saved per edit rather than on close, unlike the names: painting is where a template's content changes,
        /// and a crash between painting and closing would otherwise lose it. A drag paints at most 24 hours, so
        /// this is bounded rather than a write per frame.
        /// </summary>
        private void SetHour(int hour, TimeAssignmentDef assignment)
        {
            if (selected.schedule == null)
            {
                selected.schedule = new List<string>(PawnTemplate.ScheduleHours);

                for (int i = 0; i < PawnTemplate.ScheduleHours; i++)
                    selected.schedule.Add(string.Empty);
            }

            if (hour < 0 || hour >= selected.schedule.Count)
                return;

            selected.schedule[hour] = assignment?.defName ?? string.Empty;
            PawnTemplateStore.Save();
        }

        // ---------------------------------------------------------------------------------------
        // Policies
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// What the template's policies are, shown rather than edited.
        ///
        /// <b>Read-only on purpose, for now.</b> A policy is referenced by the name the player typed, and offering
        /// to edit one here would mean either a free text field -- where a typo becomes a policy that silently
        /// cannot be found -- or a picker listing the policies of the colony that happens to be loaded, which is not
        /// the colony the template is for. Re-capturing from a pawn who already has the right policies is both
        /// simpler and harder to get wrong. Shown in full so it is clear what applying would set.
        /// </summary>
        private float DrawPoliciesSection(Rect rect, float y, UIColorPaletteDef palette)
        {
            y = DrawSectionHeader(rect, y, "Policies", "captured from a pawn; save over this template to change "
                                                       + "them", palette);

            PawnPolicySet policies = selected.policies;

            float columnWidth = (rect.width - 28f) * 0.5f;
            float left = rect.x + 10f;
            float right = left + columnWidth + 8f;

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;

            DrawPolicyLine(new Rect(left, y, columnWidth, PolicyRowHeight), "Apparel",
                policies?.apparel, palette);
            DrawPolicyLine(new Rect(left, y + PolicyRowHeight, columnWidth, PolicyRowHeight), "Drugs",
                policies?.drug, palette);
            DrawPolicyLine(new Rect(left, y + PolicyRowHeight * 2f, columnWidth, PolicyRowHeight), "Food",
                policies?.food, palette);
            DrawPolicyLine(new Rect(left, y + PolicyRowHeight * 3f, columnWidth, PolicyRowHeight), "Reading",
                policies?.reading, palette);

            // Vanilla's own labels for these three, through the extension methods its own widgets use, so the
            // template reads the same words the assign tab does.
            string care = policies?.medicalCare == null
                ? null
                : policies.medicalCare.Value.GetLabel().CapitalizeFirst();

            string hostility = policies?.hostilityResponse == null
                ? null
                : policies.hostilityResponse.Value.GetLabel().CapitalizeFirst();

            string selfTend = policies?.selfTend == null
                ? null
                : policies.selfTend.Value ? "Yes" : "No";

            DrawPolicyLine(new Rect(right, y, columnWidth, PolicyRowHeight), "Medical care", care, palette);
            DrawPolicyLine(new Rect(right, y + PolicyRowHeight, columnWidth, PolicyRowHeight), "Hostility",
                hostility, palette);
            DrawPolicyLine(new Rect(right, y + PolicyRowHeight * 2f, columnWidth, PolicyRowHeight), "Self-tend",
                selfTend, palette);

            Text.Font = previousFont;

            return y + PolicyRowHeight * 4f + 8f;
        }

        private static void DrawPolicyLine(Rect rect, string label, string value, UIColorPaletteDef palette)
        {
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleLeft;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(rect.x, rect.y, 90f, rect.height), label);

            bool set = !value.NullOrEmpty();

            // "Not set" rather than a blank, because the two mean different things to applying: a blank looks like a
            // policy with no name, and this means the pawn keeps whatever they have.
            GUI.color = set ? palette.TextPrimary : palette.TextDisabled;
            Widgets.Label(new Rect(rect.x + 94f, rect.y, rect.width - 94f, rect.height),
                set ? value : "not set");

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
        }

        // ---------------------------------------------------------------------------------------
        // Priorities
        // ---------------------------------------------------------------------------------------

        private void DrawPrioritiesSection(Rect rect, UIColorPaletteDef palette)
        {
            float y = DrawSectionHeader(rect, rect.y, "Work priorities",
                "left click a number to raise it, right click to lower it; 0 is not assigned", palette);

            Rect inner = new Rect(rect.x + 6f, y, rect.width - 12f, rect.yMax - y);

            if (inner.height <= 0f)
                return;

            List<WorkTypeDef> works = WorkPanel.VisibleWorkTypes;
            Rect view = new Rect(0f, 0f, inner.width - 18f, works.Count * DetailRowHeight);

            Widgets.BeginScrollView(inner, ref detailScroll, view);

            float rowY = 0f;
            foreach (WorkTypeDef work in works)
            {
                DrawPriorityRow(new Rect(0f, rowY, view.width, DetailRowHeight), work, palette);
                rowY += DetailRowHeight;
            }

            Widgets.EndScrollView();
        }

        private void DrawPriorityRow(Rect row, WorkTypeDef work, UIColorPaletteDef palette)
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
                WorkPanel.LabelOf(work));

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
            PawnTemplateStore.Save();

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
                    Clear();

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

        /// <summary>
        /// Empties the parts of the template this window is scoped to, and leaves the rest alone.
        ///
        /// Scoped for the same reason applying is: clear pressed in a window about schedules must not silently wipe
        /// a whole-pawn template's work priorities.
        /// </summary>
        private void Clear()
        {
            if (Shows(PawnTemplateScope.Priorities))
            {
                foreach (WorkTypeDef work in WorkPanel.VisibleWorkTypes)
                    selected.Set(work, 0);
            }

            if (Shows(PawnTemplateScope.Schedule) && selected.schedule != null)
            {
                for (int hour = 0; hour < selected.schedule.Count; hour++)
                    selected.schedule[hour] = string.Empty;
            }

            if (Shows(PawnTemplateScope.Policies))
                selected.policies = null;

            PawnTemplateStore.Save();
        }

        // ---------------------------------------------------------------------------------------
        // Small themed widgets
        //
        // Deliberately local copies of the button and field the bar editor uses. Promoting them to the framework is
        // the right move once a third window wants them, but a control has to earn its public surface -- see the
        // Help folder's note on what belongs in UIFramework.
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
