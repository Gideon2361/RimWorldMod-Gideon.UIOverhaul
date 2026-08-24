using System;
using System.Collections.Generic;
using System.IO;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>One mod that ships songs, and how many.</summary>
    internal sealed class MusicSource
    {
        internal string Name = string.Empty;

        internal readonly List<MusicTrack> Tracks = new List<MusicTrack>();
    }

    /// <summary>
    /// Every track the player could play, and the lists the window draws.
    ///
    /// <b>Mod songs come from the def database, which is the whole trick.</b> Every music mod on the Workshop
    /// ships <see cref="SongDef"/>s, RimWorld loads them and resolves each one's clip during startup, and none of
    /// that is anything we have to arrange. So a music mod is supported the day it is published, with no per-mod
    /// work and no list of known mods to maintain.
    ///
    /// <b>Commonality is ignored on purpose.</b> Vanilla's own picker weights songs by <c>commonality</c> and
    /// skips the zeroes, which is how a mod says "only play this inside a music sequence" -- and it is what the
    /// Odyssey orbital music mods all do, so ninety of their songs will never be chosen on a planet. Here they
    /// are just songs. Every def is listed whatever its commonality, season, time of day or royal title
    /// requirement, because the player asking for a track is a better authority than the def's own guess about
    /// when it suits.
    ///
    /// <b>Built once and cached until something changes it.</b> The list is walked every frame the window is
    /// open, and building it means a pass over the def database plus a file existence check per imported track.
    /// <see cref="Invalidate"/> is called by anything that edits the library.
    /// </summary>
    internal static class MusicLibrary
    {
        private static readonly List<MusicSource> mods = new List<MusicSource>();

        private static readonly List<MusicTrack> drive = new List<MusicTrack>();

        private static readonly Dictionary<string, MusicTrack> byId = new Dictionary<string, MusicTrack>();

        private static bool built;

        /// <summary>Mods that ship songs, in load order, each with its songs in def order.</summary>
        internal static List<MusicSource> Mods
        {
            get
            {
                Build();

                return mods;
            }
        }

        /// <summary>Tracks imported from the player's drive, in the order they were imported.</summary>
        internal static List<MusicTrack> Drive
        {
            get
            {
                Build();

                return drive;
            }
        }

        internal static int TotalCount
        {
            get
            {
                Build();

                int total = drive.Count;

                for (int i = 0; i < mods.Count; i++)
                    total += mods[i].Tracks.Count;

                return total;
            }
        }

        internal static void Invalidate()
        {
            built = false;
        }

        /// <summary>The track for an id, or null if the mod that had it is gone or the entry is nonsense.</summary>
        internal static MusicTrack Track(string id)
        {
            if (id.NullOrEmpty())
                return null;

            Build();

            MusicTrack track;

            return byId.TryGetValue(id, out track) ? track : null;
        }

        /// <summary>
        /// The tracks a source id names, in the order they should play.
        ///
        /// Returns a fresh list rather than a cached one: the caller sorts and filters it, and handing out the
        /// cache would let a search box reorder the library.
        /// </summary>
        internal static List<MusicTrack> Tracks(string sourceId)
        {
            List<MusicTrack> result = new List<MusicTrack>();

            UIGuard.Try("Music.SourceTracks", () =>
            {
                Build();

                if (sourceId.NullOrEmpty() || sourceId == MusicStore.SourceGame || sourceId == MusicStore.SourceAll)
                {
                    for (int i = 0; i < mods.Count; i++)
                        result.AddRange(mods[i].Tracks);

                    result.AddRange(drive);

                    return;
                }

                if (sourceId == MusicStore.SourceDrive)
                {
                    result.AddRange(drive);

                    return;
                }

                if (sourceId == MusicStore.SourceFavourites)
                {
                    foreach (string id in MusicStore.Favourites)
                    {
                        MusicTrack track = Track(id);

                        if (track != null)
                            result.Add(track);
                    }

                    return;
                }

                if (sourceId.StartsWith(MusicStore.SourceModPrefix, StringComparison.Ordinal))
                {
                    string name = sourceId.Substring(MusicStore.SourceModPrefix.Length);

                    for (int i = 0; i < mods.Count; i++)
                    {
                        if (mods[i].Name.EqualsIgnoreCase(name))
                        {
                            result.AddRange(mods[i].Tracks);

                            return;
                        }
                    }

                    return;
                }

                if (sourceId.StartsWith(MusicStore.SourceListPrefix, StringComparison.Ordinal))
                {
                    MusicPlaylist list = MusicStore.Playlist(sourceId.Substring(MusicStore.SourceListPrefix.Length));

                    if (list == null)
                        return;

                    for (int i = 0; i < list.TrackIds.Count; i++)
                    {
                        MusicTrack track = Track(list.TrackIds[i]);

                        // A playlist entry whose mod has been removed is skipped rather than shown as broken.
                        // Unlike a moved file, there is nothing the player can do about it and nothing to point
                        // at -- and disabling a mod for one session should not make them rebuild a playlist.
                        if (track != null)
                            result.Add(track);
                    }
                }
            }, "The music list could not be built, so it shows as empty.");

            return result;
        }

        /// <summary>
        /// How many tracks a source holds, for the count beside its name in the sidebar.
        ///
        /// The three cheap answers are given directly rather than by building the list and measuring it. This is
        /// read once per visible row per frame, and All music on a modded game is several hundred entries -- so
        /// the naive version allocates a list of them sixty times a second to print one number.
        /// </summary>
        internal static int Count(string sourceId)
        {
            if (sourceId == MusicStore.SourceAll || sourceId.NullOrEmpty() || sourceId == MusicStore.SourceGame)
                return TotalCount;

            if (sourceId == MusicStore.SourceDrive)
                return Drive.Count;

            if (sourceId.StartsWith(MusicStore.SourceListPrefix, StringComparison.Ordinal))
            {
                MusicPlaylist list = MusicStore.Playlist(sourceId.Substring(MusicStore.SourceListPrefix.Length));

                return list != null ? list.TrackIds.Count : 0;
            }

            return Tracks(sourceId).Count;
        }

        private static void Build()
        {
            if (built)
                return;

            built = true;

            UIGuard.Try("Music.BuildLibrary", () =>
            {
                mods.Clear();
                drive.Clear();
                byId.Clear();

                BuildSongs();
                BuildDrive();
            }, "The music library is empty because it could not be built.");
        }

        private static void BuildSongs()
        {
            // Keyed by mod name rather than by ModContentPack, so two folders of the same mod -- which is what a
            // local copy plus a Workshop copy looks like -- read as one source.
            Dictionary<string, MusicSource> sources = new Dictionary<string, MusicSource>();

            List<SongDef> songs = DefDatabase<SongDef>.AllDefsListForReading;

            for (int i = 0; i < songs.Count; i++)
            {
                SongDef song = songs[i];

                if (song == null)
                    continue;

                MusicTrack track = MusicTrack.FromSong(song);

                if (track == null || byId.ContainsKey(track.Id))
                    continue;

                // Read here rather than in the factory: clips resolve inside ExecuteWhenFinished, so this is the
                // first point in a session where asking is reliable, and the library is rebuilt often enough
                // that a null clip now is not remembered as a length of zero forever.
                if (song.clip != null)
                    track.Length = song.clip.length;

                MusicSource source;

                if (!sources.TryGetValue(track.SourceLabel, out source))
                {
                    source = new MusicSource { Name = track.SourceLabel };
                    sources[track.SourceLabel] = source;
                    mods.Add(source);
                }

                source.Tracks.Add(track);
                byId[track.Id] = track;
            }

            SortByLoadOrder();
        }

        /// <summary>
        /// Puts the mod sources in the order the player's mod list is in.
        ///
        /// Core first, then the DLCs, then their own mods in the order they loaded them, which is the order they
        /// already know. Alphabetical would put a mod beginning with A above Core.
        /// </summary>
        private static void SortByLoadOrder()
        {
            List<ModContentPack> running = LoadedModManager.RunningModsListForReading;
            Dictionary<string, int> order = new Dictionary<string, int>();

            for (int i = 0; i < running.Count; i++)
            {
                string name = running[i] != null ? running[i].Name : null;

                if (!name.NullOrEmpty() && !order.ContainsKey(name))
                    order[name] = i;
            }

            mods.Sort((a, b) =>
            {
                int left;
                int right;

                if (!order.TryGetValue(a.Name, out left))
                    left = int.MaxValue;

                if (!order.TryGetValue(b.Name, out right))
                    right = int.MaxValue;

                return left != right
                    ? left.CompareTo(right)
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static void BuildDrive()
        {
            List<string> ids = MusicStore.Imported;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];

                if (id.NullOrEmpty() || !id.StartsWith("file:", StringComparison.Ordinal))
                    continue;

                MusicTrack track = MusicTrack.FromFile(id.Substring("file:".Length));

                if (track == null || byId.ContainsKey(track.Id))
                    continue;

                track.Missing = !UIGuard.Try("Music.TrackExists", () => File.Exists(track.FilePath), true, null);
                track.Length = MusicStore.KnownLength(track.Id);

                drive.Add(track);
                byId[track.Id] = track;
            }
        }

        /// <summary>
        /// Walks the watched folders and imports anything new.
        ///
        /// <b>On demand, not on a timer.</b> Called when the music window opens. A watcher thread would find
        /// files a few seconds earlier and would have to be shut down on quit, handle a folder on a drive that
        /// has been unplugged, and decide what to do about a file still being copied. Opening the window is the
        /// moment the answer is wanted.
        ///
        /// Returns how many tracks were added, so the caller can say so.
        /// </summary>
        internal static int Rescan()
        {
            return UIGuard.Try("Music.Rescan", () =>
            {
                int added = 0;
                List<MusicFolder> watched = MusicStore.Folders;

                for (int i = 0; i < watched.Count; i++)
                {
                    MusicFolder folder = watched[i];

                    if (folder.Path.NullOrEmpty() || !MusicFolders.Exists(folder.Path))
                        continue;

                    string[] files = MusicFolders.Files(folder.Path);

                    for (int f = 0; f < files.Length; f++)
                    {
                        if (!MusicTrack.Supported(files[f]))
                            continue;

                        string id = "file:" + files[f];

                        if (MusicStore.Imported.Contains(id))
                            continue;

                        MusicStore.NoteImported(id);
                        added++;

                        MusicPlaylist list = MusicStore.Playlist(folder.Playlist);

                        if (list != null && !list.TrackIds.Contains(id))
                            list.TrackIds.Add(id);
                    }
                }

                if (added > 0)
                {
                    MusicStore.Save();
                    Invalidate();
                }

                return added;
            }, 0, "A watched folder could not be read, so new files in it were not picked up.");
        }
    }
}
