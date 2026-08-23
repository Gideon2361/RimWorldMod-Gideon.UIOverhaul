using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>What happens when the last track of a source finishes.</summary>
    internal enum MusicRepeat
    {
        /// <summary>Stop.</summary>
        Off,

        /// <summary>Start the source again.</summary>
        All,

        /// <summary>Play the same track again.</summary>
        One
    }

    /// <summary>A playlist the player made: a name, a picture and an order.</summary>
    internal sealed class MusicPlaylist
    {
        internal string Name = string.Empty;

        /// <summary>Index into <see cref="MusicGlyphs.PlaylistIcons"/>, clamped on read.</summary>
        internal int Icon;

        /// <summary>Track ids, in the order the player arranged them.</summary>
        internal readonly List<string> TrackIds = new List<string>();
    }

    /// <summary>A folder rescanned when the window opens, feeding a playlist or the drive library.</summary>
    internal sealed class MusicFolder
    {
        internal string Path = string.Empty;

        /// <summary>The playlist new files join, or empty for the drive library alone.</summary>
        internal string Playlist = string.Empty;
    }

    /// <summary>
    /// Everything about the player's music that has to outlive the process.
    ///
    /// <b>A file of its own rather than more elements in the settings file,</b> and that is not filing
    /// preference. <c>UIOverhaulSettingsFile</c> is a flat list of scalars read by a switch over element names,
    /// on purpose -- its own comments say a nested list would be the only shape in there needing its own
    /// parsing. Playlists are exactly that shape. Putting them here keeps that file's discipline and puts all of
    /// the music state in one place a player can read.
    ///
    /// <b>Not in the save.</b> A music library is a fact about somebody's machine and taste, not about a colony:
    /// the same playlists should be there on the next colony, and at the main menu where no colony is loaded.
    /// This is the same reasoning as the favourite plants and recipes, which are also settings rather than save
    /// data.
    ///
    /// <b>Written on every change, read once.</b> Changes here are a click apart at most -- starring a track,
    /// dragging the shuffle toggle -- so there is no batching and no dirty flag to get wrong. The file is small
    /// enough that this does not matter.
    ///
    /// <b>A failed read is not an empty library.</b> If the file cannot be parsed the previous in-memory state is
    /// kept and the problem is reported, because silently starting from nothing would look like the playlists
    /// had been deleted and the next save would make that true.
    /// </summary>
    internal static class MusicStore
    {
        internal const string FileName = "UIOverhaul_Music.xml";

        /// <summary>The game picks songs, as it always did. Our player intercepts nothing.</summary>
        internal const string SourceGame = "game";

        /// <summary>Every track from every source, mods and drive together.</summary>
        internal const string SourceAll = "all";

        internal const string SourceFavourites = "favourites";

        /// <summary>Everything imported from the player's own drive.</summary>
        internal const string SourceDrive = "drive";

        /// <summary>Prefix for one mod's songs. The rest of the id is the mod's name.</summary>
        internal const string SourceModPrefix = "mod:";

        /// <summary>Prefix for a playlist. The rest of the id is its name.</summary>
        internal const string SourceListPrefix = "list:";

        private static readonly List<MusicPlaylist> playlists = new List<MusicPlaylist>();

        private static readonly List<MusicFolder> folders = new List<MusicFolder>();

        private static readonly List<string> imported = new List<string>();

        private static readonly HashSet<string> favourites = new HashSet<string>();

        /// <summary>
        /// Lengths learned by playing a file, in seconds, keyed by track id.
        ///
        /// Only files are in here. A song's length comes off its clip every time and costs nothing.
        /// </summary>
        private static readonly Dictionary<string, float> lengths = new Dictionary<string, float>();

        private static bool loaded;

        private static string source = SourceGame;

        private static bool shuffle;

        private static MusicRepeat repeat = MusicRepeat.All;

        internal static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

        internal static List<MusicPlaylist> Playlists
        {
            get
            {
                EnsureLoaded();

                return playlists;
            }
        }

        internal static List<MusicFolder> Folders
        {
            get
            {
                EnsureLoaded();

                return folders;
            }
        }

        /// <summary>Track ids imported from the drive, whether or not they are in a playlist.</summary>
        internal static List<string> Imported
        {
            get
            {
                EnsureLoaded();

                return imported;
            }
        }

        /// <summary>Which source supplies music. One of the constants above, or a prefixed name.</summary>
        internal static string Source
        {
            get
            {
                EnsureLoaded();

                return source;
            }

            set
            {
                EnsureLoaded();

                source = value.NullOrEmpty() ? SourceGame : value;
                Save();
            }
        }

        internal static bool Shuffle
        {
            get
            {
                EnsureLoaded();

                return shuffle;
            }

            set
            {
                EnsureLoaded();

                shuffle = value;
                Save();
            }
        }

        internal static MusicRepeat Repeat
        {
            get
            {
                EnsureLoaded();

                return repeat;
            }

            set
            {
                EnsureLoaded();

                repeat = value;
                Save();
            }
        }

        internal static bool Favourite(string trackId)
        {
            EnsureLoaded();

            return !trackId.NullOrEmpty() && favourites.Contains(trackId);
        }

        internal static IEnumerable<string> Favourites
        {
            get
            {
                EnsureLoaded();

                return favourites;
            }
        }

        internal static void ToggleFavourite(string trackId)
        {
            if (trackId.NullOrEmpty())
                return;

            EnsureLoaded();

            if (!favourites.Remove(trackId))
                favourites.Add(trackId);

            Save();
        }

        /// <summary>Zero when the length has never been learned.</summary>
        internal static float KnownLength(string trackId)
        {
            EnsureLoaded();

            float seconds;

            return !trackId.NullOrEmpty() && lengths.TryGetValue(trackId, out seconds) ? seconds : 0f;
        }

        /// <summary>
        /// Records a length read off a clip we just loaded.
        ///
        /// Saves only when the figure is new, because this is called from the engine as a track starts and a
        /// write per track start would be a write per song for the whole session.
        /// </summary>
        internal static void LearnLength(string trackId, float seconds)
        {
            if (trackId.NullOrEmpty() || seconds <= 0f)
                return;

            EnsureLoaded();

            float known;

            if (lengths.TryGetValue(trackId, out known) && Math.Abs(known - seconds) < 0.05f)
                return;

            lengths[trackId] = seconds;
            Save();
        }

        internal static MusicPlaylist Playlist(string name)
        {
            if (name.NullOrEmpty())
                return null;

            EnsureLoaded();

            for (int i = 0; i < playlists.Count; i++)
            {
                if (playlists[i].Name.EqualsIgnoreCase(name))
                    return playlists[i];
            }

            return null;
        }

        /// <summary>Whether a name is free. The comparison ignores case, as a player reading the list would.</summary>
        internal static bool NameAvailable(string name, MusicPlaylist except = null)
        {
            if (name.NullOrEmpty())
                return false;

            EnsureLoaded();

            for (int i = 0; i < playlists.Count; i++)
            {
                if (playlists[i] != except && playlists[i].Name.EqualsIgnoreCase(name))
                    return false;
            }

            return true;
        }

        internal static MusicPlaylist Create(string name, int icon)
        {
            EnsureLoaded();

            MusicPlaylist list = new MusicPlaylist { Name = name, Icon = icon };

            playlists.Add(list);
            Save();

            return list;
        }

        /// <summary>
        /// Renames a playlist, moving the selection with it.
        ///
        /// The selection is stored as a name, so a rename that did not fix it up would silently switch the player
        /// to the game's own choice -- which reads as the playlist having been deleted.
        /// </summary>
        internal static void Rename(MusicPlaylist list, string name, int icon)
        {
            if (list == null || name.NullOrEmpty())
                return;

            EnsureLoaded();

            bool selected = source == SourceListPrefix + list.Name;

            list.Name = name;
            list.Icon = icon;

            if (selected)
                source = SourceListPrefix + name;

            Save();
        }

        internal static void Delete(MusicPlaylist list)
        {
            if (list == null)
                return;

            EnsureLoaded();

            if (source == SourceListPrefix + list.Name)
                source = SourceGame;

            playlists.Remove(list);

            // A folder feeding a playlist that no longer exists still has somewhere to put its files: the drive
            // library, which is where they would have gone without the playlist. Dropping the folder instead
            // would quietly stop watching a folder the player never mentioned.
            for (int i = 0; i < folders.Count; i++)
            {
                if (folders[i].Playlist.EqualsIgnoreCase(list.Name))
                    folders[i].Playlist = string.Empty;
            }

            Save();
        }

        /// <summary>Adds a track id to the drive library if it is not already there.</summary>
        internal static void NoteImported(string trackId)
        {
            if (trackId.NullOrEmpty())
                return;

            EnsureLoaded();

            if (!imported.Contains(trackId))
                imported.Add(trackId);
        }

        /// <summary>
        /// Forgets a drive track entirely: the library, every playlist, the favourites and its length.
        ///
        /// Nothing is deleted from disk. Removing a track from the library is a statement about this mod's list,
        /// and deleting somebody's music file because they tidied a playlist would be indefensible.
        /// </summary>
        internal static void Forget(string trackId)
        {
            if (trackId.NullOrEmpty())
                return;

            EnsureLoaded();

            imported.Remove(trackId);
            favourites.Remove(trackId);
            lengths.Remove(trackId);

            for (int i = 0; i < playlists.Count; i++)
                playlists[i].TrackIds.Remove(trackId);

            Save();
        }

        internal static void AddFolder(string path, string playlist)
        {
            if (path.NullOrEmpty())
                return;

            EnsureLoaded();

            for (int i = 0; i < folders.Count; i++)
            {
                if (folders[i].Path.EqualsIgnoreCase(path))
                {
                    folders[i].Playlist = playlist ?? string.Empty;
                    Save();

                    return;
                }
            }

            folders.Add(new MusicFolder { Path = path, Playlist = playlist ?? string.Empty });
            Save();
        }

        internal static void RemoveFolder(string path)
        {
            EnsureLoaded();

            for (int i = folders.Count - 1; i >= 0; i--)
            {
                if (folders[i].Path.EqualsIgnoreCase(path))
                    folders.RemoveAt(i);
            }

            Save();
        }

        internal static bool Watching(string path)
        {
            if (path.NullOrEmpty())
                return false;

            EnsureLoaded();

            for (int i = 0; i < folders.Count; i++)
            {
                if (folders[i].Path.EqualsIgnoreCase(path))
                    return true;
            }

            return false;
        }

        private static void EnsureLoaded()
        {
            if (loaded)
                return;

            // Set before the read rather than after, so a throw inside Read cannot leave this false and turn
            // every subsequent property access into another attempt at a file that is not going to parse.
            loaded = true;

            UIGuard.Try("Music.StoreLoad", Read,
                "The music library could not be read, so the player starts with no playlists.");
        }

        private static void Read()
        {
            string path = FilePath;

            if (!File.Exists(path))
                return;

            XmlDocument document = new XmlDocument();
            document.Load(path);

            XmlElement root = document.DocumentElement;

            if (root == null)
                return;

            foreach (XmlNode node in root.ChildNodes)
            {
                XmlElement element = node as XmlElement;

                if (element == null)
                    continue;

                string value = element.InnerText != null ? element.InnerText.Trim() : string.Empty;

                switch (element.Name)
                {
                    case "source":
                        source = value.NullOrEmpty() ? SourceGame : value;
                        break;

                    case "shuffle":
                        shuffle = value.EqualsIgnoreCase("true");
                        break;

                    case "repeat":
                        repeat = ParseRepeat(value);
                        break;

                    case "playlists":
                        ReadPlaylists(element);
                        break;

                    case "drive":
                        ReadIds(element, imported);
                        break;

                    case "favourites":
                        ReadFavourites(element);
                        break;

                    case "folders":
                        ReadFolders(element);
                        break;

                    case "lengths":
                        ReadLengths(element);
                        break;
                }
            }
        }

        private static void ReadPlaylists(XmlElement parent)
        {
            playlists.Clear();

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;

                if (element == null || element.Name != "playlist")
                    continue;

                MusicPlaylist list = new MusicPlaylist();

                foreach (XmlNode child in element.ChildNodes)
                {
                    XmlElement field = child as XmlElement;

                    if (field == null)
                        continue;

                    string value = field.InnerText != null ? field.InnerText.Trim() : string.Empty;

                    if (field.Name == "name")
                        list.Name = value;
                    else if (field.Name == "icon")
                        list.Icon = ParseInt(value);
                    else if (field.Name == "tracks")
                        ReadIds(field, list.TrackIds);
                }

                // A playlist with no name cannot be selected, renamed or told apart from another one, so it is
                // not a playlist. Dropped rather than given a made-up name.
                if (!list.Name.NullOrEmpty() && NameAvailable(list.Name))
                    playlists.Add(list);
            }
        }

        private static void ReadIds(XmlElement parent, List<string> into)
        {
            into.Clear();

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;

                if (element == null || element.Name != "track")
                    continue;

                string id = element.InnerText != null ? element.InnerText.Trim() : string.Empty;

                if (!id.NullOrEmpty() && !into.Contains(id))
                    into.Add(id);
            }
        }

        private static void ReadFavourites(XmlElement parent)
        {
            favourites.Clear();

            List<string> ids = new List<string>();
            ReadIds(parent, ids);

            for (int i = 0; i < ids.Count; i++)
                favourites.Add(ids[i]);
        }

        private static void ReadFolders(XmlElement parent)
        {
            folders.Clear();

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;

                if (element == null || element.Name != "folder")
                    continue;

                MusicFolder folder = new MusicFolder();

                foreach (XmlNode child in element.ChildNodes)
                {
                    XmlElement field = child as XmlElement;

                    if (field == null)
                        continue;

                    string value = field.InnerText != null ? field.InnerText.Trim() : string.Empty;

                    if (field.Name == "path")
                        folder.Path = value;
                    else if (field.Name == "playlist")
                        folder.Playlist = value;
                }

                if (!folder.Path.NullOrEmpty())
                    folders.Add(folder);
            }
        }

        private static void ReadLengths(XmlElement parent)
        {
            lengths.Clear();

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;

                if (element == null || element.Name != "length")
                    continue;

                string id = element.GetAttribute("id");
                float seconds = ParseFloat(element.GetAttribute("seconds"));

                if (!id.NullOrEmpty() && seconds > 0f)
                    lengths[id] = seconds;
            }
        }

        internal static void Save()
        {
            UIGuard.Try("Music.StoreSave", Write,
                "The music library could not be written, so changes will be lost when the game closes.");
        }

        private static void Write()
        {
            string path = FilePath;

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false)
            };

            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteComment(" Music library for Gideon's UI Overhaul. Written by the music player; "
                                    + "safe to hand-edit. A track is a song's defName or a full file path. ");
                writer.WriteStartElement("UIOverhaulMusic");

                writer.WriteElementString("source", source ?? SourceGame);
                writer.WriteElementString("shuffle", shuffle ? "true" : "false");
                writer.WriteElementString("repeat", repeat.ToString());

                writer.WriteStartElement("playlists");

                for (int i = 0; i < playlists.Count; i++)
                {
                    MusicPlaylist list = playlists[i];

                    writer.WriteStartElement("playlist");
                    writer.WriteElementString("name", list.Name ?? string.Empty);
                    writer.WriteElementString("icon", list.Icon.ToString(CultureInfo.InvariantCulture));
                    WriteIds(writer, "tracks", list.TrackIds);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();

                WriteIds(writer, "drive", imported);
                WriteIds(writer, "favourites", new List<string>(favourites));

                writer.WriteStartElement("folders");

                for (int i = 0; i < folders.Count; i++)
                {
                    writer.WriteStartElement("folder");
                    writer.WriteElementString("path", folders[i].Path ?? string.Empty);
                    writer.WriteElementString("playlist", folders[i].Playlist ?? string.Empty);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();

                writer.WriteStartElement("lengths");

                foreach (KeyValuePair<string, float> pair in lengths)
                {
                    writer.WriteStartElement("length");
                    writer.WriteAttributeString("id", pair.Key);
                    writer.WriteAttributeString("seconds",
                        pair.Value.ToString("0.###", CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        private static void WriteIds(XmlWriter writer, string element, List<string> ids)
        {
            writer.WriteStartElement(element);

            for (int i = 0; i < ids.Count; i++)
                writer.WriteElementString("track", ids[i] ?? string.Empty);

            writer.WriteEndElement();
        }

        private static MusicRepeat ParseRepeat(string value)
        {
            if (value.EqualsIgnoreCase("One"))
                return MusicRepeat.One;

            if (value.EqualsIgnoreCase("Off"))
                return MusicRepeat.Off;

            return MusicRepeat.All;
        }

        private static int ParseInt(string value)
        {
            int parsed;

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0;
        }

        private static float ParseFloat(string value)
        {
            float parsed;

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0f;
        }
    }
}
