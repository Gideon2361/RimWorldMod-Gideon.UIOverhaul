using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Colors the selected span of a range slider, which is the one part <see cref="UISliderSkin"/> cannot reach.
    ///
    /// <b>Why the texture swap could not do this.</b> The three range sliders draw their rail and their selected
    /// span with the same texture -- <c>BaseContent.WhiteTex</c>, which the whole game shares -- and tell them
    /// apart only by the color set immediately before each. The rail takes <c>RangeControlTextColor</c>, which is
    /// a field and therefore swappable. The span takes a literal <c>Color.white</c>, which is not. So the handles
    /// and rail themed correctly and the span between them stayed white, which is what it looked like: a blue
    /// control with a white bar through it.
    ///
    /// <b>An operand swap, not a rewrite.</b> This replaces one instruction: the <c>call</c> that loads
    /// <c>Color.white</c> becomes a <c>call</c> to <see cref="FillColor"/>. Both are static, take nothing and
    /// return a <c>Color</c>, so the stack is identical and every instruction that computes geometry, decides
    /// which handle was grabbed, or updates the drag state is left exactly as it was. That matters more here than
    /// usual: these methods own <c>draggingId</c> and <c>curDragEnd</c>, and a slider that grabs the wrong end is
    /// a real bug rather than a cosmetic one.
    ///
    /// <b>Only the first occurrence is replaced.</b> <c>Color.white</c> appears exactly once in each of these
    /// three methods and it is the span, so this is currently the same as replacing all of them -- but a later
    /// occurrence in some future version would be a reset back to white after drawing, and turning a reset into
    /// an accent would leak the color into whatever drew next.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_RangeSliderFill
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Widgets), nameof(Widgets.FloatRange));
            yield return AccessTools.Method(typeof(Widgets), nameof(Widgets.IntRange));
            yield return AccessTools.Method(typeof(Widgets), nameof(Widgets.QualityRange));
        }

        /// <summary>
        /// The color of the selected span.
        ///
        /// Read live rather than baked, unlike the handle textures next to it: this is a color rather than a
        /// texture, so it costs nothing to look up per draw and it follows a palette change immediately.
        /// </summary>
        public static Color FillColor()
        {
            return UIGuard.Try("Framework.RangeSliderFill",
                () => UIColorPaletteDef.Active.Accent, Color.white,
                "The selected part of a range slider is drawn white.");
        }

        /// <summary>
        /// Stands in for the first <c>GUI.DrawTexture</c> in each of these methods, which is the track.
        ///
        /// <b>The track needs its own color and cannot have one through the tint.</b> These methods set
        /// <c>GUI.color</c> once for the label and never reset it before drawing the track, so the two share a
        /// value -- and a track dark enough to read as a groove would make the label above it unreadable. Drawing
        /// it here breaks that tie: the label keeps the tint, the track gets <c>ControlBackgroundFaded</c>, which
        /// is the role for exactly this and the same one the toggle switch uses for its unlit body.
        /// </summary>
        public static void Track(Rect position, Texture image)
        {
            UIGuard.Try("Framework.RangeSliderTrack",
                () => Widgets.DrawBoxSolid(position, UIColorPaletteDef.Active.ControlBackgroundFaded),
                "The unfilled part of a range slider is drawn RimWorld's own way.");
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo white = AccessTools.PropertyGetter(typeof(Color), nameof(Color.white));
            MethodInfo fill = AccessTools.Method(typeof(Patch_RangeSliderFill), nameof(FillColor));

            MethodInfo drawTexture = AccessTools.Method(typeof(GUI), nameof(GUI.DrawTexture),
                new[] { typeof(Rect), typeof(Texture) });

            MethodInfo track = AccessTools.Method(typeof(Patch_RangeSliderFill), nameof(Track));

            bool filled = false;
            bool tracked = false;

            foreach (CodeInstruction instruction in instructions)
            {
                // The track is the first DrawTexture in the method; the fill is the second and the two handles
                // follow it. Only the first is taken, so the handles keep drawing their own texture.
                if (!tracked && drawTexture != null && track != null && instruction.Calls(drawTexture))
                {
                    tracked = true;

                    yield return new CodeInstruction(OpCodes.Call, track)
                        .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);

                    continue;
                }

                if (!filled && white != null && fill != null && instruction.Calls(white))
                {
                    filled = true;

                    yield return new CodeInstruction(OpCodes.Call, fill)
                        .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);

                    continue;
                }

                yield return instruction;
            }
        }
    }
}
