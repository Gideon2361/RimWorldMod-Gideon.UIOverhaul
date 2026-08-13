using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// Collects problems found while reading a config or palette file and puts them in front of the
    /// player.
    ///
    /// The log is the wrong place for this on its own. Someone editing a theme by hand is watching the
    /// game, not the log, and a silently discarded file looks like the edit did nothing -- which sends
    /// them looking for the bug in their colors rather than in their typo.
    /// </summary>
    public static class UIConfigProblems
    {
        /// <summary>
        /// Reports a file that was rejected. Everything it contained has already been discarded by the
        /// caller; this only explains why.
        /// </summary>
        public static void Report(string file, List<string> problems)
        {
            if (problems == null || problems.Count == 0)
                return;

            // The log still gets it, so a bug report includes the detail even if the window was dismissed.
            Log.Warning($"[Gideon.UIOverhaul] Discarded {file}:\n  " + string.Join("\n  ", problems));

            if (Find.WindowStack == null)
                return;

            Dialog_UIConfigProblems existing = Find.WindowStack.WindowOfType<Dialog_UIConfigProblems>();
            if (existing != null)
            {
                existing.Add(file, problems);
                return;
            }

            Dialog_UIConfigProblems dialog = new Dialog_UIConfigProblems();
            dialog.Add(file, problems);
            Find.WindowStack.Add(dialog);
        }
    }

    /// <summary>
    /// Names the files that were rejected and what was wrong with each.
    ///
    /// One window that accumulates, rather than one per file: saving a file often raises more than one
    /// change, and several bad files at startup would otherwise stack windows on top of each other.
    /// </summary>
    public class Dialog_UIConfigProblems : Window
    {
        private readonly List<string> lines = new List<string>();
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(640f, 420f);

        public Dialog_UIConfigProblems()
        {
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public void Add(string file, List<string> problems)
        {
            if (lines.Count > 0)
                lines.Add("");

            lines.Add(file);
            foreach (string problem in problems)
                lines.Add("    " + problem);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Options.ProblemReport", inRect, () => DrawContents(inRect));
        }

        private void DrawContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            // Leaves room for the close button Window draws along the bottom.
            Rect body = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 45f);

            Text.Font = GameFont.Medium;
            GUI.color = palette.Warning;
            Rect title = new Rect(body.x, body.y, body.width, 32f);
            Widgets.Label(title, "A UI configuration file was not applied");

            Text.Font = GameFont.Small;
            GUI.color = palette.TextSecondary;
            Rect intro = new Rect(body.x, title.yMax + 2f, body.width, 42f);
            Widgets.Label(intro, "The problems below were found while reading these files. Nothing from a "
                                 + "listed file was applied, so the game is still using the last values "
                                 + "that read cleanly. Fix the file and save it again.");

            Rect listRect = new Rect(body.x, intro.yMax + 6f, body.width, body.yMax - intro.yMax - 6f);
            Widgets.DrawBoxSolid(listRect, palette.SurfaceSunken);

            float lineHeight = Text.LineHeightOf(GameFont.Small);
            Rect view = new Rect(0f, 0f, listRect.width - 20f, lines.Count * lineHeight + 8f);

            Widgets.BeginScrollView(listRect, ref scroll, view);

            float y = 4f;
            foreach (string line in lines)
            {
                // A file name sits flush left, its problems are indented. Coloring on that distinction
                // keeps the list readable without a second widget.
                GUI.color = line.StartsWith("    ") ? palette.TextSecondary : palette.TextPrimary;
                Widgets.Label(new Rect(6f, y, view.width - 12f, lineHeight), line);
                y += lineHeight;
            }

            Widgets.EndScrollView();

            GUI.color = previousColor;
            Text.Font = previousFont;
        }
    }
}
