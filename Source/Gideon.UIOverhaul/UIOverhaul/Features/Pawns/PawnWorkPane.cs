using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Pawns.Templates;
using Gideon.UIOverhaul.Features.Work;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The work priorities pane: the side panel that opens beside the pawns tab for whichever colonist was most
    /// recently expanded, with a card per work type.
    ///
    /// <b>A pane rather than columns, for the reason the architect tab has one.</b> Work priorities are two dozen
    /// values, and a grid wide enough for them is the vanilla work tab -- which stays, for players who prefer it.
    /// One pawn at a time in a tall list means each work type gets a card with room for its name, the pawn's skill
    /// at it, and the number, instead of a column head leaning over a box.
    ///
    /// <b>It follows the most recently expanded pawn.</b> Not a second selection the player has to manage: the row
    /// they just opened is the pawn they are looking at, so opening a row moves the pane and closing it puts the
    /// pane away.
    ///
    /// <b>Nothing here is cached.</b> A priority is a list index and a skill average is arithmetic over one or two
    /// levels -- both cheaper than the dictionary lookup a cache would need, and both able to change from a click
    /// in this very pane. See <see cref="PawnAttributes"/> for the rule.
    /// </summary>
    internal static class PawnWorkPane
    {
        /// <summary>
        /// How wide the pane is.
        ///
        /// Wide enough for the longest work type's gerund at the small font, its skill readout and the priority box,
        /// without the name having to truncate: "Growing" is short, but "Hauling things around" is not, and a pane
        /// that ellipsizes half its labels is a pane you cannot scan.
        /// </summary>
        internal const float PaneWidth = 330f;

        private const float Pad = 8f;
        private const float HeaderHeight = 30f;
        private const float SubtitleHeight = 18f;
        private const float ToolStripHeight = 30f;
        private const float ToolButtonSize = 26f;
        private const float ToolButtonGap = 4f;
        private const float CardHeight = 38f;
        private const float CardGap = 3f;
        private const float PriorityBoxSize = 26f;
        private const float ScrollBarWidth = 18f;

        /// <summary>Same wash alpha the work tab uses for an incapable cell, so the two read as the same state.</summary>
        private const float WarningWashAlpha = 0.10f;

        private static Vector2 scroll;

        private static readonly UICardControl Card = new UICardControl { Padding = 0f, AccentWidth = 3f };

        /// <summary>
        /// Draws the pane for one pawn.
        ///
        /// The caller owns the pane's rect and whether it is drawn at all; this draws into whatever it is handed.
        /// </summary>
        /// <returns>False when the pawn asked to close the pane, so the caller can forget them.</returns>
        internal static bool Draw(Rect pane, Pawn pawn, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(pane, palette.PanelBackground);

            Rect inner = pane.ContractedBy(Pad);

            bool stayOpen = DrawHeader(new Rect(inner.x, inner.y, inner.width, HeaderHeight), pawn, palette);

            Rect subtitle = new Rect(inner.x, inner.y + HeaderHeight, inner.width, SubtitleHeight);
            DrawSubtitle(subtitle, pawn, palette);

            Rect tools = new Rect(inner.x, subtitle.yMax + 2f, inner.width, ToolStripHeight);
            DrawTools(tools, pawn, palette);

            Rect list = new Rect(inner.x, tools.yMax + Pad, inner.width, inner.yMax - tools.yMax - Pad);
            DrawCards(list, pawn, palette);

            return stayOpen;
        }

        /// <summary>Forgets the scroll position, so a newly opened pawn starts at the top of their list.</summary>
        internal static void Reset()
        {
            scroll = Vector2.zero;
        }

        // ---------------------------------------------------------------------------------------
        // Header
        // ---------------------------------------------------------------------------------------

        /// <returns>False when the close button was pressed.</returns>
        private static bool DrawHeader(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Rect close = new Rect(rect.xMax - ToolButtonSize, rect.y + 2f, ToolButtonSize, ToolButtonSize);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(rect.x, rect.y, close.x - rect.x - 4f, rect.height),
                pawn.LabelShortCap.Truncate(close.x - rect.x - 4f));

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            GUI.color = previousColor;

            // A close button as well as clicking the row again. The row is off to the left and may have scrolled
            // out of view by the time someone wants the pane gone, and hunting for the row that opened it is not
            // an obvious way to close a panel.
            return !IconAction(close, null, "X", palette, "Close this pane");
        }

        private static void DrawSubtitle(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            // Says what the numbers mean when manual priorities are off, rather than leaving a column of
            // checkboxes to explain itself.
            Widgets.Label(rect, Find.PlaySettings.useWorkPriorities
                ? "Work priorities  --  left click raises, right click lowers"
                : "Work priorities  --  manual priorities are off, so work is only on or off");

            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        // ---------------------------------------------------------------------------------------
        // Tools
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The same five tools the work tab's row carries, scoped to priorities.
        ///
        /// <b>Shared with the work tab rather than reimplemented.</b> They go through
        /// <see cref="WorkPanel"/>'s own methods, so the clipboard is one clipboard -- copying a pawn's priorities
        /// in the work tab and pasting them here is obviously wanted, and two clipboards would have been a bug
        /// nobody reported because nobody would guess it was possible.
        ///
        /// Every template action is scoped to <see cref="PawnTemplateScope.Priorities"/>: the tools in this pane
        /// template priorities and nothing else, whatever else the template the player picks happens to carry.
        /// </summary>
        private static void DrawTools(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            const float step = ToolButtonSize + ToolButtonGap;

            float x = rect.x;
            float y = rect.y + (rect.height - ToolButtonSize) * 0.5f;

            if (IconAction(new Rect(x, y, ToolButtonSize, ToolButtonSize), WorkToolIcons.Clear, "0", palette,
                    "Clear every work priority for " + pawn.LabelShortCap))
            {
                WorkPanel.ConfirmClearPriorities(pawn);
            }

            if (IconAction(new Rect(x + step, y, ToolButtonSize, ToolButtonSize), WorkToolIcons.Copy, "C", palette,
                    "Copy " + pawn.LabelShortCap + "'s priorities"))
            {
                WorkPanel.CopyPriorities(pawn);
            }

            // Nothing to paste is a disabled button rather than a hidden one: a tool that appears once you have
            // used another tool is a tool nobody finds.
            if (IconAction(new Rect(x + step * 2f, y, ToolButtonSize, ToolButtonSize), WorkToolIcons.Paste, "P",
                    palette, WorkPanel.PasteTooltip(pawn), !WorkPanel.HasClipboard))
            {
                WorkPanel.PastePriorities(pawn);
            }

            if (IconAction(new Rect(x + step * 3f, y, ToolButtonSize, ToolButtonSize), WorkToolIcons.Save, "S",
                    palette, "Save " + pawn.LabelShortCap + "'s priorities as a template"))
            {
                PawnTemplate saved = PawnTemplateStore.CaptureFrom(pawn, PawnTemplateScope.Priorities);
                Find.WindowStack.Add(new Dialog_PawnTemplates(null, PawnTemplateScope.Priorities, saved));
            }

            if (IconAction(new Rect(x + step * 4f, y, ToolButtonSize, ToolButtonSize), WorkToolIcons.Apply, "A",
                    palette, "Apply a saved priority template to " + pawn.LabelShortCap))
            {
                Find.WindowStack.Add(new Dialog_PawnTemplates(pawn, PawnTemplateScope.Priorities));
            }
        }

        // ---------------------------------------------------------------------------------------
        // The cards
        // ---------------------------------------------------------------------------------------

        private static void DrawCards(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
            {
                DrawNoWorkNotice(rect, pawn, palette);
                return;
            }

            List<WorkTypeDef> works = WorkPanel.VisibleWorkTypes;
            Rect view = new Rect(0f, 0f, rect.width - ScrollBarWidth, works.Count * (CardHeight + CardGap));

            Widgets.BeginScrollView(rect, ref scroll, view);

            float y = 0f;
            foreach (WorkTypeDef work in works)
            {
                DrawCard(new Rect(0f, y, view.width, CardHeight), pawn, work, palette);
                y += CardHeight + CardGap;
            }

            Widgets.EndScrollView();
        }

        private static void DrawNoWorkNotice(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            GUI.color = palette.TextSecondary;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(rect.ContractedBy(10f), pawn.LabelShortCap + " cannot be given work.");

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
        }

        /// <summary>
        /// One work type: its name, the pawn's skill at it, and the priority.
        ///
        /// The card's accent stripe carries the priority's color, so the list can be read as a shape -- where the
        /// green band ends and the red one starts -- without reading two dozen numbers.
        /// </summary>
        private static void DrawCard(Rect card, Pawn pawn, WorkTypeDef work, UIColorPaletteDef palette)
        {
            bool disabled = pawn.WorkTypeIsDisabled(work);
            int priority = disabled ? 0 : pawn.workSettings.GetPriority(work);

            Card.AccentColor = disabled ? palette.Warning : WorkPanel.ColorOfPriority(priority, palette);
            Card.BackgroundColor = palette.SurfaceSunken;
            Card.DrawChrome(card, palette);

            if (disabled)
                UIElementPainter.PaintStripeWash(card, Wash(palette.Warning, WarningWashAlpha));

            Rect box = new Rect(card.xMax - PriorityBoxSize - 8f,
                card.y + (card.height - PriorityBoxSize) * 0.5f, PriorityBoxSize, PriorityBoxSize);

            float textX = card.x + Card.AccentWidth + 8f;
            float textWidth = box.x - textX - 6f;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = disabled ? palette.TextDisabled
                : priority > 0 ? palette.TextPrimary : palette.TextSecondary;

            // The work tab's own labeler, so a work type is called the same thing in both places: "Doctor", not
            // "Doctoring". The gerund stays in the tooltips below, which is where it reads as a description of the
            // activity rather than as the name of the setting being changed.
            Widgets.Label(new Rect(textX, card.y, textWidth, card.height * 0.62f),
                WorkPanel.LabelOf(work).Truncate(textWidth));

            // Asked for unconditionally, because the out parameter is only assigned by the call: reading it from
            // inside a ternary that might not have made the call is what the compiler is right to refuse.
            Color skillColor = WorkPanel.SkillColor(pawn, work, palette, out string skill);

            Text.Font = GameFont.Tiny;
            GUI.color = disabled ? palette.Warning : skillColor;

            Widgets.Label(new Rect(textX, card.y + card.height * 0.55f, textWidth, card.height * 0.42f),
                disabled ? "incapable" : skill);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (disabled)
            {
                // A dash where the number would be, matching the work tab's incapable cell.
                Widgets.DrawBoxSolid(new Rect(box.center.x - 8f, box.center.y - 1f, 16f, 2f), palette.TextDisabled);

                if (Mouse.IsOver(card))
                {
                    TooltipHandler.TipRegion(card, (TipSignal) (work.gerundLabel.CapitalizeFirst()
                        + "\n\n" + pawn.LabelShortCap + " cannot do this work."));
                }

                return;
            }

            // With manual priorities off the number means nothing -- the game treats any non-zero the same -- so a
            // box showing one would be lying about what it controls. The same substitution the work tab makes.
            if (!Find.PlaySettings.useWorkPriorities)
            {
                DrawEnabledCheckbox(box, pawn, work, priority);
                return;
            }

            DrawPriorityBox(box, card, pawn, work, priority, palette);
        }

        private static void DrawPriorityBox(Rect box, Rect card, Pawn pawn, WorkTypeDef work, int priority,
            UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(box);

            Widgets.DrawBoxSolid(box, priority == 0 ? palette.SurfaceRaised : palette.PanelBackground);

            if (over)
                Widgets.DrawBoxSolid(box, palette.HoverOverlay);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = WorkPanel.ColorOfPriority(priority, palette);
            Widgets.Label(box, priority.ToString());

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Mouse.IsOver(card))
            {
                TooltipHandler.TipRegion(card, (TipSignal) (work.gerundLabel.CapitalizeFirst()
                    + "\n\n" + work.description
                    + "\n\nLeft click raises the priority, right click lowers it."));
            }

            if (Event.current.type != EventType.MouseDown || !over)
                return;

            int step = Event.current.button == 1 ? -1 : 1;
            int next = priority + step;

            // Wraps at both ends, so a full circuit is possible from either button.
            if (next > WorkPriorityRange.Lowest)
                next = 0;
            else if (next < 0)
                next = WorkPriorityRange.Lowest;

            pawn.workSettings.SetPriority(work, next);

            // The work tab's snapshot of this pawn describes the old numbers, and would put them back the next
            // time manual priorities were switched off and on again.
            WorkPanel.ForgetRemembered(pawn);

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            Event.current.Use();
        }

        private static void DrawEnabledCheckbox(Rect box, Pawn pawn, WorkTypeDef work, int priority)
        {
            bool enabled = priority > 0;

            if (!UICheckboxControl.Draw(box, ref enabled, UIColorPaletteDef.Active))
                return;

            // The default priority rather than 1, so switching manual priorities back on leaves the pawn at the
            // middle of the range instead of at the most urgent value in it.
            pawn.workSettings.SetPriority(work, enabled ? Pawn_WorkSettings.DefaultPriority : 0);
            WorkPanel.ForgetRemembered(pawn);
        }

        private static Color Wash(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>
        /// A themed icon button, with a glyph where the art is missing and an optional disabled state.
        ///
        /// A local copy for the same reason the templates window has one: a control earns its place in the
        /// framework once a third caller wants it.
        /// </summary>
        private static bool IconAction(Rect r, Texture2D icon, string fallbackGlyph, UIColorPaletteDef palette,
            string tooltip, bool disabled = false)
        {
            TooltipHandler.TipRegion(r, (TipSignal) tooltip);

            bool over = !disabled && Mouse.IsOver(r);
            UIElementPainter.PaintButton(r, palette, over, over && Input.GetMouseButton(0));

            Color previous = GUI.color;
            GUI.color = disabled ? palette.TextDisabled : over ? palette.TextPrimary : palette.TextSecondary;

            if (icon != null)
            {
                GUI.DrawTexture(r.ContractedBy(3f), icon, ScaleMode.ScaleToFit);
            }
            else
            {
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, fallbackGlyph);
                Text.Anchor = previousAnchor;
            }

            GUI.color = previous;

            return !disabled && Widgets.ButtonInvisible(r);
        }
    }
}
