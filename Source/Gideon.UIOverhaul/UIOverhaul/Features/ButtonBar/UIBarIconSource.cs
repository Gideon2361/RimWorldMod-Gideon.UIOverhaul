using System;
using System.Collections.Generic;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>One pickable icon: the path to persist, the texture to preview, and where it came from.</summary>
    public struct UIBarIcon
    {
        public string Path;
        public Texture2D Texture;
        public string Source;
    }

    /// <summary>
    /// The icons offered when choosing a bar button's icon.
    ///
    /// Two sources, both deliberately narrow. Every icon a MainButtonDef already declares, so anything the
    /// bar can show is choosable; and anything a mod dropped in Textures/UI/MainButtonIcons, which is the
    /// convention for contributing more. Enumerating every loaded texture instead would offer tens of
    /// thousands of images, almost none of which are bar icons, and a grid of that is not a picker.
    /// </summary>
    public static class UIBarIconSource
    {
        /// <summary>Folder a mod drops extra bar icons into, relative to its Textures folder.</summary>
        public const string IconFolder = "UI/MainButtonIcons";

        private static List<UIBarIcon> cached;

        /// <summary>
        /// Every icon on offer, sorted by path. Built once: the set cannot change without a def reload,
        /// and each entry costs a ContentFinder lookup.
        /// </summary>
        public static List<UIBarIcon> All => cached ?? (cached = Build());

        /// <summary>Drops the cache, so a def reload or a new mod is picked up.</summary>
        public static void Clear()
        {
            cached = null;
        }

        private static List<UIBarIcon> Build()
        {
            List<UIBarIcon> icons = new List<UIBarIcon>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddFromButtonDefs(icons, seen);
            AddFromIconFolders(icons, seen);
            AddFromContentHolders(icons, seen);

            icons.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            return icons;
        }

        private static void AddFromButtonDefs(List<UIBarIcon> icons, HashSet<string> seen)
        {
            foreach (MainButtonDef def in DefDatabase<MainButtonDef>.AllDefsListForReading)
            {
                if (def.iconPath.NullOrEmpty() || !seen.Add(def.iconPath))
                    continue;

                Texture2D texture = ContentFinder<Texture2D>.Get(def.iconPath, false);
                if (texture == null)
                    continue;

                icons.Add(new UIBarIcon
                {
                    Path = def.iconPath,
                    Texture = texture,
                    Source = def.modContentPack?.Name ?? "Unknown"
                });
            }
        }

        /// <summary>
        /// Walks each mod's Textures/UI/MainButtonIcons for image files, turning each into the
        /// extension-less path ContentFinder wants.
        ///
        /// The disk is read rather than asking ContentFinder for the folder's contents, because the path
        /// is what has to be stored and a Texture2D does not reliably carry it.
        /// </summary>
        private static void AddFromIconFolders(List<UIBarIcon> icons, HashSet<string> seen)
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            if (mods == null)
                return;

            foreach (ModContentPack mod in mods)
            {
                if (mod?.RootDir == null)
                    continue;

                foreach (string root in ContentRoots(mod))
                {
                    string folder;
                    try
                    {
                        folder = Path.Combine(root, "Textures", IconFolder.Replace('/', Path.DirectorySeparatorChar));
                        if (!Directory.Exists(folder))
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    string[] files;
                    try { files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories); }
                    catch { continue; }

                    foreach (string file in files)
                    {
                        string extension = Path.GetExtension(file).ToLowerInvariant();
                        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg"
                            && extension != ".dds")
                            continue;

                        // Path relative to Textures, forward-slashed and without the extension, which is
                        // exactly the form ContentFinder and our config both use.
                        string relative = file.Substring(Path.Combine(root, "Textures").Length)
                            .TrimStart('\\', '/')
                            .Replace('\\', '/');

                        int dot = relative.LastIndexOf('.');
                        if (dot > 0)
                            relative = relative.Substring(0, dot);

                        if (!seen.Add(relative))
                            continue;

                        Texture2D texture = ContentFinder<Texture2D>.Get(relative, false);
                        if (texture == null)
                            continue;

                        icons.Add(new UIBarIcon
                        {
                            Path = relative,
                            Texture = texture,
                            Source = mod.Name
                        });
                    }
                }
            }
        }

        /// <summary>
        /// The same folder, read out of what each mod has actually loaded rather than off the disk.
        ///
        /// <b>This exists because the disk stopped being the whole answer.</b> This mod's own art moved into
        /// an AssetBundle, so its icons are registered straight into the content holder and there is no file
        /// under <c>Textures/UI/MainButtonIcons</c> to walk any more. The folder pass found nothing of ours
        /// and the picker quietly lost every icon this mod ships, which is most of them. Reported on
        /// 2026-08-30, with twenty six icons listed where there should have been sixty.
        ///
        /// <b>The holder is keyed by path,</b> which is the objection the folder pass was written against: a
        /// Texture2D does not carry where it came from, but the dictionary holding it does, and that key is
        /// exactly the extension-less path both ContentFinder and our config use.
        ///
        /// <b>Both passes stay.</b> A mod shipping loose files and a mod shipping a bundle are both ordinary,
        /// several ship one of each, and the shared <c>seen</c> set means a texture reachable by either route
        /// is still listed once.
        /// </summary>
        private static void AddFromContentHolders(List<UIBarIcon> icons, HashSet<string> seen)
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            if (mods == null)
                return;

            string prefix = IconFolder + "/";

            foreach (ModContentPack mod in mods)
            {
                if (mod == null)
                    continue;

                Dictionary<string, Texture2D> loaded;

                try
                {
                    ModContentHolder<Texture2D> holder = mod.GetContentHolder<Texture2D>();

                    loaded = holder?.contentList;
                }
                catch
                {
                    continue;
                }

                if (loaded == null)
                    continue;

                // Copied before walking, because a lookup on a missing texture can load one and add to the
                // dictionary being enumerated. Sixty odd entries is nothing to copy and an InvalidOperation
                // thrown out of an icon picker is a window that never opens.
                List<string> paths = new List<string>(loaded.Keys);

                foreach (string path in paths)
                {
                    if (path.NullOrEmpty()
                        || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!seen.Add(path))
                        continue;

                    Texture2D texture;

                    if (!loaded.TryGetValue(path, out texture) || texture == null)
                        continue;

                    icons.Add(new UIBarIcon
                    {
                        Path = path,
                        Texture = texture,
                        Source = mod.Name
                    });
                }
            }
        }

        private static IEnumerable<string> ContentRoots(ModContentPack mod)
        {
            List<string> folders = mod.foldersToLoadDescendingOrder;

            if (folders != null)
            {
                foreach (string folder in folders)
                {
                    string trimmed = folder?.Trim();
                    if (trimmed.NullOrEmpty())
                        continue;

                    yield return Directory.Exists(trimmed)
                        ? trimmed
                        : Path.Combine(mod.RootDir, trimmed.TrimStart('/', '\\'));
                }
            }

            yield return mod.RootDir;
        }
    }
}
