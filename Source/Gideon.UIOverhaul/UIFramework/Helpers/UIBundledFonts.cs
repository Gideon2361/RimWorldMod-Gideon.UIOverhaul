using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Fonts from the mod's AssetBundle, which is the same road RimWorld's own fonts travel.
    ///
    /// <b>This is the route Aaron chose after every other one was exhausted, and it is the right one.</b>
    /// Loading a TTF from disk is a stub in the engine RimWorld ships; registering it with the OS is invisible
    /// to the engine, whose font list is sealed before any mod code runs; and rendering glyphs ourselves from
    /// baked sheets meant reimplementing a font engine, which showed. A Font asset built into an AssetBundle
    /// with its file data included is none of those things: it is a genuine dynamic font, FreeType rasterizing
    /// hinted glyphs at whatever size a style asks, with real line metrics, rich text and full coverage --
    /// vanilla's machinery, fed our typeface.
    ///
    /// <b>The bundle is built offline with Unity 2022.3.35f1, the exact editor RimWorld was built with,</b>
    /// by the headless project in <c>ThirdParty/Fonts/BundleProject</c>. RimWorld loads every bundle in the
    /// mod's <c>AssetBundles/</c> folder on its own; this class only fishes the fonts out. A bundle with no OS
    /// suffix loads on every platform, and font data is platform agnostic, so one bundle serves Windows, Mac
    /// and Linux alike.
    /// </summary>
    internal static class UIBundledFonts
    {
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

        /// <summary>
        /// One font by asset name -- the TTF's file name without extension, such as
        /// <c>BarlowCondensed-Regular</c>. Null when no loaded bundle carries it, and the null is cached.
        /// </summary>
        internal static Font Get(string assetName)
        {
            Font existing;

            if (Fonts.TryGetValue(assetName, out existing))
                return existing;

            Font found = UIGuard.Try("UIText.BundleFont", () => Load(assetName), null, null);

            Fonts[assetName] = found;

            return found;
        }

        private static Font Load(string assetName)
        {
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod == null || mod.assemblies == null || mod.assemblies.loadedAssemblies == null)
                    continue;

                bool ours = false;

                foreach (System.Reflection.Assembly loaded in mod.assemblies.loadedAssemblies)
                {
                    if (loaded == typeof(UIBundledFonts).Assembly)
                    {
                        ours = true;

                        break;
                    }
                }

                if (!ours || mod.assetBundles == null)
                    continue;

                foreach (AssetBundle bundle in mod.assetBundles.loadedAssetBundles)
                {
                    if (bundle == null)
                        continue;

                    Font font = bundle.LoadAsset<Font>(assetName);

                    if (font != null)
                        return font;
                }
            }

            return null;
        }
    }
}
