using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>Why a bill cannot run, when it cannot.</summary>
    internal enum BillTrouble
    {
        None,

        /// <summary>Nobody in the colony is allowed to start it.</summary>
        NoWorker
    }

    /// <summary>One bill, with the bench it belongs to and whatever is wrong with it.</summary>
    internal sealed class BillEntry
    {
        internal Bill_Production Bill;
        internal Building_WorkTable Bench;
        internal BillTrouble Trouble;

        /// <summary>The player's own name for it when they set one, otherwise the recipe's.</summary>
        internal string Label => Bill?.LabelCap;

        internal bool Suspended => Bill != null && Bill.suspended;
    }

    /// <summary>Every bill on one bench.</summary>
    internal sealed class BillGroup
    {
        internal Building_WorkTable Bench;
        internal string Label;

        /// <summary>Room and map, so two identical benches can be told apart.</summary>
        internal string Where;

        internal List<BillEntry> Bills = new List<BillEntry>();
    }

    /// <summary>
    /// Every bill in the colony, gathered so one window can answer "where is that bill?".
    ///
    /// <b>Gathered on demand, never per frame.</b> Walking every map's worktables and asking each bill whether
    /// anybody can work it is far too much to do while drawing. The window collects once when it opens and again
    /// when something changes, and draws from the result.
    ///
    /// <b>Only one kind of trouble is reported, and that is a deliberate retreat from the mockup.</b> The approved
    /// design also flagged a bill with no allowed ingredient inside its search radius. Every cheap way to test that
    /// is wrong in the dangerous direction: the resource counter sees only what is in a stockpile, so a bill whose
    /// steel is sitting on the floor would be reported as broken when it is fine, and a truthful test means walking
    /// the cells in radius, which at vanilla's default radius of 999 is the whole map per bill. Reporting a healthy
    /// bill as broken is the one outcome this feature must not produce, so the check waits until it can be done
    /// properly rather than shipping a guess.
    /// </summary>
    internal static class BillCatalog
    {
        /// <summary>
        /// Bumped whenever a bill is added to or removed from any stack in the game.
        ///
        /// <b>Because "collect again after anything that could change the list" is a promise made at call
        /// sites,</b> and Aaron found the one that had not been made: importing a bench template into a bench
        /// left the colony window showing the bills it had before. Suspending, deleting and reordering each
        /// remembered to re-read; adding did not, and every future way of adding one would have had to remember
        /// too.
        ///
        /// A counter the window compares against is the version that cannot be forgotten, because what bumps it
        /// is <c>BillStack</c> itself -- so our importer, our wizard, vanilla's own float menu and any other
        /// mod's route all mark the list stale by doing the thing rather than by announcing it. See
        /// <c>Patch_BillStackChanged</c>.
        /// </summary>
        internal static int Stamp { get; private set; }

        /// <summary>Called from the patch below. Wrapping is not worth a method, but the setter being private is.</summary>
        internal static void Notify_BillsChanged()
        {
            Stamp++;
        }

        /// <summary>
        /// Every bench with at least one bill, across every map.
        ///
        /// <b>No per bench mode.</b> There used to be one, because clicking Bills on a workbench opened this
        /// window filtered to that bench. A bench now has its own card list on its own tab, so a filter parameter
        /// nothing passes would be a promise this class no longer keeps. See <c>Patch_BillsTabFill</c>.
        /// </summary>
        internal static List<BillGroup> Collect()
        {
            return UIGuard.Try("Bills.Catalog", Gather, new List<BillGroup>(),
                "The colony's bills could not be listed.");
        }

        private static List<BillGroup> Gather()
        {
            List<BillGroup> groups = new List<BillGroup>();
            List<Map> maps = Find.Maps;

            if (maps == null)
                return groups;

            bool many = maps.Count > 1;

            foreach (Map map in maps)
            {
                List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;

                if (buildings == null)
                    continue;

                foreach (Building building in buildings)
                {
                    if (!(building is Building_WorkTable bench))
                        continue;

                    BillGroup group = Read(bench, map, many);

                    if (group != null)
                        groups.Add(group);
                }
            }

            return groups;
        }

        private static BillGroup Read(Building_WorkTable bench, Map map, bool many)
        {
            List<Bill> bills = bench.billStack?.Bills;

            if (bills == null)
                return null;

            BillGroup group = new BillGroup
            {
                Bench = bench,
                Label = bench.LabelCap,
                Where = Where(bench, map, many)
            };

            foreach (Bill bill in bills)
            {
                // Only production bills carry the configuration this window edits. Anything else a mod has put on
                // a bench is left alone rather than half drawn.
                if (!(bill is Bill_Production production))
                    continue;

                group.Bills.Add(new BillEntry
                {
                    Bill = production,
                    Bench = bench,
                    Trouble = Diagnose(production, map)
                });
            }

            return group.Bills.Count == 0 ? null : group;
        }

        /// <summary>The room the bench is in, plus the map when there is more than one.</summary>
        private static string Where(Building_WorkTable bench, Map map, bool many)
        {
            string room = null;

            Room found = bench.Position.GetRoom(map);

            if (found != null && !found.PsychologicallyOutdoors)
                room = found.Role?.LabelCap;

            string place = map?.Parent?.LabelCap;

            if (!many || place.NullOrEmpty())
                return room;

            return room.NullOrEmpty() ? place : room + " - " + place;
        }

        /// <summary>
        /// Whether anybody can start this bill.
        ///
        /// <b>Asked of the game rather than reimplemented.</b> <c>PawnAllowedToStartAnew</c> already accounts for a
        /// restriction to one pawn, for the slave, mech and non mech flags, and for the skill range, so asking it
        /// once per colonist is both exact and cheaper than getting the same rules subtly wrong here.
        ///
        /// A suspended bill is not in trouble. The player switched it off on purpose and does not need telling.
        /// </summary>
        internal static BillTrouble Diagnose(Bill_Production bill, Map map)
        {
            if (bill == null || bill.suspended || map == null)
                return BillTrouble.None;

            if (!Warn())
                return BillTrouble.None;

            List<Pawn> colonists = map.mapPawns?.FreeColonistsSpawned;

            if (colonists == null || colonists.Count == 0)
                return BillTrouble.None;

            foreach (Pawn pawn in colonists)
            {
                if (bill.PawnAllowedToStartAnew(pawn))
                    return BillTrouble.None;
            }

            return BillTrouble.NoWorker;
        }

        /// <summary>Whether the player wants stalled bills pointed out at all.</summary>
        private static bool Warn()
        {
            return UIGuard.Try("Bills.ReadWarnSetting",
                () => Options.UIOverhaulSettingsFile.Current?.warnStalledBills ?? true, true, null);
        }

        /// <summary>How many bills across the whole colony are in trouble, for the title bar.</summary>
        internal static int Troubled(List<BillGroup> groups)
        {
            int count = 0;

            if (groups == null)
                return 0;

            foreach (BillGroup group in groups)
            {
                foreach (BillEntry entry in group.Bills)
                {
                    if (entry.Trouble != BillTrouble.None)
                        count++;
                }
            }

            return count;
        }

        internal static int Total(List<BillGroup> groups)
        {
            int count = 0;

            if (groups == null)
                return 0;

            foreach (BillGroup group in groups)
                count += group.Bills.Count;

            return count;
        }
    }
}
