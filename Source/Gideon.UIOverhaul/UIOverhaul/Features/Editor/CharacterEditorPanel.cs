using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// The character editor's contents, independent of what is holding them.
    ///
    /// <b>Split out from the window when the editor became a tab as well as a dialog.</b> There are now two hosts:
    /// a dialog opened on one pawn from their bio panel or their corpse, and a main tab opened on nobody in
    /// particular. They differ in two things -- whether there is a roster column, and how the host sizes itself --
    /// and in nothing else, so the body is here and each host is a dozen lines.
    ///
    /// <b>One instance per host, with its own change log.</b> Static state would have let the two hosts fight over
    /// which pawn was open and which edits belonged to whom.
    ///
    /// <b>Every change applies at once and every change is listed.</b> There is no Apply button, because a form
    /// that batches edits invites somebody to lose ten of them to a stray Escape. See <see cref="EditorChanges"/>
    /// for why Revert all is an undo log rather than the snapshot the proposal described.
    ///
    /// <b>It warns rather than blocks.</b> A fifth trait, a conflicting one, a skill a backstory disables, a gene
    /// the xenotype does not carry: it says what the consequence is and then does it. This is the tool for doing
    /// things the game would not, and a tool that silently declines is worse than no tool.
    ///
    /// <b>Where the game has an operation, this calls it rather than writing the field.</b> Traits through
    /// <c>TraitSet.GainTrait</c>, apparel through <c>Pawn_ApparelTracker.Wear</c>, hediffs through
    /// <c>HealthUtility</c>, relations through <c>AddDirectRelation</c>. Setting a field by hand is how you get a
    /// pawn that looks right and behaves like a save-file corruption three hours later.
    /// </summary>
    internal sealed class CharacterEditorPanel
    {
        private const float RailWidth = 178f;

        private const float TitleHeight = 30f;

        private const float FooterHeight = 40f;

        private const float Gap = 10f;

        private const float RailRowHeight = 26f;

        private const float RailGroupHeight = 24f;

        /// <summary>Below this the surface has nothing but ellipses in it, so the render column is dropped.</summary>
        private const float SurfaceFloor = 260f;

        private readonly EditorContext context = new EditorContext();

        /// <summary>Whether this host lists the colony down the left. The tab, and the starting characters page.</summary>
        private readonly bool roster;

        /// <summary>
        /// Where the roster column gets its people, when it is not the loaded maps.
        ///
        /// Null for the tab, which asks the colony. Supplied by the starting characters page, whose pawns are on
        /// no map yet. Asked every frame rather than snapshotted, because Randomize on that page swaps a pawn for
        /// a new object.
        /// </summary>
        private readonly Func<List<Pawn>> source;

        private EditorPanel panel;

        private Vector2 railScroll;
        private bool railDragging;
        private float railDragOffset;

        private Vector2 surfaceScroll;

        /// <summary>Height the surface came to last frame. Remembered rather than predicted.</summary>
        private float measured;

        private EditorPanel measuredFor;

        private Pawn measuredPawn;

        internal CharacterEditorPanel(Pawn pawn, bool roster, Func<List<Pawn>> source = null)
        {
            this.roster = roster;
            this.source = source;

            context.Changes = new EditorChanges();
            context.Palette = UIColorPaletteDef.Active;

            Switch(pawn);

            EditorRender.Reset();
        }

        internal Pawn Pawn
        {
            get { return context.Pawn; }
        }

        /// <summary>The width this panel would like, so a host can size itself before drawing.</summary>
        internal float WantedWidth
        {
            get
            {
                float wanted = RailWidth + Gap + 520f + Gap + EditorRender.ColumnWidth + 24f;

                if (roster)
                    wanted += EditorRoster.ColumnWidth + Gap;

                return Mathf.Min(wanted, UI.screenWidth - 40f);
            }
        }

        /// <summary>
        /// Moves the editor to a different pawn.
        ///
        /// <b>The change log is kept.</b> Its entries are closures over the pawns they were made against, so an
        /// edit to somebody the roster has since moved away from still reverts correctly -- and the footer counting
        /// edits across two people is the truth rather than a bug.
        /// </summary>
        internal void Switch(Pawn pawn)
        {
            context.Pawn = pawn;

            panel = EditorPanels.FirstFor(context);
            measuredFor = panel;
            measured = 0f;
            measuredPawn = pawn;
            surfaceScroll = Vector2.zero;
        }

        /// <summary>
        /// Moves off a pawn the supplied roster no longer holds, onto whoever is first.
        ///
        /// <b>For the starting characters page and its Randomize button.</b> That button does not edit a pawn, it
        /// generates a replacement and drops the old object, so an editor left pointing at the old one would go on
        /// showing and writing to somebody who is no longer in the game -- silently, since the discarded pawn is a
        /// perfectly valid object that simply nothing reads any more.
        ///
        /// Only for hosts that supply a source. A colony roster can legitimately lose its subject too -- somebody
        /// dies while the tab is open -- and that case is already answered by the empty state, which says who is
        /// gone instead of quietly moving on to a different person.
        /// </summary>
        private void Rejoin()
        {
            if (source == null)
                return;

            UIGuard.Try("Editor.Rejoin", () =>
            {
                List<Pawn> supplied = source();

                if (supplied == null || supplied.Count == 0)
                {
                    if (context.Pawn != null)
                        Switch(null);

                    return;
                }

                if (context.Pawn != null && supplied.Contains(context.Pawn))
                    return;

                Switch(supplied[0]);
            }, null);
        }

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        /// <summary>Draws the whole panel. Returns false when the host should close.</summary>
        internal bool Draw(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            context.Palette = palette;

            // Destroyed, and not held by a corpse. The second half is the whole test: a dead pawn lives inside
            // its Corpse's container, and dropping one for reporting itself destroyed empties the window at the
            // exact moment it is most wanted -- Bring somebody back opened an editor saying there was nobody to
            // edit. Whatever the flag says, a pawn something is still holding is a pawn we can work on, and
            // resurrection is the one thing only this window can do.
            if (context.Pawn != null && context.Pawn.Destroyed && context.Pawn.Corpse == null)
                context.Pawn = null;

            // The pawn can stop being the thing this window opened on: resurrected, or a corpse cremated while it
            // sat open. Falling back rather than drawing a panel that no longer applies.
            if (context.Pawn != null && !EditorPanels.Applies(panel, context))
            {
                panel = EditorPanels.FirstFor(context);
                measured = 0f;
            }

            Title(new Rect(inRect.x, inRect.y, inRect.width - 28f, TitleHeight), palette);

            float top = inRect.y + TitleHeight + 6f;
            float bottom = inRect.yMax - FooterHeight;

            Rect body = new Rect(inRect.x, top, inRect.width, Mathf.Max(0f, bottom - top - Gap));

            if (roster)
            {
                // Checked before the column is drawn rather than after, so a pawn replaced out from under the
                // editor is never the one the panels spend this frame reading.
                Rejoin();

                EditorRoster.Draw(new Rect(body.x, body.y, EditorRoster.ColumnWidth, body.height),
                    context.Pawn, palette, Switch, Templates, source);

                body = new Rect(body.x + EditorRoster.ColumnWidth + Gap, body.y,
                    Mathf.Max(0f, body.width - EditorRoster.ColumnWidth - Gap), body.height);
            }

            if (context.Pawn == null)
            {
                Nobody(body, palette);

                Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);

                return true;
            }

            Rail(new Rect(body.x, body.y, RailWidth, body.height), palette);

            Rect right = new Rect(body.x + RailWidth + Gap, body.y,
                Mathf.Max(0f, body.width - RailWidth - Gap), body.height);

            if (EditorPanels.NeedsRender(panel)
                && right.width > EditorRender.ColumnWidth + SurfaceFloor)
            {
                Rect column = new Rect(right.xMax - EditorRender.ColumnWidth, right.y, EditorRender.ColumnWidth,
                    right.height);

                EditorRender.Draw(column, context.Pawn, context.Dead ? "As they died" : "Live", palette);

                right = new Rect(right.x, right.y, right.width - EditorRender.ColumnWidth - Gap, right.height);
            }

            Surface(right, palette);

            Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);

            return true;
        }

        /// <summary>What the tab shows before anybody has been picked.</summary>
        private void Nobody(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                Widgets.Label(rect, roster
                    ? "Pick somebody on the left."
                    : "There is nobody to edit.");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Chrome
        // ---------------------------------------------------------------------------------------

        private void Title(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleLeft;

                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                // Through WidthOf rather than CalcSize, because that is the figure the drawing side judges it
                // against: UIRichText holds thirteen pixels back for an ellipsis, so a rect sized to the bare
                // text ellipses however much room the window has. This came out as "Character edi...".
                float used = UIRichText.WidthOf("Character editor") + 10f;

                UIRichText.Label(new Rect(rect.x, rect.y, Mathf.Max(20f, used), rect.height),
                    "Character editor");

                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                UIRichText.Label(new Rect(rect.x + used, rect.y, Mathf.Max(20f, rect.width - used), rect.height),
                    Who() ?? string.Empty);
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
        /// Who is being edited, in the words the rest of the mod uses for them.
        ///
        /// Drawn at white rather than a dim grey, because the qualifier carries the faction's own colour tag and
        /// IMGUI multiplies a tag by GUI.color -- the same trap the inspect pane header hit.
        /// </summary>
        private string Who()
        {
            return UIGuard.Try<string>("Editor.Who", () =>
            {
                Pawn pawn = context.Pawn;

                if (pawn == null)
                    return null;

                string name = pawn.LabelShortCap;

                if (!context.Dead)
                    return name + ", " + (InspectBodies.Qualifier(pawn) ?? string.Empty);

                Corpse corpse = pawn.Corpse;

                string since = corpse != null
                    ? "dead " + Corpses.CorpseFacts.AgeOf(corpse).ToStringTicksToPeriodVague()
                    : "dead";

                return name + ", " + since;
            }, null, null);
        }

        /// <summary>
        /// The rail.
        ///
        /// <b>Group headings rather than eleven flat entries,</b> because the two halves are edited for different
        /// reasons: the top is the pawn as a character, the bottom is what happens to be true this hour.
        ///
        /// A dot marks the panels that move the render, so it is obvious before clicking that Appearance changes
        /// the picture and Skills does not.
        /// </summary>
        /// <summary>
        /// One row per editor panel, grouped, skipping the ones that do not apply to this pawn.
        ///
        /// <b>Resurrect is coloured as danger rather than filled differently,</b> so it reads as the one
        /// destructive choice in the list while still being a row like any other.
        /// </summary>
        private void Rail(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            List<UIRailElement> elements = new List<UIRailElement>();

            for (int i = 0; i < EditorPanels.All.Length; i++)
            {
                EditorPanel which = EditorPanels.All[i];

                if (!EditorPanels.Applies(which, context))
                    continue;

                string group = EditorPanels.GroupOf(which, context.Dead);

                if (group != null)
                {
                    elements.Add(new UIRailSectionHeaderControl(group)
                    {
                        Rise = RailGroupHeight,
                        Color = palette.TextDisabled
                    });
                }

                string count = EditorPanels.CountOf(which, context);

                elements.Add(new UIRailClickableEntry(which.ToString(), EditorPanels.LabelOf(which))
                {
                    Rise = RailRowHeight,
                    Trailing = count.NullOrEmpty() ? null : count,
                    TextColor = which == EditorPanel.Resurrect ? palette.Danger : (Color?) null
                });
            }

            string picked = UIRailControl.Draw(rect.ContractedBy(6f), elements, panel.ToString(),
                ref railScroll, ref railDragging, ref railDragOffset, palette, false);

            if (picked == null)
                return;

            foreach (EditorPanel candidate in (EditorPanel[]) Enum.GetValues(typeof(EditorPanel)))
            {
                if (candidate.ToString() == picked)
                {
                    panel = candidate;

                    break;
                }
            }
        }

        /// <summary>
        /// The panel itself, in a scroll view that remembers rather than predicts its content height.
        ///
        /// A formula for how tall a panel will be goes wrong the first time a block is added, and the failure is
        /// silent: the view believes everything fits, does not scroll, and clips the bottom with no way to reach
        /// it. Remembering where the last draw ended costs one frame of lag after the content changes size, which
        /// is invisible.
        /// </summary>
        private void Surface(Rect rect, UIColorPaletteDef palette)
        {
            if (rect.width <= 40f)
                return;

            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Rect inner = rect.ContractedBy(10f);

            if (measuredFor != panel || measuredPawn != context.Pawn)
            {
                measuredFor = panel;
                measuredPawn = context.Pawn;
                measured = 0f;
            }

            bool scrolls = measured > inner.height;

            Rect view = new Rect(0f, 0f, inner.width - (scrolls ? 18f : 0f),
                measured > 0f ? measured : inner.height);

            Widgets.BeginScrollView(inner, ref surfaceScroll, view);

            measured = EditorPanels.Draw(panel, new Rect(0f, 0f, view.width, view.height), context) + 10f;

            Widgets.EndScrollView();
        }

        /// <summary>
        /// What has changed, and the buttons that matter.
        ///
        /// <b>No separate Close.</b> The proposal drew Revert all, Close and Done; with every change applying
        /// immediately, Close and Done do the same thing, and two buttons that cannot be told apart is worse than
        /// one that can. The tab has no Done at all, since a tab is closed the way every other tab is.
        /// </summary>
        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            GUI.color = palette.Border;

            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);

            GUI.color = Color.white;

            bool permanent = context.Changes.AnyPermanent;

            float right = rect.xMax;

            if (Closer != null)
            {
                if (TabParts.Button(new Rect(right - 100f, rect.y + 8f, 100f, 28f), "Done", palette, true, true))
                    Closer();

                right -= 116f;
            }

            bool anything = context.Changes.Count > 0;

            if (TabParts.Button(new Rect(right - 110f, rect.y + 8f, 110f, 28f), "Revert all", palette, anything,
                    false,
                    anything ? "Puts every change back, newest first." : "Nothing has changed yet."))
                Revert();

            right -= 126f;

            if (TabParts.Button(new Rect(right - 110f, rect.y + 8f, 110f, 28f), "Templates", palette, true, false,
                    "Save this character to a file, or apply a saved one. Files can be copied to another save or "
                    + "sent to somebody else."))
                Templates();

            right -= 120f;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = permanent ? palette.Danger : palette.TextSecondary;

                UIRichText.Label(new Rect(rect.x, rect.y + 4f, Mathf.Max(20f, right - rect.x), rect.height),
                    permanent
                        ? context.Changes.Summary() + " Some of it cannot be taken back."
                        : context.Changes.Summary());
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>What Done does, or null in a host that has no Done. Set by the host.</summary>
        internal System.Action Closer;

        private void Templates()
        {
            Dialog_CharacterTemplates.Open(context.Pawn, context.Changes, Switch);
        }

        private void Revert()
        {
            context.Changes.RevertAll();

            EditorParts.Redraw(context.Pawn);
        }

        /// <summary>
        /// Called by the host on the way out.
        ///
        /// The pawn keeps whatever was done to them, so the caches have to be right after the window closes as
        /// well as during it. A window closed on an edited hair leaves the old hair in every list in the game.
        /// </summary>
        internal void Closed()
        {
            UIGuard.Try("Editor.Close", () => EditorParts.Redraw(context.Pawn), null);
        }
    }
}
