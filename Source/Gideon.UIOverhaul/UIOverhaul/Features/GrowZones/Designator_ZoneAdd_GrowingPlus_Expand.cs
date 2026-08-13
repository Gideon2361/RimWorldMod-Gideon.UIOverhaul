using Gideon.UIFramework.Helpers;
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

        /// <summary>
        /// Guarded because this is asked once per cell under the cursor for as long as a zone is being dragged out,
        /// and because the fertility floor is read from defs another mod could have removed.
        ///
        /// Refusing the cell is the fallback. It is the conservative answer: the player sees they cannot draw there
        /// and can look at the log, where drawing a zone that should have been refused would leave a zone in a place
        /// nothing supports.
        /// </summary>
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            return UIGuard.Try("GrowZones.ExpandCanDesignate", () => CanDesignateCellInner(c),
                (AcceptanceReport) false,
                "The zone cannot be expanded over the cell under the cursor.");
        }

        private AcceptanceReport CanDesignateCellInner(IntVec3 c)
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

        /// <summary>
        /// <b>Deliberately not guarded, and deliberately not null-checked.</b> The caller registers whatever comes
        /// back, so handing it a null would only move the failure a few frames deeper into vanilla, where it reads as
        /// a RimWorld bug rather than one of ours.
        ///
        /// Find.CurrentMap cannot be null here in any case: Designator_ZoneAdd dereferences it itself, before this is
        /// reached. If that ever stops being true the honest failure is the one thrown right here.
        /// </summary>
        protected override Zone MakeNewZone() => new Zone_GrowingPlus(Find.CurrentMap.zoneManager);
    }
}