using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Pawns;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ColonyBar
{
    /// <summary>
    /// One named, coloured, foldable set of colonists.
    ///
    /// <b>Pawns are held by reference and saved that way.</b> A group is a player's opinion about their colony, so
    /// it belongs in the save rather than in the config: the same names would be meaningless in another colony.
    /// </summary>
    public class PawnGroup : IExposable
    {
        public string Name = "Group";

        public Color Color = Color.white;

        /// <summary>Folded in the bar. Saved, because folding is a decision rather than a session's state.</summary>
        public bool Collapsed;

        public List<Pawn> Pawns = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Name, "name", "Group");
            Scribe_Values.Look(ref Color, "color", Color.white);
            Scribe_Values.Look(ref Collapsed, "collapsed");
            Scribe_Collections.Look(ref Pawns, "pawns", LookMode.Reference);

            // A null list back from the scribe is normal for a group saved while empty, and every reader here
            // walks it, so it is repaired once at load rather than tested at every use.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Pawns == null)
                Pawns = new List<Pawn>();
        }
    }

    /// <summary>
    /// The colony's groups, saved with the game.
    ///
    /// <b>Unassigned is computed, never stored.</b> It is every colonist who is in no group, worked out at draw
    /// time. Storing it would mean a second place that has to hear about every arrival, recruitment, capture and
    /// death, and the failure mode of missing one of those is a pawn who is in no group and therefore in no part of
    /// the bar: invisible. Computing it makes that impossible, at the cost of one set-difference per rebuild.
    ///
    /// <b>Removal is driven by the game's own signal.</b> <see cref="PawnLifecycle"/> already raises an event when a
    /// pawn is destroyed and another when the roster changes, both borrowed from vanilla's own bookkeeping, so this
    /// does not enumerate the ways a colonist can leave.
    /// </summary>
    public class GameComponent_PawnGroups : GameComponent
    {
        private List<PawnGroup> groups = new List<PawnGroup>();

        /// <summary>Whether the computed Unassigned group is folded. Stored here since it has no group object.</summary>
        private bool unassignedCollapsed;

        public GameComponent_PawnGroups(Game game)
        {
            Subscribe();
        }

        internal static GameComponent_PawnGroups Current =>
            Verse.Current.Game?.GetComponent<GameComponent_PawnGroups>();

        internal List<PawnGroup> Groups => groups ?? (groups = new List<PawnGroup>());

        internal bool UnassignedCollapsed
        {
            get => unassignedCollapsed;
            set => unassignedCollapsed = value;
        }

        /// <summary>
        /// The colours offered when making or recolouring a group.
        ///
        /// A short fixed list rather than a colour picker: these have to stay apart from each other at the size of
        /// a four pixel chip, which a free picker cannot promise, and they have to stay apart from the health and
        /// mood bars underneath.
        /// </summary>
        internal static readonly Color[] Palette =
        {
            new Color(0.29f, 0.53f, 0.85f),
            new Color(0.37f, 0.66f, 0.33f),
            new Color(0.76f, 0.33f, 0.31f),
            new Color(0.82f, 0.63f, 0.24f),
            new Color(0.60f, 0.44f, 0.78f),
            new Color(0.31f, 0.71f, 0.68f),
            new Color(0.85f, 0.51f, 0.24f),
            new Color(0.55f, 0.57f, 0.60f)
        };

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref groups, "gideonPawnGroups", LookMode.Deep);
            Scribe_Values.Look(ref unassignedCollapsed, "gideonUnassignedCollapsed");

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            if (groups == null)
                groups = new List<PawnGroup>();

            // A reference that did not resolve comes back null, which happens whenever a pawn is gone from a save
            // this group outlived. Swept here rather than guarded at every read.
            foreach (PawnGroup group in groups)
            {
                if (group.Pawns == null)
                    group.Pawns = new List<Pawn>();
                else
                    group.Pawns.RemoveAll(pawn => pawn == null);
            }

            Subscribe();
        }

        private static bool subscribed;

        /// <summary>
        /// Hooks the destroyed-pawn event exactly once per session.
        ///
        /// <b>Static, and that is the point.</b> <c>PawnLifecycle.Gone</c> is a static event while this component
        /// is rebuilt for every game loaded, so subscribing an instance method would leave the component from every
        /// previously loaded save still attached for the rest of the session, each pruning a group list nobody can
        /// see any more. The handler is static and reaches the live component through <see cref="Current"/>, so
        /// there is one subscription no matter how many saves are opened.
        /// </summary>
        private static void Subscribe()
        {
            if (subscribed)
                return;

            subscribed = true;

            PawnLifecycle.Gone += Forget;
        }

        /// <summary>Drops a destroyed pawn from every group, so nothing holds them after the game let go.</summary>
        private static void Forget(Pawn pawn)
        {
            UIGuard.Try("Bar.ForgetPawn", () =>
            {
                List<PawnGroup> live = Current?.groups;

                if (pawn == null || live == null)
                    return;

                foreach (PawnGroup group in live)
                    group.Pawns?.Remove(pawn);
            }, null);
        }

        internal PawnGroup GroupOf(Pawn pawn)
        {
            if (pawn == null || groups == null)
                return null;

            foreach (PawnGroup group in groups)
            {
                if (group.Pawns != null && group.Pawns.Contains(pawn))
                    return group;
            }

            return null;
        }

        /// <summary>
        /// Puts a pawn in a group, taking them out of whichever one they were in.
        ///
        /// A pawn is in exactly one group, which is what makes Unassigned a straight set difference and what stops
        /// the same tile appearing twice in the bar. Null moves them to Unassigned.
        /// </summary>
        internal void Assign(Pawn pawn, PawnGroup group)
        {
            UIGuard.Try("Bar.Assign", () =>
            {
                if (pawn == null)
                    return;

                foreach (PawnGroup other in Groups)
                    other.Pawns?.Remove(pawn);

                if (group == null)
                    return;

                if (group.Pawns == null)
                    group.Pawns = new List<Pawn>();

                group.Pawns.Add(pawn);
            }, "That pawn was not moved.");
        }

        internal PawnGroup Add(string name)
        {
            return UIGuard.Try("Bar.AddGroup", () =>
            {
                PawnGroup group = new PawnGroup
                {
                    Name = name.NullOrEmpty() ? "Group " + (Groups.Count + 1) : name,
                    Color = Palette[Groups.Count % Palette.Length]
                };

                Groups.Add(group);

                return group;
            }, null, "The group was not created.");
        }

        /// <summary>Removes a group. Its pawns are not touched, so they fall back into Unassigned.</summary>
        internal void Remove(PawnGroup group)
        {
            UIGuard.Try("Bar.RemoveGroup", () => Groups.Remove(group), "The group was not removed.");
        }

        /// <summary>Moves a group one place left or right in the bar.</summary>
        internal void Shift(PawnGroup group, int delta)
        {
            UIGuard.Try("Bar.ShiftGroup", () =>
            {
                int at = Groups.IndexOf(group);
                int to = at + delta;

                if (at < 0 || to < 0 || to >= Groups.Count)
                    return;

                Groups.RemoveAt(at);
                Groups.Insert(to, group);
            }, null);
        }
    }
}
