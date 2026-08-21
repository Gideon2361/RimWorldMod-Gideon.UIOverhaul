using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Every saved bill template, held in one file beside the mod's own settings.
    ///
    /// <b>Global and outside the save, which Aaron asked for directly:</b> "Templates need to be global and stored
    /// outside the save for future re-use." A template kept in a colony dies with it, and the whole reason to save
    /// one is to use it on the next colony.
    ///
    /// <b>One file rather than one per template.</b> Exporting then hands somebody a single readable thing, and
    /// importing is a merge of one file into another rather than a directory walk. The file sits in RimWorld's
    /// config folder, so it survives the mod being updated or reinstalled.
    ///
    /// <b>A name clash on import renames rather than overwrites.</b> Losing a template the player made, in order to
    /// make room for one they were given, is the one outcome an import must never produce.
    ///
    /// <b>Reading and writing take an explicit path</b> so the file handling can be exercised without a game
    /// running. Only <see cref="FilePath"/> knows where the real one lives.
    /// </summary>
    internal static class BillTemplateStore
    {
        internal const string FileName = "UIOverhaul_BillTemplates.xml";

        private const string Root = "BillTemplates";
        private const string Element = "Template";

        private static List<BillTemplate> loaded;

        /// <summary>Beside <c>UIOverhaul_Settings.xml</c>, by the same reasoning that put that file there.</summary>
        internal static string FilePath =>
            UIGuard.Try("Bills.Templates.Path",
                () => Path.Combine(Verse.GenFilePaths.ConfigFolderPath, FileName), FileName, null);

        /// <summary>
        /// Every stored template, read from disk on first use.
        ///
        /// Never null, and never throws: a file that cannot be read yields an empty store rather than taking the
        /// window down with it, since a broken template file is not a reason to lose the bills screen.
        /// </summary>
        internal static List<BillTemplate> All
        {
            get
            {
                if (loaded == null)
                    loaded = Read(FilePath);

                return loaded;
            }
        }

        /// <summary>Drops the cache so the next read comes from disk. For the config watcher and for tests.</summary>
        internal static void Reload()
        {
            loaded = null;
        }

        /// <summary>Stores a template, giving it a free name if the one it arrived with is taken.</summary>
        internal static void Add(BillTemplate template)
        {
            if (template == null)
                return;

            template.Name = FreeName(All, template.Name);

            All.Add(template);

            Save();
        }

        internal static void Delete(BillTemplate template)
        {
            if (template == null || !All.Remove(template))
                return;

            Save();
        }

        /// <summary>
        /// Renames a template, refusing a name another one already has.
        ///
        /// Refused rather than silently suffixed, because unlike an import this is somebody typing a name on
        /// purpose and they should be told it is taken.
        /// </summary>
        internal static bool Rename(BillTemplate template, string name, out string problem)
        {
            problem = null;

            if (template == null)
                return false;

            string wanted = (name ?? string.Empty).Trim();

            if (wanted.Length == 0)
            {
                problem = "A template needs a name.";

                return false;
            }

            foreach (BillTemplate other in All)
            {
                if (other != template && string.Equals(other.Name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    problem = "Another template is already called that.";

                    return false;
                }
            }

            template.Name = wanted;

            Save();

            return true;
        }

        internal static void Save()
        {
            UIGuard.Try("Bills.Templates.Save", () => Write(FilePath, All),
                "The bill templates could not be saved, so this change will not survive a restart.");
        }

        /// <summary>
        /// Merges another file's templates into the store.
        /// </summary>
        /// <param name="added">How many arrived.</param>
        /// <param name="renamed">How many had to be renamed because the name was taken.</param>
        internal static bool Import(string path, out int added, out string renamedReport, out string problem)
        {
            int gained = 0;
            int clashed = 0;
            string failure = null;

            UIGuard.Try("Bills.Templates.Import", () =>
            {
                if (!File.Exists(path))
                {
                    failure = "There is no file at that path.";

                    return;
                }

                List<BillTemplate> incoming = Read(path);

                if (incoming.Count == 0)
                {
                    failure = "That file holds no templates.";

                    return;
                }

                foreach (BillTemplate template in incoming)
                {
                    string wanted = template.Name;

                    template.Name = FreeName(All, wanted);

                    if (!string.Equals(template.Name, wanted, StringComparison.Ordinal))
                        clashed++;

                    All.Add(template);
                    gained++;
                }

                Write(FilePath, All);
            }, "The templates could not be imported. Nothing was changed.");

            added = gained;
            renamedReport = clashed == 0
                ? null
                : clashed + (clashed == 1 ? " was renamed" : " were renamed") + " because the name was taken";
            problem = failure;

            return failure == null && gained > 0;
        }

        /// <summary>
        /// Writes every template to a file of the player's choosing.
        ///
        /// <b>Success is what the write reported, not whether a file is there afterwards.</b> Checking for the file
        /// would call a failed export successful whenever something already sat at that path, which is exactly the
        /// case where the player is about to hand somebody the wrong thing.
        /// </summary>
        internal static bool Export(string path, out string problem)
        {
            string failure = null;

            bool wrote = UIGuard.Try("Bills.Templates.Export", () =>
            {
                Write(path, All);

                return true;
            }, false, "The templates could not be written to that path.");

            if (!wrote)
                failure = "That file could not be written.";

            problem = failure;

            return wrote;
        }

        /// <summary>
        /// A name nothing in <paramref name="existing"/> already uses, suffixed only as far as it has to be.
        /// </summary>
        internal static string FreeName(List<BillTemplate> existing, string wanted)
        {
            string bare = (wanted ?? string.Empty).Trim();

            if (bare.Length == 0)
                bare = "Template";

            if (!Taken(existing, bare))
                return bare;

            for (int n = 2; n < 1000; n++)
            {
                string tried = bare + " (" + n + ")";

                if (!Taken(existing, tried))
                    return tried;
            }

            return bare + " (" + Guid.NewGuid().ToString("N").Substring(0, 6) + ")";
        }

        private static bool Taken(List<BillTemplate> existing, string name)
        {
            if (existing == null)
                return false;

            foreach (BillTemplate template in existing)
            {
                if (string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- file

        /// <summary>
        /// Reads a template file, tolerating everything it can.
        ///
        /// <b>A template that cannot be parsed is dropped rather than failing the file.</b> One malformed entry, in
        /// a file the player may have hand edited or been sent, must not cost them the other twenty.
        /// </summary>
        internal static List<BillTemplate> Read(string path)
        {
            return UIGuard.Try("Bills.Templates.Read", () => ReadFile(path), new List<BillTemplate>(),
                "The bill templates could not be read, so none are listed.");
        }

        private static List<BillTemplate> ReadFile(string path)
        {
            List<BillTemplate> list = new List<BillTemplate>();

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

                BillTemplate template = ReadOne(node);

                if (template != null && !string.IsNullOrEmpty(template.Name))
                    list.Add(template);
            }

            return list;
        }

        private static BillTemplate ReadOne(XmlNode node)
        {
            BillTemplate template = new BillTemplate
            {
                Name = Text(node, "name"),
                Kind = ReadKind(Text(node, "kind")),
                Origin = Text(node, "origin"),
                Saved = Text(node, "saved"),
                Recipe = Text(node, "recipe"),
                RepeatMode = Text(node, "repeatMode"),
                StoreMode = Text(node, "storeMode"),
                StoreZone = Text(node, "storeZone"),
                WorkerName = Text(node, "workerName"),
                RepeatCount = Int(node, "repeatCount", 1),
                TargetCount = Int(node, "targetCount", 10),
                PauseWhenSatisfied = Bool(node, "pauseWhenSatisfied", false),
                UnpauseWhenYouHave = Int(node, "unpauseWhenYouHave", 5),
                SearchRadius = Float(node, "searchRadius", 999f),
                IncludeEquipped = Bool(node, "includeEquipped", false),
                IncludeTainted = Bool(node, "includeTainted", false),
                LimitToAllowedStuff = Bool(node, "limitToAllowedStuff", false),
                SlavesOnly = Bool(node, "slavesOnly", false),
                MechsOnly = Bool(node, "mechsOnly", false),
                NonMechsOnly = Bool(node, "nonMechsOnly", false),
                HpMin = Float(node, "hpMin", 0f),
                HpMax = Float(node, "hpMax", 1f),
                QualityMin = Text(node, "qualityMin") ?? "Awful",
                QualityMax = Text(node, "qualityMax") ?? "Legendary",
                SkillMin = Int(node, "skillMin", 0),
                SkillMax = Int(node, "skillMax", 20),
                BenchDef = Text(node, "benchDef")
            };

            XmlNode allowed = node["allowed"];

            if (allowed != null)
            {
                foreach (XmlNode child in allowed.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element && !string.IsNullOrEmpty(child.InnerText))
                        template.Allowed.Add(child.InnerText);
                }
            }

            // A bench template's bills, read by the same function that read this one. Nesting is one level deep
            // and stays that way: a bench holds bills, and a bill holds nothing, so there is no recursion to
            // bound. A bench nested inside a bench would be read here and then ignored by everything downstream.
            XmlNode bills = node["bills"];

            if (bills == null)
                return template;

            foreach (XmlNode child in bills.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element || child.Name != Element)
                    continue;

                BillTemplate nested = ReadOne(child);

                if (nested != null && nested.Kind == BillTemplateKind.Bill)
                    template.Bills.Add(nested);
            }

            return template;
        }

        /// <summary>
        /// The kind an element names.
        ///
        /// <b>Anything unrecognised reads as a bill,</b> which is what a file written before the other kinds
        /// existed says by saying nothing. Parsed by name rather than by <c>Enum.Parse</c> so a hand-edited file
        /// with a typo gives a usable template rather than an exception on the way in, matching how every other
        /// name in this mod's config files is read.
        /// </summary>
        private static BillTemplateKind ReadKind(string text)
        {
            if (string.Equals(text, "Filter", StringComparison.OrdinalIgnoreCase))
                return BillTemplateKind.Filter;

            return string.Equals(text, "Bench", StringComparison.OrdinalIgnoreCase)
                ? BillTemplateKind.Bench
                : BillTemplateKind.Bill;
        }

        internal static void Write(string path, List<BillTemplate> list)
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

                foreach (BillTemplate template in list ?? new List<BillTemplate>())
                    WriteOne(writer, template);

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        private static void WriteOne(XmlWriter writer, BillTemplate template)
        {
            writer.WriteStartElement(Element);

            Put(writer, "name", template.Name);
            Put(writer, "kind", template.Kind.ToString());
            Put(writer, "origin", template.Origin);
            Put(writer, "saved", template.Saved);
            Put(writer, "recipe", template.Recipe);
            Put(writer, "repeatMode", template.RepeatMode);
            Put(writer, "storeMode", template.StoreMode);
            Put(writer, "storeZone", template.StoreZone);
            Put(writer, "workerName", template.WorkerName);

            Put(writer, "repeatCount", template.RepeatCount.ToString(CultureInfo.InvariantCulture));
            Put(writer, "targetCount", template.TargetCount.ToString(CultureInfo.InvariantCulture));
            Put(writer, "pauseWhenSatisfied", template.PauseWhenSatisfied ? "true" : "false");
            Put(writer, "unpauseWhenYouHave", template.UnpauseWhenYouHave.ToString(CultureInfo.InvariantCulture));
            Put(writer, "searchRadius", template.SearchRadius.ToString(CultureInfo.InvariantCulture));
            Put(writer, "includeEquipped", template.IncludeEquipped ? "true" : "false");
            Put(writer, "includeTainted", template.IncludeTainted ? "true" : "false");
            Put(writer, "limitToAllowedStuff", template.LimitToAllowedStuff ? "true" : "false");
            Put(writer, "slavesOnly", template.SlavesOnly ? "true" : "false");
            Put(writer, "mechsOnly", template.MechsOnly ? "true" : "false");
            Put(writer, "nonMechsOnly", template.NonMechsOnly ? "true" : "false");
            Put(writer, "hpMin", template.HpMin.ToString(CultureInfo.InvariantCulture));
            Put(writer, "hpMax", template.HpMax.ToString(CultureInfo.InvariantCulture));
            Put(writer, "qualityMin", template.QualityMin);
            Put(writer, "qualityMax", template.QualityMax);
            Put(writer, "skillMin", template.SkillMin.ToString(CultureInfo.InvariantCulture));
            Put(writer, "skillMax", template.SkillMax.ToString(CultureInfo.InvariantCulture));

            Put(writer, "benchDef", template.BenchDef);

            if (template.Allowed.Count > 0)
            {
                writer.WriteStartElement("allowed");

                foreach (string def in template.Allowed)
                    Put(writer, "li", def);

                writer.WriteEndElement();
            }

            // Written through the same function, so a nested bill and a top level one cannot end up with
            // different element names for the same field.
            if (template.Bills.Count > 0)
            {
                writer.WriteStartElement("bills");

                foreach (BillTemplate child in template.Bills)
                {
                    if (child != null)
                        WriteOne(writer, child);
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static void Put(XmlWriter writer, string name, string value)
        {
            if (value != null)
                writer.WriteElementString(name, value);
        }

        private static string Text(XmlNode node, string name)
        {
            XmlNode child = node[name];

            return child == null || string.IsNullOrEmpty(child.InnerText) ? null : child.InnerText;
        }

        private static int Int(XmlNode node, string name, int fallback)
        {
            string text = Text(node, name);

            return text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int value)
                ? value
                : fallback;
        }

        private static float Float(XmlNode node, string name, float fallback)
        {
            string text = Text(node, name);

            return text != null && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out float value)
                ? value
                : fallback;
        }

        private static bool Bool(XmlNode node, string name, bool fallback)
        {
            string text = Text(node, name);

            return text == null ? fallback : text.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
