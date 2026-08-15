using System;
using System.Runtime.CompilerServices;
using Gideon.UIFramework.Defs;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// The two parts of a slider that can only be reached by writing to <c>Widgets</c>' own statics.
    ///
    /// <b>Most of the restyle is not here.</b> The tracks, the filled spans and the plain slider's grip are drawn
    /// by <c>Patch_HorizontalSliderFill</c> and <c>Patch_RangeSliderFill</c>, which swap the draw calls for their
    /// own. What is left are the two things those transpilers do not touch: the range sliders' handle texture,
    /// and the colour their labels are written in.
    ///
    /// <b>Started as the whole approach, and shrank for two reasons.</b> Swapping textures looked ideal because it
    /// needs no patch at all -- and neither slider implementation separates drawing from dragging, so patching
    /// them is genuinely risky. But a texture can only be tinted by whatever <c>GUI.color</c> already holds, and
    /// both implementations set that once for the label and never reset it before the track. So the track could
    /// not be dark without the label being dark too, and the mockup wanted both. Owning the draw calls was the
    /// only way to separate them.
    ///
    /// <b>The other reason is worse and worth recording:</b> the swap silently did not take. <c>Widgets</c> loads
    /// these textures in its own class constructor, which the CLR runs on first use rather than at a fixed point.
    /// Ours ran first, wrote its textures, and was overwritten with no error to show for it -- the sliders simply
    /// kept vanilla's artwork. <see cref="Apply"/> now forces that constructor before writing, which is the fix,
    /// and is why the remaining swaps can be trusted.
    ///
    /// <b>The handle texture's colour is baked rather than tinted,</b> because handles are drawn at
    /// <c>Color.white</c> and there is no tint to borrow. A palette change therefore needs a restart before the
    /// range handles follow it; everything drawn by the transpilers reads its colour live and changes at once.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class UISliderSkin
    {
        /// <summary>How much darker a handle's rim is than its face. The switch's own factor, so they match.</summary>
        private const float RimFactor = 0.65f;

        private static Texture2D originalRangeHandle;
        private static Color originalRangeColor;
        private static bool captured;

        static UISliderSkin()
        {
            // Written out rather than through UIGuard.Try because a failing static constructor leaves the CLR
            // marking the type as failed, and every later read then throws instead of returning. Same shape as
            // NotificationIcons and SpeedGlyphs.
            try
            {
                Apply();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Framework.SliderSkin", ex,
                    "Sliders keep RimWorld's own rail and handle artwork.");

                try
                {
                    Restore();
                }
                catch (Exception restoreFailure)
                {
                    UIGuard.Report("Framework.RestoreSliderSkin", restoreFailure,
                        "Some sliders may show this mod's artwork and others RimWorld's.");
                }
            }
        }

        /// <summary>
        /// Swaps in the themed rail, handles and rail colour.
        ///
        /// Safe to call again: the originals are captured only on the first pass, so a second call cannot record
        /// this mod's own artwork as the thing to restore.
        /// </summary>
        internal static void Apply()
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // <b>Vanilla's own static constructor first, and this is the whole reason the first attempt failed.</b>
            // Widgets loads these textures from ContentFinder in its class constructor, which the CLR runs on
            // first use rather than at a fixed point in startup. Ours ran earlier, wrote its textures, and was
            // then quietly overwritten when Widgets initialised -- so the sliders kept vanilla's artwork with no
            // error to show for it. Forcing that constructor now means the swap below is the last word.
            RuntimeHelpers.RunClassConstructor(typeof(Widgets).TypeHandle);

            if (!captured)
            {
                originalRangeHandle = RangeHandle;
                originalRangeColor = RangeColor;
                captured = true;
            }

            RangeHandle = RangeTab(palette);

            // Slider labels. This also tints the track in vanilla's own drawing, but nothing reaches that path
            // any more: Patch_HorizontalSliderFill and Patch_RangeSliderFill draw both tracks themselves, which
            // is what lets the track be dark without dragging the label down with it.
            RangeColor = palette.TextSecondary;
        }

        /// <summary>Puts RimWorld's own artwork back.</summary>
        internal static void Restore()
        {
            if (!captured)
                return;

            RangeHandle = originalRangeHandle;
            RangeColor = originalRangeColor;
        }

        // The two private statics still swapped, reached by name. The two-argument overload of
        // StaticFieldRefAccess returns a ref, so these read and assign like ordinary fields; the FieldInfo
        // overload returns a delegate instead and cannot be assigned through.
        //
        // SliderRailAtlas and SliderHandle were swapped here too and no longer are: the transpiler owns both of
        // those draws outright, so a texture nothing reads is a texture that can only mislead.

        private static Texture2D RangeHandle
        {
            get => AccessTools.StaticFieldRefAccess<Texture2D>(typeof(Widgets), "FloatRangeSliderTex");
            set => AccessTools.StaticFieldRefAccess<Texture2D>(typeof(Widgets), "FloatRangeSliderTex") = value;
        }

        private static Color RangeColor
        {
            get => AccessTools.StaticFieldRefAccess<Color>(typeof(Widgets), "RangeControlTextColor");
            set => AccessTools.StaticFieldRefAccess<Color>(typeof(Widgets), "RangeControlTextColor") = value;
        }

        /// <summary>
        /// The range slider's handle: a bar hugging the edge of the selected span.
        ///
        /// <b>Drawn for the left handle only, because the right one is the same texture mirrored.</b>
        /// <c>FloatRange</c> draws the right handle with a negative width, which flips it, so a shape whose solid
        /// edge is on the right becomes one whose solid edge is on the left -- both then face inward, toward the
        /// span they bound. A symmetric knob would have looked wrong at both ends for the same reason.
        /// </summary>
        private static Texture2D RangeTab(UIColorPaletteDef palette)
        {
            const int size = 32;
            Color face = palette.Accent;
            Color rim = Darken(face);

            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                name = "Gideon.Slider.RangeHandle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];

            // Rows are bottom-up in Unity textures, which does not matter for a shape symmetric top to bottom.
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool inside = x >= size / 2 && y >= 2 && y < size - 2;
                bool edge = inside && (x == size / 2 || y == 2 || y == size - 3);

                pixels[y * size + x] = inside ? (edge ? rim : face) : Color.clear;
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
        }

        /// <summary>A rectangle of <paramref name="fill"/> with a rim, which covers the rail and the knob both.</summary>
        private static Texture2D Solid(int width, int height, Color fill, Color rim, int rimWidth)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false)
            {
                name = "Gideon.Slider.Solid",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[width * height];

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool onRim = rimWidth > 0
                             && (x < rimWidth || y < rimWidth
                                              || x >= width - rimWidth || y >= height - rimWidth);

                pixels[y * width + x] = onRim ? rim : fill;
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
        }

        private static Color Darken(Color color)
        {
            return new Color(color.r * RimFactor, color.g * RimFactor, color.b * RimFactor, color.a);
        }
    }
}
