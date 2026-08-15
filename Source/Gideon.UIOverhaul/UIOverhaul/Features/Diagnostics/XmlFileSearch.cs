using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>Where a search has got to.</summary>
    internal enum XmlSearchState
    {
        Idle,
        Running,
        Done,
        Failed
    }

    /// <summary>
    /// What kind of mention a hit is, which is the difference between an answer and noise.
    ///
    /// A raw substring scan finds every list entry, label and comment that happens to name a def, and on a large
    /// mod list that is hundreds of lines saying "somebody mentions this" when the question was "where does this
    /// come from". Classifying the match is what separates the two.
    /// </summary>
    internal enum XmlSearchHitKind
    {
        /// <summary>The file declares it: <c>&lt;defName&gt;</c> holds exactly this name.</summary>
        Definition,

        /// <summary>A patch file mentions it, so something is being added, moved or rewritten around it.</summary>
        Patch,

        /// <summary>Anything else naming it: a list entry, an attribute, a comment. Hidden unless asked for.</summary>
        Reference
    }

    /// <summary>One line of one file that mentioned the term.</summary>
    internal struct XmlSearchHit
    {
        public string Path;
        public int Line;

        /// <summary>The matching line, trimmed. Long lines are cut, since this is shown in one row.</summary>
        public string Text;

        /// <summary>The mod the file belongs to, for grouping the answer by who is responsible.</summary>
        public string Mod;

        public XmlSearchHitKind Kind;
    }

    /// <summary>
    /// Finds which XML files mention a name, by reading the mods on disk.
    ///
    /// <b>This exists because of the one question the loading console could not answer.</b> A cross-reference
    /// failure is raised in a phase with no file context, and for a list-valued field like <c>genes</c> the
    /// registration that <i>did</i> know the file goes through a generic method Harmony cannot safely patch. So
    /// the in-memory route runs out. What does not run out is the files themselves: the def is named somewhere,
    /// in text, on disk, and reading for it answers the question completely and for every case at once.
    ///
    /// <b>Deliberately a text scan, and that is the feature.</b> Not an XPath query, not a def graph -- both
    /// would need the data that already failed to be available. Reading the files cannot be defeated by a patch
    /// operation, a generic method, an inherited abstract parent, or a def that never finished loading, which is
    /// exactly the situation somebody is in when they need this.
    ///
    /// <b>What it is not is undiscriminating.</b> The first version reported every line containing the string,
    /// and on a real mod list that is hundreds of gene lists, comments and labels burying the one file that
    /// declares the thing. Each match is classified instead -- see <see cref="XmlSearchHitKind"/> -- so the
    /// answer to "where does this come from" is a declaration or a patch, and the mentions that answer a
    /// different question are kept aside rather than mixed in.
    ///
    /// <b>On a background thread, because it is slow and must not be anything else.</b> A large mod list is
    /// thousands of files and hundreds of megabytes of text; doing that on the UI thread would freeze the game
    /// for seconds with no way to tell it had not hung. Nothing here touches a Unity API, and the results are a
    /// plain list the main thread copies out under a lock.
    ///
    /// <b>The folders are collected before the thread starts.</b> <c>LoadedModManager</c> and
    /// <c>ModContentPack</c> are the game's own state and reading them off a worker thread is not something this
    /// mod gets to assume is safe, so the paths are gathered on the caller's thread and the worker sees nothing
    /// but strings.
    /// </summary>
    internal static class XmlFileSearch
    {
        /// <summary>
        /// Most hits kept.
        ///
        /// A search for a common word would otherwise return tens of thousands of lines nobody will read. The
        /// count of files that matched keeps rising past this, so the panel can say the list was cut rather than
        /// implying that was all of it.
        /// </summary>
        private const int MaxHits = 300;

        /// <summary>Longest line kept. A minified or generated XML file can have one line of half a megabyte.</summary>
        private const int MaxLineLength = 300;

        private static readonly object Lock = new object();

        private static XmlSearchState state = XmlSearchState.Idle;
        private static string term = string.Empty;
        private static string failure;
        private static List<XmlSearchHit> hits = new List<XmlSearchHit>();
        private static int scanned;
        private static int total;
        private static int matchedFiles;
        private static int declarations;
        private static int references;
        private static Thread worker;

        /// <summary>Set to ask a running search to stop at the next file.</summary>
        private static volatile bool cancelling;

        internal static XmlSearchState State
        {
            get
            {
                lock (Lock)
                    return state;
            }
        }

        internal static string Term
        {
            get
            {
                lock (Lock)
                    return term;
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

        /// <summary>Files read so far, and how many there are. For the progress line.</summary>
        internal static void Progress(out int done, out int all, out int files)
        {
            lock (Lock)
            {
                done = scanned;
                all = total;
                files = matchedFiles;
            }
        }

        /// <summary>
        /// The hits, optionally including the plain mentions.
        ///
        /// <b>References are collected but hidden by default.</b> The question this feature was built for is
        /// "where does this come from", and every list entry naming a def drowns that. But the opposite question
        /// -- "who is asking for this missing thing" -- is the other half of the same investigation, and throwing
        /// those lines away at scan time would mean a second full pass over every file to get them back. They are
        /// kept, counted separately so they cannot crowd out a declaration, and shown on request.
        /// </summary>
        internal static List<XmlSearchHit> Hits(bool includeReferences)
        {
            lock (Lock)
            {
                if (includeReferences)
                    return new List<XmlSearchHit>(hits);

                List<XmlSearchHit> shown = new List<XmlSearchHit>(declarations);

                foreach (XmlSearchHit hit in hits)
                {
                    if (hit.Kind != XmlSearchHitKind.Reference)
                        shown.Add(hit);
                }

                return shown;
            }
        }

        /// <summary>Declarations and patch mentions found, and plain references found.</summary>
        internal static void Counts(out int declared, out int referenced)
        {
            lock (Lock)
            {
                declared = declarations;
                referenced = references;
            }
        }

        /// <summary>
        /// Starts a search, replacing any that is running.
        ///
        /// The previous worker is asked to stop rather than aborted. It checks the flag between files, so the
        /// worst case is one more file being read after the request, and <c>Thread.Abort</c> in the middle of a
        /// file read is a way to leak a handle rather than a way to save a millisecond.
        /// </summary>
        internal static void Start(string searchFor)
        {
            if (searchFor.NullOrEmpty())
                return;

            List<KeyValuePair<string, string>> roots = UIGuard.Try("Diagnostics.CollectModFolders", Roots,
                new List<KeyValuePair<string, string>>(),
                "The file search has nowhere to look.");

            lock (Lock)
            {
                cancelling = true;

                term = searchFor;
                state = XmlSearchState.Running;
                failure = null;
                hits = new List<XmlSearchHit>();
                scanned = 0;
                total = 0;
                matchedFiles = 0;
                declarations = 0;
                references = 0;
            }

            Thread previous = worker;

            // A fresh flag for the new run, set after the old worker has been told to stop. The old one reads the
            // same field, so it sees false again -- which is why it is also checked against the thread identity
            // below rather than the flag alone.
            cancelling = false;

            worker = new Thread(() => Run(searchFor, roots, previous))
            {
                IsBackground = true,
                Name = "Gideon.XmlFileSearch"
            };

            worker.Start();
        }

        internal static void Cancel()
        {
            cancelling = true;

            lock (Lock)
            {
                if (state == XmlSearchState.Running)
                    state = XmlSearchState.Done;
            }
        }

        /// <summary>Forgets the last search, so the panel goes back to showing the entry.</summary>
        internal static void Reset()
        {
            Cancel();

            lock (Lock)
            {
                state = XmlSearchState.Idle;
                term = string.Empty;
                hits = new List<XmlSearchHit>();
                failure = null;
                scanned = 0;
                total = 0;
                matchedFiles = 0;
                declarations = 0;
                references = 0;
            }
        }

        /// <summary>
        /// Every loaded mod's name and root folder.
        ///
        /// Read on the caller's thread, before the worker exists. Official content is included: a def can go
        /// missing because an expansion is not enabled, and the file that wants it being in Core is exactly the
        /// answer in that case.
        /// </summary>
        private static List<KeyValuePair<string, string>> Roots()
        {
            List<KeyValuePair<string, string>> roots = new List<KeyValuePair<string, string>>();
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            if (mods == null)
                return roots;

            foreach (ModContentPack mod in mods)
            {
                if (mod == null)
                    continue;

                string root = mod.RootDir;

                if (!root.NullOrEmpty())
                    roots.Add(new KeyValuePair<string, string>(mod.Name ?? mod.PackageId ?? "unknown", root));
            }

            return roots;
        }

        /// <summary>
        /// The worker. Nothing in here may touch Unity, and nothing in here may throw.
        /// </summary>
        private static void Run(string searchFor, List<KeyValuePair<string, string>> roots, Thread previous)
        {
            try
            {
                // Waited for rather than raced with, so two searches cannot interleave their results into one
                // list. It stops between files, so this is a short wait bounded by one file read.
                if (previous != null && previous.IsAlive)
                    previous.Join(2000);

                List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();

                foreach (KeyValuePair<string, string> root in roots)
                {
                    if (Stopping())
                        return;

                    try
                    {
                        foreach (string file in Directory.GetFiles(root.Value, "*.xml",
                                     SearchOption.AllDirectories))
                            files.Add(new KeyValuePair<string, string>(root.Key, file));
                    }
                    catch
                    {
                        // A mod folder that cannot be enumerated is one mod's worth of results lost, not a
                        // failed search. Permissions, a folder deleted while the game runs, a path over the
                        // limit: none of them are worth abandoning the other three hundred mods for.
                    }
                }

                lock (Lock)
                    total = files.Count;

                foreach (KeyValuePair<string, string> file in files)
                {
                    if (Stopping())
                        return;

                    Scan(file.Key, file.Value, searchFor);

                    lock (Lock)
                        scanned++;
                }

                lock (Lock)
                {
                    if (state == XmlSearchState.Running)
                        state = XmlSearchState.Done;
                }
            }
            catch (Exception ex)
            {
                lock (Lock)
                {
                    state = XmlSearchState.Failed;
                    failure = ex.Message;
                }
            }
        }

        /// <summary>Whether this worker should give up, either because it was cancelled or superseded.</summary>
        private static bool Stopping()
        {
            return cancelling || worker != Thread.CurrentThread;
        }

        /// <summary>
        /// Reads one file, line by line, recording the lines that mention the term.
        ///
        /// Streamed rather than read whole. Some mods ship XML files of tens of megabytes, and holding one in
        /// memory to search it would spike well past anything the rest of this feature costs.
        /// </summary>
        private static void Scan(string mod, string path, string searchFor)
        {
            try
            {
                bool matchedThisFile = false;

                // Whether this file is a patch rather than a set of definitions, and whether that is settled yet.
                // The root element is on the first line or two, so this is always decided before any content
                // line is reached; a file whose root is neither is treated as definitions, which only costs it
                // the patch classification.
                bool isPatch = false;
                bool rootKnown = false;

                using (StreamReader reader = new StreamReader(path))
                {
                    int number = 0;
                    string line;

                    while ((line = reader.ReadLine()) != null)
                    {
                        number++;

                        if (!rootKnown)
                        {
                            if (line.IndexOf("<Patch", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                isPatch = true;
                                rootKnown = true;
                            }
                            else if (line.IndexOf("<Defs", StringComparison.OrdinalIgnoreCase) >= 0
                                     || line.IndexOf("<LanguageData", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                rootKnown = true;
                            }
                        }

                        if (line.IndexOf(searchFor, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        XmlSearchHitKind kind = IsDefNameLine(line, searchFor)
                            ? XmlSearchHitKind.Definition
                            : isPatch
                                ? XmlSearchHitKind.Patch
                                : XmlSearchHitKind.Reference;

                        if (!matchedThisFile && kind != XmlSearchHitKind.Reference)
                        {
                            matchedThisFile = true;

                            lock (Lock)
                                matchedFiles++;
                        }

                        lock (Lock)
                        {
                            // Capped per class, so a name mentioned in three hundred gene lists cannot crowd the
                            // one file that actually declares it out of the results. The file count keeps
                            // climbing past the cap so the panel can say how much it is not showing.
                            if (kind == XmlSearchHitKind.Reference)
                            {
                                if (references >= MaxHits)
                                    continue;

                                references++;
                            }
                            else if (declarations >= MaxHits)
                            {
                                continue;
                            }
                            else
                            {
                                declarations++;
                            }

                            string text = line.Trim();

                            hits.Add(new XmlSearchHit
                            {
                                Mod = mod,
                                Path = path,
                                Line = number,
                                Kind = kind,
                                Text = text.Length > MaxLineLength ? text.Substring(0, MaxLineLength) + "..." : text
                            });
                        }
                    }
                }
            }
            catch
            {
                // One unreadable file is not a failed search. A file locked by another program, or one whose
                // encoding the reader cannot handle, costs its own results and nothing else.
            }
        }

        /// <summary>
        /// Whether this line declares the name, rather than merely containing it.
        ///
        /// <b>The element's value is compared whole, not searched.</b> A substring test would call
        /// <c>&lt;defName&gt;BotchJob_RottingFleshExtra&lt;/defName&gt;</c> a declaration of
        /// <c>BotchJob_RottingFlesh</c>, which is the sort of near-miss that sends somebody to edit the wrong
        /// file. Same element on one line is how every def in the game is written, so nothing real is missed by
        /// not handling the split-across-lines case.
        /// </summary>
        private static bool IsDefNameLine(string line, string searchFor)
        {
            const string open = "<defName>";
            const string close = "</defName>";

            int start = line.IndexOf(open, StringComparison.OrdinalIgnoreCase);

            if (start < 0)
                return false;

            start += open.Length;

            int end = line.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);

            if (end < 0)
                return false;

            return string.Equals(line.Substring(start, end - start).Trim(), searchFor,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
