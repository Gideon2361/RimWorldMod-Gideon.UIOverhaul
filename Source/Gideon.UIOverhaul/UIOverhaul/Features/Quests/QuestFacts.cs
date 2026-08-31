using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Quests
{
    /// <summary>One reward a quest is offering, as a line the panel can draw.</summary>
    internal struct RewardRow
    {
        /// <summary>The game's own wording for it.</summary>
        internal string text;

        /// <summary>
        /// The actual goods, when this reward is goods.
        ///
        /// Kept so the panel can draw each one's icon and open its info card. Null for a reward that has no
        /// things behind it, which is most of them: goodwill, royal favour and a psylink level are all real
        /// rewards with nothing to point at.
        /// </summary>
        internal List<Thing> things;

        /// <summary>
        /// The person being offered, when this reward is a person and the quest is willing to say who.
        ///
        /// Null for every other kind of reward. Also set when the quest marks their details hidden, because
        /// that flag is the content declining to introduce them and a screen that opened their skills anyway
        /// would be overriding a decision made on purpose -- but <see cref="pawnHidden"/> says so, rather than
        /// the row looking like a reward with no person in it.
        /// </summary>
        internal Pawn pawn;

        /// <summary>Whether there is a person here that the quest will not introduce.</summary>
        internal bool pawnHidden;
    }

    /// <summary>One of the alternatives a quest asks you to pick between.</summary>
    internal struct ChoiceRow
    {
        internal List<RewardRow> rewards;
    }

    /// <summary>A quest on offer, judged against the colony that would accept it.</summary>
    internal struct OfferRow
    {
        internal Quest quest;
        internal string name;

        /// <summary>Challenge rating, or zero when the quest does not carry one.</summary>
        internal int rating;

        internal string factions;
        internal bool charity;

        /// <summary>Ticks until the offer lapses, or int.MaxValue when it does not.</summary>
        internal int expires;

        /// <summary>The alternatives, when there are two or more. Empty when the reward is not a choice.</summary>
        internal List<ChoiceRow> choices;

        /// <summary>Everything paid whichever alternative is taken.</summary>
        internal List<RewardRow> rewards;
    }

    /// <summary>A quest already running.</summary>
    internal struct ActiveRow
    {
        internal Quest quest;
        internal string name;
        internal string factions;

        /// <summary>Ticks until it ends, or int.MaxValue when nothing about it is on a clock.</summary>
        internal int ends;

        /// <summary>Colonists this quest is holding, which is what makes it cost something today.</summary>
        internal List<Pawn> reserved;

        /// <summary>Where it is pointing, with a distance when that is somewhere on the world map.</summary>
        internal string where;

        /// <summary>That same place as something the camera can be sent to.</summary>
        internal GlobalTargetInfo target;
    }

    /// <summary>One clock running on a quest: how long is left, and the game's own wording for it.</summary>
    internal struct DeadlineRow
    {
        internal int ticks;
        internal string text;
    }

    /// <summary>A quest that has finished, and how.</summary>
    internal struct HistoryRow
    {
        internal Quest quest;
        internal string name;
        internal string outcome;

        /// <summary>Which of the ended states it is in, for the colour.</summary>
        internal QuestState state;

        /// <summary>Ticks since it was cleaned up.</summary>
        internal int ago;
    }

    /// <summary>
    /// The read side of the quests tab. Everything here is already computed by the game.
    ///
    /// <b>Nothing is invented and nothing is written.</b> <c>Quest.State</c>, <c>TicksUntilExpiry</c>,
    /// <c>challengeRating</c>, <c>charity</c> and <c>QuestReserves</c> are all public and maintained whether
    /// anything reads them or not. The rewards come from the quest's own <c>QuestPart_Choice</c> parts, and the
    /// wording of each one is <c>Reward.GetDescription</c>, so a reward this mod has never heard of still
    /// describes itself correctly.
    ///
    /// <b>Every read is guarded.</b> A quest is a bag of parts contributed by content, including content from
    /// other mods, and any one of them can throw on a property this screen touches. A quest that cannot be read
    /// is left out of the list rather than taking the tab down with it.
    /// </summary>
    internal static class QuestFacts
    {
        /// <summary>The quest the panel is showing, kept across frames.</summary>
        internal static Quest Selected;

        /// <summary>Which of the three lists the rail is on.</summary>
        internal static QuestList Showing = QuestList.Offers;

        private static readonly List<Quest> Scratch = new List<Quest>();

        private static List<Quest> All()
        {
            return UIGuard.Try("Quests.List", () => Find.QuestManager?.QuestsListForReading, null, null);
        }

        /// <summary>
        /// Whether a quest belongs on this screen at all.
        ///
        /// <b>Hidden and dismissed are two different refusals and both are honoured.</b> <c>hidden</c> is the
        /// content saying this quest is machinery rather than an offer; <c>dismissed</c> is the player saying
        /// they have decided. Vanilla keeps dismissed quests reachable behind a dev toggle and so does the
        /// rail, which lists them separately rather than mixing them back in.
        /// </summary>
        private static bool Listed(Quest quest)
        {
            return quest != null && !quest.hidden && !quest.hiddenInUI;
        }

        /// <summary>Which of the four lists a quest belongs to, if any.</summary>
        private static bool Belongs(Quest quest, QuestList which)
        {
            if (!Listed(quest))
                return false;

            bool historical = UIGuard.Try("Quests.Historical", () => quest.Historical, false, null);

            if (which == QuestList.History)
                return historical;

            // Set aside covers offers and running quests alike. Dismissing is not refusing: the quest carries
            // on and its clock keeps running, it just stops taking up a row on the list being read. A running
            // quest is exactly as ignorable as an offer, and vanilla lets you dismiss both.
            if (which == QuestList.Dismissed)
                return !historical && quest.dismissed;

            if (quest.dismissed)
                return false;

            QuestState state = UIGuard.Try("Quests.State", () => quest.State, QuestState.EndedInvalid, null);

            return which == QuestList.Offers
                ? state == QuestState.NotYetAccepted
                : state == QuestState.Ongoing;
        }

        /// <summary>
        /// One list, into a buffer this type owns.
        ///
        /// <b>The buffer is shared, so a caller must finish with it before asking again.</b> The three list
        /// builders below each copy what they need out of it before returning, which is what makes that safe;
        /// <see cref="Count"/> deliberately does not go through here for the same reason.
        /// </summary>
        internal static List<Quest> Of(QuestList which)
        {
            Scratch.Clear();

            List<Quest> all = All();

            for (int i = 0; all != null && i < all.Count; i++)
            {
                if (Belongs(all[i], which))
                    Scratch.Add(all[i]);
            }

            return Scratch;
        }

        /// <summary>
        /// How many are in one list, for the rail's counts.
        ///
        /// <b>Counted rather than measured off <see cref="Of"/>.</b> That method hands back a buffer it shares
        /// with every other caller, so counting through it in the middle of walking it would empty the list
        /// being walked. The rail asks four times a frame while the panel is drawing from the same buffer,
        /// which is exactly that case.
        /// </summary>
        internal static int Count(QuestList which)
        {
            List<Quest> all = All();
            int count = 0;

            for (int i = 0; all != null && i < all.Count; i++)
            {
                if (Belongs(all[i], which))
                    count++;
            }

            return count;
        }

        // -------------------------------------------------------------------------------------------
        // Offers
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Offer rows for one of the lists, soonest to lapse first, which is the order a decision is made in.
        ///
        /// <b>Which list is a parameter because two of them draw as cards.</b> Dismissed holds set-aside
        /// offers, and reading it while this method was hardwired to Offers showed the offers list back to
        /// somebody who had asked for the dismissed one.
        /// </summary>
        internal static List<OfferRow> Offers(List<OfferRow> into, QuestList which = QuestList.Offers)
        {
            into.Clear();

            List<Quest> quests = Of(which);

            for (int i = 0; i < quests.Count; i++)
                into.Add(Offer(quests[i]));

            into.Sort((a, b) => a.expires.CompareTo(b.expires));

            return into;
        }

        /// <summary>
        /// Ticks until the offer lapses, or int.MaxValue when it never does.
        ///
        /// <b>Minus one is the game's way of saying there is no deadline,</b> not a deadline that has passed.
        /// <c>TicksUntilExpiry</c> returns it whenever <c>acceptanceExpireTick</c> was never set. Taken
        /// literally it sorted those offers to the top as the most urgent and printed "expires in --" on
        /// them, and put a red pin on today in the deadline strip. Reported on 2026-08-30.
        /// </summary>
        private static int Expiry(Quest quest)
        {
            int ticks = UIGuard.Try("Quests.Expiry", () => quest.TicksUntilExpiry, -1, null);

            return ticks < 0 ? int.MaxValue : ticks;
        }

        internal static OfferRow Offer(Quest quest)
        {
            OfferRow row = new OfferRow
            {
                quest = quest,
                name = Name(quest),
                factions = Factions(quest),
                charity = quest.charity,
                rating = quest.challengeRating > 0 ? quest.challengeRating : 0,
                expires = Expiry(quest),
                choices = new List<ChoiceRow>(),
                rewards = new List<RewardRow>()
            };

            Rewards(quest, row.choices, row.rewards);

            return row;
        }

        /// <summary>
        /// The reward stack, split into the alternatives and the part paid regardless.
        ///
        /// <b>A quest usually offers a choice, and drawing one reward is the thing this screen exists to stop
        /// doing.</b> The alternatives live on <c>QuestPart_Choice</c>: a part with two or more choices is the
        /// pick, and a part with exactly one is a reward the quest pays whichever pick is made. Vanilla's own
        /// <c>PreventsAutoAccept</c> draws the line in the same place, at two.
        /// </summary>
        private static void Rewards(Quest quest, List<ChoiceRow> choices, List<RewardRow> fixedRewards)
        {
            List<QuestPart> parts = UIGuard.Try("Quests.Parts", () => quest.PartsListForReading, null, null);

            for (int i = 0; parts != null && i < parts.Count; i++)
            {
                QuestPart_Choice choice = parts[i] as QuestPart_Choice;

                if (choice == null || choice.choices == null || choice.choices.Count == 0)
                    continue;

                if (choice.choices.Count == 1)
                {
                    Collect(choice.choices[0], fixedRewards);

                    continue;
                }

                for (int c = 0; c < choice.choices.Count; c++)
                {
                    ChoiceRow row = new ChoiceRow { rewards = new List<RewardRow>() };

                    Collect(choice.choices[c], row.rewards);

                    if (row.rewards.Count > 0)
                        choices.Add(row);
                }
            }
        }

        /// <summary>
        /// A reward description with the rich text tags taken off and its line breaks left alone.
        ///
        /// <b>The breaks are the game's list.</b> A reward describing transport pods writes a heading, a blank
        /// line and then one hyphen-led line per thing, which is a list and reads as one anywhere it is given
        /// room to be a list. It was being flattened here because the card that drew it was a single line.
        /// </summary>
        private static string Clean(string text)
        {
            if (text.NullOrEmpty())
                return text;

            // The colour tags stay on. RimWorld writes faction and thing names into reward text already
            // coloured, every label this mod draws has rich text enabled, and stripping them here threw that
            // away for no gain. Reported on 2026-08-30, with a faction name reading as plain grey.
            return text.Replace("\r", string.Empty).Trim();
        }

        /// <summary>
        /// The same description folded onto one line, for the places that only have one.
        ///
        /// <b>The game's own bullets are taken off rather than run together.</b> Joining the lines with a
        /// separator left the hyphen that started each one still sitting there, so a two-item reward read as
        /// "-  - Psychic animal pulser x3  -  - Psylink neuroformer": our separator and the game's bullet, one
        /// after the other, twice. Reported on 2026-08-30.
        /// </summary>
        internal static string Flat(string text)
        {
            if (text.NullOrEmpty())
                return text;

            // Tags come off here and only here: a one-line row is ellipsed to fit, and a cut landing
            // inside a colour tag would leave the markup showing.
            string[] lines = text.StripTags().Split((char) 10);
            string joined = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.NullOrEmpty())
                    continue;

                // The list marker, whichever of the three the language uses. It is punctuation for a layout
                // that is not happening here, so it comes off rather than being read out.
                line = line.TrimStart('-', '*', '\u2022').Trim().TrimEnd('.');

                if (line.NullOrEmpty())
                    continue;

                joined = joined == null
                    ? line
                    : joined + (joined.EndsWith(":") ? " " : ", ") + line;
            }

            return joined ?? string.Empty;
        }


        private static void Collect(QuestPart_Choice.Choice choice, List<RewardRow> into)
        {
            if (choice == null || choice.rewards == null)
                return;

            for (int i = 0; i < choice.rewards.Count; i++)
            {
                Reward reward = choice.rewards[i];

                if (reward == null)
                    continue;

                // The default generator params are what the normal wording is written against: giveToCaravan
                // false and no chosen-pawn signal. They only ever select between phrasings of the same reward,
                // so a default here reads as the reward reads on RimWorld's own pane.
                string text = UIGuard.Try("Quests.RewardText",
                    () => reward.GetDescription(default(RewardsGeneratorParams)), null, null);

                if (text.NullOrEmpty())
                    continue;

                Reward_Pawn offered = reward as Reward_Pawn;

                Reward_Items goods = reward as Reward_Items;

                into.Add(new RewardRow
                {
                    text = Clean(text),
                    pawn = offered != null && !offered.detailsHidden ? offered.pawn : null,
                    pawnHidden = offered != null && offered.detailsHidden && offered.pawn != null,
                    things = goods != null ? goods.ItemsListForReading : null
                });
            }
        }

        // -------------------------------------------------------------------------------------------
        // Active
        // -------------------------------------------------------------------------------------------

        /// <summary>Every running quest, soonest to end first.</summary>
        internal static List<ActiveRow> Active(List<ActiveRow> into)
        {
            into.Clear();

            List<Quest> quests = Of(QuestList.Active);

            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];

                ActiveRow row = new ActiveRow
                {
                    quest = quest,
                    name = Name(quest),
                    factions = Factions(quest),
                    ends = Soonest(quest),
                    reserved = new List<Pawn>()
                };

                row.where = Where(quest, out row.target);

                Reserved(quest, row.reserved);

                into.Add(row);
            }

            into.Sort((a, b) => a.ends.CompareTo(b.ends));

            return into;
        }

        /// <summary>
        /// The colonists a running quest is holding.
        ///
        /// <b>Asked of the quest rather than worked out from the map.</b> <c>QuestReserves</c> exists so the
        /// game can stop you sending somebody it has already promised elsewhere, which makes it the same
        /// question this row is asking and the only answer guaranteed to agree with the game's.
        /// </summary>
        private static void Reserved(Quest quest, List<Pawn> into)
        {
            List<Pawn> colonists = UIGuard.Try("Quests.Colonists",
                () => PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists, null, null);

            for (int i = 0; colonists != null && i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];

                if (pawn == null)
                    continue;

                if (UIGuard.Try("Quests.Reserves", () => quest.QuestReserves(pawn), false, null))
                    into.Add(pawn);
            }
        }

        // -------------------------------------------------------------------------------------------
        // Deadlines
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Every clock actually running on a quest, soonest first.
        ///
        /// <b>A quest does not have an end tick; its parts do.</b> The date a quest finishes lives on
        /// <c>QuestPart_Delay</c> as <c>enableTick + delayTicks</c>, and a quest can carry none, one or several
        /// of them: a lodger leaving and a raid arriving are two separate delays on the same quest.
        ///
        /// <b>Only enabled ones count.</b> <c>TicksLeft</c> answers zero when the part is not
        /// <c>QuestPartState.Enabled</c>, because <c>enableTick</c> is not set until the part starts. A delay
        /// waiting on a signal has no date yet, and reading one out of it would put a deadline of "now" on a
        /// bar for something that has not begun.
        ///
        /// <b>Only bad ones are shown,</b> which is the filter vanilla's own readout uses. A delay that is not
        /// <c>isBad</c> is internal pacing rather than a deadline the player is running against, and
        /// <c>expiryInfoPart</c> being empty is the content saying this one has no player-facing wording.
        ///
        /// <b>Where this goes further than vanilla is in taking all of them.</b> <c>GetShortTimeInfo</c> returns
        /// the first match and stops, which is right for one line in a narrow column and wrong for a chart whose
        /// whole purpose is showing that two clocks overlap.
        /// </summary>
        internal static List<DeadlineRow> Deadlines(Quest quest, List<DeadlineRow> into)
        {
            into.Clear();

            List<QuestPart> parts = UIGuard.Try("Quests.Parts", () => quest.PartsListForReading, null, null);

            for (int i = 0; parts != null && i < parts.Count; i++)
            {
                QuestPart_Delay delay = parts[i] as QuestPart_Delay;

                if (delay == null || delay.isBad == false || delay.expiryInfoPart.NullOrEmpty())
                    continue;

                if (UIGuard.Try("Quests.DelayState", () => delay.State, QuestPartState.Disabled, null)
                    != QuestPartState.Enabled)
                    continue;

                int left = UIGuard.Try("Quests.DelayLeft", () => delay.TicksLeft, 0, null);

                if (left <= 0)
                    continue;

                into.Add(new DeadlineRow
                {
                    ticks = left,
                    text = UIGuard.Try("Quests.DelayText", () => delay.ExpiryInfoPart, null, null)
                });
            }

            into.Sort((a, b) => a.ticks.CompareTo(b.ticks));

            return into;
        }

        /// <summary>
        /// Where a quest is pointing, and how far off that is.
        ///
        /// <b>This is the useful fact about a quest with no deadline,</b> and most of them have none. A site
        /// quest is not waiting on a clock; it is waiting on you to walk there. Nine rows all reading "running
        /// to no deadline" say nothing, where nine rows naming a place and a distance are a list of somewhere
        /// to go. Reported on 2026-08-30.
        ///
        /// <b>Taken from the quest's own look targets,</b> the same set vanilla lists at the bottom of its
        /// detail pane, so a target this mod has never heard of still names itself.
        ///
        /// <b>A world target is preferred over a thing on a map.</b> Both are valid targets and a quest often
        /// has several, but the one worth a line is the one that says where to send a caravan; a pawn or an
        /// item already on your map is somewhere you can see.
        /// </summary>
        internal static string Where(Quest quest, out GlobalTargetInfo target)
        {
            GlobalTargetInfo found = GlobalTargetInfo.Invalid;

            target = GlobalTargetInfo.Invalid;

            string label = UIGuard.Try("Quests.Targets", () =>
            {
                foreach (GlobalTargetInfo candidate in quest.QuestLookTargets)
                {
                    if (!candidate.IsValid)
                        continue;

                    if (!found.IsValid || (candidate.HasWorldObject && !found.HasWorldObject))
                        found = candidate;
                }

                return found.IsValid ? found.Label : null;
            }, null, null);

            if (label.NullOrEmpty())
                return null;

            target = found;

            return UIGuard.Try("Quests.Distance", () =>
            {
                if (!found.IsWorldTarget)
                    return label;

                Map home = Find.AnyPlayerHomeMap;

                if (home == null)
                    return label;

                int tiles = Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(home.Tile, found.Tile));

                if (tiles <= 0)
                    return label;

                return label + ", " + tiles + (tiles == 1 ? " tile away" : " tiles away");
            }, label, null);
        }

        /// <summary>The soonest clock on a quest, or int.MaxValue when it is running to no date at all.</summary>
        internal static int Soonest(Quest quest)
        {
            List<DeadlineRow> found = Deadlines(quest, Clocks);

            return found.Count == 0 ? int.MaxValue : found[0].ticks;
        }

        private static readonly List<DeadlineRow> Clocks = new List<DeadlineRow>();

        // -------------------------------------------------------------------------------------------
        // History
        // -------------------------------------------------------------------------------------------

        /// <summary>Every finished quest, most recently finished first.</summary>
        internal static List<HistoryRow> History(List<HistoryRow> into)
        {
            into.Clear();

            List<Quest> quests = Of(QuestList.History);

            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];

                QuestState state = UIGuard.Try("Quests.State", () => quest.State, QuestState.EndedInvalid, null);

                into.Add(new HistoryRow
                {
                    quest = quest,
                    name = Name(quest),
                    state = state,
                    outcome = Outcome(state),
                    ago = UIGuard.Try("Quests.Cleanup", () => quest.TicksSinceCleanup, 0, null)
                });
            }

            into.Sort((a, b) => a.ago.CompareTo(b.ago));

            return into;
        }

        private static string Outcome(QuestState state)
        {
            switch (state)
            {
                case QuestState.EndedSuccess: return "completed";
                case QuestState.EndedFailed: return "failed";
                case QuestState.EndedOfferExpired: return "expired";
                case QuestState.EndedInvalid: return "no longer valid";
                default: return "ended";
            }
        }

        // -------------------------------------------------------------------------------------------
        // Shared reads
        // -------------------------------------------------------------------------------------------

        internal static string Name(Quest quest)
        {
            string name = UIGuard.Try("Quests.Name", () => quest.name, null, null);

            return name.NullOrEmpty() ? "Unnamed quest" : name;
        }

        /// <summary>
        /// Who the quest is with, as one line.
        ///
        /// <b>Suppressed when the quest asks for it.</b> <c>QuestScriptDef.hideInvolvedFactionsInfo</c> is
        /// content saying the parties are not the player's business yet, and vanilla's own pane checks it
        /// before drawing the faction block.
        /// </summary>
        internal static string Factions(Quest quest)
        {
            return UIGuard.Try("Quests.Factions", () =>
            {
                if (quest.root != null && quest.root.hideInvolvedFactionsInfo)
                    return null;

                string joined = null;

                List<QuestPart> parts = quest.PartsListForReading;

                for (int i = 0; parts != null && i < parts.Count; i++)
                {
                    QuestPart part = parts[i];

                    if (part == null || part.InvolvedFactions == null)
                        continue;

                    foreach (Faction faction in part.InvolvedFactions)
                    {
                        if (faction == null || faction.IsPlayer || faction.Hidden)
                            continue;

                        string label = faction.Name;

                        if (label.NullOrEmpty())
                            continue;

                        if (joined == null)
                            joined = label;
                        else if (joined.IndexOf(label, System.StringComparison.Ordinal) < 0)
                            joined += ", " + label;
                    }
                }

                return joined;
            }, null, null);
        }

        /// <summary>A tick count as a period, or a dash when there is no clock on it.</summary>
        internal static string Period(int ticks)
        {
            if (ticks == int.MaxValue || ticks < 0)
                return "--";

            return UIGuard.Try("Quests.Period", () => ticks.ToStringTicksToPeriod(), "--", null);
        }
    }

    /// <summary>The four lists the rail switches between.</summary>
    internal enum QuestList
    {
        Offers,
        Active,
        History,
        Dismissed
    }
}
