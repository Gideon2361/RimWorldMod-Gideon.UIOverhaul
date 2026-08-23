using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>
    /// The pictures the music player draws, rasterized in code.
    ///
    /// <b>Generated rather than shipped,</b> for the reasons <see cref="UIGlyphCanvas"/> already gives: a glyph
    /// tinted at draw time is legible on the light theme and the dark one from one definition, and there is no
    /// file on disk for another mod to shadow with a texture of the same path. A transport bar is also exactly the
    /// case this suits -- a play triangle and a pause pair are geometry, not artwork.
    ///
    /// <b>Eight playlist icons, not eighty.</b> The point of the icon is telling one row of the sidebar from
    /// another at a glance, which eight distinct silhouettes do. A larger set would mean scrolling a picker to
    /// choose between pictures that differ by details invisible at fourteen pixels.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MusicGlyphs
    {
        internal static readonly Texture2D Note;
        internal static readonly Texture2D Play;
        internal static readonly Texture2D Pause;
        internal static readonly Texture2D Next;
        internal static readonly Texture2D Previous;
        internal static readonly Texture2D Shuffle;
        internal static readonly Texture2D Repeat;
        internal static readonly Texture2D RepeatOne;
        internal static readonly Texture2D Speaker;
        internal static readonly Texture2D Star;
        internal static readonly Texture2D StarOutline;
        internal static readonly Texture2D Folder;
        internal static readonly Texture2D Drive;
        internal static readonly Texture2D Warning;
        internal static readonly Texture2D Dice;
        internal static readonly Texture2D Package;
        internal static readonly Texture2D Up;
        internal static readonly Texture2D Down;

        /// <summary>
        /// What a playlist may be marked with. The index into this is what
        /// <see cref="MusicPlaylist.Icon"/> holds, so the order is part of the saved file and must not be
        /// reshuffled -- appending is safe, reordering renames everybody's playlists' pictures.
        /// </summary>
        internal static readonly Texture2D[] PlaylistIcons;

        /// <summary>
        /// Guarded whole, like <c>UIBarGlyphs</c>: sixteen textures rasterized in code is a good deal more that
        /// can go wrong than a content lookup, and a throw here would leave the type failed and every draw call
        /// reaching for a glyph throwing again. The fields stay null instead and the callers fall back to text.
        ///
        /// The body has to sit in the try rather than in a helper because these are readonly fields, which only
        /// the static constructor itself may assign.
        /// </summary>
        static MusicGlyphs()
        {
            try
            {
                Note = MakeNote();
                Play = MakePlay();
                Pause = MakePause();
                Next = MakeSkip(false);
                Previous = MakeSkip(true);
                Shuffle = MakeShuffle();
                Repeat = MakeRepeat(false);
                RepeatOne = MakeRepeat(true);
                Speaker = MakeSpeaker();
                Star = MakeStar(true);
                StarOutline = MakeStar(false);
                Folder = MakeFolder();
                Drive = MakeDrive();
                Warning = MakeWarning();
                Dice = MakeDice();
                Package = MakePackage();
                Up = MakeChevron(true);
                Down = MakeChevron(false);

                PlaylistIcons = new[]
                {
                    Note, Star, MakeMoon(), MakeOrbit(), MakeBolt(), MakeFlag(),
                    new UIGlyphCanvas().Snowflake(0.5f, 0.5f, 0.34f, 0.08f)
                        .ToTexture("Gideon.UIOverhaul.Music.Snowflake"),
                    MakeDrop()
                };
            }
            catch (System.Exception ex)
            {
                UIGuard.Report("Music.Glyphs", ex,
                    "The music player draws its buttons as text because its icons could not be built.");
            }
        }

        /// <summary>The icon for a playlist, clamped so a hand-edited index cannot throw.</summary>
        internal static Texture2D PlaylistIcon(int index)
        {
            if (PlaylistIcons == null || PlaylistIcons.Length == 0)
                return null;

            return PlaylistIcons[Mathf.Clamp(index, 0, PlaylistIcons.Length - 1)];
        }

        internal static int PlaylistIconCount => PlaylistIcons != null ? PlaylistIcons.Length : 0;

        // -------------------------------------------------------------------------------------------

        /// <summary>A quaver: a filled head, a stem and a flag.</summary>
        private static Texture2D MakeNote()
        {
            return new UIGlyphCanvas()
                .Ellipse(0.38f, 0.72f, 0.19f, 0.15f)
                .Capsule(0.56f, 0.70f, 0.56f, 0.20f, 0.07f)
                .Capsule(0.56f, 0.22f, 0.82f, 0.32f, 0.07f)
                .ToTexture("Gideon.UIOverhaul.Music.Note");
        }

        /// <summary>A play triangle.</summary>
        private static Texture2D MakePlay()
        {
            return new UIGlyphCanvas()
                .Polygon(0.28f, 0.16f, 0.28f, 0.84f, 0.82f, 0.50f)
                .ToTexture("Gideon.UIOverhaul.Music.Play");
        }

        private static Texture2D MakePause()
        {
            return new UIGlyphCanvas()
                .Polygon(0.30f, 0.18f, 0.44f, 0.18f, 0.44f, 0.82f, 0.30f, 0.82f)
                .Polygon(0.56f, 0.18f, 0.70f, 0.18f, 0.70f, 0.82f, 0.56f, 0.82f)
                .ToTexture("Gideon.UIOverhaul.Music.Pause");
        }

        /// <summary>
        /// Two wedges and a bar, mirrored for the back button.
        ///
        /// Mirrored by reflecting each x about the centre rather than by a direction multiplier on offsets,
        /// which is the version that stayed readable: the arithmetic is the same shape for both buttons, so the
        /// two glyphs cannot end up subtly different sizes.
        /// </summary>
        private static Texture2D MakeSkip(bool back)
        {
            UIGlyphCanvas canvas = new UIGlyphCanvas()
                .Polygon(Flip(0.08f, back), 0.22f, Flip(0.08f, back), 0.78f, Flip(0.45f, back), 0.50f)
                .Polygon(Flip(0.42f, back), 0.22f, Flip(0.42f, back), 0.78f, Flip(0.79f, back), 0.50f)
                .Polygon(Flip(0.82f, back), 0.20f, Flip(0.93f, back), 0.20f,
                    Flip(0.93f, back), 0.80f, Flip(0.82f, back), 0.80f);

            return canvas.ToTexture(back
                ? "Gideon.UIOverhaul.Music.Previous"
                : "Gideon.UIOverhaul.Music.Next");
        }

        private static float Flip(float x, bool mirror)
        {
            return mirror ? 1f - x : x;
        }

        /// <summary>Two crossing arrows, which is what shuffle looks like everywhere.</summary>
        private static Texture2D MakeShuffle()
        {
            return new UIGlyphCanvas()
                .Capsule(0.08f, 0.30f, 0.66f, 0.70f, 0.10f)
                .Capsule(0.08f, 0.70f, 0.66f, 0.30f, 0.10f)
                .Polygon(0.60f, 0.56f, 0.60f, 0.84f, 0.92f, 0.70f)
                .Polygon(0.60f, 0.16f, 0.60f, 0.44f, 0.92f, 0.30f)
                .ToTexture("Gideon.UIOverhaul.Music.Shuffle");
        }

        /// <summary>
        /// A loop broken by an arrowhead, with a stroke through the middle for repeat-one.
        ///
        /// The gap is erased before the head is drawn, so the head reads as the end of the loop rather than a
        /// lump on a closed circle. Erase order matters here: it takes away whatever has been drawn so far, so
        /// anything meant to survive it goes on afterwards.
        /// </summary>
        private static Texture2D MakeRepeat(bool one)
        {
            UIGlyphCanvas canvas = new UIGlyphCanvas()
                .Ring(0.5f, 0.5f, 0.33f, 0.33f, 0.11f)
                .Erase(0.74f, 0.24f, 0.17f)
                .Polygon(0.56f, 0.06f, 0.56f, 0.36f, 0.86f, 0.21f);

            if (one)
            {
                // A numeral one rather than a dot: it sits beside the plain loop, and two circles differing by
                // a dot in the middle are two icons nobody can tell apart at a glance.
                canvas.Polygon(0.46f, 0.36f, 0.55f, 0.36f, 0.55f, 0.68f, 0.46f, 0.68f)
                    .Capsule(0.38f, 0.44f, 0.50f, 0.36f, 0.08f);
            }

            return canvas.ToTexture(one
                ? "Gideon.UIOverhaul.Music.RepeatOne"
                : "Gideon.UIOverhaul.Music.Repeat");
        }

        /// <summary>
        /// A speaker: a neck, a cone, and two waves.
        ///
        /// The waves are chevrons rather than arcs cut out of rings. A ring plus an erase to leave one arc also
        /// erases the cone the arc sits beside, which is what turned the first version of this into a bare
        /// crescent.
        /// </summary>
        private static Texture2D MakeSpeaker()
        {
            return new UIGlyphCanvas()
                .Polygon(0.08f, 0.40f, 0.24f, 0.40f, 0.24f, 0.60f, 0.08f, 0.60f)
                .Polygon(0.22f, 0.38f, 0.46f, 0.14f, 0.46f, 0.86f, 0.22f, 0.62f)
                .Polyline(0.075f, 0.57f, 0.34f, 0.645f, 0.50f, 0.57f, 0.66f)
                .Polyline(0.075f, 0.72f, 0.22f, 0.845f, 0.50f, 0.72f, 0.78f)
                .ToTexture("Gideon.UIOverhaul.Music.Speaker");
        }

        /// <summary>
        /// Five points from a hub, filled or outlined.
        ///
        /// Filled by drawing a capsule out to each point over a central disc, which is a star at any size this is
        /// drawn; the outline version is the same shape thinner, since a true outlined star needs a polygon.
        /// </summary>
        private static Texture2D MakeStar(bool filled)
        {
            UIGlyphCanvas canvas = new UIGlyphCanvas();

            float hub = filled ? 0.15f : 0.05f;
            float arm = filled ? 0.17f : 0.07f;

            canvas.Disc(0.5f, 0.5f, hub);

            for (int i = 0; i < 5; i++)
            {
                float angle = -Mathf.PI * 0.5f + Mathf.PI * 2f * i / 5f;

                canvas.Capsule(0.5f, 0.5f,
                    0.5f + Mathf.Cos(angle) * 0.33f, 0.5f + Mathf.Sin(angle) * 0.33f, arm);
            }

            return canvas.ToTexture(filled
                ? "Gideon.UIOverhaul.Music.Star"
                : "Gideon.UIOverhaul.Music.StarOutline");
        }

        private static Texture2D MakeFolder()
        {
            return new UIGlyphCanvas()
                .Capsule(0.20f, 0.30f, 0.44f, 0.30f, 0.10f)
                .Capsule(0.20f, 0.62f, 0.80f, 0.62f, 0.30f)
                .ToTexture("Gideon.UIOverhaul.Music.Folder");
        }

        private static Texture2D MakeDrive()
        {
            return new UIGlyphCanvas()
                .Ring(0.5f, 0.5f, 0.36f, 0.36f, 0.09f)
                .Disc(0.5f, 0.5f, 0.10f)
                .ToTexture("Gideon.UIOverhaul.Music.Drive");
        }

        private static Texture2D MakeWarning()
        {
            return new UIGlyphCanvas()
                .Polygon(0.50f, 0.10f, 0.94f, 0.88f, 0.06f, 0.88f)
                .Erase(0.50f, 0.48f, 0.085f)
                .Erase(0.50f, 0.72f, 0.06f)
                .ToTexture("Gideon.UIOverhaul.Music.Warning");
        }

        /// <summary>A die, for the game choosing rather than the player.</summary>
        private static Texture2D MakeDice()
        {
            return new UIGlyphCanvas()
                .Capsule(0.28f, 0.28f, 0.72f, 0.28f, 0.10f)
                .Capsule(0.28f, 0.72f, 0.72f, 0.72f, 0.10f)
                .Capsule(0.28f, 0.28f, 0.28f, 0.72f, 0.10f)
                .Capsule(0.72f, 0.28f, 0.72f, 0.72f, 0.10f)
                .Disc(0.36f, 0.36f, 0.06f)
                .Disc(0.64f, 0.64f, 0.06f)
                .Disc(0.50f, 0.50f, 0.06f)
                .ToTexture("Gideon.UIOverhaul.Music.Dice");
        }

        /// <summary>A crate, for a mod's own songs.</summary>
        private static Texture2D MakePackage()
        {
            return new UIGlyphCanvas()
                .Capsule(0.22f, 0.26f, 0.78f, 0.26f, 0.09f)
                .Capsule(0.22f, 0.74f, 0.78f, 0.74f, 0.09f)
                .Capsule(0.22f, 0.26f, 0.22f, 0.74f, 0.09f)
                .Capsule(0.78f, 0.26f, 0.78f, 0.74f, 0.09f)
                .Capsule(0.50f, 0.26f, 0.50f, 0.74f, 0.08f)
                .ToTexture("Gideon.UIOverhaul.Music.Package");
        }

        /// <summary>
        /// A chevron, for reordering and for the strip's disclosure caret.
        ///
        /// A stroked chevron rather than a filled triangle: these sit next to text at fourteen pixels, where a
        /// solid wedge reads as a blob and two strokes read as a direction.
        /// </summary>
        private static Texture2D MakeChevron(bool up)
        {
            float near = up ? 0.62f : 0.38f;
            float far = up ? 0.38f : 0.62f;

            return new UIGlyphCanvas()
                .Polyline(0.11f, 0.20f, near, 0.50f, far, 0.80f, near)
                .ToTexture(up ? "Gideon.UIOverhaul.Music.Up" : "Gideon.UIOverhaul.Music.Down");
        }

        private static Texture2D MakeMoon()
        {
            return new UIGlyphCanvas()
                .Disc(0.46f, 0.50f, 0.34f)
                .Erase(0.66f, 0.42f, 0.30f)
                .ToTexture("Gideon.UIOverhaul.Music.Moon");
        }

        private static Texture2D MakeOrbit()
        {
            return new UIGlyphCanvas()
                .Disc(0.5f, 0.5f, 0.13f)
                .Ring(0.5f, 0.5f, 0.38f, 0.20f, 0.07f)
                .ToTexture("Gideon.UIOverhaul.Music.Orbit");
        }

        private static Texture2D MakeBolt()
        {
            return new UIGlyphCanvas()
                .Polyline(0.11f, 0.60f, 0.10f, 0.36f, 0.50f, 0.62f, 0.50f, 0.40f, 0.90f)
                .ToTexture("Gideon.UIOverhaul.Music.Bolt");
        }

        private static Texture2D MakeFlag()
        {
            return new UIGlyphCanvas()
                .Capsule(0.30f, 0.12f, 0.30f, 0.88f, 0.09f)
                .Capsule(0.34f, 0.28f, 0.76f, 0.28f, 0.22f)
                .ToTexture("Gideon.UIOverhaul.Music.Flag");
        }

        private static Texture2D MakeDrop()
        {
            return new UIGlyphCanvas()
                .Disc(0.50f, 0.62f, 0.26f)
                .Capsule(0.50f, 0.18f, 0.50f, 0.56f, 0.16f)
                .ToTexture("Gideon.UIOverhaul.Music.Drop");
        }

    }
}
