using System;
using Gideon.UIFramework.Caching;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// Tells the rest of the mod when a pawn ceases to exist, or when the colony's roster changes, so nothing is
    /// left holding a reference to a pawn who is gone.
    ///
    /// <b>Two events, because they are genuinely different things.</b> Getting this wrong is easy and the symptoms
    /// are confusing, so the distinction is worth stating plainly:
    ///
    /// <list type="bullet">
    /// <item><see cref="Gone"/> means the <c>Pawn</c> object itself has been destroyed. Nothing may hold it, and any
    /// cached value derived from it is not stale but meaningless.</item>
    /// <item><see cref="RosterChanged"/> means the set of colonists is different. The pawns involved still exist and
    /// are still perfectly readable; what has changed is whether they belong in a colonist list.</item>
    /// </list>
    ///
    /// <b>A dead colonist raises the second, not the first.</b> Killing a pawn does not destroy them: the object
    /// lives on inside a <c>Corpse</c>, keeps its health tracker, can be inspected, buried, and resurrected. So
    /// forgetting a pawn on death would be wrong -- anything that legitimately shows the dead would find its data
    /// gone -- while leaving them in the colonist roster would be equally wrong. Death is a roster change.
    ///
    /// <b>The roster signal is vanilla's own.</b> Rather than enumerate every way a colonist can arrive or leave --
    /// death, banishment, recruitment, faction change, capture, caravans -- this patches
    /// <c>ColonistBar.MarkColonistsDirty</c>, which is the method the game already calls whenever its own colonist
    /// list needs rebuilding. Two of those calls are in <c>Pawn</c> itself, in <c>Kill</c> and in
    /// <c>SetFaction</c>, and there are more elsewhere. Borrowing the game's signal means this keeps working when a
    /// future version adds another way to join or leave, which a hand-written list of hooks would not.
    /// </summary>
    public static class PawnLifecycle
    {
        /// <summary>
        /// Raised once a pawn has been destroyed. Subscribers must drop every reference they hold to them.
        ///
        /// Raised after vanilla's own destruction work, so the pawn is already in whatever state it will end in.
        /// </summary>
        public static event Action<Pawn> Gone;

        /// <summary>
        /// Raised when the set of colonists has changed. Subscribers holding a built list of pawns should rebuild
        /// it; nobody needs to drop caches, because the pawns involved still exist.
        /// </summary>
        public static event Action RosterChanged;

        /// <summary>
        /// Called from the Destroy patch. Forgets the pawn everywhere before telling anyone, so a subscriber that
        /// reacts by reading something cannot be handed a value derived from the pawn that just died.
        /// </summary>
        internal static void Notify_Gone(Pawn pawn)
        {
            if (pawn == null)
                return;

            UICacheController.Forget(pawn);

            // Invoked through the guard, and per subscriber, so one feature failing to clean up does not stop the
            // others from being told. This runs during gameplay rather than while drawing, so an escape here would
            // land in the middle of Thing.Destroy.
            Action<Pawn> handlers = Gone;

            if (handlers == null)
                return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                Action<Pawn> subscriber = (Action<Pawn>) handler;
                UIGuard.Try("Pawns.NotifyGone", () => subscriber(pawn),
                    "Something in this mod may still be holding a reference to a destroyed pawn.");
            }
        }

        internal static void Notify_RosterChanged()
        {
            Action handlers = RosterChanged;

            if (handlers == null)
                return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                Action subscriber = (Action) handler;
                UIGuard.Try("Pawns.NotifyRosterChanged", subscriber,
                    "A colonist list in this mod may be showing the previous set of colonists.");
            }
        }
    }

    /// <summary>
    /// A pawn has been destroyed.
    ///
    /// A postfix so it runs after vanilla has finished, and on <c>Pawn.Destroy</c> rather than <c>Thing.Destroy</c>
    /// so this fires once per pawn rather than for every item on the map.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
    public static class Patch_Pawn_Destroy_Lifecycle
    {
        public static void Postfix(Pawn __instance)
        {
            // Written out rather than through UIGuard.Try so the happy path allocates nothing: Destroy runs for
            // every pawn the game disposes of, including raiders and animals, not only colonists.
            try
            {
                PawnLifecycle.Notify_Gone(__instance);
            }
            catch (Exception ex)
            {
                UIGuard.Report("Pawns.DestroyHook", ex,
                    "Cached data and UI references for a destroyed pawn may not have been cleaned up. They are "
                    + "still dropped later by the caches' own pruning.");
            }
        }
    }

    /// <summary>
    /// The colonist roster has changed.
    ///
    /// <c>MarkColonistsDirty</c> is what vanilla calls when its colonist bar needs rebuilding, which makes it the
    /// broadest available "who counts as a colonist has changed" signal. It is called for deaths, faction changes,
    /// recruitment and banishment alike.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.MarkColonistsDirty))]
    public static class Patch_ColonistBar_MarkColonistsDirty
    {
        public static void Postfix()
        {
            try
            {
                PawnLifecycle.Notify_RosterChanged();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Pawns.RosterHook", ex,
                    "A colonist list in this mod may be showing the previous set of colonists until it next "
                    + "rebuilds on its own.");
            }
        }
    }
}
