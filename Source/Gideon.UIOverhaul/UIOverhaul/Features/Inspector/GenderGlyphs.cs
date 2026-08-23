using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The Mars and Venus glyphs that sit to the left of a pawn's name in the inspect pane.
    ///
    /// <b>Drawn rather than shipped,</b> for the reason <see cref="UIIconCanvas"/> exists: a mask takes the colour
    /// it is drawn with. That matters more here than anywhere else in the mod, because the whole point of these is
    /// the colour -- accent for male and mood for female, which is Aaron's own pairing -- and a pair of PNGs would
    /// need a variant per theme and would still be wrong the moment somebody edits a palette.
    ///
    /// <b>Two symbols nobody has to learn.</b> They are the two most widely recognised glyphs there are for this,
    /// and putting them before the name rather than in the qualifier means the answer arrives with the name rather
    /// than after reading a line of small grey text.
    ///
    /// <b>Authored bold, because they are read at fourteen pixels.</b> A ring two and a half units thick in a
    /// thirty-two unit square is heavy on paper and correct at the size these actually draw at; anything finer
    /// turns to a grey smudge next to a Medium-weight name.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class GenderGlyphs
    {
        /// <summary>Baked well above the drawn size, since masks are bilinear and shimmer when baked at it.</summary>
        private const int Baked = 128;

        /// <summary>How wide the glyph draws, and the gap it leaves before the name.</summary>
        internal const float Size = 14f;

        internal const float Gap = 6f;

        internal static readonly Texture2D Male;

        internal static readonly Texture2D Female;

        /// <summary>
        /// <b>Written out rather than through <c>UIGuard.Try</c>,</b> because a static constructor that throws
        /// leaves the CLR marking the type unusable and every later read throws again from wherever it was called.
        /// Catching here leaves both textures null, which <see cref="Draw"/> reads as "no glyph" and skips.
        /// </summary>
        static GenderGlyphs()
        {
            try
            {
                // Mars: a ring low and left, with the arrow leaving it at the upper right. The shaft starts on the
                // ring's own circumference rather than at its centre, so no part of it is drawn inside the ring
                // where it would fill the hole in at small sizes.
                Male = new UIIconCanvas(Baked)
                    .Ring(13f, 19.5f, 8f, 2.6f)
                    .Line(18.7f, 13.8f, 26.5f, 6f, 2.6f)
                    .Triangle(29.5f, 3.5f, 29.5f, 11.5f, 21.5f, 3.5f)
                    .ToTexture("Gideon_GenderMale");

                // Venus: a ring high and centred, with the cross hanging below it. The vertical bar starts at the
                // ring's underside for the same reason.
                Female = new UIIconCanvas(Baked)
                    .Ring(16f, 12f, 8f, 2.6f)
                    .Rect(14.7f, 19.4f, 2.6f, 10.6f)
                    .Rect(10.4f, 24f, 11.2f, 2.6f)
                    .ToTexture("Gideon_GenderFemale");
            }
            catch (Exception ex)
            {
                UIGuard.Report("Inspector.GenderGlyphs", ex,
                    "The inspect pane has no gender glyphs. Everything else about the pane is unaffected, and "
                    + "the sex is still in the line under the name.");
            }
        }

        /// <summary>
        /// The glyph for a pawn, or null when there is nothing to draw.
        ///
        /// Null for <c>Gender.None</c>, which is mechanoids and a few other things the question does not apply to.
        /// Drawing a third symbol for them would be inventing a fact.
        /// </summary>
        private static Texture2D For(Pawn pawn)
        {
            if (pawn == null)
                return null;

            switch (pawn.gender)
            {
                case Gender.Male:
                    return Male;

                case Gender.Female:
                    return Female;

                default:
                    return null;
            }
        }

        /// <summary>How much room the glyph needs before the name, or zero when there is none to draw.</summary>
        internal static float WidthFor(Pawn pawn)
        {
            return For(pawn) == null ? 0f : Size + Gap;
        }

        /// <summary>
        /// Draws the glyph, vertically centred in the line it was handed, and says how much room it took.
        ///
        /// <b>Colour by sex, which is the whole request:</b> the accent for male and the mood colour for female.
        /// Both are palette roles rather than literals, so the pair follows a theme the way everything else does.
        /// </summary>
        internal static float Draw(Rect line, Pawn pawn, UIColorPaletteDef palette)
        {
            Texture2D glyph = For(pawn);

            if (glyph == null)
                return 0f;

            Rect at = new Rect(line.x, line.y + (line.height - Size) * 0.5f, Size, Size);

            Color previous = GUI.color;

            try
            {
                GUI.color = pawn.gender == Gender.Male ? palette.Accent : palette.Mood;

                GUI.DrawTexture(at, glyph, ScaleMode.ScaleToFit);
            }
            finally
            {
                GUI.color = previous;
            }

            TooltipHandler.TipRegion(at, (TipSignal) pawn.gender.GetLabel().CapitalizeFirst());

            return Size + Gap;
        }
    }
}
