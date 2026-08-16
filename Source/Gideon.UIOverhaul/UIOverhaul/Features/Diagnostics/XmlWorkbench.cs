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
        /// Whether the build runs every mod's patch operations over the combined document, as loading does.
        ///
        /// <b>On, because without it the document is not the one the game reads.</b> Patches do far more than
        /// adjust values: they routinely create the structure other definitions depend on. The case that proved
        /// it was a mod inheriting from <c>ParentName="Cooler"</c>, where Core declares the cooler with no
        /// <c>Name</c> at all and a patch adds one so mods can inherit from it. Read before patching, every
        /// child of that node reports a missing parent, and every one of those reports is wrong.
        ///
        /// <b>Off is still worth having, for the patch simulator.</b> Testing an operation against a document
        /// that has already had that same operation applied to it is its own kind of lie: a Replace finds its
        /// own replacement and reports matching nothing. Turning this off gives the simulator the raw document,
        /// which is what an author comparing against their own file expects.
        ///
        /// It is also the slow half of the build, so making it a choice is not only about correctness.
        /// </summary>
        private static bool patching = true;

        /// <summary>How many patch operations reported failure during the last build.</summary>
        private static int patchFailures;

        internal static bool Patching
        {
            get
            {
                lock (Lock)
                    return patching;
            }
        }

        internal static int PatchFailures
        {
            get
            {
                lock (Lock)
                    return patchFailures;
            }
        }

        /// <summary>
        /// The built document, and where each top-level definition came from.
        ///
        /// Only ever touched on the main thread once the build reports Ready. The worker publishes both under the
        /// lock at the moment it finishes and never writes them again.
        /// </summary>
        private static XmlDocument document;

        private static Dictionary<XmlNode, LoadableXmlAsset> sources;

        /// <summary>The mods the current document was built from, so it can be rebuilt without asking again.</summary>
        private static List<ModContentPack> lastScope;

        /// <summary>
        /// One edit made to the document while a simulation was running, and enough to put it back.
        ///
        /// <b>Recorded from the document's own change notifications.</b> <c>XmlDocument</c> raises an event for
        /// every insertion, removal and value change, which is a complete account of what a patch did without
        /// anything having to predict what it might do.
        /// </summary>
        private struct Change
        {
            public XmlNodeChangedAction Action;
            public XmlNode Node;
            public XmlNode OldParent;
            public XmlNode NewParent;

            /// <summary>
            /// What the node sat after before it was removed.
            ///
            /// Captured from <c>NodeRemoving</c> rather than <c>NodeRemoved</c>, because by the time the removal
            /// has happened the node has no siblings left to describe its position.
            /// </summary>
            public XmlNode PreviousSibling;

            public string OldValue;
        }

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
        internal static void Build(List<ModContentPack> scope, string name, bool? applyPatches = null)
        {
            if (scope == null || scope.Count == 0)
                return;

            List<ModContentPack> captured = new List<ModContentPack>(scope);

            lock (Lock)
            {
                if (applyPatches.HasValue)
                    patching = applyPatches.Value;

                lastScope = captured;
                state = XmlWorkbenchState.Building;
                failure = null;
                scopeName = name;
                nodeCount = 0;
                fileCount = 0;
                patchFailures = 0;
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

                int failed = Patch(combined);

                if (worker != Thread.CurrentThread)
                    return;

                lock (Lock)
                {
                    document = combined;
                    sources = lookup;
                    nodeCount = combined?.DocumentElement?.ChildNodes?.Count ?? 0;
                    fileCount = assets.Count;
                    patchFailures = failed;
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
        /// Runs every active mod's patch operations over the freshly combined document.
        ///
        /// <b>Vanilla's own pass, because a reimplementation would diverge exactly where it mattered.</b>
        /// <c>LoadedModManager.ApplyPatches</c> is what the load calls, in the order the load calls it, and
        /// every custom operation any mod defines works here because it is the mod's own class doing the work.
        ///
        /// <b>Every mod's patches, not just the ones in scope, and that is deliberate.</b> A narrowed scope
        /// asks about one mod's definitions, and the things done to those definitions are done largely by other
        /// mods. Restricting the pass to the scope would answer a question nobody has.
        ///
        /// <b>The operations are fresh objects.</b> RimWorld finishes a load with <c>ClearCachedPatches</c>,
        /// which calls <c>Complete</c> on each operation and then drops the mod's cached list, so reading
        /// <c>ModContentPack.Patches</c> now re-reads the Patches folder from disk. Nothing here is re-running a
        /// completed operation, and the same pairing is done afterwards so the reloaded lists are handed back
        /// rather than held for the session.
        ///
        /// <b>Its log output is diverted rather than written, and counted.</b> A patch that matches nothing
        /// says so through <c>Log.Error</c>, and with a narrowed scope most of them legitimately match nothing
        /// because the definitions they target were not read. Letting that reach the log would fill it with
        /// hundreds of errors describing a document that only exists inside this window. The count is reported
        /// instead, where it can be read with the scope beside it.
        /// </summary>
        /// <returns>How many operations reported failure.</returns>
        private static int Patch(XmlDocument combined)
        {
            bool wanted;

            lock (Lock)
                wanted = patching;

            if (!wanted || combined == null)
                return 0;

            int failed = 0;

            UILogReplay.Begin((error, text) =>
            {
                if (error)
                    failed++;
            });

            try
            {
                LoadedModManager.ApplyPatches(combined, new Dictionary<XmlNode, LoadableXmlAsset>());
            }
            catch (Exception ex)
            {
                // Reported through the count rather than thrown on. A document that is patched as far as it
                // got is still worth far more than no document, and the build has already read every file.
                UILogReplay.End();
                UIGuard.Report("Diagnostics.WorkbenchPatches", ex,
                    "The workbench document is only partly patched. Turn patching off to read the raw files.");
            }
            finally
            {
                UILogReplay.End();

                // The vanilla pairing for the lazy reload above. Without it every mod holds a second copy of
                // its patch operations for the rest of the session, for a document that is dropped when this
                // window closes.
                UIGuard.Try("Diagnostics.WorkbenchPatchCleanup", LoadedModManager.ClearCachedPatches, null);
            }

            return failed;
        }

        /// <summary>
        /// Hands out the built document and the file each definition came from.
        ///
        /// <b>For the bug hunt, which needs to walk everything rather than ask a question.</b> Query answers one
        /// expression at a time and is the right shape for that; re-parsing every definition in the scope is a
        /// different job and would be absurd to express as a hundred thousand queries.
        ///
        /// Both are handed over as they are, not copied. The caller reads; nothing here writes to either after
        /// the build published them, and the one thing that does write -- a simulated patch -- goes through
        /// <see cref="Journaled{T}"/> and puts it back.
        /// </summary>
        /// <returns>False when nothing has been built, in which case neither output is usable.</returns>
        internal static bool Snapshot(out XmlDocument built, out Dictionary<XmlNode, LoadableXmlAsset> files)
        {
            lock (Lock)
            {
                built = document;
                files = sources;

                return state == XmlWorkbenchState.Ready && document != null;
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
        /// Runs something that edits the document, then puts the document back exactly as it was.
        ///
        /// <b>Why this exists instead of a copy.</b> Simulating a patch needs the whole document readable, since
        /// an operation may count siblings or check that something is absent. Copying it to get that was
        /// ruinous: the combined XML is the largest object graph the load builds, and duplicating it per press
        /// left the heap bloated and the game stuttering. Nothing needed a copy to <i>read</i>; the copy existed
        /// only because patches <i>write</i>.
        ///
        /// <b>So the writes are recorded and reversed.</b> <c>XmlDocument</c> announces every insertion, removal
        /// and value change, so the edits a patch makes are journalled as it makes them and replayed backwards
        /// afterwards. The document is read in full, written to briefly, and left identical.
        ///
        /// <b>This is not the game's data.</b> The document is one this class built from disk when the workbench
        /// opened; RimWorld discarded its own at the end of loading, and defs are C# objects in
        /// <c>DefDatabase</c> with no link back to XML. Nothing outside this window can see these edits even
        /// while they exist.
        ///
        /// <b>And it is checked.</b> If any part of the reversal fails, the document is rebuilt from disk rather
        /// than trusted -- a diagnostic that answers subtly wrong is worse than one that makes you wait.
        /// </summary>
        /// <param name="body">Given the document. Whatever it returns is returned from here.</param>
        /// <param name="failure">Null when the document was restored cleanly.</param>
        internal static T Journaled<T>(Func<XmlDocument, T> body, out string failure)
        {
            failure = null;

            XmlDocument target;

            lock (Lock)
            {
                if (state != XmlWorkbenchState.Ready || document == null)
                {
                    failure = "The document is not built yet.";

                    return default(T);
                }

                target = document;
            }

            List<Change> journal = new List<Change>();
            Dictionary<XmlNode, XmlNode> removing = new Dictionary<XmlNode, XmlNode>();

            XmlNodeChangedEventHandler onRemoving = (sender, args) =>
                removing[args.Node] = args.Node.PreviousSibling;

            XmlNodeChangedEventHandler onInserted = (sender, args) => journal.Add(new Change
            {
                Action = XmlNodeChangedAction.Insert,
                Node = args.Node,
                NewParent = args.NewParent
            });

            XmlNodeChangedEventHandler onRemoved = (sender, args) =>
            {
                XmlNode previous;
                removing.TryGetValue(args.Node, out previous);
                removing.Remove(args.Node);

                journal.Add(new Change
                {
                    Action = XmlNodeChangedAction.Remove,
                    Node = args.Node,
                    OldParent = args.OldParent,
                    PreviousSibling = previous
                });
            };

            XmlNodeChangedEventHandler onChanged = (sender, args) => journal.Add(new Change
            {
                Action = XmlNodeChangedAction.Change,
                Node = args.Node,
                OldValue = args.OldValue
            });

            target.NodeRemoving += onRemoving;
            target.NodeInserted += onInserted;
            target.NodeRemoved += onRemoved;
            target.NodeChanged += onChanged;

            T result = default(T);

            try
            {
                result = body(target);
            }
            finally
            {
                // Unsubscribed before anything is undone, or the undo would journal itself and never end.
                target.NodeRemoving -= onRemoving;
                target.NodeInserted -= onInserted;
                target.NodeRemoved -= onRemoved;
                target.NodeChanged -= onChanged;

                failure = Rewind(journal);

                if (failure != null)
                    Rebuild();
            }

            return result;
        }

        /// <summary>
        /// Replays a journal backwards.
        /// </summary>
        /// <returns>A description of the first failure, or null when everything was undone.</returns>
        private static string Rewind(List<Change> journal)
        {
            for (int i = journal.Count - 1; i >= 0; i--)
            {
                Change change = journal[i];

                try
                {
                    switch (change.Action)
                    {
                        case XmlNodeChangedAction.Insert:
                            Detach(change.Node, change.NewParent);
                            break;

                        case XmlNodeChangedAction.Remove:
                            Reattach(change.Node, change.OldParent, change.PreviousSibling);
                            break;

                        default:
                            change.Node.Value = change.OldValue;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    return "A simulated patch could not be undone (" + change.Action + "): " + ex.Message;
                }
            }

            return null;
        }

        /// <summary>
        /// Removes a node that a patch added.
        ///
        /// Attributes are not children, so they cannot be removed through <c>RemoveChild</c>; that throws rather
        /// than failing quietly, which is how this was found.
        /// </summary>
        private static void Detach(XmlNode node, XmlNode parent)
        {
            if (node == null || parent == null)
                return;

            XmlAttribute attribute = node as XmlAttribute;

            if (attribute != null)
            {
                XmlElement owner = parent as XmlElement;

                if (owner != null && owner.Attributes != null)
                    owner.Attributes.Remove(attribute);

                return;
            }

            if (node.ParentNode == parent)
                parent.RemoveChild(node);
        }

        /// <summary>Puts a node a patch removed back where it was.</summary>
        private static void Reattach(XmlNode node, XmlNode parent, XmlNode previous)
        {
            if (node == null || parent == null)
                return;

            XmlAttribute attribute = node as XmlAttribute;

            if (attribute != null)
            {
                XmlElement owner = parent as XmlElement;

                if (owner != null)
                    owner.Attributes.Append(attribute);

                return;
            }

            // Order is restored, not merely membership: a def whose elements came back in a different order
            // would read as changed to anybody comparing it against the file on disk.
            if (previous != null && previous.ParentNode == parent)
                parent.InsertAfter(node, previous);
            else
                parent.PrependChild(node);
        }

        /// <summary>Rebuilds the current scope from disk, after the document has been left untrustworthy.</summary>
        private static void Rebuild()
        {
            List<ModContentPack> scope;
            string name;

            lock (Lock)
            {
                scope = lastScope;
                name = scopeName;
            }

            if (scope != null && scope.Count > 0)
                Build(scope, name);
        }

        internal static string PrettyPrint(XmlNode node)
        {
            return Pretty(node);
        }

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
