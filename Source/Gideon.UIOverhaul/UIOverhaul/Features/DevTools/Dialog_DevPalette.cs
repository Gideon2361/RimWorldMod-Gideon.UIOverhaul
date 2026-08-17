using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.DevTools
{
    /// <summary>
    /// The developer menu as a search box.
    ///
    /// <b>What is wrong with the one this replaces.</b> <c>Dialog_Debug</c> sizes itself to the entire display,
    /// so using it means losing sight of the thing being debugged. Its filter calls <c>FilterAllows</c> on the
    /// tab you are already standing in, which means finding an action requires first knowing which of nine tabs
    /// owns it. And it is mouse only, for a window opened dozens of times in a session.
    ///
    /// <b>What this does instead.</b> One query runs across every tab at once, ranked, with each result saying
    /// where it came from. Recently used actions sit at the top, because debugging is the same four things over
    /// and over. Actions that read as irreversible are marked and ask first. The window is a panel you can leave
    /// open beside the map.
    ///
    /// <b>Vanilla runs the action, not us.</b> Choosing a leaf calls <c>DebugActionNode.Enter(null)</c>, which is
    /// the game's own dispatch: it handles plain actions, the two map tools and the pawn tool, and refreshes the
    /// label cache. Null is safe there -- the only thing the dialog argument is used for is closing it, and this
    /// closes itself. Reimplementing that switch would work until Ludeon added a fifth action type.
    /// </summary>
    public class Dialog_DevPalette : Window
    {
        private const float HeaderHeight = 46f;
        private const float RailWidth = 190f;
        private const float RowHeight = 26f;
        private const float FooterHeight = 26f;
        private const float Pad = 12f;

        /// <summary>How many results are shown. Past this the answer is to type more, not to scroll more.</summary>
        private const int MaxResults = 200;

        /// <summary>How many recently used actions are remembered.</summary>
        private const int MaxRecent = 6;

        /// <summary>Narrowest width worth drawing text into. Below this, nothing is drawn at all.</summary>
        private const float MinLabelWidth = 24f;

        private static readonly List<DebugActionNode> Recent = new List<DebugActionNode>();

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search every developer action",
            MaxLength = 60
        };

        private Vector2 scroll;
        private int highlighted;

        /// <summary>Null for every tab, otherwise the one being shown.</summary>
        private string tab;

        /// <summary>The branch being browsed, or null when showing search results.</summary>
        private DebugActionNode inside;

        private List<DevAction> results = new List<DevAction>();
        private string cachedQuery = string.Empty;
        private string cachedTab = string.Empty;

        /// <summary>Which branch the current results came from, so they are not rebuilt on every frame.</summary>
        private DebugActionNode cachedInside;

        /// <summary>Chosen this frame, run by <see cref="Flush"/> once the drawing has finished.</summary>
        private DebugActionNode pending;

        /// <summary>Set when the keyboard moved the highlight, so the view scrolls to it exactly once.</summary>
        private bool follow;

        /// <summary>
        /// Navigation asked for during the frame, applied by <see cref="Flush"/> once drawing has finished.
        ///
        /// <b>Deferred for the same reason actions are.</b> Changing which branch is open partway through a
        /// frame leaves the rest of that frame drawing against a state its results no longer match: the header
        /// stops showing a Back button while the list still holds the branch's children, and the control the
        /// search box would have drawn appears or vanishes mid-frame, which is exactly what IMGUI cannot take.
        /// Every navigation is a request here, and the frame that observes it is the next one.
        /// </summary>
        private DebugActionNode navigateInto;

        private bool navigateOut;

        private string navigateTab;
        private bool navigateTabSet;

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(960f, UI.screenWidth - 40f),
            Mathf.Min(620f, UI.screenHeight - 40f));

        protected override float Margin => 0f;

        public Dialog_DevPalette()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;

        }

        public override void PreOpen()
        {
            base.PreOpen();

            UIGuard.Try("DevTools.BuildIndex", DevActionIndex.Build, null);

            // Opened by a keystroke and driven by typing, so the box takes focus without being clicked.
            UIGuard.Try("DevTools.FocusSearch", search.Focus, null);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + HeaderHeight);

            UIGuardedPanel.Draw("DevTools.Palette", inRect, () => Contents(inRect),
                "The developer palette could not be drawn. The game's own developer menu still works.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            HandleKeys();
            Rebuild();

            Rect header = new Rect(inRect.x + Pad, inRect.y + 10f, inRect.width - Pad * 2f - 28f, HeaderHeight - 12f);
            DrawSearch(header, palette);

            Rect body = new Rect(inRect.x, header.yMax + 8f, inRect.width,
                Mathf.Max(0f, inRect.height - header.yMax - FooterHeight - 10f));

            Rect rail = new Rect(body.x, body.y, RailWidth, body.height);
            Rect main = new Rect(rail.xMax + 1f, body.y, Mathf.Max(0f, body.width - RailWidth - 1f), body.height);

            DrawRail(rail, palette);
            DrawResults(main, palette);

            DrawFooter(new Rect(inRect.x + Pad, inRect.yMax - FooterHeight, inRect.width - Pad * 2f,
                FooterHeight - 6f), palette);

            // Last, once every list has been read and nothing is mid-iteration.
            Flush();
        }

        /// <summary>
        /// Arrow keys move, Enter runs, Escape steps back out.
        ///
        /// <b>Read before anything draws, and consumed.</b> The search box has focus by design, and a text field
        /// that sees an arrow key moves its caret; letting that happen would make the list unusable from the
        /// keyboard, which is the point of the window.
        /// </summary>
        private void HandleKeys()
        {
            Event current = Event.current;

            if (current == null || current.type != EventType.KeyDown)
                return;

            switch (current.keyCode)
            {
                case KeyCode.DownArrow:
                    highlighted++;
                    follow = true;
                    current.Use();
                    break;

                case KeyCode.UpArrow:
                    highlighted--;
                    follow = true;
                    current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    current.Use();
                    Choose(highlighted);
                    break;

                case KeyCode.Escape:
                    current.Use();

                    // Out of a branch first, and only then out of the window. Escape closing everything from
                    // three levels deep is the behavior that makes drilling in feel like a trap.
                    if (inside != null)
                        navigateOut = true;
                    else
                        Close();

                    break;
            }
        }

        private void DrawSearch(Rect rect, UIColorPaletteDef palette)
        {
            if (inside != null)
            {
                // Inside a branch the query does not apply, so the box is replaced by where you are and the way
                // back. A search field that silently stopped filtering would be worse than not showing one.
                UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

                Rect back = new Rect(rect.x + 6f, rect.y + 4f, 70f, rect.height - 8f);

                if (Button(back, "Back", palette))
                    navigateOut = true;

                GameFont previousFont = Text.Font;
                Color previousColor = GUI.color;
                TextAnchor previousAnchor = Text.Anchor;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                // LabelNow, not label. A node built with a labelGetter leaves the label field null, and drawing
                // a null string is where backing out of such a branch threw.
                Widgets.Label(new Rect(back.xMax + 10f, rect.y, rect.width - back.width - 24f, rect.height),
                    Name(inside));

                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;

                return;
            }

            search.Draw(rect, palette);
        }

        private void DrawRail(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Color previousColor = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawLineVertical(rect.xMax, rect.y, rect.height);
            GUI.color = previousColor;

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;

            float y = rect.y + 6f;

            Heading(new Rect(rect.x + 10f, y, rect.width - 16f, 16f), "TABS", palette);
            y += 18f;

            if (RailRow(new Rect(rect.x, y, rect.width, RowHeight), "All results", DevActionIndex.Actions.Count,
                    tab == null, palette))
            {
                navigateTab = null;
                navigateTabSet = true;
                navigateOut = true;
            }

            y += RowHeight;

            foreach (KeyValuePair<string, int> entry in DevActionIndex.Tabs)
            {
                if (RailRow(new Rect(rect.x, y, rect.width, RowHeight), entry.Key, entry.Value,
                        tab == entry.Key, palette))
                {
                    navigateTab = entry.Key;
                    navigateTabSet = true;
                    navigateOut = true;
                }

                y += RowHeight;
            }

            if (Recent.Count > 0)
            {
                y += 8f;
                Heading(new Rect(rect.x + 10f, y, rect.width - 16f, 16f), "RECENT", palette);
                y += 18f;

                for (int i = 0; i < Recent.Count; i++)
                {
                    Rect row = new Rect(rect.x, y, rect.width, RowHeight);

                    if (RailRow(row, Recent[i].label, -1, false, palette))
                        Run(Recent[i]);

                    y += RowHeight;
                }
            }

            Text.Font = previousFont;
        }

        private static void Heading(Rect rect, string label, UIColorPaletteDef palette)
        {
            Color previousColor = GUI.color;
            GUI.color = palette.TextDisabled;
            Widgets.Label(rect, label);
            GUI.color = previousColor;
        }

        private static bool RailRow(Rect rect, string label, int count, bool chosen, UIColorPaletteDef palette)
        {
            if (chosen)
                Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);
            else if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            if (chosen)
            {
                Color accent = GUI.color;
                GUI.color = palette.Accent;
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 2f, rect.height), palette.Accent);
                GUI.color = accent;
            }

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = chosen ? palette.TextPrimary : palette.TextSecondary;

            Widgets.LabelEllipses(new Rect(rect.x + 12f, rect.y, rect.width - 50f, rect.height), label);

            if (count >= 0)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;
                Widgets.Label(new Rect(rect.xMax - 34f, rect.y, 26f, rect.height), count.ToString());
            }

            Text.WordWrap = previousWrap;
            Text.Anchor = previousAnchor;
            GUI.color = previousColor;

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>Recomputes the visible list when the query, the tab or the branch has moved.</summary>
        private void Rebuild()
        {
            string query = search.Text ?? string.Empty;

            // The branch is part of the cache key. Without it, browsing a branch rebuilt its children every
            // frame, which meant running a mod's childGetter sixty times a second.
            if (query == cachedQuery && tab == cachedTab && inside == cachedInside)
                return;

            cachedQuery = query;
            cachedTab = tab;
            cachedInside = inside;

            if (inside != null)
            {
                results = Children(inside);
                highlighted = 0;

                return;
            }

            results = DevActionIndex.Search(query, tab, MaxResults);
            highlighted = 0;
        }

        /// <summary>
        /// The children of a branch, expanded on demand.
        ///
        /// This is where a lazy <c>childGetter</c> finally runs, which is the right moment: the reader has asked
        /// for it, and the game state it reads is the state they are looking at.
        /// </summary>
        private static List<DevAction> Children(DebugActionNode node)
        {
            List<DevAction> children = new List<DevAction>();

            UIGuard.Try("DevTools.Expand", () =>
            {
                node.TrySetupChildren();
                node.TrySort();

                foreach (DebugActionNode child in node.children)
                {
                    if (child == null || !child.VisibleNow)
                        continue;

                    children.Add(new DevAction
                    {
                        Node = child,
                        Label = Name(child),
                        Where = Name(node),
                        Branch = child.children.Count > 0 || child.childGetter != null
                    });
                }
            }, "That developer action could not be opened.");

            return children;
        }

        private void DrawResults(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.WindowBackground);

            if (results.Count == 0)
            {
                Color empty = GUI.color;
                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, search.IsEmpty ? "Type to search." : "Nothing matches.");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = empty;

                return;
            }

            highlighted = Mathf.Clamp(highlighted, 0, results.Count - 1);

            Rect inner = rect.ContractedBy(6f);

            // <b>A branch of choices is a grid, not a column.</b> Picking a weather or a faction means reading a
            // hundred short labels, and a hundred short labels in one column is a scroll bar and a lot of empty
            // space to the right of every one of them. Search results stay a single column, because there the
            // rows carry a category as well and the ranking means the answer is near the top rather than found
            // by scanning.
            int columns = inside != null && results.Count > 12
                ? Mathf.Max(1, Mathf.FloorToInt((inner.width - 18f) / 210f))
                : 1;

            float columnWidth = (inner.width - 18f) / columns;
            int rows = Mathf.CeilToInt(results.Count / (float) columns);

            Rect view = new Rect(0f, 0f, inner.width - 18f, rows * RowHeight);

            // <b>Only when the keyboard moved it, and that distinction is the whole bug.</b> This used to run
            // every frame, so it re-asserted the scroll position continuously: with the highlight on the first
            // row, any scroll the reader made was snapped straight back on the next frame. The wheel looked
            // dead and the scrollbar could not be dragged at all. Following the selection is right when the
            // selection is what moved, and wrong the rest of the time.
            if (follow)
            {
                follow = false;

                float top = highlighted / columns * RowHeight;

                if (top < scroll.y)
                    scroll.y = top;
                else if (top + RowHeight > scroll.y + inner.height)
                    scroll.y = top + RowHeight - inner.height;
            }

            Widgets.BeginScrollView(inner, ref scroll, view);

            GameFont previousFont = Text.Font;
            bool previousWrap = Text.WordWrap;

            Text.Font = GameFont.Small;
            Text.WordWrap = false;

            // <b>Paired through a finally, and this is not defensive decoration.</b> A throw between Begin and
            // End leaves Unity's GUI clip and mouse-position stacks unbalanced, and the game then reports
            // "more calls to BeginScrollView than EndScrollView" and fixes them itself -- for the rest of the
            // session, on every window, not just this one. One bad row inside a scroll view must not be able to
            // damage the GUI state of everything drawn after it.
            try
            {
                int firstRow = Mathf.Max(0, Mathf.FloorToInt(scroll.y / RowHeight) - 1);
                int lastRow = Mathf.Min(rows, firstRow + Mathf.CeilToInt(inner.height / RowHeight) + 2);

                int first = firstRow * columns;
                int last = Mathf.Min(results.Count, lastRow * columns);

                for (int i = first; i < last; i++)
                {
                    Rect cell = new Rect(i % columns * columnWidth, i / columns * RowHeight,
                        columnWidth, RowHeight);

                    DrawResult(cell, results[i], i, palette);
                }
            }
            finally
            {
                Text.WordWrap = previousWrap;
                Text.Font = previousFont;

                Widgets.EndScrollView();
            }
        }

        private void DrawResult(Rect rect, DevAction action, int index, UIColorPaletteDef palette)
        {
            bool chosen = index == highlighted;

            if (chosen)
                UIElementPainter.FillRounded(rect, palette.Accent);
            else if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleLeft;

            float x = rect.x + 10f;

            if (action.Destructive)
            {
                Rect dot = new Rect(x, rect.y + rect.height / 2f - 3f, 6f, 6f);
                Widgets.DrawBoxSolid(dot, chosen ? palette.WindowBackground : palette.Danger);
                x += 12f;
            }

            GUI.color = chosen ? palette.WindowBackground : palette.TextPrimary;

            string label = action.Label + (action.Branch ? "  >" : string.Empty);

            // <b>The width is budgeted, not assumed.</b> This is what threw: the category column took a fixed
            // share, and on a narrow window that left the label a width at or below zero.
            // <c>Widgets.LabelEllipses</c> truncates to fit by measuring and cutting, and asking it to cut a
            // string to nothing is an index out of range from inside vanilla. The label is given first claim and
            // the category only what is genuinely spare.
            float available = Mathf.Max(0f, rect.xMax - x - 12f);
            float whereWidth = action.Where.NullOrEmpty()
                ? 0f
                : Mathf.Clamp(rect.width * 0.34f, 0f, Mathf.Max(0f, available - MinLabelWidth));

            float labelWidth = Mathf.Max(0f, available - whereWidth);

            // Below a few pixels there is no honest way to render text, so nothing is drawn rather than
            // something being cut to nothing.
            if (labelWidth >= MinLabelWidth)
                Widgets.LabelEllipses(new Rect(x, rect.y, labelWidth, rect.height), label);

            if (whereWidth >= MinLabelWidth)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = chosen ? palette.WindowBackground : palette.TextDisabled;

                Widgets.LabelEllipses(new Rect(rect.xMax - whereWidth - 6f, rect.y, whereWidth, rect.height),
                    action.Where);
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;

            if (Widgets.ButtonInvisible(rect))
            {
                highlighted = index;
                Choose(index);
            }
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= results.Count)
                return;

            DevAction action = results[index];

            if (action.Branch)
            {
                navigateInto = action.Node;
                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            if (action.Destructive && !SkipConfirm)
            {
                Confirm(action);

                return;
            }

            Run(action.Node);
        }

        /// <summary>Whether the player has asked not to be asked.</summary>
        private static bool SkipConfirm =>
            UIGuard.Try("DevTools.ReadConfirmSetting",
                () => UIOverhaulSettingsFile.Current?.skipDevActionConfirm ?? false, false, null);

        /// <summary>
        /// Asks before an action that cannot be taken back, and offers the way out of ever being asked again.
        ///
        /// <b>Three buttons rather than two, and the third is the point.</b> Somebody who finds this dialog
        /// unwelcome is looking at it precisely when they are least inclined to go hunting through a settings
        /// menu for the switch. Putting "Always allow" here means the one interruption also carries its own
        /// remedy, and the setting it writes is the same one the Developer Tools section shows.
        ///
        /// The run button is marked destructive so vanilla draws it in red, which is the standing convention for
        /// the choice that cannot be undone.
        /// </summary>
        private void Confirm(DevAction action)
        {
            Dialog_MessageBox box = new Dialog_MessageBox(
                "Run this developer action?\n\n" + action.Label + "\n\nIt looks like it cannot be undone.",
                "Run", () => Run(action.Node),
                "Cancel", null,
                "Developer action",
                true);

            box.buttonCText = "Always allow";

            box.buttonCAction = () =>
            {
                UIGuard.Try("DevTools.AlwaysAllow", () =>
                {
                    UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                    if (settings == null)
                        return;

                    settings.skipDevActionConfirm = true;
                    settings.Save();
                }, "The setting could not be saved, so this will ask again next time.");

                Run(action.Node);
            };

            Find.WindowStack.Add(box);
        }

        /// <summary>
        /// Queues an action to run once the frame is finished.
        ///
        /// <b>Nothing may run during the draw, and that was the bug.</b> The first version ran the action
        /// immediately from the click, which meant closing this window and mutating the recents list in the
        /// middle of iterating the very collections being drawn -- and several developer actions open windows or
        /// change the map, all from inside our own <c>DoWindowContents</c>. Deferring to the end of the frame
        /// makes the draw a pure read, which is the only version of this that is safe to reason about.
        /// </summary>
        private void Run(DebugActionNode node)
        {
            if (node != null)
                pending = node;
        }

        /// <summary>Applies whatever the frame asked for, after every collection has been left alone.</summary>
        private void Flush()
        {
            if (navigateTabSet)
            {
                navigateTabSet = false;
                tab = navigateTab;
            }

            if (navigateOut)
            {
                navigateOut = false;
                inside = null;
                highlighted = 0;
                scroll = Vector2.zero;
            }

            if (navigateInto != null)
            {
                inside = navigateInto;
                navigateInto = null;
                highlighted = 0;
                scroll = Vector2.zero;
            }

            if (pending == null)
                return;

            DebugActionNode node = pending;
            pending = null;

            Remember(node);

            // Closed before the action, because several of them hand control to a map tool and a panel left over
            // the map is then in the way of the click that tool is waiting for.
            Close(false);

            UIGuard.Try("DevTools.Run", () => node.Enter(null),
                "That developer action failed. The fault is in the action rather than in this window.");
        }

        private static void Remember(DebugActionNode node)
        {
            Recent.Remove(node);
            Recent.Insert(0, node);

            while (Recent.Count > MaxRecent)
                Recent.RemoveAt(Recent.Count - 1);
        }

        private void DrawFooter(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextDisabled;

            Widgets.Label(rect, results.Count + " of " + DevActionIndex.Actions.Count + " actions");

            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(rect, "Up and Down move, Enter runs, Escape backs out");

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// A node's name, never null.
        ///
        /// <c>label</c> is only set on nodes authored with a literal; anything using a <c>labelGetter</c> leaves
        /// it null and resolves through <c>LabelNow</c>, which itself falls back to the field. Reading either one
        /// alone gives a null for half the tree.
        /// </summary>
        private static string Name(DebugActionNode node)
        {
            if (node == null)
                return string.Empty;

            string name = UIGuard.Try("DevTools.Label", () => node.LabelNow, null, null);

            return name ?? node.label ?? string.Empty;
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
