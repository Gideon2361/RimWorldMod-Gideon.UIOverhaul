using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>One control group, with the mechs assigned to it.</summary>
    internal sealed class MechGroupEntry
    {
        internal MechanitorControlGroup Group;

        internal Pawn Overseer;

        /// <summary>The group's own one-based number, which is what the gizmo calls it.</summary>
        internal int Index;

        internal MechWorkModeDef Mode;

        internal readonly List<Pawn> Mechs = new List<Pawn>();

        /// <summary>Bandwidth the mechs in this group are between them occupying.</summary>
        internal int Band;

        internal string Key
        {
            get { return (Overseer == null ? "?" : Overseer.ThingID) + "/" + Index; }
        }
    }

    /// <summary>One mechanitor, their bandwidth and their groups.</summary>
    internal sealed class MechanitorEntry
    {
        internal Pawn Pawn;

        internal int Used;

        internal int Total;

        /// <summary>The part of <see cref="Used"/> that is reserved by a gestation rather than spent on a mech.</summary>
        internal int FromGestation;

        internal readonly List<MechGroupEntry> Groups = new List<MechGroupEntry>();

        internal string Key
        {
            get { return Pawn == null ? "?" : Pawn.ThingID; }
        }
    }

    /// <summary>One mech being formed in a gestator, which is spending bandwidth without existing yet.</summary>
    internal sealed class MechGestationEntry
    {
        internal Pawn Overseer;

        internal ThingDef Produced;

        internal int Band;

        internal string State;
    }

    /// <summary>
    /// The mechanitor tree, rebuilt once a frame.
    ///
    /// <b>The tree is the point.</b> RimWorld's own mech tab is a <c>PawnTable</c> of ten columns, three of
    /// which repeat one fact per row: overseer, control group and work mode all belong to the group rather
    /// than to the mech. Eight mechs in three groups print the overseer's name eight times to say something
    /// there were only two of. Building the tree once and drawing it as a tree costs less than the table did
    /// and says more.
    ///
    /// <b>Unlinked mechs are in here and are not in vanilla's.</b> Its <c>Pawns</c> filters on
    /// <c>p.OverseerSubject != null</c>, so a mech that lost its mechanitor, which is the exact situation you
    /// open the tab to fix, is the one case the tab hides.
    ///
    /// <b>Once a frame, because two of these reads are not cheap.</b>
    /// <c>Pawn_MechanitorTracker.ActiveMechBills</c> walks every gestator on every map each time it is
    /// touched, and <c>UsedBandwidthFromSubjects</c> runs a stat lookup per overseen mech. Both are fine
    /// once and wasteful forty times.
    /// </summary>
    internal static class MechRoster
    {
        internal static readonly List<MechanitorEntry> Mechanitors = new List<MechanitorEntry>();

        /// <summary>Player faction mechs on this map with no overseer at all.</summary>
        internal static readonly List<Pawn> Unlinked = new List<Pawn>();

        internal static readonly List<MechGestationEntry> Gestating = new List<MechGestationEntry>();

        internal static int UsedBandwidth;

        internal static int TotalBandwidth;

        internal static int MechCount;

        internal static int GroupCount;

        /// <summary>
        /// Groups with nothing in them, which is what the Empty groups chip counts.
        ///
        /// Usually most of them. The base mechlink grants <c>MechControlGroups 2</c> and
        /// <c>Notify_ControlGroupAmountMayChanged</c> creates every group the stat allows the moment a
        /// mechanitor exists, so a colony of three mechanitors owns six groups before it owns one mech.
        /// </summary>
        internal static int EmptyGroupCount;

        /// <summary>Mean charge across every mech with an energy need, as a whole percent.</summary>
        internal static int MeanCharge;

        internal static int DamagedCount;

        internal static int ChargingCount;

        internal static int LowChargeCount;

        internal static int DraftedCount;

        internal static int HibernatingCount;

        private static readonly Dictionary<string, int> ModeCounts = new Dictionary<string, int>();

        private static int builtFrame = -1;

        /// <summary>How many mechs are in groups set to this work mode.</summary>
        internal static int CountFor(MechWorkModeDef mode)
        {
            int count;

            return mode != null && ModeCounts.TryGetValue(mode.defName, out count) ? count : 0;
        }

        /// <summary>Rebuilds the snapshot, at most once per frame however many callers ask.</summary>
        internal static void Build()
        {
            if (builtFrame == Time.frameCount)
                return;

            builtFrame = Time.frameCount;

            UIGuard.Try("Mechs.Roster", Gather,
                "The mech tab could not read this colony's mechanitors. The list is empty until the next "
                + "frame that can.");
        }

        /// <summary>Forgets the snapshot, so the next draw rebuilds it whatever frame it is.</summary>
        internal static void Invalidate()
        {
            builtFrame = -1;
        }

        private static void Gather()
        {
            Reset();

            Map map = Find.CurrentMap;

            if (map == null || map.mapPawns == null)
                return;

            List<Pawn> pawns = map.mapPawns.PawnsInFaction(Faction.OfPlayer);

            if (pawns == null)
                return;

            float chargeTotal = 0f;
            int chargeCount = 0;

            // Mechanitors first, so a mech can be filed under one. Their order is the map's, which is stable
            // within a session and is the same order every other census screen in this mod uses.
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];

                if (pawn == null || pawn.Dead || !MechanitorUtility.IsMechanitor(pawn))
                    continue;

                Pawn_MechanitorTracker tracker = pawn.mechanitor;

                if (tracker == null)
                    continue;

                MechanitorEntry entry = new MechanitorEntry
                {
                    Pawn = pawn,
                    Used = tracker.UsedBandwidth,
                    Total = tracker.TotalBandwidth,
                    FromGestation = tracker.UsedBandwidthFromGestation
                };

                List<MechanitorControlGroup> groups = tracker.controlGroups;

                for (int g = 0; groups != null && g < groups.Count; g++)
                {
                    MechanitorControlGroup group = groups[g];

                    if (group == null)
                        continue;

                    MechGroupEntry built = new MechGroupEntry
                    {
                        Group = group,
                        Overseer = pawn,
                        Index = g + 1,
                        Mode = group.WorkMode
                    };

                    List<Pawn> mechs = group.MechsForReading;

                    for (int m = 0; mechs != null && m < mechs.Count; m++)
                    {
                        Pawn mech = mechs[m];

                        if (mech == null || mech.Dead)
                            continue;

                        built.Mechs.Add(mech);
                        built.Band += MechFacts.BandwidthCost(mech);
                    }

                    entry.Groups.Add(built);

                    if (built.Mechs.Count == 0)
                        EmptyGroupCount++;
                }

                Mechanitors.Add(entry);

                UsedBandwidth += entry.Used;
                TotalBandwidth += entry.Total;
                GroupCount += entry.Groups.Count;

                Gestations(tracker, pawn);
            }

            // Then every mech, so the counts cover the ones no group holds as well as the ones that do.
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn mech = pawns[i];

                if (mech == null || mech.Dead || mech.RaceProps == null || !mech.RaceProps.IsMechanoid)
                    continue;

                if (mech.IsGestating())
                    continue;

                if (mech.GetOverseer() == null)
                    Unlinked.Add(mech);

                MechCount++;

                float charge = MechFacts.Charge(mech);

                if (charge >= 0f)
                {
                    chargeTotal += charge;
                    chargeCount++;

                    if (charge * 100f <= MechFacts.ShutdownAt)
                        LowChargeCount++;
                }

                if (MechFacts.Flow(mech) == Shared.ChargeFlow.Charging)
                    ChargingCount++;

                if (MechFacts.Integrity(mech) < 0.999f)
                    DamagedCount++;

                if (mech.Drafted)
                    DraftedCount++;

                if (MechFacts.Hibernating(mech))
                    HibernatingCount++;

                Tally(mech);
            }

            MeanCharge = chargeCount == 0 ? 0 : Mathf.RoundToInt(chargeTotal / chargeCount * 100f);
        }

        /// <summary>Files a mech under its group's work mode, for the strip's chips.</summary>
        private static void Tally(Pawn mech)
        {
            MechWorkModeDef mode = mech.GetMechWorkMode();

            if (mode == null)
                return;

            int count;

            ModeCounts.TryGetValue(mode.defName, out count);
            ModeCounts[mode.defName] = count + 1;
        }

        private static void Gestations(Pawn_MechanitorTracker tracker, Pawn overseer)
        {
            List<Bill_Mech> bills = tracker.ActiveMechBills;

            for (int i = 0; bills != null && i < bills.Count; i++)
            {
                Bill_Mech bill = bills[i];

                if (bill == null || bill.recipe == null)
                    continue;

                Gestating.Add(new MechGestationEntry
                {
                    Overseer = overseer,
                    Produced = bill.recipe.ProducedThingDef,
                    Band = Mathf.RoundToInt(bill.BandwidthCost),
                    State = bill.State.ToString().ToLowerInvariant()
                });
            }
        }

        private static void Reset()
        {
            Mechanitors.Clear();
            Unlinked.Clear();
            Gestating.Clear();
            ModeCounts.Clear();

            UsedBandwidth = 0;
            TotalBandwidth = 0;
            MechCount = 0;
            GroupCount = 0;
            EmptyGroupCount = 0;
            MeanCharge = 0;
            DamagedCount = 0;
            ChargingCount = 0;
            LowChargeCount = 0;
            DraftedCount = 0;
            HibernatingCount = 0;
        }
    }
}
