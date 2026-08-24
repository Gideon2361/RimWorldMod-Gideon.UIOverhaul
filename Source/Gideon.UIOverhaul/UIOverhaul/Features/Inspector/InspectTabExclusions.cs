using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// Inspect tabs that keep their own window instead of being drawn inside the pane.
    ///
    /// <b>The pane hosts every tab, and a few tabs cannot be hosted.</b> The rebuild's rule is that no chip
    /// opens a window -- half the row rendering in place and half popping out was the original complaint -- and
    /// that rule holds for anything shaped like a panel, however large, because the pane grows to fit and
    /// scrolls past what will not. It does not hold for a tab that is not a panel at all.
    ///
    /// <b>The case that produced this, found by Aaron on 2026-08-24.</b> RimDark 40k's Framework patches
    /// <c>Core40k.ITab_RankSystem</c> onto every <c>BasePawn</c>, and that tab sets
    /// <c>size = new Vector2(UI.screenWidth, PaneTopY - 100f)</c> -- it is a full-screen rank tree that happens
    /// to be reached through the tab system. Hosting it did exactly what it asked for and grew the pane over the
    /// whole map. Nothing is wrong with either side; the two designs are simply incompatible, and the tab's own
    /// window is where it works.
    ///
    /// <b>A file rather than a list in code, which is Aaron's call and the right one.</b> There is no way to
    /// detect this from the outside: a tab that wants the screen and a tab that is genuinely large make the same
    /// request, and the difference is what the author meant. So it is a judgement per tab, kept where a
    /// judgement can be added without a build -- and shipped at the same path other mods are asked to use, so
    /// an author can exclude their own tab without going through us. Same convention as
    /// <c>PlantNoticeCacheLoader</c>, for the same reasons recorded there.
    ///
    /// <b>Excluded means the popout comes back, not that the tab disappears.</b> The chip stays in the row and
    /// stays selectable; <see cref="InspectTabStrip"/> calls <c>DoTabGUI</c> for it, which is the one line of
    /// vanilla's <c>DoTabs</c> the rebuild deliberately dropped.
    /// </summary>
    public static class InspectTabExclusions
    {
        public const string OwnPackageId = "gideon.uioverhaul";

        public const string IntegrationFolder = "gideon.uioverhaul";

        public const string IntegrationFile = "InspectTabExclusions.xml";

        /// <summary>Full type names, matched exactly.</summary>
        private static readonly HashSet<string> Tabs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Namespace prefixes, matched against the front of the type's full name.</summary>
        private static readonly List<string> Namespaces = new List<string>();

        /// <summary>Package ids, matched against the mod that owns the type's assembly.</summary>
        private static readonly HashSet<string> Mods =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The answer per tab type, because this is asked several times a frame.
        ///
        /// <c>OpenForeign</c> alone is called from the frame, the metrics and the width patch, and each of those
        /// walks the selection's whole tab list. The lookup underneath is a set probe and an assembly walk, and
        /// neither belongs in a draw.
        /// </summary>
        private static readonly Dictionary<Type, bool> Answers = new Dictionary<Type, bool>();

        private static bool loaded;

        /// <summary>Whether this tab keeps its own window.</summary>
        internal static bool Excluded(InspectTabBase tab)
        {
            return tab != null && Excluded(tab.GetType());
        }

        internal static bool Excluded(Type type)
        {
            if (type == null)
                return false;

            bool answer;

            if (Answers.TryGetValue(type, out answer))
                return answer;

            answer = UIGuard.Try("Inspector.TabExcluded", () =>
            {
                Load();

                return Matches(type);
            }, false, null);

            Answers[type] = answer;

            return answer;
        }

        private static bool Matches(Type type)
        {
            string full = type.FullName;

            if (full.NullOrEmpty())
                return false;

            if (Tabs.Contains(full))
                return true;

            for (int i = 0; i < Namespaces.Count; i++)
            {
                if (full.StartsWith(Namespaces[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return Mods.Count > 0 && Mods.Contains(OwnerOf(type));
        }

        /// <summary>
        /// The packageId of the mod whose assemblies define this type, or null.
        ///
        /// Walked rather than read off an attribute, because there is nothing on a type that names its mod.
        /// <c>ModContentPack.assemblies.loadedAssemblies</c> is the list RimWorld itself keeps of what each mod
        /// loaded, so this is the same answer the game would give. Only reached for a type nothing more specific
        /// matched, and cached by the caller either way.
        /// </summary>
        private static string OwnerOf(Type type)
        {
            Assembly assembly = type.Assembly;

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            for (int i = 0; mods != null && i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];

                List<Assembly> loaded = mod?.assemblies?.loadedAssemblies;

                for (int a = 0; loaded != null && a < loaded.Count; a++)
                {
                    if (loaded[a] == assembly)
                        return mod.PackageId;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads every loaded mod's file, ours first.
        ///
        /// Lazy rather than at startup, and deliberately: this is only ever asked once a pane is open, which is
        /// long after def loading, and a table this small does not earn a place in the loading sequence. A bad
        /// file is reported and skipped -- it never takes the rest of the table with it, and never the pane.
        /// </summary>
        private static void Load()
        {
            if (loaded)
                return;

            loaded = true;

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            if (mods == null)
                return;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    ModContentPack mod = mods[i];

                    if (mod?.RootDir == null || IsOwn(mod) != (pass == 0))
                        continue;

                    string path = Path.Combine(mod.RootDir, "Mods", IntegrationFolder, IntegrationFile);

                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        int before = Tabs.Count + Namespaces.Count + Mods.Count;

                        ReadFile(path);

                        int added = Tabs.Count + Namespaces.Count + Mods.Count - before;

                        if (!IsOwn(mod))
                            Log.Message(UILogTag.Prefix + $"Loaded {added} inspect tab "
                                        + $"exclusion{(added == 1 ? string.Empty : "s")} from '{mod.Name}'.");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(UILogTag.Prefix + "Could not read the inspect tab exclusion file supplied by '"
                                  + mod.Name + "' at " + path + ". It was skipped.\n" + ex);
                    }
                }
            }
        }

        private static bool IsOwn(ModContentPack mod)
        {
            return mod.PackageId != null
                   && mod.PackageId.Equals(OwnPackageId, StringComparison.OrdinalIgnoreCase);
        }

        private static void ReadFile(string path)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(path);

            XmlNode root = doc.DocumentElement;

            if (root == null)
                throw new InvalidDataException("The file has no root element.");

            XmlNodeList entries = root.SelectNodes("entry");

            if (entries == null)
                return;

            foreach (XmlNode entry in entries)
            {
                // Most specific key wins, and only one is read per entry: an entry naming both a tab and a mod
                // is an author who has not decided which they meant, and honouring both would silently exclude
                // far more than the line appears to say.
                string tab = Text(entry, "tab");

                if (!tab.NullOrEmpty())
                {
                    Tabs.Add(tab);
                    continue;
                }

                string space = Text(entry, "namespace");

                if (!space.NullOrEmpty())
                {
                    // Stored with the dot so "Foo" cannot also match "Foobar.ITab_X". An author who wrote the
                    // trailing dot themselves is not punished for it.
                    Namespaces.Add(space.EndsWith(".", StringComparison.Ordinal) ? space : space + ".");
                    continue;
                }

                string mod = Text(entry, "mod");

                if (!mod.NullOrEmpty())
                    Mods.Add(mod);
            }
        }

        private static string Text(XmlNode entry, string name)
        {
            XmlNode node = entry?.SelectSingleNode(name);

            return node == null ? null : node.InnerText?.Trim();
        }
    }
}
