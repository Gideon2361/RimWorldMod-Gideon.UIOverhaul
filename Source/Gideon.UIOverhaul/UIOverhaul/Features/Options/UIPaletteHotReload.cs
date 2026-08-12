using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Gideon.UIFramework.Components.Colors;
using Gideon.UIFramework.Defs;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// Applies edits to palette XML without restarting the game, so a theme can be tuned by saving the
    /// file and looking at the result.
    ///
    /// Palettes are Defs, and there is no re-running RimWorld's def loader short of restarting. What
    /// makes this possible anyway is that a UIColorPaletteDef holds nothing but authored strings: the
    /// file can be parsed here and the values assigned straight onto the live def instance, then its
    /// derived colors invalidated.
    ///
    /// Two limits, both inherent rather than shortcuts:
    ///
    /// * Only fields the file actually names are applied. That is deliberate -- values a palette inherits
    ///   through ParentName were resolved by XmlInheritance during def load, and re-reading one file in
    ///   isolation cannot see them. The consequence is that *deleting* a field will not revert it to the
    ///   parent's value until the game restarts, though changing one always takes effect.
    /// * A palette added to the file after startup is not created. There is no def to assign to, and
    ///   registering one properly means def load order, inheritance and cross-references.
    /// </summary>
    public static class UIPaletteHotReload
    {
        /// <summary>
        /// Marker used to decide whether a Defs file is worth watching. Matches both the short and the
        /// fully qualified element name.
        /// </summary>
        private const string Marker = "UIColorPaletteDef";

        private static readonly List<string> WatchedFiles = new List<string>();
        private static readonly List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();

        private static bool started;

        /// <summary>
        /// Finds every palette file across active mods and watches the folders holding them.
        ///
        /// Only folders that actually contain a palette are watched. Watching every mod's Defs tree would
        /// mean one OS handle per mod, which on a large load order is a real cost for a developer
        /// convenience.
        /// </summary>
        public static void Start(FileSystemEventHandler onChanged)
        {
            if (started)
                return;

            started = true;

            try
            {
                FindPaletteFiles();

                HashSet<string> folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in WatchedFiles)
                {
                    string folder = Path.GetDirectoryName(file);
                    if (!folder.NullOrEmpty())
                        folders.Add(folder);
                }

                foreach (string folder in folders)
                {
                    FileSystemWatcher watcher = new FileSystemWatcher(folder, "*.xml")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                        IncludeSubdirectories = false
                    };

                    watcher.Changed += onChanged;
                    watcher.Created += onChanged;
                    watcher.EnableRaisingEvents = true;
                    Watchers.Add(watcher);
                }

                if (WatchedFiles.Count > 0)
                {
                    Log.Message($"[Gideon.UIOverhaul] Watching {WatchedFiles.Count} palette file(s) for "
                                + "live edits.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Gideon.UIOverhaul] Could not watch palette files for changes; theme edits "
                            + "will need a restart.\n" + ex);
            }
        }

        /// <summary>
        /// Re-reads every known palette file and pushes its values onto the live defs. Main thread only:
        /// it reassigns fields the UI reads while drawing.
        /// </summary>
        public static void ReapplyAll()
        {
            for (int i = 0; i < WatchedFiles.Count; i++)
                Reapply(WatchedFiles[i]);
        }

        /// <summary>One validated field assignment, held back until the whole file has been checked.</summary>
        private struct PendingField
        {
            public UIColorPaletteDef Def;
            public FieldInfo Field;
            public object Value;
        }

        /// <summary>
        /// Reads one file, validates every value in it, and applies the lot only if all of it is good.
        ///
        /// All or nothing on purpose. Assigning as it goes would leave a palette half updated when the
        /// third color of five has a typo, giving a UI that matches neither the old file nor the new one
        /// and no obvious way back short of a restart.
        /// </summary>
        private static void Reapply(string path)
        {
            List<string> problems = new List<string>();
            List<PendingField> pending = new List<PendingField>();
            HashSet<UIColorPaletteDef> touched = new HashSet<UIColorPaletteDef>();

            try
            {
                if (!File.Exists(path))
                    return;

                XmlDocument doc = new XmlDocument();
                doc.Load(path);

                XmlElement root = doc.DocumentElement;
                if (root == null)
                {
                    problems.Add("The file has no root element.");
                }
                else
                {
                    foreach (XmlNode node in root.ChildNodes)
                    {
                        if (!(node is XmlElement element))
                            continue;

                        // Both <UIColorPaletteDef> and <Gideon.UIFramework.Defs.UIColorPaletteDef> are
                        // valid in a Defs file, so match on the tail of the element name.
                        if (element.Name.EndsWith(Marker, StringComparison.Ordinal))
                            Stage(element, pending, touched, problems);
                    }
                }
            }
            catch (XmlException ex)
            {
                // Malformed XML. Worth telling the player about: mid-save the file is briefly unreadable,
                // but the debounce means by the time we get here it should have settled.
                problems.Add("The file is not valid XML: " + ex.Message);
            }
            catch (Exception ex)
            {
                problems.Add("Could not be read: " + ex.Message);
            }

            if (problems.Count > 0)
            {
                UIConfigProblems.Report(path, problems);
                return;
            }

            foreach (PendingField field in pending)
                field.Field.SetValue(field.Def, field.Value);

            foreach (UIColorPaletteDef def in touched)
                def.Invalidate();

            if (touched.Count > 0)
            {
                Log.Message($"[Gideon.UIOverhaul] Reapplied {touched.Count} palette(s) from "
                            + Path.GetFileName(path) + ".");
            }
        }

        /// <summary>
        /// Validates one palette element and queues its assignments. Adds to <paramref name="problems"/>
        /// rather than throwing, so every fault in the file is reported at once instead of one per save.
        /// </summary>
        private static void Stage(XmlElement element, List<PendingField> pending,
            HashSet<UIColorPaletteDef> touched, List<string> problems)
        {
            string defName = element["defName"]?.InnerText?.Trim();
            if (defName.NullOrEmpty())
            {
                problems.Add("A palette has no <defName>.");
                return;
            }

            UIColorPaletteDef def = DefDatabase<UIColorPaletteDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                // Not a fault. An abstract parent has no database entry, and a palette added to the file
                // since startup has no instance to assign to -- both need a restart, neither is an error
                // in what the player wrote.
                return;
            }

            foreach (XmlNode node in element.ChildNodes)
            {
                if (!(node is XmlElement field))
                    continue;

                // Identity, not appearance: changing these live would be misleading, since the def
                // database is keyed on defName and a selector caches the label.
                if (field.Name == "defName" || field.Name == "label" || field.Name == "description")
                    continue;

                // Reflection rather than a switch per role: the palette gains fields as roles are added,
                // and a hand-written list here would silently stop covering the new ones.
                FieldInfo info = typeof(UIColorPaletteDef).GetField(field.Name,
                    BindingFlags.Public | BindingFlags.Instance);

                if (info == null)
                {
                    problems.Add($"'{defName}': <{field.Name}> is not a palette field. Check the spelling "
                                 + "against Help/UIColorPaletteDef.md.");
                    continue;
                }

                string value = field.InnerText?.Trim();

                if (info.FieldType == typeof(string))
                {
                    // Color roles are the fields whose names the parser can vet. Texture paths are also
                    // strings and cannot be checked here without hitting the disk, so they are taken
                    // as written and reported by the loader if they turn out to be missing.
                    if (IsColorField(info.Name) && !UIColorParser.TryParse(value, out _, out string error))
                    {
                        problems.Add($"'{defName}': <{field.Name}> is not a color -- {error}");
                        continue;
                    }

                    pending.Add(new PendingField { Def = def, Field = info, Value = value });
                    touched.Add(def);
                }
                else if (info.FieldType == typeof(bool))
                {
                    if (!bool.TryParse(value, out bool flag))
                    {
                        problems.Add($"'{defName}': <{field.Name}> should be true or false, not '{value}'.");
                        continue;
                    }

                    pending.Add(new PendingField { Def = def, Field = info, Value = flag });
                    touched.Add(def);
                }

                // Anything else -- the custom color list, for one -- is a real field this cannot hot
                // reload. Silently skipped rather than reported: the file is not wrong, we are limited.
            }
        }

        /// <summary>
        /// Whether a string field holds a color, and so can be validated before being applied. Decided by
        /// asking the role enum, so a role added later is covered without touching this.
        /// </summary>
        private static bool IsColorField(string fieldName)
        {
            foreach (UIColorRole role in Enum.GetValues(typeof(UIColorRole)))
            {
                if (UIColorPaletteDef.FieldNameOf(role) == fieldName)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Scans the Defs folders of every active mod for files mentioning a palette.
        ///
        /// Bounded to Defs rather than the whole mod root, and matched on file content rather than name,
        /// so a palette shipped in a file called anything at all is still found.
        /// </summary>
        private static void FindPaletteFiles()
        {
            WatchedFiles.Clear();

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            if (mods == null)
                return;

            foreach (ModContentPack mod in mods)
            {
                if (mod?.RootDir == null)
                    continue;

                foreach (string root in DefsFolders(mod))
                {
                    string[] files;
                    try
                    {
                        if (!Directory.Exists(root))
                            continue;

                        files = Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (string file in files)
                    {
                        try
                        {
                            if (File.ReadAllText(file).IndexOf(Marker, StringComparison.Ordinal) >= 0)
                                WatchedFiles.Add(file);
                        }
                        catch
                        {
                            // Unreadable file: not ours to complain about.
                        }
                    }
                }
            }
        }

        private static IEnumerable<string> DefsFolders(ModContentPack mod)
        {
            List<string> folders = mod.foldersToLoadDescendingOrder;

            if (folders != null)
            {
                foreach (string folder in folders)
                {
                    string trimmed = folder?.Trim();
                    if (trimmed.NullOrEmpty())
                        continue;

                    string baseDir = Directory.Exists(trimmed)
                        ? trimmed
                        : Path.Combine(mod.RootDir, trimmed.TrimStart('/', '\\'));

                    yield return Path.Combine(baseDir, "Defs");
                }
            }

            yield return Path.Combine(mod.RootDir, "Defs");
        }
    }
}
