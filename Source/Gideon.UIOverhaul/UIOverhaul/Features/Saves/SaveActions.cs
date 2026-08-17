using System;
using System.IO;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Deleting, renaming and moving a save, with its picture kept alongside it.
    ///
    /// <b>Every one of these goes through <see cref="SaveThumbnails"/>.</b> A save carries a <c>.png</c> beside
    /// it, and an action that moves or removes the save without it leaves a picture belonging to nothing --
    /// which nobody would notice until their Saves folder was full of them.
    ///
    /// <b>Failures come back as sentences, not exceptions.</b> Each of these is something a player asked for
    /// while looking at a window, and the answer to "that name is already taken" belongs in the window that is
    /// still open rather than in a log.
    /// </summary>
    internal static class SaveActions
    {
        /// <summary>
        /// Whether this file is the save the running game will autosave over, which makes it untouchable.
        ///
        /// <b>Only permadeath ties a running game to a filename, and it ties it completely.</b>
        /// <c>Autosaver.DoAutosave</c> writes to <c>Current.Game.Info.permadeathModeUniqueName</c> rather than
        /// to a numbered autosave, so renaming that file means the next autosave recreates the old name and the
        /// renamed copy stops being the game being played, and deleting it means the next autosave silently
        /// brings it back. Neither is a thing to let somebody do by accident.
        ///
        /// An ordinary save is safe to delete or rename while it is loaded. Nothing keeps the file open --
        /// Scribe closes it when the load finishes -- and no running state remembers where the game came from.
        /// </summary>
        internal static bool IsRunningPermadeathSave(FileInfo file)
        {
            if (file == null)
                return false;

            return UIGuard.Try("Saves.CheckPermadeath", () =>
            {
                Game game = Current.Game;

                if (game == null || game.Info == null || !game.Info.permadeathMode)
                    return false;

                string unique = game.Info.permadeathModeUniqueName;

                return !unique.NullOrEmpty()
                       && string.Equals(Path.GetFileNameWithoutExtension(file.Name), unique,
                           StringComparison.OrdinalIgnoreCase);
            }, false, null);
        }

        /// <summary>Why this save cannot be changed, or null when it can.</summary>
        internal static string Blocked(FileInfo file)
        {
            if (file == null)
                return "No save is selected.";

            if (!File.Exists(file.FullName))
                return "That save is no longer on disk.";

            return IsRunningPermadeathSave(file)
                ? "This is the permadeath game you are playing. It cannot be changed from here."
                : null;
        }

        /// <summary>
        /// Removes a save and its picture.
        ///
        /// A plain delete, by decision: no recycle bin and no holding folder of our own. What guards this is
        /// the confirmation in front of it, not a way back afterwards.
        /// </summary>
        internal static bool Delete(FileInfo file, out string failure)
        {
            failure = Blocked(file);

            if (failure != null)
                return false;

            string path = file.FullName;
            string reason = null;

            bool done = UIGuard.Try("Saves.Delete", () =>
            {
                File.Delete(path);

                // After the save, so a picture is never orphaned by a delete that failed halfway.
                SaveThumbnails.Remove(path);

                return true;
            }, false, null);

            if (!done && reason == null)
                reason = "That save could not be deleted. It may be open in another program.";

            failure = reason;

            return done;
        }

        /// <summary>Gives a save a new name, keeping it in the folder it is already in.</summary>
        internal static bool Rename(FileInfo file, string newName, out string failure)
        {
            return Relocate(file, newName, SaveFolders.FolderOf(file), out failure);
        }

        /// <summary>Moves a save to another folder under its existing name. Null folder means Saves itself.</summary>
        internal static bool Move(FileInfo file, string folder, out string failure)
        {
            return Relocate(file, file == null ? null : Path.GetFileNameWithoutExtension(file.Name), folder,
                out failure);
        }

        /// <summary>
        /// The one operation behind both renaming and moving, because they are the same thing: the file's path
        /// changes and its picture follows.
        ///
        /// <b>Names stay unique across every folder,</b> which is what vanilla already assumes -- see
        /// <see cref="SaveFolders"/>. So a rename is refused when the name is taken anywhere, not merely in the
        /// destination folder, and the check deliberately excludes the file being renamed so that changing only
        /// its folder is allowed.
        /// </summary>
        private static bool Relocate(FileInfo file, string newName, string folder, out string failure)
        {
            failure = Blocked(file);

            if (failure != null)
                return false;

            string cleaned = GenFile.SanitizedFileName(newName ?? string.Empty).Trim();

            if (cleaned.NullOrEmpty())
            {
                failure = "A save needs a name.";

                return false;
            }

            string from = file.FullName;
            string to = SaveFolders.PathFor(folder, cleaned);

            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return true;

            FileInfo clash = SaveFolders.Find(cleaned);

            if (clash != null && !string.Equals(clash.FullName, from, StringComparison.OrdinalIgnoreCase))
            {
                failure = "There is already a save called " + cleaned + ".";

                return false;
            }

            bool done = UIGuard.Try("Saves.Relocate", () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to) ?? SaveFolders.Root);

                File.Move(from, to);

                // Only once the save itself has moved. A picture that moved first and then failed would be
                // attached to a save that is still under its old name.
                SaveThumbnails.Move(from, to);

                return true;
            }, false, null);

            if (!done)
                failure = "That save could not be moved. The name may not be usable on this drive.";

            return done;
        }
    }
}
