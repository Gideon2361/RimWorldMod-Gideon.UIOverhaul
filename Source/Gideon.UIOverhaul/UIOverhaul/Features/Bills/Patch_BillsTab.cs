using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>The bills tab's own def, looked up once.</summary>
    [DefOf]
    public static class BillsDefOf
    {
        public static MainButtonDef Gideon_Bills;

        static BillsDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(BillsDefOf));
        }
    }

    /// <summary>
    /// Turns a workbench's Bills tab into the colony bills tab, filtered to that bench.
    ///
    /// <b>It opens the same tab the bottom button opens, not a window of its own.</b> That is what keeps the per
    /// bench view from drifting away from the colony view: one window class, one set of columns, one editor, and
    /// the bench is a filter on it. Pressing the button in the bottom row clears the filter.
    ///
    /// <b>Hooked on the tab opening rather than on it drawing.</b> <c>FillTab</c> runs every frame the tab is
    /// visible, so switching tabs from there would fight the player sixty times a second. <c>OnOpen</c> fires once.
    ///
    /// <b>Patched on the base class on purpose.</b> <c>ITab_Bills</c> does not declare <c>OnOpen</c> itself, so
    /// the patch goes on <c>InspectTabBase</c> where it is declared and tests the instance. Patching a method a
    /// type merely inherits attaches to the base anyway; doing it deliberately makes the filter visible rather
    /// than accidental.
    /// </summary>
    [HarmonyPatch(typeof(InspectTabBase), "OnOpen")]
    internal static class Patch_BillsTabOpen
    {
        [HarmonyPostfix]
        public static void Postfix(InspectTabBase __instance)
        {
            if (!(__instance is ITab_Bills))
                return;

            UIGuard.Try("Bills.TabOpen", () => Open(Find.Selector?.SingleSelectedThing as Building_WorkTable),
                "The bills tab did not open. RimWorld's own tab is still behind it.");
        }

        /// <summary>Switches to the bills tab and points it at one bench.</summary>
        internal static void Open(Building_WorkTable bench)
        {
            if (bench == null || BillsDefOf.Gideon_Bills == null)
                return;

            Find.MainTabsRoot?.SetCurrentTab(BillsDefOf.Gideon_Bills);

            // Read after the switch, because switching is what creates the window, and told to reread rather than
            // left to notice: PostOpen has already run by the time the filter arrives.
            MainTabWindow_Bills window = Find.WindowStack?.WindowOfType<MainTabWindow_Bills>();

            if (window == null)
                return;

            window.only = bench;

            window.Reread();
        }
    }

    /// <summary>
    /// Stops the vanilla bills tab drawing its cramped list behind ours.
    ///
    /// <b>Replaced rather than removed.</b> The tab still exists, because taking it away would remove the place
    /// players click and would fight every other mod that expects it to be there. It says where its contents went
    /// and offers the way there, which also leaves an obvious route back if the tab ever fails to open.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Bills), "FillTab")]
    internal static class Patch_BillsTabFill
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !UIGuard.Replaced("Bills.TabFill", Draw,
                "RimWorld's own bills tab is drawn instead of ours.");
        }

        private static void Draw()
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(12f, 12f, 396f, 60f),
                    "This bench's bills are on the Bills tab, in the row along the bottom.");

                if (BillButtons.Button(new Rect(12f, 78f, 180f, 30f), "Open bills", palette, true))
                    Patch_BillsTabOpen.Open(Find.Selector?.SingleSelectedThing as Building_WorkTable);
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }
    }
}
