using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.DevTools
{
    /// <summary>
    /// Finds the thing a developer action is about, so a branch of them can be drawn with pictures.
    ///
    /// <b>Why this has to guess.</b> A <c>DebugActionNode</c> carries a label and a delegate and nothing else.
    /// The node for spawning a wooden club knows it spawns a wooden club only inside a closure the game built;
    /// there is no def on it to read. So the only thing available to match on is the label, which is the def's
    /// own <c>label</c> because that is what the game names these nodes after.
    ///
    /// <b>Guessing is acceptable here and would not be elsewhere.</b> The worst outcome of a wrong match is the
    /// wrong picture beside a correct name, on a developer tool, and the action still runs through vanilla's own
    /// dispatch either way: nothing here decides what happens, only what is drawn. A wrong match cannot spawn
    /// the wrong thing.
    ///
    /// <b>Ambiguous labels resolve to nothing rather than to one of the candidates.</b> Several defs can share a
    /// label, and picking whichever the database happened to list first would put a random picture on a card and
    /// look like a bug in the icon rather than a tie in the data. A card with no picture is honest.
    ///
    /// <b>Built once, on first use, and never rebuilt.</b> Def labels do not change after startup, and this is
    /// asked once per visible card per frame.
    /// </summary>
    internal static class DevActionArt
    {
        /// <summary>
        /// How much of a branch has to resolve before it is drawn as cards.
        ///
        /// <b>A proportion, because a partial match is worse than either extreme.</b> A grid where two cards in
        /// twenty carry art reads as broken art rather than as a grid of names, so the choice is made for the
        /// branch as a whole. Three quarters is high enough that the odd unmatched name looks like a gap in the
        /// game's own data and low enough that one renamed def does not cost a whole branch its pictures.
        /// </summary>
        private const float CardThreshold = 0.75f;

        /// <summary>
        /// Fewest children a branch needs before cards are worth it.
        ///
        /// Below this the list is short enough to read at a glance and a grid of large cards is just further to
        /// move the mouse.
        /// </summary>
        private const int CardMinimum = 8;

        private static Dictionary<string, ThingDef> byLabel;

        private static bool failed;

        /// <summary>
        /// The def a label names, or null when nothing or more than one thing does.
        ///
        /// The dictionary holds a null value for an ambiguous label rather than dropping the key, so a second def
        /// arriving with the same label poisons the entry instead of being ignored.
        /// </summary>
        internal static ThingDef Resolve(string label)
        {
            if (label.NullOrEmpty())
                return null;

            Build();

            if (byLabel == null)
                return null;

            return byLabel.TryGetValue(label.Trim(), out ThingDef found) ? found : null;
        }

        /// <summary>
        /// Whether this list of children should be drawn as cards rather than as rows.
        ///
        /// Asked of the whole branch at once, for the reason on <see cref="CardThreshold"/>. A branch holding
        /// sub-branches is never cards: drilling further is a different gesture from picking a thing, and a
        /// folder does not have a picture.
        /// </summary>
        internal static bool SuitsCards(List<DevAction> children)
        {
            if (children == null || children.Count < CardMinimum)
                return false;

            int resolved = 0;

            foreach (DevAction action in children)
            {
                if (action.Branch)
                    return false;

                if (Resolve(action.Label) != null)
                    resolved++;
            }

            return resolved >= children.Count * CardThreshold;
        }

        private static void Build()
        {
            if (byLabel != null || failed)
                return;

            Dictionary<string, ThingDef> map = UIGuard.Try("DevTools.IndexArt", () =>
            {
                Dictionary<string, ThingDef> built =
                    new Dictionary<string, ThingDef>(StringComparer.OrdinalIgnoreCase);

                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (def == null || def.label.NullOrEmpty())
                        continue;

                    string key = def.label.Trim();

                    // Present already means two defs answer to this label, so it is set to null and stays null.
                    // See the class note: a random pick would look like a broken icon rather than a tie.
                    if (built.ContainsKey(key))
                    {
                        built[key] = null;

                        continue;
                    }

                    built[key] = def;
                }

                return built;
            }, null, "Developer action lists are drawn without pictures. Nothing else is affected.");

            if (map == null)
            {
                failed = true;

                return;
            }

            byLabel = map;
        }
    }
}
