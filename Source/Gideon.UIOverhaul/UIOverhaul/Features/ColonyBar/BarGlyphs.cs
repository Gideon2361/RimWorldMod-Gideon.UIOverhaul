using System;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ColonyBar
{
    /// <summary>
    /// Glyphs the colonist bar draws into a tile's name row.
    ///
    /// <b>Drawn rather than typed.</b> The obvious way to mark an undead is the Unicode skull, U+2620, and it does
    /// not work: RimWorld's fonts are bitmap atlases built for the characters the game ships translations for, so a
    /// glyph outside that set renders as a hollow box. A generated mask always renders.
    ///
    /// <b>Drawn rather than shipped,</b> for the reason <see cref="UIIconCanvas"/> exists: a mask is a white shape
    /// in an alpha channel, so it takes the colour it is drawn with and follows the active palette instead of
    /// needing one PNG per theme.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class BarGlyphs
    {
        /// <summary>Baked well above the drawn size, since masks are bilinear and shimmer when baked at it.</summary>
        private const int Baked = 64;

        /// <summary>A skull, standing in for the word "Undead" in a pawn's name. Null if the bake failed.</summary>
        internal static readonly Texture2D Skull;

        /// <summary>
        /// <b>Written out rather than through <c>UIGuard.Try</c>,</b> because a static constructor that throws
        /// leaves the CLR marking the type unusable and every later read of the field throws again from wherever it
        /// was called. Catching here leaves the field null, which the drawer already reads as "draw the word".
        /// </summary>
        static BarGlyphs()
        {
            try
            {
                Skull = BuildSkull();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Bar.Glyphs", ex,
                    "Undead pawns in the colonist bar show their full name instead of a skull.");
            }
        }

        /// <summary>
        /// A cranium, a tapered jaw, two sockets and a nose.
        ///
        /// <b>Built for eleven pixels, which is what decides every proportion here.</b> The sockets are far larger
        /// relative to the head than a real skull's, because at this size they are the whole identity of the symbol:
        /// anatomically correct ones close to nothing and leave a pale blob. The taper on the jaw is what stops the
        /// silhouette reading as a lightbulb.
        ///
        /// Teeth are deliberately absent. Three gaps four units wide render as one grey smudge at eleven pixels and
        /// only muddy the sockets above them.
        ///
        /// Y increases downward in this space, so the cranium's smaller numbers put it above the jaw.
        /// </summary>
        private static Texture2D BuildSkull()
        {
            return new UIIconCanvas(Baked)
                .Disc(16f, 13f, 8.6f)
                .Rect(11.4f, 18f, 9.2f, 8.4f)
                .CutTriangle(11.4f, 21.5f, 11.4f, 26.4f, 13.3f, 26.4f)
                .CutTriangle(20.6f, 21.5f, 20.6f, 26.4f, 18.7f, 26.4f)
                .CutDisc(12.3f, 12.6f, 3.3f)
                .CutDisc(19.7f, 12.6f, 3.3f)
                .CutTriangle(16f, 15.6f, 14.4f, 19.6f, 17.6f, 19.6f)
                .ToTexture("Gideon.Icon.BarUndead");
        }
    }
}
