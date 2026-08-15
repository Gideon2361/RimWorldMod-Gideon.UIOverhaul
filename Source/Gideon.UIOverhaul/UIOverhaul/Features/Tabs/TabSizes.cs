using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Tabs
{
    /// <summary>
    /// The size each tab was last dragged to, remembered between openings and between sessions.
    ///
    /// <b>Persisting this is not a nicety.</b> <c>MainTabWindow.SetInitialSizeAndPosition</c> runs from
    /// <c>PreOpen</c>, so a tab is re-sized from its <c>RequestedTabSize</c> every single time it is opened. A
    /// resize that is not stored survives until the player closes the tab, which for a feature about how much
    /// room the work grid gets is the same as not working.
    ///
    /// <b>A file of its own rather than more elements in the settings file.</b> This is an open-ended map keyed by
    /// defName -- one entry per tab the player has ever dragged, including tabs from mods -- and the settings file
    /// is a flat list of named fields that warns about any element it does not recognize. Folding a variable
    /// number of entries into it would mean either that warning firing on every launch or a special case in the
    /// loader for one setting.
    ///
    /// <b>Keyed by defName, so a tab that is not loaded keeps its entry.</b> Someone who disables a mod and
    /// re-enables it a month later gets their size back, and an entry whose def never turns up costs one line of
    /// XML. Pruning them would be tidier and would silently throw away the settings of anybody cycling mods.
    /// </summary>
    internal static class TabSizes
    {
        internal const string FileName = "UIOverhaul_TabSizes.xml";

        private static Dictionary<string, Vector2> sizes;
        private static bool unsaved;

        internal static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

        private static Dictionary<string, Vector2> Sizes
        {
            get
            {
                if (sizes == null)
                    sizes = Load();

                return sizes;
            }
        }

        /// <summary>The stored size for a tab, if it has ever been dragged.</summary>
        internal static bool TryGet(string defName, out Vector2 size)
        {
            size = Vector2.zero;

            return !defName.NullOrEmpty() && Sizes.TryGetValue(defName, out size);
        }

        /// <summary>
        /// Records a size. Held in memory rather than written, since this is called on every frame of a drag.
        /// </summary>
        internal static void Set(string defName, Vector2 size)
        {
            if (defName.NullOrEmpty())
                return;

            Sizes[defName] = size;
            unsaved = true;
        }

        /// <summary>Forgets one tab's size, so it opens at whatever the tab itself asks for again.</summary>
        internal static void Reset(string defName)
        {
            if (!defName.NullOrEmpty() && Sizes.Remove(defName))
            {
                unsaved = true;
                Save();
            }
        }

        /// <summary>Forgets every tab's size.</summary>
        internal static void ResetAll()
        {
            if (Sizes.Count == 0)
                return;

            Sizes.Clear();
            unsaved = true;
            Save();
        }

        internal static int Count => Sizes.Count;

        /// <summary>
        /// Writes the file, if anything has changed since the last write.
        ///
        /// Called when a drag ends rather than while one is in progress: a resize raises a change on every frame
        /// the mouse moves, and writing per frame would put a few hundred file writes behind one drag across the
        /// screen.
        /// </summary>
        internal static void SaveIfNeeded()
        {
            if (unsaved)
                Save();
        }

        private static void Save()
        {
            unsaved = false;

            string path = FilePath;

            try
            {
                // So the watcher does not mistake our own write for someone editing the file.
                UIConfigWatcher.NotifySelfWrite();

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

                XmlWriterSettings writerSettings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    Encoding = new UTF8Encoding(false)
                };

                using (XmlWriter writer = XmlWriter.Create(path, writerSettings))
                {
                    writer.WriteStartDocument();
                    writer.WriteComment(" Tab sizes for Gideon's UI Overhaul, one entry per tab that has been "
                                        + "resized. Written when a resize finishes; safe to hand-edit or to "
                                        + "delete, which puts every tab back to its own default size. ");
                    writer.WriteStartElement("UIOverhaulTabSizes");

                    foreach (KeyValuePair<string, Vector2> entry in Sizes)
                    {
                        writer.WriteStartElement("tab");
                        writer.WriteAttributeString("defName", entry.Key);

                        // Invariant, matching how it is read. A size written with the machine's decimal
                        // separator would fail to parse on a machine that writes it differently, which is a
                        // config that silently resets itself when it is shared.
                        writer.WriteAttributeString("width",
                            entry.Value.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("height",
                            entry.Value.y.ToString(CultureInfo.InvariantCulture));

                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Error(UILogTag.Prefix + $"Could not write {path}.\n{ex}");
            }
        }

        /// <summary>
        /// Reads the file, treating anything unreadable as "no sizes stored".
        ///
        /// <b>A bad entry is skipped rather than failing the file.</b> Every entry here is independent -- one
        /// tab's width has nothing to do with another's -- so one malformed line should cost that tab's size and
        /// not everyone else's. That is the opposite of how the settings file treats a parse failure, and the
        /// difference is that a settings file is a single object where a half-read state is worse than none.
        /// </summary>
        private static Dictionary<string, Vector2> Load()
        {
            Dictionary<string, Vector2> loaded = new Dictionary<string, Vector2>();
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                    return loaded;

                XmlDocument doc = new XmlDocument();
                doc.Load(path);

                XmlElement root = doc.DocumentElement;

                if (root == null)
                    return loaded;

                foreach (XmlNode node in root.ChildNodes)
                {
                    XmlElement element = node as XmlElement;

                    if (element == null || element.Name != "tab")
                        continue;

                    string defName = element.GetAttribute("defName");

                    if (defName.NullOrEmpty())
                        continue;

                    float width;
                    float height;

                    if (!float.TryParse(element.GetAttribute("width"), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out width)
                        || !float.TryParse(element.GetAttribute("height"), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out height))
                        continue;

                    loaded[defName] = new Vector2(width, height);
                }
            }
            catch (Exception ex)
            {
                // Reported rather than swallowed, since a file that cannot be read looks exactly like a feature
                // that has stopped remembering anything, and the player has no other way to tell them apart.
                UIConfigProblems.Report(path, new List<string>
                {
                    "Could not be read, so every tab opens at its own default size: " + ex.Message
                });
            }

            return loaded;
        }

        /// <summary>Drops the loaded map, so the next read comes off disk. For the config watcher.</summary>
        internal static void Reload()
        {
            sizes = null;
            unsaved = false;
        }
    }
}
