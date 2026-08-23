using HarmonyLib;
using RimWorld;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>
    /// Where the music player gets its frame, and where vanilla's picker is stood down.
    ///
    /// <b>A prefix rather than a replacement of the method.</b> Returning false skips vanilla's update for that
    /// frame only, and the decision is taken fresh every frame from the current setting -- so switching the source
    /// back to the game's own choice hands control back at once, with no patch to unapply and nothing to restart.
    ///
    /// <b>The prefix does nothing itself.</b> All of the work, and all of the guarding, is in
    /// <see cref="MusicEngine.Tick"/>, which answers false if anything at all went wrong. So the failure mode of
    /// this patch is that RimWorld picks the music, which is exactly what the player would have had without the
    /// mod.
    /// </summary>
    [HarmonyPatch(typeof(MusicManagerPlay), nameof(MusicManagerPlay.MusicUpdate))]
    internal static class Patch_MusicManagerPlay_MusicUpdate
    {
        private static bool Prefix()
        {
            // Not UIGuard.Replaced: the engine has its own guard and its own idea of when it is in charge, and
            // this has to be the negation of "we handled it" rather than of "we failed".
            return !MusicEngine.Tick();
        }
    }

    /// <summary>
    /// The same for the main menu, which has a music manager of its own.
    ///
    /// Worth patching rather than leaving alone: the menu is where a library gets built before a colony is
    /// loaded, and a player who has just arranged a playlist should hear it there rather than being told to load
    /// a save first.
    /// </summary>
    [HarmonyPatch(typeof(MusicManagerEntry), nameof(MusicManagerEntry.MusicManagerEntryUpdate))]
    internal static class Patch_MusicManagerEntry_Update
    {
        private static bool Prefix()
        {
            return !MusicEngine.TickEntry();
        }
    }
}
