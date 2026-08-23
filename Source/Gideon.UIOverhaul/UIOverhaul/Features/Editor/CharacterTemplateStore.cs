using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Saved characters, as one XML file each in RimWorld's config folder.
    ///
    /// <b>A file per character rather than one file of many,</b> which is where this differs from the work
    /// template store next door. The point of these is to leave the machine: a character you want to give somebody
    /// is a file you can attach, and a character somebody sends you is a file you drop in a folder. One combined
    /// document would mean editing XML by hand to share a single pawn.
    ///
    /// <b>Written by hand rather than through Scribe,</b> for the reason the work templates give and one more:
    /// Scribe is a singleton owned by whatever save or load is in progress, and this writes while a game is
    /// running. Hand-written XML also survives being opened in a text editor, which somebody trading characters
    /// will do.
    ///
    /// <b>Nothing is cached.</b> The folder is small, the list is only read when a window opens, and a player who
    /// drops a file in expects to see it without restarting.
    /// </summary>
    internal static class CharacterTemplateStore
    {
        private const string FolderName = "GideonCharacters";

        private const string RootElement = "GideonCharacter";

        private const string Extension = ".xml";

        internal static string FolderPath
        {
            get { return Path.Combine(GenFilePaths.ConfigFolderPath, FolderName); }
        }

        // ---------------------------------------------------------------------------------------
        // Reading
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Every readable character in the folder, newest first.
        ///
        /// A file that will not parse is skipped and logged rather than throwing: one corrupt character must not
        /// cost the player the other twenty, and the list is the only way they would find out which one it was.
        /// </summary>
        internal static List<CharacterTemplate> All()
        {
            List<CharacterTemplate> found = new List<CharacterTemplate>();

            UIGuard.Try("Template.List", () =>
            {
                string folder = FolderPath;

                if (!Directory.Exists(folder))
                    return;

                string[] files = Directory.GetFiles(folder, "*" + Extension);

                List<string> ordered = new List<string>(files);

                ordered.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));

                for (int i = 0; i < ordered.Count; i++)
                {
                    CharacterTemplate template = Read(ordered[i]);

                    if (template != null)
                        found.Add(template);
                }
            }, "The saved characters folder could not be read.");

            return found;
        }

        private static CharacterTemplate Read(string path)
        {
            try
            {
                XmlDocument document = new XmlDocument();

                document.Load(path);

                XmlNode root = document.DocumentElement;

                if (root == null || root.Name != RootElement)
                    return null;

                CharacterTemplate template = new CharacterTemplate
                {
                    Name = Text(root, "name") ?? Path.GetFileNameWithoutExtension(path),
                    SavedFrom = Text(root, "savedFrom"),
                    SavedAt = Text(root, "savedAt"),
                    First = Text(root, "first"),
                    Nick = Text(root, "nick"),
                    Last = Text(root, "last"),
                    Gender = Text(root, "gender"),
                    BiologicalYears = Number(root, "biologicalYears"),
                    ChronologicalYears = Number(root, "chronologicalYears"),
                    Childhood = Text(root, "childhood"),
                    Adulthood = Text(root, "adulthood"),
                    BodyType = Text(root, "bodyType"),
                    HeadType = Text(root, "headType"),
                    Hair = Text(root, "hair"),
                    Beard = Text(root, "beard"),
                    FaceTattoo = Text(root, "faceTattoo"),
                    BodyTattoo = Text(root, "bodyTattoo"),
                    SkinColor = Text(root, "skinColor"),
                    HairColor = Text(root, "hairColor"),
                    Xenotype = Text(root, "xenotype")
                };

                XmlNode traits = root.SelectSingleNode("traits");

                if (traits != null)
                {
                    foreach (XmlNode node in traits.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element)
                            continue;

                        template.Traits.Add(new TemplateTrait
                        {
                            DefName = Text(node, "def"),
                            Degree = Number(node, "degree", 0)
                        });
                    }
                }

                XmlNode skills = root.SelectSingleNode("skills");

                if (skills != null)
                {
                    foreach (XmlNode node in skills.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element)
                            continue;

                        template.Skills.Add(new TemplateSkill
                        {
                            DefName = Text(node, "def"),
                            Level = Number(node, "level", 0),
                            Passion = Text(node, "passion")
                        });
                    }
                }

                XmlNode genes = root.SelectSingleNode("genes");

                if (genes != null)
                {
                    foreach (XmlNode node in genes.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element)
                            continue;

                        template.Genes.Add(new TemplateGene
                        {
                            DefName = Text(node, "def"),
                            Xenogene = Text(node, "xenogene") == "true"
                        });
                    }
                }

                XmlNode apparel = root.SelectSingleNode("apparel");

                if (apparel != null)
                {
                    foreach (XmlNode node in apparel.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element)
                            continue;

                        template.Apparel.Add(Thing(node));
                    }
                }

                XmlNode weapon = root.SelectSingleNode("weapon");

                if (weapon != null)
                    template.Weapon = Thing(weapon);

                XmlNode health = root.SelectSingleNode("health");

                if (health != null)
                {
                    foreach (XmlNode node in health.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element)
                            continue;

                        template.Health.Add(new TemplateHediff
                        {
                            DefName = Text(node, "def"),
                            PartDef = Text(node, "part"),
                            PartIndex = Number(node, "partIndex"),
                            Severity = Real(node, "severity"),
                            Permanent = Text(node, "permanent") == "true",
                            Order = Number(node, "order", 2)
                        });
                    }

                    // Sorted on read as well as on write, so a hand-edited file that lists an implant before the
                    // missing part it sits on still applies in the order the categories need.
                    template.Health.Sort((a, b) => a.Order.CompareTo(b.Order));
                }

                return template;
            }
            catch (Exception ex)
            {
                Log.Warning(UILogTag.Prefix + "Could not read the saved character at " + path + ".\n" + ex);

                return null;
            }
        }

        private static TemplateThing Thing(XmlNode node)
        {
            return new TemplateThing
            {
                DefName = Text(node, "def"),
                Stuff = Text(node, "stuff"),
                Quality = Text(node, "quality")
            };
        }

        private static string Text(XmlNode parent, string name)
        {
            XmlNode node = parent.SelectSingleNode(name);

            return node == null || node.InnerText.NullOrEmpty() ? null : node.InnerText.Trim();
        }

        private static int Number(XmlNode parent, string name, int fallback = -1)
        {
            string text = Text(parent, name);

            int value;

            return !text.NullOrEmpty()
                   && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        /// <summary>
        /// A severity, read invariantly.
        ///
        /// <c>InvariantCulture</c> on both sides and not by habit: a machine set to a comma decimal separator
        /// would write "0,42" and then read it back as 42, which is the difference between a scar and a dead pawn.
        /// </summary>
        private static float Real(XmlNode parent, string name, float fallback = 0f)
        {
            string text = Text(parent, name);

            float value;

            return !text.NullOrEmpty()
                   && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        // ---------------------------------------------------------------------------------------
        // Writing
        // ---------------------------------------------------------------------------------------

        /// <summary>Where a template will be written, so a caller can say whether it is about to overwrite one.</summary>
        internal static string PathFor(string name)
        {
            return Path.Combine(FolderPath, Sanitise(name) + Extension);
        }

        internal static bool Exists(string name)
        {
            return UIGuard.Try("Template.Exists", () => File.Exists(PathFor(name)), false, null);
        }

        /// <summary>
        /// A file name that is safe on every platform RimWorld runs on.
        ///
        /// The displayed name is kept in the file as well, so a character called "Mei's clone (2)" keeps its
        /// punctuation on screen even though the file it lives in cannot.
        /// </summary>
        internal static string Sanitise(string name)
        {
            if (name.NullOrEmpty())
                return "character";

            StringBuilder safe = new StringBuilder();

            char[] bad = Path.GetInvalidFileNameChars();

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];

                bool ok = true;

                for (int b = 0; b < bad.Length; b++)
                {
                    if (bad[b] != c)
                        continue;

                    ok = false;

                    break;
                }

                safe.Append(ok ? c : '_');
            }

            string result = safe.ToString().Trim();

            return result.NullOrEmpty() ? "character" : result;
        }

        internal static bool Save(CharacterTemplate template)
        {
            if (template == null)
                return false;

            string path = PathFor(template.Name);

            try
            {
                Directory.CreateDirectory(FolderPath);

                XmlWriterSettings settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    Encoding = new UTF8Encoding(false)
                };

                using (XmlWriter writer = XmlWriter.Create(path, settings))
                {
                    writer.WriteStartDocument();

                    Comment(writer);

                    writer.WriteStartElement(RootElement);

                    Element(writer, "name", template.Name);
                    Element(writer, "savedFrom", template.SavedFrom);
                    Element(writer, "savedAt", template.SavedAt);

                    Element(writer, "first", template.First);
                    Element(writer, "nick", template.Nick);
                    Element(writer, "last", template.Last);
                    Element(writer, "gender", template.Gender);

                    if (template.BiologicalYears >= 0)
                        Element(writer, "biologicalYears",
                            template.BiologicalYears.ToString(CultureInfo.InvariantCulture));

                    if (template.ChronologicalYears >= 0)
                        Element(writer, "chronologicalYears",
                            template.ChronologicalYears.ToString(CultureInfo.InvariantCulture));

                    Element(writer, "childhood", template.Childhood);
                    Element(writer, "adulthood", template.Adulthood);

                    Element(writer, "bodyType", template.BodyType);
                    Element(writer, "headType", template.HeadType);
                    Element(writer, "hair", template.Hair);
                    Element(writer, "beard", template.Beard);
                    Element(writer, "faceTattoo", template.FaceTattoo);
                    Element(writer, "bodyTattoo", template.BodyTattoo);
                    Element(writer, "skinColor", template.SkinColor);
                    Element(writer, "hairColor", template.HairColor);
                    Element(writer, "xenotype", template.Xenotype);

                    if (template.Traits.Count > 0)
                    {
                        writer.WriteStartElement("traits");

                        for (int i = 0; i < template.Traits.Count; i++)
                        {
                            writer.WriteStartElement("li");

                            Element(writer, "def", template.Traits[i].DefName);
                            Element(writer, "degree",
                                template.Traits[i].Degree.ToString(CultureInfo.InvariantCulture));

                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                    }

                    if (template.Skills.Count > 0)
                    {
                        writer.WriteStartElement("skills");

                        for (int i = 0; i < template.Skills.Count; i++)
                        {
                            writer.WriteStartElement("li");

                            Element(writer, "def", template.Skills[i].DefName);
                            Element(writer, "level",
                                template.Skills[i].Level.ToString(CultureInfo.InvariantCulture));
                            Element(writer, "passion", template.Skills[i].Passion);

                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                    }

                    if (template.Genes.Count > 0)
                    {
                        writer.WriteStartElement("genes");

                        for (int i = 0; i < template.Genes.Count; i++)
                        {
                            writer.WriteStartElement("li");

                            Element(writer, "def", template.Genes[i].DefName);
                            Element(writer, "xenogene", template.Genes[i].Xenogene ? "true" : "false");

                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                    }

                    if (template.Apparel.Count > 0)
                    {
                        writer.WriteStartElement("apparel");

                        for (int i = 0; i < template.Apparel.Count; i++)
                            WriteThing(writer, "li", template.Apparel[i]);

                        writer.WriteEndElement();
                    }

                    if (template.Weapon != null)
                        WriteThing(writer, "weapon", template.Weapon);

                    if (template.Health.Count > 0)
                    {
                        writer.WriteStartElement("health");

                        for (int i = 0; i < template.Health.Count; i++)
                        {
                            TemplateHediff entry = template.Health[i];

                            writer.WriteStartElement("li");

                            Element(writer, "def", entry.DefName);
                            Element(writer, "part", entry.PartDef);

                            if (entry.PartIndex >= 0)
                                Element(writer, "partIndex",
                                    entry.PartIndex.ToString(CultureInfo.InvariantCulture));

                            if (entry.Severity > 0f)
                                Element(writer, "severity",
                                    entry.Severity.ToString("0.####", CultureInfo.InvariantCulture));

                            if (entry.Permanent)
                                Element(writer, "permanent", "true");

                            Element(writer, "order", entry.Order.ToString(CultureInfo.InvariantCulture));

                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(UILogTag.Prefix + "Could not save the character to " + path + ".\n" + ex);

                return false;
            }
        }

        private static void WriteThing(XmlWriter writer, string element, TemplateThing thing)
        {
            if (thing == null || thing.DefName.NullOrEmpty())
                return;

            writer.WriteStartElement(element);

            Element(writer, "def", thing.DefName);
            Element(writer, "stuff", thing.Stuff);
            Element(writer, "quality", thing.Quality);

            writer.WriteEndElement();
        }

        private static void Element(XmlWriter writer, string name, string value)
        {
            if (value.NullOrEmpty())
                return;

            writer.WriteElementString(name, value);
        }

        private static void Comment(XmlWriter writer)
        {
            writer.WriteComment(
                " A saved character for Gideon's UI Overhaul. Written by the character editor; safe to hand-edit,"
                + "\n     and safe to copy to another machine or send to somebody else."
                + "\n\n     Everything here is a defName, so a character written with one mod list and read with"
                + "\n     another applies whatever it can find and skips the rest, reporting how many it skipped."
                + "\n\n     <health>      the durable half of a body: implants and prosthetics, missing parts,"
                + "\n                   permanent injuries, and chronic conditions. Nothing transient -- no fresh"
                + "\n                   wounds, diseases, blood loss or addictions. <part> plus <partIndex> names"
                + "\n                   a body part, since both arms are called Arm and only their position tells"
                + "\n                   them apart. <order> is the write order: 0 missing parts, 1 implants,"
                + "\n                   2 everything else, and it is re-sorted on read so hand-editing is safe."
                + "\n\n     WHAT IS DELIBERATELY NOT HERE: needs, thoughts and relationships. The first two expire"
                + "\n     on their own and the third points at pawns that do not exist in another save. A template"
                + "\n     describes a person, not a situation. ");
        }

        internal static bool Delete(CharacterTemplate template)
        {
            if (template == null)
                return false;

            return UIGuard.Try("Template.Delete", () =>
            {
                string path = PathFor(template.Name);

                if (!File.Exists(path))
                    return false;

                File.Delete(path);

                return true;
            }, false, "That saved character could not be deleted.");
        }
    }
}
