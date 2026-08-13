using System;
using System.Xml;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;
using Gideon.UIFramework.Stages;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Counts def nodes so the longest phase of the load gets a bar that actually moves.
    ///
    /// The total comes from the unified XML document handed to ParseAndProcessXML: its top-level
    /// children are the def nodes about to be processed, so this is a real count rather than an
    /// estimate that has to be tuned per mod list.
    /// </summary>
    [HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML))]
    public static class Patch_LoadedModManager_ParseAndProcessXML
    {
        /// <summary>
        /// Guarded because of where it sits: an escape from here aborts def processing, and a game whose defs did
        /// not load does not start. A loading screen that cannot count is worth strictly less than that.
        /// </summary>
        public static void Prefix(XmlDocument xmlDoc)
        {
            try
            {
                int total = xmlDoc?.DocumentElement?.ChildNodes?.Count ?? 0;
                UILoadingScreen.SetDefTotal(total);
            }
            catch (Exception ex)
            {
                UIGuard.Report("LoadingScreen.CountDefs", ex);
            }
        }
    }

    /// <summary>
    /// One def has been built and registered. Called tens of thousands of times on a large mod list,
    /// so the handler does nothing but take a lock and assign a couple of fields.
    ///
    /// The guard is written out here rather than going through <c>UIGuard.Try</c>, and that is the only reason
    /// this looks different from the rest of the patches. A lambda would allocate a closure per call, which at
    /// this call count is real garbage; a try block that never throws costs nothing at all.
    /// </summary>
    [HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.AddDef))]
    public static class Patch_ModContentPack_AddDef
    {
        public static void Postfix(Def def)
        {
            try
            {
                Gideon.UIFramework.Stages.UILoadingScreen.ReportDef(def?.defName);
            }
            catch (Exception ex)
            {
                UIGuard.Report("LoadingScreen.ReportDef", ex);
            }
        }
    }
}
