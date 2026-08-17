using System.Collections.Generic;
using System.IO;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Makes the game see saves that are in subfolders.
    ///
    /// <b>One line of vanilla is the whole reason folders do not work.</b>
    /// <c>GenFilePaths.AllSavedGameFiles</c> is <c>directoryInfo.GetFiles()</c>, which does not recurse, so a
    /// save moved into a folder vanishes from the load list, from <c>SavedGameNamedExists</c>, and from the
    /// autosave numbering that depends on it. Every one of those reads this property, so widening it here
    /// fixes all of them at once and needs no second patch.
    ///
    /// <b>A postfix that adds, rather than a prefix that replaces.</b> Returning our own list would discard
    /// whatever another mod had contributed to the same property. Appending to what arrived keeps that, and
    /// the deduplication means it does not matter if a future RimWorld starts recursing on its own.
    /// </summary>
    [HarmonyPatch(typeof(GenFilePaths), nameof(GenFilePaths.AllSavedGameFiles), MethodType.Getter)]
    internal static class Patch_AllSavedGameFiles
    {
        public static void Postfix(ref IEnumerable<FileInfo> __result)
        {
            IEnumerable<FileInfo> original = __result;

            __result = UIGuard.Try("Saves.WidenEnumeration", () => Combined(original), original,
                "Saves inside folders are not listed.");
        }

        /// <summary>
        /// Everything the original found, plus everything in subfolders, newest first.
        ///
        /// <b>Enumerated here rather than yielded lazily,</b> which is a deliberate difference from vanilla.
        /// The ordering has to be applied across both sets, and a caller cannot be sorted lazily; more to the
        /// point, this property is read inside <c>foreach</c> loops that also delete and write files, and a
        /// live directory enumeration walking a folder while it changes is how that becomes an exception on
        /// somebody else's machine.
        /// </summary>
        private static IEnumerable<FileInfo> Combined(IEnumerable<FileInfo> original)
        {
            List<FileInfo> all = new List<FileInfo>();
            HashSet<string> seen = new HashSet<string>();

            if (original != null)
            {
                foreach (FileInfo file in original)
                {
                    if (file != null && seen.Add(file.FullName))
                        all.Add(file);
                }
            }

            foreach (FileInfo file in SaveFolders.AllSaves())
            {
                if (seen.Add(file.FullName))
                    all.Add(file);
            }

            all.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

            return all;
        }
    }

    /// <summary>
    /// Sends one save into a chosen folder.
    ///
    /// <b>Armed for a single save and never left standing.</b> <c>FilePathForSavedGame</c> is also what
    /// autosave asks, so a redirect that outlived its save would quietly file the next autosave wherever the
    /// player last chose by hand. <see cref="SaveWriter"/> sets it immediately before the write and clears it
    /// in a finally, which is why nothing else in the mod is allowed to touch it.
    ///
    /// <b>Matched on the name as well as the flag,</b> so that even inside the redirect window a save of some
    /// other name -- an autosave firing on the same frame -- goes where it would have gone anyway.
    ///
    /// <b>It also answers for reading, and that half is what makes folders usable at all.</b> Loading resolves
    /// a bare save name through this method: <c>SavedGameLoaderNow</c> is handed a name and asks here for the
    /// path. Vanilla returns <c>Saves/name.rws</c> unconditionally, so a save inside a folder could be listed
    /// and selected and then fail to open, which is the worst possible way for this feature to be wrong.
    /// Falling back to a search when the plain path does not exist fixes loading everywhere at once,
    /// including through the game's own load dialog.
    /// </summary>
    [HarmonyPatch(typeof(GenFilePaths), nameof(GenFilePaths.FilePathForSavedGame))]
    internal static class Patch_FilePathForSavedGame
    {
        public static bool Prefix(string gameName, ref string __result)
        {
            // Resolved into a local first, because a ref parameter cannot be assigned from inside the lambda
            // UIGuard takes. Null means vanilla should answer, which is the safe default throughout.
            string chosen = UIGuard.Try("Saves.ResolvePath", () => Resolve(gameName), null,
                "One save was looked for in the Saves folder rather than in its own folder.");

            if (chosen == null)
                return true;

            __result = chosen;

            return false;
        }

        private static string Resolve(string gameName)
        {
            string redirect = SaveFolders.Redirect;

            // Writing. The redirect wins outright, since the caller has just chosen where this goes.
            if (!redirect.NullOrEmpty()
                && string.Equals(Path.GetFileNameWithoutExtension(redirect), gameName,
                    System.StringComparison.OrdinalIgnoreCase))
                return redirect;

            if (gameName.NullOrEmpty())
                return null;

            // Reading. A file sitting where vanilla expects is vanilla's to answer for, which keeps this off
            // the common path entirely and means a folder can never shadow a save in the root.
            if (File.Exists(Path.Combine(SaveFolders.Root, gameName + ".rws")))
                return null;

            FileInfo found = SaveFolders.Find(gameName);

            return found == null ? null : found.FullName;
        }
    }
}
