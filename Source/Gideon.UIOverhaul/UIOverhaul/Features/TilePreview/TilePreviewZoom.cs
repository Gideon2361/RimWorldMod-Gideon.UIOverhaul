using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.TilePreview
{
    /// <summary>
    /// The picture while a true analysis is running: a satellite closing on the tile, a step at a time.
    ///
    /// <b>It shows progress that is real rather than a spinner that is not.</b> Each magnification is one
    /// generation step that has actually finished, so a tile whose mod list makes it slow looks slow, and the
    /// count underneath says how much is left. A spinner would have claimed the same thing about every tile.
    ///
    /// <b>It zooms the estimate, because the estimate is the only picture there is until the last step.</b> The
    /// terrain is not known until the terrain steps have run, so what is magnified here is the guess the panel
    /// was already showing. That is honest as long as it is never mistaken for the answer, which is what the
    /// caption is for.
    /// </summary>
    internal static class TilePreviewZoom
    {
        /// <summary>How far in it will go. Past this the crop is a handful of cells and reads as noise.</summary>
        private const int MaxLevel = 8;

        private const float ReticleGap = 0.34f;

        internal static void Draw(Rect image, Texture2D texture, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(image, palette.SurfaceSunken);

            if (texture != null)
            {
                int level = Mathf.Clamp(1 + TilePreviewJob.StepsDone, 1, MaxLevel);

                // The crop narrows on the middle as the level rises, which is what makes it read as closing in
                // rather than as the picture being replaced.
                float span = 1f / level;
                float edge = (1f - span) * 0.5f;

                GUI.DrawTextureWithTexCoords(image, texture, new Rect(edge, edge, span, span));

                Reticle(image, palette);
            }

            Widgets.DrawBox(image);
        }

        /// <summary>Four ticks pointing at the middle. Enough to say "instrument", cheap enough to draw hot.</summary>
        private static void Reticle(Rect image, UIColorPaletteDef palette)
        {
            Color previous = GUI.color;

            try
            {
                GUI.color = new Color(palette.Accent.r, palette.Accent.g, palette.Accent.b, 0.5f);

                float midX = image.x + image.width * 0.5f;
                float midY = image.y + image.height * 0.5f;
                float arm = image.width * 0.09f;
                float gapX = image.width * ReticleGap;
                float gapY = image.height * ReticleGap;

                Widgets.DrawBoxSolid(new Rect(midX - 0.5f, image.y + gapY - arm, 1f, arm), GUI.color);
                Widgets.DrawBoxSolid(new Rect(midX - 0.5f, image.yMax - gapY, 1f, arm), GUI.color);
                Widgets.DrawBoxSolid(new Rect(image.x + gapX - arm, midY - 0.5f, arm, 1f), GUI.color);
                Widgets.DrawBoxSolid(new Rect(image.xMax - gapX, midY - 0.5f, arm, 1f), GUI.color);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        /// <summary>The line under the picture while it runs: how far in, and how far through.</summary>
        internal static string Caption()
        {
            int level = Mathf.Clamp(1 + TilePreviewJob.StepsDone, 1, MaxLevel);

            if (TilePreviewJob.Phase == TilePreviewJobPhase.Preparing || TilePreviewJob.StepsTotal <= 0)
                return "Acquiring";

            return "Analyzing " + level + "x  (" + TilePreviewJob.StepsDone + "/"
                + TilePreviewJob.StepsTotal + ")";
        }
    }
}
