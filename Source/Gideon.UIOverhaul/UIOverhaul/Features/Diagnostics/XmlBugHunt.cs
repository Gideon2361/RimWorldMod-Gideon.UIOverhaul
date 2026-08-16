using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>Where the hunt has got to.</summary>
    internal enum XmlBugHuntState
    {
        Idle,

        /// <summary>Registering inheritance, which has to finish before a single definition can be read.</summary>
        Preparing,

        Scanning,
        Finished,
        Cancelled,
        Failed
    }

    /// <summary>One thing the parser complained about, with everything needed to go and fix it.</summary>
    internal struct BugFinding
    {
        /// <summary>Whether it stopped the whole definition rather than one field.</summary>
        public bool Fatal;

        public bool Error;

        /// <summary>The node's element name, which is the definition type as the file spells it.</summary>
        public string DefType;

        public string DefName;

        /// <summary>The field the parser was reading, when the message says. Null when it cannot be told.</summary>
        public string Field;

        /// <summary>The message, cut down to the part worth reading on a card.</summary>
        public string Message;

        /// <summary>The whole message, for copying into a bug report.</summary>
        public string Detail;

        /// <summary>The offending XML, when the message quoted it.</summary>
        public string Snippet;

        /// <summary>Full path of the file the definition was read from.</summary>
        public string Path;

        public string Mod;
    }

    /// <summary>
    /// Re-reads every definition in the workbench's scope through RimWorld's own deserializer and collects what
    /// it complains about, naming the file, the definition and the field for each.
    ///
    /// <b>The problem this solves.</b> A bad value in XML does not stop a load and does not name itself. RimWorld
    /// writes something like <c>Exception parsing System.Int32 from "7.5"</c> into a log holding thousands of
    /// lines, with no file, no defName and, on the path that matters most, no field either. In 1.6 the
    /// consequence is worse than it used to be: the deserializer the game actually uses does not catch per-field
    /// failures, so one decimal where an integer belongs takes the entire definition down and everything that
    /// referenced it starts failing for reasons that look unrelated. The information needed to fix it in five
    /// seconds exists; it is simply never put together. This puts it together.
    ///
    /// <b>The game's parser does the work, not a reimplementation.</b> Anything else would be a validator that
    /// agreed with RimWorld right up until the moment it mattered. The definitions built here are read and
    /// dropped; nothing is added to <c>DefDatabase</c> and nothing the running game can see is touched.
    ///
    /// <b>The legacy deserializer is used deliberately, and it is the one real judgement call here.</b> 1.6
    /// defaults to <c>DirectXmlToObjectNew</c>, which emits IL per definition type and is what the load runs.
    /// This uses <c>DirectXmlToObject</c> instead, for two reasons. It catches each field's failure separately
    /// and carries on, so a definition with four bad values reports four findings rather than the first one and
    /// silence; and it takes a <c>doPostLoad</c> flag, which the emitted one does not. Running PostLoad matters:
    /// <c>BuildableDef.PostLoad</c> queues graphic resolution, so scanning a large mod list with it enabled
    /// would load a texture for every building in the game as a side effect of asking a question about XML.
    /// Both parsers read values through the same <c>ParseHelper</c> and resolve fields through the same
    /// <c>XmlToObjectUtils</c>, so what is found is the same; only the recovery differs, and finding more is the
    /// direction to err in.
    ///
    /// <b>Run against the files as they ship, before patch operations.</b> The workbench builds its document
    /// from each mod's Defs folder and does not run the patch pass, which is one of the most expensive parts of
    /// a load and whose operations have already been completed once for this session. The bias that leaves is
    /// the safe one: a patch that repairs a broken value is vanishingly rare, while a patch that introduces one
    /// is invisible here. So this under-reports rather than over-reports, and the panel says so.
    ///
    /// <b>Inheritance is resolved the way the load resolves it.</b> <c>XmlInheritance</c> is registered,
    /// resolved and cleared exactly as <c>LoadedModManager.ParseAndProcessXML</c> does, so a definition that
    /// inherits a broken field from an abstract parent reports it. The resolution builds its own copies of the
    /// nodes and never writes to the document, so the workbench's other tabs see it unchanged afterwards.
    ///
    /// <b>Pumped from the main thread rather than run on a worker.</b> Everything downstream of
    /// <c>ObjectFromXml</c> is static and unsynchronised -- the field lookup caches, the type-to-parser
    /// dictionaries, the stack of types currently being instantiated -- and constructors of arbitrary comp
    /// classes run as part of it. A background thread would be racing all of that for the sake of a progress
    /// bar. See <see cref="Pump"/>.
    /// </summary>
    internal static class XmlBugHunt
    {
        /// <summary>
        /// Most findings kept. A single genuinely broken mod can raise tens of thousands.
        ///
        /// Counting continues past this; only storing stops. The number is what says how bad it is, and holding
        /// a hundred thousand strings to say the same thing is how a diagnostic becomes the problem.
        /// </summary>
        private const int MaxFindings = 2000;

        /// <summary>
        /// Most findings kept for one definition.
        ///
        /// A definition written against a different game version can miss on every field it has. Twenty five is
        /// past the point where the answer has stopped being "this field is wrong" and become "this file is for
        /// another version", which one card can say.
        /// </summary>
        private const int MaxPerDef = 25;

        /// <summary>How much of a message is worth showing on a card before it is cut.</summary>
        private const int MessageBudget = 400;

        private struct Logged
        {
            public bool Error;
            public string Text;
        }

        private static XmlBugHuntState state = XmlBugHuntState.Idle;
        private static string failure;

        private static List<XmlNode> queue = new List<XmlNode>();
        private static Dictionary<XmlNode, LoadableXmlAsset> sources;
        private static int index;

        /// <summary>
        /// How many definitions the run covers, kept apart from the queue.
        ///
        /// The queue is dropped the moment the run ends, because it holds a reference to every definition node
        /// in the document and there is no reason to keep a second list of them alive for the rest of the
        /// session. The count still has to be reportable afterwards, so it lives here.
        /// </summary>
        private static int total;

        /// <summary>How far registration has got, during Preparing.</summary>
        private static int registered;

        private static readonly List<BugFinding> findings = new List<BugFinding>();
        private static readonly List<Logged> captured = new List<Logged>();

        /// <summary>
        /// Every definition's name and inheritance name, against the file it was read from.
        ///
        /// <b>For the one phase that reports faults without a node to blame them on.</b> Inheritance is
        /// resolved in a single pass over the whole document, so a broken <c>ParentName</c> is reported from
        /// somewhere that knows only the text of the node. That text carries the definition's own name, and
        /// this turns that name back into a file, which is the entire question the reader has.
        ///
        /// Keyed on both, because a node used as an inheritance parent is identified by its <c>Name</c>
        /// attribute rather than by a defName, and abstract ones have no defName at all. Last one wins on a
        /// collision: two definitions sharing a name is itself a fault, and it is reported on its own.
        /// </summary>
        private static readonly Dictionary<string, LoadableXmlAsset> owners =
            new Dictionary<string, LoadableXmlAsset>();

        private static string currentFile;
        private static int suppressed;
        private static int brokenDefs;
        private static string scopeName;

        internal static XmlBugHuntState State => state;
        internal static string Failure => failure;
        internal static List<BugFinding> Findings => findings;

        /// <summary>How many definitions were found to have something wrong with them.</summary>
        internal static int BrokenDefs => brokenDefs;

        /// <summary>Findings past <see cref="MaxFindings"/>, which were counted and not kept.</summary>
        internal static int Suppressed => suppressed;

        internal static int Total => total;
        internal static int Done => state == XmlBugHuntState.Preparing ? registered : index;

        /// <summary>The file being read, for the progress window to name.</summary>
        internal static string CurrentFile => currentFile;

        internal static string ScopeName => scopeName;

        internal static float Fraction
        {
            get
            {
                if (total == 0)
                    return 0f;

                // Registration and scanning are two passes over the same list, so the bar covers both rather
                // than filling once and starting again, which reads as a stall followed by a restart.
                float half = (float) Done / total * 0.5f;

                return state == XmlBugHuntState.Preparing ? half : 0.5f + half;
            }
        }

        internal static bool Running =>
            state == XmlBugHuntState.Preparing || state == XmlBugHuntState.Scanning;

        /// <summary>
        /// Builds the work list and puts the hunt in its first phase. Cheap: nothing is parsed here.
        /// </summary>
        internal static void Begin(string scope)
        {
            Reset();

            scopeName = scope;

            XmlDocument document;
            Dictionary<XmlNode, LoadableXmlAsset> files;

            if (!XmlWorkbench.Snapshot(out document, out files))
            {
                state = XmlBugHuntState.Failed;
                failure = "The XML has not finished loading yet.";

                return;
            }

            sources = files;

            bool built = UIGuard.Try("Diagnostics.BugHuntBegin", () =>
            {
                XmlNodeList children = document.DocumentElement == null
                    ? null
                    : document.DocumentElement.ChildNodes;

                if (children == null)
                    return;

                // Every element, abstract ones included. They are filtered at the scanning step and not here,
                // because inheritance has to be registered over the whole set first: an abstract node exists
                // precisely to be inherited from, so leaving it out would leave every one of its children
                // unable to find its parent and report a fault that the XML does not have.
                foreach (XmlNode node in children)
                {
                    if (node.NodeType == XmlNodeType.Element)
                        queue.Add(node);
                }
            }, "The bug hunt could not read the loaded XML.");

            if (!built)
            {
                state = XmlBugHuntState.Failed;
                failure = "The list of definitions could not be built.";

                return;
            }

            total = queue.Count;

            // Cleared before registering rather than only afterwards. It is empty for the whole of a session
            // once loading has finished, so this is a guard against starting from someone else's leftovers
            // rather than routine housekeeping.
            XmlInheritance.Clear();

            state = XmlBugHuntState.Preparing;
        }

        /// <summary>
        /// Whether the load would decline to build this node, so the scan does too.
        ///
        /// Abstract nodes exist to be inherited from and are never built on their own, and a node whose
        /// <c>MayRequire</c> mods are not active is not part of this game. Reporting either would be reporting
        /// XML that is doing exactly what it is supposed to do. Both tests mirror
        /// <c>LoadedModManager.ParseAndProcessXML</c> and <c>DirectXmlLoader.DefFromNode</c>.
        ///
        /// <b>Asked at the scanning step and not while building the queue,</b> for the reason given there:
        /// inheritance is registered over everything, and only building is selective. Nothing is lost by
        /// skipping an abstract parent here, since whatever is wrong inside it surfaces against every child
        /// that inherits it, named against a definition somebody can actually find in the game.
        /// </summary>
        private static bool Skipped(XmlNode node)
        {
            XmlAttributeCollection attributes = node.Attributes;

            if (attributes == null)
                return true;

            XmlAttribute isAbstract = attributes["Abstract"];

            if (isAbstract != null && isAbstract.Value.Equals("true", StringComparison.InvariantCultureIgnoreCase))
                return true;

            XmlAttribute all = attributes["MayRequire"];

            if (all != null && !ModLister.AllModsActiveNoSuffix(all.Value.ToLower().Split(',')))
                return true;

            XmlAttribute any = attributes["MayRequireAnyOf"];

            return any != null && !ModLister.AnyModActiveNoSuffix(any.Value.ToLower().Split(','));
        }

        /// <summary>
        /// Does as much of the hunt as fits in <paramref name="budgetMs"/> and returns.
        ///
        /// <b>Main thread, in slices, and both halves of that are deliberate.</b> The parser is a thicket of
        /// unsynchronised static caches and it runs arbitrary constructors, so it belongs on the thread the
        /// game already uses for it. Doing the whole scan in one call would freeze the game for as long as it
        /// takes, which on a large mod list is long enough to look like a crash. A few milliseconds a frame
        /// leaves the window drawing, the bar moving and the cancel button live, and costs only wall clock.
        ///
        /// The budget is checked between definitions rather than inside one. A single definition is fast and
        /// there is no way to suspend a parse partway; the check is what stops a slice from running long, not
        /// what makes it precise.
        /// </summary>
        /// <returns>True when there is nothing left to do.</returns>
        internal static bool Pump(float budgetMs)
        {
            if (!Running)
                return true;

            bool ran = UIGuard.Try("Diagnostics.BugHuntPump", () => Slice(budgetMs),
                "The bug hunt stops where it is. Anything it had already found is still listed.");

            // Reported as well as stopped. A run that ended early has covered less than it says it set out to,
            // and a results panel that quietly showed fewer findings would read as good news.
            if (!ran)
            {
                failure = "The scan stopped on an unexpected error after " + index + " of " + total
                          + " definitions. What it found before that is listed below.";

                Finish(XmlBugHuntState.Failed);
            }

            return !Running;
        }

        private static void Slice(float budgetMs)
        {
            Stopwatch clock = Stopwatch.StartNew();

            if (state == XmlBugHuntState.Preparing)
            {
                Prepare(clock, budgetMs);

                return;
            }

            while (index < queue.Count)
            {
                XmlNode node = queue[index];

                index++;

                if (!Skipped(node))
                    ScanOne(node);

                if (clock.Elapsed.TotalMilliseconds >= budgetMs)
                    return;
            }

            Finish(XmlBugHuntState.Finished);
        }

        /// <summary>
        /// Registers every node for inheritance, then resolves.
        ///
        /// <b>Registration is sliced and resolution is not,</b> because resolution has no seam in it: it walks
        /// the parent links it just built and produces one merged node per child, and there is no way to stop
        /// partway and be left with something a parse could use. On a large mod list that is a visible pause,
        /// which the progress window says it is doing rather than leaving the bar apparently stuck.
        /// </summary>
        private static void Prepare(Stopwatch clock, float budgetMs)
        {
            while (registered < queue.Count)
            {
                XmlNode node = queue[registered];
                LoadableXmlAsset asset;

                sources.TryGetValue(node, out asset);
                currentFile = asset == null ? null : asset.FullFilePath;

                // Registration reports duplicate names, which are findings in their own right, so it runs
                // inside the replay like everything else. Attributed to the file being registered.
                captured.Clear();
                UILogReplay.Begin(Note);

                try
                {
                    XmlInheritance.TryRegister(node, asset == null ? null : asset.mod);
                }
                finally
                {
                    UILogReplay.End();
                }

                Index(node, asset);
                Record(node, asset, false);

                registered++;

                if (clock.Elapsed.TotalMilliseconds >= budgetMs)
                    return;
            }

            currentFile = "resolving inheritance";

            captured.Clear();
            UILogReplay.Begin(Note);

            try
            {
                XmlInheritance.Resolve();
            }
            finally
            {
                UILogReplay.End();
            }

            // Resolution failures belong to no single file: a broken ParentName is a relationship between two
            // of them, and the message names the node. Filed without a path rather than blamed on whichever
            // node happened to be last.
            Record(null, null, false);

            state = XmlBugHuntState.Scanning;
        }

        /// <summary>
        /// Reads one definition and files whatever the parser said about it.
        /// </summary>
        private static void ScanOne(XmlNode node)
        {
            LoadableXmlAsset asset;

            sources.TryGetValue(node, out asset);
            currentFile = asset == null ? null : asset.FullFilePath;

            captured.Clear();

            bool fatal = false;

            UILogReplay.Begin(Note);

            try
            {
                XmlNode resolved = XmlInheritance.GetResolvedNodeFor(node);

                // Class on the node names the type to build, exactly as DefFromNode reads it, so a def using a
                // custom class is checked against that class and not against the element name.
                XmlAttribute declared = resolved.Attributes == null ? null : resolved.Attributes["Class"];
                string typeName = declared == null ? node.Name : declared.Value;

                Type type = GenTypes.GetTypeInAnyAssembly(typeName);

                if (type == null || !GenTypes.IsDef(type))
                {
                    Note(true, "Type " + typeName + " is not a Def type or could not be found.");
                }
                else
                {
                    // doPostLoad false. See the note on this class: PostLoad on a BuildableDef queues graphic
                    // resolution, and a scan that loaded every texture in the game would be a worse problem
                    // than the one it was asked to find.
                    DirectXmlToObject.GetObjectFromXmlMethod(type)(node, false);
                }
            }
            catch (Exception ex)
            {
                // The parser only throws when it cannot recover at all, which means this definition did not
                // load. Worth saying in those words rather than as one more message among the field ones.
                fatal = true;
                Note(true, "This definition could not be read at all.\n\n" + ex);
            }
            finally
            {
                UILogReplay.End();

                // Every Def-typed field registers a wanted cross-reference as it is read, and those pile up in
                // a static list that the game treats as "a load is in progress". Cleared per definition rather
                // than at the end so the flag is never left standing over a scan that takes a minute.
                DirectXmlCrossRefLoader.Clear();
            }

            Record(node, asset, fatal);
        }

        /// <summary>Notes which file a definition came from, under every name it can be referred to by.</summary>
        private static void Index(XmlNode node, LoadableXmlAsset asset)
        {
            if (asset == null)
                return;

            XmlNode named = node["defName"];

            if (named != null && !named.InnerText.NullOrEmpty())
                owners[named.InnerText] = asset;

            XmlAttribute inheritance = node.Attributes == null ? null : node.Attributes["Name"];

            if (inheritance != null && !inheritance.Value.NullOrEmpty())
                owners[inheritance.Value] = asset;
        }

        /// <summary>
        /// The definition an unattributed message is about, and the file it lives in.
        ///
        /// Both are read out of the node the message quotes. Vanilla's inheritance errors end with
        /// <c>Full node: &lt;ThingDef ParentName="..." Name="..."&gt;&lt;defName&gt;...</c>, which carries
        /// everything needed; the alternative was a card saying "unknown file", which is the one thing this
        /// whole feature exists to stop saying.
        /// </summary>
        private static LoadableXmlAsset Owner(string text, out string defName)
        {
            defName = Between(text, "<defName>", "</defName>");

            // The leading space is load bearing: ParentName="Cooler" contains Name=" as a substring, so
            // matching without it reads the parent's name and files the fault against the parent's mod.
            string key = defName ?? Between(text, " Name=\"", "\"");

            LoadableXmlAsset asset = null;

            if (!key.NullOrEmpty())
                owners.TryGetValue(key, out asset);

            return asset;
        }

        private static string Between(string text, string opening, string closing)
        {
            int start = text.IndexOf(opening, StringComparison.Ordinal);

            if (start < 0)
                return null;

            start += opening.Length;

            int end = text.IndexOf(closing, start, StringComparison.Ordinal);

            if (end < 0)
                return null;

            string found = text.Substring(start, end - start).Trim();

            return found.NullOrEmpty() ? null : found;
        }

        /// <summary>Collects one diverted log line. Handed to <see cref="UILogReplay"/> for the duration.</summary>
        private static void Note(bool error, string text)
        {
            if (!text.NullOrEmpty())
                captured.Add(new Logged { Error = error, Text = text });
        }

        /// <summary>
        /// Turns whatever was captured into findings against one definition.
        /// </summary>
        private static void Record(XmlNode node, LoadableXmlAsset asset, bool fatal)
        {
            if (captured.Count == 0)
                return;

            brokenDefs++;

            // Inheritance resolution is the one caller with no node to name: a broken ParentName is a
            // relationship between two definitions rather than a fault in either, and the message says which
            // names were involved. Labelled for what it is instead of showing as an unnamed definition.
            string defType = node == null ? "XML inheritance" : node.Name;
            string defName = null;

            if (node != null)
            {
                XmlNode named = node["defName"];
                defName = named == null ? null : named.InnerText;
            }

            int kept = 0;

            foreach (Logged line in captured)
            {
                if (kept >= MaxPerDef)
                {
                    suppressed += captured.Count - kept;

                    break;
                }

                if (findings.Count >= MaxFindings)
                {
                    suppressed += captured.Count - kept;

                    break;
                }

                string field;
                string snippet;

                Locate(line.Text, out field, out snippet);

                // With no node to blame, the message is asked which definition it is about and the index turns
                // that into a file. Only ever a fallback: when the node is known, it is the truth.
                LoadableXmlAsset source = asset;
                string named = defName;

                if (source == null)
                {
                    string fromMessage;

                    source = Owner(line.Text, out fromMessage);
                    named = named ?? fromMessage;
                }

                findings.Add(new BugFinding
                {
                    Fatal = fatal,
                    Error = line.Error,
                    DefType = defType,
                    DefName = named,
                    Field = field,
                    Message = Shorten(line.Text),
                    Detail = line.Text,
                    Snippet = snippet,
                    Path = source == null ? null : source.FullFilePath,
                    Mod = source == null || source.mod == null ? null : source.mod.Name
                });

                kept++;
            }

            captured.Clear();
        }

        /// <summary>
        /// The field a message is about, and the XML it quoted, when either can be told from the wording.
        ///
        /// <b>Read out of the text because the text is all there is.</b> The parser reports through
        /// <c>Log.Error</c> from inside a recursive descent, with nothing carried along that says which field it
        /// was on, so the message is the only handle. Each pattern below is one specific call site in
        /// <c>DirectXmlToObject</c> or <c>XmlToObjectUtils</c>, matched on its own wording.
        ///
        /// <b>Deliberately narrow, and it gives up rather than guesses.</b> Naming the wrong field sends
        /// somebody to edit a line that is fine, which is worse than admitting the message did not say. Anything
        /// unrecognised comes back with no field and is shown as it arrived.
        /// </summary>
        private static void Locate(string text, out string field, out string snippet)
        {
            field = null;
            snippet = null;

            if (text.NullOrEmpty())
                return;

            // "XML ThingDef defines the same field twice: stackLimit." The whole definition is quoted further
            // down the message, so this has to be tested before anything that reads the first element it finds.
            const string twice = "defines the same field twice: ";

            int at = text.IndexOf(twice, StringComparison.Ordinal);

            if (at >= 0)
            {
                field = Until(text, at + twice.Length, '.');

                return;
            }

            // "Attempt to use string stacklimit to refer to field stackLimit in type ThingDef". The name the
            // file used is the one worth reporting: that is the text to go and correct.
            const string attempt = "Attempt to use string ";

            at = text.IndexOf(attempt, StringComparison.Ordinal);

            if (at >= 0)
            {
                field = Until(text, at + attempt.Length, ' ');

                return;
            }

            // "Exception parsing <stackLimit>7.5</stackLimit> to type System.Int32", and "XML error:
            // <stakLimit>7</stakLimit> doesn't correspond to any field in type ThingDef". Both quote the field
            // node itself as the first thing in the message, which is the case worth the most here: a bad value
            // and a misspelled tag are the two faults nobody can find by reading.
            if (text.StartsWith("Exception parsing <", StringComparison.Ordinal)
                || text.StartsWith("XML error: <", StringComparison.Ordinal))
            {
                int open = text.IndexOf('<');

                field = Until(text, open + 1, '>', ' ', '/');
                snippet = Element(text, open);
            }
        }

        /// <summary>The text from <paramref name="start"/> up to the first of <paramref name="stops"/>.</summary>
        private static string Until(string text, int start, params char[] stops)
        {
            if (start < 0 || start >= text.Length)
                return null;

            int end = text.IndexOfAny(stops, start);

            if (end < 0)
                end = text.Length;

            string found = text.Substring(start, end - start).Trim();

            return found.NullOrEmpty() ? null : found;
        }

        /// <summary>
        /// The complete element beginning at <paramref name="open"/>, so a card can show the offending line as
        /// it appears in the file.
        ///
        /// Bounded by the closing tag, and gives up if it is not found within a reasonable distance. A field
        /// whose value is itself a long nested block is not a snippet, and the message is shown in full anyway.
        /// </summary>
        private static string Element(string text, int open)
        {
            if (open < 0)
                return null;

            string name = Until(text, open + 1, '>', ' ', '/');

            if (name.NullOrEmpty())
                return null;

            string closing = "</" + name + ">";

            int end = text.IndexOf(closing, open, StringComparison.Ordinal);

            if (end < 0)
            {
                // A self-closing or empty element, which is a real case: an empty tag where a number belongs.
                end = text.IndexOf('>', open);

                return end < 0 ? null : text.Substring(open, end - open + 1);
            }

            int length = end + closing.Length - open;

            return length > 300 ? null : text.Substring(open, length);
        }

        /// <summary>
        /// The part of a message worth putting on a card.
        ///
        /// These messages carry the whole surrounding definition after "Context:" or "Whole XML:", which on a
        /// ThingDef is several hundred lines. Useful when copying a report, ruinous in a list. The full text is
        /// kept alongside for exactly that.
        /// </summary>
        private static string Shorten(string text)
        {
            if (text.NullOrEmpty())
                return string.Empty;

            string trimmed = text;

            trimmed = CutAt(trimmed, " Context: ");
            trimmed = CutAt(trimmed, "\n\nWhole XML:");
            trimmed = CutAt(trimmed, "\n\nField contents:");

            // Stack traces below an exception line say where in RimWorld it happened, which is never the
            // question here: the question is which line of XML.
            trimmed = CutAt(trimmed, "\n  at ");

            trimmed = trimmed.Trim();

            return trimmed.Length > MessageBudget
                ? trimmed.Substring(0, MessageBudget).TrimEnd() + " ..."
                : trimmed;
        }

        private static string CutAt(string text, string marker)
        {
            int at = text.IndexOf(marker, StringComparison.Ordinal);

            return at < 0 ? text : text.Substring(0, at);
        }

        /// <summary>Stops the hunt where it is. What has been found so far stays listed.</summary>
        internal static void Cancel()
        {
            if (Running)
                Finish(XmlBugHuntState.Cancelled);
        }

        /// <summary>
        /// Ends a run and gives back everything borrowed from the game's static state.
        ///
        /// Reached from cancellation and from a fault as well as from finishing, which is the point of it being
        /// one method: leaving <c>XmlInheritance</c> populated would make the next real def load see nodes from
        /// a document that no longer exists.
        /// </summary>
        private static void Finish(XmlBugHuntState reached)
        {
            state = reached;
            currentFile = null;

            UIGuard.Try("Diagnostics.BugHuntFinish", () =>
            {
                UILogReplay.End();
                XmlInheritance.Clear();
                DirectXmlCrossRefLoader.Clear();
            }, "Restart RimWorld before loading a save, as a precaution.");

            queue = new List<XmlNode>();
            sources = null;

            // The findings already carry the paths they needed from this, and it holds an entry for every
            // definition in the scope. Nothing reads it once the run is over.
            owners.Clear();
        }

        /// <summary>Clears the last run's results, so a second hunt does not append to the first.</summary>
        private static void Reset()
        {
            findings.Clear();
            captured.Clear();
            owners.Clear();
            queue = new List<XmlNode>();
            sources = null;
            index = 0;
            registered = 0;
            total = 0;
            suppressed = 0;
            brokenDefs = 0;
            failure = null;
            currentFile = null;
            state = XmlBugHuntState.Idle;
        }

        /// <summary>Drops the results. Called when the workbench closes, with the document itself.</summary>
        internal static void Release()
        {
            if (Running)
                Finish(XmlBugHuntState.Cancelled);

            Reset();
        }
    }
}
