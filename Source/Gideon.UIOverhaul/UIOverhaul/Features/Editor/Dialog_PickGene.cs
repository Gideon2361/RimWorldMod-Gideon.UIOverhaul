using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// One gene a picker is offering, and what to do when it is taken.
    ///
    /// No equivalent of <see cref="EditorOption.Marked"/>, because neither caller has anything to mark: both
    /// already exclude the genes the pawn has, and nothing else about a gene makes it a bad choice that the
    /// tile's own description does not say. <c>GeneUIUtility.DrawGeneDef</c> takes an extra tooltip for exactly
    /// that purpose if one is ever needed -- see the call in <see cref="Dialog_PickGene"/>.
    /// </summary>
    internal sealed class GeneChoice
    {
        internal GeneDef Def;

        internal Action Chosen;
    }

    /// <summary>
    /// The gene picker: a grid of the game's own gene tiles.
    ///
    /// <b>Genes are recognised by sight, and a list throws that away.</b> Aaron made this point on 2026-08-23
    /// about the Genes panel, against a screenshot of fourteen rows each reading "endogene", and again the same
    /// day about this picker -- a column of identical grey rows where every entry has an icon the player already
    /// knows from the gene assembler. <see cref="Dialog_PickFrom"/> is right for backstories and traits, which
    /// are words; it is wrong for the two lists whose entries are pictures.
    ///
    /// <b>Through <c>GeneUIUtility.DrawGeneDef</c>, the same call the gene assembler makes.</b> So a gene looks
    /// here exactly as it does in the two screens a player already knows it from, including the backgrounds that
    /// separate an endogene from a xenogene and the biostats along the bottom. The tooltip -- label, full
    /// description, and the warning line if there is one -- comes with it.
    ///
    /// <b>Drawn unclickable and clicked by us,</b> for the reason <see cref="EditorGeneTiles"/> records: vanilla's
    /// tile takes the whole rect for its own info card, and here the whole rect has to mean "take this one".
    /// </summary>
    internal sealed class Dialog_PickGene : Window
    {
        private const float HeaderHeight = 28f;

        private const float FooterHeight = 34f;

        private const float Pad = 8f;

        private const float Gap = 6f;

        /// <summary>Vanilla's own tile, so the grid lines up with the gene assembler's.</summary>
        private static readonly Vector2 TileSize = GeneCreationDialogBase.GeneSize;

        private readonly string heading;

        private readonly List<GeneChoice> choices;

        private readonly List<GeneChoice> matching = new List<GeneChoice>();

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private Vector2 scroll;

        private Dialog_PickGene(string heading, List<GeneChoice> choices, string placeholder)
        {
            this.heading = heading;
            this.choices = choices;

            search.Placeholder = placeholder ?? "Search";

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = false;
            drawShadow = true;
        }

        internal static void Open(string heading, List<GeneChoice> choices, string placeholder = null)
        {
            if (choices == null || choices.Count == 0)
            {
                EditorParts.Warn("There is nothing to choose from here.");

                return;
            }

            Find.WindowStack.Add(new Dialog_PickGene(heading, choices, placeholder));
        }

        /// <summary>
        /// Four tiles across, which is what decides the width.
        ///
        /// Wider than the row picker because a tile is 87 wide where a row was a line of text. Four is the gene
        /// assembler's own density at this width and keeps a full alphabet of hair colours to two screens of
        /// scrolling rather than five.
        /// </summary>
        public override Vector2 InitialSize
        {
            get { return new Vector2(4f * (TileSize.x + Gap) - Gap + Pad * 2f + 18f, 560f); }
        }

        /// <summary>At the cursor and clamped on screen, exactly as the row picker is and for the same reason.</summary>
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
            UIGuardedPanel.Draw("Editor.GenePicker", inRect, () => Contents(inRect),
                "The gene picker failed to draw. Nothing has been changed.");
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

                Rect grid = new Rect(inRect.x, box.yMax + Pad, inRect.width,
                    Mathf.Max(0f, inRect.height - HeaderHeight - 26f - FooterHeight - Pad * 2f));

                Grid(grid, palette);

                if (matching.Count == 0)
                {
                    GUI.color = palette.TextDisabled;
                    Text.Font = GameFont.Tiny;

                    Widgets.Label(new Rect(grid.x + 4f, grid.y + 4f, grid.width - 8f, 40f),
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

        private void Grid(Rect grid, UIColorPaletteDef palette)
        {
            int perRow = Mathf.Max(1, Mathf.FloorToInt((grid.width - 18f + Gap) / (TileSize.x + Gap)));
            int rows = Mathf.CeilToInt(matching.Count / (float) perRow);

            Rect view = new Rect(0f, 0f, grid.width - 18f, rows * (TileSize.y + Gap));

            Widgets.BeginScrollView(grid, ref scroll, view);

            for (int i = 0; i < matching.Count; i++)
            {
                Rect tile = new Rect(
                    i % perRow * (TileSize.x + Gap),
                    i / perRow * (TileSize.y + Gap),
                    TileSize.x, TileSize.y);

                // Only what is on screen. A full gene list with every mod loaded runs to several hundred, and
                // each tile is an icon draw plus a tooltip region.
                if (tile.yMax >= scroll.y && tile.y <= scroll.y + grid.height)
                    Tile(tile, matching[i], palette);
            }

            Widgets.EndScrollView();
        }

        private void Tile(Rect rect, GeneChoice choice, UIColorPaletteDef palette)
        {
            // Xenogene, because that is what both callers add. The background a tile carries is the game's way of
            // saying which kind a gene is, so showing one kind and adding another would be a picture that lies.
            //
            // The null is the extra tooltip, which is where a per-gene warning would go if one were ever wanted.
            GeneUIUtility.DrawGeneDef(choice.Def, rect, GeneType.Xenogene, null, true, false);

            if (Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);

            MouseoverSounds.DoRegion(rect);

            if (!Widgets.ButtonInvisible(rect))
                return;

            SoundDefOf.Click.PlayOneShotOnCamera();

            // Closed before the gene is added, so the window is gone by the time anything it caused draws --
            // a warning about the change lands in front of the editor rather than behind this.
            Close();

            if (choice.Chosen != null)
                choice.Chosen();
        }

        private void Gather()
        {
            matching.Clear();

            for (int i = 0; i < choices.Count; i++)
            {
                GeneChoice choice = choices[i];

                if (choice == null || choice.Def == null)
                    continue;

                if (!search.IsEmpty && !search.Matches(choice.Def.LabelCap))
                    continue;

                matching.Add(choice);
            }
        }
    }
}
