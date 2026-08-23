using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// The beds, and putting somebody in one.
    ///
    /// <b>RimWorld has no way to reserve a medical bed for a patient, and this does not invent one.</b> Medical
    /// beds are claimed when they are needed, not owned, so "put him in that bed" can only honestly mean "somebody
    /// carry him to that bed now". That is exactly the job the right-click Rescue option builds, so this builds
    /// the same one: <c>JobDefOf.Rescue</c> with the patient as the target and the chosen bed as the destination.
    ///
    /// <b>The rescuer is the nearest colonist who can do it,</b> because asking which of eleven people should walk
    /// over is a question with an obvious answer and a float menu is a poor way to give it. The game's own
    /// <c>HealthAIUtility.CanRescueNow</c> decides who can, forced, which is the same test the right-click menu
    /// runs.
    /// </summary>
    internal static class HospitalBeds
    {
        /// <summary>Every bed on the map marked medical, nearest to the patient first.</summary>
        internal static void Medical(Map map, Pawn patient, List<Building_Bed> into)
        {
            if (into == null)
                return;

            into.Clear();

            if (map == null)
                return;

            UIGuard.Try("Hospital.MedicalBeds", () =>
            {
                List<Building> buildings = map.listerBuildings.allBuildingsColonist;

                if (buildings == null)
                    return;

                for (int i = 0; i < buildings.Count; i++)
                {
                    Building_Bed bed = buildings[i] as Building_Bed;

                    if (bed == null || !bed.Medical || bed.Destroyed)
                        continue;

                    if (patient != null && !RestUtility.CanUseBedEver(patient, bed.def))
                        continue;

                    into.Add(bed);
                }

                if (patient == null || !patient.Spawned)
                    return;

                IntVec3 from = patient.Position;

                into.SortBy(bed => bed.Position.DistanceToSquared(from));
            }, null);
        }

        /// <summary>How many medical beds there are on this map, and how many have somebody in them.</summary>
        internal static void Count(Map map, out int occupied, out int total)
        {
            int foundOccupied = 0;
            int foundTotal = 0;

            UIGuard.Try("Hospital.CountBeds", () =>
            {
                List<Building> buildings = map != null ? map.listerBuildings.allBuildingsColonist : null;

                if (buildings == null)
                    return;

                for (int i = 0; i < buildings.Count; i++)
                {
                    Building_Bed bed = buildings[i] as Building_Bed;

                    if (bed == null || !bed.Medical)
                        continue;

                    foundTotal++;

                    if (bed.AnyOccupants)
                        foundOccupied++;
                }
            }, null);

            occupied = foundOccupied;
            total = foundTotal;
        }

        /// <summary>
        /// Sends somebody to carry this patient to this bed.
        ///
        /// <b>The message on failure is the point of the return value.</b> "Nobody can reach them" is a fact the
        /// player has to know, and a control that silently does nothing when clicked is the worst possible answer
        /// to a colonist bleeding on the floor.
        /// </summary>
        internal static void Assign(Pawn patient, Building_Bed bed)
        {
            UIGuard.Try("Hospital.AssignBed", () =>
            {
                Pawn rescuer = Rescuer(patient);

                if (rescuer == null)
                {
                    Messages.Message(
                        "Nobody is free and able to carry " + patient.LabelShortCap + " to that bed.", patient,
                        MessageTypeDefOf.RejectInput, false);

                    return;
                }

                Job job = JobMaker.MakeJob(JobDefOf.Rescue, patient, bed);

                job.count = 1;

                rescuer.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                HospitalRoster.Invalidate();
            }, "The patient could not be sent to that bed. Right clicking them on the map with a colonist "
               + "selected still works.");
        }

        /// <summary>The nearest colonist who could pick this patient up right now.</summary>
        private static Pawn Rescuer(Pawn patient)
        {
            if (patient == null || patient.Map == null)
                return null;

            List<Pawn> colonists = patient.Map.mapPawns.FreeColonistsSpawned;

            if (colonists == null)
                return null;

            Pawn best = null;
            float nearest = float.MaxValue;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];

                if (pawn == null || pawn == patient || pawn.Downed || pawn.Dead)
                    continue;

                if (!HealthAIUtility.CanRescueNow(pawn, patient, true))
                    continue;

                float distance = pawn.Position.DistanceToSquared(patient.Position);

                if (best != null && distance >= nearest)
                    continue;

                best = pawn;
                nearest = distance;
            }

            return best;
        }
    }
}
