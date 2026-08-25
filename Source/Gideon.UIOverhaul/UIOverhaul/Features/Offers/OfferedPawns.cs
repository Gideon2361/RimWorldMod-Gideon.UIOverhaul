using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Gideon.UIOverhaul.Features.Offers
{
    /// <summary>
    /// Works out which pawns a letter is actually offering, so the dialog can say something about them.
    ///
    /// <b>Named types rather than a sweep of every letter with a pawn in its look targets.</b> Almost every
    /// letter in the game points at a pawn -- a death, a breakdown, a raid, a birthday -- and a blanket rule
    /// would put a statistics panel on all of them. What separates the letters handled here is that the
    /// player's answer depends on who the pawn is, which is exactly when their skills are worth the space.
    ///
    /// The cost of naming types is that a modded offer letter gets nothing until it is added here. That is the
    /// right way round: a panel that fails to appear is a feature that has not reached somewhere yet, and a
    /// panel that appears on a death notice is a bug the player has to look at every time.
    /// </summary>
    internal static class OfferedPawns
    {
        /// <summary>
        /// The pawns this letter is asking the player to judge, or an empty list when it is not asking that.
        /// </summary>
        internal static List<Pawn> For(ChoiceLetter letter)
        {
            List<Pawn> found = new List<Pawn>();

            if (letter == null)
                return found;

            UIGuard.Try("Offers.Resolve", () =>
            {
                // Several candidates, one of whom the player picks: a quest reward, a refugee group. This is the
                // letter the feature exists for, since vanilla offers the choice as a row of bare names.
                if (letter is ChoiceLetter_ChoosePawn choose && choose.pawns != null)
                {
                    for (int i = 0; i < choose.pawns.Count; i++)
                        Add(found, choose.pawns[i]);

                    return;
                }

                // A creepjoiner. The speaker is deliberately left out: they are the one making the offer, not
                // the thing being offered.
                if (letter is ChoiceLetter_AcceptCreepJoiner creep)
                {
                    Add(found, creep.pawn);
                    return;
                }

                // A kidnapped colonist held to ransom. Their own skills are the whole question -- the price is
                // the same whoever it is.
                if (letter is ChoiceLetter_RansomDemand ransom)
                {
                    Add(found, ransom.kidnapped);
                    return;
                }

                // A wanderer, a fleeing refugee, a quest lodger arriving. This letter carries no pawn field of
                // its own, so the joiner is read from where it does point: its look targets, which are what the
                // Jump to location option uses and are the pawn themselves.
                if (letter is ChoiceLetter_AcceptJoiner joiner)
                    FromTargets(found, joiner);
            }, "Offer dialogs are drawn the way RimWorld draws them.");

            return found;
        }

        private static void FromTargets(List<Pawn> found, Letter letter)
        {
            if (letter.lookTargets == null || letter.lookTargets.targets == null)
                return;

            List<GlobalTargetInfo> targets = letter.lookTargets.targets;

            for (int i = 0; i < targets.Count; i++)
                Add(found, targets[i].Thing as Pawn);
        }

        /// <summary>
        /// Adds a pawn once, if there is one worth drawing.
        ///
        /// A destroyed pawn is refused because <c>ChoiceLetter_ChoosePawn</c> keeps its list across a save and
        /// draws its options from whichever entries survive, so ours has to make the same allowance or the
        /// panel would describe somebody the buttons no longer offer.
        /// </summary>
        private static void Add(List<Pawn> found, Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || found.Contains(pawn))
                return;

            found.Add(pawn);
        }
    }
}
