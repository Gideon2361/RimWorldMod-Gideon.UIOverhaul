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

            Rect query = new Rect(inRect.x, scope.yMax + 4f, inRect.width, BarHeight);
            DrawQueryBar(query, palette);

            // Side by side rather than stacked. The matches are short lines and the XML is a tall block, so a
            // list across the full width wastes most of its row on nothing while the code underneath gets a
            // letterbox. Splitting them gives the list the height to show a useful number of matches and the
            // XML the height to show a definition without scrolling.
            float top = query.yMax + 6f;
            float height = Mathf.Max(0f, inRect.yMax - top - 4f);

            Rect list = new Rect(inRect.x, top, inRect.width * ListFraction - 4f, height);
            Rect detail = new Rect(list.xMax + 8f, top, Mathf.Max(0f, inRect.xMax - list.xMax - 8f), height);

            DrawResults(list, palette);
            DrawDetail(detail, palette);
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
            Rect picker = new Rect(rect.x, rect.y, Mathf.Min(420f, rect.width * 0.45f), rect.height);

            string label = chosenMod == null
                ? "Scope: every active mod"
                : "Scope: " + chosenMod.Name + " + Core";

            if (Button(picker, label, palette))
                OpenScopeMenu();

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            bool building = XmlWorkbench.State == XmlWorkbenchState.Building;

            GUI.color = building ? palette.Accent : palette.TextDisabled;

            Widgets.Label(new Rect(picker.xMax + 10f, rect.y, Mathf.Max(0f, rect.xMax - picker.xMax - 12f),
                    rect.height),
                building
                    ? "Reading " + XmlWorkbench.ScopeName + " ..."
                    : chosenMod == null
                        ? "Every active mod's Defs folder. Narrow the scope for a faster, smaller read."
                        : "One mod plus the base game, which is enough for most expressions.");

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
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

        private void Load()
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

                XmlWorkbench.Build(scope, name);

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

                int first = Mathf.Max(0, Mathf.FloorToInt(resultScroll.y / RowHeight) - 1);
                int last = Mathf.Min(results.Count, first + Mathf.CeilToInt(inner.height / RowHeight) + 2);

                for (int i = first; i < last; i++)
                    DrawResult(new Rect(0f, i * RowHeight, view.width, RowHeight), i, palette);

                Widgets.EndScrollView();
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
                UIElementPainter.SelectableText(view, xml, palette.TextPrimary);
                Widgets.EndScrollView();
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
