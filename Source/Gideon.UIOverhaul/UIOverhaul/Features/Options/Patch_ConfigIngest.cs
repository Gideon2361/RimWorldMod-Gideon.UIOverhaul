using Gideon.UIFramework.Caching;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
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
        /// <summary>
        /// <b>The most important guard in the mod.</b> This runs inside LoadAllPlayData, so an exception here does
        /// not produce a mod with broken settings -- it produces a game that will not start, and a player with no
        /// way to tell which of their mods did it.
        ///
        /// The two calls are guarded separately on purpose. Reading the config and starting the watcher are
        /// independent, and a settings file that cannot be parsed must not also cost the player hot-reloading;
        /// more to the point, whichever one fails, the other still gets its chance to run.
        /// </summary>
        public static void Postfix()
        {
            // Before the config is read, because every def instance the caches could be holding has just been
            // rebuilt from XML: a cached value keyed by a def from the previous load describes an object that no
            // longer exists.
            UIGuard.Try("Cache.ClearOnReload", UICacheController.ClearAll);

            UIGuard.Try("Options.Ingest", UIConfigWatcher.Ingest);
            UIGuard.Try("Options.StartWatching", UIConfigWatcher.Start);
        }
    }

    /// <summary>
    /// Drops every cached value when a game becomes playable, which covers loading a save, starting a new colony
    /// and returning to the menu and back.
    ///
    /// <b>The class doc on <see cref="UICacheController"/> has always said the caches are cleared when the world
    /// changes underneath them. Until now only a def reload actually did it</b> -- and the gap is a real one rather
    /// than tidiness. Entries are keyed by the object they describe, so a colonist from the previous save is a
    /// different key and cannot show wrong data; what it does instead is hold that object alive. A
    /// <c>Zone_Growing</c> from an abandoned map still answers a Map when asked, so it survives pruning
    /// indefinitely, and a growing zone pins the map it belongs to.
    ///
    /// <c>FinalizeInit</c> rather than <c>LoadGame</c> or <c>InitNewGame</c>, because it is the one seam both paths
    /// pass through, and it runs at the point the world is fully assembled -- which is the moment a cache read is
    /// safe again.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_Game_FinalizeInit_Cache
    {
        public static void Postfix()
        {
            UIGuard.Try("Cache.ClearOnGameLoad", UICacheController.ClearAll);
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
        /// <summary>
        /// Guarded even though Root.Update has a try/catch of its own, because a postfix runs after the method
        /// body and therefore outside it. An escape from here lands in Unity's Update dispatch, which reports it
        /// and carries on -- once per frame, for the rest of the session, until the log gives up.
        /// </summary>
        public static void Postfix()
        {
            UIGuard.Try("Options.WatcherTick", UIConfigWatcher.Tick);

            // Housekeeping only: this drops cache entries whose subject has gone away, at most once a minute, and
            // returns immediately the rest of the time. It does not refresh anything -- caches rebuild on read.
            UIGuard.Try("Cache.Tick", () => UICacheController.Tick(Time.realtimeSinceStartup));
        }
    }

    /// <summary>Pumps the file watcher once per frame in game. See the note on the menu patch.</summary>
    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
    public static class Patch_Root_Play_Update
    {
        public static void Postfix()
        {
            UIGuard.Try("Options.WatcherTick", UIConfigWatcher.Tick);

            // Housekeeping only: this drops cache entries whose subject has gone away, at most once a minute, and
            // returns immediately the rest of the time. It does not refresh anything -- caches rebuild on read.
            UIGuard.Try("Cache.Tick", () => UICacheController.Tick(Time.realtimeSinceStartup));
        }
    }
}
