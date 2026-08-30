using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Stops a stepped slider clicking at you forever after you let go of it.
    ///
    /// <b>The symptom.</b> Drag one of the Rimbody workout goals on the health tab to certain values and the drag
    /// sound keeps playing about thirteen times a second after the mouse is released. Closing the tab silences it;
    /// reopening it starts it again. Reported 2026-08-30.
    ///
    /// <b>The cause is two ways of rounding to the same step producing two different floats.</b> The last thing
    /// <c>Widgets.HorizontalSlider</c> does is round its working value to <c>roundTo</c> and then ask whether the
    /// result differs from the value it was handed:
    ///
    /// <code>
    ///     if (roundTo &gt; 0f) num = (float) Mathf.RoundToInt(num / roundTo) * roundTo;
    ///     if (value != num) CheckPlayDragSliderSound();
    /// </code>
    ///
    /// That comparison is meant to catch a drag, and it does. What it also catches is a caller whose stored value
    /// is the right number expressed as the wrong float. <c>RoundToInt(x / 0.1f) * 0.1f</c> and the far more
    /// natural <c>Mathf.Round(x * 10f) / 10f</c> agree to seven digits and disagree in the last bit, because
    /// <c>0.1f</c> is not a tenth. So the value goes in, comes back a millionth larger, is not equal to itself,
    /// and the slider plays its click -- on every frame the control is drawn, forever, because nothing ever
    /// converges. Nobody is dragging anything.
    ///
    /// <b>How often: 99 of the 501 tenths a workout goal can hold.</b> Roughly one value in five. 30.3 is one of
    /// them, which is the goal in the screenshot the report came with. A value that came out of the slider itself is
    /// always safe, which is why this only shows up after something else has written the field: our own
    /// <c>Clamped</c> helper on the Rimbody goals, or a settings file, where a float saved as "8.9" and parsed
    /// back is the decimal-nearest float and not the multiple-of-<c>0.1f</c> one.
    ///
    /// <b>Fixed here rather than at the four call sites.</b> The workout goals, the trade beacon radius in options,
    /// the same radius on the beacon window and the grav engine radius all pass <c>0.1f</c> and all have the bug
    /// latent in them; three of those read their value back from a text settings file, so it arrives in the losing
    /// form on every launch. Snapping once, on the way in, retires the whole class of it -- including for any
    /// slider added later, and for the vanilla screens that have it too, since the fault is RimWorld's rather than
    /// this mod's.
    ///
    /// <b>Nothing else changes, and that is checkable rather than hoped for.</b> The method rounds this value to
    /// this step anyway before returning it, so rounding it a moment earlier cannot alter what any caller
    /// receives: the arithmetic is idempotent, and the second application returns the first one's answer. The only
    /// other thing <c>value</c> reaches is the handle's x position, which moves by about four millionths of a
    /// pixel.
    /// </summary>
    /// <remarks>
    /// <b>The argument types are not optional,</b> for the reason recorded at length on
    /// <see cref="Patch_HorizontalSliderFill"/>: this method has a <c>ref float</c> wrapper overload beside it, a
    /// <c>HarmonyPatch</c> attribute given only a name cannot choose between the two, and the
    /// <c>AmbiguousMatchException</c> it throws instead used to take every patch in the mod down with it. The
    /// wrapper needs no patch of its own -- it forwards here.
    /// </remarks>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.HorizontalSlider), new[]
    {
        typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool),
        typeof(string), typeof(string), typeof(string), typeof(float)
    })]
    public static class Patch_SliderStepSound
    {
        /// <summary>
        /// Puts the incoming value into the same arithmetic the method's own rounding uses.
        ///
        /// <b>A prefix on <c>value</c> rather than a postfix on the sound,</b> because the sound is played from
        /// inside the method through a static that has no seam in it. Correcting the input is the smaller change
        /// and it fixes the comparison rather than muting its consequence -- a slider that is genuinely being
        /// dragged still clicks, which is the whole point of the sound.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(ref float value, float roundTo)
        {
            float incoming = value;

            value = UIGuard.Try("Framework.SliderStep", () => Snapped(incoming, roundTo), incoming, null);
        }

        private static float Snapped(float value, float roundTo)
        {
            // An unstepped slider has nothing to snap to and never makes the comparison that misfires.
            if (roundTo <= 0f)
                return value;

            float steps = value / roundTo;

            // RoundToInt is an int conversion underneath, so a value too large to count in steps would come back
            // as noise rather than as itself. Written as a failed less-than so a NaN takes this branch too.
            // Vanilla has the same hole and reaches it in the same breath; leaving the value alone here hands the
            // caller exactly what it would have got without this patch.
            if (!(Mathf.Abs(steps) < 1e7f))
                return value;

            return Mathf.RoundToInt(steps) * roundTo;
        }
    }
}
