using System;
using System.Collections.Generic;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Reading one line of a RimWorld save, and the tables that say what a line means.
    ///
    /// <b>Shared so the reader and the writer cannot disagree.</b> The scan decides what is wrong with a save and
    /// the sweep rewrites it; if each had its own idea of where a record's def sits, the sweep would remove
    /// something the scan never counted. One parser makes that class of bug impossible, and it means the oracle
    /// that validated the scan validates the sweep as well.
    ///
    /// <b>Line oriented, and that is a deliberate bet on RimWorld's own writer.</b> <c>ScribeSaver</c> configures
    /// <c>XmlWriterSettings</c> with <c>Indent = true</c> and <c>IndentChars = "\t"</c>, so a save is one element
    /// per line with depth expressed as tabs. Callers check that bet rather than assume it.
    ///
    /// <b>Nothing here knows about the game</b>, which is what lets both halves be run against a save from a test
    /// harness with no RimWorld loaded.
    /// </summary>
    internal static class SaveSweepXml
    {
        /// <summary>How deep the ancestor stack goes. Real saves sit at around fifteen.</summary>
        internal const int MaxDepth = 64;

        /// <summary>
        /// Which load id namespace a record belongs to, keyed by the list element that holds it.
        ///
        /// <b>Two lists can share one namespace, and that is where the collisions hide.</b> <c>endogenes</c> and
        /// <c>xenogenes</c> are both the <c>Gene_</c> namespace, so a gene numbered the same in each is a genuine
        /// collision that grouping per list cannot see. On the specimen this was built against, that one fact
        /// accounts for 27 of the 28 gene collisions.
        ///
        /// <b>Absence means silence, not safety.</b> A list that is not named here contributes no findings at all.
        /// That bias is deliberate: under-reporting leaves an error in place, while over-reporting invites the
        /// player to rewrite a save that was never broken.
        /// </summary>
        private static readonly Dictionary<string, string> Namespaces =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "hediffs", "Hediff" },
                { "endogenes", "Gene" },
                { "xenogenes", "Gene" },
                { "abilities", "Ability" },
                { "necromancyAbilities", "Ability" },
                { "verbs", "Verb" },
                { "bills", "Bill" },
                { "allFactions", "Faction" },
                { "battles", "Battle" },
                { "jobs", "Job" },
                { "ships", "TransportShip" },
                { "lords", "Lord" },
                { "groups", "StorageGroup" },
                { "quests", "Quest" },
                { "tales", "Tale" },
                { "ideos", "Ideo" }
            };

        /// <summary>
        /// Which kind of def a record's <c>def</c> line has to name, keyed by the list element holding the record.
        ///
        /// <b>The same element name means different things in different places</b>, which is why this is keyed by
        /// container rather than by anything on the line itself. A <c>def</c> under <c>hediffs</c> is a
        /// <c>HediffDef</c>; the identical line under <c>plants</c> is a <c>ThingDef</c>. Resolving one against the
        /// wrong database would report every def in the save as missing.
        ///
        /// <b>Only lists whose def kind is certain appear here.</b> <c>values</c> and <c>entities</c> are left out
        /// because their contents were not established, and guessing would turn a healthy save into a list of
        /// hundreds of imaginary faults.
        /// </summary>
        private static readonly Dictionary<string, string> DefKinds =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "thing", "ThingDef" },
                { "things", "ThingDef" },
                { "plants", "ThingDef" },
                { "buildings", "ThingDef" },
                { "items", "ThingDef" },
                { "innerList", "ThingDef" },
                { "pawnsAlive", "ThingDef" },
                { "pawnsMothballed", "ThingDef" },
                { "pawnsDead", "ThingDef" },
                { "hediffs", "HediffDef" },
                { "allTraits", "TraitDef" },
                { "memories", "ThoughtDef" },
                { "endogenes", "GeneDef" },
                { "xenogenes", "GeneDef" },
                { "abilities", "AbilityDef" },
                { "needs", "NeedDef" },
                { "skills", "SkillDef" },
                { "tales", "TaleDef" }
            };

        /// <summary>
        /// Whether removing records of this kind changes only broken data.
        ///
        /// <b>The distinction the window draws its two groups along.</b> A hediff, trait, thought or gene whose def
        /// has gone is discarded by RimWorld during load anyway, with an error logged for each, so removing it first
        /// changes nothing except the errors. A missing <c>ThingDef</c> is different in degree: the thing also fails
        /// to load, but it might be a pawn, and a player deserves to be told that before agreeing to it.
        /// </summary>
        internal static bool Discarded(string kind)
        {
            switch (kind)
            {
                case "HediffDef":
                case "TraitDef":
                case "ThoughtDef":
                case "GeneDef":
                case "AbilityDef":
                case "NeedDef":
                case "SkillDef":
                case "TaleDef":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The load key each policy database's records identify themselves by.
        ///
        /// <b>Keyed by the database section, not by the list inside it.</b> The drug database's list is called
        /// <c>policies</c>, which is a name anything could use; the section name is unique. Both are available at a
        /// record, so using the stricter one costs nothing.
        ///
        /// <b>The keys come from the game rather than from a guess.</b> <c>Policy.GetUniqueLoadID</c> returns
        /// <c>LoadKey_label_id</c>, and each subclass supplies its own key. The save's section names are the older
        /// spellings kept for compatibility, which is why <c>outfitDatabase</c> pairs with <c>ApparelPolicy</c>.
        /// </summary>
        private static readonly Dictionary<string, string> PolicyKeys =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "foodRestrictionDatabase", "FoodPolicy" },
                { "drugPolicyDatabase", "DrugPolicy" },
                { "outfitDatabase", "ApparelPolicy" },
                { "readingPolicyDatabase", "ReadingPolicy" }
            };

        /// <summary>The load key for records held in this database section, or null when it is not one.</summary>
        internal static bool TryPolicyKey(string section, out string key)
        {
            key = null;

            return section != null && PolicyKeys.TryGetValue(section, out key);
        }

        /// <summary>
        /// A policy's own unique id, built exactly as the game builds it.
        ///
        /// Returns null when either half is missing, because a record that cannot be identified must not be judged
        /// unused and removed.
        /// </summary>
        internal static string PolicyId(string key, string label, string id)
        {
            if (key == null || label == null || string.IsNullOrEmpty(id))
                return null;

            return key + "_" + label + "_" + id;
        }

        /// <summary>Whether this value is something a policy assignment would hold.</summary>
        internal static bool IsPolicyReference(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (KeyValuePair<string, string> pair in PolicyKeys)
            {
                if (value.Length > pair.Value.Length && value[pair.Value.Length] == '_'
                    && value.StartsWith(pair.Value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether records under this list element carry a def of a kind that can be resolved.</summary>
        internal static bool TryDefKind(string list, out string kind)
        {
            kind = null;

            return list != null && DefKinds.TryGetValue(list, out kind);
        }

        /// <summary>The load id namespace records under this list element belong to, or null when it is not one.</summary>
        internal static bool TryNamespace(string list, out string space)
        {
            space = null;

            return list != null && Namespaces.TryGetValue(list, out space);
        }

        /// <summary>
        /// Whether this line is a container naming one of the things it holds.
        ///
        /// <b>A <c>ThingOwner</c> written in reference mode is the one place an <c>li</c> is certainly a load id.</b>
        /// Its contents go out as one id per line under <c>innerList</c>, and nothing else in a save uses that
        /// element name, so the parent alone settles it. That narrowness is what makes this safe when
        /// <see cref="CanReference"/> has to refuse every other <c>li</c> in the file.
        ///
        /// <b>Why it matters more than the other references.</b> A corpse holds its pawn this way, because the pawn
        /// itself is saved among the dead world pawns. Clear that reference or remove what it names and
        /// <c>Corpse.Bugged</c> is true, at which point <c>SpawnSetup</c> logs and returns before the corpse is
        /// registered on the map at all. Unlike a relationship or a combat log entry, this reference is not optional.
        /// </summary>
        internal static bool IsContentsReference(string name, int depth, string[] ancestors)
        {
            if (name != "li" || ancestors == null || depth < 1 || depth - 1 >= ancestors.Length)
                return false;

            return ancestors[depth - 1] == "innerList";
        }

        /// <summary>How many tabs the line is indented by, which is its depth in the document.</summary>
        internal static int Depth(string line)
        {
            int tabs = 0;

            while (tabs < line.Length && line[tabs] == '\t')
                tabs++;

            return tabs;
        }

        internal static bool IsClose(string line)
        {
            int at = line.IndexOf('<');

            return at >= 0 && at + 1 < line.Length && line[at + 1] == '/';
        }

        /// <summary>
        /// Whether this line opens an element that can contain others, which is what belongs on the ancestor stack.
        ///
        /// Three kinds of line are excluded, and each would corrupt the stack: a close tag, a self closing tag such
        /// as an IsNull placeholder, and a one line element carrying its own value and close tag.
        /// </summary>
        internal static bool Opens(string line)
        {
            if (IsClose(line))
                return false;

            int close = line.LastIndexOf('>');

            if (close <= 0 || line[close - 1] == '/')
                return false;

            return line.IndexOf("</", StringComparison.Ordinal) < 0;
        }

        /// <summary>
        /// The element name on this line, or null when the line is not a lone tag.
        ///
        /// Attributes end the name, so <c>&lt;li Class="Pawn"&gt;</c> reads as <c>li</c>. A line holding a value as
        /// well as its tags still reports its name, which is what lets the def and id lines be recognized.
        /// </summary>
        internal static string ElementName(string line)
        {
            int at = line.IndexOf('<');

            if (at < 0)
                return null;

            int start = at + 1;

            if (start < line.Length && line[start] == '/')
                start++;

            int end = start;

            while (end < line.Length && line[end] != '>' && line[end] != ' ' && line[end] != '/')
                end++;

            return end > start ? line.Substring(start, end - start) : null;
        }

        /// <summary>The text between this line's tags, or null when there is none.</summary>
        internal static string Value(string line)
        {
            int open = line.IndexOf('>');
            int close = line.LastIndexOf('<');

            return open >= 0 && close > open + 1 ? line.Substring(open + 1, close - open - 1) : null;
        }

        internal static bool Numeric(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Which kind of def this line names, or null when the line is not a record's own def.
        ///
        /// Same fixed depth rule as <see cref="UniqueId"/>, and for the same reason: a def two levels below its list
        /// belongs to that list's records, while anything deeper belongs to something nested and would be resolved
        /// against the wrong database.
        /// </summary>
        internal static string DefKind(string line, string name, int depth, string[] ancestors)
        {
            if (name != "def" || IsClose(line))
                return null;

            string list = Parent(depth, ancestors);

            return list != null && DefKinds.TryGetValue(list, out string kind) ? kind : null;
        }

        /// <summary>
        /// The load id this line declares, named the way the game names it, or null when the line declares none.
        ///
        /// <b>Three element names, and XML is case sensitive.</b> Things write <c>id</c>, most records write
        /// <c>loadID</c>, and an ability writes <c>Id</c> with a capital I. Checking for only one of them silently
        /// misses the others, which is how an earlier version of this scan called a save with 45 broken records
        /// perfectly clean.
        ///
        /// <b>The stored value is usually not the id.</b> A hediff, gene or ability stores a bare integer and its
        /// type prepends a prefix at run time, so the game reports <c>Hediff_670</c> for a file that only says
        /// <c>670</c>. Searching a save for the id quoted in an error message therefore finds nothing at all. Verbs
        /// are the exception and store an already prefixed value, which the game then prefixes a second time.
        ///
        /// <b>A Thing is the one record needing no context.</b> Its id is already unique across the whole save, as
        /// in <c>Slate400596</c> or <c>Human1122</c>, so it is taken verbatim wherever it appears. That same element
        /// name also holds per database counters which are not load ids at all: outfits, drug policies, food
        /// restrictions and reading policies each number their contents from one, so ten healthy databases look like
        /// ten duplicates. Being numeric is what tells the two apart.
        ///
        /// <b>The namespace lookup is fixed depth on purpose.</b> A record's id sits exactly two levels below its
        /// list, as the list, then <c>li</c>, then the id, so the answer is the ancestor at <c>depth - 2</c> and
        /// nowhere else. Searching further up the stack attributes a quest part's nested id to the quest itself,
        /// which invented 15 collisions the game had never complained about.
        /// </summary>
        /// <param name="space">
        /// The namespace the id belongs to, or null for a Thing id, which carries no prefix. A repair needs this to
        /// know which pool to draw a replacement from.
        /// </param>
        /// <param name="raw">The value exactly as the file stores it, before any prefix is applied.</param>
        internal static string UniqueId(string line, string name, int depth, string[] ancestors,
            out string space, out string raw)
        {
            space = null;
            raw = null;

            if (name == null || IsClose(line))
                return null;

            bool thingStyle = name == "id";

            if (!thingStyle && name != "loadID" && name != "Id")
                return null;

            string value = Value(line);

            if (string.IsNullOrEmpty(value))
                return null;

            if (thingStyle && !Numeric(value))
            {
                raw = value;

                return value;
            }

            string list = Parent(depth, ancestors);

            if (list == null || !Namespaces.TryGetValue(list, out string found))
                return null;

            space = found;
            raw = value;

            return found + "_" + value;
        }

        /// <summary>Namespaces a reference value may name, which is every one records are collected under.</summary>
        private static readonly HashSet<string> ReferenceSpaces = BuildReferenceSpaces();

        private static HashSet<string> BuildReferenceSpaces()
        {
            HashSet<string> spaces = new HashSet<string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> pair in Namespaces)
                spaces.Add(pair.Value);

            // Things are not in the namespace table, because a Thing writes its own id rather than being numbered
            // inside a list. They are still referred to constantly, so the space has to be recognized here.
            spaces.Add("Thing");

            return spaces;
        }

        /// <summary>
        /// Whether an element of this name can hold a reference to another record.
        ///
        /// <b><c>li</c> is excluded here and judged by <see cref="IsListReference"/> instead.</b> A list element
        /// holds anything at all, so it needs the extra context that test has and this one does not. The split is
        /// also a difference in what a repair may do: an element named here is pointed at nothing, while a list
        /// entry is removed outright, and those are not interchangeable.
        /// </summary>
        internal static bool CanReference(string name)
        {
            return name != null && name != "id" && name != "loadID" && name != "Id" && name != "li";
        }

        /// <summary>
        /// Whether this <c>li</c> holds a reference that can be judged, and dropped when it is broken.
        ///
        /// <b>This used to be refused outright, and the measurement that justified refusing it has expired.</b>
        /// Treating every <c>li</c> as a reference once produced 431 that resolved against 70,988 that did not, and
        /// the sensible answer at the time was to read none of them. What has changed since is
        /// <see cref="Target"/>: it now demands a namespace this scan actually collects and, for a Thing, an id
        /// shaped like a def name with its number attached. Re-measured against that on a 46 MB save, list entries
        /// give 1,343 references that resolve against 11 that do not, and all 11 are genuinely broken.
        ///
        /// <b>A dictionary is written as two parallel lists, and that is the one place this must not reach.</b>
        /// <c>keys</c> and <c>values</c> are matched by position, so removing an entry from one without the other
        /// shifts every pair after it. Nine of those 11 broken entries are dead factions sitting in a
        /// <c>keys</c> list, and repairing them would have been far worse than leaving them: RimWorld drops an
        /// unresolved dictionary entry as a pair on its own, which is exactly the operation this cannot perform.
        ///
        /// <b>Anywhere else, a list is as valid one entry shorter.</b> A pawn that no longer exists leaves a
        /// relationship record's reference set; a thing that no longer exists leaves a container. Both are states
        /// the game reaches by itself the moment it fails to resolve the id, so writing them into the file only
        /// saves it the failure.
        /// </summary>
        internal static bool IsListReference(string name, int depth, string[] ancestors)
        {
            if (name != "li" || ancestors == null || depth < 1 || depth - 1 >= ancestors.Length)
                return false;

            string list = ancestors[depth - 1];

            // An unknown parent means the position in the document is not established, which is not a footing to
            // remove anything from.
            return list != null && list != "keys" && list != "values";
        }

        /// <summary>
        /// The defined id this value refers to, or null when the value is not a reference this can judge.
        ///
        /// <b>A Thing is referred to by a different string than it is defined by.</b> Its definition writes the
        /// bare <c>Human2168</c> while every reference to it writes <c>Thing_Human2168</c>, so the prefix has to be
        /// taken off before the lookup. Missing that reported 2,499 of one save's healthy pawn references as broken.
        ///
        /// <b>Judged only when the namespace is one we collect.</b> A save is full of <c>Precept_1120</c> and
        /// <c>WorldObject_721</c>, which are real references to records this scan never gathers, so it cannot know
        /// whether they resolve and must not guess. Anything outside the known namespaces returns null.
        ///
        /// <b>Two shapes are rejected even inside a known namespace.</b> Every namespace but Thing numbers its
        /// records, so a non-numeric remainder such as <c>Ability_VPE_Darkvision_Thing_Human255822</c> is something
        /// this does not understand. And a Thing id ends in digits attached to a letter, which rejects a mod's
        /// composite <c>Thing_Human518670_0_Smash_Managed</c>: 829 of those looked broken until the shape was
        /// checked.
        /// </summary>
        internal static string Target(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "null")
                return null;

            int cut = value.IndexOf('_');

            if (cut <= 0 || cut + 1 >= value.Length)
                return null;

            string space = value.Substring(0, cut);
            string rest = value.Substring(cut + 1);

            if (!ReferenceSpaces.Contains(space))
                return null;

            if (space == "Thing")
                return ThingShaped(rest) ? rest : null;

            return Numeric(rest) ? value : null;
        }

        /// <summary>Whether this looks like a ThingID: a def name with its number attached to the end.</summary>
        private static bool ThingShaped(string value)
        {
            int at = value.Length;

            while (at > 0 && char.IsDigit(value[at - 1]))
                at--;

            return at < value.Length && at > 0 && char.IsLetter(value[at - 1]);
        }

        /// <summary>
        /// The list element holding the record this line belongs to, two levels up, or null when out of range.
        /// </summary>
        internal static string Parent(int depth, string[] ancestors)
        {
            if (ancestors == null || depth < 2 || depth - 2 >= ancestors.Length)
                return null;

            return ancestors[depth - 2];
        }
    }
}
