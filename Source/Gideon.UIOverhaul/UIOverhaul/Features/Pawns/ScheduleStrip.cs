using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The 24 hour schedule strip and the brush that paints it, drawn the same way wherever a day is edited.
    ///
    /// <b>Extracted rather than copied, for the reason the shared portrait was.</b> Two places edit a day now --
    /// a pawn's row in the pawns tab, and a schedule template in the manager window -- and a second copy of this
    /// would be two strips that could drift apart in how a drag paints, what the tooltip says, and which hour is
    /// outlined. The only thing that differs between the two is where the hours are read from and written to, so
    /// that is the only thing passed in.
    ///
    /// <b>The brush is deliberately global.</b> It is a tool the player picks up rather than a property of what
    /// they are painting, so choosing Sleep on a pawn's row and then opening a template finds Sleep still in hand.
    /// </summary>
    internal static class ScheduleStrip
    {
        /// <summary>Hours in a day. The strip is always this wide.</summary>
        internal const int Hours = 24;

        /// <summary>Height the strip's cells want, and the width the brush picker wants.</summary>
        internal const float CellHeight = 24f;

        internal const float BrushWidth = 120f;

        private static TimeAssignmentDef brush;

        internal static TimeAssignmentDef Brush => brush ?? (brush = TimeAssignmentDefOf.Work);

        /// <summary>
        /// The brush picker: which assignment a click paints.
        ///
        /// Every entry carries its own color swatch, in the def's own color, so the menu and the strip cannot
        /// disagree about what a color means -- and so the player picks a color rather than reading a word.
        ///
        /// Picking from it sets the brush rather than writing anything; clicking an hour is what writes. That
        /// separation is what makes painting a block of hours one choice and several clicks instead of a choice
        /// per click.
        /// </summary>
        internal static void DrawBrushPicker(Rect rect, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));

            Rect swatch = new Rect(rect.x + 5f, rect.y + 5f, 14f, 14f);
            Widgets.DrawBoxSolid(swatch, Brush.color);

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;

            GUI.color = palette.Border;
            Widgets.DrawBox(swatch, 1);

            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(swatch.xMax + 6f, rect.y, rect.width - 30f, rect.height), Brush.LabelCap);

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            GUI.color = previousColor;

            if (!Widgets.ButtonInvisible(rect))
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (TimeAssignmentDef def in Assignments)
            {
                TimeAssignmentDef captured = def;

                // The icon constructor rather than extraPartOnGUI. extraPartOnGUI draws to the right of the
                // label, which left the swatches ragged along the ends of variable-length words; iconTex is
                // drawn at the left and defaults to iconJustification = Left, so they line up in a column.
                //
                // A white 1x1 tinted by the def's color, rather than the def's own ColorTexture: that property
                // builds and caches a texture per def, and asking for it here would pin one for every
                // assignment a mod ever adds when a tint of the shared white does the same job.
                options.Add(new FloatMenuOption(captured.LabelCap, () => brush = captured,
                    BaseContent.WhiteTex, captured.color));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Every loaded assignment, not the five in the DefOf.
        ///
        /// Mods do add assignment types, and a picker that could not offer them would make this tab unable to set
        /// a schedule the vanilla tab can. Read from the database in load order, which is the order vanilla's own
        /// schedule tab uses, so the two read the same.
        /// </summary>
        internal static List<TimeAssignmentDef> Assignments =>
            DefDatabase<TimeAssignmentDef>.AllDefsListForReading;

        /// <summary>
        /// The 24 hour cells.
        ///
        /// Dragging paints, not just clicking: setting eight hours of sleep is one gesture rather than eight
        /// clicks. Held-button painting is why this reads the mouse state directly instead of using
        /// ButtonInvisible -- a button reports a completed click, and a drag never completes one per cell.
        /// </summary>
        /// <param name="read">
        /// This hour's assignment, or null where the strip has nothing to show. A template can hold an hour it has
        /// no opinion about, which draws as an empty cell rather than as a guess.
        /// </param>
        /// <param name="write">Called for a cell being painted, with the brush. Null draws a read-only strip.</param>
        /// <param name="currentHour">
        /// Outlined so the strip can be read against the clock without counting cells. Negative where there is no
        /// clock to speak of, which is the case for a template: it describes any day, not today.
        /// </param>
        internal static void DrawHours(Rect rect, UIColorPaletteDef palette,
            Func<int, TimeAssignmentDef> read, Action<int, TimeAssignmentDef> write, int currentHour)
        {
            if (read == null)
                return;

            float cellWidth = rect.width / Hours;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            for (int hour = 0; hour < Hours; hour++)
            {
                Rect cell = new Rect(rect.x + hour * cellWidth, rect.y, cellWidth, rect.height);

                TimeAssignmentDef assignment = read(hour);

                // An hour with no assignment is drawn as a hole rather than as any assignment's color, so "this
                // template says nothing about 3am" cannot be mistaken for "this template wants Anything at 3am".
                Widgets.DrawBoxSolid(cell.ContractedBy(0.5f),
                    assignment != null ? assignment.color : palette.SurfaceSunken);

                bool over = Mouse.IsOver(cell);

                if (over)
                    Widgets.DrawBoxSolid(cell.ContractedBy(0.5f), palette.HoverOverlay);

                GUI.color = hour == currentHour ? palette.Accent : palette.Border;
                Widgets.DrawBox(cell, hour == currentHour ? 2 : 1);

                GUI.color = palette.TextPrimary;
                Widgets.Label(cell, hour.ToString());

                // Mouse state rather than a click: this is what lets a drag paint a run of hours.
                if (write != null && over && Input.GetMouseButton(0) && assignment != Brush)
                {
                    write(hour, Brush);
                    SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera();
                }

                if (over)
                    TooltipHandler.TipRegion(cell, (TipSignal) Tooltip(hour, assignment, write != null));
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        private static string Tooltip(int hour, TimeAssignmentDef assignment, bool editable)
        {
            string text = hour + ":00 -- " + (assignment != null ? assignment.LabelCap.ToString() : "unset");

            if (editable)
                text += "\n\nClick or drag to set " + Brush.LabelCap + ".";

            return text;
        }
    }
}
