using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Repaints every radio button in the game from the active palette.
    ///
    /// <c>Widgets.RadioButtonDraw</c> is the whole of vanilla's radio button drawing and the only reader of
    /// RadioButOnTex and RadioButOffTex. Its entire body is: pick one of the two textures, grey the color if
    /// disabled, draw it into a 24px square, restore the color. Its only callers are
    /// <c>Widgets.RadioButton</c> and <c>Widgets.RadioButtonLabeled</c>, so replacing it covers every radio
    /// button in the game and in any mod, with nothing behavioral touched -- the row hit target, the
    /// <c>Tick_Tiny</c> click sound and the label all stay in those callers.
    ///
    /// The method is private. Patching a private method is a version risk, which is accepted here because the
    /// alternative is patching the two public callers instead and reimplementing their label layout and hit
    /// handling to get at the drawing. If it is renamed in a future version the patch fails to apply, Harmony
    /// says so at startup, and radio buttons stay vanilla -- the same outcome as before this existed.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "RadioButtonDraw")]
    public static class Patch_Widgets_RadioButtonDraw
    {
        /// <summary>Returns false to replace vanilla's texture drawing outright.</summary>
        public static bool Prefix(float x, float y, bool chosen, bool disabled)
        {
            // The same rect vanilla builds: it takes no size and draws at the RadioButtonSize constant.
            Rect circle = new Rect(x, y, Widgets.RadioButtonSize, Widgets.RadioButtonSize);

            return UIElementPainter.Paint(() => UIElementPainter.PaintRadioButton(circle, chosen,
                UIColorPaletteDef.Active, disabled, Mouse.IsOver(circle)), "Radio button");
        }
    }
}
