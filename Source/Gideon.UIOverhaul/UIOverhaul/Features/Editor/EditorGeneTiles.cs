using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Genes as tiles, drawn by the game's own control.
    ///
    /// <b>Tiles because a gene has an icon and rows throw it away.</b> Asked for 2026-08-23, against a screenshot
    /// of fourteen rows reading "endogene" fourteen times: an archlich's gene set is the one list in this window
    /// where the entries are recognised by sight, and a column of identical grey rows is the one shape that
    /// guarantees they cannot be.
    ///
    /// <b>Through <c>GeneUIUtility.DrawGene</c> rather than a tile of ours.</b> That is the same call the gene
    /// assembler and the xenotype editor make, so a gene looks here exactly as it does in the two screens a
    /// player already knows it from -- including the three background textures that distinguish an endogene from a
    /// xenogene from an archite gene, and the dimming that marks one as overridden. Drawing our own would be a
    /// second vocabulary for a fact the player has already learnt, and it would stop matching the moment Ludeon
    /// added a fourth background.
    ///
    /// <b>Two hit targets per tile, and this owns both.</b> Vanilla's tile takes the whole rect for its info card,
    /// which would fight a remove button drawn on top of it: one click would fire both. So the tile is drawn with
    /// <c>clickable: false</c> and the clicks are handled here -- the corner cross removes, the rest opens the
    /// info card. The cross is tested first and the card is refused while the pointer is over it, since one rect
    /// cannot have a hole cut in it.
    /// </summary>
    internal static class EditorGeneTiles
    {
        /// <summary>Vanilla's own tile size, so a row of these lines up with the gene assembler's.</summary>
        private static readonly Vector2 Size = GeneCreationDialogBase.GeneSize;

        private const float Gap = 6f;

        /// <summary>Side of the remove cross in the tile's corner.</summary>
        private const float CrossSize = 14f;

        /// <summary>
        /// Lays out every gene as a tile and returns the y the next block starts at.
        ///
        /// <paramref name="remove"/> is called with the gene whose cross was pressed. Null leaves the crosses off
        /// entirely, for a caller that only wants to show them.
        /// </summary>
        internal static float Draw(Rect view, float y, List<Gene> genes, Pawn pawn, UIColorPaletteDef palette,
            Action<Gene> remove)
        {
            if (genes == null || genes.Count == 0)
                return EditorParts.Note(view, y, "None.", palette);

            int perRow = Mathf.Max(1, Mathf.FloorToInt((view.width + Gap) / (Size.x + Gap)));

            float x = view.x;
            float rowY = y;

            for (int i = 0; i < genes.Count; i++)
            {
                Gene gene = genes[i];

                if (gene == null || gene.def == null)
                    continue;

                // Wrapped in front of the tile rather than behind it. Measuring after drawing is what left the
                // inspect pane's chips overhanging their column four separate times.
                if (i > 0 && i % perRow == 0)
                {
                    x = view.x;
                    rowY += Size.y + Gap;
                }

                Tile(new Rect(x, rowY, Size.x, Size.y), gene, pawn, palette, remove);

                x += Size.x + Gap;
            }

            return rowY + Size.y + Gap;
        }

        private static void Tile(Rect tile, Gene gene, Pawn pawn, UIColorPaletteDef palette, Action<Gene> remove)
        {
            bool xeno = UIGuard.Try("Editor.TileIsXeno",
                () => pawn.genes != null && pawn.genes.IsXenogene(gene), false, null);

            UIGuard.Try("Editor.GeneTile", () =>
                    // clickable: false, so the info card is not opened from inside the tile as well as from here.
                    GeneUIUtility.DrawGene(gene, tile, xeno ? GeneType.Xenogene : GeneType.Endogene, true, false),
                null);

            bool over = Mouse.IsOver(tile);

            if (over)
                Widgets.DrawHighlight(tile);

            Rect cross = new Rect(tile.xMax - CrossSize - 3f, tile.y + 3f, CrossSize, CrossSize);

            if (remove != null)
            {
                // Drawn on every tile rather than only the hovered one, and faded until the pointer arrives. A
                // control that comes and goes between frames is how a neighbour's id gets shifted, and fourteen
                // crosses at full strength is fourteen things shouting at once.
                Color previous = GUI.color;

                try
                {
                    GUI.color = new Color(1f, 1f, 1f, Mouse.IsOver(cross) ? 1f : over ? 0.8f : 0.3f);

                    GUI.DrawTexture(cross, TexButton.Delete);
                }
                finally
                {
                    GUI.color = previous;
                }

                if (Widgets.ButtonInvisible(cross))
                {
                    remove(gene);

                    return;
                }
            }

            // The whole tile, with the cross's corner refused rather than cut out: a rect cannot have a hole, so
            // the separation is this test rather than the geometry. The cross was already given its chance above
            // and returned if it took it.
            if (!Widgets.ButtonInvisible(tile))
                return;

            if (remove != null && Mouse.IsOver(cross))
                return;

            UIGuard.Try("Editor.GeneCard",
                () => Find.WindowStack.Add(new Dialog_InfoCard(gene.def)), null);
        }
    }
}
