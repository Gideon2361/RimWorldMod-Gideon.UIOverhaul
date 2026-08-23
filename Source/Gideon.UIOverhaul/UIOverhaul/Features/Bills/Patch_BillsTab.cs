using System;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Widens the bills tab's pane to fit the cards.
    ///
    /// <b>The size belongs to the tab object, not to the drawing.</b> <c>ITab_Bills</c> sets <c>size</c> from its
    /// own <c>WinSize</c> in its constructor, and RimWorld lays the inspect pane out from that field, so
    /// drawing wider than it without changing it just puts our right hand column outside the pane and clips it.
    /// Widened on Aaron's report of the target column wrapping, 2026-08-19.
    ///
    /// <b>The constructor rather than the field.</b> There is one <c>ITab_Bills</c> instance per workbench def,
    /// each built once when its def is resolved, so a postfix here runs a few dozen times at startup and never
    /// again. Setting the field from the draw path instead would write it sixty times a second.
    ///
    /// <b>Height is the bench's own, and only its floor is set here.</b> The constructor runs at startup with no
    /// bench selected, so all it can honestly say is the minimum; the real figure depends on how many bills the
    /// bench being looked at holds and is set by <see cref="Patch_BillsTabHeight"/> each time the size is asked
    /// for.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Bills), MethodType.Constructor)]
    internal static class Patch_BillsTabSize
    {
        /// <summary>
        /// <c>InspectTabBase.size</c>, which is protected.
        ///
        /// Reached by reflection rather than by deriving from the tab, because the tab we need to resize is the
        /// one every workbench def already names. Looked up once: this runs per workbench def at startup, and a
        /// field lookup per instance would be waste.
        /// </summary>
        private static readonly FieldInfo Size = AccessTools.Field(typeof(InspectTabBase), "size");

        [HarmonyPostfix]
        public static void Postfix(ITab_Bills __instance)
        {
            UIGuard.Try("Bills.TabSize", () =>
            {
                if (Size == null)
                {
                    // Reported rather than ignored: without it the pane stays 420 and the cards are clipped on
                    // the right, which looks like our drawing is broken rather than like a field being renamed.
                    UIGuard.Report("Bills.TabSize",
                        new MissingFieldException("InspectTabBase.size could not be found"),
                        "The bills tab keeps RimWorld's narrower pane, so its right hand column is cut off.");

                    return;
                }

                Size.SetValue(__instance, new Vector2(WorkBenchBillsTab.Width, WorkBenchBillsTab.MinHeight));
            }, "The bills tab keeps RimWorld's narrower pane, so its right hand column is cut off.");
        }

        internal static void Apply(InspectTabBase tab, Vector2 size)
        {
            if (Size != null)
                Size.SetValue(tab, size);
        }
    }

    /// <summary>
    /// Sizes the bills tab to the bench it is about to show.
    ///
    /// <b>A fixed height is wrong at both ends.</b> A bench with two bills got a pane two thirds empty, and one
    /// with twelve got five cards behind a scrollbar. Aaron reported the second on 2026-08-22, with the tab's own
    /// list bar, the inspect pane's, and the horizontal bar that one forced, all on screen at once.
    ///
    /// <b><c>UpdateSize</c> is the seam RimWorld provides for exactly this,</b> and it is the right one rather
    /// than a convenient one: it is what <c>InspectTabBase.TabRect</c> calls before laying the tab out, and what
    /// this mod's own pane calls before deciding how tall to grow. Both therefore get the figure before it is
    /// needed rather than a frame late.
    ///
    /// <b>Patched on the base rather than on <c>ITab_Bills</c>,</b> which does not override it: naming the
    /// subclass would fail to find a method to patch. The type test costs one check per tab per frame and nothing
    /// else runs for anything that is not a bills tab.
    /// </summary>
    [HarmonyPatch(typeof(InspectTabBase), "UpdateSize")]
    internal static class Patch_BillsTabHeight
    {
        [HarmonyPostfix]
        public static void Postfix(InspectTabBase __instance)
        {
            if (!(__instance is ITab_Bills))
                return;

            UIGuard.Try("Bills.TabHeight", () =>
            {
                Building_WorkTable bench = Find.Selector == null
                    ? null
                    : Find.Selector.SingleSelectedThing as Building_WorkTable;

                // Nothing selected leaves the floor in place. This is called while the pane is closing as well as
                // while it is open, and shrinking to nothing on the way out would make the pane jump.
                if (bench == null)
                    return;

                Patch_BillsTabSize.Apply(__instance,
                    new Vector2(WorkBenchBillsTab.Width, WorkBenchBillsTab.HeightFor(bench)));
            }, "The bills tab keeps its minimum height, so a bench with many bills scrolls its list.");
        }
    }

    /// <summary>
    /// Replaces the contents of a workbench's Bills tab with this mod's own card list.
    ///
    /// <b>A bench's tab is now about that bench, and nothing else.</b> It used to switch the player to the colony
    /// wide bills tab with a filter set, which answered a different question from the one they asked by clicking
    /// on a workbench: they pointed at one bench and got the whole colony, with the main tab bar changing under
    /// them. Aaron asked for that removed on 2026-08-19 and for the growing zone's shape in its place.
    ///
    /// <b>Replacing the contents rather than registering a new ITab.</b> Every workbench def in the game and in
    /// every mod names <c>ITab_Bills</c>, so a tab of our own would have to be patched onto each of those def
    /// lists and would still leave the vanilla tab beside it. Prefixing <c>FillTab</c> reaches every bench that
    /// has the tab, including ones from mods that never heard of us, and it keeps the tab in the place players
    /// already click.
    ///
    /// <b>The vanilla tab's own state is left untouched.</b> Its paste button and its <c>mouseoverBill</c>
    /// tracking are skipped along with its drawing; nothing reads them once the body does not run, and
    /// <c>TabUpdate</c> handles a null perfectly well.
    ///
    /// <b>On failure it hands drawing back to RimWorld.</b> <c>Replaced</c> rather than <c>Try</c>, because a
    /// bench with no bills interface is a bench a player cannot use, and vanilla's cramped list is far better than
    /// an empty panel. See <c>no-vanilla-fallback</c> for why this is the exception: that rule is about our own
    /// windows never quietly handing off, and this is a panel drawn inside RimWorld's own tab rather than a window
    /// of ours.
    ///
    /// <b>Its return value is passed straight through, not negated.</b> <c>Replaced</c> already answers as a
    /// prefix does: false when we drew, true to hand the method back. This was written negated and shipped that
    /// way, which ran vanilla's list underneath ours and produced two interfaces stacked on top of each other.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Bills), "FillTab")]
    internal static class Patch_BillsTabFill
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return UIGuard.Replaced("Bills.TabFill", Draw,
                "RimWorld's own bills tab is drawn instead of ours.");
        }

        private static void Draw()
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                WorkBenchBillsTab.Draw(Find.Selector?.SingleSelectedThing as Building_WorkTable);
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
