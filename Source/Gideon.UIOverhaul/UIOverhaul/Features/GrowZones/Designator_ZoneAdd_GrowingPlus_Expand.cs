using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    public class DesignatorZoneAddGrowingPlusExpand : Designator_ZoneAdd
    {
        protected override string NewZoneLabel => "GrowingZone".Translate();

        public DesignatorZoneAddGrowingPlusExpand()
        {
            zoneTypeToPlace = typeof (Zone_GrowingPlus);
            defaultLabel = "DesignatorZoneExpand".Translate();
            hotKey = KeyBindingDefOf.Misc6;
            defaultDesc = "DesignatorGrowingZoneDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Growing");
            tutorTag = "ZoneAdd_Growing";
            soundSucceeded = SoundDefOf.Designate_ZoneAdd_Growing;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            // Checked before the base call, which would otherwise reject on fog, the map-edge
            // buffer or an overlapping thing -- all of which this setting is meant to bypass.
            if (GrowZonesFeature.Settings != null && GrowZonesFeature.Settings.allowZonesAnywhere)
            {
                AcceptanceReport? anywhere = GzpZonePlacement.CanDesignateAnywhere(this, c);
                if (anywhere.HasValue)
                    return anywhere.Value;
            }

            // base here is Designator_ZoneAdd: bounds, conflicting zone, cell zoneability.
            if (!base.CanDesignateCell(c).Accepted)
                return false;

            float minimumFertility = ModsConfig.BiotechActive ? 0.5f : ThingDefOf.Plant_Potato.plant.fertilityMin;
            if (ModsConfig.IdeologyActive && BuildCopyCommandUtility.FindAllowedDesignator( TerrainDefOf.FungalGravel) != null)
                minimumFertility = Mathf.Min(minimumFertility, ThingDefOf.Plant_Nutrifungus.plant.fertilityMin);
            return !(c.GetFertility(Map) < (double) minimumFertility);
        }

        protected override Zone MakeNewZone() => new Zone_GrowingPlus(Find.CurrentMap.zoneManager);
    }
}