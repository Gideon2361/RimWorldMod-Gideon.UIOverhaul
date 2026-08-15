using System;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Panel
{
    /// <summary>
    /// Replaces the speed control glyphs with drawn ones, by swapping the textures vanilla already reaches for.
    ///
    /// <b>No patch, and that is the point.</b> <c>TimeControls.DoTimeControlsGUI</c> was the obvious target and
    /// carries far more than drawing: the force-pause and forced-normal-speed rules, the knowledge database entries,
    /// the per-speed sounds, and the keyboard shortcuts, which live in the same method after the buttons. Replacing
    /// it to change five pictures would mean owning all of that, and getting the shortcut half subtly wrong is the
    /// kind of bug a player reports as "pause key sometimes does nothing".
    ///
    /// <c>TexButton.SpeedButtonTextures</c> is a public array that vanilla indexes by <c>TimeSpeed</c>, so writing
    /// our own textures into it restyles every place the game draws a speed button -- the time controls, and
    /// anywhere else that reads the same array -- from one place. This is the same reasoning
    /// <c>TimeAssignmentColors</c> uses to recolor the schedule by rewriting the defs rather than patching each
    /// widget that draws them.
    ///
    /// <b>Vanilla's originals are kept, although nothing offers to switch back to them.</b> There was a setting
    /// once and it was removed: the choice it presented was between these glyphs and the ones they were drawn to
    /// replace. They are kept anyway, because a swap that cannot be undone is a swap that cannot be tested
    /// against, because the failure path below needs them to put a half-applied row back, and because a later
    /// per-surface setting -- vanilla's icons in one place and ours in another -- would be a loop over this array
    /// rather than a restart.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class SpeedGlyphs
    {
        /// <summary>
        /// Baked at twice the button's own 32 by 24, rather than square.
        ///
        /// <b>A square mask here would be visibly wrong, not merely imprecise.</b> Vanilla's speed button is 32 by
        /// 24 and <c>Widgets.ButtonImage</c> stretches whatever texture it is given to fill it, so a square glyph
        /// arrives a quarter shorter than it was drawn -- triangles come out squat and anything round comes out an
        /// ellipse. Matching the aspect means the shapes land at the proportions they were authored at.
        ///
        /// Twice rather than four times, because the whole texture is only 64 by 48 and these are simple shapes;
        /// the notification icons need the larger multiple because they draw at a third of this size.
        /// </summary>
        private const int Width = 64;

        private const int Height = 48;

        private static Texture2D[] originals;

        /// <summary>
        /// <b>Guarded, and written out rather than through <c>UIGuard.Try</c>,</b> because a static constructor that
        /// throws leaves the CLR marking the type as failed -- and this one is doing something to shared game state,
        /// so a partial application is the case worth defending against. If it fails halfway, some speed buttons are
        /// ours and some are vanilla's, which looks broken rather than unstyled; restoring what was swapped puts the
        /// whole row back to a consistent state.
        ///
        /// <b>Applied unconditionally.</b> There was a setting in front of this, and it was removed rather than
        /// defaulted on: the choice it offered was between these glyphs and the ones they were drawn to replace,
        /// which is not a decision worth a line in a settings window. <see cref="Restore"/> is kept even so, for
        /// the failure path below and because a swap that cannot be undone cannot be tested against.
        /// </summary>
        static SpeedGlyphs()
        {
            try
            {
                Apply();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Panel.SpeedGlyphs", ex,
                    "The speed controls keep their vanilla icons.");

                try
                {
                    Restore();
                }
                catch (Exception restoreFailure)
                {
                    UIGuard.Report("Panel.RestoreSpeedGlyphs", restoreFailure,
                        "Some speed buttons may show this mod's icons and others the vanilla ones.");
                }
            }
        }

        private static void Apply()
        {
            Texture2D[] slots = TexButton.SpeedButtonTextures;

            if (slots == null)
                return;

            originals = (Texture2D[]) slots.Clone();

            // Indexed by TimeSpeed, which is Paused, Normal, Fast, Superfast, Ultrafast. Written by index rather
            // than by name so a speed this mod has no glyph for keeps vanilla's rather than being cleared.
            Write(slots, TimeSpeed.Paused, BuildPause());
            Write(slots, TimeSpeed.Normal, BuildPlay(1));
            Write(slots, TimeSpeed.Fast, BuildPlay(2));
            Write(slots, TimeSpeed.Superfast, BuildPlay(3));
            Write(slots, TimeSpeed.Ultrafast, BuildUltra());
        }

        private static void Write(Texture2D[] slots, TimeSpeed speed, Texture2D glyph)
        {
            int index = (int) speed;

            if (index >= 0 && index < slots.Length && glyph != null)
                slots[index] = glyph;
        }

        /// <summary>Puts vanilla's textures back, for a failed apply and for whatever setting eventually wants it.</summary>
        internal static void Restore()
        {
            Texture2D[] slots = TexButton.SpeedButtonTextures;

            if (slots == null || originals == null)
                return;

            for (int i = 0; i < slots.Length && i < originals.Length; i++)
                slots[i] = originals[i];
        }

        // ---------------------------------------------------------------------------------------
        // The glyphs
        //
        // Authored in 32 units across by 24 down, which is the canvas's own space at this aspect: it keeps 32 units
        // of width and scales the height uniformly, so 64 by 48 pixels is 32 by 24 units. Laid out against 24
        // rather than 32, or they would fall off the bottom of the texture.
        //
        // Kept a couple of units clear of the top and bottom edges so there is a little air around the glyph inside
        // the button, rather than because anything would clip.
        // ---------------------------------------------------------------------------------------

        private static Texture2D BuildPause()
        {
            return new UIIconCanvas(Width, Height)
                .Rect(10f, 5f, 4.5f, 14f)
                .Rect(17.5f, 5f, 4.5f, 14f)
                .ToTexture("Gideon.Icon.SpeedPause");
        }

        /// <summary>
        /// One, two or three triangles, evenly spaced and centered as a group.
        ///
        /// Generated rather than written out per speed, so the three glyphs cannot drift apart in size or in how
        /// far they sit from the middle -- which is exactly the sort of thing that is invisible in isolation and
        /// obvious in a row.
        /// </summary>
        private static Texture2D BuildPlay(int count)
        {
            const float width = 9f;
            const float top = 5f;
            const float bottom = 19f;

            UIIconCanvas canvas = new UIIconCanvas(Width, Height);

            float total = count * width;
            float x = 16f - total * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float left = x + i * width;

                canvas.Triangle(left, top, left, bottom, left + width, (top + bottom) * 0.5f);
            }

            return canvas.ToTexture("Gideon.Icon.Speed" + count);
        }

        /// <summary>
        /// Ultrafast: two triangles against a bar, which is the shape everything else uses for "as far as it goes".
        ///
        /// Drawn even though vanilla's own controls skip this speed, because the array is indexed by the enum and
        /// something else may draw it -- and a slot left vanilla in a restyled row is more noticeable than one that
        /// is never shown.
        /// </summary>
        private static Texture2D BuildUltra()
        {
            return new UIIconCanvas(Width, Height)
                .Triangle(8f, 5f, 8f, 19f, 16f, 12f)
                .Triangle(15f, 5f, 15f, 19f, 23f, 12f)
                .Rect(23f, 5f, 3.5f, 14f)
                .ToTexture("Gideon.Icon.SpeedUltra");
        }
    }
}
