using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Repaints the selectable rows of an option list from the active palette.
    ///
    /// These are not buttons, which is why restyling buttons left them alone. Vanilla draws them from
    /// four static Color fields -- OptionSelectedBGFillColor, OptionSelectedBGBorderColor and the
    /// Unselected pair -- and never touches the button atlases:
    ///
    ///   DrawOptionBackground(rect, selected)
    ///     -> selected ? DrawOptionSelected(rect) : DrawOptionUnselected(rect)
    ///     -> DrawHighlightIfMouseover(rect)
    ///
    /// The two leaf methods are patched rather than DrawOptionBackground itself, so vanilla keeps
    /// calling DrawHighlightIfMouseover afterwards and the hover feedback is unchanged.
    ///
    /// What this covers, all of it through the same two methods: the mod list and the category rows in
    /// Options (Dialog_Options.DoModOptions and DoCategoryRow -- the reason this exists), scenario
    /// selection, storyteller selection, starting pawns, the entity codex, xenotype creation, ideoligion
    /// presets, and choosing new wanderers.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "DrawOptionUnselected")]
    public static class Patch_Widgets_DrawOptionUnselected
    {
        public static bool Prefix(Rect rect)
        {
            return UIElementPainter.Paint(
                () => UIElementPainter.PaintOption(rect, UIColorPaletteDef.Active, false),
                "Option row background");
        }
    }

    [HarmonyPatch(typeof(Widgets), "DrawOptionSelected")]
    public static class Patch_Widgets_DrawOptionSelected
    {
        public static bool Prefix(Rect rect)
        {
            return UIElementPainter.Paint(
                () => UIElementPainter.PaintOption(rect, UIColorPaletteDef.Active, true),
                "Selected option row background");
        }
    }
}
