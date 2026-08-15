using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;

namespace Gideon.UIOverhaul.Features.Panel
{
    /// <summary>
    /// Replaces the bottom right corner with <see cref="GlobalControlsPanel"/>.
    ///
    /// <b>Why the whole method.</b> <c>GlobalControlsOnGUI</c> is the corner: it walks a cursor up the right hand
    /// side and draws every readout against it, some through <c>GlobalControlsUtility</c> calls and some inline.
    /// The inline ones are the reason this patch exists. The temperature is a bare <c>Widgets.Label</c>, and the
    /// weather and the conditions have their cursor moved by the caller rather than by the method being called, so
    /// there was no seam to hide any of the three at -- which is why their settings existed with nothing behind
    /// them. Nothing smaller than this method makes them reachable.
    ///
    /// It also owns where the letter stack begins, since it ends by handing its leftover cursor to
    /// <c>LettersOnGUI</c>. That is why replacing it moves letters, and why message docking and letter width
    /// become possible once it is ours.
    ///
    /// <b>Guarded with <c>TryOnce</c>.</b> This draws several nested groups through vanilla's own readouts, and a
    /// throw partway through one leaves Unity's clip stack unbalanced for everything drawn afterwards. Retrying
    /// every frame would repeat that indefinitely, so the site retires on its first failure and vanilla's corner
    /// takes over for the rest of the session.
    ///
    /// The fallback is real rather than nominal: the hide patches on <c>GlobalControlsUtility</c> are still in
    /// place and only ever fire on that path, so a player whose panel has stood down still has working settings
    /// for the speed controls, the date, the clock and the toggle row. The three inline readouts go back to being
    /// unhideable, which is the state they were in before this existed.
    ///
    /// A prefix returns false to suppress the original, so success is false here and failure is true.
    /// </summary>
    [HarmonyPatch(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI))]
    public static class Patch_GlobalControls_GlobalControlsOnGUI
    {
        public static bool Prefix()
        {
            return !UIGuard.TryOnce("Panel.Corner", GlobalControlsPanel.Draw,
                "The bottom right corner is drawn RimWorld's own way for the rest of this session, and the "
                + "temperature, weather and conditions cannot be hidden while it is.");
        }
    }
}
