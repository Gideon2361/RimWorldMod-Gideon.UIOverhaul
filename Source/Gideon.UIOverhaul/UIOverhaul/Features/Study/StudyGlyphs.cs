using System;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Study
{
    /// <summary>
    /// The icon on the study assignment command.
    ///
    /// <b>Drawn rather than shipped,</b> for the reason <see cref="UIIconCanvas"/> exists: a mask takes the colour
    /// it is drawn with, so one glyph follows the palette and the button's state instead of needing a PNG per
    /// theme. It also means the command has an icon at all without adding a texture to the mod folder for a single
    /// button.
    ///
    /// <b>A head inside a magnifier,</b> which is the two halves of what the button does: somebody specific, and
    /// studying. Read at 32 pixels in a command bar, so it is one bold silhouette with a wide gap between the ring
    /// and the figure inside it. A literal person under a lens is a smudge at that size.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class StudyGlyphs
    {
        /// <summary>Baked well above the drawn size, since masks are bilinear and shimmer when baked at it.</summary>
        private const int Baked = 128;

        internal static readonly Texture2D Assign;

        /// <summary>
        /// <b>Written out rather than through <c>UIGuard.Try</c>,</b> because a static constructor that throws
        /// leaves the CLR marking the type unusable and every later read throws again from wherever it was called.
        /// Catching here leaves the icon null, which the command draws as a plain labelled button.
        /// </summary>
        static StudyGlyphs()
        {
            try
            {
                Assign = new UIIconCanvas(Baked)
                    .Ring(14f, 14f, 11f, 2.4f)
                    .Line(21.5f, 21.5f, 29f, 29f, 3.2f)
                    .Disc(14f, 10.6f, 3.1f)
                    .CutDisc(14f, 10.6f, 1.4f)
                    .Rect(9.4f, 15.2f, 9.2f, 5.4f)
                    .CutRect(11.2f, 16.8f, 5.6f, 3.8f)
                    .ToTexture("Gideon_StudyAssign");
            }
            catch (Exception ex)
            {
                UIGuard.Report("Study.Glyphs", ex,
                    "The study assignment button has no icon. It still works and still says who is assigned.");
            }
        }
    }
}
