using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Stages;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Logs every definition as it is built, with the full path of the XML file it came from.
    ///
    /// <b>This is the one place both facts exist at once.</b> A <c>Def</c> does not carry the file it was read
    /// from -- it knows its <c>ModContentPack</c> and nothing finer -- and the unified XML document the defs are
    /// parsed out of has had every file's contents merged into it, so by then the boundaries are gone. What
    /// survives is <c>LoadedModManager</c>'s <c>assetlookup</c>, a map from each top-level node back to the
    /// <c>LoadableXmlAsset</c> it arrived in, and it is handed straight to the method that turns a node into a
    /// def. Patching that method means the def and its source arrive together, already paired by vanilla, with
    /// nothing inferred.
    ///
    /// <b>Both deserializers are patched.</b> RimWorld picks between <c>DirectXmlToObjectNew.DefFromNodeNew</c>
    /// and the older <c>DirectXmlLoader.DefFromNode</c> on a command line switch, so patching only the modern one
    /// would leave anybody running <c>legacy-xml-deserializer</c> with a console full of definitions and no paths.
    /// They take the same two arguments and return the same thing, so one postfix serves both.
    ///
    /// <b>Paths are cached per file, and that is the difference between this being cheap and being a problem.</b>
    /// <c>LoadableXmlAsset.FullFilePath</c> composes a new string every time it is read, and this runs once per
    /// definition -- tens of thousands of times on a large mod list, for what is really a few thousand distinct
    /// files. Caching by asset means one string per file, shared by every definition in it, so the log holds
    /// references rather than copies.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_DefSources
    {
        /// <summary>
        /// One path string per source file.
        ///
        /// A <c>HybridDictionary</c> rather than a plain <c>Dictionary</c>: it stays a small list while there are
        /// few entries and promotes itself to a hash table once there are many, which suits a map that is empty
        /// on a vanilla install and holds thousands on a heavy one. Keyed by the asset object, which has no
        /// equality of its own, so this is reference identity -- exactly right, since two assets are the same
        /// file only if they are the same instance.
        /// </summary>
        private static readonly HybridDictionary Paths = new HybridDictionary();

        private static readonly object Lock = new object();

        /// <summary>
        /// Both methods that build a def from a node, where either exists.
        ///
        /// Missing ones are skipped rather than throwing. These are internal details of a game that is free to
        /// rename or retire either deserializer, and the right failure for that is a console without paths, not
        /// a patch class that fails to apply.
        /// </summary>
        public static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase modern = AccessTools.Method(typeof(DirectXmlToObjectNew), "DefFromNodeNew",
                new[] { typeof(System.Xml.XmlNode), typeof(LoadableXmlAsset) });

            if (modern != null)
                yield return modern;

            MethodBase legacy = AccessTools.Method(typeof(DirectXmlLoader), "DefFromNode",
                new[] { typeof(System.Xml.XmlNode), typeof(LoadableXmlAsset) });

            if (legacy != null)
                yield return legacy;
        }

        /// <summary>
        /// The file being parsed right now, for anything that logs a problem while it is.
        ///
        /// <b>This is how an error gets a file path.</b> A parse failure is raised from deep inside the
        /// deserializer with nothing in the message identifying the file -- "could not find type", "unrecognised
        /// field" and the rest name a field and a type and stop there. The one thing that knows the file is the
        /// call that is on the stack, so the file is published while that call runs and read by whoever logs.
        ///
        /// Per thread, because def parsing happens on the loading thread while the rest of the game logs from the
        /// main one, and an error over there has nothing to do with whatever is being parsed here.
        ///
        /// Cleared in a finalizer rather than a postfix: a deserializer that throws would otherwise leave this
        /// pointing at a file that stopped being parsed, and every later error in the load would be blamed on it.
        /// </summary>
        [ThreadStatic] private static string currentPath;

        /// <summary>The file currently being parsed on this thread, or null.</summary>
        public static string CurrentPath => currentPath;

        /// <summary>
        /// Publishes the file about to be parsed.
        ///
        /// <b>Written out rather than going through <c>UIGuard.Try</c>, and that is the only reason this looks
        /// unlike the rest of the patches.</b> A lambda would allocate a closure on every definition, which at
        /// this call count is real garbage on the loading thread. A try block that never throws costs nothing.
        /// </summary>
        public static void Prefix(LoadableXmlAsset loadingAsset)
        {
            try
            {
                currentPath = UILoadingLog.Active ? PathOf(loadingAsset) : null;
            }
            catch
            {
                currentPath = null;
            }
        }

        public static void Postfix(Def __result, LoadableXmlAsset loadingAsset)
        {
            try
            {
                if (__result != null && UILoadingLog.Active)
                    UILoadingLog.RecordDef(__result.defName, PathOf(loadingAsset));
            }
            catch (Exception ex)
            {
                UIGuard.Report("LoadingScreen.DefSource", ex,
                    "The loading console lists definitions without the file they came from.");
            }
        }

        /// <summary>
        /// Stops publishing the file, whether the parse succeeded or threw.
        ///
        /// A finalizer rather than a postfix so it also runs on the throwing path.
        ///
        /// <b>Returning void is load bearing.</b> A finalizer declared to return <c>Exception</c> <i>replaces</i>
        /// the exception with whatever it returns, and returning null there means suppressing it -- so the
        /// obvious-looking <c>return null</c> would silently swallow every deserializer failure in the game. A
        /// void finalizer leaves the exception exactly as it was, which is the only acceptable behavior for a
        /// method whose entire job is clearing a field.
        /// </summary>
        public static void Finalizer()
        {
            currentPath = null;
        }

        /// <summary>
        /// The cached full path for an asset, or null when there is not one to give.
        ///
        /// A null asset is normal rather than a fault: a def built from a string, or generated rather than read,
        /// has no file behind it and the console shows it without a path.
        /// </summary>
        private static string PathOf(LoadableXmlAsset asset)
        {
            if (asset == null)
                return null;

            lock (Lock)
            {
                object cached = Paths[asset];

                if (cached != null)
                    return (string) cached;

                // FullFilePath is a property that concatenates on every read, so this is the one call per file
                // the whole feature makes.
                string path = asset.FullFilePath;

                Paths[asset] = path ?? string.Empty;

                return path;
            }
        }

        /// <summary>
        /// Which file asked for a def that may not exist, by the name it asked for.
        ///
        /// <b>Filled during parsing and read a phase later, which is the whole trick.</b> A cross-reference
        /// failure is not reported when the reference is written; it is reported when
        /// <c>ResolveAllWantedCrossReferences</c> runs, long after every file has been closed and on several
        /// threads at once. There is no ambient file to read by then. But the request was <i>registered</i>
        /// during parsing, when the file was known, so the answer is recorded at that moment and looked up when
        /// the failure finally arrives.
        ///
        /// First writer wins. Several files can want the same missing def, and the first one to ask is a real
        /// answer where a list of nine would be noise on a single log line.
        /// </summary>
        private static readonly Dictionary<string, string> WantedBy = new Dictionary<string, string>();

        /// <summary>Records that the file being parsed wants <paramref name="targetDefName"/>.</summary>
        public static void NoteWantedDef(string targetDefName)
        {
            if (targetDefName.NullOrEmpty() || currentPath.NullOrEmpty())
                return;

            lock (Lock)
            {
                if (!WantedBy.ContainsKey(targetDefName))
                    WantedBy[targetDefName] = currentPath;
            }
        }

        /// <summary>The file that first asked for this def name, or null.</summary>
        public static string WanterOf(string targetDefName)
        {
            if (targetDefName.NullOrEmpty())
                return null;

            lock (Lock)
            {
                string path;

                return WantedBy.TryGetValue(targetDefName, out path) ? path : null;
            }
        }

        /// <summary>
        /// Drops the path cache.
        ///
        /// Called when the log is released, since these strings exist only to be pointed at by its entries. The
        /// assets themselves are vanilla's and are dropped when it is finished with them; holding a dictionary
        /// keyed by them for the rest of the session would keep every one of them alive.
        /// </summary>
        public static void ClearCache()
        {
            lock (Lock)
            {
                Paths.Clear();
                WantedBy.Clear();
            }
        }
    }
}
