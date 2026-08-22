using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Icons for the training kinds that ship without one.
    ///
    /// <b>All nine of Odyssey's trainables have an empty <c>icon</c> field.</b> Core's five declare theirs; comfort,
    /// forage, dig, attack target, egg spew, sludge spew, terror roar, war trumpet and thrumbo roar declare
    /// nothing, and asking a def for an icon it does not have is what took the animals tab down on 2026-08-22. The
    /// null check fixed the crash and left those boxes carrying two letters, which works and is not what the boxes
    /// are for: a row of small squares is legible as a set of symbols and unreadable as a set of abbreviations.
    ///
    /// <b>Drawn rather than shipped,</b> for the reason <see cref="UIIconCanvas"/> exists: a mask is a white shape
    /// in an alpha channel, so it takes the colour it is drawn with and follows the palette and the box's state
    /// instead of needing a PNG per theme.
    ///
    /// <b>Keyed by defName, and only these nine.</b> A trainable from another mod that declares an icon keeps its
    /// own art, and one that declares none falls back to letters rather than being handed a symbol that means
    /// something else. Painting over somebody else's def because it happens to be iconless would be worse than
    /// the letters.
    ///
    /// <b>Built for sixteen pixels, which decides every proportion.</b> These are drawn inside a 22 pixel
    /// checkbox, so each glyph is one bold silhouette with generous gaps. Two of them are frankly interpretations
    /// rather than depictions: a roar is drawn as sound arcs and a trumpet as a flared horn, because a literal
    /// megasloth at this size is a smudge.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class AnimalTrainingIcons
    {
        /// <summary>Baked well above the drawn size, since masks are bilinear and shimmer when baked at it.</summary>
        private const int Baked = 64;

        private static readonly Dictionary<string, Texture2D> Drawn = new Dictionary<string, Texture2D>();

        /// <summary>
        /// <b>Written out rather than through <c>UIGuard.Try</c>,</b> because a static constructor that throws
        /// leaves the CLR marking the type unusable and every later read throws again from wherever it was called.
        /// Catching here leaves the table empty, which the caller already reads as "use letters".
        /// </summary>
        static AnimalTrainingIcons()
        {
            try
            {
                Add("AttackTarget", Crosshair);
                Add("Comfort", Heart);
                Add("Forage", Berries);
                Add("Dig", Shovel);
                Add("EggSpew", Egg);
                Add("SludgeSpew", Drips);
                Add("TerrorRoar", Roar);
                Add("WarTrumpet", Trumpet);
                Add("ThrumboRoar", Horn);
            }
            catch (Exception ex)
            {
                UIGuard.Report("Animals.TrainingIcons", ex,
                    "The Odyssey training kinds show two letters in their checkboxes instead of a symbol.");
            }
        }

        private static void Add(string defName, Func<UIIconCanvas, UIIconCanvas> build)
        {
            UIIconCanvas canvas = new UIIconCanvas(Baked);

            Drawn[defName] = build(canvas).ToTexture("Gideon_Trainable_" + defName);
        }

        /// <summary>Our drawing for this kind, or null when it is not one of ours.</summary>
        internal static Texture2D For(TrainableDef kind)
        {
            if (kind == null || kind.defName.NullOrEmpty())
                return null;

            Texture2D found;

            return Drawn.TryGetValue(kind.defName, out found) ? found : null;
        }

        // ---------------------------------------------------------------------------------------
        // The glyphs, all in the canvas's 32 unit space with y running downward
        // ---------------------------------------------------------------------------------------

        /// <summary>Attack target: a crosshair, which is what the order actually is.</summary>
        private static UIIconCanvas Crosshair(UIIconCanvas canvas)
        {
            return canvas
                .Ring(16f, 16f, 9.5f, 2.6f)
                .Line(16f, 1.5f, 16f, 6.5f, 2.6f)
                .Line(16f, 25.5f, 16f, 30.5f, 2.6f)
                .Line(1.5f, 16f, 6.5f, 16f, 2.6f)
                .Line(25.5f, 16f, 30.5f, 16f, 2.6f)
                .Disc(16f, 16f, 2.4f);
        }

        /// <summary>Comfort: a heart, from two lobes and a point.</summary>
        private static UIIconCanvas Heart(UIIconCanvas canvas)
        {
            return canvas
                .Disc(11.2f, 12.5f, 6.4f)
                .Disc(20.8f, 12.5f, 6.4f)
                .Triangle(4.8f, 14.6f, 27.2f, 14.6f, 16f, 28.5f);
        }

        /// <summary>
        /// Forage: three berries on a stem.
        ///
        /// Berries rather than a leaf, because a leaf at this size is the same silhouette as a drip and this set
        /// already has two of those.
        /// </summary>
        private static UIIconCanvas Berries(UIIconCanvas canvas)
        {
            return canvas
                .Line(16f, 2.5f, 16f, 11f, 2.2f)
                .Disc(16f, 13.5f, 5.2f)
                .Disc(9.5f, 22.5f, 5.2f)
                .Disc(22.5f, 22.5f, 5.2f);
        }

        /// <summary>Dig: a spade cutting into the ground.</summary>
        private static UIIconCanvas Shovel(UIIconCanvas canvas)
        {
            return canvas
                .Rect(2f, 26.5f, 28f, 3.5f)
                .Line(21f, 4f, 12.5f, 17f, 3.2f)
                .Triangle(6f, 15.5f, 19f, 15.5f, 12.5f, 25.5f);
        }

        /// <summary>Egg spew: an egg, narrow end up.</summary>
        private static UIIconCanvas Egg(UIIconCanvas canvas)
        {
            return canvas
                .Disc(16f, 20f, 7.2f)
                .Triangle(9.2f, 20.5f, 22.8f, 20.5f, 16f, 5.5f);
        }

        /// <summary>
        /// Sludge spew: three drips, falling away.
        ///
        /// Three of them at descending sizes rather than one, so it cannot be mistaken for the egg above it: one
        /// drop and one egg are the same shape at sixteen pixels.
        /// </summary>
        private static UIIconCanvas Drips(UIIconCanvas canvas)
        {
            canvas.Disc(10f, 12f, 5.4f).Triangle(5.2f, 12.4f, 14.8f, 12.4f, 10f, 3f);
            canvas.Disc(21f, 19f, 4.2f).Triangle(17.2f, 19.4f, 24.8f, 19.4f, 21f, 11.5f);
            canvas.Disc(13.5f, 26.5f, 3.2f);

            return canvas;
        }

        /// <summary>
        /// Terror roar: sound arcs leaving a source.
        ///
        /// Rings with their left halves cut away, which is the canvas's way of drawing an arc: cutting subtracts
        /// coverage, so a ring minus a rectangle is the part of the ring outside it.
        /// </summary>
        private static UIIconCanvas Roar(UIIconCanvas canvas)
        {
            canvas.Ring(6f, 16f, 10f, 2.6f);
            canvas.Ring(6f, 16f, 17f, 2.6f);

            // Everything to the left of the arcs' opening goes, which is what turns two rings into two arcs.
            canvas.CutRect(0f, 0f, 8.5f, 32f);

            // The source is drawn after the cut, not before it. The canvas paints in order over one buffer, so a
            // disc drawn first would sit inside the rectangle that is about to be erased and vanish with it.
            canvas.Disc(5.6f, 16f, 3.6f);

            return canvas;
        }

        /// <summary>War trumpet: a flared horn with a mouthpiece.</summary>
        private static UIIconCanvas Trumpet(UIIconCanvas canvas)
        {
            return canvas
                .Rect(2.5f, 14f, 8f, 4f)
                .Triangle(9f, 9.5f, 9f, 22.5f, 28.5f, 16f)
                .Triangle(20f, 3.5f, 20f, 28.5f, 29f, 16f);
        }

        /// <summary>
        /// Thrumbo roar: a curved horn.
        ///
        /// A quarter of a thick ring, which is the shape of a thrumbo's horn and is nothing else in this set.
        /// </summary>
        private static UIIconCanvas Horn(UIIconCanvas canvas)
        {
            canvas.Ring(22f, 24f, 15f, 5.2f);

            // Keep the upper left quarter: the rest of the ring is cut away in two passes.
            canvas.CutRect(22f, 0f, 10f, 32f);
            canvas.CutRect(0f, 24f, 32f, 8f);

            canvas.Disc(6.6f, 23.6f, 3.4f);

            return canvas;
        }
    }
}
