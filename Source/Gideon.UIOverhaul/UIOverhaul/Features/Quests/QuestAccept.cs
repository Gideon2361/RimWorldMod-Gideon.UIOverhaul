using System.Collections.Generic;
using System.Linq;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Quests
{
    /// <summary>
    /// Accepting a quest, following RimWorld's own path step for step.
    ///
    /// <b>This is the one place on the tab that writes to the game,</b> so it does as little thinking of its own
    /// as possible. Every gate here is vanilla's: <c>QuestUtility.CanAcceptQuest</c> decides whether the button
    /// works at all, <c>CanPawnAcceptQuest</c> decides who may be the accepter, and the confirmation shown
    /// before handing a royal title to somebody unsuited is assembled from the same three checks and the same
    /// translation keys <c>MainTabWindow_Quests</c> uses.
    ///
    /// <b>The confirmation is the reason this is not four lines.</b> A quest that grants royal favour warns you
    /// when the pawn accepting it is incapable of social, carries a conceited trait, or has a trait that will
    /// hurt their psylink later. Dropping that warning would let somebody accept a title in one click that
    /// vanilla makes them confirm, and the cost lands hours later as a mood spiral.
    ///
    /// <b>A quest with a reward choice has no accept button,</b> here or in vanilla: choosing the reward is what
    /// accepts it. That is why <see cref="Choice"/> exists, and why the panel puts a button on each alternative
    /// instead of one at the bottom.
    /// </summary>
    internal static class QuestAccept
    {
        /// <summary>
        /// The quest's reward choice part, whatever it holds. Null when the quest offers no rewards at all.
        ///
        /// <b>The first one, of any size, which is what vanilla looks for.</b> It matters that this is not
        /// gated on there being two or more: a part carrying exactly one choice still has to be chosen, and a
        /// quest accepted without calling <c>Choose</c> on it is a quest whose reward nobody claimed. The
        /// caller decides how to present it: a button per alternative where there are several, and one button
        /// that also takes the single choice where there is one.
        ///
        /// <b>A quest with no reward part is acceptable in the ordinary way.</b> Plenty are, and this screen
        /// got it wrong first time round: a strange signal, an ascension and a scanner defence all had nothing
        /// to choose between, and so were left with nothing to accept with. Reported on 2026-08-30.
        /// </summary>
        internal static QuestPart_Choice Choice(Quest quest)
        {
            List<QuestPart> parts = UIGuard.Try("Quests.Parts", () => quest.PartsListForReading, null, null);

            for (int i = 0; parts != null && i < parts.Count; i++)
            {
                QuestPart_Choice choice = parts[i] as QuestPart_Choice;

                if (choice != null)
                    return choice;
            }

            return null;
        }

        /// <summary>Whether the game will let this quest be accepted, and why not when it will not.</summary>
        internal static AcceptanceReport Can(Quest quest)
        {
            return UIGuard.Try("Quests.CanAccept", () => QuestUtility.CanAcceptQuest(quest),
                AcceptanceReport.WasRejected, null);
        }

        /// <summary>
        /// Accept, asking for an accepter first when the quest needs one.
        /// </summary>
        /// <param name="before">
        /// Run after the decision is confirmed and before the quest is accepted. This is where a reward choice
        /// is taken, so that a player who backs out of the confirmation has not silently picked a reward.
        /// </param>
        internal static void Begin(Quest quest, System.Action before = null)
        {
            UIGuard.Try("Quests.Accept", () =>
            {
                if (quest == null)
                    return;

                if (!QuestUtility.CanAcceptQuest(quest))
                {
                    Messages.Message("MessageCannotAcceptQuest".Translate(), MessageTypeDefOf.RejectInput, false);

                    return;
                }

                if (!quest.RequiresAccepter)
                {
                    Take(quest, null, before);

                    return;
                }

                List<FloatMenuOption> options = new List<FloatMenuOption>();

                foreach (Pawn pawn in PawnsFinder
                             .AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended)
                {
                    if (!QuestUtility.CanPawnAcceptQuest(pawn, quest))
                        continue;

                    Pawn accepter = pawn;
                    string label = "AcceptWith".Translate(accepter);

                    if (accepter.royalty != null && accepter.royalty.AllTitlesInEffectForReading.Any())
                        label += " (" + accepter.royalty.MostSeniorTitle.def.GetLabelFor(accepter) + ")";

                    options.Add(new FloatMenuOption(label, () => Chosen(quest, accepter, before)));
                }

                if (options.Count == 0)
                {
                    Messages.Message("MessageCannotAcceptQuest".Translate(), MessageTypeDefOf.RejectInput, false);

                    return;
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }, "The quest could not be accepted. Nothing has changed, and RimWorld's own quest screen can still "
               + "accept it.");
        }

        /// <summary>
        /// One accepter picked, with the warning vanilla shows before a title goes to the wrong person.
        /// </summary>
        private static void Chosen(Quest quest, Pawn accepter, System.Action before)
        {
            // Asked again, because the float menu was built a frame or more ago and the pawn may have been
            // downed, drafted into something else or killed since it opened.
            if (!QuestUtility.CanPawnAcceptQuest(accepter, quest))
            {
                Messages.Message("MessageCannotAcceptQuest".Translate(), MessageTypeDefOf.RejectInput, false);

                return;
            }

            QuestPart_GiveRoyalFavor favor = quest.PartsListForReading
                .OfType<QuestPart_GiveRoyalFavor>()
                .FirstOrDefault();

            if (favor == null || !favor.giveToAccepter)
            {
                Take(quest, accepter, before);

                return;
            }

            IEnumerable<Trait> conceited = RoyalTitleUtility.GetConceitedTraits(accepter);
            IEnumerable<Trait> psylink = RoyalTitleUtility.GetTraitsAffectingPsylinkNegatively(accepter);

            bool mute = accepter.skills.GetSkill(SkillDefOf.Social).TotallyDisabled;
            bool proud = conceited.Any();
            bool blocked = !accepter.HasPsylink && psylink.Any();

            if (!mute && !proud && !blocked)
            {
                Take(quest, accepter, before);

                return;
            }

            NamedArgument who = accepter.Named("PAWN");
            NamedArgument whose = favor.faction.Named("FACTION");

            TaggedString text = "QuestGivesRoyalFavor".Translate(who, whose);

            if (mute)
                text += "\n\n" + "RoyalIncapableOfSocial".Translate(who, whose);

            if (proud)
            {
                text += "\n\n" + "RoyalWithConceitedTrait".Translate(who, whose,
                    conceited.Select(t => t.Label).ToCommaList(true));
            }

            if (blocked)
            {
                text += "\n\n" + "RoyalWithTraitAffectingPsylinkNegatively".Translate(who, whose,
                    psylink.Select(t => t.Label).ToCommaList(true));
            }

            text += "\n\n" + "WantToContinue".Translate();

            Find.WindowStack.Add(new Dialog_MessageBox(text, "Confirm".Translate(),
                () => Take(quest, accepter, before), "GoBack".Translate()));
        }


        /// <summary>
        /// Setting an offer aside, or taking it back off the shelf.
        ///
        /// <b>Dismissing is not refusing.</b> The offer stays in the save and its clock keeps running; it moves
        /// off the list you are reading so that six offers you are still thinking about are not buried under
        /// twenty you are not. That is why the rail lists dismissed quests separately rather than hiding them,
        /// and why this is reversible in one click.
        ///
        /// <b>It applies to a running quest as readily as to an offer.</b> A quest you have accepted and are
        /// not going to get to for a season is exactly as ignorable as one you have not taken.
        ///
        /// <b>Subquests follow the parent, which is vanilla's own behaviour.</b> A quest chain dismissed at the
        /// top with its children left behind would leave those children on the offers list with no context,
        /// looking like offers in their own right.
        /// </summary>
        internal static void SetAside(Quest quest, bool aside)
        {
            UIGuard.Try("Quests.Dismiss", () =>
            {
                if (quest == null)
                    return;

                quest.dismissed = aside;

                foreach (Quest sub in quest.GetSubquests())
                    sub.dismissed = aside;

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();

                // The quest has just left the list being read, so the detail view it was opened from has
                // nothing behind it any more. Closing back to the list is the only honest place to land.
                QuestFacts.Selected = null;
            }, "The quest could not be set aside. Nothing has changed.");
        }

        /// <summary>The write itself, and the only one on this tab.</summary>
        private static void Take(Quest quest, Pawn accepter, System.Action before)
        {
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();

            if (before != null)
                before();

            quest.Accept(accepter);
            quest.dismissed = false;

            QuestFacts.Selected = quest;
            QuestFacts.Showing = QuestList.Active;
        }
    }
}
