using System;
using System.IO;
using System.Threading;
using Gideon.UIOverhaul.Features.ButtonBar;
using UnityEngine;
using Verse;
using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// Watches this mod's config files and re-reads them when the OS reports a change, so hand-editing
    /// either file takes effect without restarting the game or reloading a save.
    ///
    /// The OS raises those notifications on a thread pool thread, where almost nothing in RimWorld or
    /// Unity may be touched. So the callbacks do the least possible -- bump a counter -- and the actual
    /// re-read happens on the main thread in <see cref="Tick"/>. That split is the whole design; doing
    /// the read in the callback would mean parsing XML and reassigning the active palette from under a
    /// frame that is already drawing.
    /// </summary>
    public static class UIConfigWatcher
    {
        /// <summary>
        /// Matches every config file this mod writes: UIOverhaul_Settings.xml, UIOverhaul_ButtonBar.xml and
        /// UIOverhaul_WorkTemplates.xml.
        /// </summary>
        private const string Filter = "UIOverhaul_*.xml";

        /// <summary>
        /// How long the file has to stay quiet before we read it. Editors rarely write once: a save is
        /// commonly a truncate followed by a write, which raises two or three events, and reading
        /// between them gets a half-written file.
        /// </summary>
        private const float QuietSeconds = 0.35f;

        /// <summary>
        /// How long to ignore changes after we write the file ourselves. Our own Save raises exactly the
        /// events we are listening for, and reacting to them would re-read what we just wrote.
        /// </summary>
        private const float SelfWriteGraceSeconds = 1.5f;

        private static FileSystemWatcher watcher;

        /// <summary>
        /// Bumped from the OS callback thread; compared on the main thread. A counter rather than a bool
        /// because it also has to survive being raised again while we are waiting out the quiet period.
        /// </summary>
        private static int changeToken;

        private static int seenToken;
        private static float quietUntil;
        private static float selfWriteUntil;
        private static bool started;

        /// <summary>
        /// Reads both config files and applies them. Called once the game has finished loading play
        /// data, and again whenever the files change on disk.
        /// </summary>
        public static void Ingest()
        {
            UIOverhaulSettingsFile.Reload();
            UIButtonBarConfig.Reload();

            // Matched by the same filter, so an edit to the templates file already wakes the watcher; without
            // this it would report a reload and then keep serving the old templates.
            Features.Pawns.Templates.PawnTemplateStore.Reload();
            Features.Notifications.AlertState.Reload();
            Features.Tabs.TabSizes.Reload();

            // Touching Current forces the read now rather than on some later first use, so a bad file is
            // reported at a predictable moment.
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;
            UIButtonBarConfig.Current.Resolve();

            // Palette XML before the theme is applied: a palette whose colors just changed on disk needs
            // its new values in place before anything reads them.
            UIPaletteHotReload.ReapplyAll();

            settings.ApplyTheme();

            // Written onto defs rather than read where they are used, so a file edited on disk changes nothing
            // until they are written again. The other features here reload data; these three have to push it.
            Features.Gravships.GravshipTuning.Apply();
            Features.Salvage.AncientSalvage.Apply();
            Features.Mood.MoodFixes.Apply();
        }

        /// <summary>
        /// Starts watching. Safe to call more than once: loading play data happens again whenever the mod
        /// list changes, and one watcher is enough.
        /// </summary>
        public static void Start()
        {
            if (started)
                return;

            started = true;

            // Independent of the config folder watcher below: palette files live in mod folders, so a
            // missing or unreadable config folder must not cost us theme hot-reloading too.
            UIPaletteHotReload.Start(OnChanged);

            try
            {
                string folder = GenFilePaths.ConfigFolderPath;
                if (folder.NullOrEmpty() || !Directory.Exists(folder))
                {
                    Log.Warning(UILogTag.Prefix + "Config folder not found; edits to the settings files "
                                + "will not be picked up until the game is restarted.");
                    return;
                }

                watcher = new FileSystemWatcher(folder, Filter)
                {
                    // Size as well as LastWrite: some editors update one without the other.
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = false
                };

                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnChanged;
                watcher.EnableRaisingEvents = true;

                // Palette XML rides the same counter, debounce and main-thread pump. One mechanism for
                // both means an edit that touches a theme and a setting together is applied in one pass
                // rather than as two visible steps.
                UIPaletteHotReload.Start(OnChanged);
            }
            catch (Exception ex)
            {
                // Watching is a convenience. Losing it must not cost the player their settings.
                Log.Warning(UILogTag.Prefix + "Could not watch the config folder for changes; edits "
                            + "will need a restart to apply.\n" + ex);
                watcher = null;
            }
        }

        /// <summary>
        /// Call before writing either config file, so the write we are about to do is not mistaken for
        /// someone editing it.
        /// </summary>
        public static void NotifySelfWrite()
        {
            selfWriteUntil = RealtimeNow + SelfWriteGraceSeconds;
        }

        /// <summary>
        /// Main-thread pump. Re-reads the config once the file has stopped changing.
        /// </summary>
        public static void Tick()
        {
            int token = Volatile.Read(ref changeToken);

            if (token != seenToken)
            {
                seenToken = token;
                quietUntil = RealtimeNow + QuietSeconds;
                return;
            }

            if (quietUntil <= 0f || RealtimeNow < quietUntil)
                return;

            quietUntil = 0f;

            if (RealtimeNow < selfWriteUntil)
                return;

            try
            {
                Ingest();
                Log.Message(UILogTag.Prefix + "Settings changed on disk; reloaded.");
            }
            catch (Exception ex)
            {
                Log.Error(UILogTag.Prefix + "Failed to reload settings after a change on disk.\n" + ex);
            }
        }

        /// <summary>
        /// Unity's clock, which is main-thread only. Never called from the watcher callbacks -- that is
        /// why they bump a counter instead of recording a time.
        /// </summary>
        private static float RealtimeNow => Time.realtimeSinceStartup;

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            Interlocked.Increment(ref changeToken);
        }
    }
}
