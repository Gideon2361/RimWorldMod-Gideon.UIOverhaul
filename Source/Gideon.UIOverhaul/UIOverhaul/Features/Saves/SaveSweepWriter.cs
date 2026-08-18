using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>What the player asked the sweep to do.</summary>
    internal sealed class SaveSweepOptions
    {
        /// <summary>Remove things, buildings, plants and pawns whose <c>ThingDef</c> is no longer installed.</summary>
        internal bool RemoveMissingThings = true;

        /// <summary>
        /// Remove hediffs, traits, thoughts, genes and abilities whose def is gone.
        ///
        /// Separate from the above because these are the ones RimWorld discards on load anyway, so removing them
        /// changes nothing except the errors.
        /// </summary>
        internal bool RemoveDiscardedRecords = true;

        /// <summary>Give a record that collided with an earlier load id a fresh one.</summary>
        internal bool RenumberDuplicates = true;

        /// <summary>
        /// Point references at nothing when what they named is not in the file.
        ///
        /// Written as the literal <c>null</c>, which is what the scribe itself writes for an empty reference, so the
        /// game gets the same answer it already computes today without having to fail to find anything first.
        /// </summary>
        internal bool RepairDangling = true;

        /// <summary>Remove dead pawn records.</summary>
        internal bool RemoveDeadPawns;

        /// <summary>
        /// Remove mothballed world pawns that nothing refers to.
        ///
        /// Which ones qualify is decided by the scan, not here: see
        /// <see cref="SaveSweepReport.RemovableMothballed"/>.
        /// </summary>
        internal bool RemoveMothballed;

        /// <summary>Remove the history graphs and the play log.</summary>
        internal bool RemoveHistory;

        /// <summary>
        /// Remove food, drug, apparel and reading policies no pawn is assigned to.
        ///
        /// Which ones qualify is decided by the scan: see <see cref="SaveSweepReport.RemovablePolicies"/>.
        /// </summary>
        internal bool RemoveUnusedPolicies;
    }

    /// <summary>What the sweep did, or would do.</summary>
    internal sealed class SaveSweepOutcome
    {
        /// <summary>False for a dry run, and false for a failure. <see cref="Problem"/> tells the two apart.</summary>
        internal bool Wrote;

        internal string Path;
        internal long BytesRemoved;
        internal int RecordsRemoved;
        internal int Renumbered;

        /// <summary>How many references were pointed at nothing because what they named was gone.</summary>
        internal int Repaired;

        /// <summary>How many records went, by the reason they went. What the window itemizes.</summary>
        internal Dictionary<string, int> RemovedByReason = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The bytes those records occupied, by the same reasons.</summary>
        internal Dictionary<string, long> BytesByReason = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>Set when the sweep could not finish. Null on success.</summary>
        internal string Problem;

        internal int Changes => RecordsRemoved + Renumbered;
    }

    /// <summary>
    /// Writes a repaired copy of a save.
    ///
    /// <b>It never touches the original, and that is not a setting.</b> There is no code path here that opens the
    /// source for writing. The output is a new file the player chooses to load, and until they do, nothing about
    /// their colony has changed. Aaron asked for exactly this: "We need extensive testing before we overwrite live
    /// data without a backup."
    ///
    /// <b>Dropping a record means dropping a balanced subtree.</b> A removable record is buffered from its opening
    /// tag to its matching close, then either written out or discarded whole. Because the unit is always a complete
    /// element, the result cannot be malformed XML no matter which options are on, and a record nested inside a
    /// removed one goes with its parent for free.
    ///
    /// <b>Buffering is bounded by the largest record, not the file.</b> Frames only open where a record starts, so
    /// the worst case is one pawn in memory at a time rather than a 47 MB document.
    ///
    /// <b>A repair never reuses an id.</b> Fresh ids are drawn from above the highest the scan saw in that
    /// namespace, so a renumbered record cannot collide with anything, including another renumbered record. That
    /// invariant is what makes the output safe to load even when the input was badly broken.
    ///
    /// <b>Renumbering keeps existing references pointing where they already pointed.</b> Today the second record to
    /// claim an id fails to register at all, so every reference to that id resolves to the first one. Moving the
    /// second record preserves that exactly, and additionally lets the moved record register properly. References
    /// inside the moved record's own subtree are rewritten, since those unambiguously belong to it; nothing outside
    /// it is touched, because a reference to a duplicated id cannot be attributed to one claimant or the other.
    ///
    /// <b>The output is plain XML even when the input was compressed.</b> A swept copy exists to be inspected and
    /// loaded once; writing it uncompressed keeps this class free of the compression path entirely, and the game
    /// reads either.
    /// </summary>
    internal static class SaveSweepWriter
    {
        /// <summary>Sections removed wholesale when the history option is on.</summary>
        private static readonly HashSet<string> HistorySections =
            new HashSet<string>(StringComparer.Ordinal) { "history", "playLog", "battleLog" };

        /// <summary>The element names a removable record can go by.</summary>
        private static readonly HashSet<string> RecordNames =
            new HashSet<string>(StringComparer.Ordinal) { "li", "thing" };

        /// <summary>One buffered element, held until it is known whether it survives.</summary>
        private sealed class Frame
        {
            internal int Depth;
            internal string Name;
            internal string List;
            internal string Kind;
            internal bool Drop;
            internal string Reason;
            internal long Bytes;
            internal readonly List<string> Lines = new List<string>();

            /// <summary>An id this record moved, so references inside it can follow. Null when it moved nothing.</summary>
            internal string OldToken;

            internal string NewToken;

            /// <summary>
            /// For a policy record, the load key of its database plus the two halves of its own id.
            ///
            /// A policy is identified by key, label and number together, and those arrive on separate lines, so the
            /// decision waits until the record closes.
            /// </summary>
            internal string PolicyKey;

            internal string PolicyLabel;

            internal string PolicyNumber;
        }

        /// <summary>
        /// Counts what a sweep would change, writing nothing.
        ///
        /// The window calls this so the footer can state the outcome before the player commits, and it runs the
        /// identical walk the real thing does. Two code paths that could disagree about what is about to happen
        /// would make the confirmation worthless.
        /// </summary>
        internal static SaveSweepOutcome Preview(string source, SaveSweepOptions options, SaveSweepReport report,
            Dictionary<string, HashSet<string>> missing)
        {
            return UIGuard.Try("Saves.Sweep.Preview",
                () => Walk(source, null, options, report, missing), Failed("The save could not be examined."),
                "The sweep could not be estimated. Nothing was changed.");
        }

        /// <summary>Writes the repaired copy to <paramref name="target"/>.</summary>
        internal static SaveSweepOutcome Write(string source, string target, SaveSweepOptions options,
            SaveSweepReport report, Dictionary<string, HashSet<string>> missing)
        {
            return UIGuard.Try("Saves.Sweep.Write",
                () => Twice(source, target, options, report, missing),
                Failed("The cleaned copy could not be written."),
                "No cleaned copy was written. The original save is untouched.");
        }

        /// <summary>
        /// Removes first, then clears the references that removal just broke.
        ///
        /// <b>One pass cannot do both, and a single pass quietly made things worse.</b> Whether a reference dangles
        /// depends on what the finished file contains, so the set is computed before any record is dropped. Removing
        /// 100 dead pawns that memorials and combat logs still name then left 94 fresh dangling references in the
        /// output: a tool whose purpose is removing broken references was manufacturing them.
        ///
        /// So when something was actually removed, the output is scanned again and the references to whatever went
        /// are cleared in a second pass. The invariant that buys is worth the extra walk: <b>a swept file never
        /// refers to anything it does not contain</b>, whichever options were chosen.
        ///
        /// <b>The original is still never touched.</b> The intermediate is a sibling of the target, and the target
        /// is only ever written, never moved onto or deleted.
        /// </summary>
        private static SaveSweepOutcome Twice(string source, string target, SaveSweepOptions options,
            SaveSweepReport report, Dictionary<string, HashSet<string>> missing)
        {
            if (!options.RepairDangling)
                return Walk(source, target, options, report, missing);

            string part = target + ".part";

            Discard(part);

            SaveSweepOutcome first = Walk(source, part, options, report, missing);

            if (first.Problem != null)
            {
                Discard(part);

                return first;
            }

            // Nothing was removed, so nothing new can dangle and the first pass is already the answer.
            if (first.RecordsRemoved == 0)
            {
                Move(part, target);
                first.Path = target;

                return first;
            }

            SaveSweepReport after = SaveSweepScan.Scan(part);

            if (!after.Shaped || after.DanglingIds.Count == 0)
            {
                Move(part, target);
                first.Path = target;

                return first;
            }

            // Only the reference repair this time. Removing again would find nothing and cost another walk.
            SaveSweepOutcome second = Walk(part, target, new SaveSweepOptions
            {
                RemoveMissingThings = false,
                RemoveDiscardedRecords = false,
                RenumberDuplicates = false,
                RepairDangling = true,
                RemoveDeadPawns = false,
                RemoveMothballed = false,
                RemoveHistory = false
            }, after, null);

            Discard(part);

            if (second.Problem != null)
                return second;

            first.Repaired += second.Repaired;
            first.Path = target;
            first.Wrote = true;

            return first;
        }

        private static void Move(string from, string to)
        {
            Discard(to);
            File.Move(from, to);
        }

        private static void Discard(string path)
        {
            if (path != null && File.Exists(path))
                File.Delete(path);
        }

        private static SaveSweepOutcome Failed(string problem)
        {
            return new SaveSweepOutcome { Problem = problem };
        }

        private static SaveSweepOutcome Walk(string source, string target, SaveSweepOptions options,
            SaveSweepReport report, Dictionary<string, HashSet<string>> missing)
        {
            SaveSweepOutcome outcome = new SaveSweepOutcome { Path = target };

            if (options == null || report == null || !report.Shaped)
            {
                outcome.Problem = "The save was not in the expected shape, so nothing was changed.";

                return outcome;
            }

            StreamWriter writer = null;

            try
            {
                if (target != null)
                {
                    // A BOM and CRLF, because that is what RimWorld's own writer produces and a swept copy should
                    // be indistinguishable from one the game wrote.
                    writer = new StreamWriter(target, false, new UTF8Encoding(true)) { NewLine = "\r\n" };
                }

                Sweep(source, writer, options, report, missing, outcome);

                outcome.Wrote = target != null;
            }
            finally
            {
                writer?.Dispose();
            }

            return outcome;
        }

        private static void Sweep(string source, StreamWriter writer, SaveSweepOptions options,
            SaveSweepReport report, Dictionary<string, HashSet<string>> missing, SaveSweepOutcome outcome)
        {
            List<Frame> stack = new List<Frame>();
            string[] ancestors = new string[SaveSweepXml.MaxDepth];
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            
            // Newlines go BEFORE each line after the first, never after the last. RimWorld ends a save at the closing
            // tag with nothing following it, so a trailing newline would make even a no-op sweep differ from its input.
            bool started = false;
            Dictionary<string, int> next = new Dictionary<string, int>(report.HighestId, StringComparer.Ordinal);

            using (StreamReader reader = SaveArchive.OpenReader(source))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    int depth = SaveSweepXml.Depth(line);
                    string name = SaveSweepXml.ElementName(line);
                    bool closing = SaveSweepXml.IsClose(line);

                    // Closing the innermost buffered element settles it, one way or the other.
                    if (closing && stack.Count > 0)
                    {
                        Frame top = stack[stack.Count - 1];

                        if (depth == top.Depth && name == top.Name)
                        {
                            Add(top, line);
                            stack.RemoveAt(stack.Count - 1);

                            // A policy can only be identified once its whole record has been seen, since its id is
                            // built from its label as well as its number.
                            if (top.PolicyKey != null && !top.Drop)
                            {
                                string built =
                                    SaveSweepXml.PolicyId(top.PolicyKey, top.PolicyLabel, top.PolicyNumber);

                                if (built != null && report.RemovablePolicies.Contains(built))
                                {
                                    top.Drop = true;
                                    top.Reason = "Unused policies and filters";
                                }
                            }

                            if (top.Drop)
                            {
                                // No adjustment for nested removals is needed, and adding one was a bug worth
                                // recording: a record removed from inside this one never had its lines added to
                                // this frame's buffer, so this frame's byte total already excludes them. Its
                                // bytes and theirs are disjoint, and each removed record is counted exactly once
                                // under the rule that removed it.
                                outcome.RecordsRemoved++;
                                outcome.BytesRemoved += top.Bytes;
                                Tally(outcome, top.Reason, top.Bytes);
                            }
                            else
                            {
                                Emit(stack, writer, ref started, top.Lines);
                            }

                            continue;
                        }
                    }

                    // A record's own id, which is where a collision is repaired.
                    string id = SaveSweepXml.UniqueId(line, name, depth, ancestors, out string space, out string raw);

                    if (id != null)
                    {
                        if (!seen.Add(id) && options.RenumberDuplicates && space != null
                            && SaveSweepXml.Numeric(raw) && stack.Count > 0)
                        {
                            int fresh = Allocate(next, space);
                            Frame owner = stack[stack.Count - 1];

                            owner.OldToken = space + "_" + raw;
                            owner.NewToken = space + "_" + fresh;

                            line = WithValue(line, fresh.ToString());

                            seen.Add(space + "_" + fresh);
                            outcome.Renumbered++;
                        }
                    }

                    line = Follow(stack, line, name, outcome);

                    // A reference to something the file does not contain. Pointed at nothing rather than removed,
                    // because the element itself is usually required and its absence would mean something else.
                    if (options.RepairDangling && !closing && report.DanglingIds.Count > 0
                        && SaveSweepXml.CanReference(name))
                    {
                        string aim = SaveSweepXml.Target(SaveSweepXml.Value(line));

                        if (aim != null && report.DanglingIds.Contains(aim))
                        {
                            line = WithValue(line, "null");
                            outcome.Repaired++;
                        }
                    }

                    // A record's own def, which is where a missing one is caught. Read before any frame opens for
                    // this line, because a def line never opens a frame.
                    if (stack.Count > 0 && name == "def" && !closing)
                    {
                        Frame owner = stack[stack.Count - 1];

                        if (depth == owner.Depth + 1 && owner.Kind != null && !owner.Drop)
                            Judge(owner, SaveSweepXml.Value(line), options, missing);
                    }

                    // A policy record gives up its number and its label on separate lines; both are kept for the
                    // decision taken when the record closes.
                    if (stack.Count > 0 && !closing)
                    {
                        Frame owner = stack[stack.Count - 1];

                        if (owner.PolicyKey != null && depth == owner.Depth + 1)
                        {
                            if (name == "id")
                                owner.PolicyNumber = SaveSweepXml.Value(line);
                            else if (name == "label")
                                owner.PolicyLabel = SaveSweepXml.Value(line);
                        }
                    }

                    // A mothballed pawn nothing refers to. Which ones qualify was decided by the scan, which is the
                    // only place that can know: whether anything names this pawn depends on the whole file.
                    if (stack.Count > 0 && name == "id" && !closing && options.RemoveMothballed)
                    {
                        Frame owner = stack[stack.Count - 1];

                        if (depth == owner.Depth + 1 && owner.List == "pawnsMothballed" && !owner.Drop
                            && report.RemovableMothballed.Contains(SaveSweepXml.Value(line) ?? string.Empty))
                        {
                            owner.Drop = true;
                            owner.Reason = "Mothballed world pawns";
                        }
                    }

                    if (!closing && SaveSweepXml.Opens(line))
                    {
                        Frame opened = Open(line, name, depth, ancestors, options);

                        if (name != null && depth < SaveSweepXml.MaxDepth)
                            ancestors[depth] = name;

                        if (opened != null)
                        {
                            stack.Add(opened);
                            Add(opened, line);

                            continue;
                        }
                    }

                    if (stack.Count > 0)
                        Add(stack[stack.Count - 1], line);
                    else
                        WriteLine(writer, ref started, line);
                }
            }

            // An unclosed frame means the file ended mid record, which the scan's shape check should already have
            // caught. Flushing rather than discarding keeps a truncated input from losing more than it arrived with.
            for (int i = 0; i < stack.Count; i++)
                Emit(null, writer, ref started, stack[i].Lines);
        }

        /// <summary>Whether this line starts an element the sweep might remove, and a frame to hold it if so.</summary>
        private static Frame Open(string line, string name, int depth, string[] ancestors, SaveSweepOptions options)
        {
            if (name == null)
                return null;

            if (depth == 2 && options.RemoveHistory && HistorySections.Contains(name))
            {
                return new Frame
                {
                    Depth = depth, Name = name, List = "game", Drop = true, Reason = "History and logs"
                };
            }

            if (!RecordNames.Contains(name) || depth < 1 || depth - 1 >= ancestors.Length)
                return null;

            string list = ancestors[depth - 1];

            // A policy record. Keyed off the database section rather than the list, since the drug database's list
            // is called "policies" and that is a name anything could use.
            if (options.RemoveUnusedPolicies && depth >= 2
                && SaveSweepXml.TryPolicyKey(ancestors[depth - 2], out string policy))
            {
                return new Frame
                {
                    Depth = depth,
                    Name = name,
                    List = list,
                    PolicyKey = policy
                };
            }

            if (list == null || !SaveSweepXml.TryDefKind(list, out string kind))
                return null;

            bool dead = list == "pawnsDead" && options.RemoveDeadPawns;

            return new Frame
            {
                Depth = depth,
                Name = name,
                List = list,
                Kind = kind,
                Drop = dead,
                Reason = dead ? "Dead pawn records" : null
            };
        }

        /// <summary>Marks a record for removal when its def is one of the missing ones.</summary>
        private static void Judge(Frame frame, string def, SaveSweepOptions options,
            Dictionary<string, HashSet<string>> missing)
        {
            if (string.IsNullOrEmpty(def) || def == "null" || missing == null)
                return;

            if (!missing.TryGetValue(frame.Kind, out HashSet<string> gone) || !gone.Contains(def))
                return;

            bool discarded = SaveSweepXml.Discarded(frame.Kind);

            if (discarded ? !options.RemoveDiscardedRecords : !options.RemoveMissingThings)
                return;

            frame.Drop = true;
            frame.Reason = frame.Kind + " no longer installed";
        }

        /// <summary>
        /// Rewrites a reference that belongs to a record whose id has just moved.
        ///
        /// Only <c>loadID</c> and <c>ability</c> values are considered, and only inside the moving record, because
        /// those are the two places a verb records the ability it belongs to. Anything broader would start guessing.
        /// </summary>
        private static string Follow(List<Frame> stack, string line, string name, SaveSweepOutcome outcome)
        {
            if (name != "loadID" && name != "ability")
                return line;

            for (int i = stack.Count - 1; i >= 0; i--)
            {
                Frame frame = stack[i];

                if (frame.OldToken == null)
                    continue;

                string value = SaveSweepXml.Value(line);

                if (value == null || !value.StartsWith(frame.OldToken, StringComparison.Ordinal))
                    continue;

                // Guards against Ability_424 matching Ability_4240, which is a different ability entirely.
                if (value.Length > frame.OldToken.Length && char.IsDigit(value[frame.OldToken.Length]))
                    continue;

                // A loadID here is the nested record's own id, derived from its owner's, so it has genuinely been
                // renumbered and is counted as such. An <ability> line is a reference and is not.
                if (name == "loadID")
                    outcome.Renumbered++;

                return WithValue(line, frame.NewToken + value.Substring(frame.OldToken.Length));
            }

            return line;
        }

        private static int Allocate(Dictionary<string, int> next, string space)
        {
            int value = next.TryGetValue(space, out int high) ? high + 1 : 1;

            next[space] = value;

            return value;
        }

        /// <summary>The same line with a different value between its tags, indentation and name preserved.</summary>
        private static string WithValue(string line, string value)
        {
            int open = line.IndexOf('>');
            int close = line.LastIndexOf('<');

            if (open < 0 || close <= open)
                return line;

            return line.Substring(0, open + 1) + value + line.Substring(close);
        }

        private static void Add(Frame frame, string line)
        {
            frame.Lines.Add(line);
            frame.Bytes += line.Length + SaveSweepScan.NewlineBytes;
        }

        /// <summary>Hands surviving lines to the enclosing record, or to the file when there is none.</summary>
        private static void Emit(List<Frame> stack, StreamWriter writer, ref bool started, List<string> lines)
        {
            if (stack != null && stack.Count > 0)
            {
                Frame parent = stack[stack.Count - 1];

                for (int i = 0; i < lines.Count; i++)
                    Add(parent, lines[i]);

                return;
            }

            if (writer == null)
                return;

            for (int i = 0; i < lines.Count; i++)
                WriteLine(writer, ref started, lines[i]);
        }

        /// <summary>
        /// Writes one line, placing the newline before it rather than after.
        ///
        /// <b>So that a sweep which changes nothing produces a byte for byte copy.</b> RimWorld ends a save at its
        /// closing tag with nothing following, and a writer that appends a newline per line leaves a trailing one
        /// the original never had. Harmless to the parser, but it makes the output differ from its input for no
        /// reason, and "identical unless something was actually repaired" is a property worth being able to check.
        /// </summary>
        private static void WriteLine(StreamWriter writer, ref bool started, string line)
        {
            if (writer == null)
                return;

            if (started)
                writer.Write("\r\n");

            writer.Write(line);

            started = true;
        }

        private static void Tally(SaveSweepOutcome outcome, string reason, long bytes)
        {
            string key = reason ?? "Removed";

            outcome.RemovedByReason[key] =
                outcome.RemovedByReason.TryGetValue(key, out int count) ? count + 1 : 1;

            outcome.BytesByReason[key] =
                outcome.BytesByReason.TryGetValue(key, out long held) ? held + bytes : bytes;
        }
    }
}
