using HarmonyLib;
using System.Collections.Generic;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    /// <summary>
    /// Which side of the ledger a notice falls on. This is the only thing that decides whether a
    /// card shows the biohazard icon over a red wash, the question mark over orange, or the
    /// caduceus over green.
    /// </summary>
    public enum PlantNoticeKind
    {
        /// <summary>The plant produces a hazard itself, e.g. venting gas as it grows.</summary>
        CreatesHazard,

        /// <summary>A hazard must already be present for the plant to grow at all.</summary>
        RequiresHazard,

        /// <summary>Something suggests hazardous behavior, but nobody has confirmed what it does.</summary>
        PossibleHazard,

        /// <summary>The plant confers something useful -- medicine, clean air, a meditation focus.</summary>
        CreatesBenefit,

        /// <summary>
        /// Explicitly no notice. Exists so a contributing mod can cancel a broad rule that catches
        /// one of its plants wrongly, without having to describe it as something it is not.
        /// </summary>
        None
    }

    /// <summary>
    /// Corrects what the card says about light. The stat row is derived from a plant's own
    /// <c>diesToLight</c> and <c>growMinGlow</c>, which is right for anything that leaves the work
    /// to vanilla -- but a mod that implements light damage in its own thingClass leaves those
    /// fields describing a plant that tolerates light when it does not.
    /// </summary>
    public enum PlantLightBehaviour
    {
        /// <summary>No override. Read the plant's own fields.</summary>
        Unspecified,

        /// <summary>Light harms it, whatever its fields claim.</summary>
        Deadly,

        /// <summary>Genuinely indifferent to light level.</summary>
        Any,

        /// <summary>Wants ordinary crop light.</summary>
        Normal
    }

    /// <summary>
    /// One row of the notice table.
    ///
    /// Deliberately NOT a Def. Every row -- including our own -- is read from a plain XML file by
    /// <see cref="PlantNoticeCacheLoader"/> rather than through RimWorld's def loader, so there is
    /// no custom def type for the game to resolve and nothing that can fail at def-load time.
    ///
    /// Exactly one match key should be set. They are tried most specific first by
    /// <see cref="PlantNotices.Resolve"/>, so a defName row can override a broad class or comp rule.
    /// </summary>
    public class PlantNoticeRow
    {
        /// <summary>Exact plant defName. Most specific.</summary>
        public string plant;

        /// <summary>Full or bare type name of the plant's thingClass.</summary>
        public string thingClass;

        /// <summary>Full or bare class name of a CompProperties the plant carries.</summary>
        public string compClass;

        /// <summary>defName of the harvested product, e.g. "MedicineHerbal".</summary>
        public string harvestedThing;

        public PlantNoticeKind kind = PlantNoticeKind.CreatesHazard;

        /// <summary>Card row text. Falls back to a generic phrase for the kind.</summary>
        public string cardLabel;

        /// <summary>Specifics, shown in the add-bill window's notice section.</summary>
        public string detail;

        /// <summary>Optional light correction. Applies even when kind is None.</summary>
        public PlantLightBehaviour light = PlantLightBehaviour.Unspecified;

        /// <summary>Tooltip for the light stat when <see cref="light"/> is set.</summary>
        public string lightDetail;

        /// <summary>Where this row came from, for error messages.</summary>
        public string source;
    }

    /// <summary>Lets a mod tag its own plant directly. Beats every table row.</summary>
    public class PlantHazardExtension : DefModExtension
    {
        public PlantNoticeKind kind = PlantNoticeKind.CreatesHazard;
        public string cardLabel;
        public string detail;
        public PlantLightBehaviour light = PlantLightBehaviour.Unspecified;
        public string lightDetail;
    }

    public struct PlantNoticeInfo
    {
        public readonly PlantNoticeKind Kind;
        public readonly string CardLabel;
        public readonly string Detail;
        public readonly PlantLightBehaviour Light;
        public readonly string LightDetail;

        public PlantNoticeInfo(PlantNoticeKind kind, string cardLabel, string detail,
            PlantLightBehaviour light, string lightDetail)
        {
            Kind = kind;
            CardLabel = cardLabel;
            Detail = detail;
            Light = light;
            LightDetail = lightDetail;
        }

        public bool IsBenefit => Kind == PlantNoticeKind.CreatesBenefit;

        public bool IsPossibleHazard => Kind == PlantNoticeKind.PossibleHazard;

        public string Label
        {
            get
            {
                if (!CardLabel.NullOrEmpty())
                    return CardLabel;
                switch (Kind)
                {
                    case PlantNoticeKind.RequiresHazard: return "Growth Requires Hazard Present";
                    case PlantNoticeKind.PossibleHazard: return "Possible Hazard";
                    case PlantNoticeKind.CreatesBenefit: return "Provides Benefit";
                    default: return "Creates Hazard";
                }
            }
        }
    }

    /// <summary>
    /// Notice lookup. Tables are built once on first use and every answer is memoised, including
    /// the negative ones, because this is queried per plant per frame while the window is open.
    /// </summary>
    public static class PlantNotices
    {
        private static Dictionary<string, PlantNoticeRow> byPlantDefName;
        private static Dictionary<string, PlantNoticeRow> byThingClass;
        private static Dictionary<string, PlantNoticeRow> byCompClass;
        private static Dictionary<string, PlantNoticeRow> byHarvest;
        private static Dictionary<ThingDef, PlantNoticeInfo?> resolved;

        /// <summary>The notice to draw, or null when there is none to draw.</summary>
        public static PlantNoticeInfo? For(ThingDef plant)
        {
            PlantNoticeInfo? info = Lookup(plant);
            if (!info.HasValue || info.Value.Kind == PlantNoticeKind.None)
                return null;
            return info;
        }

        /// <summary>
        /// The light correction for a plant, if a row supplies one. Kept separate from
        /// <see cref="For"/> because a row may carry nothing but a light correction, with kind None
        /// -- that is a data fix, not a notice, and must not put a hazard stripe on the card.
        /// </summary>
        public static PlantLightBehaviour LightFor(ThingDef plant, out string detail)
        {
            PlantNoticeInfo? info = Lookup(plant);
            detail = info?.LightDetail;
            return info?.Light ?? PlantLightBehaviour.Unspecified;
        }

        private static PlantNoticeInfo? Lookup(ThingDef plant)
        {
            if (plant == null)
                return null;

            EnsureTables();

            if (resolved.TryGetValue(plant, out PlantNoticeInfo? cached))
                return cached;

            PlantNoticeInfo? info = Resolve(plant);
            resolved[plant] = info;
            return info;
        }

        private static PlantNoticeInfo? Resolve(ThingDef plant)
        {
            PlantHazardExtension extension = plant.GetModExtension<PlantHazardExtension>();
            if (extension != null)
            {
                return new PlantNoticeInfo(extension.kind, extension.cardLabel, extension.detail,
                    extension.light, extension.lightDetail);
            }

            // Most specific first, so a defName row can override a broad thingClass or comp rule.
            if (byPlantDefName.TryGetValue(plant.defName, out PlantNoticeRow row))
                return Make(row);

            if (TryMatchType(byThingClass, plant.thingClass, out row))
                return Make(row);

            // Loops the plant's own comps (rarely more than three), not the table.
            if (plant.comps != null)
            {
                foreach (CompProperties comp in plant.comps)
                {
                    if (comp != null && TryMatchType(byCompClass, comp.GetType(), out row))
                        return Make(row);
                }
            }

            string harvest = plant.plant?.harvestedThingDef?.defName;
            if (harvest != null && byHarvest.TryGetValue(harvest, out row))
                return Make(row);

            return null;
        }

        /// <summary>
        /// Matches a type against a table keyed by whatever the XML author wrote. Both the
        /// namespace-qualified name and the bare name are tried, so a row may say either
        /// "RimWorld.Plant_Boomshroom" or "Plant_Boomshroom". Bare names also let one row cover the
        /// same class name across different mod namespaces, which is deliberate.
        /// </summary>
        private static bool TryMatchType(Dictionary<string, PlantNoticeRow> table, System.Type type,
            out PlantNoticeRow row)
        {
            row = null;
            if (type == null)
                return false;
            if (type.FullName != null && table.TryGetValue(type.FullName, out row))
                return true;
            return table.TryGetValue(type.Name, out row);
        }

        /// <summary>
        /// Builds the info for a matched row. Rows with kind None are kept rather than discarded:
        /// they still cancel broader rules, and they may carry a light correction. <see cref="For"/>
        /// is what decides a None row draws no notice.
        /// </summary>
        private static PlantNoticeInfo? Make(PlantNoticeRow row)
        {
            return new PlantNoticeInfo(row.kind, row.cardLabel, row.detail, row.light, row.lightDetail);
        }

        internal static void EnsureTables()
        {
            if (byPlantDefName != null)
                return;

            byPlantDefName = new Dictionary<string, PlantNoticeRow>();
            byThingClass = new Dictionary<string, PlantNoticeRow>();
            byCompClass = new Dictionary<string, PlantNoticeRow>();
            byHarvest = new Dictionary<string, PlantNoticeRow>();
            resolved = new Dictionary<ThingDef, PlantNoticeInfo?>();

            // Rows arrive with ours first, so a contributing mod's row always replaces ours when
            // they share a match key -- whatever order the mods themselves load in.
            foreach (PlantNoticeRow row in PlantNoticeCacheLoader.LoadAllRows())
                Register(row);
        }

        private static void Register(PlantNoticeRow row)
        {
            if (!row.plant.NullOrEmpty()) byPlantDefName[row.plant] = row;
            if (!row.thingClass.NullOrEmpty()) byThingClass[row.thingClass] = row;
            if (!row.compClass.NullOrEmpty()) byCompClass[row.compClass] = row;
            if (!row.harvestedThing.NullOrEmpty()) byHarvest[row.harvestedThing] = row;
        }
    }
}
