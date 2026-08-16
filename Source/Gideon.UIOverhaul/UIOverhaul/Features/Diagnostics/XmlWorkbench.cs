using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Xml;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>Where the workbench has got to.</summary>
    internal enum XmlWorkbenchState
    {
        Empty,
        Building,
        Ready,
        Failed
    }

    /// <summary>One node an XPath matched.</summary>
    internal struct XmlMatch
    {
        /// <summary>The matched node's own XML.</summary>
        public string Xml;

        /// <summary>The def this node sits inside, by name where it has one.</summary>
        public string Owner;

        /// <summary>Full path of the file the owning def was read from.</summary>
        public string Path;

        public string Mod;
    }

    /// <summary>
    /// A rebuilt copy of the combined definition XML, for testing XPath expressions against.
    ///
    /// <b>Why this has to be rebuilt rather than kept.</b> RimWorld combines every mod's XML into one document,
    /// applies every patch to it, reads the definitions out and lets it go. It is gone long before anybody could
    /// ask a question about it, and holding on to the original would mean keeping the largest single object of
    /// the entire load alive for the whole session on the chance it is wanted.
    ///
    /// <b>Built with the game's own reader and combiner, which is what makes it trustworthy.</b>
    /// <c>DirectXmlLoader.XmlAssetsInModFolder</c> resolves a mod's folders exactly as loading does -- version
    /// folders, Common, whatever LoadFolders.xml says -- and <c>CombineIntoUnifiedXML</c> merges them in load
    /// order. Reimplementing either would produce a document that is subtly not the one patches actually run
    /// against, and a patch tester that lies is worse than no patch tester.
    ///
    /// <b><c>LoadedModManager.LoadModXML</c> is deliberately not used,</b> though it looks like the obvious call.
    /// It goes through <c>ModContentPack.LoadDefs</c>, which logs "LoadDefs called with already existing def
    /// packages" when a mod's definitions are already loaded -- which they always are by the time anybody opens
    /// this. The two methods above are pure reads and touch no game state at all.
    ///
    /// <b>Scoped, because the whole thing does not fit.</b> On a large mod list the combined document is the
    /// biggest structure the load ever builds, and building a second one on top of a running game is not a cost
    /// to impose without asking. So a scope is chosen and only those mods are read.
    ///
    /// Built on a background thread and released when the window closes.
    /// </summary>
    internal static class XmlWorkbench
    {
        private static readonly object Lock = new object();

        private static XmlWorkbenchState state = XmlWorkbenchState.Empty;
        private static string failure;
        private static string scopeName = string.Empty;
        private static int nodeCount;
        private static int fileCount;
        private static Thread worker;

        /// <summary>
        /// The built document, and where each top-level definition came from.
        ///
        /// Only ever touched on the main thread once the build reports Ready. The worker publishes both under the
        /// lock at the moment it finishes and never writes them again.
        /// </summary>
        private static XmlDocument document;

        private static Dictionary<XmlNode, LoadableXmlAsset> sources;

        internal static XmlWorkbenchState State
        {
            get
            {
                lock (Lock)
                    return state;
            }
        }

        internal static string Failure
        {
            get
            {
                lock (Lock)
                    return failure;
            }
        }

        internal static string ScopeName
        {
            get
            {
                lock (Lock)
                    return scopeName;
            }
        }

        internal static void Stats(out int nodes, out int files)
        {
            lock (Lock)
            {
                nodes = nodeCount;
                files = fileCount;
            }
        }

        /// <summary>
        /// Every mod that ships definitions, for the scope picker.
        ///
        /// Read on the caller's thread. A mod with no Defs folder is left out rather than listed and empty: it is
        /// not a thing anybody would choose deliberately, and the list is long enough already.
        /// </summary>
        internal static List<ModContentPack> Candidates()
        {
            return UIGuard.Try("Diagnostics.WorkbenchCandidates", () =>
            {
                List<ModContentPack> found = new List<ModContentPack>();
                List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

                if (mods == null)
                    return found;

                foreach (ModContentPack mod in mods)
                {
                    if (mod != null)
                        found.Add(mod);
                }

                return found;
            }, new List<ModContentPack>(), "The workbench has no mods to offer.");
        }

        /// <summary>
        /// Reads the chosen mods and combines them, off the main thread.
        ///
        /// <paramref name="scope"/> is captured before the worker starts, so the worker only ever sees the mod
        /// objects it was given rather than reaching into the running mod list itself.
        /// </summary>
        internal static void Build(List<ModContentPack> scope, string name)
        {
            if (scope == null || scope.Count == 0)
                return;

            List<ModContentPack> captured = new List<ModContentPack>(scope);

            lock (Lock)
            {
                state = XmlWorkbenchState.Building;
                failure = null;
                scopeName = name;
                nodeCount = 0;
                fileCount = 0;
                document = null;
                sources = null;
            }

            worker = new Thread(() => Run(captured))
            {
                IsBackground = true,
                Name = "Gideon.XmlWorkbench"
            };

            worker.Start();
        }

        private static void Run(List<ModContentPack> scope)
        {
            try
            {
                List<LoadableXmlAsset> assets = new List<LoadableXmlAsset>();

                foreach (ModContentPack mod in scope)
                {
                    if (worker != Thread.CurrentThread)
                        return;

                    try
                    {
                        // Vanilla's own folder resolution. Anything else would disagree with what the game
                        // actually loaded for mods using LoadFolders.xml or per-version folders.
                        LoadableXmlAsset[] found = DirectXmlLoader.XmlAssetsInModFolder(mod, "Defs/");

                        if (found != null)
                            assets.AddRange(found);
                    }
                    catch
                    {
                        // One mod that cannot be read costs that mod. A workbench missing one mod's definitions
                        // is still useful; one that refuses to open because of it is not.
                    }
                }

                Dictionary<XmlNode, LoadableXmlAsset> lookup = new Dictionary<XmlNode, LoadableXmlAsset>();
                XmlDocument combined = LoadedModManager.CombineIntoUnifiedXML(assets, lookup);

                if (worker != Thread.CurrentThread)
                    return;

                lock (Lock)
                {
                    document = combined;
                    sources = lookup;
                    nodeCount = combined?.DocumentElement?.ChildNodes?.Count ?? 0;
                    fileCount = assets.Count;
                    state = XmlWorkbenchState.Ready;
                }
            }
            catch (Exception ex)
            {
                lock (Lock)
                {
                    state = XmlWorkbenchState.Failed;
                    failure = ex.Message;
                }
            }
        }

        /// <summary>
        /// Runs an XPath and returns what it matched.
        ///
        /// <b>A bad expression is an answer, not a fault.</b> Testing an XPath means getting it wrong repeatedly,
        /// so a malformed one comes back as a message to read rather than an exception in the log. That is most
        /// of the point of the tool.
        /// </summary>
        internal static List<XmlMatch> Query(string xpath, int limit, out string error)
        {
            error = null;

            List<XmlMatch> results = new List<XmlMatch>();

            XmlDocument doc;
            Dictionary<XmlNode, LoadableXmlAsset> lookup;

            lock (Lock)
            {
                if (state != XmlWorkbenchState.Ready)
                    return results;

                doc = document;
                lookup = sources;
            }

            if (doc == null || xpath.NullOrEmpty())
                return results;

            XmlNodeList matched;

            try
            {
                matched = doc.SelectNodes(xpath);
            }
            catch (Exception ex)
            {
                // XPathException and its friends. The message is what the reader needs.
                error = ex.Message;

                return results;
            }

            if (matched == null)
                return results;

            foreach (XmlNode node in matched)
            {
                if (results.Count >= limit)
                    break;

                results.Add(Describe(node, lookup));
            }

            return results;
        }

        /// <summary>
        /// One match, with the file it came from.
        ///
        /// <b>The source is found by walking up, not by looking the node up.</b> The lookup is keyed by the
        /// top-level definition nodes, because that is the granularity a file has: everything below one of them
        /// came from the same file. A matched node is usually deep inside a definition, so the walk finds the
        /// ancestor that is a direct child of the document root and asks about that.
        /// </summary>
        private static XmlMatch Describe(XmlNode node, Dictionary<XmlNode, LoadableXmlAsset> lookup)
        {
            // Climb until the parent is the document element, so this lands on the definition itself. The first
            // version tested for a grandparent instead and went one level too far -- every match reported its
            // owner as "Defs" and its file as unknown, because the root is not a key in the lookup.
            XmlNode root = node?.OwnerDocument?.DocumentElement;
            XmlNode top = node;

            while (top?.ParentNode != null && top.ParentNode != root)
                top = top.ParentNode;

            LoadableXmlAsset asset = null;

            if (top != null && lookup != null)
                lookup.TryGetValue(top, out asset);

            string owner = null;

            if (top != null)
            {
                XmlNode defName = top["defName"];
                owner = defName != null ? top.Name + " " + defName.InnerText : top.Name;
            }

            return new XmlMatch
            {
                Xml = Pretty(node),
                Owner = owner,
                Path = asset?.FullFilePath,
                Mod = asset?.mod?.Name
            };
        }

        /// <summary>
        /// A node's XML, indented.
        ///
        /// <b>The document carries no formatting of its own.</b> Whitespace is stripped when the files are read
        /// -- <c>LoadableXmlAsset</c> sets <c>IgnoreWhitespace</c> -- so <c>OuterXml</c> is one unbroken line
        /// however the file was written. For a definition of any size that is a wall of text nobody can read a
        /// structure out of, which is most of what somebody opens this to do.
        ///
        /// Falls back to the raw form if the writer objects to something, since an unreadable answer still beats
        /// no answer.
        /// </summary>
        private static string Pretty(XmlNode node)
        {
            if (node == null)
                return string.Empty;

            try
            {
                StringBuilder text = new StringBuilder(node.OuterXml.Length + 128);

                XmlWriterSettings settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    OmitXmlDeclaration = true,
                    NewLineChars = "\n",
                    CheckCharacters = false
                };

                using (XmlWriter writer = XmlWriter.Create(text, settings))
                    node.WriteTo(writer);

                return text.ToString();
            }
            catch
            {
                return node.OuterXml;
            }
        }

        /// <summary>Drops the document. Called when the window closes, and when a game starts.</summary>
        internal static void Release()
        {
            lock (Lock)
            {
                state = XmlWorkbenchState.Empty;
                document = null;
                sources = null;
                failure = null;
                scopeName = string.Empty;
                nodeCount = 0;
                fileCount = 0;
            }
        }
    }
}
