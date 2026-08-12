using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Gideon.UIFramework.Defs;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// This mod's player-facing settings, stored as XML in RimWorld's config folder beside the game's
    /// own settings and the button bar layout.
    ///
    /// Not ModSettings. These are preferences that have to be readable before defs finish loading -- the
    /// chosen theme in particular -- and keeping them in a plain file next to the bar layout means one
    /// place to look, one format, and something a player can inspect or share.
    /// </summary>
    public class UIOverhaulSettingsFile
    {
        public const string FileName = "UIOverhaul_Settings.xml";

        /// <summary>
        /// defName of the palette the player chose. Empty means the shipped default.
        /// </summary>
        public string activePalette = "";

        // There is deliberately no option to hide the bar's UI options button. It used to exist, back when
        // these settings were also reachable from the vanilla Options window; that route turned out to be
        // impossible -- Dialog_Options ignores any OptionCategoryDef from a mod -- which leaves the bar
        // button as the only way in. An option to remove the only way in is a trap, not a preference.

        public static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

        private static UIOverhaulSettingsFile current;

        public static UIOverhaulSettingsFile Current => current ?? (current = Load());

        public static void Reload()
        {
            current = null;
        }

        /// <summary>
        /// Pushes the stored theme into the framework. Called once the def database exists, since a
        /// palette is a Def and cannot be resolved before then.
        /// </summary>
        public void ApplyTheme()
        {
            UIColorPaletteDef.ActiveDefName = activePalette.NullOrEmpty() ? null : activePalette;

            if (UIColorPaletteDef.ActiveIsMissing)
            {
                Log.Warning($"[Gideon.UIOverhaul] Palette '{activePalette}' is not loaded -- the mod that "
                            + "supplied it may be disabled. Falling back to the default theme.");
                UIColorPaletteDef.ActiveDefName = null;
            }
        }

        private static UIOverhaulSettingsFile Load()
        {
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                    return new UIOverhaulSettingsFile();

                XmlDocument doc = new XmlDocument();
                doc.Load(path);

                UIOverhaulSettingsFile settings = new UIOverhaulSettingsFile();
                XmlElement root = doc.DocumentElement;
                if (root == null)
                    return settings;

                foreach (XmlNode node in root.ChildNodes)
                {
                    if (!(node is XmlElement field))
                        continue;

                    string value = field.InnerText?.Trim();

                    switch (field.Name)
                    {
                        case "activePalette":
                            settings.activePalette = value ?? "";
                            break;

                        case "showBarButton":
                            // Retired setting. Accepted silently so an older config file does not raise a
                            // warning about something the player never chose to write.
                            break;

                        default:
                            Log.Warning($"[Gideon.UIOverhaul] Unknown setting <{field.Name}>; ignored.");
                            break;
                    }
                }

                return settings;
            }
            catch (Exception ex)
            {
                // Reported rather than logged and forgotten. Discarding the file silently would look
                // like a hand-edit had no effect.
                UIConfigProblems.Report(path, new List<string>
                {
                    "Could not be read, so the previous settings are still in use: " + ex.Message
                });

                return new UIOverhaulSettingsFile();
            }
        }

        public void Save()
        {
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
                    writer.WriteComment(" Settings for Gideon's UI Overhaul. Written by the UI options "
                                        + "page; safe to hand-edit. ");
                    writer.WriteStartElement("UIOverhaulSettings");
                    writer.WriteElementString("activePalette", activePalette ?? "");
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Gideon.UIOverhaul] Could not write {path}.\n{ex}");
            }
        }
    }
}
