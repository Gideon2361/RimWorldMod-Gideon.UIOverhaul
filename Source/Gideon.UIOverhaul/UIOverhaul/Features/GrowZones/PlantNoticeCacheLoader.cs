using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    /// <summary>
    /// Builds the notice tables at the end of def loading rather than on first use, so a malformed
    /// file is reported at startup alongside every other mod's errors. Kept separate from
    /// <see cref="PlantNoticeCacheLoader"/> so that class's static constructor is not re-entered
    /// while EnsureTables calls back into it.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PlantNoticeStartup
    {
        static PlantNoticeStartup()
        {
            PlantNotices.EnsureTables();
        }
    }

    /// <summary>
    /// Reads plant-notice rows from disk. Every row goes through here, ours included: this mod ships
    /// its own table at
    ///     &lt;Gideon.UIOverhaul&gt;/Mods/gideon.uioverhaul/PlantNotices.xml
    /// in exactly the format documented for other mods.
    ///
    /// Two reasons for that. It keeps the data out of RimWorld's def loader entirely, so there is no
    /// custom def type to resolve and nothing to fail at def-load time. And it means the path other
    /// authors are asked to use is the same one we rely on ourselves, so it cannot quietly rot. It is
    /// also the same convention the loading screen uses -- the nested Mods/&lt;packageId&gt; folder names
    /// the mod the data is being handed to.
    ///
    /// The Growing Zones Plus location is still read. Third-party mods already ship
    /// Mods/babylettuce.growingzone/GZP_PlantHazardCache.xml for it, and silently ignoring those files
    /// after absorbing that mod would break integrations their authors have no reason to expect to
    /// break.
    /// </summary>
    public static class PlantNoticeCacheLoader
    {
        public const string OwnPackageId = "gideon.uioverhaul";
        public const string IntegrationFolder = "gideon.uioverhaul";
        public const string IntegrationFile = "PlantNotices.xml";

        /// <summary>
        /// Where Growing Zones Plus asked for this data before the feature moved here. Read after the
        /// current location, so a mod shipping both has its newer file win.
        /// </summary>
        public const string LegacyIntegrationFolder = "babylettuce.growingzone";

        public const string LegacyIntegrationFile = "GZP_PlantHazardCache.xml";

        /// <summary>
        /// Every row on disk, our own first so a contributing mod always overrides us regardless of
        /// mod load order. A bad file is reported and skipped; it never takes down the rest of the
        /// table or the game.
        /// </summary>
        public static List<PlantNoticeRow> LoadAllRows()
        {
            List<PlantNoticeRow> rows = new List<PlantNoticeRow>();
            int contributingMods = 0;

            foreach (ModContentPack mod in OwnFirst(LoadedModManager.RunningModsListForReading))
            {
                bool contributed = false;

                // Legacy first, so a mod shipping both files has its current one applied last and win.
                foreach (string path in CandidatePaths(mod))
                {
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        int before = rows.Count;
                        ReadFile(path, mod.Name, rows);
                        int added = rows.Count - before;

                        if (!IsOwn(mod))
                        {
                            contributed = true;
                            Log.Message($"[Gideon.UIOverhaul] Loaded {added} plant notice "
                                        + $"entr{(added == 1 ? "y" : "ies")} from '{mod.Name}'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Gideon.UIOverhaul] Could not read the plant notice file supplied by "
                                  + $"'{mod.Name}' at {path}. It was skipped.\n{ex}");
                    }
                }

                if (contributed)
                    contributingMods++;
            }

            if (contributingMods > 0)
                Log.Message($"[Gideon.UIOverhaul] {contributingMods} mod(s) contributed plant notice entries.");

            return rows;
        }

        /// <summary>
        /// Both places a mod may have put its notice table, oldest convention first so the newer file
        /// is applied last and takes precedence.
        /// </summary>
        private static IEnumerable<string> CandidatePaths(ModContentPack mod)
        {
            if (mod?.RootDir == null)
                yield break;

            yield return Path.Combine(mod.RootDir, "Mods", LegacyIntegrationFolder, LegacyIntegrationFile);
            yield return Path.Combine(mod.RootDir, "Mods", IntegrationFolder, IntegrationFile);
        }

        private static bool IsOwn(ModContentPack mod)
        {
            return mod.PackageId != null
                   && mod.PackageId.Equals(OwnPackageId, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<ModContentPack> OwnFirst(List<ModContentPack> mods)
        {
            foreach (ModContentPack mod in mods)
                if (IsOwn(mod))
                    yield return mod;

            foreach (ModContentPack mod in mods)
                if (!IsOwn(mod))
                    yield return mod;
        }

        private static void ReadFile(string path, string sourceName, List<PlantNoticeRow> rows)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(path);

            XmlNode root = doc.DocumentElement;
            if (root == null)
                throw new InvalidDataException("The file has no root element.");

            XmlNodeList entries = root.SelectNodes("entry");
            if (entries == null)
                return;

            int index = 0;
            foreach (XmlNode entry in entries)
            {
                index++;
                PlantNoticeRow row = ReadEntry(entry, path, index, sourceName);
                if (row != null)
                    rows.Add(row);
            }
        }

        private static PlantNoticeRow ReadEntry(XmlNode entry, string path, int index, string sourceName)
        {
            PlantNoticeRow row = new PlantNoticeRow
            {
                plant = Text(entry, "plant"),
                thingClass = Text(entry, "thingClass"),
                compClass = Text(entry, "compClass"),
                harvestedThing = Text(entry, "harvestedThing"),
                cardLabel = Text(entry, "cardLabel"),
                detail = Text(entry, "detail"),
                lightDetail = Text(entry, "lightDetail"),
                source = sourceName
            };

            if (row.plant.NullOrEmpty() && row.thingClass.NullOrEmpty()
                && row.compClass.NullOrEmpty() && row.harvestedThing.NullOrEmpty())
            {
                Log.Error($"[Gideon.UIOverhaul] Entry {index} in {path} sets no match key "
                          + "(plant, thingClass, compClass or harvestedThing). Skipped.");
                return null;
            }

            string kind = Text(entry, "kind");
            if (kind.NullOrEmpty())
            {
                Log.Error($"[Gideon.UIOverhaul] Entry {index} in {path} has no <kind>. Skipped.");
                return null;
            }

            if (!Enum.TryParse(kind, true, out PlantNoticeKind parsed))
            {
                Log.Error($"[Gideon.UIOverhaul] Entry {index} in {path} has an unrecognized kind "
                          + $"'{kind}'. Valid values are CreatesHazard, RequiresHazard, "
                          + "PossibleHazard, CreatesBenefit and None. Skipped.");
                return null;
            }

            row.kind = parsed;

            // Optional, and unlike kind a bad value only costs the override -- the notice itself is
            // still worth keeping, so this warns rather than dropping the row.
            string light = Text(entry, "light");
            if (!light.NullOrEmpty())
            {
                if (Enum.TryParse(light, true, out PlantLightBehaviour parsedLight))
                {
                    row.light = parsedLight;
                }
                else
                {
                    Log.Warning($"[Gideon.UIOverhaul] Entry {index} in {path} has an unrecognized "
                                + $"light '{light}'. Valid values are Deadly, Any and Normal. "
                                + "Ignoring the light override; the rest of the entry was kept.");
                }
            }

            return row;
        }

        private static string Text(XmlNode parent, string childName)
        {
            XmlNode child = parent[childName];
            return child?.InnerText?.Trim();
        }
    }
}
