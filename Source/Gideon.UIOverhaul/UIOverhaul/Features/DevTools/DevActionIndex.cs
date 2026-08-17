using System;
using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.DevTools
{
    /// <summary>One thing the developer menu can do, flattened out of the tree it lives in.</summary>
    internal struct DevAction
    {
        public DebugActionNode Node;

        /// <summary>The label as authored. <c>LabelNow</c> is read at draw time, since it can change.</summary>
        public string Label;

        /// <summary>Where it sits, for the badge on the row: the tab, then any categories above it.</summary>
        public string Where;

        /// <summary>The tab it came from, for the rail's counts.</summary>
        public string Tab;

        /// <summary>Whether choosing it drills in rather than doing something.</summary>
        public bool Branch;

        /// <summary>Whether it reads as something that cannot be undone.</summary>
        public bool Destructive;

        /// <summary>Lowercased label and path, so searching does not rebuild these per keystroke.</summary>
        public string Haystack;

        /// <summary>
        /// How deep in the tree it sits. One is a tab's own top level.
        ///
        /// Kept because depth is most of what tells a useful result from a useless one. "Spawn mech cluster" is
        /// depth one; the two thousand point values underneath it are depth two, and listing those alongside it
        /// is what turned an empty query into a wall of numbers.
        /// </summary>
        public int Depth;
    }

    /// <summary>
    /// Every developer action in the game, flattened once so all of them can be searched at once.
    ///
    /// <b>Built on vanilla's own tree rather than a copy of it.</b> <c>Dialog_Debug.TrySetupNodeGraph</c> is
    /// public and builds the roots the real menu uses, one per <c>DebugTabMenuDef</c>; this walks those. So every
    /// action another mod adds appears here with no cooperation from it, and nothing needs to know this exists.
    ///
    /// <b>Lazy branches are deliberately not expanded.</b> A node can carry a <c>childGetter</c> that enumerates
    /// every thing def, every weather, every faction; running all of them to build a search index would be slow,
    /// would allocate a great deal, and several of them read the current map and throw when there is not one.
    /// They are indexed as branches and expanded when the reader actually opens one, which is also when the game
    /// state they depend on is the state the reader is in.
    ///
    /// The practical consequence is worth stating plainly: searching finds <i>Set weather</i>, not <i>Set weather
    /// to Foggy rain</i>. Opening it shows the weathers.
    /// </summary>
    internal static class DevActionIndex
    {
        /// <summary>
        /// Words that mark an action as one to be careful with.
        ///
        /// A heuristic on the label, because nothing in the data says which actions are destructive. It is used
        /// only to add a mark and a confirmation, so a false positive costs one extra click and a false negative
        /// leaves the action exactly as dangerous as it is in vanilla.
        /// </summary>
        private static readonly string[] DangerWords =
        {
            "destroy", "kill", "delete", "remove all", "wipe", "annihilate", "explode", "obliterate"
        };

        private static readonly FieldInfo RootsField = AccessTools.Field(typeof(Dialog_Debug), "roots");

        private static List<DevAction> actions = new List<DevAction>();
        private static bool built;

        internal static List<DevAction> Actions
        {
            get
            {
                Build();

                return actions;
            }
        }

        /// <summary>Tab names in the order the real menu shows them, with how many actions each holds.</summary>
        internal static List<KeyValuePair<string, int>> Tabs { get; private set; }
            = new List<KeyValuePair<string, int>>();

        /// <summary>
        /// Flattens the tree, once per session.
        ///
        /// Not rebuilt as the game state changes: what is <i>visible</i> does change, and that is tested at draw
        /// time through <c>VisibleNow</c>, but which actions exist does not.
        /// </summary>
        internal static void Build()
        {
            if (built)
                return;

            built = true;

            UIGuard.Try("DevTools.BuildIndex", () =>
            {
                Dialog_Debug.TrySetupNodeGraph();

                if (RootsField == null)
                    return;

                Dictionary<DebugTabMenuDef, DebugActionNode> roots =
                    RootsField.GetValue(null) as Dictionary<DebugTabMenuDef, DebugActionNode>;

                if (roots == null)
                    return;

                List<DevAction> found = new List<DevAction>();
                List<KeyValuePair<string, int>> tabs = new List<KeyValuePair<string, int>>();

                foreach (KeyValuePair<DebugTabMenuDef, DebugActionNode> entry in roots)
                {
                    string tab = entry.Key?.label ?? "Other";
                    int before = found.Count;

                    Walk(entry.Value, tab, tab, found, 0);

                    tabs.Add(new KeyValuePair<string, int>(tab, found.Count - before));
                }

                actions = found;
                Tabs = tabs;
            }, "The developer palette has no actions to list.");
        }

        /// <summary>
        /// Adds a node and, where it is already expanded, its children.
        /// </summary>
        /// <param name="depth">Guards against a tree that refers to itself, which a mod can produce.</param>
        private static void Walk(DebugActionNode node, string tab, string where, List<DevAction> into, int depth)
        {
            if (node == null || depth > 8)
                return;

            foreach (DebugActionNode child in node.children)
            {
                if (child == null)
                    continue;

                // Already-populated children only. See the note on the class: running childGetter here is what
                // would make this slow and, on several nodes, throw.
                bool branch = child.children.Count > 0 || child.childGetter != null;

                string label = child.label ?? string.Empty;

                into.Add(new DevAction
                {
                    Node = child,
                    Label = label,
                    Where = where,
                    Tab = tab,
                    Branch = branch,
                    Depth = depth + 1,
                    Destructive = IsDestructive(label),
                    Haystack = (label + " " + where).ToLowerInvariant()
                });

                if (child.children.Count > 0)
                    Walk(child, tab, label, into, depth + 1);
            }
        }

        private static bool IsDestructive(string label)
        {
            if (label.NullOrEmpty())
                return false;

            string lower = label.ToLowerInvariant();

            foreach (string word in DangerWords)
            {
                if (lower.Contains(word))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The actions matching a query, best first.
        ///
        /// <b>Ranked rather than merely filtered.</b> Vanilla hides rows that do not match and leaves the rest
        /// where they were, so the thing you typed for can be anywhere in a column of three hundred. Scoring puts
        /// a label that starts with the query above one that merely contains it, and both above a match that only
        /// hit the category, which is nearly always the order a reader wants.
        /// </summary>
        internal static List<DevAction> Search(string query, string tab, int limit)
        {
            Build();

            List<DevAction> results = new List<DevAction>();
            string needle = (query ?? string.Empty).Trim().ToLowerInvariant();

            // <b>An empty query is a menu, not a dump.</b> Listing every action with nothing typed produced two
            // thousand rows in which the top level was buried under the contents of whatever branch happened to
            // sort first -- a screen of "1000 points", "10000 points", "1050 points". With nothing typed the
            // useful view is the same one the real menu opens on: each tab's own top level, in the order the
            // game put them in. Typing is what reaches deeper.
            if (needle.Length == 0)
            {
                foreach (DevAction action in actions)
                {
                    if (results.Count >= limit)
                        break;

                    if (action.Depth != 1)
                        continue;

                    if (tab.NullOrEmpty() || action.Tab == tab)
                        results.Add(action);
                }

                return results;
            }

            List<KeyValuePair<int, DevAction>> scored = new List<KeyValuePair<int, DevAction>>();

            foreach (DevAction action in actions)
            {
                if (!tab.NullOrEmpty() && action.Tab != tab)
                    continue;

                int score = Score(action, needle);

                if (score < 0)
                    continue;

                scored.Add(new KeyValuePair<int, DevAction>(score, action));
            }

            scored.Sort((a, b) => b.Key != a.Key
                ? b.Key.CompareTo(a.Key)
                : string.Compare(a.Value.Label, b.Value.Label, StringComparison.OrdinalIgnoreCase));

            foreach (KeyValuePair<int, DevAction> entry in scored)
            {
                if (results.Count >= limit)
                    break;

                results.Add(entry.Value);
            }

            return results;
        }

        private static int Score(DevAction action, string needle)
        {
            string label = action.Label?.ToLowerInvariant() ?? string.Empty;

            // Depth is a penalty, and a heavy one. A top level action is nearly always what somebody typing two
            // words meant; the same words matching a leaf four levels down inside a branch almost never are.
            int depth = Mathf.Max(0, action.Depth - 1) * 12;

            if (label.StartsWith(needle, StringComparison.Ordinal))
                return 100 - LengthPenalty(label.Length) - depth;

            if (label.Contains(needle))
                return 60 - LengthPenalty(label.Length) - depth;

            // The category matched but the label did not, which is a weaker answer and ranks below both.
            return action.Haystack != null && action.Haystack.Contains(needle) ? 20 - depth : -1;
        }

        /// <summary>
        /// A small penalty for length, so an exact short label beats a long one that merely contains it.
        /// </summary>
        private static int LengthPenalty(int length)
        {
            return length > 40 ? 10 : length / 4;
        }
    }
}
