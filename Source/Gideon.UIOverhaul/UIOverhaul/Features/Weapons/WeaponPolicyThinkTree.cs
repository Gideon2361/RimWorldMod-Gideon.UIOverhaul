using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Weapons
{
    /// <summary>
    /// Puts <see cref="JobGiver_OptimizeWeapon"/> into the humanlike think tree, beside the apparel one.
    ///
    /// <b>In code rather than in an XML patch, and the reason is the failure mode.</b> A
    /// <c>PatchOperationAdd</c> into <c>Humanlike.xml</c> is the declarative way to do this and would be fine
    /// right up until the day this assembly fails to load: RimWorld would then meet a think node naming a class
    /// that does not exist, fail to resolve the humanlike tree, and every pawn in the game would stop thinking.
    /// Done here it is inside a guard, and the worst case is that colonists do not re-arm themselves.
    ///
    /// <b>Beside the apparel job giver, not merely somewhere in the tree.</b> Position in a think tree is
    /// priority: everything above it wins. Sitting immediately after apparel optimization puts re-arming exactly
    /// where re-dressing already is -- after duties, lord work and anything urgent, before idle behaviour -- which
    /// is the priority a player already understands from watching colonists change clothes.
    ///
    /// <b>The save key is assigned rather than left unresolved.</b> Think nodes get their keys during def
    /// loading, and a node inserted afterwards keeps the sentinel that means "never assigned".
    /// <c>ThinkTreeKeyAssigner.AssignSingleKey</c> is the game's own public method for exactly this case and
    /// takes the collision set into account, so the inserted node cannot take a key another node is already
    /// saving under.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class WeaponPolicyThinkTree
    {
        static WeaponPolicyThinkTree()
        {
            UIGuard.Try("Weapons.ThinkTree", Insert,
                "Colonists will not fetch weapons on their own this session. Weapon policies can still be set "
                + "and still decide what a colonist may be given.");
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
        /// Finds every parent holding an apparel optimizer and inserts ours after it.
        ///
        /// <b>Every tree, not the humanlike one by name.</b> The node this anchors to is what defines the right
        /// place, so a mod that builds its own humanlike-shaped tree gets the same treatment, and a tree without
        /// apparel optimization is left alone rather than guessed at. Iterating the copied list means the
        /// insertion cannot disturb the walk.
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

                Walk(tree, child);

                if (!Anchor(child))
                    continue;

                int at = children.IndexOf(child);

                if (at < 0 || Already(children))
                    continue;

                JobGiver_OptimizeWeapon giver = new JobGiver_OptimizeWeapon { parent = node };

                ThinkTreeKeyAssigner.AssignSingleKey(giver, tree.defName.GetHashCode());

                children.Insert(at + 1, giver);
            }
        }

        /// <summary>
        /// Whether this node is the apparel optimizer, or the tagger wrapped around one.
        ///
        /// Vanilla wraps it in a <c>ThinkNode_Tagger</c> so the resulting job is tagged as changing apparel, and
        /// the tagger is what actually sits in the list -- so anchoring on the job giver alone would never match
        /// in the tree this exists for.
        /// </summary>
        private static bool Anchor(ThinkNode node)
        {
            if (node is RimWorld.JobGiver_OptimizeApparel)
                return true;

            List<ThinkNode> children = node.subNodes;

            for (int i = 0; children != null && i < children.Count; i++)
            {
                if (children[i] is RimWorld.JobGiver_OptimizeApparel)
                    return true;
            }

            return false;
        }

        /// <summary>Whether this list already has ours, so a second startup pass cannot double it up.</summary>
        private static bool Already(List<ThinkNode> children)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is JobGiver_OptimizeWeapon)
                    return true;
            }

            return false;
        }
    }
}
