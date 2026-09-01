using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Factions
{
    /// <summary>
    /// The four things worth doing to a faction from the tab that lists them.
    ///
    /// <b>Vanilla's factions tab does nothing at all.</b> Clicking a row opens an info card and that is the
    /// whole of it: calling a faction means finding the comms console, finding a colonist and right clicking,
    /// and finding one on the map means hunting the planet by eye. Both of those are decisions made while
    /// reading this screen, so both belong on it.
    ///
    /// <b>Nothing here shortcuts a rule.</b> Calling gives a colonist the same job the console's own float
    /// menu gives them, so they still walk there, the console still needs power, and the call still fails for
    /// every reason it would have failed for. What is saved is the walking about looking for the console, not
    /// the walking of the colonist.
    /// </summary>
    internal static class FactionActions
    {
        /// <summary>The game's own card for a faction, which is what a row click does today.</summary>
        internal static void Inspect(Faction faction)
        {
            UIGuard.Try("Factions.Inspect", () =>
            {
                if (faction != null)
                    Find.WindowStack.Add(new Dialog_InfoCard(faction));
            }, "That faction's info card could not be opened.");
        }

        /// <summary>
        /// Puts the planet view on one of their settlements and selects it.
        ///
        /// Nearest is not worked out: a faction's settlements are all somewhere on the same planet and the
        /// point of the jump is to find the faction, not to route a caravan. The first is enough, and the
        /// player can see the rest around it once the camera is there.
        /// </summary>
        internal static void ShowOnMap(Faction faction)
        {
            UIGuard.Try("Factions.ShowOnMap", () =>
            {
                Settlement settlement = FactionsFacts.AnySettlement(faction);

                if (settlement == null)
                    return;

                CameraJumper.TryJumpAndSelect(settlement);
            }, "The planet view could not be moved to that faction.");
        }

        /// <summary>
        /// Why a faction cannot be called right now, or null when they can.
        ///
        /// <b>The reasons are the console's, not ours.</b> A missing console, a console with no power, a solar
        /// flare and a faction whose leader is unreachable are all conditions RimWorld already enforces at the
        /// comms console; this asks the same questions in the same order so the button never offers a call the
        /// game would refuse.
        /// </summary>
        internal static string CallProblem(Faction faction)
        {
            return UIGuard.Try<string>("Factions.CallProblem", () =>
            {
                if (faction == null || faction.IsPlayer)
                    return "There is nobody to call.";

                if (faction.defeated)
                    return "They have nobody left to answer.";

                if (faction.leader == null)
                    return "They have no leader to speak to.";

                Map map = Find.CurrentMap;

                if (map == null)
                    return "A call is placed from a map, and there is none open.";

                Building_CommsConsole console = Console(map);

                if (console == null)
                    return "This map has no comms console that works.";

                return Negotiator(map, console) == null
                    ? "Nobody here can get to the console and talk."
                    : null;
            }, null, null);
        }

        /// <summary>
        /// Sends somebody to the comms console to call them.
        ///
        /// <b>The same job the console's own menu gives.</b> <c>GiveUseCommsJob</c> is what a right click on
        /// the console does, so the colonist walks there, the negotiation window opens when they arrive, and
        /// everything that could interrupt it still can.
        /// </summary>
        internal static void Call(Faction faction)
        {
            UIGuard.Try("Factions.Call", () =>
            {
                Map map = Find.CurrentMap;

                if (map == null || faction == null)
                    return;

                Building_CommsConsole console = Console(map);

                if (console == null)
                    return;

                Pawn negotiator = Negotiator(map, console);

                if (negotiator == null)
                    return;

                console.GiveUseCommsJob(negotiator, faction);

                Find.MainTabsRoot.EscapeCurrentTab();

                Messages.Message(negotiator.LabelShort + " is going to call " + faction.Name.CapitalizeFirst()
                                 + ".", negotiator, MessageTypeDefOf.TaskCompletion, false);
            }, "That call could not be placed.");
        }

        /// <summary>The first comms console on the map that could take a call right now.</summary>
        private static Building_CommsConsole Console(Map map)
        {
            foreach (Building_CommsConsole console in
                     map.listerBuildings.AllBuildingsColonistOfClass<Building_CommsConsole>())
            {
                if (console != null && console.Spawned && console.CanUseCommsNow)
                    return console;
            }

            return null;
        }

        /// <summary>
        /// Who to send: the free colonist with the best social skill who can actually get there and speak.
        ///
        /// <b>Best rather than nearest,</b> because the skill changes what the call is worth and the walk
        /// changes only when it happens. This is the choice a player makes by hand at the console, and making
        /// it for them is the only part of this that is a convenience rather than a shortcut.
        /// </summary>
        private static Pawn Negotiator(Map map, Building_CommsConsole console)
        {
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;

            Pawn best = null;
            int bestSkill = -1;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];

                if (pawn == null || pawn.Downed || pawn.Drafted || pawn.InMentalState)
                    continue;

                if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                    continue;

                if (!pawn.CanReserveAndReach(console, PathEndMode.InteractionCell, Danger.Some))
                    continue;

                int skill = Skill(pawn);

                if (skill <= bestSkill)
                    continue;

                bestSkill = skill;
                best = pawn;
            }

            return best;
        }

        private static int Skill(Pawn pawn)
        {
            return UIGuard.Try("Factions.Skill",
                () => pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0, 0, null);
        }

        /// <summary>
        /// How many quests on offer or under way involve this faction.
        ///
        /// <b>Worked out only for the faction whose card is open,</b> because <c>InvolvedFactions</c> walks
        /// every part of every quest and doing that for a dozen rows a frame would be a real cost for a figure
        /// most of them would never show.
        /// </summary>
        internal static int QuestCount(Faction faction)
        {
            return UIGuard.Try("Factions.Quests", () =>
            {
                QuestManager manager = Find.QuestManager;

                if (manager == null || faction == null)
                    return 0;

                List<Quest> quests = manager.QuestsListForReading;
                int count = 0;

                for (int i = 0; i < quests.Count; i++)
                {
                    Quest quest = quests[i];

                    if (quest == null || quest.hidden)
                        continue;

                    if (quest.State != QuestState.Ongoing && quest.State != QuestState.NotYetAccepted)
                        continue;

                    foreach (Faction involved in quest.InvolvedFactions)
                    {
                        if (involved != faction)
                            continue;

                        count++;

                        break;
                    }
                }

                return count;
            }, 0, null);
        }

        /// <summary>Opens whichever quests tab this install has, ours by preference.</summary>
        internal static void ShowQuests()
        {
            UIGuard.Try("Factions.ShowQuests", () =>
            {
                MainButtonDef tab = Quests.QuestTabs.Available
                    ? Quests.QuestTabs.Ours()
                    : DefDatabase<MainButtonDef>.GetNamedSilentFail(Quests.QuestTabs.VanillaDefName);

                if (tab != null)
                    Find.MainTabsRoot.SetCurrentTab(tab);
            }, "The quests tab could not be opened.");
        }
    }
}
