using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>
    /// The bug hunt's results, as a card per finding.
    ///
    /// <b>Cards rather than rows, because a finding is not one fact.</b> Every one of these carries five things
    /// somebody needs at once -- how bad it is, which definition, which field, what the parser said, and which
    /// file to open -- and a table wide enough to hold all five puts the file path in a column too narrow to
    /// read and the message in one too narrow to be a message. A card gives the message the width of the panel
    /// and lets its height be whatever the message needs.
    ///
    /// <b>Sorted by file, not by severity.</b> The unit of work is a file: somebody opens one and fixes
    /// everything wrong in it. Ordering by severity would scatter four faults in the same file across a list
    /// and turn one edit into four searches.
    ///
    /// <b>Heights are measured once per width.</b> <c>Text.CalcHeight</c> walks the string to find where it
    /// wraps, and with a couple of thousand cards on screen that is not something to do per frame. See the same
    /// note on the patch simulator's report, which is where this was learned the expensive way.
    /// </summary>
    internal static class BugHuntPanel
    {
        private const float Padding = 8f;
        private const float Gap = 3f;
        private const float BarHeight = 28f;

        /// <summary>Width kept clear on the right for the card's own buttons.</summary>
        private const float ButtonStrip = 132f;

        /// <summary>
        /// The narrowest rect worth handing to <see cref="Ellipsis"/>.
        ///
        /// <c>Widgets.LabelEllipses</c> works out how many characters fit by subtracting the width of "..." and
        /// dividing, then takes that many with <c>Substring</c>. Give it less room than the ellipsis itself
        /// needs and the count comes out negative, and the exception is thrown from inside vanilla where it
        /// looks like a fault in RimWorld. This window is resizable and its cards carry several labels sharing
        /// one row, so a narrow enough drag reaches that.
        /// </summary>
        private const float MinLabelWidth = 24f;

        private static readonly UITextBoxControl Filter = new UITextBoxControl
        {
            Placeholder = "Filter by mod, file, def or message"
        };

        private static Vector2 scroll;

        /// <summary>Indices into the hunt's findings, in display order, after filtering.</summary>
        private static readonly List<int> shown = new List<int>();

        private static readonly List<float> heights = new List<float>();
        private static readonly List<float> tops = new List<float>();

        private static float builtWidth = -1f;
        private static string builtFilter;
        private static int builtCount = -1;
        private static float contentHeight;
        private static int files;

        private static readonly UICardControl Card = new UICardControl
        {
            HoverHighlight = false,
            AccentWidth = 3f
        };

        /// <summary>Throws away the cached layout, so the next draw rebuilds it.</summary>
        internal static void Invalidate()
        {
            builtCount = -1;
            builtWidth = -1f;
            scroll = Vector2.zero;
        }

        internal static void Draw(Rect rect, UIColorPaletteDef palette, Action run, Action<BugFinding> reveal)
        {
            Rect bar = new Rect(rect.x, rect.y, rect.width, BarHeight);
            DrawBar(bar, palette, run);

            Rect body = new Rect(rect.x, bar.yMax + 6f, rect.width,
                Mathf.Max(0f, rect.yMax - bar.yMax - 6f));

            UIElementPainter.OutlineRounded(body, palette.Border, palette.PanelBackground);

            Rect inner = body.ContractedBy(6f);

            if (Empty(inner, palette))
                return;

            Rebuild(inner.width);

            Rect view = new Rect(0f, 0f, inner.width - 18f, contentHeight);

            Widgets.BeginScrollView(inner, ref scroll, view);

            try
            {
                for (int i = 0; i < shown.Count; i++)
                {
                    float top = tops[i];

                    // Only what is on screen is drawn. Everything above and below still occupies its height,
                    // so the scroll bar and the positions are unaffected.
                    if (top + heights[i] < scroll.y || top > scroll.y + inner.height)
                        continue;

                    DrawCard(new Rect(0f, top, view.width, heights[i]), XmlBugHunt.Findings[shown[i]], palette,
                        reveal);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// The controls, and what the run found in one line.
        /// </summary>
        private static void DrawBar(Rect rect, UIColorPaletteDef palette, Action run)
        {
            bool ready = XmlWorkbench.State == XmlWorkbenchState.Ready && !XmlBugHunt.Running;
            bool ran = XmlBugHunt.State != XmlBugHuntState.Idle;

            Rect start = new Rect(rect.x, rect.y, 150f, rect.height);

            if (ready && Button(start, ran ? "Run again" : "Run bug hunt", palette))
            {
                run();

                return;
            }

            if (!ready)
                Disabled(start, ran ? "Run again" : "Run bug hunt", palette);

            Rect box = new Rect(start.xMax + 8f, rect.y, Mathf.Min(320f, rect.width * 0.3f), rect.height);
            Filter.Draw(box, palette);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;

            Rect summary = new Rect(box.xMax + 10f, rect.y, Mathf.Max(0f, rect.xMax - box.xMax - 10f),
                rect.height);

            Ellipsis(summary, Summary());

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        private static string Summary()
        {
            if (XmlBugHunt.State == XmlBugHuntState.Idle)
                return "Reads every definition through RimWorld's own parser.";

            int found = XmlBugHunt.Findings.Count;

            if (found == 0)
                return "Nothing wrong in " + XmlBugHunt.Total + " definitions.";

            string text = found + " problems in " + XmlBugHunt.BrokenDefs + " definitions, across " + files
                          + " files";

            if (XmlBugHunt.Suppressed > 0)
                text += "  (" + XmlBugHunt.Suppressed + " more not listed)";

            return text;
        }

        /// <summary>
        /// What fills the panel when there are no cards, which is most of its life.
        /// </summary>
        /// <returns>True when it drew, meaning the list should not.</returns>
        private static bool Empty(Rect rect, UIColorPaletteDef palette)
        {
            string message = null;
            Color color = palette.TextDisabled;

            // Nothing is listed while the scan is running, and that is a performance decision as much as a
            // presentational one. The findings grow every frame, so a list drawn from them would remeasure
            // every card's wrapped height every frame, on a panel nobody can reach because the progress window
            // is modal over it. The counts are on that window, where they can be read.
            if (XmlBugHunt.Running)
            {
                message = "Scanning: " + XmlBugHunt.Done + " of " + XmlBugHunt.Total + " definitions.";
            }
            else
            {
                switch (XmlBugHunt.State)
                {
                    case XmlBugHuntState.Idle:
                        message = "Press Run bug hunt.\n\nEvery definition in the current scope is read again "
                                  + "through RimWorld's own deserializer, and everything it complains about is "
                                  + "listed here with the file, the def and the field it came from.\n\nThis "
                                  + "reads the files as they ship, before patch operations run, so it can miss "
                                  + "a fault a patch introduces. It does not invent ones that are not there.";
                        break;

                    case XmlBugHuntState.Failed:
                        message = XmlBugHunt.Failure ?? "The scan could not run.";
                        color = palette.Danger;
                        break;

                    case XmlBugHuntState.Cancelled:
                        if (XmlBugHunt.Findings.Count == 0)
                            message = "Cancelled before anything was found.";

                        break;

                    case XmlBugHuntState.Finished:
                        if (XmlBugHunt.Findings.Count == 0)
                        {
                            message = "Nothing wrong. All " + XmlBugHunt.Total
                                      + " definitions in this scope were read without a single complaint from "
                                      + "the parser.";
                            color = palette.Success;
                        }

                        break;
                }
            }

            if (message == null)
                return false;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                GUI.color = color;

                Widgets.Label(rect.ContractedBy(10f), message);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            return true;
        }

        /// <summary>
        /// Works out which findings are shown, in what order, and how tall each card is.
        ///
        /// Runs only when the width, the filter or the number of findings has changed, which during a scan is
        /// once per slice and afterwards is once.
        /// </summary>
        private static void Rebuild(float width)
        {
            string filter = Filter.Text ?? string.Empty;

            if (Mathf.Approximately(builtWidth, width) && builtCount == XmlBugHunt.Findings.Count
                                                       && filter == builtFilter)
                return;

            builtWidth = width;
            builtCount = XmlBugHunt.Findings.Count;
            builtFilter = filter;

            shown.Clear();
            heights.Clear();
            tops.Clear();

            List<BugFinding> all = XmlBugHunt.Findings;
            string needle = filter.Trim().ToLower();

            for (int i = 0; i < all.Count; i++)
            {
                if (needle.Length == 0 || Matches(all[i], needle))
                    shown.Add(i);
            }

            // By file, then by definition within it. See the note on this class: the unit of repair is a file.
            shown.Sort((a, b) => Order(all[a], all[b]));

            HashSet<string> distinct = new HashSet<string>();

            foreach (BugFinding finding in all)
                distinct.Add(finding.Path ?? string.Empty);

            files = distinct.Count;

            float y = 0f;
            float text = width - 18f - Padding * 2f - 3f;

            GameFont previousFont = Text.Font;
            bool previousWrap = Text.WordWrap;

            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;

            try
            {
                float small = UIFonts.LineHeightOf(GameFont.Small);
                float tiny = UIFonts.LineHeightOf(GameFont.Tiny);

                foreach (int i in shown)
                {
                    BugFinding finding = all[i];

                    float height = Padding * 2f + small + Gap + tiny;

                    if (!finding.Snippet.NullOrEmpty())
                        height += tiny + 4f + Gap;

                    height += Mathf.Max(tiny,
                        Text.CalcHeight(finding.Message ?? string.Empty, Mathf.Max(40f, text))) + Gap;

                    heights.Add(height);
                    tops.Add(y);

                    y += height + 6f;
                }
            }
            finally
            {
                Text.WordWrap = previousWrap;
                Text.Font = previousFont;
            }

            contentHeight = y;
        }

        private static int Order(BugFinding a, BugFinding b)
        {
            int by = string.Compare(a.Mod ?? string.Empty, b.Mod ?? string.Empty, StringComparison.Ordinal);

            if (by != 0)
                return by;

            by = string.Compare(a.Path ?? string.Empty, b.Path ?? string.Empty, StringComparison.Ordinal);

            return by != 0
                ? by
                : string.Compare(a.DefName ?? string.Empty, b.DefName ?? string.Empty, StringComparison.Ordinal);
        }

        private static bool Matches(BugFinding finding, string needle)
        {
            return Has(finding.Mod, needle) || Has(finding.Path, needle) || Has(finding.DefName, needle)
                   || Has(finding.DefType, needle) || Has(finding.Field, needle)
                   || Has(finding.Message, needle);
        }

        private static bool Has(string text, string needle)
        {
            return !text.NullOrEmpty() && text.ToLower().Contains(needle);
        }

        private static void DrawCard(Rect rect, BugFinding finding, UIColorPaletteDef palette,
            Action<BugFinding> reveal)
        {
            Color severity = finding.Error ? palette.Danger : palette.Warning;

            Card.AccentColor = severity;
            Card.BackgroundColor = palette.SurfaceRaised;
            Card.BorderColor = palette.Border;
            Card.Padding = Padding;

            // Chrome only. The card carries buttons of its own, and Draw finishes with ButtonInvisible, which
            // eats the click before anything inside it is asked.
            Card.DrawChrome(rect, palette);

            Rect content = Card.ContentRect(rect);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleLeft;

                float small = UIFonts.LineHeightOf(GameFont.Small);
                float tiny = UIFonts.LineHeightOf(GameFont.Tiny);

                Rect head = new Rect(content.x, content.y, content.width, small);
                float x = head.x;

                // The shared badge, so a fault flagged here and the same fault flagged in the loading console
                // are the same object rather than two panels agreeing by hand.
                Text.Font = GameFont.Tiny;

                string label = finding.Fatal ? "FATAL" : finding.Error ? "ERROR" : "WARN";

                x = UITagControl.DrawLeading(new Rect(x, head.y, head.width, small), label, severity, palette,
                    8f);

                Text.Anchor = TextAnchor.MiddleLeft;

                // The field, right aligned, so the eye can run down one column of field names rather than hunt
                // for them at the end of names of different lengths.
                float fieldWidth = 0f;

                if (!finding.Field.NullOrEmpty())
                {
                    // <b>The slack is not padding, it is a requirement.</b> LabelEllipses goes through
                    // Text.ClampTextWithEllipsis, which refuses to draw text unless it fits in the rect with
                    // 13 pixels to spare, and truncates it otherwise. Sizing this box to the measured text
                    // plus ten was three pixels short of that, so every field name came out clipped no matter
                    // how much room the card had. Sized past the reserve, and allowed a good deal more of the
                    // row: field names in this game run to things like acceptableMeltTemperatureThreshold.
                    fieldWidth = Mathf.Min(head.width * 0.45f, Text.CalcSize(finding.Field).x + 18f);

                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = palette.Accent;

                    Ellipsis(new Rect(head.xMax - fieldWidth, head.y, fieldWidth, head.height), finding.Field);

                    Text.Anchor = TextAnchor.MiddleLeft;
                }

                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;

                string title = finding.DefName.NullOrEmpty()
                    ? finding.DefType ?? "(unnamed)"
                    : finding.DefType + "  " + finding.DefName;

                Ellipsis(new Rect(x, head.y, Mathf.Max(0f, head.xMax - x - fieldWidth - 8f), head.height), title);

                Text.Font = GameFont.Tiny;

                float y = head.yMax + Gap;

                if (!finding.Snippet.NullOrEmpty())
                {
                    // The offending XML on its own strip. This is the line to go and change, and pulling it out
                    // of the middle of a paragraph is most of what makes a finding actionable at a glance.
                    Rect strip = new Rect(content.x, y, content.width, tiny + 4f);

                    Widgets.DrawBoxSolid(strip, palette.SurfaceSunken);

                    GUI.color = palette.TextPrimary;
                    Ellipsis(new Rect(strip.x + 5f, strip.y, strip.width - 10f, strip.height),
                        finding.Snippet.Replace("`n", " "));

                    y = strip.yMax + Gap;
                }

                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextSecondary;

                float messageHeight = Mathf.Max(0f, content.yMax - y - tiny - Gap);

                Widgets.Label(new Rect(content.x, y, content.width, messageHeight),
                    finding.Message ?? string.Empty);

                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextDisabled;

                Rect foot = new Rect(content.x, content.yMax - tiny, content.width, tiny);

                Ellipsis(new Rect(foot.x, foot.y, Mathf.Max(0f, foot.width - ButtonStrip), tiny),
                    (finding.Mod.NullOrEmpty() ? string.Empty : finding.Mod + "  -  ")
                    + (finding.Path ?? "unknown file"));

                if (!finding.Path.NullOrEmpty())
                    TooltipHandler.TipRegion(foot, (TipSignal) finding.Path);

                Rect find = new Rect(foot.xMax - 62f, foot.y - 2f, 62f, tiny + 4f);
                Rect copy = new Rect(find.x - 66f, foot.y - 2f, 62f, tiny + 4f);

                if (Button(copy, "Copy", palette))
                {
                    UIGuard.Try("Diagnostics.CopyBugFinding",
                        () => { GUIUtility.systemCopyBuffer = Report(finding); }, null);

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                // Only offered when there is something to select on. An xpath needs a defName to key off, and a
                // button that silently did nothing would be worse than one that is not there.
                if (!finding.DefName.NullOrEmpty() && !finding.DefType.NullOrEmpty()
                                                   && Button(find, "Find", palette))
                {
                    reveal(finding);

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// A finding as text, for pasting somewhere it can be acted on.
        ///
        /// The full message rather than the shortened one, because the thing that was cut for the card -- the
        /// surrounding definition -- is exactly what somebody reading a bug report has no other way to see.
        /// </summary>
        private static string Report(BugFinding finding)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();

            text.Append(finding.Fatal ? "FATAL" : finding.Error ? "ERROR" : "WARN").Append("  ")
                .Append(finding.DefType).Append(' ').Append(finding.DefName).Append('\n');

            if (!finding.Field.NullOrEmpty())
                text.Append("field: ").Append(finding.Field).Append('\n');

            if (!finding.Mod.NullOrEmpty())
                text.Append("mod: ").Append(finding.Mod).Append('\n');

            text.Append("file: ").Append(finding.Path ?? "unknown").Append("\n\n")
                .Append(finding.Detail ?? finding.Message);

            return text.ToString();
        }

        /// <summary>
        /// <c>Widgets.LabelEllipses</c>, skipped rather than crashed when there is no room for it.
        ///
        /// Dropping the label is the right failure: at this width it would have been a bare "..." anyway, and
        /// everything shown here is also in the tooltip or on the copy button.
        /// </summary>
        private static void Ellipsis(Rect rect, string text)
        {
            if (rect.width >= MinLabelWidth)
                Widgets.LabelEllipses(rect, text ?? string.Empty);
        }

        private static bool Button(Rect rect, string label, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);
            UIElementPainter.PaintButton(rect, palette, over, over && Input.GetMouseButton(0));

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextPrimary;

            Widgets.Label(rect, label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>
        /// A button that cannot be pressed, drawn in the palette's own vocabulary for one.
        ///
        /// Restores the anchor and color to what it found rather than to a guess. <c>Text.StartOfOnGUI</c>
        /// checks that state at the top of every frame and complains once if it was left changed, which is how
        /// the same mistake was caught on the simulator's own disabled button.
        /// </summary>
        private static void Disabled(Rect rect, string label, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.ControlBackgroundFaded);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextDisabled;

            Widgets.Label(rect, label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }
    }
}
