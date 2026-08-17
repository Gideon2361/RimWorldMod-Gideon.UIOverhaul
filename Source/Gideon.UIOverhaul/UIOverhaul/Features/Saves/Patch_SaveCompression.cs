using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Hands each freshly written save to the compressor.
    ///
    /// <b>On <c>SaveGame</c> rather than in the save dialog,</b> which is the only place that catches every
    /// save. <c>Autosaver.DoAutosave</c>, permadeath's own writes and the autostart save all arrive here and
    /// none of them go anywhere near this mod's window. Compressing only what the dialog wrote would leave a
    /// Saves folder that is half one format and half another, and the autosaves -- the files that actually
    /// accumulate -- would be the uncompressed half.
    ///
    /// <b>The path is captured in a prefix, not recomputed in the postfix.</b> <c>SaveGame</c> resolves it
    /// through <c>GenFilePaths.FilePathForSavedGame</c>, which this mod redirects while a save is being
    /// written to a chosen folder; asking again afterwards would work today and would depend on the redirect
    /// still being armed, which is a coupling between two patches that nothing enforces. Asking at the same
    /// moment vanilla does gives the same answer for the same reason vanilla gets it.
    ///
    /// <b>The postfix runs even when the save failed,</b> because <c>SaveGame</c> catches its own exceptions
    /// and returns normally. That is handled where it is visible, in the compressor, which checks the file is
    /// really there before touching it.
    /// </summary>
    [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame))]
    internal static class Patch_SaveCompression
    {
        public static void Prefix(string fileName, out string __state)
        {
            __state = UIGuard.Try("Saves.ResolveWritten",
                () => GenFilePaths.FilePathForSavedGame(fileName), null, null);
        }

        public static void Postfix(string __state)
        {
            if (__state.NullOrEmpty())
                return;

            SaveCompressor.AfterWrite(__state);
        }
    }
}
