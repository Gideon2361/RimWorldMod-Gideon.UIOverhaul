using System;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// The drawn icon set: everything geometric enough to be described in shapes rather than painted.
    ///
    /// <b>What is here and what is deliberately not.</b> Weather, conditions, temperature, the envelope, the hazard
    /// badge and the reminder bell are all shapes -- a sun is a disc and rays, a thermometer is a stem and a bulb --
    /// and drawing them costs a few lines each and follows the palette for free. Representational event icons are
    /// not here: crossed swords or a virus built from primitives reads as a diagram of the thing rather than the
    /// thing, and those are the one place worth real art. Until that art exists, a letter shows
    /// <see cref="Envelope"/> and its tone comes from the card's colored edge.
    ///
    /// <b>Baked at four times the size they draw.</b> These sit at 16 to 20 pixels in a card; the masks are 64,
    /// because a bilinear texture drawn smaller than it was baked is smooth and one drawn larger is not.
    ///
    /// <b>Built once, on first use, and never rebuilt.</b> A memo rather than an interval cache -- the shapes cannot
    /// change -- so this is deliberately not a <c>UICache</c>. The one thing that could invalidate them is a
    /// resolution change, which does not affect a mask.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class NotificationIcons
    {
        /// <summary>
        /// Baked size. Four times the largest size these draw at, which is the cheapest way to stay sharp without
        /// generating a mask per size.
        /// </summary>
        private const int Size = 64;

        internal static readonly Texture2D Envelope;
        internal static readonly Texture2D Hazard;
        internal static readonly Texture2D Bell;
        internal static readonly Texture2D Thermometer;
        internal static readonly Texture2D Clear;
        internal static readonly Texture2D Eclipse;
        internal static readonly Texture2D Overcast;
        internal static readonly Texture2D Rain;
        internal static readonly Texture2D Snow;
        internal static readonly Texture2D Wind;
        internal static readonly Texture2D Toxic;
        internal static readonly Texture2D SolarFlare;
        internal static readonly Texture2D Skull;

        /// <summary>
        /// <b>Guarded, and written out rather than through <c>UIGuard.Try</c>.</b> These are <c>static readonly</c>,
        /// which C# only allows the static constructor itself to assign -- a lambda is a different method and would
        /// not compile. Every static constructor in this mod is written this way for that reason.
        ///
        /// The catch matters more here than the usual amount. RimWorld already catches a failing static constructor,
        /// so the game still loads -- but the CLR then marks the type as failed, and every later read of one of these
        /// fields raises <c>TypeInitializationException</c> instead of returning a texture. Since they are read while
        /// drawing, that turns one startup fault into a fault on every frame. Catching here leaves them null, which
        /// the drawing code treats as "no icon" and survives.
        /// </summary>
        static NotificationIcons()
        {
            try
            {
                Envelope = BuildEnvelope();
                Hazard = BuildHazard();
                Bell = BuildBell();
                Thermometer = BuildThermometer();
                Clear = BuildClear();
                Eclipse = BuildEclipse();
                Overcast = BuildOvercast();
                Rain = BuildRain();
                Snow = BuildSnow();
                Wind = BuildWind();
                Toxic = BuildToxic();
                SolarFlare = BuildSolarFlare();
                Skull = BuildSkull();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Notifications.BuildIcons", ex,
                    "Notification cards draw without icons. Their colored edges still carry the tone.");
            }
        }

        // ---------------------------------------------------------------------------------------
        // The shapes
        //
        // All in the canvas's 32 unit square, top down. Kept to a handful of primitives each: these are read at 16
        // pixels, where roughly five strokes survive, so an icon that needs more detail than this to be recognized
        // is an icon that will not be recognized.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// A skull: cranium, jaw, and the features cut out of them rather than drawn on top.
        ///
        /// <b>Everything is a cut, which is what keeps it readable when it shrinks.</b> Drawing dark eye sockets
        /// over a light cranium would need a second color and would turn to mud at 16 pixels; removing them means
        /// the sockets are whatever is behind the icon, so the contrast is the panel's rather than the glyph's.
        /// Same trick as the hazard badge above.
        ///
        /// Two teeth gaps, not four. At the size this is read a third gap closes up into a grey smear and the jaw
        /// stops reading as a jaw.
        /// </summary>
        private static Texture2D BuildSkull()
        {
            return new UIIconCanvas(Size)
                .Disc(16f, 14f, 10f)
                .Rect(11f, 21f, 10f, 6f)
                .CutDisc(11.5f, 14f, 3.6f)
                .CutDisc(20.5f, 14f, 3.6f)
                .CutTriangle(16f, 17f, 13.8f, 21f, 18.2f, 21f)
                .CutRect(13.5f, 22.5f, 1.3f, 4.5f)
                .CutRect(17.2f, 22.5f, 1.3f, 4.5f)
                .ToTexture("Gideon.Icon.Skull");
        }

        /// <summary>A body with the flap cut out of it, rather than a body with a V drawn on top.</summary>
        private static Texture2D BuildEnvelope()
        {
            return new UIIconCanvas(Size)
                .Rect(2f, 7f, 28f, 18f)
                .CutRect(4f, 9f, 24f, 14f)
                .Rect(4f, 9f, 24f, 2f)
                .Triangle(4f, 9f, 28f, 9f, 16f, 20f)
                .CutTriangle(6f, 8f, 26f, 8f, 16f, 17f)
                .ToTexture("Gideon.Icon.Envelope");
        }

        /// <summary>
        /// The critical badge: a triangle with the bar and dot cut out, so the mark is the card showing through
        /// rather than a second color drawn over the top.
        /// </summary>
        private static Texture2D BuildHazard()
        {
            return new UIIconCanvas(Size)
                .Triangle(16f, 2f, 31f, 29f, 1f, 29f)
                .CutRect(14.5f, 11f, 3f, 9f)
                .CutRect(14.5f, 22f, 3f, 3f)
                .ToTexture("Gideon.Icon.Hazard");
        }

        private static Texture2D BuildBell()
        {
            return new UIIconCanvas(Size)
                .Disc(16f, 14f, 8.5f)
                .Rect(7.5f, 14f, 17f, 6f)
                .Rect(4f, 20f, 24f, 3f)
                .Disc(16f, 27f, 3f)
                .Rect(15f, 1f, 2f, 4f)
                .ToTexture("Gideon.Icon.Bell");
        }

        private static Texture2D BuildThermometer()
        {
            return new UIIconCanvas(Size)
                .Line(16f, 6f, 16f, 20f, 6f)
                .Disc(16f, 24f, 6f)
                .Rect(22f, 8f, 4f, 2f)
                .Rect(22f, 13f, 4f, 2f)
                .Rect(22f, 18f, 4f, 2f)
                .ToTexture("Gideon.Icon.Thermometer");
        }

        /// <summary>Clear skies: a disc and eight rays, four square and four diagonal.</summary>
        private static Texture2D BuildClear()
        {
            UIIconCanvas canvas = new UIIconCanvas(Size).Disc(16f, 16f, 7f);

            for (int i = 0; i < 8; i++)
            {
                // Rays generated rather than written out, because eight hand-placed diagonals is eight chances to
                // get one slightly wrong, and a sun with an uneven ray is the kind of thing you see every time.
                float angle = i * Mathf.PI * 0.25f;
                float dx = Mathf.Cos(angle);
                float dy = Mathf.Sin(angle);

                canvas.Line(16f + dx * 10f, 16f + dy * 10f, 16f + dx * 14f, 16f + dy * 14f, 2.5f);
            }

            return canvas.ToTexture("Gideon.Icon.Clear");
        }

        /// <summary>A crescent, made by cutting an offset disc out of a disc. No other way to say "eclipse" in shapes.</summary>
        private static Texture2D BuildEclipse()
        {
            return new UIIconCanvas(Size)
                .Disc(16f, 16f, 13f)
                .CutDisc(23f, 13f, 11f)
                .ToTexture("Gideon.Icon.Eclipse");
        }

        private static UIIconCanvas Cloud(UIIconCanvas canvas)
        {
            return canvas
                .Disc(11f, 12f, 6f)
                .Disc(20f, 10f, 7f)
                .Rect(5f, 12f, 22f, 6f);
        }

        private static Texture2D BuildOvercast()
        {
            return Cloud(new UIIconCanvas(Size)).ToTexture("Gideon.Icon.Overcast");
        }

        private static Texture2D BuildRain()
        {
            UIIconCanvas canvas = Cloud(new UIIconCanvas(Size));

            for (int i = 0; i < 3; i++)
                canvas.Line(9f + i * 7f, 22f, 6f + i * 7f, 29f, 2.5f);

            return canvas.ToTexture("Gideon.Icon.Rain");
        }

        /// <summary>
        /// Three crossing bars and no tick marks.
        ///
        /// A real snowflake's ticks are under a pixel each at the size this draws, so they would only muddy the
        /// middle. Three bars at sixty degrees is what survives.
        /// </summary>
        private static Texture2D BuildSnow()
        {
            UIIconCanvas canvas = new UIIconCanvas(Size);

            for (int i = 0; i < 3; i++)
            {
                float angle = Mathf.PI * (0.5f + i / 3f);
                float dx = Mathf.Cos(angle) * 13f;
                float dy = Mathf.Sin(angle) * 13f;

                canvas.Line(16f - dx, 16f - dy, 16f + dx, 16f + dy, 2.8f);
            }

            return canvas.ToTexture("Gideon.Icon.Snow");
        }

        /// <summary>Three bars of different lengths. The curled ends of a drawn wind glyph do not survive 16 pixels.</summary>
        private static Texture2D BuildWind()
        {
            return new UIIconCanvas(Size)
                .Line(4f, 11f, 24f, 11f, 3f)
                .Line(7f, 17f, 28f, 17f, 3f)
                .Line(4f, 23f, 19f, 23f, 3f)
                .ToTexture("Gideon.Icon.Wind");
        }

        /// <summary>
        /// The toxic mark: a ring with three wedges cut out of it, which is as close to a trefoil as shapes get.
        /// </summary>
        private static Texture2D BuildToxic()
        {
            UIIconCanvas canvas = new UIIconCanvas(Size).Disc(16f, 16f, 13f);

            for (int i = 0; i < 3; i++)
            {
                float angle = i * Mathf.PI * 2f / 3f - Mathf.PI * 0.5f;

                float ax = 16f + Mathf.Cos(angle) * 16f;
                float ay = 16f + Mathf.Sin(angle) * 16f;

                float bx = 16f + Mathf.Cos(angle + 0.7f) * 16f;
                float by = 16f + Mathf.Sin(angle + 0.7f) * 16f;

                canvas.CutTriangle(16f, 16f, ax, ay, bx, by);
            }

            // Cut a disc, then put a smaller one back. That leaves the ring of empty space between the blades and
            // the center dot, which is the part of the trefoil that makes it read as one -- cutting the center
            // alone would leave a hole where the dot belongs, which reads as a gear rather than a hazard.
            return canvas
                .CutDisc(16f, 16f, 5f)
                .Disc(16f, 16f, 3.5f)
                .ToTexture("Gideon.Icon.Toxic");
        }

        /// <summary>A disc with a flare arcing off it, for a solar flare or any radiation-adjacent condition.</summary>
        private static Texture2D BuildSolarFlare()
        {
            return new UIIconCanvas(Size)
                .Disc(13f, 18f, 9f)
                .Line(19f, 11f, 27f, 4f, 3f)
                .Line(23f, 14f, 30f, 11f, 2.5f)
                .Line(16f, 7f, 19f, 2f, 2.5f)
                .ToTexture("Gideon.Icon.SolarFlare");
        }
    }
}
