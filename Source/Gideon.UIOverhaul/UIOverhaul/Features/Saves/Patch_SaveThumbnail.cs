using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Gives a picture to every save that did not come from this mod's save window.
    ///
    /// <b>Autosaves had none, and that is the whole reason this exists.</b> <see cref="SaveThumbnails.Capture"/>
    /// was only ever called from <c>SaveWriter</c>, which only the save window calls. <c>Autosaver.DoAutosave</c>,
    /// permadeath's rolling save, the autostart save and anything another mod writes all go straight to
    /// <c>GameDataSaveLoader.SaveGame</c> and never went near it, so the files that actually accumulate were the
    /// ones with no preview. Reported by Aaron on 2026-08-19.
    ///
    /// <b>On <c>SaveGame</c> for the same reason the compressor is,</b> which is that it is the only place every
    /// save arrives. See <see cref="Patch_SaveCompression"/>, whose prefix and postfix pair this copies exactly:
    /// the path is resolved at the moment vanilla resolves it, because <c>FilePathForSavedGame</c> is redirected
    /// while this mod's window is writing and asking again afterwards would depend on that redirect still being
    /// armed.
    ///
    /// <b>After the write rather than before it, unlike the window's own capture.</b> An autosave gives no warning:
    /// by the time anything knows one is happening it is already inside a long event with the saving overlay up.
    /// So the picture is taken on the first frame after the event ends, which is a normal frame of the map a moment
    /// later. For a save the player did not ask for, a moment later is the same picture.
    ///
    /// <b>It never overwrites the window's own.</b> That one is timed before the write on purpose, and
    /// <see cref="SaveThumbnails.Handled"/> is how this knows to leave it alone.
    ///
    /// <b>The postfix runs even when the save failed,</b> since <c>SaveGame</c> catches its own exceptions and
    /// returns normally. A picture beside a save that does not exist would be litter, so the file is checked for
    /// before anything is captured.
    /// </summary>
    [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame))]
    internal static class Patch_SaveThumbnail
    {
        public static void Prefix(string fileName, out string __state)
        {
            __state = UIGuard.Try("Saves.ResolveThumbnailTarget",
                () => GenFilePaths.FilePathForSavedGame(fileName), null, null);
        }

        public static void Postfix(string __state)
        {
            if (__state.NullOrEmpty())
                return;

            UIGuard.Try("Saves.ThumbnailAfterWrite", () =>
            {
                string claimed = SaveThumbnails.Handled;

                // <b>The null is tested before the comparison, and that is not a formality.</b>
                // SaveFolders.SamePath answers true when either side is empty, deliberately: its other caller is
                // deciding whether to delete a file it might have just written to, and "same" is the answer that
                // cannot lose a save. Here the meaning is inverted, since Handled is null for every autosave, so
                // handing it a null would skip a picture for exactly the saves this patch exists to cover.
                bool mine = !claimed.NullOrEmpty() && SaveFolders.SamePath(__state, claimed);

                if (mine)
                {
                    // Cleared here as well as in the writer's finally, so whichever runs first the flag never
                    // survives into the next save.
                    SaveThumbnails.Handled = null;

                    return;
                }

                if (!System.IO.File.Exists(__state))
                    return;

                SaveThumbnails.Capture(__state);
            }, "This save has no preview picture. Nothing else is affected.");
        }
    }
}
