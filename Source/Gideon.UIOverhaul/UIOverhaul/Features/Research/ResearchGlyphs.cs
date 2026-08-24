using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// The pictures the research tab draws, rasterized in code.
    ///
    /// <b>Two unrelated sets, in one place because they are made the same way.</b> The handful of interface icons
    /// this tab needs -- a plus, a cross, a grip, a tick -- and the thirty marks the
    /// <see cref="ResearchScript.Generated"/> script masks with. Both are geometry rather than artwork, both tint
    /// to the running palette, and neither leaves a file on disk for another mod to shadow.
    ///
    /// <b>Thirty marks, and they have to be thirty different silhouettes.</b> A mask is a run of these drawn at
    /// random, so any two that read alike at thirteen pixels halve the apparent alphabet and the run starts to
    /// look like a repeating pattern -- which reads as a rendering fault rather than as an unknown language. They
    /// are deliberately mixed: straight-stroke marks, curved ones, ones with dots, ones with a headline, so a run
    /// has the texture of writing.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class ResearchGlyphs
    {
        /// <summary>Adds the selected project to the queue.</summary>
        internal static readonly Texture2D Plus;

        /// <summary>Takes a project out of the queue, and closes things.</summary>
        internal static readonly Texture2D Cross;

        /// <summary>The handle a queue row is dragged by.</summary>
        internal static readonly Texture2D Grip;

        /// <summary>A finished project.</summary>
        internal static readonly Texture2D Tick;

        /// <summary>A bench or facility the project wants.</summary>
        internal static readonly Texture2D Bench;

        /// <summary>Something to go and study or analyze.</summary>
        internal static readonly Texture2D Eye;

        /// <summary>The marks the generated script is written in.</summary>
        internal static readonly Texture2D[] Marks;

        /// <summary>
        /// Guarded whole, the way <c>MusicGlyphs</c> and <c>UIBarGlyphs</c> are: thirty-six textures rasterized in
        /// code is a good deal more that can go wrong than a content lookup, and a throw out of a static
        /// constructor leaves the type failed so that every later draw throws again. The fields stay null instead
        /// and the callers fall back to text.
        ///
        /// The body sits in the try rather than in a helper because these are readonly fields, which only the
        /// static constructor itself may assign.
        /// </summary>
        static ResearchGlyphs()
        {
            try
            {
                Plus = MakePlus();
                Cross = MakeCross();
                Grip = MakeGrip();
                Tick = MakeTick();
                Bench = MakeBench();
                Eye = MakeEye();

                Marks = MakeMarks();
            }
            catch (System.Exception ex)
            {
                UIGuard.Report("Research.Glyphs", ex,
                    "The research tab draws its buttons as text because its icons could not be built. Nothing "
                    + "about your colony's research has changed.");
            }
        }

        /// <summary>How many marks the generated script has.</summary>
        internal static int MarkCount
        {
            get { return Marks == null ? 0 : Marks.Length; }
        }

        private static Texture2D MakePlus()
        {
            return new UIGlyphCanvas()
                .Capsule(0.5f, 0.22f, 0.5f, 0.78f, 0.12f)
                .Capsule(0.22f, 0.5f, 0.78f, 0.5f, 0.12f)
                .ToTexture("Gideon.UIOverhaul.Research.Plus");
        }

        private static Texture2D MakeCross()
        {
            return new UIGlyphCanvas()
                .Capsule(0.26f, 0.26f, 0.74f, 0.74f, 0.11f)
                .Capsule(0.74f, 0.26f, 0.26f, 0.74f, 0.11f)
                .ToTexture("Gideon.UIOverhaul.Research.Cross");
        }

        /// <summary>Two columns of three dots, which is what a drag handle looks like everywhere else.</summary>
        private static Texture2D MakeGrip()
        {
            UIGlyphCanvas canvas = new UIGlyphCanvas();

            for (int column = 0; column < 2; column++)
            {
                for (int row = 0; row < 3; row++)
                    canvas.Disc(0.38f + column * 0.24f, 0.26f + row * 0.24f, 0.075f);
            }

            return canvas.ToTexture("Gideon.UIOverhaul.Research.Grip");
        }

        private static Texture2D MakeTick()
        {
            return new UIGlyphCanvas()
                .Polyline(0.12f, 0.22f, 0.52f, 0.43f, 0.72f, 0.80f, 0.28f)
                .ToTexture("Gideon.UIOverhaul.Research.Tick");
        }

        /// <summary>A bench: a top and two legs, which reads at fourteen pixels where a workbench does not.</summary>
        private static Texture2D MakeBench()
        {
            return new UIGlyphCanvas()
                .Capsule(0.16f, 0.42f, 0.84f, 0.42f, 0.12f)
                .Capsule(0.28f, 0.48f, 0.28f, 0.80f, 0.10f)
                .Capsule(0.72f, 0.48f, 0.72f, 0.80f, 0.10f)
                .ToTexture("Gideon.UIOverhaul.Research.Bench");
        }

        /// <summary>
        /// An eye, for the two states that are answered by going and looking at something: an analysis and an
        /// inspection. The pupil is drawn before the lid so the erase cannot take it away.
        /// </summary>
        private static Texture2D MakeEye()
        {
            return new UIGlyphCanvas()
                .Ellipse(0.5f, 0.5f, 0.42f, 0.26f)
                .Erase(0.5f, 0.5f, 0.30f)
                .Disc(0.5f, 0.5f, 0.13f)
                .ToTexture("Gideon.UIOverhaul.Research.Eye");
        }

        /// <summary>
        /// The thirty marks, built once.
        ///
        /// Authored as a flat list rather than generated from parameters on purpose: a procedural family comes out
        /// as thirty variations on one idea, which is the failure mode this set exists to avoid. Each of these was
        /// drawn to be a different shape from its neighbours.
        /// </summary>
        private static Texture2D[] MakeMarks()
        {
            return new[]
            {
                // Straight strokes.
                Mark(1, c => c.Polyline(0.11f, 0.24f, 0.14f, 0.66f, 0.50f, 0.24f, 0.86f)),
                Mark(2, c => c.Polyline(0.11f, 0.18f, 0.20f, 0.82f, 0.20f).Capsule(0.50f, 0.20f, 0.36f, 0.86f, 0.11f)),
                Mark(3, c => c.Polyline(0.11f, 0.18f, 0.86f, 0.42f, 0.18f, 0.66f, 0.86f).Capsule(0.84f, 0.20f, 0.84f, 0.86f, 0.11f)),
                Mark(4, c => c.Polyline(0.11f, 0.18f, 0.26f, 0.78f, 0.26f, 0.44f, 0.86f)),
                Mark(5, c => c.Capsule(0.24f, 0.14f, 0.24f, 0.86f, 0.11f).Polyline(0.11f, 0.24f, 0.50f, 0.80f, 0.24f).Capsule(0.24f, 0.50f, 0.80f, 0.78f, 0.11f)),
                Mark(6, c => c.Polyline(0.11f, 0.16f, 0.34f, 0.84f, 0.16f).Capsule(0.50f, 0.26f, 0.50f, 0.88f, 0.11f)),
                Mark(7, c => c.Polyline(0.11f, 0.20f, 0.86f, 0.50f, 0.16f, 0.80f, 0.86f).Capsule(0.32f, 0.56f, 0.68f, 0.56f, 0.10f)),
                Mark(8, c => c.Polyline(0.11f, 0.20f, 0.20f, 0.80f, 0.50f, 0.20f, 0.80f)),

                // Curves.
                Mark(9, c => c.Ring(0.5f, 0.5f, 0.34f, 0.34f, 0.11f).Capsule(0.5f, 0.06f, 0.5f, 0.24f, 0.10f)),
                Mark(10, c => c.Ring(0.44f, 0.42f, 0.28f, 0.28f, 0.11f).Capsule(0.62f, 0.62f, 0.86f, 0.90f, 0.11f)),
                Mark(11, c => c.Ring(0.5f, 0.36f, 0.30f, 0.22f, 0.10f).Ring(0.5f, 0.70f, 0.20f, 0.16f, 0.10f)),
                Mark(12, c => c.Polyline(0.11f, 0.22f, 0.86f, 0.22f, 0.34f, 0.50f, 0.16f, 0.78f, 0.34f, 0.78f, 0.86f)),
                Mark(13, c => c.Ring(0.5f, 0.5f, 0.36f, 0.36f, 0.10f).Erase(0.86f, 0.5f, 0.22f)),
                Mark(14, c => c.Ring(0.5f, 0.5f, 0.32f, 0.32f, 0.10f).Capsule(0.18f, 0.5f, 0.82f, 0.5f, 0.10f)),
                Mark(15, c => c.Polyline(0.11f, 0.20f, 0.18f, 0.72f, 0.34f, 0.24f, 0.56f, 0.80f, 0.86f)),
                Mark(16, c => c.Ring(0.42f, 0.62f, 0.26f, 0.26f, 0.10f).Capsule(0.42f, 0.10f, 0.42f, 0.42f, 0.10f).Capsule(0.42f, 0.20f, 0.82f, 0.20f, 0.10f)),

                // Marks with dots, which is what makes a run read as writing rather than as symbols.
                Mark(17, c => c.Polyline(0.11f, 0.22f, 0.80f, 0.50f, 0.30f, 0.78f, 0.80f).Disc(0.5f, 0.10f, 0.085f)),
                Mark(18, c => c.Capsule(0.22f, 0.30f, 0.78f, 0.30f, 0.11f).Disc(0.34f, 0.68f, 0.085f).Disc(0.66f, 0.68f, 0.085f)),
                Mark(19, c => c.Ring(0.5f, 0.60f, 0.28f, 0.24f, 0.10f).Disc(0.5f, 0.14f, 0.09f)),
                Mark(20, c => c.Capsule(0.5f, 0.14f, 0.5f, 0.86f, 0.11f).Disc(0.22f, 0.36f, 0.085f).Disc(0.78f, 0.64f, 0.085f)),
                Mark(21, c => c.Polyline(0.11f, 0.20f, 0.24f, 0.80f, 0.24f, 0.20f, 0.74f).Disc(0.78f, 0.78f, 0.09f)),

                // Headline marks: a bar across the top with the character hanging from it.
                Mark(22, c => c.Capsule(0.12f, 0.20f, 0.88f, 0.20f, 0.10f).Capsule(0.36f, 0.22f, 0.36f, 0.68f, 0.10f).Ring(0.56f, 0.70f, 0.22f, 0.18f, 0.10f)),
                Mark(23, c => c.Capsule(0.12f, 0.20f, 0.88f, 0.20f, 0.10f).Capsule(0.5f, 0.22f, 0.5f, 0.88f, 0.10f).Capsule(0.26f, 0.58f, 0.74f, 0.58f, 0.10f)),
                Mark(24, c => c.Capsule(0.12f, 0.20f, 0.88f, 0.20f, 0.10f).Polyline(0.10f, 0.28f, 0.22f, 0.28f, 0.60f, 0.72f, 0.88f)),
                Mark(25, c => c.Capsule(0.12f, 0.20f, 0.88f, 0.20f, 0.10f).Capsule(0.70f, 0.22f, 0.70f, 0.88f, 0.10f).Ring(0.40f, 0.64f, 0.22f, 0.20f, 0.10f)),

                // Solids, for weight in a run that would otherwise be all outline.
                Mark(26, c => c.Polygon(0.5f, 0.12f, 0.86f, 0.5f, 0.5f, 0.88f, 0.14f, 0.5f).Erase(0.5f, 0.5f, 0.14f)),
                Mark(27, c => c.Polygon(0.20f, 0.84f, 0.5f, 0.16f, 0.80f, 0.84f).Erase(0.5f, 0.72f, 0.14f)),
                Mark(28, c => c.Disc(0.5f, 0.5f, 0.24f).Capsule(0.5f, 0.06f, 0.5f, 0.26f, 0.09f).Capsule(0.5f, 0.74f, 0.5f, 0.94f, 0.09f)),
                Mark(29, c => c.Polygon(0.18f, 0.24f, 0.82f, 0.24f, 0.5f, 0.76f).Capsule(0.5f, 0.76f, 0.5f, 0.94f, 0.09f)),
                Mark(30, c => c.Snowflake(0.5f, 0.5f, 0.34f, 0.09f).Erase(0.5f, 0.5f, 0.10f))
            };
        }

        private delegate UIGlyphCanvas Drawing(UIGlyphCanvas canvas);

        /// <summary>
        /// One mark, named by its number so a texture in a memory profile can be traced back to a line above.
        ///
        /// A fresh canvas per mark, since <c>UIGlyphCanvas</c> is not reset by <c>ToTexture</c> -- reusing one
        /// would leave every mark carrying the ink of all the marks before it.
        /// </summary>
        private static Texture2D Mark(int number, Drawing drawing)
        {
            return drawing(new UIGlyphCanvas()).ToTexture("Gideon.UIOverhaul.Research.Mark" + number);
        }
    }
}
