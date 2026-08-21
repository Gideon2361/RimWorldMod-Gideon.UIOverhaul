using System.IO;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Writes a save into a chosen folder, and is the only thing allowed to arm the path redirect.
    ///
    /// <b>Vanilla's own save call does the writing.</b> <c>GameDataSaveLoader.SaveGame</c> goes through
    /// <c>SafeSaver</c>, which writes to a temporary file and swaps it in only once the write has finished,
    /// so a crash mid-save cannot destroy the previous one. Reimplementing that to control the path would
    /// mean reimplementing the one part of saving nobody should be reimplementing. The path is redirected
    /// instead and the game writes as it always has.
    ///
    /// <b>The long event is vanilla's too.</b> Saving on the main thread with no notice would look like a
    /// freeze on a large colony, and RimWorld already has the screen for it.
    /// </summary>
    internal static class SaveWriter
    {
        /// <summary>
        /// Saves the current game as <paramref name="saveName"/> inside <paramref name="folder"/>.
        ///
        /// <paramref name="folder"/> null writes directly to Saves. When a save of this name already exists
        /// somewhere else, it is removed after the new one is safely written, so a rename between folders
        /// moves the save rather than leaving two.
        /// </summary>
        /// <param name="compress">
        /// Whether to compress this one save.
        ///
        /// Passed down rather than read from the settings at the bottom, because the setting only records
        /// what the box should show next time. What governs a save is what was ticked when it was written.
        /// </param>
        internal static void Save(string saveName, string folder, bool compress)
        {
            string cleaned = GenFile.SanitizedFileName(saveName ?? string.Empty).Trim();

            if (cleaned.NullOrEmpty())
                return;

            string target = SaveFolders.PathFor(folder, cleaned);

            // Captured before the write, because afterwards the new file is itself a save of this name and
            // the old one could no longer be told apart from it.
            FileInfo existing = SaveFolders.Find(cleaned);

            // Canonicalized, never compared as written. The two sides are built differently -- target out of
            // SaveFolders.Root, this one read off a FileInfo -- so one file can arrive in two spellings, and a
            // comparison that wrongly says "different" turns an overwrite into a move that deletes the file the
            // save just went into. SamePath resolves doubt as "same", which is the answer that cannot lose a save.
            string replaced = existing != null && !SaveFolders.SamePath(existing.FullName, target)
                ? existing.FullName
                : null;

            // Before the long event, not inside it. The overlay that appears during saving is interface and
            // would not show up in a camera render anyway, but the map keeps rendering throughout a save and
            // capturing first is what makes the picture the frame the player was actually looking at.
            SaveThumbnails.Capture(target);

            // Claimed here and not inside Capture, because Patch_SaveThumbnail calls Capture too. Setting it in
            // there would have that patch arm the flag against itself, and the next autosave landing on the same
            // rotation slot would be skipped as already handled.
            SaveThumbnails.Handled = target;

            LongEventHandler.QueueLongEvent(() =>
            {
                UIGuard.Try("Saves.Write", () =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? SaveFolders.Root);

                    SaveFolders.Redirect = target;

                    // Armed alongside the redirect and cleared with it, for the same reason: both describe
                    // this one save, and either left standing would be inherited by the next autosave.
                    SaveCompressor.Requested = compress;

                    try
                    {
                        GameDataSaveLoader.SaveGame(cleaned);
                    }
                    finally
                    {
                        // In a finally and not after the call, because SaveGame catches its own exceptions
                        // but the redirect must come off even on a path that does not.
                        SaveFolders.Redirect = null;
                        SaveCompressor.Requested = null;

                        // Cleared alongside them for the same reason. Patch_SaveThumbnail normally clears this
                        // itself when it sees the save it names go by, but a save that never reaches SaveGame at
                        // all would otherwise leave it armed and cost the next autosave its picture.
                        SaveThumbnails.Handled = null;
                    }

                    // Only once the new file is actually there. A move that deleted first and then failed to
                    // write would be the one bug in a save manager nobody ever forgives.
                    // The audit line for the whole decision, gated behind debug logging like the rest of the
                    // sentinels. A save that loses a file otherwise leaves nothing behind but a success message,
                    // which is exactly the position the "Northern Hibum - LZMA" report left us in: no way to tell
                    // which step removed it. These four facts identify the step.
                    UIDebug.Log("Saves.Write: target=" + target
                                + " folder=" + (folder ?? "<root>")
                                + " existing=" + (existing == null ? "<none>" : existing.FullName)
                                + " replaced=" + (replaced ?? "<none>")
                                + " targetExists=" + File.Exists(target)
                                + " targetBytes=" + (File.Exists(target) ? new FileInfo(target).Length : -1L));

                    if (replaced == null || !File.Exists(target) || !File.Exists(replaced))
                        return;

                    // <b>Asked a second time, immediately before the delete.</b> The first test ran before the
                    // write, against a path predicted from the chosen folder; this one runs against what is
                    // actually on disk now. Anything that redirected the write elsewhere -- another patch on
                    // FilePathForSavedGame, compression swapping the file in -- could have landed the new save on
                    // the very path about to be removed. One redundant comparison is nothing next to the failure
                    // it rules out, which is deleting the save the player just asked for.
                    if (SaveFolders.SamePath(replaced, target))
                        return;

                    // The new file has to be a real save before the old one goes. A zero length target means the
                    // write produced nothing, and the previous save is then the only copy that exists.
                    if (new FileInfo(target).Length <= 0L)
                        return;

                    UIDebug.Warning("Saves.Write: removing the previous copy at " + replaced
                                    + ", because the save moved to " + target);

                    File.Delete(replaced);

                    // The old file's picture goes with it, or the Saves folder accumulates a png for every
                    // save that was ever renamed.
                    SaveThumbnails.Remove(replaced);
                }, "The game was not saved. Nothing that was already on disk has been changed.");
            }, "SavingLongEvent", false, null);

            Messages.Message("SavedAs".Translate(cleaned), MessageTypeDefOf.SilentInput, false);
            PlayerKnowledgeDatabase.Save();
        }
    }
}
