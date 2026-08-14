using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using RimWorld;
using Verse;
using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.Pawns.Templates
{
    /// <summary>
    /// The saved templates, as XML in RimWorld's config folder.
    ///
    /// In the config folder rather than in the save, because a template's whole purpose is reuse: the point of
    /// naming an assignment is to put it on the next colonist, in the next colony, in a save that does not exist
    /// yet. Storing it in the save would make it as disposable as the pawn it came from.
    ///
    /// Written by hand rather than through Scribe for the same reason <see cref="ButtonBar.UIButtonBarConfig"/>
    /// is: Scribe belongs to a game in progress, and this file has to be readable with no game loaded and editable
    /// by a player in a text editor.
    /// </summary>
    public static class PawnTemplateStore
    {
        /// <summary>
        /// Deliberately still named for work priorities, which is what this file held when templates could only be
        /// priorities.
        ///
        /// Renaming it would be tidier and would silently throw away every template anyone has already saved, since
        /// nothing would be looking at the old file any more. A stale name is a much smaller cost than that, and the
        /// root element keeps its old name for exactly the same reason.
        /// </summary>
        public const string FileName = "UIOverhaul_WorkTemplates.xml";

        private const string RootElement = "WorkTemplates";

        public static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

        private static List<PawnTemplate> templates;

        /// <summary>
        /// The templates, read on first use. Never null: an unreadable file yields an empty list, so a broken file
        /// costs the player their templates but not their tab.
        /// </summary>
        public static List<PawnTemplate> Templates => templates ?? (templates = Load());

        public static void Reload()
        {
            templates = null;
        }

        /// <summary>
        /// The templates a set of tools should offer, which is every template that speaks for the part being edited.
        ///
        /// <b>Covers, not equals.</b> A whole-pawn template holds a schedule, so the schedule strip's tools offer
        /// it: refusing would mean the player's most complete template is the one template they cannot use where
        /// they want its schedule. Applying from those tools passes the same scope as a limit, so only the schedule
        /// is written -- see <see cref="PawnTemplate.ApplyTo(Pawn, PawnTemplateScope)"/>.
        /// </summary>
        public static List<PawnTemplate> ForScope(PawnTemplateScope wanted)
        {
            List<PawnTemplate> result = new List<PawnTemplate>();

            foreach (PawnTemplate template in Templates)
            {
                if (template.Covers(wanted))
                    result.Add(template);
            }

            return result;
        }

        /// <summary>
        /// Captures the in-scope parts of a pawn under a name derived from them, and saves.
        ///
        /// Named rather than prompted for: the manager window renames in place, so asking for a name first would be
        /// a dialog in front of a dialog that can already do the job.
        /// </summary>
        public static PawnTemplate CaptureFrom(Pawn pawn, PawnTemplateScope scope)
        {
            PawnTemplate template = PawnTemplate.From(pawn, UniqueName(DefaultName(pawn, scope)), scope);

            Templates.Add(template);
            Save();

            return template;
        }

        /// <summary>
        /// What a freshly captured template is called: the pawn plus the part that was taken.
        ///
        /// Named after the scope rather than always "'s template", because the three tool sets each produce their
        /// own kind and a list of eight entries all called "Aaron's template" is a list you cannot use.
        /// </summary>
        public static string DefaultName(Pawn pawn, PawnTemplateScope scope)
        {
            string who = pawn?.LabelShortCap ?? "Colonist";

            if (scope == PawnTemplateScope.Everything)
                return who + "'s setup";

            if (scope == PawnTemplateScope.Priorities)
                return who + "'s priorities";

            if (scope == PawnTemplateScope.Schedule)
                return who + "'s schedule";

            if (scope == PawnTemplateScope.Policies)
                return who + "'s policies";

            return who + "'s template";
        }

        public static void Remove(PawnTemplate template)
        {
            if (template == null)
                return;

            Templates.Remove(template);
            Save();
        }

        /// <summary>
        /// The wanted name, or the wanted name with a number after it if something else already has it.
        ///
        /// Names are what the player picks a template by, so two identical ones are a usability bug even though
        /// nothing here needs them to be unique.
        /// </summary>
        public static string UniqueName(string wanted, PawnTemplate except = null)
        {
            if (wanted.NullOrEmpty())
                wanted = "Template";

            string candidate = wanted;

            for (int suffix = 2; Taken(candidate, except); suffix++)
                candidate = wanted + " " + suffix;

            return candidate;
        }

        private static bool Taken(string name, PawnTemplate except)
        {
            foreach (PawnTemplate template in Templates)
            {
                if (template != except && string.Equals(template.name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------
        // Reading
        //
        // Every field is optional and an unreadable one is skipped rather than failing the template. A file this
        // one is allowed to hand-edit will be hand-edited wrongly, and losing one policy line is a fair outcome
        // where losing the whole set is not.
        // ---------------------------------------------------------------------------------------

        private static List<PawnTemplate> Load()
        {
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                    return new List<PawnTemplate>();

                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                return Read(doc.DocumentElement);
            }
            catch (Exception ex)
            {
                Features.Options.UIConfigProblems.Report(path, new List<string>
                {
                    "Could not be read, so no templates are available: " + ex.Message
                });

                return new List<PawnTemplate>();
            }
        }

        private static List<PawnTemplate> Read(XmlElement root)
        {
            List<PawnTemplate> result = new List<PawnTemplate>();
            if (root == null)
                return result;

            foreach (XmlNode node in root.ChildNodes)
            {
                if (node is XmlElement element && element.Name == "template")
                    result.Add(ReadTemplate(element));
            }

            return result;
        }

        private static PawnTemplate ReadTemplate(XmlElement element)
        {
            PawnTemplate template = new PawnTemplate();

            // Priorities-only unless the file says otherwise, which is what makes every template saved before
            // scope existed read correctly: they were all priorities, and none of them says so.
            bool scopeStated = false;

            foreach (XmlNode node in element.ChildNodes)
            {
                if (!(node is XmlElement field))
                    continue;

                switch (field.Name)
                {
                    case "name":
                        template.name = field.InnerText?.Trim();
                        break;

                    case "scope":
                        template.scope = ReadScope(field.InnerText, template.name);
                        scopeStated = true;
                        break;

                    case "priorities":
                        foreach (XmlNode child in field.ChildNodes)
                        {
                            if (child is XmlElement entry && entry.Name == "priority")
                                ReadPriority(entry, template);
                        }
                        break;

                    case "schedule":
                        template.schedule = ReadSchedule(field);
                        break;

                    case "policies":
                        template.policies = ReadPolicies(field);
                        break;

                    default:
                        Log.Warning(UILogTag.Prefix + $"Unknown template field <{field.Name}>; ignored.");
                        break;
                }
            }

            if (template.name.NullOrEmpty())
                template.name = "Template";

            if (!scopeStated)
                template.scope = PawnTemplateScope.Priorities;

            return template;
        }

        /// <summary>
        /// Reads a scope written as names: "Priorities", "Priorities, Schedule", "Everything".
        ///
        /// Names rather than the enum's numeric value, because this file is meant to be legible. Separators are
        /// generous -- commas, spaces, pipes -- since a player writing this by hand should not have to guess which
        /// one is expected.
        /// </summary>
        private static PawnTemplateScope ReadScope(string text, string templateName)
        {
            PawnTemplateScope scope = PawnTemplateScope.None;
            bool recognized = false;

            string[] tokens = (text ?? string.Empty).Split(',', ' ', '|', '\t', '\n', '\r');

            foreach (string token in tokens)
            {
                string name = token.Trim();

                if (name.Length == 0)
                    continue;

                try
                {
                    scope |= (PawnTemplateScope) Enum.Parse(typeof(PawnTemplateScope), name, true);
                    recognized = true;
                }
                catch (Exception)
                {
                    Log.Warning(UILogTag.Prefix + "Template \"" + (templateName ?? "?") + "\" has an unknown "
                                + "scope \"" + name + "\"; ignored. Expected some of Priorities, Schedule, "
                                + "Policies, Everything.");
                }
            }

            // A scope element that says nothing recognizable is a typo, not a request for an inert template, and
            // the old default is the safe reading of it.
            return recognized ? scope : PawnTemplateScope.Priorities;
        }

        /// <summary>
        /// One work type's value. Stored as two child elements rather than as an element named after the work type,
        /// because a defName is not guaranteed to be a legal XML element name.
        /// </summary>
        private static void ReadPriority(XmlElement entry, PawnTemplate template)
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
                template.priorities[work] = Math.Max(0, Math.Min(Work.WorkPriorityRange.Lowest, value));
        }

        /// <summary>
        /// The 24 hours, as <c>hour</c> elements carrying a TimeAssignmentDef defName.
        ///
        /// <b>Each hour states its own index.</b> Position in the file would have been shorter, and would make a
        /// hand-edited file that deleted one line silently shift every hour after it. The index is what the player
        /// is actually editing by, so it is what the file records; a line without one falls back to its position
        /// so a partly hand-written block still reads.
        /// </summary>
        private static List<string> ReadSchedule(XmlElement element)
        {
            List<string> schedule = new List<string>(PawnTemplate.ScheduleHours);

            for (int hour = 0; hour < PawnTemplate.ScheduleHours; hour++)
                schedule.Add(string.Empty);

            int position = 0;

            foreach (XmlNode node in element.ChildNodes)
            {
                if (!(node is XmlElement field) || field.Name != "hour")
                    continue;

                int hour = position;
                string stated = field.GetAttribute("index");

                if (!stated.NullOrEmpty() && !int.TryParse(stated.Trim(), out hour))
                {
                    Log.Warning(UILogTag.Prefix + "Template schedule hour index \"" + stated
                                + "\" is not a number; taking it in file order instead.");
                    hour = position;
                }

                position++;

                if (hour < 0 || hour >= PawnTemplate.ScheduleHours)
                {
                    Log.Warning(UILogTag.Prefix + "Template schedule has an hour " + hour + ", outside 0 to "
                                + (PawnTemplate.ScheduleHours - 1) + "; ignored.");
                    continue;
                }

                schedule[hour] = field.InnerText?.Trim() ?? string.Empty;
            }

            return schedule;
        }

        private static PawnPolicySet ReadPolicies(XmlElement element)
        {
            PawnPolicySet set = new PawnPolicySet();

            foreach (XmlNode node in element.ChildNodes)
            {
                if (!(node is XmlElement field))
                    continue;

                string value = field.InnerText?.Trim();

                switch (field.Name)
                {
                    case "apparel":
                        set.apparel = value;
                        break;

                    case "drug":
                        set.drug = value;
                        break;

                    case "food":
                        set.food = value;
                        break;

                    case "reading":
                        set.reading = value;
                        break;

                    case "medicalCare":
                        set.medicalCare = ReadEnum<MedicalCareCategory>(value, "medical care");
                        break;

                    case "hostilityResponse":
                        set.hostilityResponse = ReadEnum<HostilityResponseMode>(value, "hostility response");
                        break;

                    case "selfTend":
                        set.selfTend = ReadBool(value);
                        break;

                    default:
                        Log.Warning(UILogTag.Prefix + $"Unknown template policy <{field.Name}>; ignored.");
                        break;
                }
            }

            return set;
        }

        /// <summary>
        /// An enum by name, or null when it cannot be read.
        ///
        /// Null rather than the enum's default, because for every one of these the default is a real setting a
        /// player might want: falling back to it would turn a typo into a template that demands no medicine.
        /// </summary>
        private static T? ReadEnum<T>(string text, string what) where T : struct
        {
            if (text.NullOrEmpty())
                return null;

            try
            {
                return (T) Enum.Parse(typeof(T), text.Trim(), true);
            }
            catch (Exception)
            {
                Log.Warning(UILogTag.Prefix + "Template " + what + " \"" + text + "\" is not a recognized value; "
                            + "left unset. Valid values: " + string.Join(", ", Enum.GetNames(typeof(T))) + ".");

                return null;
            }
        }

        private static bool? ReadBool(string text)
        {
            if (text.NullOrEmpty())
                return null;

            return bool.TryParse(text.Trim(), out bool value) ? value : (bool?) null;
        }

        // ---------------------------------------------------------------------------------------
        // Writing
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Writes every template. Reported and swallowed on failure, as with the other config files: a read-only
        /// config folder must not take a tab down with it.
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
                    WriteFileComment(writer);

                    writer.WriteStartElement(RootElement);

                    foreach (PawnTemplate template in Templates)
                        WriteTemplate(writer, template);

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(UILogTag.Prefix + "Could not save templates to " + path + ".\n" + ex);
            }
        }

        private static void WriteFileComment(XmlWriter writer)
        {
            writer.WriteComment(
                " Pawn templates for Gideon's UI Overhaul. Written by the template manager; safe to hand-edit."
                + "\n\n     <scope>       what the template speaks for: any of Priorities, Schedule, Policies, or"
                + "\n                   Everything. A template with no scope is read as Priorities, which is what"
                + "\n                   every template saved before scope existed was."
                + "\n     <priorities>  <work> is a WorkTypeDef defName and <value> its priority, 0 meaning not"
                + "\n                   assigned."
                + "\n     <schedule>    one <hour index=\"0\"> per hour of the day, holding a TimeAssignmentDef"
                + "\n                   defName. An empty hour is left as the pawn had it."
                + "\n     <policies>    apparel, drug, food and reading are policy names as you typed them, not"
                + "\n                   defNames. Anything omitted here is left as the pawn had it."
                + "\n\n     Applying a template leaves alone anything it does not list, anything the pawn is"
                + "\n     incapable of, and anything it names that no longer exists. ");
        }

        private static void WriteTemplate(XmlWriter writer, PawnTemplate template)
        {
            writer.WriteStartElement("template");

            writer.WriteElementString("name", template.name);
            writer.WriteElementString("scope", DescribeScope(template.scope));

            // Each block is written when it holds something, rather than when the scope claims it. A template whose
            // scope was widened by hand keeps whatever it had, and one whose pawn had nothing to record does not
            // get an empty block implying otherwise.
            if (template.priorities.Count > 0)
                WritePriorities(writer, template);

            if (template.schedule != null)
                WriteSchedule(writer, template);

            if (template.policies != null)
                WritePolicies(writer, template.policies);

            writer.WriteEndElement();
        }

        /// <summary>
        /// The scope as names. <c>ToString</c> on a flags enum already does this, and produces "Priorities,
        /// Schedule, Policies" rather than "Everything" for the combined value, so the one case worth spelling out
        /// is spelled out.
        /// </summary>
        private static string DescribeScope(PawnTemplateScope scope)
        {
            return scope == PawnTemplateScope.Everything ? "Everything" : scope.ToString();
        }

        private static void WritePriorities(XmlWriter writer, PawnTemplate template)
        {
            writer.WriteStartElement("priorities");

            foreach (KeyValuePair<string, int> entry in template.priorities)
            {
                writer.WriteStartElement("priority");
                writer.WriteElementString("work", entry.Key);
                writer.WriteElementString("value", entry.Value.ToString());
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static void WriteSchedule(XmlWriter writer, PawnTemplate template)
        {
            writer.WriteStartElement("schedule");

            for (int hour = 0; hour < template.schedule.Count; hour++)
            {
                writer.WriteStartElement("hour");
                writer.WriteAttributeString("index", hour.ToString());
                writer.WriteString(template.schedule[hour] ?? string.Empty);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static void WritePolicies(XmlWriter writer, PawnPolicySet policies)
        {
            writer.WriteStartElement("policies");

            // Only what the set has an opinion about. An element written for a null policy would read back as a
            // policy named "", which is a name no policy has, and would be reported as unresolvable on every apply.
            WriteIfPresent(writer, "apparel", policies.apparel);
            WriteIfPresent(writer, "drug", policies.drug);
            WriteIfPresent(writer, "food", policies.food);
            WriteIfPresent(writer, "reading", policies.reading);

            if (policies.medicalCare.HasValue)
                writer.WriteElementString("medicalCare", policies.medicalCare.Value.ToString());

            if (policies.hostilityResponse.HasValue)
                writer.WriteElementString("hostilityResponse", policies.hostilityResponse.Value.ToString());

            if (policies.selfTend.HasValue)
                writer.WriteElementString("selfTend", policies.selfTend.Value ? "true" : "false");

            writer.WriteEndElement();
        }

        private static void WriteIfPresent(XmlWriter writer, string name, string value)
        {
            if (!value.NullOrEmpty())
                writer.WriteElementString(name, value);
        }
    }
}
