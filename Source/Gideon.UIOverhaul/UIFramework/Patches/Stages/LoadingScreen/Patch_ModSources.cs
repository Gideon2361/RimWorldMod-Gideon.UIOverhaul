using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Stages;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Works out where a message came from when the message itself does not say.
    ///
    /// <b>Two families of message, two different answers.</b> Neither can be handled the way def parse errors
    /// are -- by publishing the file while it is open -- because neither is raised while anything is open.
    ///
    /// <list type="bullet">
    /// <item><b>Patch XML that is not a patch.</b> <c>ModContentPack.LoadPatches</c> reports
    /// <c>Unexpected document element in patch XML; got X, expected 'Patch'</c> and does not name the file, even
    /// though the asset in its own loop knows the path. The method is private and the asset is a loop local, so
    /// there is nothing to read from outside it. What can be done instead: know which mod is being loaded, and
    /// go and find the file in it whose root element is the one named.</item>
    /// <item><b>A type with an asset in a static field.</b>
    /// <c>Type X probably needs a StaticConstructorOnStartup attribute</c> names a type and nothing else. That
    /// one is answerable exactly rather than deduced: resolve the type, take its assembly, and look up which mod
    /// loaded it.</item>
    /// </list>
    /// </summary>
    public static class Patch_ModSources
    {
        /// <summary>The mod whose patches are being read on this thread, or null.</summary>
        [ThreadStatic] private static ModContentPack loadingPatchesFor;

        /// <summary>
        /// Publishes which mod is having its patches read.
        ///
        /// <b>A prefix and a finalizer around the whole method rather than anything cleverer.</b> The failing
        /// asset cannot be reached from out here, so this does not try; it establishes the one fact that makes
        /// the file findable afterwards. Cleared in a finalizer so a mod whose patches throw does not leave every
        /// later message attributed to it.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_LoadPatches
        {
            [HarmonyTargetMethod]
            public static MethodBase Target()
            {
                // Private, so found by name. Missing rather than renamed is handled the same way: no patch, and
                // patch-file messages simply keep arriving without a path.
                return AccessTools.Method(typeof(ModContentPack), "LoadPatches");
            }

            [HarmonyPrefix]
            public static void Opening(ModContentPack __instance)
            {
                loadingPatchesFor = UILoadingLog.Active ? __instance : null;
            }

            /// <summary>Returning void, so an exception from the original passes through untouched.</summary>
            [HarmonyFinalizer]
            public static void Closing()
            {
                loadingPatchesFor = null;
            }
        }

        /// <summary>Vanilla's exact wording, which is what makes reading the element name out of it safe.</summary>
        private const string PatchPrefix = "Unexpected document element in patch XML; got ";

        private const string PatchSuffix = ", expected 'Patch'";

        /// <summary>
        /// The patch file whose root element is the one the message complained about.
        ///
        /// <b>Found by looking, because the message and the mod are all there is.</b> Only runs on this one
        /// message, so reading the first element of each of a mod's patch files costs nothing that matters.
        ///
        /// <b>Silent when the answer is not unique.</b> Two files with the same wrong root element means two
        /// candidates and no way to tell which was reported; naming one of them would send somebody to edit a
        /// file that was fine. A missing path is a smaller problem than a wrong one.
        /// </summary>
        public static string PatchFileFor(string text)
        {
            if (text == null || !text.StartsWith(PatchPrefix, StringComparison.Ordinal))
                return null;

            ModContentPack mod = loadingPatchesFor;

            if (mod == null)
                return null;

            int end = text.IndexOf(PatchSuffix, StringComparison.Ordinal);

            if (end <= PatchPrefix.Length)
                return null;

            string element = text.Substring(PatchPrefix.Length, end - PatchPrefix.Length);

            if (element.NullOrEmpty())
                return null;

            return UIGuard.Try("LoadingScreen.FindPatchFile", () => Search(mod, element), null, null);
        }

        /// <summary>Answers already worked out, since one bad patch file reports once per mod that reads it.</summary>
        private static readonly Dictionary<string, string> patchFiles =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The patch file whose root element is <paramref name="element"/>.
        ///
        /// <b>Every running mod is searched, not only the one being loaded.</b> Knowing which mod is inside
        /// <c>LoadPatches</c> was the original plan and it is not dependable: that method is called lazily from
        /// the <c>Patches</c> property, so whether our prefix is applied before the first mod asks depends on
        /// load order rather than on anything we control. Searching everything removes the question. It also
        /// costs nothing in the normal case, because this whole path only runs on a message that has already
        /// gone wrong.
        ///
        /// The mod being loaded is still used, as a place to look first.
        /// </summary>
        private static string Search(ModContentPack mod, string element)
        {
            string answer;

            if (patchFiles.TryGetValue(element, out answer))
                return answer;

            string found = null;
            int matches = 0;

            if (mod != null)
                Scan(mod, element, ref found, ref matches);

            if (matches == 0)
            {
                foreach (ModContentPack other in LoadedModManager.RunningMods)
                {
                    if (other != null && !ReferenceEquals(other, mod))
                        Scan(other, element, ref found, ref matches);
                }
            }

            // Silent when the answer is not unique. Two files with the same wrong root element means no way to
            // tell which was reported, and naming one would send somebody to edit a file that was fine.
            answer = matches == 1 ? found : null;

            patchFiles[element] = answer;

            return answer;
        }

        private static void Scan(ModContentPack mod, string element, ref string found, ref int matches)
        {
            string root = mod.RootDir;

            if (root.NullOrEmpty() || !Directory.Exists(root))
                return;

            // Every Patches folder in the mod, since a mod can carry one per game version as well as a shared
            // one, and LoadPatches reads all of them.
            foreach (string folder in Directory.GetDirectories(root, "Patches", SearchOption.AllDirectories))
            {
                foreach (string file in Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories))
                {
                    // Case insensitively, which XML itself would not be. The message reports the element's real
                    // name so an exact compare should hold, and a diagnostic that misses its file over a capital
                    // letter is worse than one that is slightly generous.
                    if (!string.Equals(RootElementOf(file), element, StringComparison.OrdinalIgnoreCase))
                        continue;

                    matches++;

                    if (matches > 1)
                        return;

                    found = file;
                }
            }
        }

        /// <summary>
        /// The name of a file's first element, without parsing the rest of it.
        ///
        /// Streamed rather than loaded, because this runs over every patch file a mod has and some of them are
        /// large. Anything unreadable answers null and is skipped.
        /// </summary>
        private static string RootElementOf(string path)
        {
            try
            {
                using (XmlReader reader = XmlReader.Create(path, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                }))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                            return reader.Name;
                    }
                }
            }
            catch
            {
                // A patch file that cannot be opened or parsed is not the one being looked for, or is a second
                // problem that this method is not the place to report.
            }

            return null;
        }

        /// <summary>Vanilla's wording, from <c>StaticConstructorOnStartupUtility</c>.</summary>
        private const string TypePrefix = "Type ";

        private const string TypeMarker = " probably needs a StaticConstructorOnStartup attribute";

        /// <summary>
        /// The assembly of the type a static-constructor warning names, which identifies the mod that shipped it.
        ///
        /// <b>Exact rather than deduced.</b> The message names a type; a type belongs to exactly one assembly and
        /// an assembly to at most one mod, so there is no guessing anywhere in this. Returning the assembly's own
        /// path means the answer renders as a path like every other attribution, with the mod's folder in it, and
        /// needs nothing new from the console.
        /// </summary>
        public static string ModAssemblyFor(string text)
        {
            if (text == null || !text.StartsWith(TypePrefix, StringComparison.Ordinal))
                return null;

            int end = text.IndexOf(TypeMarker, StringComparison.Ordinal);

            if (end <= TypePrefix.Length)
                return null;

            string name = text.Substring(TypePrefix.Length, end - TypePrefix.Length).Trim();

            if (name.NullOrEmpty())
                return null;

            return UIGuard.Try("LoadingScreen.FindTypeMod", () => AssemblyPathFor(name), null, null);
        }

        /// <summary>Answers already worked out. These warnings arrive in bunches from the same assembly.</summary>
        private static readonly Dictionary<string, string> typeOwners =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static Dictionary<Assembly, string> assemblyOwners;

        /// <summary>
        /// Which mod shipped the type with this <i>short</i> name.
        ///
        /// <b>Matched on <c>Type.Name</c> by walking every loaded type, and the short name is the whole reason
        /// this cannot go through <c>GenTypes.GetTypeInAnyAssembly</c>.</b> The warning is built from
        /// <c>t.Name</c>, so it never carries a namespace; that lookup ends at
        /// <c>Assembly.GetType(typeName)</c>, which requires a namespace-qualified name and only ever prefixes
        /// RimWorld's own namespaces. A mod type therefore resolves to null and no attribution appears -- which
        /// is exactly what the first version of this did.
        ///
        /// <b>Over <c>GenTypes.AllTypes</c>, which RimWorld has already built,</b> rather than calling
        /// <c>GetTypes()</c> on every mod assembly ourselves. The list exists; walking it is string comparisons
        /// and nothing else.
        ///
        /// <b>Ambiguity answers null.</b> Two mods can each ship a <c>Patch_Something</c>, and there is nothing
        /// in the message to tell them apart. Naming one of them would be a coin toss presented as a fact.
        /// </summary>
        private static string AssemblyPathFor(string typeName)
        {
            string answer;

            if (typeOwners.TryGetValue(typeName, out answer))
                return answer;

            if (assemblyOwners == null)
                assemblyOwners = BuildAssemblyOwners();

            string found = null;
            int matches = 0;

            foreach (Type type in GenTypes.AllTypes)
            {
                if (type == null || !string.Equals(type.Name, typeName, StringComparison.Ordinal))
                    continue;

                string owner;

                // A type from the game itself or from Harmony has no owning mod, and is skipped rather than
                // blamed on whichever mod happened to be checked last.
                if (type.Assembly == null || !assemblyOwners.TryGetValue(type.Assembly, out owner))
                    continue;

                if (matches > 0 && !string.Equals(found, owner, StringComparison.OrdinalIgnoreCase))
                {
                    found = null;
                    matches = 2;

                    break;
                }

                found = owner;
                matches = 1;
            }

            answer = matches == 1 ? found : null;
            typeOwners[typeName] = answer;

            return answer;
        }

        private static Dictionary<Assembly, string> BuildAssemblyOwners()
        {
            Dictionary<Assembly, string> owners = new Dictionary<Assembly, string>();

            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod == null || mod.assemblies == null || mod.assemblies.loadedAssemblies == null)
                    continue;

                foreach (Assembly loaded in mod.assemblies.loadedAssemblies)
                {
                    // The mod's folder, not the DLL: the folder is what somebody recognises and what they would
                    // go looking in, and an Assemblies subfolder adds nothing to that.
                    if (loaded != null && !owners.ContainsKey(loaded))
                        owners.Add(loaded, mod.RootDir);
                }
            }

            return owners;
        }

        /// <summary>
        /// Everything this class can work out about one message, in the order the answers are trustworthy.
        ///
        /// The exact one first, then the deduced one. Both are cheap to ask and both answer null immediately for
        /// a message that is not theirs, since each tests a literal prefix before doing anything.
        /// </summary>
        public static string PathFor(string text)
        {
            return ModAssemblyFor(text) ?? PatchFileFor(text);
        }
    }
}
