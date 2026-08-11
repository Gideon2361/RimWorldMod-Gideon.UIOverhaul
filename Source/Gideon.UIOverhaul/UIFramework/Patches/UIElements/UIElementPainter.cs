using System;
using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// The palette-driven backgrounds the patches in this folder paint.
    ///
    /// It exists because more than one vanilla method has to end up looking the same. A button and a
    /// selectable row in the options list are drawn by entirely unrelated code -- Widgets
    /// .DrawButtonGraphic against the button atlases, Widgets.DrawOptionUnselected against four static
    /// Color fields -- and if each patch carried its own copy of "our button look" the two would drift
    /// apart the first time one of them was adjusted.
    ///
    /// Not API, and not a control: it is shared between sibling patches, nothing more. Ask for a button
    /// control if our own windows need to draw one, and this moves to Controls.
    /// </summary>
    internal static class UIElementPainter
    {
        /// <summary>
        /// Set once any of our element painting has thrown, after which every patch in this folder
        /// hands its method back to vanilla for the rest of the session.
        ///
        /// Shared rather than one flag per patch: a failure here means palette drawing is broken, which
        /// is not a condition that applies to buttons but not to option rows. Reverting all of it keeps
        /// the UI internally consistent instead of half restyled.
        /// </summary>
        internal static bool Failed { get; private set; }

        /// <summary>
        /// Runs <paramref name="paint"/>, reporting once and disabling all of our element painting if it
        /// throws. Returns what a Harmony prefix should return: false when we painted, true to let the
        /// original method run.
        /// </summary>
        internal static bool Paint(Action paint, string what)
        {
            if (Failed)
                return true;

            try
            {
                paint();
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Gideon.UIFramework] {what} failed to draw; falling back to vanilla "
                              + "for every restyled UI element.\n" + ex, 0x17C0_10B1);
                Failed = true;
                return true;
            }
        }

        /// <summary>
        /// A button background: fill, state wash, border.
        ///
        /// Leaves GUI.color as it found it, which is what vanilla's DrawAtlas does. The label is drawn
        /// afterwards by ButtonTextWorker, which does not set a color on this path -- it captures the
        /// ambient one and restores it -- so anything left behind here tints the text. Widgets
        /// .DrawBoxSolid resets GUI.color to white rather than to the previous value, which is exactly
        /// how that would happen.
        /// </summary>
        internal static void PaintButton(Rect rect, UIColorPaletteDef palette, bool over, bool held)
        {
            Color previous = GUI.color;

            Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);

            // HoverOverlay and PressedOverlay carry alpha as part of their value: washes over the fill,
            // not replacements for it. The palette decides how strong its own feedback is.
            if (held)
                Widgets.DrawBoxSolid(rect, palette.PressedOverlay);
            else if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            GUI.color = over ? palette.BorderFocused : palette.Border;
            Widgets.DrawBox(rect, 1);

            GUI.color = previous;
        }

        /// <summary>
        /// A selectable row in an option list -- the mod list in Options, the category rows beside it,
        /// scenario and storyteller selection, and the rest.
        ///
        /// Painted with the button fill on purpose. These rows read as buttons and sit next to real
        /// ones, so giving them their own color is what made the options screen look untouched while
        /// every actual button had been restyled.
        ///
        /// No hover wash here: the caller, Widgets.DrawOptionBackground, follows this with
        /// DrawHighlightIfMouseover, and adding our own would light the row up twice.
        ///
        /// Ends on white rather than restoring the previous color, matching the postcondition of the
        /// vanilla methods this replaces -- both of them finish with GUI.color = Color.white, so callers
        /// are entitled to assume it.
        /// </summary>
        internal static void PaintOption(Rect rect, UIColorPaletteDef palette, bool selected)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);

            if (selected)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);

            GUI.color = selected ? palette.BorderFocused : palette.Border;
            Widgets.DrawBox(rect, 1);

            GUI.color = Color.white;
        }
    }
}
