using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Verse;

namespace Gideon.UIOverhaul.Features.Work
{
    /// <summary>
    /// The saved work priority templates, as XML in RimWorld's config folder.
    ///
    /// In the config folder rather than in the save, because a template's whole purpose is reuse: the point of
    /// naming an assignment is to put it on the next colonist, in the next colony, in a save that does not
    /// exist yet. Storing it in the save would make it as disposable as the pawn it came from.
    ///
    /// Written by hand rather than through Scribe for the same reason <see cref="ButtonBar.UIButtonBarConfig"/>
    /// is: Scribe belongs to a game in progress, and this file has to be readable with no game loaded and
    /// editable by a player in a text editor.
    /// </summary>
    public static class WorkTemplateStore
    {
        public const string FileName = "UIOverhaul_WorkTemplates.xml";

        public static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

        private static List<WorkPriorityTemplate> templates;

        /// <summary>
        /// The templates, read on first use. Never null: an unreadable file yields an empty list, so a broken
        /// file costs the player their templates but not their work tab.
        /// </summary>
        public static List<WorkPriorityTemplate> Templates => templates ?? (templates = Load());

        public static void Reload()
        {
            templates = null;
        }

        /// <summary>
        /// Captures a pawn's priorities under a name derived from them, and saves.
        ///
        /// Named rather than prompted for: the manager window renames in place, so asking for a name first
        /// would be a dialog in front of a dialog that can already do the job.
        /// </summary>
        public static WorkPriorityTemplate CaptureFrom(Pawn pawn)
        {
            WorkPriorityTemplate template = WorkPriorityTemplate.From(pawn,
                UniqueName(pawn.LabelShortCap + "'s priorities"));

            Templates.Add(template);
            Save();

            return template;
        }

        public static void Remove(WorkPriorityTemplate template)
        {
            if (template == null)
                return;

            Templates.Remove(template);
            Save();
        }

        /// <summary>
        /// The wanted name, or the wanted name with a number after it if something else already has it.
        ///
        /// Names are what the player picks a template by, so two identical ones are a usability bug even
        /// though nothing here needs them to be unique.
        /// </summary>
        public static string UniqueName(string wanted, WorkPriorityTemplate except = null)
        {
            if (wanted.NullOrEmpty())
                wanted = "Template";

            string candidate = wanted;

            for (int suffix = 2; Taken(candidate, except); suffix++)
                candidate = wanted + " " + suffix;

            return candidate;
        }

        private static bool Taken(string name, WorkPriorityTemplate except)
        {
            foreach (WorkPriorityTemplate template in Templates)
            {
                if (template != except && string.Equals(template.name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------
        // Reading and writing
        // ---------------------------------------------------------------------------------------

        private static List<WorkPriorityTemplate> Load()
        {
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                    return new List<WorkPriorityTemplate>();

                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                return Read(doc.DocumentElement);
            }
            catch (Exception ex)
            {
                Features.Options.UIConfigProblems.Report(path, new List<string>
                {
                    "Could not be read, so no work priority templates are available: " + ex.Message
                });

                return new List<WorkPriorityTemplate>();
            }
        }

        private static List<WorkPriorityTemplate> Read(XmlElement root)
        {
            List<WorkPriorityTemplate> result = new List<WorkPriorityTemplate>();
            if (root == null)
                return result;

            foreach (XmlNode node in root.ChildNodes)
            {
                if (node is XmlElement element && element.Name == "template")
                    result.Add(ReadTemplate(element));
            }

            return result;
        }

        private static WorkPriorityTemplate ReadTemplate(XmlElement element)
        {
            WorkPriorityTemplate template = new WorkPriorityTemplate();

            foreach (XmlNode node in element.ChildNodes)
            {
                if (!(node is XmlElement field))
                    continue;

                switch (field.Name)
                {
                    case "name":
                        template.name = field.InnerText?.Trim();
                        break;

                    case "priorities":
                        foreach (XmlNode child in field.ChildNodes)
                        {
                            if (child is XmlElement entry && entry.Name == "priority")
                                ReadPriority(entry, template);
                        }
                        break;

                    default:
                        Log.Warning($"[Gideon.UIOverhaul] Unknown work template field <{field.Name}>; ignored.");
                        break;
                }
            }

            if (template.name.NullOrEmpty())
                template.name = "Template";

            return template;
        }

        /// <summary>
        /// One work type's value. Stored as two child elements rather than as an element named after the work
        /// type, because a defName is not guaranteed to be a legal XML element name.
        /// </summary>
        private static void ReadPriority(XmlElement entry, WorkPriorityTemplate template)
        {
            string work = null;
            int value = 0;

            foreach (XmlNode node in entry.ChildNodes)
            {
                if (!(node is XmlElement field))
                    continue;

                if (field.Name == "work")
                    work = field.InnerText?.Trim();
                else if (field.Name == "value")
                    int.TryParse(field.InnerText?.Trim(), out value);
            }

            if (!work.NullOrEmpty())
                template.priorities[work] = Math.Max(0, Math.Min(WorkPriorityRange.Lowest, value));
        }

        /// <summary>
        /// Writes every template. Reported and swallowed on failure, as with the other config files: a
        /// read-only config folder must not take the work tab down with it.
        /// </summary>
        public static void Save()
        {
            string path = FilePath;

            try
            {
                // So the config watcher does not mistake our own write for someone editing the file.
                Features.Options.UIConfigWatcher.NotifySelfWrite();

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
                    writer.WriteComment(
                        " Work priority templates for Gideon's UI Overhaul. Written by the work tab's template"
                        + " manager; safe to hand-edit. <work> is a WorkTypeDef defName and <value> its"
                        + " priority, 0 meaning not assigned. Applying a template leaves any work type not"
                        + " listed here alone. ");

                    writer.WriteStartElement("WorkTemplates");

                    foreach (WorkPriorityTemplate template in Templates)
                    {
                        writer.WriteStartElement("template");
                        writer.WriteElementString("name", template.name);

                        writer.WriteStartElement("priorities");

                        foreach (KeyValuePair<string, int> entry in template.priorities)
                        {
                            writer.WriteStartElement("priority");
                            writer.WriteElementString("work", entry.Key);
                            writer.WriteElementString("value", entry.Value.ToString());
                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Gideon.UIOverhaul] Could not save work priority templates to " + path + ".\n"
                            + ex);
            }
        }
    }
}
