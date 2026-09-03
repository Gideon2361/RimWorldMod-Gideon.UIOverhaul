using System;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// Whether the rebuilt pane applies to the pane being drawn.
    ///
    /// <b>Only the map's inspect pane, and only when the setting is on.</b> <c>WorldInspectPane</c> implements the
    /// same interface and goes through every one of these methods, and it is a different panel with its own
    /// geometry, its own label rules and its own contents. It is left entirely to RimWorld; a settlement's pane
    /// already gained what this rebuild would have added to it when backlog 31 put faction standing into the
    /// inspect string.
    /// </summary>
    internal static class InspectPaneTarget
    {
        internal static bool Ours(IInspectPane pane)
        {
            return pane is MainTabWindow_Inspect && InspectPaneMetrics.Enabled;
        }
    }

    /// <summary>
    /// Replaces the whole of the inspect pane's layout.
    ///
    /// <b>A prefix that returns false rather than a postfix that adds,</b> because there is nothing to add to:
    /// <c>InspectPaneOnGUI</c> <i>is</i> the layout, from the margin to the label's font to where the contents
    /// begin. Every affordance it carries is reproduced in <see cref="InspectPaneFrame"/> and in the same order,
    /// including the two that belong to other mods -- the pane buttons and the inspect string.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.InspectPaneOnGUI))]
    internal static class Patch_InspectPaneOnGUI
    {
        public static bool Prefix(Rect inRect, IInspectPane pane)
        {
            if (!InspectPaneTarget.Ours(pane))
                return true;

            return UIGuard.Replaced("Inspector.Pane", () => InspectPaneFrame.Draw(inRect, pane),
                "The inspect pane is RimWorld's own for the rest of this session.");
        }
    }

    /// <summary>
    /// Replaces the tab row with chips, and takes over the six pawn tabs that are rebuilt in the pane.
    ///
    /// <c>DoTabs</c> is private, which is not a reason to leave it alone: it is called from exactly one place,
    /// <c>ExtraOnGUI</c>, and it owns both the buttons and the decision to open an ITab window. Reproducing it is
    /// the only way to have a chip know it is selected.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
    internal static class Patch_InspectPaneDoTabs
    {
        public static bool Prefix(IInspectPane pane)
        {
            if (!InspectPaneTarget.Ours(pane))
                return true;

            return UIGuard.Replaced("Inspector.Tabs",
                () => InspectTabStrip.Draw(pane, UIFramework.Defs.UIColorPaletteDef.Active,
                    InspectPaneUtility.PaneWidthFor(pane)),
                "The inspect pane's tabs are RimWorld's own for the rest of this session.");
        }
    }

    /// <summary>
    /// Widens the pane to what its contents and its chips need.
    ///
    /// <b>Vanilla's answer is kept as the floor rather than replaced.</b> It is the tab count times 72, and the
    /// chips are still laid out in those slots, so a thing with ten tabs gets a wider pane here exactly as it
    /// does in the game.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.PaneWidthFor))]
    internal static class Patch_InspectPaneWidth
    {
        public static void Postfix(IInspectPane pane, ref float __result)
        {
            if (!InspectPaneTarget.Ours(pane))
                return;

            float vanilla = __result;

            __result = UIGuard.Try("Inspector.Width",
                () => InspectPaneMetrics.WidthFor(Mathf.Max(Mathf.Max(vanilla, InspectTabStrip.WidthNeeded(pane)),
                    InspectPaneMetrics.WidthForTab(pane))),
                vanilla, "The inspect pane is RimWorld's own width.");
        }
    }

    /// <summary>
    /// Gives the pane the height the player dragged it to.
    ///
    /// <c>MainTabWindow_Inspect.RequestedTabSize</c> reads this, and its <c>DoWindowContents</c> re-positions the
    /// window whenever the answer changes, so this is the whole of the resize: nothing writes
    /// <c>windowRect</c> and nothing has to be told the height has changed.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.PaneSizeFor))]
    internal static class Patch_InspectPaneSize
    {
        public static void Postfix(IInspectPane pane, ref Vector2 __result)
        {
            if (!InspectPaneTarget.Ours(pane))
                return;

            Vector2 vanilla = __result;

            __result = UIGuard.Try("Inspector.Size",
                () => new Vector2(vanilla.x, InspectPaneMetrics.HeightFor(pane)), vanilla,
                "The inspect pane is RimWorld's own height.");
        }
    }

    /// <summary>
    /// Moves the top of the pane up with its height.
    ///
    /// <b>This is what keeps the tab strip and every ITab window welded to the pane.</b>
    /// <c>InspectTabBase.TabRect</c> places its window at <c>PaneTopY - 30 - size.y</c> and the designator's
    /// extra controls are drawn from the same number, so a taller pane that left this alone would have its chips
    /// buried underneath it and every open tab floating in the middle of the map.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.PaneTopY), MethodType.Getter)]
    internal static class Patch_InspectPaneTopY
    {
        /// <summary>Whether this getter is already being answered further up the stack.</summary>
        [ThreadStatic] private static bool measuring;

        public static void Postfix(MainTabWindow_Inspect __instance, ref float __result)
        {
            if (!InspectPaneMetrics.Enabled)
                return;

            float vanilla = __result;

            // <b>Re-entrancy is guarded, and it is not a theoretical worry: it crashes the game to desktop.</b>
            // Reported by Criz on 2026-09-01 against Vanilla Psycasts Expanded. Our height is measured by asking
            // the hosted tab how big it wants to be, which runs the tab's own UpdateSize -- and a tab is entirely
            // within its rights to size itself against the pane, because RimWorld hands it PaneTopY to do exactly
            // that. VPE's psycast tree does: size.y = PaneTopY - 30f. That reaches back into this getter, which
            // measures again, which asks again, and the recursion is unbounded.
            //
            // A StackOverflowException cannot be caught, so UIGuard never sees this one and neither does
            // RimWorld: the process dies with nothing in the log at all. The guard is what makes that
            // impossible rather than what reports it.
            //
            // Vanilla's own number is the right answer to give the inner call. It is always defined, it does not
            // depend on anything we are part way through computing, and it is stable -- so a tab that sizes from
            // the pane sizes from a fixed number, the pane grows to fit it once, and it settles. Returning a
            // half-computed height instead would leave such a tab oscillating between two sizes every frame.
            //
            // Per thread for the same reason Patch_LogCapture is: the flag has to unwind with the stack that set
            // it, and a finally is what guarantees a throw through UIGuard does not leave it stuck on.
            if (measuring)
                return;

            measuring = true;

            try
            {
                __result = UIGuard.Try("Inspector.TopY",
                    () => UI.screenHeight - InspectPaneMetrics.HeightFor(__instance) - InspectPaneMetrics.BarHeight,
                    vanilla, "The inspect pane sits where RimWorld puts it.");
            }
            finally
            {
                measuring = false;
            }
        }
    }

    /// <summary>
    /// Forgets which body was showing when the pane is reset.
    ///
    /// <c>MainTabWindow_Inspect.Reset</c> runs when the game is torn down and rebuilt, which is exactly when a
    /// remembered tab and a scroll offset stop meaning anything: the next thing clicked is in a different colony.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.Reset))]
    internal static class Patch_InspectPaneReset
    {
        public static void Postfix()
        {
            UIGuard.Try("Inspector.Reset", InspectPaneState.Reset, null);
        }
    }
}
