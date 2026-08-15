using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Gives the plain slider the filled span the range sliders have, so the theme has no exceptions in it.
    ///
    /// <b>Vanilla never drew one here.</b> <c>HorizontalSlider</c> draws a rail and a handle and nothing between
    /// them, while <c>FloatRange</c> and its siblings draw a filled span from one handle to the other. Once the
    /// range sliders were themed, that inconsistency was the most visible thing left: two controls that do the
    /// same job, one showing how far along it is and one not.
    ///
    /// <b>Two operand swaps, no injected logic.</b> The method contains exactly one <c>DrawAtlas</c> call and
    /// exactly one <c>GUI.DrawTexture</c> call, so each can be pointed at a replacement with the same signature.
    /// Everything that computes the handle position, reads the drag, updates <c>sliderDraggingID</c> or plays the
    /// drag sound is left untouched -- which is the whole reason for doing it this way rather than replacing the
    /// method. A slider that grabs the wrong end, or one that stops making its sound, is a real bug; a slider
    /// without a fill was only a plain one.
    ///
    /// <b>Why the rail rect travels between the two.</b> The fill needs to span from the rail's left edge to the
    /// handle, and neither call knows both: the rail draw has the extent, the handle draw has the position. So
    /// the first records its rect and the second consumes it. They are sequential statements in one method on one
    /// thread, so there is nothing to race -- and the flag is cleared on read, so if the rail draw is ever
    /// transpiled away by another mod the handle simply draws without a fill instead of using a stale rect from
    /// some earlier slider.
    /// </summary>
    /// <remarks>
    /// <b>The argument types are not optional here.</b> <c>HorizontalSlider</c> has two overloads -- this one and
    /// a <c>ref float</c> wrapper that forwards to it -- and a <c>HarmonyPatch</c> attribute given only a name
    /// cannot choose between them. It does not fall back to a guess either: it throws
    /// <c>AmbiguousMatchException</c> out of <c>PatchAll</c>, which fails the whole mod's constructor and takes
    /// every other patch in the assembly down with it. Naming the parameters is what makes it resolve, and the
    /// wrapper is covered anyway because it forwards to this one.
    /// </remarks>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.HorizontalSlider), new[]
    {
        typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool),
        typeof(string), typeof(string), typeof(string), typeof(float)
    })]
    public static class Patch_HorizontalSliderFill
    {
        /// <summary>Thickness of the track and the filled span, matching the 4 pixels the range sliders use.</summary>
        private const float FillHeight = 4f;

        /// <summary>Width of the knob. Narrower than vanilla's 12 square, which read as a block rather than a grip.</summary>
        private const float KnobWidth = 9f;

        private static Rect railRect;
        private static bool railKnown;

        /// <summary>
        /// Stands in for <c>Widgets.DrawAtlas</c>: draws the track, and remembers where it was.
        ///
        /// <b>Drawn rather than tinted, and the atlas is ignored.</b> Swapping <c>SliderRailAtlas</c> for a flat
        /// texture was tried first and did not take: <c>Widgets</c> loads that field in its own static
        /// constructor from <c>ContentFinder</c>, and when that runs after ours it puts vanilla's beveled art
        /// back. Owning the draw sidesteps the ordering question entirely -- and it also frees the track from
        /// <c>RangeControlTextColor</c>, which tints whatever is drawn here and has to stay light enough for the
        /// slider labels it also colors. The track can be its own color now.
        /// </summary>
        public static void Rail(Rect rect, Texture2D atlas)
        {
            railRect = rect;
            railKnown = true;

            UIGuard.Try("Framework.SliderTrack", () =>
            {
                if (Event.current.type != EventType.Repaint)
                    return;

                Widgets.DrawBoxSolid(
                    new Rect(rect.x, rect.center.y - FillHeight * 0.5f, rect.width, FillHeight),
                    UIColorPaletteDef.Active.ControlBackgroundFaded);
            }, null);
        }

        /// <summary>
        /// Stands in for <c>GUI.DrawTexture</c>: draws the filled span up to the handle, then the knob.
        ///
        /// In that order, so the knob sits on top of its own fill rather than being cut in half by it. The
        /// texture is ignored for the same reason the atlas is: it arrives as whatever <c>Widgets</c> loaded, and
        /// drawing the knob directly gives it the switch's own face and rim without depending on a swap winning
        /// a race against a static constructor.
        /// </summary>
        public static void Handle(Rect position, Texture image)
        {
            if (railKnown)
            {
                railKnown = false;

                UIGuard.Try("Framework.SliderFill", () => Fill(position), null);
            }

            UIGuard.Try("Framework.SliderKnob", () => Knob(position), null);
        }

        /// <summary>
        /// The grip: a narrow accent bar with the same darker rim the toggle switch uses, so the two controls
        /// read as the same family.
        /// </summary>
        private static void Knob(Rect handle)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            Color face = palette.Accent;

            Rect knob = new Rect(handle.center.x - KnobWidth * 0.5f, handle.y, KnobWidth, handle.height);

            Widgets.DrawBoxSolid(knob, face);

            Color previous = GUI.color;
            GUI.color = new Color(face.r * RimFactor, face.g * RimFactor, face.b * RimFactor, face.a);
            Widgets.DrawBox(knob, 1);
            GUI.color = previous;
        }

        /// <summary>The switch's own rim factor, so a slider grip and a toggle are visibly the same material.</summary>
        private const float RimFactor = 0.65f;

        private static void Fill(Rect handle)
        {
            // Repaint only. The fill hits nothing, so drawing it during layout or an input pass is wasted work
            // several times a frame.
            if (Event.current.type != EventType.Repaint)
                return;

            float width = Mathf.Clamp(handle.center.x - railRect.x, 0f, railRect.width);

            if (width <= 0f)
                return;

            // DrawBoxSolid takes its color as an argument and resets GUI.color to white itself, so nothing here
            // needs to set or restore it -- and the caller's white is exactly what the handle texture wants next.
            Widgets.DrawBoxSolid(
                new Rect(railRect.x, railRect.center.y - FillHeight * 0.5f, width, FillHeight),
                UIColorPaletteDef.Active.Accent);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo drawAtlas = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawAtlas),
                new[] { typeof(Rect), typeof(Texture2D) });

            MethodInfo drawTexture = AccessTools.Method(typeof(GUI), nameof(GUI.DrawTexture),
                new[] { typeof(Rect), typeof(Texture) });

            MethodInfo rail = AccessTools.Method(typeof(Patch_HorizontalSliderFill), nameof(Rail));
            MethodInfo handle = AccessTools.Method(typeof(Patch_HorizontalSliderFill), nameof(Handle));

            foreach (CodeInstruction instruction in instructions)
            {
                if (drawAtlas != null && rail != null && instruction.Calls(drawAtlas))
                {
                    yield return new CodeInstruction(OpCodes.Call, rail)
                        .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);

                    continue;
                }

                if (drawTexture != null && handle != null && instruction.Calls(drawTexture))
                {
                    yield return new CodeInstruction(OpCodes.Call, handle)
                        .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);

                    continue;
                }

                yield return instruction;
            }
        }
    }
}
