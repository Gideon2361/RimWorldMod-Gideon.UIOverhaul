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
    /// <b>Height is left alone.</b> The pane grows upward from the bottom of the screen and 480 already holds
    /// five cards; taller would start covering the map for no gain, since the list scrolls.
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

                Size.SetValue(__instance, new Vector2(WorkBenchBillsTab.Width, WorkBenchBillsTab.Height));
            }, "The bills tab keeps RimWorld's narrower pane, so its right hand column is cut off.");
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
