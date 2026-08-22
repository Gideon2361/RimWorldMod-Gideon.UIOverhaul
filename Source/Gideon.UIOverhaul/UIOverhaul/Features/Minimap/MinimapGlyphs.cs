using System;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Minimap
{
    /// <summary>
    /// Glyphs the minimap draws over its dots.
    ///
    /// <b>Drawn rather than shipped, and rather than typed.</b> The same two reasons the colonist bar's skull is
    /// generated: a Unicode paw is outside RimWorld's bitmap font atlases and would render as a hollow box, and a
    /// mask takes the colour it is drawn with instead of needing a PNG per palette.
    ///
    /// <b>Built for eleven pixels, which decides every proportion.</b> A real paw print has a broad pad and four
    /// small toes; at this size four toes of realistic scale would each be one pixel and would blur into the pad.
    /// So the toes are oversized relative to life and set well clear of the pad, because the gap between them is
    /// what makes the silhouette read as a paw rather than as a blob.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MinimapGlyphs
    {
        /// <summary>Baked well above the drawn size, since masks are bilinear and shimmer when baked at it.</summary>
        private const int Baked = 64;

        /// <summary>A paw print, marking a predator. Null if the bake failed.</summary>
        internal static readonly Texture2D Paw;

        /// <summary>
        /// <b>Written out rather than through <c>UIGuard.Try</c>,</b> because a static constructor that throws
        /// leaves the CLR marking the type unusable and every later read of the field throws again from wherever it
        /// was called. Catching here leaves the field null, which the drawer already reads as "draw the plain dot".
        /// </summary>
        static MinimapGlyphs()
        {
            try
            {
                Paw = BuildPaw();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Minimap.Glyphs", ex,
                    "Predators on the minimap are drawn as ordinary animal dots rather than as paw prints.");
            }
        }

        /// <summary>
        /// A pad and four toes, in the canvas's 32 unit space with y running downward.
        ///
        /// The toes sit on an arc rather than in a line, with the outer two lower than the inner two, which is what
        /// a paw actually looks like and is also what stops the four of them reading as a dashed line when they are
        /// three pixels each.
        /// </summary>
        private static Texture2D BuildPaw()
        {
            UIIconCanvas canvas = new UIIconCanvas(Baked);

            // The pad. Low in the frame, leaving the upper half for the toes.
            canvas.Disc(16f, 21.5f, 7.6f);

            // Inner toes, high and close together.
            canvas.Disc(12.6f, 9.6f, 3.7f);
            canvas.Disc(19.4f, 9.6f, 3.7f);

            // Outer toes, lower and wider, which is what gives the print its splay.
            canvas.Disc(6.4f, 13.4f, 3.5f);
            canvas.Disc(25.6f, 13.4f, 3.5f);

            return canvas.ToTexture("Gideon_MinimapPaw");
        }
    }
}
