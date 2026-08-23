using System;
using System.IO;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>Where a track came from, which decides how it is identified, loaded and labelled.</summary>
    internal enum MusicTrackKind
    {
        /// <summary>A <see cref="SongDef"/> from RimWorld or a mod. Its clip is already loaded.</summary>
        Song,

        /// <summary>A file on the player's own drive, loaded on demand.</summary>
        File
    }

    /// <summary>
    /// One playable thing, whether it came from a mod or off the player's drive.
    ///
    /// <b>Two kinds behind one type, because everything above this does not care.</b> A playlist holds tracks,
    /// the list draws tracks, and the engine plays tracks; only <see cref="MusicClips"/> and the persistence
    /// layer ever ask which kind they have. Splitting this into two types would push that question into every
    /// caller.
    ///
    /// <b>Identity is a string, and that is deliberate.</b> A playlist is written to a config file that a player
    /// can hand-edit, so an entry has to survive a restart and be readable when it does. A mod song is its
    /// defName, a file is its full path -- see <see cref="Id"/> for why the prefix matters.
    /// </summary>
    internal sealed class MusicTrack
    {
        /// <summary>
        /// The five extensions the player asked for, lower case and with the dot.
        ///
        /// mp4 is here as a container: a video file with an audio stream plays its audio, which is what somebody
        /// pointing us at an mp4 of a song means. flac and wma are deliberately absent -- flac needs a decoder
        /// nothing in RimWorld's folder carries.
        /// </summary>
        internal static readonly string[] Extensions = { ".ogg", ".wav", ".mp3", ".mp4", ".m4a" };

        /// <summary>The two that Unity's own loader cannot decode on the desktop, so NAudio takes them.</summary>
        private static readonly string[] MediaFoundationExtensions = { ".mp4", ".m4a" };

        internal MusicTrackKind Kind;

        /// <summary>Set for <see cref="MusicTrackKind.Song"/> only.</summary>
        internal SongDef Song;

        /// <summary>Set for <see cref="MusicTrackKind.File"/> only. Always a full path.</summary>
        internal string FilePath;

        /// <summary>
        /// Stable identity, and the exact text written to the playlist file.
        ///
        /// Prefixed by kind rather than guessed at on read. Without the prefix a defName and a relative path
        /// would be indistinguishable, and the reader would have to try the def database first and treat every
        /// miss as maybe-a-file -- which turns a mod that is temporarily disabled into a file that does not
        /// exist, and loses the entry either way.
        /// </summary>
        internal string Id;

        /// <summary>What the player sees. Underscores become spaces, because clip paths are full of them.</summary>
        internal string Label;

        /// <summary>The mod's name, or the folder the file sits in.</summary>
        internal string SourceLabel;

        /// <summary>
        /// Seconds, or zero when it is not known yet.
        ///
        /// A mod's song reports this for free from its loaded clip. A file does not until something has read it,
        /// so the column shows a dash until the first play and <see cref="MusicStore"/> remembers it afterwards.
        /// Never invented: a guess from the file size would be wrong by minutes on a variable bitrate mp3.
        /// </summary>
        internal float Length;

        /// <summary>A file the player has moved or deleted since importing it. Never true for a song.</summary>
        internal bool Missing;

        /// <summary>Lower case, with the dot. Empty for a song.</summary>
        internal string Extension
        {
            get
            {
                if (Kind != MusicTrackKind.File || FilePath.NullOrEmpty())
                    return string.Empty;

                return UIGuard.Try("Music.TrackExtension",
                    () => Path.GetExtension(FilePath).ToLowerInvariant(), string.Empty, null);
            }
        }

        /// <summary>Whether this one goes through NAudio rather than Unity's loader.</summary>
        internal bool NeedsMediaFoundation
        {
            get
            {
                string extension = Extension;

                for (int i = 0; i < MediaFoundationExtensions.Length; i++)
                {
                    if (MediaFoundationExtensions[i].Equals(extension, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
        }

        internal static MusicTrack FromSong(SongDef song)
        {
            if (song == null)
                return null;

            return new MusicTrack
            {
                Kind = MusicTrackKind.Song,
                Song = song,
                Id = "def:" + song.defName,
                Label = Prettify(song.defName),
                SourceLabel = SourceOf(song),

                // Zero rather than reaching for song.clip.length here. Clips resolve inside
                // LongEventHandler.ExecuteWhenFinished, so a library built during loading would cache zero
                // permanently; MusicLibrary refreshes the figure each time it rebuilds instead.
                Length = 0f
            };
        }

        internal static MusicTrack FromFile(string path)
        {
            if (path.NullOrEmpty())
                return null;

            return new MusicTrack
            {
                Kind = MusicTrackKind.File,
                FilePath = path,
                Id = "file:" + path,
                Label = UIGuard.Try("Music.TrackLabel",
                    () => Prettify(Path.GetFileNameWithoutExtension(path)), path, null),
                SourceLabel = UIGuard.Try("Music.TrackSource",
                    () => Path.GetDirectoryName(path) ?? string.Empty, string.Empty, null)
            };
        }

        /// <summary>
        /// Whether a path is one of the five formats.
        ///
        /// Used by the importer to filter a folder and by the store to reject a hand-edited entry, so it takes a
        /// path rather than an extension and tolerates one without a dot.
        /// </summary>
        internal static bool Supported(string path)
        {
            return UIGuard.Try("Music.Supported", () =>
            {
                if (path.NullOrEmpty())
                    return false;

                string extension = Path.GetExtension(path);

                if (extension.NullOrEmpty())
                    return false;

                for (int i = 0; i < Extensions.Length; i++)
                {
                    if (Extensions[i].Equals(extension, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }, false, null);
        }

        /// <summary>
        /// A clip path or file name turned into something readable.
        ///
        /// Song defNames in this game are clip file names: <c>Noodle_Starscape_a</c>, <c>qf_Space_Cruise</c>. The
        /// underscores go and the words are left alone otherwise, because a mod author's capitalisation is more
        /// likely to be right than a rule of ours.
        /// </summary>
        private static string Prettify(string raw)
        {
            if (raw.NullOrEmpty())
                return string.Empty;

            return raw.Replace('_', ' ').Trim();
        }

        /// <summary>
        /// The name of the mod a song came from.
        ///
        /// <c>modContentPack</c> is null for a def created in code rather than loaded from XML, which is rare but
        /// real, so there is a fallback rather than a crash.
        /// </summary>
        private static string SourceOf(SongDef song)
        {
            return UIGuard.Try("Music.TrackModName",
                () => song.modContentPack != null ? song.modContentPack.Name : "Unknown", "Unknown", null);
        }

        /// <summary>Seconds as m:ss, or a dash when the length is not known.</summary>
        internal static string Duration(float seconds)
        {
            if (seconds <= 0f)
                return "-";

            return Clock(seconds);
        }

        /// <summary>
        /// A playhead position as m:ss, always a time and never a dash.
        ///
        /// Separate from <see cref="Duration"/> because zero means two different things in the two places. An
        /// unknown length is a dash; a track at its very beginning is nought seconds in, and showing a dash there
        /// makes the readout flicker between the two as the position crosses zero.
        /// </summary>
        internal static string Elapsed(float seconds)
        {
            return Clock(seconds < 0f ? 0f : seconds);
        }

        private static string Clock(float seconds)
        {
            int whole = Mathf.FloorToInt(seconds);

            return (whole / 60) + ":" + (whole % 60).ToString("00");
        }
    }
}
