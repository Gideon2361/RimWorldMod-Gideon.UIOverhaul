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
    /// A window for asking what an XPath actually matches.
    ///
    /// <b>What this is for.</b> Writing a patch operation means writing an XPath against a document nobody can
    /// see. The only feedback the game gives is that a <c>PatchOperationReplace</c> matched nothing and failed,
    /// somewhere in a load, hours after the mistake -- which is exactly how the storage tab patch in this mod
    /// shipped broken. Being able to type an expression and see the nodes it returns, with the file each one came
    /// from, turns that into a question with an answer.
    ///
    /// <b>The document is rebuilt on demand, not held.</b> See <see cref="XmlWorkbench"/> for why, and for why it
    /// is scoped. The short version is that the real one is discarded during load and is the largest thing the
    /// load ever builds.
    ///
    /// <b>Queries run when asked, not as you type.</b> An XPath over a hundred thousand nodes is not free, and a
    /// half-written expression is usually either invalid or matches everything -- so running one per keystroke
    /// would spend most of its time answering questions nobody asked. Enter or the Run button.
    /// </summary>
    public class Dialog_XmlWorkbench : Window
    {
        /// <summary>Most matches listed. A broad expression can return tens of thousands.</summary>
        private const int MaxResults = 500;

        private const float RowHeight = 26f;
        private const float HeaderHeight = 30f;
        private const float BarHeight = 28f;

        /// <summary>
        /// How much of the width the match list takes, the rest going to the XML.
        ///
        /// Weighted toward the code: a match is one line naming a def and a file, while the XML it stands for is
        /// the thing being read.
        /// </summary>
        private const float ListFraction = 0.42f;

        /// <summary>The three things the workbench does.</summary>
        private enum WorkbenchMode
        {
            Find,
            Simulate,
            Hunt
        }

        /// <summary>
        /// Which of them is on screen.
        ///
        /// Static, so switching tabs and reopening the window comes back to where the last question was left.
        /// It was a bool while there were two modes; a third made the pair of bools it would have become the
        /// kind of state that goes wrong quietly.
        /// </summary>
        private static WorkbenchMode mode = WorkbenchMode.Find;

        /// <summary>
        /// The operation being tested, typed or pasted.
        ///
        /// <b>A real editable box, so an xpath can be corrected in place.</b> Testing a patch is a loop: run it,
        /// see it match nothing, change one predicate, run it again. Making this read only meant leaving the
        /// game for an editor on every turn of that loop, which is most of the value of having the tool at all.
        ///
        /// <c>UITextBoxControl</c> rather than a bare text area, because that is the control that keeps the
        /// camera and the shortcut keys off the keystrokes -- typing "Steel" into anything else drives the map
        /// sideways and toggles whatever S and L are bound to.
        /// </summary>
        private static readonly UITextBoxControl Operation = new UITextBoxControl
        {
            Multiline = true,
            ShowClearButton = false,
            MaxLength = 20000,
            Placeholder = "Paste or type a single Operation element"
        };

        private static PatchSimulation simulation;
        private static bool hasSimulation;

        /// <summary>
        /// The report, composed once when the patch runs.
        ///
        /// <b>Built here rather than while drawing, and that was the lag.</b> The first version assembled the
        /// whole report -- up to fifty pretty printed XML nodes -- into a StringBuilder on every frame, then
        /// measured it with <c>Text.CalcHeight</c>, which walks the text to work out how it wraps. Sixty times a
        /// second, for a string that can run to tens of thousands of characters. Neither the text nor its height
        /// changes between frames unless the pane is resized, so neither belongs in the draw.
        /// </summary>
        private static string reportText = string.Empty;

        private static float reportHeight;
        private static float reportWidth = -1f;

        private static float pastedHeight;
        private static float pastedWidth = -1f;
        private static int pastedLength = -1;

        private static Vector2 pasteScroll;
        private static Vector2 simulationScroll;

        private static readonly UITextBoxControl Xpath = new UITextBoxControl
        {
            Placeholder = "Defs/ThingDef[defName=\"Steel\"]"
        };

        private Vector2 resultScroll;
        private Vector2 detailScroll;

        private List<XmlMatch> results = new List<XmlMatch>();
        private string queryError;
        private string ranExpression;
        private int? selected;

        /// <summary>Which mods the next build will read. Null means every active mod.</summary>
        private ModContentPack chosenMod;

        public Dialog_XmlWorkbench()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Min(1200f, UI.screenWidth - 80f), Mathf.Min(820f, UI.screenHeight - 80f));

        /// <summary>
        /// Drops the rebuilt document when the window closes.
        ///
        /// It exists only to answer questions asked through this window, and it is large. Keeping it after the
        /// window is gone would be holding the biggest object of the load for a question nobody is asking.
        /// </summary>
        /// <summary>
        /// Starts reading as soon as the window opens.
        ///
        /// Every control here needs the document, so there is nothing useful to do before it exists. The read
        /// runs on a background thread, so opening stays instant and the scope bar reports progress.
        /// </summary>
        public override void PostOpen()
        {
            base.PostOpen();

            Load();
        }

        public override void PostClose()
        {
            base.PostClose();

            // The hunt first. It borrows the game's inheritance registry for the length of a run, and that has
            // to be handed back before the document it refers to is dropped.
            UIGuard.Try("Diagnostics.ReleaseBugHunt", XmlBugHunt.Release, null);
            UIGuard.Try("Diagnostics.ReleaseWorkbench", XmlWorkbench.Release, null);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Diagnostics.Workbench", inRect, () => Contents(inRect),
                "The XML workbench shows a failure notice. Nothing else is affected.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect header = new Rect(inRect.x, inRect.y, inRect.width - 30f, HeaderHeight);
            DrawHeader(header, palette);

            Rect scope = new Rect(inRect.x, header.yMax + 2f, inRect.width, BarHeight);
            DrawScopeBar(scope, palette);

            Rect modes = new Rect(inRect.x, scope.yMax + 4f, inRect.width, BarHeight);
            DrawModeBar(modes, palette);

            // The hunt has its own controls inside its panel, so it gets the whole area under the tabs rather
            // than a bar of its own that would sit empty.
            float top = modes.yMax + 4f;

            if (mode == WorkbenchMode.Hunt)
            {
                BugHuntPanel.Draw(new Rect(inRect.x, top, inRect.width, Mathf.Max(0f, inRect.yMax - top - 4f)),
                    palette, StartHunt, Reveal);

                return;
            }

            Rect query = new Rect(inRect.x, top, inRect.width, BarHeight);

            if (mode == WorkbenchMode.Simulate)
                DrawSimulateBar(query, palette);
            else
                DrawQueryBar(query, palette);

            // Side by side rather than stacked. The matches are short lines and the XML is a tall block, so a
            // list across the full width wastes most of its row on nothing while the code underneath gets a
            // letterbox. Splitting them gives the list the height to show a useful number of matches and the
            // XML the height to show a definition without scrolling.
            top = query.yMax + 6f;

            float height = Mathf.Max(0f, inRect.yMax - top - 4f);

            Rect list = new Rect(inRect.x, top, inRect.width * ListFraction - 4f, height);
            Rect detail = new Rect(list.xMax + 8f, top, Mathf.Max(0f, inRect.xMax - list.xMax - 8f), height);

            if (mode == WorkbenchMode.Simulate)
            {
                DrawOperation(list, palette);
                DrawSimulation(detail, palette);
            }
            else
            {
                DrawResults(list, palette);
                DrawDetail(detail, palette);
            }
        }

        /// <summary>The three things the workbench does, as tabs.</summary>
        private void DrawModeBar(Rect rect, UIColorPaletteDef palette)
        {
            Rect find = new Rect(rect.x, rect.y, 130f, rect.height);
            Rect patch = new Rect(find.xMax + 4f, rect.y, 150f, rect.height);
            Rect hunt = new Rect(patch.xMax + 4f, rect.y, 130f, rect.height);

            if (Mode(find, "Find", mode == WorkbenchMode.Find, palette))
                mode = WorkbenchMode.Find;

            if (Mode(patch, "Simulate patch", mode == WorkbenchMode.Simulate, palette))
                mode = WorkbenchMode.Simulate;

            if (Mode(hunt, "Bug hunt", mode == WorkbenchMode.Hunt, palette))
                mode = WorkbenchMode.Hunt;
        }

        /// <summary>
        /// Starts a scan and puts its progress window up.
        ///
        /// The window is what drives the scan; see <see cref="Dialog_BugHunt"/>. Opening it after Begin rather
        /// than before means it never draws a bar for a run that failed to start.
        /// </summary>
        private void StartHunt()
        {
            UIGuard.Try("Diagnostics.StartBugHunt", () =>
            {
                BugHuntPanel.Invalidate();
                XmlBugHunt.Begin(chosenMod == null ? "every active mod" : chosenMod.Name);

                if (XmlBugHunt.Running)
                    Find.WindowStack.Add(new Dialog_BugHunt());

                SoundDefOf.Click.PlayOneShotOnCamera();
            }, "The bug hunt could not be started.");
        }

        /// <summary>
        /// Jumps from a finding to the definition it is about, in the Find tab.
        ///
        /// The whole definition rather than the offending field: the surrounding XML is what tells somebody
        /// whether the value is wrong or the field is in the wrong place, and the field is named on the card
        /// they came from.
        /// </summary>
        private void Reveal(BugFinding finding)
        {
            UIGuard.Try("Diagnostics.RevealBugFinding", () =>
            {
                Xpath.Text = "Defs/" + finding.DefType + "[defName=\"" + finding.DefName + "\"]";
                mode = WorkbenchMode.Find;

                Run();
            }, "That definition could not be selected.");
        }

        private static bool Mode(Rect rect, string label, bool chosen, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            // <b>Unselected is not the same as unavailable.</b> The unselected segment used to sit on
            // ControlBackgroundFaded with TextSecondary on top, which is the palette's vocabulary for a control
            // that cannot be used: a washed out body and dimmed text. It read as greyed out rather than as the
            // other half of a choice. A raised surface with full strength text says "available, just not the
            // one you are on", and hovering lifts it further.
            if (chosen)
                UIElementPainter.FillRounded(rect, palette.Accent);
            else
                UIElementPainter.OutlineRounded(rect, palette.Border,
                    over ? palette.SurfaceRaised : palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = chosen ? palette.WindowBackground : palette.TextPrimary;

            Widgets.Label(rect, label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>
        /// The simulator's controls.
        ///
        /// <b>Pasted from the clipboard rather than typed, and that is a deliberate choice twice over.</b> An
        /// Operation is a multi-line block, and the one text control in this mod that is safe to type into is a
        /// single-line field: anything else lets the camera and the shortcut keys take the keystrokes. More to
        /// the point, nobody composes a patch here. They have one in a file, and the question is what it does to
        /// this document, so copying it out of the file and pressing a button is the actual workflow.
        /// </summary>
        private void DrawSimulateBar(Rect rect, UIColorPaletteDef palette)
        {
            Rect paste = new Rect(rect.x, rect.y, 168f, rect.height);
            Rect run = new Rect(paste.xMax + 6f, rect.y, 110f, rect.height);
            Rect clear = new Rect(run.xMax + 6f, rect.y, 90f, rect.height);

            // Kept alongside Ctrl+V in the box itself, because it means something slightly different: replace
            // everything, rather than insert at the caret.
            if (Button(paste, "Paste over", palette))
            {
                UIGuard.Try("Diagnostics.PastePatch",
                    () => { Operation.Text = GUIUtility.systemCopyBuffer ?? string.Empty; },
                    "The clipboard could not be read.");

                hasSimulation = false;
                pastedWidth = -1f;
                pasteScroll = Vector2.zero;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            bool ready = !Operation.Text.NullOrEmpty() && XmlWorkbench.State == XmlWorkbenchState.Ready;

            if (ready && Button(run, "Simulate", palette))
            {
                simulation = XmlPatchSimulator.Run(Operation.Text);
                hasSimulation = true;
                reportText = Compose(simulation);
                reportWidth = -1f;
                simulationScroll = Vector2.zero;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            else if (!ready)
            {
                UIElementPainter.OutlineRounded(run, palette.Border, palette.ControlBackgroundFaded);

                // <b>Restored to what was here, not to a guess.</b> This used to put the anchor back to
                // MiddleLeft and the color to TextPrimary, neither of which is what RimWorld starts a frame
                // with. Text.StartOfOnGUI checks that state at the top of every frame and logs once when it
                // finds it modified, which is the error that appeared on switching tabs.
                TextAnchor previousDisabledAnchor = Text.Anchor;
                Color previousDisabledColor = GUI.color;

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                Widgets.Label(run, "Simulate");

                Text.Anchor = previousDisabledAnchor;
                GUI.color = previousDisabledColor;
            }

            if (Button(clear, "Clear", palette))
            {
                Operation.Text = string.Empty;
                hasSimulation = false;
                pastedWidth = -1f;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Color previousColor = GUI.color;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(clear.xMax + 10f, rect.y, Mathf.Max(0f, rect.xMax - clear.xMax - 10f),
                rect.height), "Copy a single Operation element from a mod's XML, then press Ctrl+V or the "
                              + "button.");

            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// What the operation did: whether it applied, and the targeted nodes before and after.
        ///
        /// <b>Applied and matched are different answers and both are shown.</b> An operation can report success
        /// while changing nothing, because <c>success</c> can be forced in the XML, and it can change something
        /// and still report failure. The count before and after says what actually happened to the document,
        /// which is the thing a patch author is guessing at.
        /// </summary>
        private void DrawSimulation(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(8f);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;

                float line = UIFonts.LineHeightOf(GameFont.Tiny) + 2f;

                GUI.color = palette.TextDisabled;
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, line), "RESULT");

                Rect body = new Rect(inner.x, inner.y + line + 2f, inner.width,
                    Mathf.Max(0f, inner.height - line - 2f));

                if (!hasSimulation)
                {
                    GUI.color = palette.TextSecondary;
                    Widgets.Label(body, "Not run yet.");

                    return;
                }

                if (!simulation.Error.NullOrEmpty())
                {
                    GUI.color = palette.Danger;
                    Text.WordWrap = true;
                    Widgets.Label(body, simulation.Error);

                    return;
                }

                Text.WordWrap = false;

                string text = reportText;

                // Measured only when the pane's width actually changes. CalcHeight walks the whole string to
                // work out where it wraps, which on a report of this size is not something to do per frame.
                if (!Mathf.Approximately(reportWidth, body.width))
                {
                    reportWidth = body.width;
                    reportHeight = Text.CalcHeight(text, body.width - 18f);
                }

                float height = Mathf.Max(body.height, reportHeight);
                Rect view = new Rect(0f, 0f, body.width - 18f, height);

                Widgets.BeginScrollView(body, ref simulationScroll, view);

                try
                {
                    UIElementPainter.SelectableText(view, text,
                        simulation.Applied ? palette.TextPrimary : palette.Warning);
                }
                finally
                {
                    Widgets.EndScrollView();
                }
            }
            finally
            {
                // Restored to what it was, not to true. True happens to be RimWorld's default, which is exactly
                // why a hardcoded restore hides the bug until some caller sets it false.
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// Turns a finished simulation into the text of the report, once.
        ///
        /// <b>Capped, because a matched node can be a whole definition.</b> Twenty five nodes before and after,
        /// each potentially several hundred lines of pretty printed XML, is a string IMGUI struggles to lay out
        /// and nobody reads to the end of. What answers the question is the shape of the change and the first
        /// few examples of it; the counts above say how many there were.
        /// </summary>
        private static string Compose(PatchSimulation result)
        {
            System.Text.StringBuilder report = new System.Text.StringBuilder();

            report.Append(result.Applied ? "APPLIED" : "DID NOT APPLY").Append("   ")
                .Append(result.Operation).Append('\n');

            if (!result.Xpath.NullOrEmpty())
            {
                report.Append(result.Xpath).Append('\n')
                    .Append("matched ").Append(result.MatchedBefore).Append(" before, ")
                    .Append(result.MatchedAfter).Append(" after\n");
            }
            else
            {
                report.Append("This operation has no single xpath, so there is nothing to show before and "
                              + "after.\n");
            }

            // The most common failure has its own sentence, because "matched 0" is the answer to the question
            // and deserves to be said rather than inferred from a zero.
            if (result.MatchedBefore == 0 && !result.Xpath.NullOrEmpty())
                report.Append("\nThe xpath matched nothing in this scope. Check the scope above, and the path "
                              + "itself.\n");

            Append(report, "BEFORE", result.Before);
            Append(report, "AFTER", result.After);

            return report.ToString();
        }

        private static void Append(System.Text.StringBuilder report, string heading, List<string> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return;

            report.Append('\n').Append(heading).Append('\n');

            // <b>A tight budget, and the tightness is the point.</b> This text ends up in a GUI.TextArea, which
            // IMGUI lays out in full on every frame the pane is open -- so an oversized report does not cost a
            // moment when it is built, it costs frame rate across the whole game for as long as the window
            // stays up. Four thousand characters a section is enough to see what a patch did and cheap enough
            // to draw sixty times a second.
            const int budget = 4000;
            int used = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                string node = nodes[i] ?? string.Empty;

                if (used >= budget)
                {
                    report.Append("... ").Append(nodes.Count - i).Append(" more not shown\n");

                    return;
                }

                if (node.Length > budget - used)
                    node = node.Substring(0, budget - used) + "\n... truncated";

                used += node.Length;

                report.Append(node).Append('\n');
            }
        }

        /// <summary>The operation as pasted, so what is about to run is visible before it runs.</summary>
        private void DrawOperation(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(8f);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                float line = UIFonts.LineHeightOf(GameFont.Tiny) + 2f;
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, line), "OPERATION");

                Rect body = new Rect(inner.x, inner.y + line + 2f, inner.width,
                    Mathf.Max(0f, inner.height - line - 2f));

                Text.WordWrap = false;

                // Measured on width change only. CalcHeight walks the whole string, and this runs every frame
                // the tab is open.
                if (!Mathf.Approximately(pastedWidth, body.width) || Operation.Text.Length != pastedLength)
                {
                    pastedWidth = body.width;
                    pastedLength = Operation.Text.Length;
                    pastedHeight = Text.CalcHeight(Operation.Text + "\n ", body.width - 22f);
                }

                // The box is given the taller of the pane and its content, so it grows as the operation does and
                // the scroll view carries it. A text area does not scroll itself.
                float height = Mathf.Max(body.height - 2f, pastedHeight + 8f);
                Rect view = new Rect(0f, 0f, body.width - 18f, height);

                Widgets.BeginScrollView(body, ref pasteScroll, view);

                try
                {
                    if (Operation.Draw(view, palette))
                    {
                        // Any edit invalidates the result below it: a report describing the previous text, sat
                        // beside text that has since changed, is the panel telling two stories at once.
                        hasSimulation = false;
                    }
                }
                finally
                {
                    Widgets.EndScrollView();
                }
            }
            finally
            {
                // Restored to what it was, not to true. True happens to be RimWorld's default, which is exactly
                // why a hardcoded restore hides the bug until some caller sets it false.
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        private static void DrawHeader(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;
            Widgets.Label(rect, "XML Workbench");

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextSecondary;

            int nodes;
            int files;

            XmlWorkbench.Stats(out nodes, out files);

            string status;

            switch (XmlWorkbench.State)
            {
                case XmlWorkbenchState.Building:
                    status = "Reading " + XmlWorkbench.ScopeName + " ...";
                    break;

                case XmlWorkbenchState.Ready:
                    status = XmlWorkbench.ScopeName + ": " + nodes + " definitions from " + files + " files";
                    break;

                case XmlWorkbenchState.Failed:
                    GUI.color = palette.Danger;
                    status = "Could not read: " + XmlWorkbench.Failure;
                    break;

                default:
                    status = "Nothing loaded";
                    break;
            }

            Widgets.Label(rect, status);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// Choosing what to read, and reading it.
        ///
        /// <b>The scope is offered rather than assumed</b> because reading everything is genuinely expensive on a
        /// large mod list, and most questions are about one mod. Core is always included with a single mod, since
        /// almost every patch matches something the base game defined and a document without it would answer no
        /// for the wrong reason.
        /// </summary>
        /// <summary>
        /// The scope picker, and what the load is doing.
        ///
        /// <b>There is no load button, deliberately.</b> There was one, and it was a trap: nothing in the window
        /// works until the document exists, so the only thing a player could do on opening was press it -- and
        /// forgetting to made every other control silently do nothing. A step that must always be taken before
        /// anything else is not a choice, it is a delay. Opening the window loads, and changing the scope
        /// reloads.
        /// </summary>
        private void DrawScopeBar(Rect rect, UIColorPaletteDef palette)
        {
            Rect picker = new Rect(rect.x, rect.y, Mathf.Min(340f, rect.width * 0.36f), rect.height);

            string label = chosenMod == null
                ? "Scope: every active mod"
                : "Scope: " + chosenMod.Name + " + Core";

            if (Button(picker, label, palette))
                OpenScopeMenu();

            // <b>The single most consequential setting in this window.</b> Off, the document is the files as
            // they sit on disk, which is not what the game reads and not what anything here should be judged
            // against: the bug hunt reports missing inherited parents that patches create, and the Find tab
            // shows values that patches have since changed. On, it costs the slowest half of the build. See
            // XmlWorkbench.Patch.
            Rect patched = new Rect(picker.xMax + 10f, rect.y, 150f, rect.height);
            bool applying = XmlWorkbench.Patching;

            if (UICheckboxControl.Draw(patched, ref applying, palette, "Apply patches",
                    "Runs every mod's patch operations over the document, the way loading does.\n\nOn is what "
                    + "the game actually reads, and what the bug hunt needs in order not to report faults that "
                    + "patches have already fixed.\n\nTurn it off to test a patch operation against the raw "
                    + "files, so your own patch is not already in the document you are testing it against.",
                    disabled: XmlWorkbench.State == XmlWorkbenchState.Building))
            {
                Load(applying);
            }

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            bool building = XmlWorkbench.State == XmlWorkbenchState.Building;

            GUI.color = building ? palette.Accent : palette.TextDisabled;

            Rect hint = new Rect(patched.xMax + 10f, rect.y, Mathf.Max(0f, rect.xMax - patched.xMax - 12f),
                rect.height);

            Widgets.Label(hint, building
                ? "Reading " + XmlWorkbench.ScopeName + " ..."
                : XmlWorkbench.Patching
                    ? Patched()
                    : "Raw files, before any patch operation has run.");

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// What the patch pass did, once it has.
        ///
        /// <b>Failures are stated rather than hidden, and put next to the scope,</b> because with one mod
        /// selected most of them are meaningless: every other mod's patches were still run, and the definitions
        /// they target were never read. On the full scope the same number means something quite different, so
        /// the sentence changes with the scope rather than leaving the reader to work out which case they are
        /// looking at.
        /// </summary>
        private string Patched()
        {
            int failures = XmlWorkbench.PatchFailures;

            if (failures == 0)
                return "Patched, as the game reads it.";

            return chosenMod == null
                ? "Patched. " + failures + " operations matched nothing, which is worth looking into."
                : "Patched. " + failures + " operations matched nothing, expected when the scope is narrowed.";
        }

        private void OpenScopeMenu()
        {
            // Choosing a scope reloads immediately. Picking one and then having to press something else would be
            // the load button again by another name.
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Every active mod",
                    UIGuard.Wrap("Diagnostics.WorkbenchScopeAll", () =>
                    {
                        chosenMod = null;
                        Load();
                    }))
            };

            foreach (ModContentPack mod in XmlWorkbench.Candidates())
            {
                ModContentPack captured = mod;

                options.Add(new FloatMenuOption(mod.Name ?? mod.PackageId,
                    UIGuard.Wrap("Diagnostics.WorkbenchScopeOne", () =>
                    {
                        chosenMod = captured;
                        Load();
                    })));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <param name="applyPatches">Null keeps whatever the toggle is already set to.</param>
        private void Load(bool? applyPatches = null)
        {
            UIGuard.Try("Diagnostics.WorkbenchLoad", () =>
            {
                List<ModContentPack> scope = new List<ModContentPack>();
                string name;

                if (chosenMod == null)
                {
                    scope.AddRange(XmlWorkbench.Candidates());
                    name = "every active mod";
                }
                else
                {
                    // Core comes along whatever else was picked. A patch almost always matches something the
                    // base game defined, and a document without it would say "no matches" for a reason that has
                    // nothing to do with the expression being tested.
                    foreach (ModContentPack mod in XmlWorkbench.Candidates())
                    {
                        if (mod == chosenMod || mod.IsCoreMod)
                            scope.Add(mod);
                    }

                    name = chosenMod.Name;
                }

                XmlWorkbench.Build(scope, name, applyPatches);

                // Findings belong to the scope they were found in, and their nodes belong to the document that
                // is about to be replaced. Keeping them across a rebuild would leave cards pointing at files
                // that are no longer being read.
                XmlBugHunt.Release();
                BugHuntPanel.Invalidate();

                results = new List<XmlMatch>();
                queryError = null;
                ranExpression = null;
                selected = null;
            }, "The workbench could not read the chosen mods.");
        }

        private void DrawQueryBar(Rect rect, UIColorPaletteDef palette)
        {
            Rect run = new Rect(rect.xMax - 70f, rect.y, 68f, rect.height);
            Rect box = new Rect(rect.x, rect.y, Mathf.Max(80f, run.x - rect.x - 6f), rect.height);

            // <b>Tested before the field is drawn, and that is the whole fix.</b> Unity's text field handles the
            // event stream as it draws: by the time Draw has returned, the Return keypress has been consumed and
            // the event is no longer a KeyDown to anybody downstream. Reading it first, and consuming it before
            // the field ever sees it, is also what stops the keypress being treated as text.
            //
            // Focused is last frame's answer, which is correct here: the box was focused when the key went down.
            // Matched on the character as well as the key code, because Unity sends a keypress into a text field
            // as two events: one carrying the code with a null character, and one carrying the character with no
            // code. Which of the two arrives first is not something to depend on.
            bool entered = Xpath.Focused && Event.current.type == EventType.KeyDown
                                         && (Event.current.keyCode == KeyCode.Return
                                             || Event.current.keyCode == KeyCode.KeypadEnter
                                             || Event.current.character == '\n'
                                             || Event.current.character == '\r');

            if (entered)
                Event.current.Use();

            Xpath.Draw(box, palette);

            if ((Button(run, "Run", palette) || entered) && XmlWorkbench.State == XmlWorkbenchState.Ready)
                Run();
        }

        private void Run()
        {
            UIGuard.Try("Diagnostics.WorkbenchQuery", () =>
            {
                string error;

                results = XmlWorkbench.Query(Xpath.Text, MaxResults, out error);
                queryError = error;
                ranExpression = Xpath.Text;
                selected = null;
                resultScroll = Vector2.zero;
            }, "That expression could not be run.");
        }

        private void DrawResults(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Color previousColor = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;

            Rect inner = rect.ContractedBy(4f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;

                // An invalid expression is the most common thing to see here and is an answer, not a fault. It
                // reads as a message where the results would be, rather than as an empty list that looks like a
                // valid expression matching nothing.
                if (!queryError.NullOrEmpty())
                {
                    GUI.color = palette.Danger;
                    Widgets.Label(inner, "That expression is not valid XPath:\n" + queryError);

                    return;
                }

                if (XmlWorkbench.State != XmlWorkbenchState.Ready)
                {
                    GUI.color = palette.TextDisabled;
                    Widgets.Label(inner, XmlWorkbench.State == XmlWorkbenchState.Building
                        ? "Reading the XML..."
                        : "No XML loaded. Choose a scope to read one.");

                    return;
                }

                if (ranExpression == null)
                {
                    GUI.color = palette.TextDisabled;
                    Widgets.Label(inner, "Type an XPath expression and press Enter.");

                    return;
                }

                if (results.Count == 0)
                {
                    GUI.color = palette.Warning;
                    Widgets.Label(inner, "Valid expression, no matches. A patch operation using this would "
                                         + "fail.");

                    return;
                }

                float contentHeight = results.Count * RowHeight;
                Rect view = new Rect(0f, 0f, inner.width - 18f, contentHeight);

                Widgets.BeginScrollView(inner, ref resultScroll, view);

                try
                {
                    int first = Mathf.Max(0, Mathf.FloorToInt(resultScroll.y / RowHeight) - 1);
                    int last = Mathf.Min(results.Count, first + Mathf.CeilToInt(inner.height / RowHeight) + 2);

                    for (int i = first; i < last; i++)
                        DrawResult(new Rect(0f, i * RowHeight, view.width, RowHeight), i, palette);
                }
                finally
                {
                    Widgets.EndScrollView();
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

        private void DrawResult(Rect rect, int index, UIColorPaletteDef palette)
        {
            XmlMatch match = results[index];
            bool chosen = selected == index;

            if (chosen)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
            else if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            GUI.color = palette.TextPrimary;

            float half = rect.width * 0.42f;

            Widgets.LabelEllipses(new Rect(rect.x + 4f, rect.y, half, rect.height),
                match.Owner ?? "(node)");

            GUI.color = palette.TextDisabled;

            Widgets.LabelEllipses(new Rect(rect.x + half + 8f, rect.y,
                    Mathf.Max(0f, rect.width - half - 12f), rect.height),
                (match.Mod == null ? string.Empty : match.Mod + "  -  ") + (match.Path ?? "unknown file"));

            if (Mouse.IsOver(rect) && !match.Path.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) match.Path);

            if (!Widgets.ButtonInvisible(rect))
                return;

            selected = chosen ? (int?) null : index;
            detailScroll = Vector2.zero;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DrawDetail(Rect rect, UIColorPaletteDef palette)
        {
            // Raised rather than sunken: this is a reading surface on the panel, and SurfaceSunken is this
            // palette's empty-socket color, two steps below the window. See the note in LoadingConsole.
            Color previousColor = GUI.color;
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(6f);

            // The pane is always here now that it sits beside the list rather than below it, so an empty one has
            // to say what would fill it.
            if (!selected.HasValue || selected.Value >= results.Count)
            {
                GameFont previousEmptyFont = Text.Font;

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(inner, results.Count > 0
                    ? "Select a match to see its XML."
                    : "Matched XML appears here.");

                GUI.color = previousColor;
                Text.Font = previousEmptyFont;

                return;
            }

            XmlMatch match = results[selected.Value];

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                float line = UIFonts.LineHeightOf(GameFont.Tiny) + 2f;
                Rect head = new Rect(inner.x, inner.y, inner.width - 180f, line);

                GUI.color = palette.Accent;
                Widgets.LabelEllipses(head, match.Path ?? "no file recorded");

                Rect copyPath = new Rect(inner.xMax - 86f, inner.y, 86f, line);
                Rect copyXml = new Rect(copyPath.x - 86f, inner.y, 82f, line);

                if (Button(copyXml, "Copy XML", palette))
                {
                    UIGuard.Try("Diagnostics.CopyWorkbenchXml",
                        () => { GUIUtility.systemCopyBuffer = match.Xml; }, null);

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                if (Button(copyPath, "Copy path", palette))
                {
                    UIGuard.Try("Diagnostics.CopyWorkbenchPath",
                        () => { GUIUtility.systemCopyBuffer = match.Path ?? string.Empty; }, null);

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                Rect body = new Rect(inner.x, head.yMax + 4f, inner.width,
                    Mathf.Max(0f, inner.yMax - head.yMax - 4f));

                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = palette.TextPrimary;

                string xml = match.Xml ?? string.Empty;

                float height = Text.CalcHeight(xml, body.width - 18f);
                Rect view = new Rect(0f, 0f, body.width - 18f, Mathf.Max(height, body.height));

                // A read-only text area rather than a label, so the XML can be selected and part of it copied.
                // A label cannot be selected at all, which left the copy button as the only way to get anything
                // out of this pane -- all of it or none. Read-only means edits are impossible rather than merely
                // discarded, so there is no way to think you have changed something.
                //
                // This is what vanilla's own debug log does with its stack traces, for the same reason.
                Widgets.BeginScrollView(body, ref detailScroll, view);

                try
                {
                    UIElementPainter.SelectableText(view, xml, palette.TextPrimary);
                }
                finally
                {
                    Widgets.EndScrollView();
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
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
    }
}
