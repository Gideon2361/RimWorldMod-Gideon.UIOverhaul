using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Content
{
    /// <summary>
    /// Stops RimWorld loading a mod's AssetBundles a second time when they are already in hand.
    ///
    /// <b>This exists so the loading screen can use our typefaces.</b> RimWorld loads bundles from
    /// <c>ModContentPack.ReloadContentInt</c>, which is deferred to <c>LongEventHandler</c> and therefore lands
    /// at the *end* of the load -- long after the loading screen and the loading console have finished drawing.
    /// Anything on those two screens asking for a bundled face got RimWorld's font instead, silently, because
    /// the font simply was not there yet.
    ///
    /// The fix is to ask for the bundles ourselves from the mod's constructor, which runs before defs load. That
    /// uses RimWorld's own loader, so the bundles land in the same list everything else reads. What it cannot do
    /// is survive the deferred call arriving later and loading the same files again: Unity refuses a second load
    /// of a file it already holds, returns null, and RimWorld logs a red "Could not load asset bundle".
    ///
    /// So this prefix skips the work when the handler already has bundles. It is deliberately not specific to
    /// our mod -- any mod that loads its own bundles early gets the same protection, and a mod that does not is
    /// untouched, because its list is still empty when the deferred call arrives.
    /// </summary>
    [HarmonyPatch(typeof(ModAssetBundlesHandler), nameof(ModAssetBundlesHandler.ReloadAll))]
    internal static class Patch_EarlyBundleLoad
    {
        private static bool Prefix(ModAssetBundlesHandler __instance)
        {
            if (__instance == null || __instance.loadedAssetBundles == null)
                return true;

            // Already loaded, by us or by an earlier call. Running again would only produce the double-load
            // error, since nothing here unloads between the two calls.
            return __instance.loadedAssetBundles.Count == 0;
        }
    }
}
