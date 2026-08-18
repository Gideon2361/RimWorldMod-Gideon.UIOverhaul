using System;
using System.Collections.Generic;
using System.IO;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Real folders under the Saves directory, and where the next save is written.
    ///
    /// <b>Directories on disk, not tags in a file of ours.</b> <c>GenFilePaths.AllSavedGameFiles</c> calls
    /// <c>GetFiles()</c> without recursing, so anybody who has ever tidied their Saves folder in Explorer
    /// watched every save in a subfolder disappear from the load list. This is not a new organizing scheme
    /// laid over the files; it is the game finally reading the organizing that was already there.
    ///
    /// <b>Names stay unique across every folder,</b> which is what vanilla already assumes:
    /// <c>SaveGameFilesUtility.SavedGameNamedExists</c> and the autosave numbering both work on bare names,
    /// and <c>FilePathForSavedGame</c> maps a name to exactly one path. Folders organize; they do not create
    /// a second namespace. Two saves called Riverbend in different folders would make an overwrite ambiguous
    /// and the load list unreadable, for no benefit anybody asked for.
    /// </summary>
    internal static class SaveFolders
    {
        /// <summary>The Saves directory itself, which is also the folder a save with no folder is in.</summary>
        internal static string Root => GenFilePaths.SavedGamesFolderPath;

        /// <summary>How a save sitting directly in Saves is described, where a folder name would go.</summary>
        internal const string RootLabel = "Saves";

        /// <summary>
        /// Where the next call to <c>GameDataSaveLoader.SaveGame</c> should write, or null for the default.
        ///
        /// <b>Armed for one save and cleared in a finally.</b> <c>FilePathForSavedGame</c> is asked by
        /// autosave as well, so a value left standing here would silently file the next autosave in whichever
        /// folder somebody last chose by hand. See <see cref="SaveWriter"/>, which is the only thing that sets
        /// it.
        /// </summary>
        internal static string Redirect;

        /// <summary>
        /// The folder the player last had chosen in a save or load window, so the next one opens where they left
        /// off.
        ///
        /// Three states, and they are not the same: <c>null</c> means nothing has been chosen yet, an empty string
        /// means the Saves root itself, and anything else is a folder. The two windows read the same field and
        /// resolve it their own way, because their idea of "no folder" differs -- the save window has to write
        /// somewhere and treats no folder as the root, while the load window can show everything at once.
        ///
        /// <b>Held here rather than in the settings file.</b> It is a position, not a preference: writing it to
        /// disk would mean a settings write on every folder click, and a file whose contents change when nobody
        /// asked for a setting to change. It lasts as long as the game is running, which is what "the next time
        /// they open it" means in practice.
        /// </summary>
        internal static string LastFolder;

        /// <summary>
        /// Every folder under Saves, by name, alphabetically.
        ///
        /// One level deep on purpose. A tree would need a tree control, a move-into-parent gesture and a
        /// story for what happens to a nested folder when its parent is deleted, and none of that earns its
        /// place before anybody has asked for more than a handful of folders.
        /// </summary>
        internal static List<string> Names()
        {
            return UIGuard.Try("Saves.ListFolders", () =>
            {
                List<string> found = new List<string>();
                DirectoryInfo root = new DirectoryInfo(Root);

                if (!root.Exists)
                    return found;

                foreach (DirectoryInfo child in root.GetDirectories())
                {
                    // Vanilla's own backup folder is not somewhere to file a save by hand, and hidden
                    // folders are the operating system's business rather than the player's.
                    if ((child.Attributes & FileAttributes.Hidden) != 0)
                        continue;

                    found.Add(child.Name);
                }

                found.Sort(StringComparer.OrdinalIgnoreCase);

                return found;
            }, new List<string>(), "Save folders are not listed.");
        }

        /// <summary>
        /// The folder a save file sits in, or null when it is directly in Saves.
        ///
        /// Compared on the parent directory rather than by string matching the path, so a save reached
        /// through a junction or a differently cased path still resolves.
        /// </summary>
        internal static string FolderOf(FileInfo file)
        {
            return UIGuard.Try("Saves.FolderOf", () =>
            {
                DirectoryInfo parent = file?.Directory;

                if (parent == null)
                    return null;

                DirectoryInfo root = new DirectoryInfo(Root);

                string here = parent.FullName.TrimEnd(Path.DirectorySeparatorChar);
                string top = root.FullName.TrimEnd(Path.DirectorySeparatorChar);

                if (string.Equals(here, top, StringComparison.OrdinalIgnoreCase))
                    return null;

                // <b>The whole path below Saves, not just the immediate parent's name.</b> AllSaves enumerates
                // with SearchOption.AllDirectories, so a save can legitimately sit two or more levels down --
                // and returning only "B" for Saves\A\B meant PathFor rebuilt it as Saves\B. That made an
                // overwrite look like a different file to SaveWriter, which then wrote the shallow path and
                // deleted the original as though the save had been moved. The player saw their save disappear.
                //
                // Combine takes a relative path with separators in it without complaint, so rebuilding is exact
                // at any depth and the round trip through FolderOf and PathFor lands on the same file.
                return here.Length > top.Length
                       && here.StartsWith(top, StringComparison.OrdinalIgnoreCase)
                    ? here.Substring(top.Length).TrimStart(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                    : parent.Name;
            }, null, null);
        }

        /// <summary>The full path a save of this name would take in this folder. Null folder means Saves.</summary>
        internal static string PathFor(string folder, string saveName)
        {
            string directory = folder.NullOrEmpty() ? Root : Path.Combine(Root, folder);

            return Path.Combine(directory, saveName + ".rws");
        }

        /// <summary>
        /// Creates a folder, reporting why not rather than throwing.
        /// </summary>
        /// <param name="failure">Null when it was created or already existed.</param>
        internal static bool Create(string name, out string failure)
        {
            failure = null;

            string cleaned = GenFile.SanitizedFileName(name ?? string.Empty).Trim();

            if (cleaned.NullOrEmpty())
            {
                failure = "A folder needs a name.";

                return false;
            }

            string reason = null;

            bool made = UIGuard.Try("Saves.CreateFolder", () =>
            {
                string path = Path.Combine(Root, cleaned);

                if (Directory.Exists(path))
                {
                    reason = "There is already a folder called " + cleaned + ".";

                    return false;
                }

                Directory.CreateDirectory(path);

                return true;
            }, false, "That folder could not be created.");

            if (!made && reason == null)
                reason = "That folder could not be created. The name may not be usable on this drive.";

            failure = reason;

            return made;
        }

        /// <summary>
        /// Every save file anywhere under Saves, including subfolders.
        ///
        /// This is what <c>AllSavedGameFiles</c> should have been. Ordered the way vanilla orders it, newest
        /// written first, because callers that take the first match are relying on that.
        /// </summary>
        internal static List<FileInfo> AllSaves()
        {
            return UIGuard.Try("Saves.EnumerateAll", () =>
            {
                List<FileInfo> found = new List<FileInfo>();
                DirectoryInfo root = new DirectoryInfo(Root);

                if (!root.Exists)
                    return found;

                foreach (FileInfo file in root.GetFiles("*.rws", SearchOption.AllDirectories))
                {
                    // The pattern is not trusted on its own. Windows matches a search pattern against each
                    // file's 8.3 short name as well as its real one, so "*.rws" can return a file whose actual
                    // extension is something else entirely -- the working file compression writes beside a save
                    // being the case that prompted this. Checking the real name costs nothing and means no
                    // temporary file can ever be offered to somebody as a colony to load.
                    if (".rws".Equals(file.Extension, StringComparison.OrdinalIgnoreCase))
                        found.Add(file);
                }

                found.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

                return found;
            }, new List<FileInfo>(), "Saves in subfolders are not listed.");
        }

        /// <summary>The existing save with this name, wherever it is, or null.</summary>
        /// <summary>
        /// Whether two paths name the same file on disk.
        ///
        /// <b>Never compare save paths as raw strings.</b> One side of a comparison is typically built by
        /// <see cref="PathFor"/> out of <see cref="Root"/>, and the other read back off a <c>FileInfo</c>; the two
        /// can describe one file in two spellings -- separator style, a relative segment, casing, a junction in
        /// the middle. A comparison that says "different" about one file is what lets a delete meant for the old
        /// copy land on the new one.
        ///
        /// <b>Doubt resolves to same.</b> If either path cannot be canonicalized the answer is true, because every
        /// caller uses this to decide whether removing a file is safe, and the safe answer when we do not know is
        /// that it is the file we just wrote.
        /// </summary>
        internal static bool SamePath(string first, string second)
        {
            if (first.NullOrEmpty() || second.NullOrEmpty())
                return true;

            return UIGuard.Try("Saves.SamePath",
                () => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                    StringComparison.OrdinalIgnoreCase), true, null);
        }

        internal static FileInfo Find(string saveName)
        {
            if (saveName.NullOrEmpty())
                return null;

            foreach (FileInfo file in AllSaves())
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(file.Name), saveName,
                        StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return null;
        }
    }
}
