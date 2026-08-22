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
    /// A colonist's work priorities as a grid of cards, drawn inside their opened row.
    ///
    /// <b>This was a side pane until 2026-08-22, and the pane is gone.</b> Approved from a mockup: the same cards,
    /// reflowed from one column of twenty-four into six columns of four, and drawn under the row that opened
    /// rather than beside the whole table. What that buys is the 330 pixels the pane was reserving from every
    /// other row, and the resize machinery that came with it: the window no longer watches a width and re-places
    /// itself when a pane opens, and a tab dragged narrow no longer has its columns squeezed by a panel.
    ///
    /// <b>The card is unchanged, and that was the point of choosing this shape.</b> A work type still gets its
    /// name, the pawn's skill at it and the number, with the priority's colour on the accent stripe so a block of
    /// green or red is legible without reading two dozen figures. The two rejected shapes both gave that up: tabs
    /// hid two thirds of the row behind a click, and a strip of rotated labels is the vanilla work tab again.
    ///
    /// <b>No scroll view.</b> The grid is as tall as it needs to be and the row grows to fit it, which is what
    /// makes this simpler than the pane rather than merely different: nothing here has a scroll position to
    /// remember, reset when the pawn changes, or restore when the row reopens.
    ///
    /// <b>The work tab still exists and still wins for comparison.</b> This is the one pawn view. Reading eight
    /// colonists across twenty work types is a grid of its own, and nothing here replaces it.
    /// </summary>
    internal static class PawnWorkGrid
    {
        private const float CardHeight = 38f;
        private const float CardGap = 4f;

        /// <summary>
        /// Narrowest a card may be before a column is dropped.
        ///
        /// Sized for the longest work label at the small font plus its priority box: below this the names start
        /// truncating, and a grid of ellipsised words is the thing the card design exists to avoid.
        /// </summary>
        private const float MinCardWidth = 158f;

        /// <summary>Most columns worth having. Past this the cards are wide and the grid is one row of stubs.</summary>
        private const int MaxColumns = 8;

        private const float HeaderHeight = 24f;
        private const float PriorityBoxSize = 26f;
        private const float ToolButtonSize = 22f;
        private const float ToolButtonGap = 4f;

        /// <summary>Same wash alpha the work tab uses for an incapable cell, so the two read as the same state.</summary>
        private const float WarningWashAlpha = 0.10f;

        private static readonly UICardControl Card = new UICardControl { Padding = 0f, AccentWidth = 3f };

        /// <summary>How many columns fit, which decides the height as well as the layout.</summary>
        private static int ColumnsIn(float width)
        {
            return Mathf.Clamp(Mathf.FloorToInt((width + CardGap) / (MinCardWidth + CardGap)), 1, MaxColumns);
        }

        /// <summary>
        /// How tall the whole band is for this pawn at this width.
        ///
        /// Zero for a pawn who can never work, so a mech or a baby's row does not open onto a heading over
        /// nothing. The notice that says so is worth one line, and that is what the pawn gets instead.
        /// </summary>
        internal static float HeightFor(Pawn pawn, float width)
        {
            if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
                return pawn == null ? 0f : HeaderHeight + UIFonts.LineHeightOf(GameFont.Small) + 6f;

            List<WorkTypeDef> works = WorkPanel.VisibleWorkTypes;

            if (works == null || works.Count == 0)
                return 0f;

            int columns = ColumnsIn(width);
            int rows = Mathf.CeilToInt(works.Count / (float) columns);

            return HeaderHeight + rows * (CardHeight + CardGap);
        }

        internal static void Draw(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn == null)
                return;

            Header(new Rect(rect.x, rect.y, rect.width, HeaderHeight), pawn, palette);

            Rect body = new Rect(rect.x, rect.y + HeaderHeight, rect.width,
                Mathf.Max(0f, rect.height - HeaderHeight));

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
            {
                Notice(body, pawn, palette);

                return;
            }

            List<WorkTypeDef> works = WorkPanel.VisibleWorkTypes;

            if (works == null)
                return;

            int columns = ColumnsIn(body.width);
            float card = (body.width - CardGap * (columns - 1)) / columns;

            for (int i = 0; i < works.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;

                Rect at = new Rect(body.x + column * (card + CardGap), body.y + row * (CardHeight + CardGap),
                    card, CardHeight);

                DrawCard(at, pawn, works[i], palette);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Header
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The band's caption, and the five priority tools on the right.
        ///
        /// The caption says what the numbers mean when manual priorities are off, rather than leaving a grid of
        /// checkboxes to explain itself. The tools sit in the heading because they act on this band and not on the
        /// pawn: a player who has just pasted priorities should see where they landed.
        /// </summary>
        private static void Header(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextDisabled;

                float tools = PawnTools.WidthFor(PawnTemplateScope.Priorities);

                Widgets.LabelEllipses(new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - tools - 12f),
                        rect.height),
                    Find.PlaySettings.useWorkPriorities
                        ? "WORK PRIORITIES   left click raises, right click lowers"
                        : "WORK   manual priorities are off, so work is only on or off");

                // The row itself is PawnTools, shared with the schedule band, the policy band and the pawn row's
                // own column. Scoped to Priorities here, so it templates priorities and nothing else whatever
                // the template the player picks happens to carry.
                PawnTools.Row(new Rect(rect.xMax - tools, rect.y, tools, rect.height), pawn,
                    PawnTemplateScope.Priorities, palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }


        private static void Notice(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            GUI.color = palette.TextSecondary;
            Text.Anchor = TextAnchor.UpperLeft;

            Widgets.Label(rect, pawn.LabelShortCap + " cannot be given work.");

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
        }

        // ---------------------------------------------------------------------------------------
        // The card
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// One work type: its name, the pawn's skill at it, and the priority.
        ///
        /// The card's accent stripe carries the priority's colour, so the grid can be read as a shape rather than
        /// as two dozen numbers.
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
            // "Doctoring". The gerund stays in the tooltips below, where it reads as a description of the activity
            // rather than as the name of the setting being changed.
            Widgets.Label(new Rect(textX, card.y, textWidth, card.height * 0.62f),
                WorkPanel.LabelOf(work).Truncate(textWidth));

            // Asked for unconditionally, because the out parameter is only assigned by the call: reading it from
            // inside a ternary that might not have made the call is what the compiler is right to refuse.
            string skill;
            Color skillColor = WorkPanel.SkillColor(pawn, work, palette, out skill);

            Text.Font = GameFont.Tiny;
            GUI.color = disabled ? palette.Warning : skillColor;

            // Height taken from the top of the line to the bottom of the card rather than as a fraction of it: a
            // fraction fits Tiny and clips Small, and this draw gets Small whenever TinyFontSupported is false.
            float subtitleTop = card.y + card.height * 0.55f;

            Widgets.Label(
                new Rect(textX, subtitleTop, textWidth,
                    Mathf.Max(UIFonts.LineHeightOf(GameFont.Tiny), card.yMax - subtitleTop)),
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

            // With manual priorities off the number means nothing, since the game treats any non-zero the same, so
            // a box showing one would be lying about what it controls. The same substitution the work tab makes.
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

    }
}
