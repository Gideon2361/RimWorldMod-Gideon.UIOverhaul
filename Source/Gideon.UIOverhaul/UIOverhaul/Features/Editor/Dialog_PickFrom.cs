using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// One row of a picker: what it is, what taking it would do, and what to call when it is taken.
    ///
    /// <b>The note is the whole point of this being a window.</b> Choosing a backstory blind is what makes
    /// vanilla's own character creation a wiki tab, so a row can say what it grants -- "Shooting +4, Melee +2,
    /// no violence" -- and a float menu could show none of that.
    /// </summary>
    internal sealed class EditorOption
    {
        internal string Label;

        /// <summary>What taking this would do, shown right-aligned on the row.</summary>
        internal string Note;

        internal string Tooltip;

        /// <summary>The one that is already set. Ringed rather than hidden.</summary>
        internal bool Current;

        /// <summary>
        /// Something is wrong with this choice, but it is still offered.
        ///
        /// A backstory that does not suit the pawn's age, a gene the xenotype does not carry, a trait that
        /// conflicts with one they have. Marked and takeable, never absent: undoing exactly that is a thing
        /// people open an editor for.
        /// </summary>
        internal string Marked;

        internal Action Chosen;
    }

    /// <summary>
    /// The editor's one picker, used by every panel that chooses something from a list.
    ///
    /// <b>A window rather than a float menu, and one window rather than eleven.</b> Hair, heads, body types,
    /// backstories, traits, genes, hediffs, body parts, apparel, weapons and relations are all "pick one of these
    /// several hundred things", and every one of them needs a search box and a line of consequence per row. A
    /// float menu can carry neither, and eleven bespoke pickers would be eleven places for the same bug.
    ///
    /// <b>Nothing is filtered out for being a bad idea.</b> The <see cref="EditorOption.Marked"/> line says what
    /// is wrong and the row still works, which is the editor's posture everywhere: warn, do not block.
    /// </summary>
    internal sealed class Dialog_PickFrom : Window
    {
        private const float HeaderHeight = 28f;

        private const float RowHeight = 30f;

        private const float FooterHeight = 34f;

        private const float Pad = 8f;

        private readonly string heading;

        private readonly List<EditorOption> options;

        private readonly List<EditorOption> matching = new List<EditorOption>();

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private Vector2 scroll;

        private Dialog_PickFrom(string heading, List<EditorOption> options, string placeholder)
        {
            this.heading = heading;
            this.options = options;

            search.Placeholder = placeholder ?? "Search";

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = false;
            drawShadow = true;
        }

        internal static void Open(string heading, List<EditorOption> options, string placeholder = null)
        {
            if (options == null || options.Count == 0)
            {
                EditorParts.Warn("There is nothing to choose from here.");

                return;
            }

            Find.WindowStack.Add(new Dialog_PickFrom(heading, options, placeholder));
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(420f, 520f); }
        }

        /// <summary>
        /// At the cursor and clamped inside the screen.
        ///
        /// The placement rule a float menu follows, for the same reason: this window is the answer to a control
        /// that was just clicked and has to appear next to it.
        /// </summary>
        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            Vector2 mouse = UI.MousePositionOnUIInverted;

            windowRect = new Rect(
                Mathf.Clamp(mouse.x, 0f, UI.screenWidth - size.x),
                Mathf.Clamp(mouse.y, 0f, UI.screenHeight - size.y),
                size.x, size.y);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Editor.Picker", inRect, () => Contents(inRect),
                "The picker failed to draw. Nothing has been changed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight), heading);

                Rect box = new Rect(inRect.x, inRect.y + HeaderHeight, inRect.width, 26f);

                search.Draw(box, palette);

                Gather();

                Rect list = new Rect(inRect.x, box.yMax + Pad, inRect.width,
                    Mathf.Max(0f, inRect.height - HeaderHeight - 26f - FooterHeight - Pad * 2f));

                Rect view = new Rect(0f, 0f, list.width - 18f, matching.Count * RowHeight + 4f);

                Widgets.BeginScrollView(list, ref scroll, view);

                float y = 0f;

                for (int i = 0; i < matching.Count; i++)
                {
                    Row(new Rect(0f, y, view.width, RowHeight - 2f), matching[i], palette);

                    y += RowHeight;
                }

                Widgets.EndScrollView();

                if (matching.Count == 0)
                {
                    GUI.color = palette.TextDisabled;
                    Text.Font = GameFont.Tiny;

                    Widgets.Label(new Rect(list.x + 4f, list.y + 4f, list.width - 8f, 40f),
                        "Nothing here matches that.");

                    Text.Font = GameFont.Small;
                    GUI.color = palette.TextPrimary;
                }

                if (TabParts.Button(new Rect(inRect.xMax - 90f, inRect.yMax - FooterHeight, 90f, 28f), "Cancel",
                        palette))
                    Close();
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private void Gather()
        {
            matching.Clear();

            for (int i = 0; i < options.Count; i++)
            {
                EditorOption option = options[i];

                if (!search.IsEmpty && !search.Matches(option.Label) && !search.Matches(option.Note))
                    continue;

                matching.Add(option);
            }
        }

        private void Row(Rect rect, EditorOption option, UIColorPaletteDef palette)
        {
            // Composited rather than translucent, for the reason on UIElementPainter.Composite: an outline is
            // two fills, and an overlay handed in as the inside lands on the border colour.
            UIElementPainter.OutlineRounded(rect, option.Current ? palette.Accent : palette.Border,
                option.Current
                    ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                    : Mouse.IsOver(rect)
                        ? palette.SurfaceRaised
                        : palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            float noteWidth = 0f;

            try
            {
                Text.WordWrap = false;

                string note = option.Marked ?? option.Note;

                if (!note.NullOrEmpty())
                {
                    Text.Font = GameFont.Tiny;

                    noteWidth = Mathf.Min(rect.width * 0.55f, UIRichText.WidthOf(note) + 8f);

                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = option.Marked != null ? palette.Warning : palette.TextSecondary;

                    UIRichText.Label(new Rect(rect.xMax - noteWidth - 6f, rect.y, noteWidth, rect.height), note);
                }

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                UIRichText.Label(new Rect(rect.x + 6f, rect.y,
                    Mathf.Max(10f, rect.width - noteWidth - 16f), rect.height), option.Label ?? "?");
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            string tip = option.Tooltip;

            if (option.Marked != null)
                tip = tip.NullOrEmpty() ? option.Marked : option.Marked + "\n\n" + tip;

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tip);

            if (!Widgets.ButtonInvisible(rect))
                return;

            // Closed before the callback runs. A callback that opens another window -- picking a body part after
            // picking a hediff -- would otherwise open it behind this one.
            Close();

            if (option.Chosen == null)
                return;

            UIGuard.Try("Editor.Chosen", option.Chosen, "That change could not be made.");
        }
    }
}
