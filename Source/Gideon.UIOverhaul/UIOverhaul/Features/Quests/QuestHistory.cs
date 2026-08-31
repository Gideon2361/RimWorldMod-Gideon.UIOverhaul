using System.Collections.Generic;
using System.Linq;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Quests
{
    /// <summary>
    /// Deleting finished quests, properly rather than by hiding them.
    ///
    /// <b>Vanilla's delete is not a delete.</b> The bin on a historical quest sets <c>hiddenInUI</c>, which
    /// takes it off the list and leaves it in the save with all its parts, its signal registrations and its
    /// references. A colony a few years old carries hundreds, and they are written out on every autosave.
    ///
    /// <b>So this calls <c>QuestManager.Remove</c>,</b> which drops the quest from all three lists, clears the
    /// display cache and deregisters it from the signal manager. That last part is why removal is safe and
    /// hand-editing the list would not be: a quest still registered as a signal receiver after being dropped is
    /// a null reference waiting for the next signal that matches it.
    ///
    /// <b>Quests in a chain are left alone, and that is the whole of the safety argument.</b> A quest that has a
    /// parent, or that another quest points at as its parent, is reachable from something still in the save;
    /// vanilla's own quest card draws hyperlinks in both directions. Removing one end of that leaves the other
    /// pointing at nothing. The sweep skips them and says how many it skipped, rather than quietly doing less
    /// than the button promised.
    /// </summary>
    internal static class QuestHistory
    {
        /// <summary>
        /// Whether this quest can be dropped from the save without leaving something else dangling.
        ///
        /// Only finished quests, and only ones standing on their own. The state check is not paranoia: a quest
        /// can be historical by <c>Historical</c> and still be the parent of one that is running.
        /// </summary>
        internal static bool Removable(Quest quest)
        {
            return UIGuard.Try("Quests.Removable", () =>
            {
                if (quest == null || !quest.Historical)
                    return false;

                if (quest.parent != null)
                    return false;

                return !quest.GetSubquests().Any();
            }, false, null);
        }

        /// <summary>Why one cannot be removed, for the tooltip on a disabled control.</summary>
        internal static string Blocked(Quest quest)
        {
            if (Removable(quest))
                return null;

            bool chained = UIGuard.Try("Quests.Chained",
                () => quest != null && (quest.parent != null || quest.GetSubquests().Any()), false, null);

            return chained
                ? "This quest is part of a chain. Removing it would leave the quests linked to it pointing at "
                  + "nothing, so it stays in the save."
                : "Only finished quests can be removed.";
        }

        /// <summary>Drops one quest from the save.</summary>
        internal static void Remove(Quest quest)
        {
            if (!Removable(quest))
                return;

            UIGuard.Try("Quests.Remove", () =>
            {
                Find.QuestManager.Remove(quest);

                if (QuestFacts.Selected == quest)
                    QuestFacts.Selected = null;

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }, "That quest could not be removed. It is still in the save and nothing else has changed.");
        }

        /// <summary>
        /// The outcomes the history can be swept by, in the order a player thinks of them.
        ///
        /// <b>Grouped by what happened rather than by state,</b> which is why this is a list and not the
        /// <c>QuestState</c> enum. Two of the seven states mean "it ended and nobody recorded how", and a menu
        /// offering "ended unknown outcome" and "ended invalid" as separate lines would be reading the enum out
        /// loud rather than answering a question anybody has.
        /// </summary>
        internal static readonly QuestState[] Groups =
        {
            QuestState.EndedSuccess,
            QuestState.EndedFailed,
            QuestState.EndedOfferExpired
        };

        /// <summary>What to call one of those groups.</summary>
        internal static string GroupLabel(QuestState state)
        {
            switch (state)
            {
                case QuestState.EndedSuccess: return "completed";
                case QuestState.EndedFailed: return "failed";
                case QuestState.EndedOfferExpired: return "expired";
                default: return "ended";
            }
        }

        /// <summary>
        /// Whether a quest falls in one of the named groups.
        ///
        /// The two vague states fall together under the last group, so a sweep of everything still reaches
        /// them and no finished quest is unreachable by any button.
        /// </summary>
        private static bool InGroup(Quest quest, QuestState group)
        {
            QuestState state = UIGuard.Try("Quests.State", () => quest.State, QuestState.EndedInvalid, null);

            if (group == QuestState.EndedSuccess || group == QuestState.EndedFailed
                                                 || group == QuestState.EndedOfferExpired)
                return state == group;

            return state != QuestState.EndedSuccess && state != QuestState.EndedFailed
                                                    && state != QuestState.EndedOfferExpired;
        }

        /// <summary>How many of one outcome could be removed.</summary>
        internal static int CountOf(QuestState group)
        {
            List<Quest> all = UIGuard.Try("Quests.List", () => Find.QuestManager?.QuestsListForReading, null,
                null);

            int count = 0;

            for (int i = 0; all != null && i < all.Count; i++)
            {
                if (Removable(all[i]) && InGroup(all[i], group))
                    count++;
            }

            return count;
        }

        /// <summary>Drops every standalone finished quest with one outcome.</summary>
        internal static int SweepOf(QuestState group)
        {
            return UIGuard.Try("Quests.SweepGroup", () =>
            {
                List<Quest> doomed = new List<Quest>();
                List<Quest> all = Find.QuestManager?.QuestsListForReading;

                for (int i = 0; all != null && i < all.Count; i++)
                {
                    if (Removable(all[i]) && InGroup(all[i], group))
                        doomed.Add(all[i]);
                }

                for (int i = 0; i < doomed.Count; i++)
                    Find.QuestManager.Remove(doomed[i]);

                if (doomed.Count > 0)
                {
                    QuestFacts.Selected = null;

                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }

                return doomed.Count;
            }, 0, "Those quests could not be removed. They are still in the save and nothing else has changed.");
        }

        /// <summary>How many finished quests the sweep would take, and how many it would have to leave.</summary>
        internal static void Sweepable(out int removable, out int chained)
        {
            removable = 0;
            chained = 0;

            List<Quest> all = UIGuard.Try("Quests.List", () => Find.QuestManager?.QuestsListForReading, null,
                null);

            for (int i = 0; all != null && i < all.Count; i++)
            {
                Quest quest = all[i];

                if (quest == null || !UIGuard.Try("Quests.Historical", () => quest.Historical, false, null))
                    continue;

                if (Removable(quest))
                    removable++;
                else
                    chained++;
            }
        }

        /// <summary>
        /// Drops every finished quest that stands on its own.
        ///
        /// <b>Collected before anything is removed.</b> <c>QuestManager.Remove</c> mutates the list this would
        /// otherwise be walking, and removing while enumerating skips every second entry, which would leave
        /// the button reporting a number it did not achieve.
        /// </summary>
        internal static int Sweep()
        {
            return UIGuard.Try("Quests.Sweep", () =>
            {
                List<Quest> doomed = new List<Quest>();
                List<Quest> all = Find.QuestManager?.QuestsListForReading;

                for (int i = 0; all != null && i < all.Count; i++)
                {
                    if (Removable(all[i]))
                        doomed.Add(all[i]);
                }

                for (int i = 0; i < doomed.Count; i++)
                    Find.QuestManager.Remove(doomed[i]);

                if (doomed.Count > 0)
                {
                    QuestFacts.Selected = null;

                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }

                return doomed.Count;
            }, 0, "The finished quests could not be removed. They are still in the save and nothing else has "
                  + "changed.");
        }
    }
}
