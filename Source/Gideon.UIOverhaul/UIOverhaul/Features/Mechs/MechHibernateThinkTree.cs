using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// Puts <see cref="JobGiver_MechHibernate"/> into the mech think tree, in front of the idle branch.
    ///
    /// <b>In code rather than in an XML patch, for the reason <c>VerminHuntThinkTree</c> already records.</b>
    /// A <c>PatchOperationAdd</c> into <c>Mechanoid.xml</c> would be fine right up until the day this
    /// assembly fails to load: RimWorld would then meet a think node naming a class that does not exist, fail
    /// to resolve the mechanoid tree, and every mechanoid in the game would stop thinking, hostile ones
    /// included. Done here it is inside a guard, and the worst case is that mechs wander the way they always
    /// have.
    ///
    /// <b>Anchored on the idle branch, which is the node it has to beat.</b> The tail of the mech tree is a
    /// <c>ThinkNode_ConditionalPlayerControlledMech</c> wrapping a tagger wrapping
    /// <c>JobGiver_WanderColony</c>. Inserting in front of that whole branch lands after every work mode
    /// branch and before the wander, which is exactly the slot where "there was nothing to do" has just been
    /// established. The giver re-checks the work mode itself, so it does not matter that this sits outside
    /// the work mode conditionals.
    ///
    /// <b>Matched on the outermost branch, and the descent stops there.</b> Every ancestor of the wander node
    /// also contains one, so a search that kept descending would insert a copy at each level.
    ///
    /// <b>The save key is assigned rather than left unresolved.</b> Think nodes get their keys during def
    /// loading, and a node inserted afterwards keeps the sentinel that means "never assigned".
    /// <c>ThinkTreeKeyAssigner.AssignSingleKey</c> is the game's own public method for this case and takes
    /// the collision set into account.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MechHibernateThinkTree
    {
        static MechHibernateThinkTree()
        {
            UIGuard.Try("Mechs.HibernateThinkTree", Insert,
                "Mech hibernation is unavailable this session. Mechs wander between jobs the way they do "
                + "without this mod, and nothing else about them changes.");
        }

        private static void Insert()
        {
            // Nothing to hook when Biotech is not loaded: there are no player mechs, no work modes and no
            // mechanoid tree worth walking.
            if (!ModsConfig.BiotechActive)
                return;

            List<ThinkTreeDef> trees = DefDatabase<ThinkTreeDef>.AllDefsListForReading;

            for (int i = 0; trees != null && i < trees.Count; i++)
            {
                ThinkTreeDef tree = trees[i];

                if (tree != null && tree.thinkRoot != null)
                    Walk(tree, tree.thinkRoot);
            }
        }

        /// <summary>
        /// Finds the outermost branch holding the player mech idle wander and inserts ours in front of it.
        ///
        /// <b>Every tree, not the mechanoid one by name.</b> The node this anchors to is what defines the
        /// right place, so a mod that builds its own mech shaped tree gets the same treatment and a tree with
        /// no such branch is left alone rather than guessed at. Iterating a copy means the insertion cannot
        /// disturb the walk.
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

                JobGiver_MechHibernate giver = new JobGiver_MechHibernate { parent = node };

                ThinkTreeKeyAssigner.AssignSingleKey(giver, tree.defName.GetHashCode());

                children.Insert(at, giver);
            }
        }

        /// <summary>
        /// Whether this node is the player mech idle branch, or anything wrapped around one.
        ///
        /// Two conditions, both needed. <c>ThinkNode_ConditionalPlayerControlledMech</c> alone appears three
        /// times in the tree and only one of those is the idle branch; <c>JobGiver_WanderColony</c> alone
        /// also appears inside the combat mech patrol branch, which must keep patrolling. The pair of them,
        /// with the conditional on the outside and no work mode conditional in between, is the idle branch
        /// and nothing else.
        /// </summary>
        private static bool Anchor(ThinkNode node)
        {
            if (!(node is ThinkNode_ConditionalPlayerControlledMech))
                return false;

            ThinkNode_Conditional conditional = node as ThinkNode_Conditional;

            // The tree also carries an inverted copy, for mechs nobody controls, which fights hostiles. Ours
            // is the one that fires for the player's own.
            if (conditional != null && conditional.invert)
                return false;

            return Wanders(node, 0);
        }

        /// <summary>
        /// Whether a wander sits under this node without a work mode conditional in the way.
        ///
        /// The depth cap is not defensive dressing: it is what keeps this from matching the outer player mech
        /// conditional that wraps every work mode branch, since the patrol wander lives several levels down
        /// inside one of those. The idle branch is a conditional over a tagger over the wander, so two levels
        /// is the whole of it.
        /// </summary>
        private static bool Wanders(ThinkNode node, int depth)
        {
            if (node == null || depth > 2)
                return false;

            if (node is JobGiver_WanderColony)
                return true;

            if (node is ThinkNode_ConditionalWorkMode)
                return false;

            List<ThinkNode> children = node.subNodes;

            for (int i = 0; children != null && i < children.Count; i++)
            {
                if (Wanders(children[i], depth + 1))
                    return true;
            }

            return false;
        }

        /// <summary>Whether this list already carries our giver, so a second pass adds nothing.</summary>
        private static bool Already(List<ThinkNode> children)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is JobGiver_MechHibernate)
                    return true;
            }

            return false;
        }
    }
}
