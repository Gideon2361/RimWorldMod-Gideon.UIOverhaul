using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// Reads the config the moment the game finishes loading play data, which is the frame our loading
    /// screen gives way to the main menu.
    ///
    /// This is a better moment than a StaticConstructorOnStartup class, which is what it replaces. Those
    /// run *during* LoadAllPlayData, in an order nothing guarantees; here every def exists, every static
    /// constructor has been called, and the palette a stored theme names can actually be resolved.
    ///
    /// It also fires again on a def reload. Changing the mod list re-runs LoadAllPlayData, which
    /// rebuilds the def database and would otherwise leave the active palette pointing at a
    /// UIColorPaletteDef instance that no longer exists.
    /// </summary>
    [HarmonyPatch(typeof(PlayDataLoader), nameof(PlayDataLoader.LoadAllPlayData))]
    public static class Patch_PlayDataLoader_ConfigIngest
    {
        public static void Postfix()
        {
            UIConfigWatcher.Ingest();
            UIConfigWatcher.Start();
        }
    }

    /// <summary>
    /// Pumps the file watcher once per frame at the main menu.
    ///
    /// Root.Update is virtual and both Root_Entry and Root_Play override it, so a patch on the base
    /// would never run -- Unity calls the subclass. Hence one patch per subclass rather than one on
    /// Root. Update rather than OnGUI as well: OnGUI runs several times a frame, once per input event,
    /// and swapping the active palette partway through a frame that is already drawing would show a
    /// half-restyled UI for that frame.
    /// </summary>
    [HarmonyPatch(typeof(Root_Entry), nameof(Root_Entry.Update))]
    public static class Patch_Root_Entry_Update
    {
        public static void Postfix()
        {
            UIConfigWatcher.Tick();
        }
    }

    /// <summary>Pumps the file watcher once per frame in game. See the note on the menu patch.</summary>
    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
    public static class Patch_Root_Play_Update
    {
        public static void Postfix()
        {
            UIConfigWatcher.Tick();
        }
    }
}
