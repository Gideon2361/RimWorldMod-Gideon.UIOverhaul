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
        /// How much of a branch has to resolve before it is drawn as thing rows.
        ///
        /// <b>A quarter, where the card grid this replaced wanted three quarters.</b> That bar was set because a
        /// grid with two pictures in twenty reads as broken art rather than as a grid of names -- a card is
        /// mostly picture, so a card without one is a hole. A row is mostly text: the name sits in the same
        /// column either way and an unresolved row simply has space where its icon would be, which reads as a
        /// gap in the game's data rather than as a fault in the list.
        ///
        /// Low enough, too, that a branch of modded things still gets the layout when only some of their defs
        /// carry labels this can match -- which is the common case and the reason the layout was asked for. It
        /// stays above zero so that a branch of things that are not <c>ThingDef</c>s at all, weathers and
        /// factions among them, keeps the plain name list it has always had.
        /// </summary>
        private const float CardThreshold = 0.25f;

        /// <summary>
        /// Fewest children a branch needs before cards are worth it.
        ///
        /// Below this the list is short enough to read at a glance and a grid of large cards is just further to
        /// move the mouse.
        /// </summary>
        private const int CardMinimum = 8;

        private static Dictionary<string, ThingDef> byLabel;

        /// <summary>
        /// The same defs again, keyed by <c>defName</c>, for the mods that never gave theirs a label.
        ///
        /// A defName is unique by definition, so unlike <see cref="byLabel"/> this one has no ambiguity to
        /// poison an entry with.
        /// </summary>
        private static Dictionary<string, ThingDef> byDefName;

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

            string key = label.Trim();

            if (byLabel.TryGetValue(key, out ThingDef found) && found != null)
                return found;

            // <b>The defName is the second guess, and on a modded game it is most of them.</b> The game names
            // these nodes after the def's label, but a great many mods never set one -- so the node is called
            // AM_AK101A or AEXP_EggAnaconda, which is a defName wearing a label's place. Vanilla content mostly
            // has real labels and resolves on the first lookup; without this fallback a heavily modded Spawn
            // thing list is a wall of names with no pictures, which is the case that needed it.
            //
            // Second rather than first, because a label is what a reader sees and a defName collision with some
            // other def's label should lose to the label.
            return byDefName != null && byDefName.TryGetValue(key, out ThingDef named) ? named : null;
        }

        /// <summary>
        /// Which mod a def came from, in the words the mod list uses.
        ///
        /// Core content answers "RimWorld" rather than an empty string: the question a reader is asking is "is
        /// this vanilla or is this something I installed", and a blank reads as missing data instead of as an
        /// answer.
        /// </summary>
        internal static string Mod(ThingDef def)
        {
            return UIGuard.Try("DevTools.ArtMod", () =>
            {
                string name = def != null && def.modContentPack != null ? def.modContentPack.Name : null;

                return name.NullOrEmpty() ? "RimWorld" : name;
            }, "RimWorld", null);
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

            Dictionary<string, ThingDef> names =
                new Dictionary<string, ThingDef>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, ThingDef> map = UIGuard.Try("DevTools.IndexArt", () =>
            {
                Dictionary<string, ThingDef> built =
                    new Dictionary<string, ThingDef>(StringComparer.OrdinalIgnoreCase);

                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (def == null)
                        continue;

                    if (!def.defName.NullOrEmpty())
                        names[def.defName] = def;

                    if (def.label.NullOrEmpty())
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
            byDefName = names;
        }
    }
}
