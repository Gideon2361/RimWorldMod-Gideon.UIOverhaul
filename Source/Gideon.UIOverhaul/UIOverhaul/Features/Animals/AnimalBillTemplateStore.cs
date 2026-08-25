using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Where saved hunting and taming bills live.
    ///
    /// <b>One file beside the settings,</b> which is where this mod's other template store puts its own, and for
    /// the same reason: a player who backs up their config folder backs up their templates with it, and a player
    /// looking for them can find them without being told a path.
    ///
    /// <b>Written by hand rather than through Scribe.</b> Scribe is a singleton owned by whatever save or load is
    /// in progress, and this writes while a game is running. The workbench template store carries the same note.
    ///
    /// <b>Nothing here throws.</b> A template file that has been hand-edited into nonsense is not a reason to
    /// lose the animals tab, so a bad read yields an empty store and a bad write reports false.
    /// </summary>
    internal static class AnimalBillTemplateStore
    {
        internal const string FileName = "UIOverhaul_AnimalBillTemplates.xml";

        private const string Root = "AnimalBillTemplates";
        private const string Element = "Template";

        private static List<AnimalBillTemplate> loaded;

        internal static string FilePath =>
            UIGuard.Try("Animals.Templates.Path",
                () => Path.Combine(GenFilePaths.ConfigFolderPath, FileName), FileName, null);

        internal static List<AnimalBillTemplate> All
        {
            get
            {
                if (loaded == null)
                    loaded = Read(FilePath);

                return loaded;
            }
        }

        /// <summary>Every stored template of one kind, which is all a given window can offer.</summary>
        internal static List<AnimalBillTemplate> Of(bool taming)
        {
            List<AnimalBillTemplate> found = new List<AnimalBillTemplate>();
            List<AnimalBillTemplate> all = All;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Taming == taming)
                    found.Add(all[i]);
            }

            return found;
        }

        internal static void Reload()
        {
            loaded = null;
        }

        /// <summary>Stores a template, giving it a free name if the one it arrived with is taken.</summary>
        internal static void Add(AnimalBillTemplate template)
        {
            if (template == null)
                return;

            template.Name = FreeName(All, template.Name);

            All.Add(template);

            Save();
        }

        internal static void Delete(AnimalBillTemplate template)
        {
            if (template == null)
                return;

            All.Remove(template);

            Save();
        }

        /// <summary>
        /// Renames a template, refusing a blank or a duplicate rather than quietly making one up.
        ///
        /// The other direction from <see cref="Add"/> on purpose: a name arriving from an import is data being
        /// tidied and a name arriving from a text box is a person being told what they typed will not work.
        /// </summary>
        internal static bool Rename(AnimalBillTemplate template, string name, out string problem)
        {
            problem = null;

            if (template == null)
                return false;

            name = (name ?? string.Empty).Trim();

            if (name.NullOrEmpty())
            {
                problem = "A template needs a name.";

                return false;
            }

            List<AnimalBillTemplate> all = All;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != template && all[i] != null && all[i].Name.EqualsIgnoreCase(name))
                {
                    problem = "There is already a template called " + name + ".";

                    return false;
                }
            }

            template.Name = name;

            Save();

            return true;
        }

        internal static void Save()
        {
            UIGuard.Try("Animals.Templates.Save", () => Write(FilePath, All),
                "The animal bill templates could not be saved.");
        }

        /// <summary>
        /// Adds every template in another file to this one.
        ///
        /// <b>Added rather than replacing,</b> because importing somebody else's set should not throw away your
        /// own. Names that collide are given a free one and reported, so nothing is silently overwritten and
        /// nothing is silently dropped.
        /// </summary>
        internal static bool Import(string path, out int added, out string problem)
        {
            int count = 0;
            string fault = null;

            bool ok = UIGuard.Try("Animals.Templates.Import", () =>
            {
                if (!File.Exists(path))
                {
                    fault = "There is no file at " + path + ".";

                    return false;
                }

                List<AnimalBillTemplate> incoming = Read(path);

                if (incoming.Count == 0)
                {
                    fault = "That file holds no animal bill templates.";

                    return false;
                }

                for (int i = 0; i < incoming.Count; i++)
                {
                    incoming[i].Name = FreeName(All, incoming[i].Name);

                    All.Add(incoming[i]);

                    count++;
                }

                Save();

                return true;
            }, false, null);

            added = count;
            problem = fault ?? (ok ? null : "That file could not be read.");

            return ok;
        }

        internal static bool Export(string path, out string problem)
        {
            string fault = null;

            bool ok = UIGuard.Try("Animals.Templates.Export", () =>
            {
                Write(path, All);

                return true;
            }, false, null);

            problem = fault ?? (ok ? null : "That file could not be written.");

            return ok;
        }

        /// <summary>A name nothing else is using, by adding a number until one is free.</summary>
        internal static string FreeName(List<AnimalBillTemplate> existing, string wanted)
        {
            wanted = (wanted ?? string.Empty).Trim();

            if (wanted.NullOrEmpty())
                wanted = "Template";

            if (!Taken(existing, wanted))
                return wanted;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = wanted + " " + i;

                if (!Taken(existing, candidate))
                    return candidate;
            }

            return wanted + " " + Find.TickManager.TicksGame;
        }

        private static bool Taken(List<AnimalBillTemplate> existing, string name)
        {
            for (int i = 0; existing != null && i < existing.Count; i++)
            {
                if (existing[i] != null && existing[i].Name.EqualsIgnoreCase(name))
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------
        // The file
        // ---------------------------------------------------------------------------------------

        internal static List<AnimalBillTemplate> Read(string path)
        {
            return UIGuard.Try("Animals.Templates.Read", () =>
            {
                List<AnimalBillTemplate> found = new List<AnimalBillTemplate>();

                if (!File.Exists(path))
                    return found;

                XmlDocument document = new XmlDocument();

                document.Load(path);

                XmlNode root = document.DocumentElement;

                if (root == null)
                    return found;

                foreach (XmlNode node in root.ChildNodes)
                {
                    if (node == null || node.Name != Element)
                        continue;

                    AnimalBillTemplate template = One(node);

                    if (template != null)
                        found.Add(template);
                }

                return found;
            }, new List<AnimalBillTemplate>(), null);
        }

        private static AnimalBillTemplate One(XmlNode node)
        {
            AnimalBillTemplate template = new AnimalBillTemplate
            {
                Name = Text(node, "name", "Template"),
                Taming = Text(node, "taming", "false").EqualsIgnoreCase("true"),
                Mode = Text(node, "mode", "UntilStocked"),
                TargetCount = Number(node, "targetCount", 300),
                ResumeAt = Number(node, "resumeAt", -1),
                KeepAlive = Number(node, "keepAlive", 2),
                MaxPopulation = Number(node, "maxPopulation", 6),
                MaxOutstanding = Number(node, "maxOutstanding", 6),
                AllowPredators = Text(node, "allowPredators", "false").EqualsIgnoreCase("true"),
                MaxManhunterChance = Real(node, "maxManhunterChance", 0.1f),
                MinTameChance = Real(node, "minTameChance", 0.05f)
            };

            List(node, "items", template.Items);
            List(node, "species", template.Species);

            XmlNode targets = node.SelectSingleNode("targets");

            if (targets != null)
            {
                foreach (XmlNode entry in targets.ChildNodes)
                {
                    if (entry == null || entry.Name != "li")
                        continue;

                    string species = Text(entry, "species", null);

                    if (species.NullOrEmpty())
                        continue;

                    template.Targets.Add(new AnimalBillTarget
                    {
                        Species = species,
                        Males = Number(entry, "males", 0),
                        Females = Number(entry, "females", 0)
                    });
                }
            }

            return template;
        }

        private static void List(XmlNode node, string name, List<string> into)
        {
            XmlNode parent = node.SelectSingleNode(name);

            if (parent == null)
                return;

            foreach (XmlNode entry in parent.ChildNodes)
            {
                if (entry != null && entry.Name == "li" && !entry.InnerText.NullOrEmpty())
                    into.Add(entry.InnerText.Trim());
            }
        }

        private static string Text(XmlNode parent, string name, string fallback)
        {
            XmlNode node = parent.SelectSingleNode(name);

            return node == null || node.InnerText.NullOrEmpty() ? fallback : node.InnerText.Trim();
        }

        private static int Number(XmlNode parent, string name, int fallback)
        {
            int parsed;

            return int.TryParse(Text(parent, name, string.Empty), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        /// <summary>
        /// A number with a decimal point, read in the invariant culture.
        ///
        /// Invariant is the point: this file is shared between players, and a chance written as 0.05 must not
        /// stop parsing because the game is running in a language that writes it 0,05.
        /// </summary>
        private static float Real(XmlNode parent, string name, float fallback)
        {
            float parsed;

            return float.TryParse(Text(parent, name, string.Empty), NumberStyles.Float,
                CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        internal static void Write(string path, List<AnimalBillTemplate> list)
        {
            string folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  "
            };

            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement(Root);

                writer.WriteComment(" Saved hunting and taming bills for Gideon's UI Overhaul. Safe to edit by "
                                    + "hand, to copy between installs, and to delete. ");

                foreach (AnimalBillTemplate template in list ?? new List<AnimalBillTemplate>())
                {
                    if (template == null)
                        continue;

                    writer.WriteStartElement(Element);

                    Element_(writer, "name", template.Name);
                    Element_(writer, "taming", template.Taming ? "true" : "false");
                    Element_(writer, "maxOutstanding", template.MaxOutstanding.ToString(CultureInfo.InvariantCulture));

                    if (template.Taming)
                    {
                        Element_(writer, "minTameChance",
                            template.MinTameChance.ToString("0.###", CultureInfo.InvariantCulture));

                        writer.WriteStartElement("targets");

                        foreach (AnimalBillTarget target in template.Targets ?? new List<AnimalBillTarget>())
                        {
                            if (target == null || target.Species.NullOrEmpty())
                                continue;

                            writer.WriteStartElement("li");

                            Element_(writer, "species", target.Species);
                            Element_(writer, "males", target.Males.ToString(CultureInfo.InvariantCulture));
                            Element_(writer, "females", target.Females.ToString(CultureInfo.InvariantCulture));

                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                    }
                    else
                    {
                        Element_(writer, "mode", template.Mode);
                        Element_(writer, "targetCount", template.TargetCount.ToString(CultureInfo.InvariantCulture));
                        Element_(writer, "resumeAt", template.ResumeAt.ToString(CultureInfo.InvariantCulture));
                        Element_(writer, "keepAlive", template.KeepAlive.ToString(CultureInfo.InvariantCulture));
                        Element_(writer, "maxPopulation",
                            template.MaxPopulation.ToString(CultureInfo.InvariantCulture));
                        Element_(writer, "allowPredators", template.AllowPredators ? "true" : "false");
                        Element_(writer, "maxManhunterChance",
                            template.MaxManhunterChance.ToString("0.###", CultureInfo.InvariantCulture));

                        Names(writer, "items", template.Items);
                        Names(writer, "species", template.Species);
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        private static void Names(XmlWriter writer, string name, List<string> values)
        {
            writer.WriteStartElement(name);

            foreach (string value in values ?? new List<string>())
            {
                if (!value.NullOrEmpty())
                    writer.WriteElementString("li", value);
            }

            writer.WriteEndElement();
        }

        /// <summary>Named with a trailing underscore because <c>Element</c> is already the element name above.</summary>
        private static void Element_(XmlWriter writer, string name, string value)
        {
            writer.WriteElementString(name, value ?? string.Empty);
        }
    }
}
