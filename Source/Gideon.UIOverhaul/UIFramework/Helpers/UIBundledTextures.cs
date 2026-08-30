using System;
using System.Reflection;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Teaches RimWorld to look for <c>.dds</c> inside an AssetBundle, which it otherwise never does.
    ///
    /// <b>The gap is one array.</b> A DDS on disk loads fine:
    /// <c>ModContentLoader.AcceptableExtensionsTexture</c> lists <c>.dds</c> and hands the file to
    /// <c>ModDdsLoader</c>. The bundle path consults a different list --
    /// <c>ModAssetBundlesHandler.TextureExtensions</c>, which is only <c>.png</c>, <c>.psd</c>, <c>.jpg</c>
    /// and <c>.jpeg</c> -- so <c>ContentFinder.TryFindAssetInModBundles</c> builds candidate names from those
    /// four and a bundled DDS is never probed for. Nothing errors. The texture is simply not found and the
    /// caller draws the placeholder.
    ///
    /// <b>Nothing needs decoding.</b> Unity imports a DDS through <c>IHVImageFormatImporter</c>, its
    /// pass-through importer for block-compressed formats, so the bundle already contains a finished
    /// <c>Texture2D</c> holding exactly the payload that was authored. <c>ModDdsLoader</c> is for files on
    /// disk and has no part in this.
    ///
    /// <b>Why the array and not a Harmony patch.</b> The obvious patch is a postfix on
    /// <c>ContentFinder&lt;Texture2D&gt;.TryFindAssetInModBundles</c>, and it is a trap: methods on a generic
    /// type share one native implementation across every reference type argument, so patching the
    /// <c>Texture2D</c> instantiation can also run against the <c>AudioClip</c> and <c>Shader</c> ones, where
    /// the postfix's <c>ref Texture2D __result</c> would be bound to something that is not a texture. Editing
    /// the array is duller and cannot mistype anything.
    ///
    /// <b>This changes the lookup for every mod, deliberately.</b> The array is vanilla state, so any mod
    /// shipping a bundled DDS starts working too. That is strictly additive: the extension is only ever tried
    /// on the fallback path, after a filesystem miss, where the alternative is the null it already returns.
    /// </summary>
    internal static class UIBundledTextures
    {
        private const string Extension = ".dds";

        /// <summary>
        /// Adds the extension, once. Safe to call twice; the second call finds it already present.
        ///
        /// Reports rather than throws. A failure here costs the bundled art and nothing else, and it must not
        /// escape into the mod's constructor, where RimWorld's answer is to apply none of the mod at all.
        /// </summary>
        internal static void Enable()
        {
            try
            {
                FieldInfo field = typeof(ModAssetBundlesHandler).GetField("TextureExtensions",
                    BindingFlags.Public | BindingFlags.Static);

                if (field == null)
                {
                    Report("ModAssetBundlesHandler.TextureExtensions no longer exists.");

                    return;
                }

                string[] current = field.GetValue(null) as string[];

                if (current == null)
                {
                    Report("ModAssetBundlesHandler.TextureExtensions was not a string array.");

                    return;
                }

                foreach (string extension in current)
                {
                    if (string.Equals(extension, Extension, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                string[] extended = new string[current.Length + 1];

                Array.Copy(current, extended, current.Length);

                // Last, so the four vanilla formats are still tried first and a mod shipping both spellings
                // keeps whichever behavior it has today.
                extended[current.Length] = Extension;

                field.SetValue(null, extended);

                // Read back rather than assume. Writing a static readonly field through reflection is allowed
                // on the Mono runtime the game ships and refused on some others, and a refusal that threw
                // nothing would leave every bundled texture quietly missing.
                string[] after = field.GetValue(null) as string[];

                if (after == null || after.Length != extended.Length)
                {
                    Report("the runtime did not accept the change.");

                    return;
                }

                if (Prefs.LogVerbose)
                    Log.Message(UILogTag.Prefix + "Bundled DDS textures enabled.");
            }
            catch (Exception e)
            {
                Report(e.Message);
            }
        }

        private static void Report(string reason)
        {
            Log.Warning(UILogTag.Prefix + "Could not enable DDS lookup inside asset bundles: " + reason
                        + " Textures shipped only in the bundle will fall back to the missing texture "
                        + "placeholder.");
        }
    }
}
