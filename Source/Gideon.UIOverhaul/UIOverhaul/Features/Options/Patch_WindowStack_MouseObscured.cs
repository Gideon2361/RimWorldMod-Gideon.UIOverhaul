using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// Answers "is the cursor covered by another window" from the real cursor while a hosted settings page is
    /// being drawn under a transform of ours.
    ///
    /// <b>What this fixes.</b> A tooltip anywhere in a mod's settings page, shown inside our Options window,
    /// blinked in and out about twice a second and could not be read. It was destroying itself, and the loop is
    /// worth writing down because nothing about it is obvious from either end.
    ///
    /// <c>Mouse.IsOver</c>, which every tooltip goes through, is not the geometry test it looks like. It is
    /// <c>rect.Contains(mouse) &amp;&amp; !Mouse.IsInputBlockedNow</c>, and the blocking half consults
    /// <c>WindowStack.MouseObscuredNow</c>, which is
    /// <c>GetWindowAt(UI.MousePosUIInvertedUseEventIfCan) != currentlyDrawnWindow</c>. So a control reports
    /// "not hovered" whenever the topmost window under the cursor is not the window drawing it.
    ///
    /// A tooltip is itself a window. <c>ActiveTip.DrawTooltip</c> puts it on the stack as an immediate window at
    /// <c>WindowLayer.Super</c>, above every dialog, positioned at the cursor by
    /// <c>GenUI.GetMouseAttachedWindowPos</c>. That normally offsets the box clear of the cursor, sixteen right
    /// and fourteen down, which is precisely why a tooltip does not ordinarily cover the point that produced it.
    ///
    /// <b>The transform is what breaks that.</b> <c>UI.MousePosUIInvertedUseEventIfCan</c> reaches
    /// <c>GUIUtility.GUIToScreenPoint(Event.current.mousePosition / Prefs.UIScale)</c>, which is only correct
    /// while the ambient <c>GUI.matrix</c> is the one RimWorld itself set in <c>UI.ApplyUIScale</c>. See
    /// <see cref="Dialog_UIOptions"/>: a mod's page is handed the rect it was authored against and the
    /// coordinate space is scaled and moved to fit our pane, so during that call the matrix is ours and the
    /// conversion lands somewhere other than the cursor. Land inside the tooltip's own box, which sits a short
    /// way down and to the right, and the control that raised the tooltip is told it is not hovered.
    ///
    /// The tip is then dropped by <c>TooltipHandler.CleanActiveTooltips</c>, the box goes with it, the cursor is
    /// unobscured again, and the whole thing restarts from the four hundred and fifty millisecond delay. That is
    /// the blink.
    ///
    /// <b>Why the fix is a different mouse rather than a different matrix.</b> <c>UI.MousePositionOnUIInverted</c>
    /// reads <c>Input.mousePosition</c> and divides by <c>Prefs.UIScale</c>, so it owes nothing to
    /// <c>GUI.matrix</c> and cannot be thrown off by ours. It is also what vanilla itself falls back to when
    /// there is no event to ask, so this is the same number by a route that our transform cannot reach rather
    /// than a number of our own invention.
    ///
    /// <b>Scope.</b> Only while <see cref="Transformed"/> is set, which is only inside the guarded call that draws
    /// another mod's page. Every other consultation of this getter, and there are many per frame across the whole
    /// game, is left exactly as it was.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.MouseObscuredNow), MethodType.Getter)]
    public static class Patch_WindowStack_MouseObscured
    {
        /// <summary>
        /// Set for the duration of a hosted settings page's draw.
        ///
        /// A field rather than an argument because what has to be told is a Harmony prefix on somebody else's
        /// property, which takes nothing of ours. Cleared in the same finally that restores the matrix, so an
        /// exception inside another mod's page cannot leave every window in the game answering this differently.
        /// </summary>
        internal static bool Transformed;

        public static bool Prefix(WindowStack __instance, ref bool __result)
        {
            if (!Transformed || __instance == null)
                return true;

            // The same comparison vanilla makes, from a cursor that no transform can move.
            Vector2 cursor = UI.MousePositionOnUIInverted;

            __result = __instance.GetWindowAt(cursor) != __instance.currentlyDrawnWindow;

            return false;
        }
    }
}
