using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Puts the art from our AssetBundle into RimWorld's own texture cache, so everything downstream finds it
    /// without knowing a bundle exists.
    ///
    /// <b>Why not let ContentFinder do it.</b> ContentFinder does have a bundle fallback, but it builds the
    /// name it looks for from <c>ModAssetBundlesHandler.TextureExtensions</c> -- <c>.png</c>, <c>.psd</c>,
    /// <c>.jpg</c>, <c>.jpeg</c> -- and never <c>.dds</c>, which is what our art is. Widening that array meant
    /// writing a public static readonly field by reflection, and the runtime declined, silently, which is how
    /// a hundred textures turned into placeholder squares with nothing in the log to say why. Registering the
    /// textures ourselves depends on no vanilla behavior we do not control.
    ///
    /// <b>It also fixes what the fallback could not.</b> A def with a multi-directional graphic resolves
    /// through <c>GetAllUnderPath</c>, which walks a prefix trie of known paths rather than probing names, so
    /// no extension list would ever have helped it. Filling the cache serves both roads at once.
    ///
    /// <b>Case is the reason the bundle carries a manifest.</b> Unity lower cases every asset name it stores;
    /// RimWorld's cache is an ordinary <c>Dictionary</c> and its trie is prefix matched, both case sensitive.
    /// So the real spelling is written into <c>_paths.txt</c> at bake time and read back here, rather than
    /// being guessed from the lower case names the bundle would otherwise report.
    /// </summary>
    internal static class UIBundledTextures
    {
        /// <summary>Tried in order. The mod's own art is all DDS; PNG is here for anything added later.</summary>
        private static readonly string[] Extensions = { ".dds", ".png" };

        private const string ManifestName = "_paths.txt";

        private static FieldInfo trieField;
        private static MethodInfo trieAdd;

        /// <summary>
        /// Registers every texture the manifest names, skipping any path already present so a file left on
        /// disk still wins. Returns how many were added, for the caller to log.
        /// </summary>
        internal static int Register(ModContentPack mod)
        {
            if (mod == null || mod.assetBundles == null || mod.assetBundles.loadedAssetBundles == null)
                return 0;

            ModContentHolder<Texture2D> holder = mod.GetContentHolder<Texture2D>();

            if (holder == null || holder.contentList == null)
                return 0;

            string packageId = mod.PackageIdPlayerFacing;
            string manifestPath = "Assets/Data/" + packageId + "/" + ManifestName;
            string root = "Assets/Data/" + packageId + "/Textures/";

            int added = 0;

            foreach (AssetBundle bundle in mod.assetBundles.loadedAssetBundles)
            {
                if (bundle == null)
                    continue;

                TextAsset manifest = bundle.LoadAsset<TextAsset>(manifestPath);

                if (manifest == null || manifest.text.NullOrEmpty())
                    continue;

                foreach (string line in manifest.text.Split('\n'))
                {
                    string path = line.Trim();

                    if (path.Length == 0 || holder.contentList.ContainsKey(path))
                        continue;

                    Texture2D texture = Load(bundle, root, path);

                    if (texture == null)
                    {
                        Log.Warning(UILogTag.Prefix + "The bundle names '" + path
                                    + "' but holds no texture for it.");

                        continue;
                    }

                    holder.contentList.Add(path, texture);

                    if (AddToTrie(holder, path))
                        added++;
                }
            }

            return added;
        }

        private static Texture2D Load(AssetBundle bundle, string root, string path)
        {
            foreach (string extension in Extensions)
            {
                Texture2D texture = bundle.LoadAsset<Texture2D>(root + path + extension);

                if (texture == null)
                    continue;

                // RimWorld names a texture after its file when loading from disk, and a few places read that
                // back rather than the path they asked for.
                int slash = path.LastIndexOf('/');

                texture.name = slash < 0 ? path : path.Substring(slash + 1);

                return texture;
            }

            return null;
        }

        /// <summary>
        /// The trie is private and has no public door, but it is an ordinary instance field, so reflection
        /// reaches it without any of the trouble a readonly static would bring.
        ///
        /// A path missing from the trie is not a broken texture -- a direct <c>Get</c> still finds it in the
        /// dictionary -- but it is invisible to folder enumeration, which is how multi-directional graphics
        /// are built. Failing loudly here is better than a pawn rendering as one frame.
        /// </summary>
        private static bool AddToTrie(ModContentHolder<Texture2D> holder, string path)
        {
            try
            {
                if (trieField == null)
                {
                    trieField = typeof(ModContentHolder<Texture2D>).GetField("contentListTrie",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (trieField == null)
                    {
                        Log.Warning(UILogTag.Prefix + "ModContentHolder.contentListTrie no longer exists, so "
                                    + "bundled art will not appear in folder lookups.");

                        return false;
                    }
                }

                object trie = trieField.GetValue(holder);

                if (trie == null)
                    return false;

                if (trieAdd == null)
                    trieAdd = trie.GetType().GetMethod("Add", new[] { typeof(string) });

                if (trieAdd == null)
                {
                    Log.Warning(UILogTag.Prefix + "The content trie has no Add(string), so bundled art will "
                                + "not appear in folder lookups.");

                    return false;
                }

                trieAdd.Invoke(trie, new object[] { path });

                return true;
            }
            catch (Exception e)
            {
                Log.Warning(UILogTag.Prefix + "Could not index '" + path + "' for folder lookups: " + e.Message);

                return false;
            }
        }
    }
}
