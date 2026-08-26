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

        /// <summary>
        /// The def to draw a picture of, when this option is one you choose by looking rather than by reading.
        ///
        /// <b>A def rather than a texture,</b> because <c>Widgets.DefIcon</c> is what knows how to turn one into a
        /// picture -- including a hair or beard, which has no icon of its own and is drawn from its south-facing
        /// graphic. Handing it the def means a style added by another mod draws exactly as it does in the game's
        /// own styling station.
        /// </summary>
        internal Def Icon;

        /// <summary>What colour to draw <see cref="Icon"/> in. Hair and beards take the pawn's own hair colour.</summary>
        internal Color IconColor = Color.white;
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

        /// <summary>How wide a tile is, and how tall. The extra height is the name under the picture.</summary>
        private const float TileWidth = 84f;

        private const float TileHeight = 104f;

        private const float TileGap = 6f;

        /// <summary>
        /// Whether this picker shows pictures in a grid instead of names in a list.
        ///
        /// <b>Decided by the options rather than by a flag the caller passes.</b> A picker draws tiles exactly
        /// when every option has something to draw, which is a question the options can answer themselves -- and
        /// it means a call site cannot ask for tiles and then supply half of them.
        /// </summary>
        private readonly bool tiled;

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

            tiled = Drawable(options);

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

        /// <summary>Whether every option carries a picture, which is what makes this a grid.</summary>
        private static bool Drawable(List<EditorOption> options)
        {
            if (options == null || options.Count == 0)
                return false;

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i]?.Icon == null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Wider and taller when it is showing pictures.
        ///
        /// <b>Six tiles to a row.</b> Two hundred hairstyles at one name per line is a list nobody reads to the
        /// end of; the same two hundred as pictures is something you scan. The window has to be wide enough that
        /// scanning is what it feels like, which a 420 pixel column is not.
        /// </summary>
        public override Vector2 InitialSize
        {
            get { return tiled ? new Vector2(640f, 620f) : new Vector2(420f, 520f); }
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

                if (tiled)
                    Tiles(list, palette);
                else
                    Rows(list, palette);

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

        /// <summary>The list form: one option per line, with its consequence beside it.</summary>
        private void Rows(Rect list, UIColorPaletteDef palette)
        {
            Rect view = new Rect(0f, 0f, list.width - 18f, matching.Count * RowHeight + 4f);

            Widgets.BeginScrollView(list, ref scroll, view);

            float y = 0f;

            for (int i = 0; i < matching.Count; i++)
            {
                Row(new Rect(0f, y, view.width, RowHeight - 2f), matching[i], palette);

                y += RowHeight;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// The grid form: a picture per option, with its name under it.
        ///
        /// <b>The name stays.</b> Vanilla's styling station shows pictures alone and puts the name in a tooltip,
        /// which is fine when you are browsing and useless when somebody has told you to pick "Bowlcut". The
        /// search box above filters on that name, so it has to be readable for the search to make sense.
        /// </summary>
        private void Tiles(Rect list, UIColorPaletteDef palette)
        {
            int columns = Mathf.Max(1, Mathf.FloorToInt((list.width - 18f + TileGap) / (TileWidth + TileGap)));
            int rows = Mathf.CeilToInt(matching.Count / (float) columns);

            Rect view = new Rect(0f, 0f, list.width - 18f, rows * (TileHeight + TileGap) + 4f);

            Widgets.BeginScrollView(list, ref scroll, view);

            // Only what is on screen. A colony with a few style mods installed has several hundred of these, and
            // every one drawn is a material fetched and a label measured.
            float first = scroll.y - TileHeight - TileGap;
            float last = scroll.y + list.height;

            for (int i = 0; i < matching.Count; i++)
            {
                float y = i / columns * (TileHeight + TileGap);

                if (y < first || y > last)
                    continue;

                Rect cell = new Rect(i % columns * (TileWidth + TileGap), y, TileWidth, TileHeight);

                Tile(cell, matching[i], palette);
            }

            Widgets.EndScrollView();
        }

        /// <summary>One tile: the picture, the name, and the ring that says which one is already set.</summary>
        private void Tile(Rect rect, EditorOption option, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.OutlineRounded(rect, option.Current ? palette.Accent : palette.Border,
                option.Current
                    ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                    : over
                        ? palette.SurfaceRaised
                        : palette.PanelBackground);

            Rect picture = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 26f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                // Vanilla's own icon drawer, which is how a hair or beard becomes a picture at all: they carry no
                // icon and are drawn from their south-facing graphic, and reproducing that here would be a second
                // opinion about shaders and mask textures.
                GUI.color = option.IconColor;

                UIGuard.Try("Editor.PickerIcon", () => Widgets.DefIcon(picture, option.Icon, null, 1.25f));

                GUI.color = option.Marked.NullOrEmpty() ? palette.TextPrimary : palette.Warning;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                Text.WordWrap = false;

                Widgets.LabelEllipses(new Rect(rect.x + 2f, picture.yMax, rect.width - 4f, 20f), option.Label);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (over)
            {
                string tip = option.Label;

                if (!option.Note.NullOrEmpty())
                    tip += "\n\n" + option.Note;

                if (!option.Marked.NullOrEmpty())
                    tip += "\n\n" + option.Marked;

                if (!option.Tooltip.NullOrEmpty())
                    tip += "\n\n" + option.Tooltip;

                TooltipHandler.TipRegion(rect, (TipSignal) tip);
            }

            if (!Widgets.ButtonInvisible(rect))
                return;

            Take(option);
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

            Take(option);
        }

        /// <summary>Commits a choice, whether it came from a row or from a tile.</summary>
        private void Take(EditorOption option)
        {
            // Closed before the callback runs. A callback that opens another window -- picking a body part after
            // picking a hediff -- would otherwise open it behind this one.
            Close();

            if (option?.Chosen == null)
                return;

            UIGuard.Try("Editor.Chosen", option.Chosen, "That change could not be made.");
        }
    }
}
