using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Drops pawns from the Colonists idle alert who are idle for a reason no order can fix: somebody else's
    /// pawn standing in your colony, and somebody who has no work available to them at all.
    ///
    /// <b>What the alert is for.</b> It exists to catch a colonist with nothing queued, because that is a
    /// colonist you can go and give a job to. Both groups here are idle in the literal sense and neither is
    /// actionable, so the alert fires and stays lit however the player answers it. An alert that cannot be
    /// cleared by doing the right thing trains the player to ignore the alert, which costs them the times it
    /// was real.
    ///
    /// <b>Vanilla already makes this distinction and stops halfway.</b> <c>IdleColonists</c> skips quest
    /// lodgers, and skips a royal whose title carries <c>suppressIdleAlert</c>. So the principle is RimWorld's
    /// own; what is missing is every other way a pawn arrives at the same position.
    ///
    /// <b>Patched at the private <c>IdleColonists</c> getter rather than at <c>GetReport</c>.</b> That getter
    /// returns the alert's own <c>idleColonistsResult</c> field, which is also what <c>GetLabel</c> counts and
    /// what <c>GetExplanation</c> lists. Filtering the returned list therefore fixes the count, the names and
    /// the culprits together. Filtering an <c>AlertReport</c> afterwards would leave the label reading a number
    /// that no longer matched the list under it.
    /// </summary>
    [HarmonyPatch(typeof(Alert_ColonistsIdle), "IdleColonists", MethodType.Getter)]
    internal static class Patch_IdleColonistAlert
    {
        public static void Postfix(List<Pawn> __result)
        {
            UIGuard.Try("Notifications.IdleAlert", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null || !settings.quietIdleAlert || __result == null)
                    return;

                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    if (Excused(__result[i]))
                        __result.RemoveAt(i);
                }
            }, "The idle alert lists everyone RimWorld would have listed.");
        }

        /// <summary>Whether this pawn's idleness is not something the player can act on.</summary>
        private static bool Excused(Pawn pawn)
        {
            if (pawn == null)
                return false;

            return Visiting(pawn) || !CanWorkAtAll(pawn);
        }

        /// <summary>
        /// Whether the pawn is in the colony without being the player's to command.
        ///
        /// <b>Three tests, because there are three ways in.</b> <c>GuestStatus</c> covers a pawn the colony is
        /// hosting outright. <c>IsQuestLodger</c> is vanilla's own test and is repeated here rather than relied
        /// on, since the alert applies it before our postfix and a future version moving it would silently drop
        /// the case. The extra-faction pair covers a pawn lent by a quest, who reads as a colonist everywhere
        /// else but goes home when the quest ends.
        ///
        /// A slave is deliberately not excused. Slaves work, and a slave with nothing to do is the same
        /// actionable problem as a colonist with nothing to do.
        /// </summary>
        private static bool Visiting(Pawn pawn)
        {
            if (pawn.IsSlave)
                return false;

            if (pawn.GuestStatus.HasValue)
                return true;

            return pawn.IsQuestLodger() || pawn.HasExtraHomeFaction() || pawn.HasExtraMiniFaction();
        }

        /// <summary>
        /// Whether any work type at all is open to this pawn.
        ///
        /// <b>Asked per work type rather than off a single flag,</b> because there is no single flag for it.
        /// A pawn is incapable of everything through some combination of traits, genes, a hediff and their
        /// childhood, and each of those disables its own set. The question the player would ask is whether the
        /// Work tab has a single column this pawn could be given, so that is the question asked here.
        ///
        /// <b>Invisible work types do not count.</b> A work type with <c>visible</c> false never appears on the
        /// Work tab, so a pawn whose only remaining work is invisible has nothing the player can assign, and
        /// telling them to go and assign it would be advice they cannot follow.
        ///
        /// Uninitialized work settings mean a pawn who does not work at all, which is how babies and children
        /// below working age arrive here.
        /// </summary>
        private static bool CanWorkAtAll(Pawn pawn)
        {
            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                return false;

            List<WorkTypeDef> types = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            for (int i = 0; i < types.Count; i++)
            {
                WorkTypeDef type = types[i];

                if (type != null && type.visible && !pawn.WorkTypeIsDisabled(type))
                    return true;
            }

            return false;
        }
    }
}
