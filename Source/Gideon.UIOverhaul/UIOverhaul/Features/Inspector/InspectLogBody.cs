using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The Log body: one stream of what has happened to this pawn, filtered by chip.
    ///
    /// <b>Merged rather than split, which is the decision Aaron made when he approved the mockup.</b> "What
    /// happened to this pawn" is one question, and vanilla answers it with two checkboxes over one list that is
    /// already interleaved. The chips are the same two switches said as an answer instead of as a pair of
    /// options: All is both, Combat is one, Social is the other.
    ///
    /// <b>The lines themselves are RimWorld's own.</b> <c>ITab_Pawn_Log_Utility.GenerateLogLinesFor</c> is what
    /// builds them, including the day headers, the icons and the highlight when a log entry is being sought, and
    /// each one draws itself. Rewriting that would mean reimplementing every <c>LogEntry</c> subclass in the game
    /// plus whatever mods have added, to gain nothing but our own font.
    ///
    /// <b>There is no Medical chip,</b> which the mockup drew. RimWorld's log has exactly two categories and
    /// tending is not one of them: a Medical chip would either be empty or would filter on the wording of the
    /// text, which stops working the moment the game is played in another language.
    /// </summary>
    internal static class InspectLogBody
    {
        /// <summary>How many entries are asked for. Vanilla's own tab asks for three hundred.</summary>
        private const int MaxLines = 200;

        /// <summary>Drawing state RimWorld's log lines keep between each other, for the alternating rows.</summary>
        private static readonly ITab_Pawn_Log_Utility.LogDrawData DrawData =
            new ITab_Pawn_Log_Utility.LogDrawData();

        internal static float Draw(Rect view, Pawn pawn, UIColorPaletteDef palette)
        {
            float y = Filters(view, view.y, palette);

            bool combat = InspectPaneState.Log != InspectPaneState.LogFilter.Social;
            bool social = InspectPaneState.Log != InspectPaneState.LogFilter.Combat;

            y = Stream(view, y, pawn, combat, social, MaxLines, palette);

            return y - view.y;
        }

        /// <summary>The three chips, laid across the top of the body.</summary>
        private static float Filters(Rect view, float y, UIColorPaletteDef palette)
        {
            float x = view.x;
            float height = 0f;

            height = Mathf.Max(height, Chip(view, ref x, y, InspectPaneState.LogFilter.All, "All", palette));
            height = Mathf.Max(height, Chip(view, ref x, y, InspectPaneState.LogFilter.Combat, "Combat", palette));
            height = Mathf.Max(height, Chip(view, ref x, y, InspectPaneState.LogFilter.Social, "Social", palette));

            return y + height + 8f;
        }

        private static float Chip(Rect view, ref float x, float y, InspectPaneState.LogFilter filter, string label,
            UIColorPaletteDef palette)
        {
            bool selected = InspectPaneState.Log == filter;

            Rect chip = InspectPaneParts.Tag(view, x, y, label,
                selected ? palette.Accent : palette.Border, selected, palette);

            if (Widgets.ButtonInvisible(chip))
                InspectPaneState.Log = filter;

            x = chip.xMax + 5f;

            return chip.height;
        }

        /// <summary>
        /// RimWorld's own log lines for this pawn, drawn into our column.
        ///
        /// Shared with the Social body, which asks for the social half of the same stream rather than keeping a
        /// second reader of the play log.
        ///
        /// <b>The group is what makes this work.</b> A <c>LogLineDisplayable</c> draws itself at x zero of
        /// whatever GUI space it is in, so it is given one whose origin is this column's left edge.
        /// </summary>
        internal static float Stream(Rect view, float y, Pawn pawn, bool combat, bool social, int max,
            UIColorPaletteDef palette)
        {
            List<ITab_Pawn_Log_Utility.LogLineDisplayable> lines = UIGuard.Try("Inspector.LogLines",
                () => ITab_Pawn_Log_Utility.GenerateLogLinesFor(pawn, false, combat, social, max), null,
                "The inspect pane cannot show this pawn's log.");

            if (lines == null || lines.Count == 0)
                return InspectPaneParts.Note(view, y, "Nothing recent.", palette);

            float total = 0f;

            for (int i = 0; i < lines.Count; i++)
                total += Height(lines[i], view.width);

            Widgets.BeginGroup(new Rect(view.x, y, view.width, total));

            try
            {
                DrawData.StartNewDraw();

                float at = 0f;

                for (int i = 0; i < lines.Count; i++)
                {
                    ITab_Pawn_Log_Utility.LogLineDisplayable line = lines[i];

                    float height = Height(line, view.width);

                    // Each line individually, because one of them is a log entry from whichever mod produced it
                    // and a throw in the middle of the list should cost that line rather than the rest of them.
                    float at1 = at;

                    UIGuard.Try("Inspector.LogLine", () => line.Draw(at1, view.width, DrawData), null);

                    at += height;
                }
            }
            finally
            {
                Widgets.EndGroup();
            }

            return y + total;
        }

        /// <summary>A line's height, guarded, since it is measured by the same arbitrary code that draws it.</summary>
        private static float Height(ITab_Pawn_Log_Utility.LogLineDisplayable line, float width)
        {
            return UIGuard.Try("Inspector.LogHeight", () => line.GetHeight(width), 0f, null);
        }
    }
}
