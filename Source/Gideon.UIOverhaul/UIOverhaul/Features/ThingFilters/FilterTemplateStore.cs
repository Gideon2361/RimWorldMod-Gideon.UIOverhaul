using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using System.IO;
using System.Xml;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ThingFilters
{
    /// <summary>
    /// Every saved filter template, in one file beside the mod's settings.
    ///
    /// <b>The same arrangement the bill templates use,</b> for the same reason Aaron gave when those were built:
    /// a template kept inside a save dies with the colony, and the whole point of saving one is the next colony.
    /// One file rather than one per template, in RimWorld's config folder, so it survives the mod being updated.
    ///
    /// <b>A name clash renames rather than overwrites.</b> Losing a template somebody made is the one outcome
    /// saving must never produce.
    ///
    /// <b>Nothing here throws.</b> A file that cannot be read yields an empty list: a broken template file is not
    /// a reason to lose the storage tab.
    /// </summary>
    internal static class FilterTemplateStore
    {
        internal const string FileName = "UIOverhaul_FilterTemplates.xml";

        private const string Root = "FilterTemplates";
        private const string Element = "Template";

        private static List<FilterTemplate> loaded;

        internal static string FilePath =>
            UIGuard.Try("Filters.Templates.Path",
                () => Path.Combine(GenFilePaths.ConfigFolderPath, FileName), FileName, null);

        internal static List<FilterTemplate> All
        {
            get
            {
                if (loaded == null)
                    loaded = Read(FilePath);

                return loaded;
            }
        }

        /// <summary>Drops the cache so the next read comes from disk.</summary>
        internal static void Reload()
        {
            loaded = null;
        }

        /// <summary>Stores a template, giving it a free name if the one it arrived with is taken.</summary>
        internal static void Add(FilterTemplate template)
        {
            if (template == null)
                return;

            template.Name = FreeName(All, template.Name);

            All.Add(template);

            Save();
        }

        internal static void Delete(FilterTemplate template)
        {
            if (template == null || !All.Remove(template))
                return;

            Save();
        }

        internal static void Save()
        {
            UIGuard.Try("Filters.Templates.Save", () => Write(FilePath, All),
                "The filter templates could not be saved, so this change will not survive a restart.");
        }

        /// <summary>
        /// A name nobody else is using, by adding a number.
        ///
        /// Blank becomes "Filter", because a nameless template is unfindable in a list and refusing the save would
        /// lose work somebody has already done.
        /// </summary>
        internal static string FreeName(List<FilterTemplate> existing, string wanted)
        {
            string name = string.IsNullOrEmpty(wanted) ? "Filter" : wanted.Trim();

            if (!Taken(existing, name))
                return name;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = name + " " + i;

                if (!Taken(existing, candidate))
                    return candidate;
            }

            return name + " " + Mathf.Abs(name.GetHashCode() % 10000);
        }

        private static bool Taken(List<FilterTemplate> existing, string name)
        {
            if (existing == null)
                return false;

            for (int i = 0; i < existing.Count; i++)
            {
                if (existing[i] != null && string.Equals(existing[i].Name, name, System.StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------
        // The file
        // ---------------------------------------------------------------------------------------

        internal static List<FilterTemplate> Read(string path)
        {
            return UIGuard.Try("Filters.Templates.Read", () => ReadFile(path), new List<FilterTemplate>(),
                "The filter templates could not be read, so none are listed.");
        }

        private static List<FilterTemplate> ReadFile(string path)
        {
            List<FilterTemplate> list = new List<FilterTemplate>();

            if (!File.Exists(path))
                return list;

            XmlDocument document = new XmlDocument();

            document.Load(path);

            XmlNode root = document.DocumentElement;

            if (root == null)
                return list;

            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element || node.Name != Element)
                    continue;

                // One malformed entry is dropped rather than failing the file, which would cost the player the
                // other twenty over somebody's hand edit.
                FilterTemplate template = UIGuard.Try("Filters.Templates.ReadOne", () => ReadOne(node), null, null);

                if (template != null && !string.IsNullOrEmpty(template.Name))
                    list.Add(template);
            }

            return list;
        }

        private static FilterTemplate ReadOne(XmlNode node)
        {
            FilterTemplate template = new FilterTemplate
            {
                Name = Text(node, "name"),
                Origin = Text(node, "origin"),
                Saved = Text(node, "saved"),
                HitPoints = new FloatRange(Number(node, "hitPointsMin", 0f), Number(node, "hitPointsMax", 1f)),
                Quality = new QualityRange(Quality(node, "qualityMin", QualityCategory.Awful),
                    Quality(node, "qualityMax", QualityCategory.Legendary))
            };

            template.Defs = Items(node, "defs");
            template.Specials = Items(node, "specials");

            return template;
        }

        internal static void Write(string path, List<FilterTemplate> list)
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

                foreach (FilterTemplate template in list ?? new List<FilterTemplate>())
                    WriteOne(writer, template);

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        private static void WriteOne(XmlWriter writer, FilterTemplate template)
        {
            if (template == null)
                return;

            writer.WriteStartElement(Element);

            Put(writer, "name", template.Name);
            Put(writer, "origin", template.Origin);
            Put(writer, "saved", template.Saved);

            Put(writer, "hitPointsMin", template.HitPoints.min.ToString(CultureInfo.InvariantCulture));
            Put(writer, "hitPointsMax", template.HitPoints.max.ToString(CultureInfo.InvariantCulture));
            Put(writer, "qualityMin", template.Quality.min.ToString());
            Put(writer, "qualityMax", template.Quality.max.ToString());

            PutList(writer, "defs", template.Defs);
            PutList(writer, "specials", template.Specials);

            writer.WriteEndElement();
        }

        private static void Put(XmlWriter writer, string name, string value)
        {
            writer.WriteElementString(name, value ?? string.Empty);
        }

        private static void PutList(XmlWriter writer, string name, List<string> values)
        {
            writer.WriteStartElement(name);

            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                    writer.WriteElementString("li", values[i] ?? string.Empty);
            }

            writer.WriteEndElement();
        }

        private static string Text(XmlNode node, string name)
        {
            XmlNode child = node?[name];

            return child == null ? string.Empty : child.InnerText;
        }

        private static float Number(XmlNode node, string name, float fallback)
        {
            float value;

            return float.TryParse(Text(node, name), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static QualityCategory Quality(XmlNode node, string name, QualityCategory fallback)
        {
            string text = Text(node, name);

            if (string.IsNullOrEmpty(text))
                return fallback;

            try
            {
                return (QualityCategory) System.Enum.Parse(typeof(QualityCategory), text, true);
            }
            catch
            {
                return fallback;
            }
        }

        private static List<string> Items(XmlNode node, string name)
        {
            List<string> values = new List<string>();
            XmlNode parent = node?[name];

            if (parent == null)
                return values;

            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element && !string.IsNullOrEmpty(child.InnerText))
                    values.Add(child.InnerText);
            }

            return values;
        }
    }
}
