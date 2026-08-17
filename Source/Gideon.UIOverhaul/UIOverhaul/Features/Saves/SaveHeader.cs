using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// What a save's meta header says: the build it was written by, and the mods that were loaded.
    ///
    /// <b>Read here rather than through <c>ScribeMetaHeaderUtility</c>, and that is the whole point of this
    /// type.</b> Vanilla's reader puts its answers in static fields -- <c>loadedModIdsList</c> and friends --
    /// which describe whichever save was inspected most recently and nothing more. Anything that reads them
    /// later is reading a leftover. That is not a hypothetical: the load window used to ask
    /// <c>LoadedModsMatchesActiveModsNoInfo()</c> while drawing a row, and at the main menu those fields have
    /// never been filled at all, so every save in the list was reported as having a different mod list than
    /// the one running.
    ///
    /// <b>One pass for both facts.</b> <c>SaveFileInfo.LoadData</c> reads the same header to get the version
    /// alone, so using it as well would mean opening -- and for a compressed save, decompressing -- the file
    /// twice per selection.
    ///
    /// <b>Opened through <see cref="SaveArchive"/>,</b> so a compressed save's header is as readable as a
    /// plain one's.
    /// </summary>
    internal sealed class SaveHeader
    {
        /// <summary>The build string as written, or null when the header could not be read.</summary>
        internal string GameVersion;

        /// <summary>Package ids of the mods that were loaded, in load order.</summary>
        internal List<string> ModIds = new List<string>();

        /// <summary>Display names for the same mods, in the same order. May be shorter on an odd header.</summary>
        internal List<string> ModNames = new List<string>();

        /// <summary>False when the file had no readable meta element, which makes every field above a guess.</summary>
        internal bool Read;

        /// <summary>
        /// Reads one save's header, or returns a header marked unread.
        ///
        /// Never throws: a save that cannot be parsed is a save the panel describes as unknown, not an
        /// exception on the way into a window.
        /// </summary>
        internal static SaveHeader Of(string path)
        {
            SaveHeader header = new SaveHeader();

            if (path.NullOrEmpty() || !File.Exists(path))
                return header;

            UIGuard.Try("Saves.ReadHeader", () => Fill(path, header),
                "That save's version and mod list could not be read.");

            return header;
        }

        private static void Fill(string path, SaveHeader header)
        {
            using (StreamReader stream = SaveArchive.OpenReader(path))
            using (XmlTextReader reader = new XmlTextReader(stream))
            {
                // Vanilla's own seek, so this finds the meta element wherever it finds it.
                if (!ScribeMetaHeaderUtility.ReadToMetaElement(reader))
                    return;

                XmlDocument document = new XmlDocument();

                // Only the meta element is parsed into memory. The game node behind it is tens of megabytes
                // and is nobody's business here.
                using (XmlReader meta = reader.ReadSubtree())
                    document.Load(meta);

                XmlNode root = document.DocumentElement;

                if (root == null)
                    return;

                header.GameVersion = Text(root, "gameVersion");
                header.ModIds = List(root, "modIds");
                header.ModNames = List(root, "modNames");
                header.Read = true;
            }
        }

        private static string Text(XmlNode meta, string name)
        {
            XmlNode node = meta.SelectSingleNode(name);

            return node == null ? null : node.InnerText;
        }

        /// <summary>
        /// One of the header's list elements, which scribe writes as a run of <c>li</c> children.
        /// </summary>
        private static List<string> List(XmlNode meta, string name)
        {
            List<string> values = new List<string>();
            XmlNode node = meta.SelectSingleNode(name);

            if (node == null)
                return values;

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                    values.Add(child.InnerText);
            }

            return values;
        }
    }

    /// <summary>How a save's mod list differs from the one running now.</summary>
    internal sealed class SaveModDiff
    {
        /// <summary>Mods the save was made with that are not loaded now, by the name the save recorded.</summary>
        internal readonly List<string> Missing = new List<string>();

        /// <summary>Mods loaded now that the save was not made with, by their current name.</summary>
        internal readonly List<string> Added = new List<string>();

        /// <summary>
        /// Set when the same mods are present but in a different order.
        ///
        /// <b>Worth reporting on its own,</b> because RimWorld applies patches in load order, so two identical
        /// mod lists in different orders are not the same game. It is also the case vanilla would still have
        /// warned about, and this window is the only warning now.
        /// </summary>
        internal bool OrderDiffers;

        /// <summary>Whether the header could be read at all. False makes everything else meaningless.</summary>
        internal bool Known;

        internal bool Matches => Known && Missing.Count == 0 && Added.Count == 0 && !OrderDiffers;

        internal int Differences => Missing.Count + Added.Count;

        /// <summary>
        /// Compares a save's recorded mods against what is loaded now.
        ///
        /// <b>A mod counts as present under either its package id or its folder name,</b> which is the same
        /// latitude <c>ScribeMetaHeaderUtility</c> allows. Saves written by older builds recorded folder
        /// names, and treating those as missing would report a wall of absent mods that are all sitting
        /// there.
        /// </summary>
        internal static SaveModDiff Compare(SaveHeader header)
        {
            SaveModDiff diff = new SaveModDiff();

            if (header == null || !header.Read)
                return diff;

            diff.Known = true;

            List<ModContentPack> running = UIGuard.Try("Saves.ListRunningMods",
                () => new List<ModContentPack>(LoadedModManager.RunningMods), new List<ModContentPack>(),
                null);

            HashSet<string> runningKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> runningIds = new List<string>();

            foreach (ModContentPack mod in running)
            {
                if (mod == null)
                    continue;

                if (!mod.PackageId.NullOrEmpty())
                    runningKeys.Add(mod.PackageId);

                if (!mod.FolderName.NullOrEmpty())
                    runningKeys.Add(mod.FolderName);

                runningIds.Add(mod.PackageId ?? string.Empty);
            }

            HashSet<string> savedKeys = new HashSet<string>(header.ModIds, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < header.ModIds.Count; i++)
            {
                string id = header.ModIds[i];

                if (runningKeys.Contains(id))
                    continue;

                // The recorded name where the header has one, since a package id is not what anybody
                // subscribed to. The id is the fallback and is better than nothing to go looking with.
                diff.Missing.Add(i < header.ModNames.Count && !header.ModNames[i].NullOrEmpty()
                    ? header.ModNames[i]
                    : id);
            }

            foreach (ModContentPack mod in running)
            {
                if (mod == null)
                    continue;

                if (savedKeys.Contains(mod.PackageId ?? string.Empty)
                    || savedKeys.Contains(mod.FolderName ?? string.Empty))
                    continue;

                diff.Added.Add(mod.Name ?? mod.PackageId ?? "Unknown");
            }

            if (diff.Missing.Count == 0 && diff.Added.Count == 0)
                diff.OrderDiffers = !SameOrder(header.ModIds, runningIds);

            return diff;
        }

        private static bool SameOrder(List<string> saved, List<string> running)
        {
            if (saved.Count != running.Count)
                return false;

            for (int i = 0; i < saved.Count; i++)
            {
                if (!string.Equals(saved[i], running[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
    }
}
