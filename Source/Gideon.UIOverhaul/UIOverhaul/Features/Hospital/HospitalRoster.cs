using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Pawns;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// Which of the tab's sections a patient belongs in.
    ///
    /// <b>The order of this enum is the order of the screen, and it is the order you would work in.</b> Somebody
    /// bleeding on the floor before somebody whose infection is being fought, before an operation waiting on a
    /// surgeon, before somebody who is simply asleep and getting better. A patient appears in exactly one, chosen
    /// by the worst thing true about them.
    /// </summary>
    internal enum HospitalTriage
    {
        /// <summary>Losing something you cannot give back: bleeding out, downed on the floor, freezing.</summary>
        Critical,

        /// <summary>Something is being fought: a disease, an untended wound, a tend running out.</summary>
        InTreatment,

        /// <summary>Nothing is wrong that a doctor is treating; an operation is queued and waiting.</summary>
        AwaitingSurgery,

        /// <summary>In a bed, healing, and nothing is holding it up.</summary>
        Recovering,

        /// <summary>Colony animals, whatever their state. Their own section rather than mixed through.</summary>
        Animals,

        /// <summary>Everybody else, and only when the toolbar toggle asks for them.</summary>
        Healthy
    }

    /// <summary>
    /// One patient, read once per rebuild so the columns are drawing figures rather than computing them.
    ///
    /// Every field here is something a column shows. Nothing is stored that only one caller wants, and nothing is
    /// recomputed in a cell: a table of thirty rows redrawn sixty times a second cannot afford to ask a pawn for
    /// their bleed rate ninety times.
    /// </summary>
    internal sealed class HospitalPatient
    {
        internal Pawn Pawn;

        internal Map Map;

        /// <summary>The same reading the pawns tab and the colonist bar use, so nothing can disagree.</summary>
        internal PawnHealthSummary Summary;

        /// <summary>Overall health, 0 to 1, as the game's own summary computes it.</summary>
        internal float Health;

        internal float Pain;

        /// <summary>Blood lost per day. Zero when nothing is open.</summary>
        internal float Bleeding;

        internal Building_Bed Bed;

        internal bool InMedicalBed;

        internal HospitalTriage Triage;

        /// <summary>Queued operations that change the body: implants, removals, amputations.</summary>
        internal int Operations;

        /// <summary>Queued doses: bills whose recipe only administers something.</summary>
        internal int Doses;

        /// <summary>Standing orders pointed at this patient, whether by name or by a colony wide target.</summary>
        internal int StandingOrders;

        internal bool Animal;

        /// <summary>A hospital mod's paying patient. Only ever true with one of the two mods installed.</summary>
        internal bool Visiting;

        /// <summary>Colony Hospital's own word for where this patient is up to, or null.</summary>
        internal string VisitStatus;

        /// <summary>What they owe so far, or negative when nobody is billing them.</summary>
        internal int VisitBill;

        /// <summary>What is being done, or what is holding it up. See <see cref="HospitalTreatment"/>.</summary>
        internal HospitalTreatment Treatment;

        internal void Reset()
        {
            Pawn = null;
            Map = null;
            Summary = default(PawnHealthSummary);
            Health = 1f;
            Pain = 0f;
            Bleeding = 0f;
            Bed = null;
            InMedicalBed = false;
            Triage = HospitalTriage.Healthy;
            Operations = 0;
            Doses = 0;
            StandingOrders = 0;
            Animal = false;
            Visiting = false;
            VisitStatus = null;
            VisitBill = -1;
            Treatment = default(HospitalTreatment);
        }
    }

    /// <summary>One triage section, and the patients in it across every map.</summary>
    internal sealed class HospitalSection
    {
        internal HospitalTriage Triage;

        internal string Label;

        internal readonly List<HospitalPatient> Patients = new List<HospitalPatient>();

        internal int Count
        {
            get { return Patients.Count; }
        }
    }

    /// <summary>
    /// Who the colony's doctors have to think about, sorted into what you would do about each one.
    ///
    /// <b>The rule for who appears is Aaron's, and it is a rule about the patient rather than about the colony.</b>
    /// Anything other than healthy, or an operation queued on them, or a bed somebody deliberately marked medical,
    /// or a standing order pointed at them, or a hospital mod calling them a patient. Everybody else is absent,
    /// because a hospital tab listing eleven healthy colonists has hidden the two who are not. The toolbar's
    /// toggle brings them back for the once-a-quadrum question of who is fit to travel.
    ///
    /// <b>Animals are a section rather than a mixed-in row.</b> They take beds, medicine and a doctor's time, so
    /// they belong on this screen; they are also read completely differently, so they sit under the people rather
    /// than between them.
    ///
    /// <b>Every loaded map.</b> A gravship site with a wounded crew is exactly the case where you most want one
    /// list, and the pawns and animals tabs already work this way.
    ///
    /// <b>Rebuilt on the game's clock, not the frame's.</b> Reading a patient walks their hediffs, their bills and
    /// their bed, none of which can change while the game is paused. Twice a game second, plus an
    /// <see cref="Invalidate"/> from anything the player does through the tab. The same cache policy the animals
    /// tab uses, for the same reason.
    /// </summary>
    internal static class HospitalRoster
    {
        /// <summary>Ticks between rebuilds. Thirty is half a second at normal speed and nothing while paused.</summary>
        private const int RebuildIntervalTicks = 30;

        private static readonly List<HospitalSection> Built = new List<HospitalSection>();

        /// <summary>Patient objects not currently in use, so a rebuild does not allocate thirty of them.</summary>
        private static readonly List<HospitalPatient> Spare = new List<HospitalPatient>();

        private static int builtAt = -99999;
        private static bool dirty = true;
        private static bool subscribed;

        /// <summary>Whether the toolbar toggle is asking for the whole colony rather than only its patients.</summary>
        internal static bool ShowEverybody;

        /// <summary>
        /// The current sections, rebuilt first if they are stale.
        ///
        /// The caller may hold this list for the length of a draw and no longer: a rebuild reuses the same patient
        /// objects, so a reference kept across frames would quietly start describing somebody else.
        /// </summary>
        internal static List<HospitalSection> Sections
        {
            get
            {
                Subscribe();

                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

                if (dirty || now - builtAt >= RebuildIntervalTicks || now < builtAt)
                {
                    builtAt = now;
                    dirty = false;

                    UIGuard.Try("Hospital.Gather", Rebuild,
                        "The hospital tab could not finish reading the colony, so the list may be incomplete "
                        + "until it refreshes.");
                }

                return Built;
            }
        }

        /// <summary>Forces the next read to rebuild. Called after anything the player does through this tab.</summary>
        internal static void Invalidate()
        {
            dirty = true;
        }

        /// <summary>The patient record for one pawn, or null when they are not on the list.</summary>
        internal static HospitalPatient PatientFor(Pawn pawn)
        {
            if (pawn == null)
                return null;

            for (int s = 0; s < Built.Count; s++)
            {
                List<HospitalPatient> patients = Built[s].Patients;

                for (int i = 0; i < patients.Count; i++)
                {
                    if (patients[i].Pawn == pawn)
                        return patients[i];
                }
            }

            return null;
        }

        private static void Subscribe()
        {
            if (subscribed)
                return;

            subscribed = true;

            UIGuard.Try("Hospital.Subscribe", () =>
            {
                PawnLifecycle.Gone += Forget;
                PawnLifecycle.RosterChanged += Invalidate;
            }, "The hospital tab will not notice people arriving or dying until it is reopened.");
        }

        private static void Forget(Pawn pawn)
        {
            dirty = true;
        }

        // -------------------------------------------------------------------------------------------
        // Gathering
        // -------------------------------------------------------------------------------------------

        private static void Rebuild()
        {
            Recycle();

            List<Map> maps = Verse.Find.Maps;

            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                    GatherMap(maps[i]);
            }

            Sort();
        }

        /// <summary>Empties the sections back into the spare pool rather than dropping them on the collector.</summary>
        private static void Recycle()
        {
            for (int s = 0; s < Built.Count; s++)
            {
                List<HospitalPatient> patients = Built[s].Patients;

                for (int i = 0; i < patients.Count; i++)
                {
                    patients[i].Reset();
                    Spare.Add(patients[i]);
                }

                patients.Clear();
            }

            if (Built.Count != 0)
                return;

            // Built once and then reused for the life of the session: the sections are fixed, and only their
            // contents change.
            Add(HospitalTriage.Critical, "Critical");
            Add(HospitalTriage.InTreatment, "In treatment");
            Add(HospitalTriage.AwaitingSurgery, "Awaiting surgery");
            Add(HospitalTriage.Recovering, "Recovering");
            Add(HospitalTriage.Animals, "Animals");
            Add(HospitalTriage.Healthy, "Everybody else");
        }

        private static void Add(HospitalTriage triage, string label)
        {
            Built.Add(new HospitalSection { Triage = triage, Label = label });
        }

        private static void GatherMap(Map map)
        {
            if (map == null || map.mapPawns == null)
                return;

            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;

            if (all == null)
                return;

            for (int i = 0; i < all.Count; i++)
            {
                Pawn pawn = all[i];

                if (pawn == null || pawn.Dead || pawn.Destroyed)
                    continue;

                if (!OurBusiness(pawn))
                    continue;

                HospitalPatient patient = Take();

                Read(patient, pawn, map);

                if (patient.Triage == HospitalTriage.Healthy)
                {
                    if (!ShowEverybody)
                    {
                        patient.Reset();
                        Spare.Add(patient);

                        continue;
                    }

                    // With the roster turned on, a healthy animal still belongs with the animals rather than
                    // among the colonists: the toggle asks for everybody, not for the sections to be abandoned.
                    if (patient.Animal)
                        patient.Triage = HospitalTriage.Animals;
                }

                Section(patient.Triage).Patients.Add(patient);
            }
        }

        /// <summary>
        /// Whether this pawn is somebody the colony's doctors would ever be asked about.
        ///
        /// <b>Wider than "is a colonist" on purpose.</b> A prisoner is treated, a guest lying in one of our beds
        /// is being treated, and a hospital mod's paying visitor is the whole reason those mods exist. A hostile
        /// standing on the map is not, whatever state they are in.
        /// </summary>
        private static bool OurBusiness(Pawn pawn)
        {
            return UIGuard.Try("Hospital.OurBusiness", () =>
            {
                if (pawn.IsColonist || pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony)
                    return true;

                if (pawn.RaceProps != null && pawn.RaceProps.Animal && pawn.Faction != null
                    && pawn.Faction.IsPlayer)
                    return true;

                // Hostiles are rejected before the hospital mods are asked, which is a performance decision as
                // well as a correct one: this runs for every spawned pawn twice a game second, the visiting test
                // is a reflection call, and during a raid most of the map is people who are certainly not
                // patients.
                if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
                    return false;

                if (HospitalIntegrations.IsVisitingPatient(pawn))
                    return true;

                // A guest or a lodger asleep in a bed we built is being looked after by us, whoever they belong
                // to. Anything else that merely happens to be standing here is not.
                Building_Bed bed = pawn.CurrentBed();

                return bed != null && bed.Faction != null && bed.Faction.IsPlayer;
            }, false, null);
        }

        private static HospitalPatient Take()
        {
            if (Spare.Count == 0)
                return new HospitalPatient();

            HospitalPatient patient = Spare[Spare.Count - 1];

            Spare.RemoveAt(Spare.Count - 1);

            return patient;
        }

        private static HospitalSection Section(HospitalTriage triage)
        {
            for (int i = 0; i < Built.Count; i++)
            {
                if (Built[i].Triage == triage)
                    return Built[i];
            }

            return Built[Built.Count - 1];
        }

        // -------------------------------------------------------------------------------------------
        // Reading one patient
        // -------------------------------------------------------------------------------------------

        private static void Read(HospitalPatient patient, Pawn pawn, Map map)
        {
            patient.Pawn = pawn;
            patient.Map = map;
            patient.Animal = pawn.RaceProps != null && pawn.RaceProps.Animal;

            patient.Summary = UIGuard.Try("Hospital.Summary", () => PawnHealthSummary.For(pawn),
                default(PawnHealthSummary), null);

            patient.Health = UIGuard.Try("Hospital.Health",
                () => pawn.health != null && pawn.health.summaryHealth != null
                    ? Mathf.Clamp01(pawn.health.summaryHealth.SummaryHealthPercent)
                    : 1f, 1f, null);

            patient.Pain = UIGuard.Try("Hospital.Pain",
                () => pawn.health != null && pawn.health.hediffSet != null
                    ? pawn.health.hediffSet.PainTotal
                    : 0f, 0f, null);

            patient.Bleeding = UIGuard.Try("Hospital.Bleeding",
                () => pawn.health != null && pawn.health.hediffSet != null
                    ? pawn.health.hediffSet.BleedRateTotal
                    : 0f, 0f, null);

            patient.Bed = UIGuard.Try("Hospital.Bed", () => pawn.CurrentBed(), null, null);
            patient.InMedicalBed = patient.Bed != null && patient.Bed.Medical;

            CountBills(patient, pawn);

            patient.StandingOrders = UIGuard.Try("Hospital.OrderCount",
                () => MapComponent_StandingOrders.CountFor(pawn), 0, null);

            if (HospitalIntegrations.AnyHospitalMod)
            {
                patient.Visiting = HospitalIntegrations.IsVisitingPatient(pawn);

                if (patient.Visiting)
                {
                    patient.VisitStatus = HospitalIntegrations.StatusOf(pawn);
                    patient.VisitBill = HospitalIntegrations.BillOf(pawn);
                }
            }

            patient.Treatment = HospitalTreatment.For(patient);
            patient.Triage = TriageOf(patient);
        }

        /// <summary>
        /// Splits the bill stack into operations and doses, because they mean opposite things on this screen.
        ///
        /// An operation is a decision waiting on a surgeon and belongs in its own section; a dose is somebody
        /// being handed a pill, usually by a standing order, and is part of ordinary treatment. Counting them
        /// together would file every patient on painkillers as awaiting surgery.
        /// </summary>
        private static void CountBills(HospitalPatient patient, Pawn pawn)
        {
            UIGuard.Try("Hospital.Bills", () =>
            {
                BillStack stack = pawn.BillStack;

                if (stack == null || stack.Bills == null)
                    return;

                for (int i = 0; i < stack.Bills.Count; i++)
                {
                    Bill_Medical bill = stack.Bills[i] as Bill_Medical;

                    if (bill == null || bill.deleted)
                        continue;

                    if (HospitalSurgery.IsDose(bill.recipe))
                        patient.Doses++;
                    else
                        patient.Operations++;
                }
            }, null);
        }

        /// <summary>
        /// Which section this patient belongs in: the worst thing true about them.
        ///
        /// <b>A mental break is filed as critical, and that is a judgement rather than a reading.</b> It is not a
        /// medical problem at all and no doctor can help with it, but it is a drop-what-you-are-doing, and Aaron's
        /// rule puts anything the condition column calls unhealthy on this tab. Filing it under recovering would
        /// bury a purple BREAK badge at the bottom of the screen, which is the one place it does no good.
        /// </summary>
        private static HospitalTriage TriageOf(HospitalPatient patient)
        {
            HospitalTriage triage = Severity(patient);

            if (!patient.Animal)
                return triage;

            // An animal goes into its own section, but only if it would have been listed at all. Without this a
            // colony with forty chickens has a hospital tab that is forty rows of poultry, which is exactly the
            // failure the "who appears" rule exists to prevent -- and the reason it reads the human answer first
            // rather than having a rule of its own is that a sick chicken and a sick colonist are sick the same
            // way.
            return triage == HospitalTriage.Healthy ? HospitalTriage.Healthy : HospitalTriage.Animals;
        }

        /// <summary>Which section this patient would be in if everybody were read the same way.</summary>
        private static HospitalTriage Severity(HospitalPatient patient)
        {
            switch (patient.Summary.State)
            {
                case Pawns.PawnHealthState.BleedingOut:
                case Pawns.PawnHealthState.Vacuum:
                case Pawns.PawnHealthState.Downed:
                case Pawns.PawnHealthState.SevereTemperature:
                case Pawns.PawnHealthState.LifeThreatening:
                case Pawns.PawnHealthState.MentalBreak:
                    return HospitalTriage.Critical;

                case Pawns.PawnHealthState.UrgentTending:
                case Pawns.PawnHealthState.NeedsTending:
                    return HospitalTriage.InTreatment;
            }

            // Something is being fought even though nothing is tendable this minute: a disease losing to immunity,
            // a tend that will run out, a temperature the pawn is standing in.
            if (patient.Treatment.Active)
                return HospitalTriage.InTreatment;

            if (patient.Operations > 0)
                return HospitalTriage.AwaitingSurgery;

            if (patient.Summary.State == Pawns.PawnHealthState.Recovering || patient.InMedicalBed
                                                                    || patient.Health < 0.999f
                                                                    || patient.Doses > 0)
                return HospitalTriage.Recovering;

            if (patient.Visiting)
                return HospitalTriage.Recovering;

            return HospitalTriage.Healthy;
        }

        // -------------------------------------------------------------------------------------------
        // Ordering
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Worst first inside every section, so the top of a section is the one to look at.
        ///
        /// Health rather than the condition label, because two people can share a label and be in very different
        /// amounts of trouble, and health is the number the column beside it is already showing.
        /// </summary>
        private static void Sort()
        {
            for (int s = 0; s < Built.Count; s++)
            {
                Built[s].Patients.Sort((a, b) =>
                {
                    int byHealth = a.Health.CompareTo(b.Health);

                    if (byHealth != 0)
                        return byHealth;

                    int byPain = b.Pain.CompareTo(a.Pain);

                    return byPain != 0 ? byPain : NameOf(a).CompareTo(NameOf(b));
                });
            }
        }

        private static string NameOf(HospitalPatient patient)
        {
            return UIGuard.Try("Hospital.Name",
                () => patient.Pawn != null ? patient.Pawn.LabelShortCap.ToString() : string.Empty, string.Empty,
                null);
        }
    }
}
