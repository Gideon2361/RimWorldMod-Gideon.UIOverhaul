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

        /// <summary>Crossed swords, standing in for the letter D on a drafted pawn. Null if the bake failed.</summary>
        internal static readonly Texture2D Swords;

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
                Swords = BuildSwords();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Bar.Glyphs", ex,
                    "Undead pawns in the colonist bar show their full name instead of a skull, and drafted pawns "
                    + "show the letter D instead of crossed swords.");
            }
        }

        /// <summary>
        /// Two crossed swords, points up, hilts down.
        ///
        /// <b>Four strokes and nothing else, for the same reason the skull has no teeth.</b> Pommels sized in
        /// proportion land under two pixels across and read as grit on the blade, and a tapered point is lost
        /// entirely: the capsule end a stroke already has does that job. What has to survive the shrink is the X
        /// and the two crossbars, because that pair is the whole difference between "swords" and "scissors".
        ///
        /// <b>The crossbars are what set the size this is drawn at, and they nearly did not survive.</b> Drawn at
        /// the name row's line height, about thirteen pixels, the guards merge into the blades and the glyph
        /// collapses into a plain red X -- which in a game full of cancel buttons says something else entirely.
        /// The blades are therefore thinner than the guards would suggest, the guards are pushed down toward the
        /// hilts where there is clear space either side of them, and <c>ColonistBarPanel</c> draws this at a floor
        /// of eighteen pixels rather than at the line height. Below that it is an X again.
        ///
        /// The blades cross at the center of the canvas rather than nearer the hilts, so the symbol reads as a
        /// balanced X rather than as a wishbone.
        /// </summary>
        private static Texture2D BuildSwords()
        {
            return new UIIconCanvas(Baked)
                .Line(7.5f, 24.5f, 25.5f, 6.5f, 2.4f)
                .Line(24.5f, 24.5f, 6.5f, 6.5f, 2.4f)
                .Line(5.6f, 17.2f, 13.6f, 25.2f, 2.4f)
                .Line(26.4f, 17.2f, 18.4f, 25.2f, 2.4f)
                .ToTexture("Gideon.Icon.BarDrafted");
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
