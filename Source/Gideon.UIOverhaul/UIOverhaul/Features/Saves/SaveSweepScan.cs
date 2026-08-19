using System;
using System.Collections.Generic;
using System.IO;
using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>One thing the scan found, and what it costs.</summary>
    internal sealed class SaveSweepFinding
    {
        internal string Key;
        internal string Label;
        internal int Count;
        internal long Bytes;
    }

    /// <summary>
    /// What a save file is made of.
    ///
    /// <b>Bytes are the least of it.</b> The point of the sweep is what the game keeps paying for at run time:
    /// records walked every tick, references that fail to resolve and are retried, ids held in lookup tables for
    /// the life of the colony. Size is the one part that can be measured from the file alone, so it is what this
    /// reports; the cost each finding carries is stated in the window, next to the number.
    /// </summary>
    internal sealed class SaveSweepReport
    {
        internal string Path;
        internal long FileBytes;
        internal long ScannedBytes;

        /// <summary>Where the weight is: the depth-2 sections under <c>game</c>.</summary>
        internal List<SaveSweepFinding> Sections = new List<SaveSweepFinding>();

        /// <summary>What the sweep could remove.</summary>
        internal List<SaveSweepFinding> Reclaimable = new List<SaveSweepFinding>();

        /// <summary>
        /// How many records claimed a load id that an earlier record had already taken. Zero is the healthy answer.
        ///
        /// This is the true count. <see cref="DuplicateIds"/> holds only the first
        /// <see cref="SaveSweepScan.MaxExamples"/> of them, so on a badly broken save the two disagree and the
        /// window has to read this one.
        /// </summary>
        internal int Duplicates;

        /// <summary>
        /// The colliding ids, named the way the game names them, capped at
        /// <see cref="SaveSweepScan.MaxExamples"/> entries.
        /// </summary>
        internal List<string> DuplicateIds = new List<string>();

        /// <summary>How many load ids the scan could attribute to a namespace.</summary>
        internal int LoadIds;

        /// <summary>
        /// The largest numeric id seen in each namespace.
        ///
        /// <b>What a repair has to start from.</b> Giving a colliding record a fresh id means picking one nothing
        /// else uses, and the only safe floor is above everything already present. Namespaces whose ids are not
        /// numeric, such as verbs, are absent: those cannot be renumbered directly and are instead carried along by
        /// the record that owns them.
        /// </summary>
        internal Dictionary<string, int> HighestId = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Every def name the save mentions, grouped by the kind of def it has to be.
        ///
        /// <b>Collected here and resolved elsewhere.</b> This class knows nothing about the game, which is what
        /// lets it be run against a save from a test harness. Whether a def still exists is a question only the
        /// loaded game can answer, so <see cref="SaveSweepDefs"/> takes these names to the def database.
        /// </summary>
        internal Dictionary<string, HashSet<string>> DefNames =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>Every unique load id the save defines. What a reference has to be found in to resolve.</summary>
        internal HashSet<string> DefinedIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Ids that something refers to but nothing defines.
        ///
        /// Each one is a resolve that fails on load, and some are retried by their owner rather than dropped, so
        /// they are paid for repeatedly rather than once.
        /// </summary>
        internal HashSet<string> DanglingIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>How many times a dangling id is referred to, which is more than the number of distinct ids.</summary>
        internal int DanglingReferences;

        /// <summary>References that did resolve. Shown beside the broken ones so the number has a denominator.</summary>
        internal int ResolvedReferences;

        /// <summary>
        /// Mothballed world pawns that nothing refers to and that belong to no player faction.
        ///
        /// <b>This is the relationship test, and it is deliberately stricter than one.</b> Rather than reading a
        /// pawn's relations list, it asks whether anything anywhere in the save names that pawn: a family tie, a
        /// quest, an ideo, a memory, a corpse. A pawn nothing names cannot be missed by anything. That also makes it
        /// conservative in the right direction, since a clique of mothballed pawns referring only to each other
        /// keeps all of them.
        ///
        /// The corpse in that list is found separately, through <see cref="SaveSweepXml.IsContentsReference"/>,
        /// because a container names its contents with an <c>li</c> and the general reference sweep cannot read
        /// those. Before that was added the claim above was false for exactly the case it mattered most in.
        /// </summary>
        internal HashSet<string> RemovableMothballed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>How many mothballed pawns were held back, and why. Both shown so the row can explain itself.</summary>
        internal int MothballedReferenced;

        internal int MothballedPlayer;

        /// <summary>
        /// Dead world pawns that no container on any map still holds.
        ///
        /// <b>A corpse does not contain its pawn, it points at it.</b> <c>Corpse.innerContainer</c> is a
        /// <c>ThingOwner</c> in reference mode, so the body itself lives among the dead world pawns and the corpse
        /// names it. Remove the record and the corpse loads holding nothing, <c>Corpse.Bugged</c> is true, and
        /// <c>SpawnSetup</c> logs "spawned in bugged state" and returns before registering the thing on the map.
        /// Every corpse lying on the ground, in a grave or in a freezer works this way, so a colony after a raid
        /// can lose dozens at once.
        ///
        /// <b>This is a narrower test than the mothballed one, on purpose.</b> A dead pawn is named all over a save
        /// by relationships, memorials and combat logs, and clearing those is what the repair pass is for; holding
        /// back every pawn anything mentions would make the option do nothing at all. What cannot be cleared is a
        /// container's contents, because a container holding nothing is not a smaller container, it is a broken one.
        /// </summary>
        internal HashSet<string> RemovableDeadPawns = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>How many dead pawns stayed because a corpse names them. What the row explains itself with.</summary>
        internal int DeadPawnsHeld;

        /// <summary>
        /// Food, drug, apparel and reading policies no pawn is assigned to.
        ///
        /// Held as the policy's own unique id, which is how a pawn names the one it uses, so a policy is judged
        /// unused by exact string match rather than by matching a number that each database counts separately.
        /// </summary>
        internal HashSet<string> RemovablePolicies = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>How many policies there are in total, so the row can say how many of them are idle.</summary>
        internal int Policies;

        /// <summary>
        /// Whether the file looked the way this scan expects.
        ///
        /// False means every number here should be discarded rather than shown. See the note on the scanner about
        /// why that check exists.
        /// </summary>
        internal bool Shaped;
    }

    /// <summary>
    /// Reads a save and reports what is in it, without writing anything.
    ///
    /// <b>The bet on the file's shape is checked rather than assumed.</b> If the root and the game element are not
    /// found where <see cref="SaveSweepXml"/> expects them, <see cref="SaveSweepReport.Shaped"/> comes back false
    /// and the caller shows nothing. A wrong number here would be read as a reason to delete something, so a scan
    /// that cannot be trusted has to say so rather than approximate.
    ///
    /// <b>Compression is somebody else's problem.</b> <see cref="SaveArchive.OpenReader"/> hands back a reader over
    /// the plain XML whatever the file is stored as, so this never learns the difference.
    /// </summary>
    internal static class SaveSweepScan
    {
        /// <summary>
        /// Bytes to charge for a line's newline.
        ///
        /// Two, because RimWorld writes CRLF. Being wrong by a byte a line would be about 1% on a save of this
        /// shape, which matters for a report somebody reads as "how much would this save".
        /// </summary>
        internal const int NewlineBytes = 2;

        /// <summary>
        /// How many colliding ids to keep as examples. The count itself is always exact; only the list is capped.
        /// </summary>
        internal const int MaxExamples = 200;

        /// <summary>
        /// The three lists inside <c>worldPawns</c>, which is where the reclaimable weight actually is.
        ///
        /// Mothballed pawns are the large one and the one to be careful with: they are relatives, ex colonists and
        /// faction figures the game deliberately keeps so relationships and quests still resolve. Vanilla has its
        /// own collector for them, so anything removed here is something vanilla decided to keep.
        /// </summary>
        internal static readonly string[] PawnLists = { "pawnsAlive", "pawnsMothballed", "pawnsDead" };

        internal static SaveSweepReport Scan(string path)
        {
            return UIGuard.Try("Saves.Sweep.Scan", () => Read(path), new SaveSweepReport { Path = path },
                "The save could not be examined. Nothing was changed.");
        }

        private static SaveSweepReport Read(string path)
        {
            SaveSweepReport report = new SaveSweepReport { Path = path };

            if (File.Exists(path))
                report.FileBytes = new FileInfo(path).Length;

            Dictionary<string, long> sections = new Dictionary<string, long>(StringComparer.Ordinal);
            Dictionary<string, long> pawnBytes = new Dictionary<string, long>(StringComparer.Ordinal);
            Dictionary<string, int> pawnCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            // Ids are held rather than streamed past, because a duplicate can only be recognized against every id
            // seen so far. A 47 MB save carries about seventy thousand, which is a set worth a few megabytes for the
            // few seconds the scan runs.
            HashSet<string> ids = report.DefinedIds;

            // References cannot be judged while reading, because the record a reference names may not have been
            // reached yet. They are counted here and resolved against the finished set of definitions afterwards.
            Dictionary<string, int> references = new Dictionary<string, int>(StringComparer.Ordinal);

            // Records in each load id namespace, against the ids those records actually wrote. A namespace with
            // more records than ids has one whose id was written by leaving the element out: see below.
            Dictionary<string, int> records = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> written = new Dictionary<string, int>(StringComparer.Ordinal);

            // Dead world pawns, and the things every container still holds. The two are compared at the end to find
            // the dead pawns that are somebody's corpse.
            HashSet<string> dead = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> contained = new HashSet<string>(StringComparer.Ordinal);

            // Mothballed pawns, the faction each belongs to, and which faction is the player's.
            Dictionary<string, string> mothballed = new Dictionary<string, string>(StringComparer.Ordinal);
            string mothCurrent = null;
            int mothDepth = -1;
            string playerFaction = null;
            string factionDef = null;
            string factionId = null;

            // Policies, and the assignments that name them. A policy record gives up its id and its label on two
            // separate lines, so both are held until the record ends and its unique id can be built.
            // Grouped by database, and in the order the file lists them, because a database must never be emptied
            // and the survivor has to be a predictable one.
            Dictionary<string, List<string>> policies =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            HashSet<string> assigned = new HashSet<string>(StringComparer.Ordinal);
            string policyKey = null;
            string policyId = null;
            string policyLabel = null;
            int policyDepth = -1;

            // The open element at each depth, which is how a record's namespace and def kind are found. Entries
            // deeper than the line being read are stale and never consulted, because the lookup only ever reaches
            // two levels up.
            string[] ancestors = new string[SaveSweepXml.MaxDepth];

            string section = null;
            string pawnList = null;
            bool sawGame = false;

            using (StreamReader reader = SaveArchive.OpenReader(path))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    long bytes = line.Length + NewlineBytes;
                    report.ScannedBytes += bytes;

                    int depth = SaveSweepXml.Depth(line);
                    string name = SaveSweepXml.ElementName(line);
                    bool closing = SaveSweepXml.IsClose(line);

                    if (depth == 1 && name == "game")
                        sawGame = true;

                    // A depth-2 open tag starts a new section and ends the previous one. Close tags are ignored:
                    // the next open tag is what moves the cursor, and a section's own close line belongs to it.
                    if (depth == 2 && name != null && !closing)
                        section = name;

                    if (section != null)
                        sections[section] = Get(sections, section) + bytes;

                    if (depth == 3 && name == "worldPawns" && !closing)
                        pawnList = null;

                    if (depth == 4 && name != null)
                    {
                        if (closing && name == pawnList)
                            pawnList = null;
                        else if (!closing && Array.IndexOf(PawnLists, name) >= 0)
                            pawnList = name;
                    }

                    if (pawnList != null)
                    {
                        pawnBytes[pawnList] = Get(pawnBytes, pawnList) + bytes;

                        // A record's own def line, at the depth a list element's children sit at. Counting these
                        // rather than <li> avoids counting the apparel and hediffs nested inside each pawn.
                        if (depth == 6 && name == "def")
                            pawnCounts[pawnList] = (pawnCounts.TryGetValue(pawnList, out int n) ? n : 0) + 1;
                    }

                    string id = SaveSweepXml.UniqueId(line, name, depth, ancestors, out string space, out string raw);

                    if (id != null)
                    {
                        report.LoadIds++;

                        if (space != null)
                            written[space] = written.TryGetValue(space, out int had) ? had + 1 : 1;

                        // Tracked even for records that turn out to collide, because a repair has to clear every
                        // id in the namespace, including the ones it is about to move.
                        if (space != null && int.TryParse(raw, out int numeric)
                                          && (!report.HighestId.TryGetValue(space, out int high) || numeric > high))
                        {
                            report.HighestId[space] = numeric;
                        }

                        if (!ids.Add(id))
                        {
                            report.Duplicates++;

                            if (report.DuplicateIds.Count < MaxExamples)
                                report.DuplicateIds.Add(id);
                        }
                    }

                    string holder = SaveSweepXml.Parent(depth, ancestors);

                    if (!closing && holder != null)
                    {
                        string value = SaveSweepXml.Value(line);

                        // A mothballed pawn, and the faction it belongs to, which is a sibling of its id.
                        if (holder == "pawnsMothballed" && name == "id" && !string.IsNullOrEmpty(value))
                        {
                            mothCurrent = value;
                            mothDepth = depth;
                            mothballed[value] = null;
                        }
                        else if (mothCurrent != null && name == "faction" && depth == mothDepth)
                        {
                            mothballed[mothCurrent] = value;
                        }

                        // A dead world pawn. Held as the file writes it, without the Thing_ prefix a reference to it
                        // carries, since that is the form the sweep sees when it decides whether to drop the record.
                        if (holder == "pawnsDead" && name == "id" && !string.IsNullOrEmpty(value))
                            dead.Add(value);

                        // Which faction is the player's. Order of def and loadID is not assumed, so both are held
                        // and the pairing is tested whenever either arrives.
                        if (holder == "allFactions")
                        {
                            if (name == "def")
                                factionDef = value;
                            else if (name == "loadID")
                                factionId = value;

                            if (factionDef != null && factionId != null
                                && factionDef.StartsWith("Player", StringComparison.Ordinal))
                            {
                                playerFaction = "Faction_" + factionId;
                            }
                        }
                    }

                    // A policy record: a new one starts at its <li>, and its id and label arrive as children.
                    if (!closing && SaveSweepXml.TryPolicyKey(section, out string key))
                    {
                        if (name == "li" && depth >= 1 && depth - 1 < ancestors.Length
                            && ancestors[depth - 1] != null && depth == 4)
                        {
                            policyKey = key;
                            policyDepth = depth;
                            policyId = null;
                            policyLabel = null;
                        }
                        else if (policyKey != null && depth == policyDepth + 1)
                        {
                            if (name == "id")
                                policyId = SaveSweepXml.Value(line);
                            else if (name == "label")
                                policyLabel = SaveSweepXml.Value(line);

                            string built = SaveSweepXml.PolicyId(policyKey, policyLabel, policyId);

                            if (built != null)
                            {
                                if (!policies.TryGetValue(policyKey, out List<string> group))
                                {
                                    group = new List<string>();
                                    policies[policyKey] = group;
                                }

                                if (!group.Contains(built))
                                    group.Add(built);
                            }
                        }
                    }

                    // An assignment naming a policy. Any element may hold one, so the value's shape decides.
                    if (!closing && SaveSweepXml.CanReference(name))
                    {
                        string named = SaveSweepXml.Value(line);

                        if (SaveSweepXml.IsPolicyReference(named))
                            assigned.Add(named);
                    }

                    if (!closing && SaveSweepXml.CanReference(name))
                    {
                        string aim = SaveSweepXml.Target(SaveSweepXml.Value(line));

                        if (aim != null)
                            references[aim] = references.TryGetValue(aim, out int seen) ? seen + 1 : 1;
                    }

                    // What a container holds. Kept apart from the references above because these are the ones the
                    // repair pass must not touch, so counting them among the repairable ones would have the window
                    // promise a fix it will not make.
                    if (!closing && SaveSweepXml.IsContentsReference(name, depth, ancestors))
                    {
                        string held = SaveSweepXml.Target(SaveSweepXml.Value(line));

                        if (held != null)
                            contained.Add(held);
                    }

                    // One record in a namespaced list, counted against the ids that list's records write.
                    if (!closing && name == "li" && depth >= 1 && depth - 1 < ancestors.Length
                        && SaveSweepXml.Opens(line) && SaveSweepXml.TryNamespace(ancestors[depth - 1], out string owns))
                    {
                        records[owns] = records.TryGetValue(owns, out int n) ? n + 1 : 1;
                    }

                    // A new faction record clears the pairing so one record's def cannot be read with another's id.
                    if (!closing && name == "li" && depth >= 1 && depth - 1 < ancestors.Length
                        && ancestors[depth - 1] == "allFactions")
                    {
                        factionDef = null;
                        factionId = null;
                    }

                    string kind = SaveSweepXml.DefKind(line, name, depth, ancestors);

                    if (kind != null)
                    {
                        string def = SaveSweepXml.Value(line);

                        if (!string.IsNullOrEmpty(def))
                        {
                            if (!report.DefNames.TryGetValue(kind, out HashSet<string> named))
                            {
                                named = new HashSet<string>(StringComparer.Ordinal);
                                report.DefNames[kind] = named;
                            }

                            named.Add(def);
                        }
                    }

                    if (name != null && depth < SaveSweepXml.MaxDepth && SaveSweepXml.Opens(line))
                        ancestors[depth] = name;
                }
            }

            report.Shaped = sawGame && sections.Count > 0;

            if (!report.Shaped)
                return report;

            // AN ID OF ZERO IS WRITTEN BY LEAVING THE ELEMENT OUT, and missing that silently rewrote a healthy save.
            //
            // Scribe_Values.Look skips an element whose value equals the default it was given, and a load id is
            // scribed as Look(ref loadID, "loadID", 0) by Faction, Ideo, Hediff, Bill and the rest. So the record
            // numbered zero writes no id line at all, this scan never saw it defined, and every reference to it
            // looked broken. On the save this was found against that was Faction_0 and Ideo_0: 96 references, and
            // the repair pass duly pointed all 96 at nothing, which cost 46 pawns their ideoligion and stripped 45
            // factions of their relation to the insect hive. Nothing was wrong with that save.
            //
            // A namespace holding more records than it wrote ids has exactly one such record, since ids are unique
            // and only zero can be omitted. Counted on that save: one each in Faction, Ideo, Hediff, Gene, Lord and
            // Tale, and none at all in Ability or Verb.
            foreach (KeyValuePair<string, int> pair in records)
            {
                if (pair.Value > (written.TryGetValue(pair.Key, out int had) ? had : 0))
                    ids.Add(pair.Key + "_0");
            }

            // Now that every definition is known, a reference can finally be judged.
            foreach (KeyValuePair<string, int> pair in references)
            {
                if (ids.Contains(pair.Key))
                {
                    report.ResolvedReferences += pair.Value;

                    continue;
                }

                report.DanglingIds.Add(pair.Key);
                report.DanglingReferences += pair.Value;
            }

            // A policy nobody is assigned to. Matched on the whole id, so two databases that each number their
            // contents from one cannot be confused for each other.
            //
            // A DATABASE IS NEVER EMPTIED, and that guard is the difference between a cleaned save and an
            // unloadable one. RimWorld's databases hand out a default by taking the first entry, so a colony that
            // happens to assign no drug policy at all would otherwise have all of them removed and then index an
            // empty list. One entry always survives, and it is the first the file lists so the choice is stable.
            foreach (KeyValuePair<string, List<string>> group in policies)
            {
                report.Policies += group.Value.Count;

                bool anyKept = false;

                foreach (string uid in group.Value)
                {
                    if (assigned.Contains(uid))
                        anyKept = true;
                }

                for (int i = 0; i < group.Value.Count; i++)
                {
                    if (assigned.Contains(group.Value[i]))
                        continue;

                    if (!anyKept && i == 0)
                    {
                        anyKept = true;

                        continue;
                    }

                    report.RemovablePolicies.Add(group.Value[i]);
                }
            }

            // A mothballed pawn goes only if nothing names it and it is nobody's colonist.
            foreach (KeyValuePair<string, string> pawn in mothballed)
            {
                if (playerFaction != null && pawn.Value == playerFaction)
                {
                    report.MothballedPlayer++;

                    continue;
                }

                if (references.ContainsKey(pawn.Key) || contained.Contains(pawn.Key))
                {
                    report.MothballedReferenced++;

                    continue;
                }

                report.RemovableMothballed.Add(pawn.Key);
            }

            // A dead pawn goes only if no container is holding it. See the note on RemovableDeadPawns for why this
            // is the one reference that cannot be cleared instead.
            foreach (string pawn in dead)
            {
                if (contained.Contains(pawn))
                {
                    report.DeadPawnsHeld++;

                    continue;
                }

                report.RemovableDeadPawns.Add(pawn);
            }

            foreach (KeyValuePair<string, long> pair in sections)
                report.Sections.Add(new SaveSweepFinding
                    { Key = pair.Key, Label = pair.Key, Count = 0, Bytes = pair.Value });

            report.Sections.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

            foreach (string list in PawnLists)
            {
                if (!pawnBytes.ContainsKey(list))
                    continue;

                report.Reclaimable.Add(new SaveSweepFinding
                {
                    Key = list,
                    Label = Describe(list),
                    Count = pawnCounts.TryGetValue(list, out int n) ? n : 0,
                    Bytes = pawnBytes[list]
                });
            }

            return report;
        }

        private static string Describe(string list)
        {
            switch (list)
            {
                case "pawnsAlive": return "World pawns still alive";
                case "pawnsMothballed": return "Mothballed world pawns";
                case "pawnsDead": return "Dead pawn records";
                default: return list;
            }
        }

        private static long Get(Dictionary<string, long> map, string key)
        {
            return map.TryGetValue(key, out long value) ? value : 0L;
        }
    }
}
