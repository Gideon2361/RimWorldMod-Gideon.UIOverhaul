using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    /// <summary>
    /// Plant lookup and bill creation shared by the bills tab and the add-bill window.
    /// </summary>
    public static class GrowBillUtility
    {
        private static readonly List<IPlantToGrowSettable> TmpSettables = new List<IPlantToGrowSettable>();

        public static List<ThingDef> AvailablePlants(Zone_GrowingPlus zone)
        {
            List<ThingDef> plants = new List<ThingDef>();
            if (zone == null)
                return plants;

            TmpSettables.Clear();
            TmpSettables.Add(zone);

            foreach (ThingDef plantType in PlantUtility.ValidPlantTypesForGrowers(TmpSettables))
            {
                if (IsPlantAvailable(plantType, zone.Map))
                    plants.Add(plantType);
            }

            plants.SortBy(x => 0f - ListPriority(x), x => x.label);
            return plants;
        }

        public static bool IsPlantAvailable(ThingDef plantDef, Map map)
        {
            List<ResearchProjectDef> researchPrerequisites = plantDef.plant.sowResearchPrerequisites;
            if (researchPrerequisites != null)
            {
                for (int index = 0; index < researchPrerequisites.Count; ++index)
                {
                    if (!researchPrerequisites[index].IsFinished)
                        return false;
                }
            }
            return !plantDef.plant.mustBeWildToSow || map.Biome.AllWildPlants.Contains(plantDef);
        }

        public static float ListPriority(ThingDef plantDef)
        {
            if (plantDef.plant.IsTree)
                return 1f;

            switch (plantDef.plant.purpose)
            {
                case PlantPurpose.Food:
                    return 4f;
                case PlantPurpose.Health:
                    return 3f;
                case PlantPurpose.Beauty:
                    return 2f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// What the plant is grown for. Purpose is checked first because it is authored intent;
        /// anything left over that still yields something is treated as a materials crop, which is
        /// how cotton, devilstrand and trees end up classified.
        /// </summary>
        public static string PurposeLabel(ThingDef plantDef)
        {
            switch (plantDef.plant.purpose)
            {
                case PlantPurpose.Food:
                    return "Food";
                case PlantPurpose.Health:
                    return "Medicine";
                case PlantPurpose.Beauty:
                    return "Beauty";
            }

            if (plantDef.plant.IsTree || plantDef.plant.harvestedThingDef != null)
                return "Materials";

            return "Misc";
        }

        /// <summary>
        /// Adds a Grow Forever bill for <paramref name="plantDef"/>, replaying the vanilla tutor
        /// event and the roof / skill / cave-plant warnings the old float menu raised.
        /// </summary>
        public static void AddBill(Zone_GrowingPlus zone, ThingDef plantDef, string tutorTag)
        {
            string ep = $"{tutorTag}-{plantDef.defName}";
            if (!TutorSystem.AllowAction((EventPack) ep))
                return;

            zone.BillStack.AddBill(new Bill_Growing(plantDef));
            zone.UpdatePlantDefToGrow();

            if (plantDef.plant.interferesWithRoof)
            {
                foreach (IntVec3 cell in zone.Cells)
                {
                    if (!cell.Roofed(zone.Map))
                        continue;
                    Messages.Message("MessagePlantIncompatibleWithRoof".Translate(
                            (NamedArgument) Find.ActiveLanguageWorker.Pluralize(plantDef.LabelCap)),
                        MessageTypeDefOf.CautionInput, false);
                    break;
                }
            }

            PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.SetGrowingZonePlant, KnowledgeAmount.Total);
            WarnAsAppropriate(zone, plantDef);
            TutorSystem.Notify_Event((EventPack) ep);
        }

        private static void WarnAsAppropriate(Zone_GrowingPlus zone, ThingDef plantDef)
        {
            if (plantDef.plant.sowMinSkill > 0 && !AnyGrowerCanSow(zone, plantDef))
            {
                Find.WindowStack.Add(new Dialog_MessageBox("NoGrowerCanPlant".Translate(
                    (NamedArgument) plantDef.label, plantDef.plant.sowMinSkill).CapitalizeFirst()));
            }

            if (!plantDef.plant.cavePlant)
                return;

            foreach (IntVec3 cell in zone.Cells)
            {
                if (cell.Roofed(zone.Map) && zone.Map.glowGrid.GroundGlowAt(cell, true) <= 0.0)
                    continue;

                Messages.Message("MessageWarningCavePlantsExposedToLight".Translate(plantDef.LabelCap),
                    new TargetInfo(cell, zone.Map), MessageTypeDefOf.RejectInput);
                return;
            }
        }

        public static bool AnyGrowerCanSow(Zone_GrowingPlus zone, ThingDef plantDef)
        {
            foreach (Pawn pawn in zone.Map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.skills.GetSkill(SkillDefOf.Plants).Level >= plantDef.plant.sowMinSkill
                    && !pawn.Downed
                    && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Growing))
                    return true;
            }

            return ModsConfig.BiotechActive
                   && MechanitorUtility.AnyPlayerMechCanDoWork(WorkTypeDefOf.Growing, plantDef.plant.sowMinSkill, out Pawn _);
        }
    }
}
