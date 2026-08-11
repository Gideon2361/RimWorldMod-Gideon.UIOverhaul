using Gideon.UIFramework.Defs;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Repaints the background of every standard RimWorld button from the active palette.
    ///
    /// <c>Widgets.DrawButtonGraphic</c> is the whole of vanilla's button background, and the only
    /// reader of ButtonBGAtlas, ButtonBGAtlasMouseover and ButtonBGAtlasClick. Its entire body is:
    /// pick one of those three by hover and mouse-button state, then DrawAtlas. Replacing it therefore
    /// restyles every button in the game -- both Widgets.ButtonText overloads, both
    /// ButtonTextDraggable overloads, ListableOption.DrawOption and WidgetRow.ButtonText which go
    /// through them, and the three places calling it directly (the health overview tab, the entity tab,
    /// the prisoner tab). Any mod calling Widgets.ButtonText comes along for free.
    ///
    /// It is also the narrowest possible seam. Everything else about a button stays vanilla: the
    /// mouseover sound, the click and drag detection, the label, its color, the text anchor, the word
    /// wrap rule, and the inactive-button behavior all live in ButtonTextWorker and are untouched.
    /// Patching the worker instead would have meant reimplementing them, or flipping its drawBackground
    /// argument -- and that argument does double duty, also selecting the text anchor and whether the
    /// mouseover text color applies, so turning it off to suppress the background would quietly have
    /// changed how every label is aligned and tinted.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawButtonGraphic))]
    public static class Patch_Widgets_DrawButtonGraphic
    {
        /// <summary>Returns false to replace vanilla's atlas drawing outright.</summary>
        public static bool Prefix(Rect rect)
        {
            return UIElementPainter.Paint(() =>
            {
                // The same two tests vanilla uses to choose between its three atlases, so a button
                // lights up and depresses on exactly the frames it used to.
                bool over = Mouse.IsOver(rect);
                bool held = over && Input.GetMouseButton(0);

                UIElementPainter.PaintButton(rect, UIColorPaletteDef.Active, over, held);
            }, "Button background");
        }
    }
}
