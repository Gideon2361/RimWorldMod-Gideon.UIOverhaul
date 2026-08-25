using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Puts <see cref="JobGiver_HuntVermin"/> into the animal think tree, just above idle behavior.
    ///
    /// <b>In code rather than in an XML patch, for the same reason the weapon one is.</b> A
    /// <c>PatchOperationAdd</c> into <c>Animal.xml</c> would be fine right up until the day this assembly fails
    /// to load: RimWorld would then meet a think node naming a class that does not exist, fail to resolve the
    /// animal tree, and every animal in the game would stop thinking. Done here it is inside a guard, and the
    /// worst case is that owls do not patrol.
    ///
    /// <b>Anchored above the tame-animal block, which is where idle behavior starts.</b> The obvious anchor is
    /// the SatisfyBasicNeeds subtree immediately above it, but <c>ThinkNode_Subtree.treeDef</c> is private and
    /// there is no supported way to ask a resolved subtree which tree it came from. <c>JobGiver_Mate</c> is
    /// buried inside the block below and is a public class, so the branch containing it is what gets anchored
    /// to. Inserting in front of that branch lands in the slot between the two: after eating, sleeping and
    /// anything urgent, ahead of mating, nuzzling, roaming and hauling. Hunger still wins, which is correct --
    /// a starving owl should eat, not patrol.
    ///
    /// <b>The outermost match, and then stop.</b> The mating job giver sits two levels down, under a
    /// chance-per-hour gate and a tagger, all of it inside a faction conditional. Every one of those ancestors
    /// "contains a mating node", so a search that kept descending would insert a copy at each level. Matching on
    /// the outermost one and not recursing into it gives exactly one insertion, in the highest of those slots,
    /// which is also the one that reads as a priority rather than as a coin flip.
    ///
    /// That puts it outside the faction conditional the mating block sits in, which costs nothing:
    /// <see cref="JobGiver_HuntVermin"/> tests for the player's faction itself, because it has to distinguish
    /// the colony's animals from a visitor's regardless of where it is called from.
    ///
    /// <b>The save key is assigned rather than left unresolved.</b> Think nodes get their keys during def
    /// loading, and a node inserted afterwards keeps the sentinel that means "never assigned".
    /// <c>ThinkTreeKeyAssigner.AssignSingleKey</c> is the game's own public method for this case and takes the
    /// collision set into account.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class VerminHuntThinkTree
    {
        static VerminHuntThinkTree()
        {
            UIGuard.Try("Animals.HuntThinkTree", Insert,
                "Tame hunting animals will not patrol for vermin this session. They still hunt when hungry, "
                + "the way every predator does.");
        }

        private static void Insert()
        {
            List<ThinkTreeDef> trees = DefDatabase<ThinkTreeDef>.AllDefsListForReading;

            for (int i = 0; trees != null && i < trees.Count; i++)
            {
                ThinkTreeDef tree = trees[i];

                if (tree != null && tree.thinkRoot != null)
                    Walk(tree, tree.thinkRoot);
            }
        }

        /// <summary>
        /// Finds the outermost branch holding a mating node and inserts ours in front of it.
        ///
        /// <b>Every tree, not the animal one by name.</b> The node this anchors to is what defines the right
        /// place, so a mod that builds its own animal-shaped tree gets the same treatment, and a tree with no
        /// mating branch is left alone rather than guessed at. Iterating a copy means the insertion cannot
        /// disturb the walk.
        ///
        /// <b>A match ends the descent.</b> See the class note: every ancestor of the mating node also contains
        /// one, so descending past a match would add a giver at each level down.
        /// </summary>
        private static void Walk(ThinkTreeDef tree, ThinkNode node)
        {
            List<ThinkNode> children = node.subNodes;

            if (children == null || children.Count == 0)
                return;

            List<ThinkNode> snapshot = new List<ThinkNode>(children);

            for (int i = 0; i < snapshot.Count; i++)
            {
                ThinkNode child = snapshot[i];

                if (child == null)
                    continue;

                if (!Anchor(child))
                {
                    Walk(tree, child);
                    continue;
                }

                int at = children.IndexOf(child);

                if (at < 0 || Already(children))
                    continue;

                JobGiver_HuntVermin giver = new JobGiver_HuntVermin { parent = node };

                ThinkTreeKeyAssigner.AssignSingleKey(giver, tree.defName.GetHashCode());

                children.Insert(at, giver);
            }
        }

        /// <summary>
        /// Whether this node is the mating job giver, or anything wrapped around one.
        ///
        /// Vanilla buries it two levels down, under a chance-per-hour gate and then a tagger, so the node
        /// actually sitting in the list is neither of the two things a single-level check would look for. The
        /// search is therefore recursive, and stops at the first hit.
        /// </summary>
        private static bool Anchor(ThinkNode node)
        {
            if (node is RimWorld.JobGiver_Mate)
                return true;

            List<ThinkNode> children = node.subNodes;

            for (int i = 0; children != null && i < children.Count; i++)
            {
                if (children[i] != null && Anchor(children[i]))
                    return true;
            }

            return false;
        }

        /// <summary>Whether this list already has ours, so a second startup pass cannot double it up.</summary>
        private static bool Already(List<ThinkNode> children)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is JobGiver_HuntVermin)
                    return true;
            }

            return false;
        }
    }
}
