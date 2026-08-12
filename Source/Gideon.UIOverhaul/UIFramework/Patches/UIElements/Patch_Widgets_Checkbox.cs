using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Repaints every boolean checkbox in the game from the active palette.
    ///
    /// <c>Widgets.CheckboxDraw</c> is the whole of vanilla's checkbox drawing and the only reader of
    /// CheckboxOnTex and CheckboxOffTex on this path. Its entire body is: dim the color if disabled, pick one
    /// of the two textures, draw it, restore the color. Replacing it therefore restyles every checkbox
    /// reached through <c>Widgets.Checkbox</c>, <c>Widgets.CheckboxLabeled</c> and
    /// <c>Widgets.CheckboxLabeledSelectable</c> -- which is all of them, in the options window, mod settings,
    /// bill details, storage filters, and any mod that draws one.
    ///
    /// It is also the narrowest seam available. Nothing about the behavior is touched: the click handling,
    /// the paint-dragging across a column of boxes, the mouseover sound and the label all live in the callers
    /// and are left alone. Patching <c>CheckboxLabeled</c> instead would have meant reimplementing its label
    /// layout and its <c>placeCheckboxNearText</c> rule for no gain.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxDraw))]
    public static class Patch_Widgets_CheckboxDraw
    {
        /// <summary>Returns false to replace vanilla's texture drawing outright.</summary>
        public static bool Prefix(float x, float y, bool active, bool disabled, float size,
            Texture2D texChecked, Texture2D texUnchecked)
        {
            // A caller that supplied its own art asked for that art specifically -- the sun lamp's radius
            // toggles and similar do -- and overpainting it would lose the meaning the image carried.
            if (texChecked != null || texUnchecked != null)
                return true;

            return UIElementPainter.Paint(() => UIElementPainter.PaintCheckbox(
                new Rect(x, y, size, size),
                active ? MultiCheckboxState.On : MultiCheckboxState.Off,
                UIColorPaletteDef.Active,
                disabled), "Checkbox");
        }
    }

    /// <summary>
    /// Repaints the tri-state checkboxes -- the ones in thing filter trees, where a category can be fully
    /// allowed, disallowed, or partly allowed.
    ///
    /// A postfix that paints over vanilla, rather than a prefix that replaces it. <c>CheckboxMulti</c> is not
    /// a drawing method: it bundles the draw with <c>ButtonImageDraggable</c>, the paint-dragging state held
    /// in two private statics, the mouseover sound, and the state cycling, and it returns the new state.
    /// Replacing it would mean reimplementing all of that from the outside and keeping the reimplementation
    /// correct across game updates -- for a widget whose failure mode is a filter tree that no longer sets
    /// filters, not a cosmetic one.
    ///
    /// Painting over it instead leaves every bit of that behavior vanilla. Our box is opaque and fills the
    /// same rect, so vanilla's texture is simply covered. The one visible difference is that the box shows the
    /// state the method just returned rather than the one it was called with, so a click reads a frame
    /// earlier than it used to.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxMulti))]
    public static class Patch_Widgets_CheckboxMulti
    {
        public static void Postfix(Rect rect, MultiCheckboxState __result)
        {
            UIElementPainter.Paint(() => UIElementPainter.PaintCheckbox(rect, __result,
                UIColorPaletteDef.Active, false), "Tri-state checkbox");
        }
    }
}
