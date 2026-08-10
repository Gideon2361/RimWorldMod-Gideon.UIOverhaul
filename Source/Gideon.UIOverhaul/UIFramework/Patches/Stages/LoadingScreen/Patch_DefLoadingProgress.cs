using System.Xml;
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
        public static void Prefix(XmlDocument xmlDoc)
        {
            int total = xmlDoc?.DocumentElement?.ChildNodes?.Count ?? 0;
            UILoadingScreen.SetDefTotal(total);
        }
    }

    /// <summary>
    /// One def has been built and registered. Called tens of thousands of times on a large mod list,
    /// so the handler does nothing but take a lock and assign a couple of fields.
    /// </summary>
    [HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.AddDef))]
    public static class Patch_ModContentPack_AddDef
    {
        public static void Postfix(Def def)
        {
            Gideon.UIFramework.Stages.UILoadingScreen.ReportDef(def?.defName);
        }
    }
}
