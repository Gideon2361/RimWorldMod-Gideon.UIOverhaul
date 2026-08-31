using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Content
{
    /// <summary>
    /// Registers our bundled art the instant RimWorld finishes loading this mod's textures from disk.
    ///
    /// <b>The timing is the whole point.</b> Content loading is deferred --
    /// <c>ModContentPack.ReloadContent</c> hands the real work to <c>LongEventHandler</c> -- so nothing can
    /// safely add to the texture cache from a mod constructor, and by the time
    /// <c>StaticConstructorOnStartup</c> runs, other startup classes may already have asked for a texture and
    /// cached the failure. Sitting on the end of <c>ReloadContentInt</c> puts the bundled art in place in the
    /// same breath as the art from disk, before anything can look for either.
    ///
    /// <b>Patched here rather than on the lookup.</b> <c>ContentFinder</c> is generic, and methods on a
    /// generic type share one native implementation across every reference type argument, so a patch aimed at
    /// the <c>Texture2D</c> instantiation can also run for <c>AudioClip</c> and <c>Shader</c>.
    /// <c>ReloadContentInt</c> is an ordinary method on an ordinary class, with none of that.
    /// </summary>
    [HarmonyPatch(typeof(ModContentPack), "ReloadContentInt")]
    internal static class Patch_RegisterBundledTextures
    {
        private static void Postfix(ModContentPack __instance, bool hotReload)
        {
            // A hot reload re-reads files from disk and leaves the bundles alone, so the cache still holds
            // everything registered the first time.
            if (hotReload || __instance == null)
                return;

            UIGuard.Try("Content.RegisterBundled", () =>
            {
                int added = UIBundledTextures.Register(__instance);

                if (added > 0)
                {
                    Log.Message(UILogTag.Prefix + "Registered " + added + " textures from the asset bundle.");

                    return;
                }

                // Nothing registered is normal for every other mod, which has no manifest of ours to find,
                // and total failure for this one: all of its art ships in the bundle. So the report is worth
                // its cost here and only here, and it is a warning because a player seeing this has a mod
                // that is not working.
                if (UIBundleReport.IsOurs(__instance))
                    Log.Warning(UILogTag.Prefix + UIBundleReport.Compose(__instance, added));
            }, "Art shipped only in the asset bundle will draw as the missing texture placeholder.");
        }
    }
}
