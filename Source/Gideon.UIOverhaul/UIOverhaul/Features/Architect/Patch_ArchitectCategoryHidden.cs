using System;
using System.Collections.Generic;
using System.Xml;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Architect
{
    /// <summary>
    /// Reads the <c>hidden</c> field off category defs, before RimWorld parses them.
    ///
    /// A second prefix on this method, alongside the loading screen's def counter. Harmony runs both; they touch
    /// different things and neither depends on the other. Separate rather than folded together because they are
    /// separate features, and a loading-screen patch is the wrong place to keep an architect decision.
    /// </summary>
    [HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML))]
    public static class Patch_LoadedModManager_ArchitectHidden
    {
        /// <summary>
        /// Guarded because of where it sits. An escape from here would stop def processing before it began, and a
        /// game whose defs did not load does not start. Any category that would have been hidden simply is not.
        /// </summary>
        public static void Prefix(XmlDocument xmlDoc)
        {
            try
            {
                ArchitectCategoryVisibility.Ingest(xmlDoc);
            }
            catch (Exception ex)
            {
                UIGuard.Report("Architect.IngestHiddenCategories", ex,
                    "Architect categories marked hidden in XML stay visible, and RimWorld may report <hidden> "
                    + "as an unrecognized field on each of them.");
            }
        }
    }

    /// <summary>
    /// Reports a hidden category as not visible.
    ///
    /// <b>Why this is not enough on its own, and is still needed.</b> Vanilla's architect does not omit an
    /// invisible category, it greys the button out and answers a click with "Nothing available in category" --
    /// which is why <c>researchPrerequisites</c>, the trick this field replaces, never actually hid anything. The
    /// omission is done by the CacheDesPanels patch below.
    ///
    /// This patch is what makes everything else agree with that omission. The combined "everything at once" view
    /// walks <c>DefDatabase&lt;DesignationCategoryDef&gt;</c> directly and filters on Visible, as does anything
    /// another mod writes against the same property, so without this a hidden category would keep contributing its
    /// designators to searches while having no button of its own.
    ///
    /// <b>God mode is deliberately not an escape hatch here</b>, unlike vanilla's own use of Visible. God mode
    /// reveals things gated by progression, and this is not progression: it is an authoring decision about what
    /// the panel contains. Honoring it would also only half work, since the panel list is built once when the
    /// window is constructed and toggling god mode would not rebuild it.
    /// </summary>
    [HarmonyPatch(typeof(DesignationCategoryDef), nameof(DesignationCategoryDef.Visible), MethodType.Getter)]
    public static class Patch_DesignationCategoryDef_Visible
    {
        public static void Postfix(DesignationCategoryDef __instance, ref bool __result)
        {
            // Ordered so the common case is a single integer compare: almost no game hides any category, and this
            // getter is read several times a frame while the architect is open.
            if (!__result || !ArchitectCategoryVisibility.AnyHidden)
                return;

            try
            {
                if (ArchitectCategoryVisibility.IsHidden(__instance))
                    __result = false;
            }
            catch (Exception ex)
            {
                UIGuard.Report("Architect.HiddenCategoryVisible", ex,
                    "A category marked hidden in XML may still appear in the architect panel.");
            }
        }
    }

    /// <summary>
    /// Takes hidden categories out of the architect's category list, which is what actually hides them.
    ///
    /// <b>The list is the right seam.</b> It is built once per window and read by everything that draws or
    /// measures the panel: vanilla's own button loop, its <c>WinHeight</c>, its search state, and this mod's
    /// architect panel, which reads the same private field rather than building a second list. Removing an entry
    /// here therefore hides the category in the redesigned panel and in vanilla's, and shrinks the window to suit
    /// in both.
    ///
    /// Nothing in vanilla indexes that list by a fixed position, so removing from it is safe: the button loop
    /// steps two at a time and copes with any count, and every other use is a foreach or a Count.
    ///
    /// A postfix rather than a transpiler because the original builds the list from a def query, and filtering
    /// afterwards is both simpler to read and unaffected by any change to how that query is written.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Architect), "CacheDesPanels")]
    public static class Patch_MainTabWindow_Architect_CacheDesPanels
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_Architect, List<ArchitectCategoryTab>> PanelsOf =
            AccessTools.FieldRefAccess<MainTabWindow_Architect, List<ArchitectCategoryTab>>("desPanelsCached");

        public static void Postfix(MainTabWindow_Architect __instance)
        {
            if (!ArchitectCategoryVisibility.AnyHidden)
                return;

            UIGuard.Try("Architect.RemoveHiddenCategories", () =>
            {
                List<ArchitectCategoryTab> panels = PanelsOf(__instance);

                if (panels == null)
                    return;

                // Backwards, so removing an entry cannot skip the one after it.
                for (int i = panels.Count - 1; i >= 0; i--)
                {
                    if (ArchitectCategoryVisibility.IsHidden(panels[i]?.def))
                        panels.RemoveAt(i);
                }
            }, "Categories marked hidden in XML are drawn greyed out rather than being left off the panel.");
        }
    }
}
